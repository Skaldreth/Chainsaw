// Command chainsaw-tunnel is Chainsaw's own WireGuard helper. It runs WireGuard in user mode (wireguard-go with
// gVisor's network stack, so no network adapter and no administrator rights are needed) and offers the tunnel to
// Chainsaw as a SOCKS5 proxy on 127.0.0.1 only, guarded by a user name and password Chainsaw chose for this tunnel.
//
// The protocol with Chainsaw, on the standard streams:
//
//   - stdin, first line: one JSON object {"configuration": "<the WireGuard file>", "username": "...",
//     "password": "..."}. The configuration comes from Chainsaw's vault and is never written to disk or to a log.
//   - stdout, first line: {"port": N} when the tunnel is up (the peer has answered a handshake), or
//     {"error": "<code>", "message": "..."} when it could not be started; the helper then exits.
//   - stdout, later: {"stopped": "<code>"} when the tunnel falls; the helper then exits.
//   - stdin closed: the helper stops. When Chainsaw ends, in any way, its end of the pipe closes, so the helper
//     never outlives it.
//
// stderr is never written to: the helper logs nothing.
package main

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"io"
	"net"
	"net/netip"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"

	"golang.zx2c4.com/wireguard/conn"
	"golang.zx2c4.com/wireguard/device"
	"golang.zx2c4.com/wireguard/tun/netstack"
)

// maxStartBytes bounds the first line on stdin; a WireGuard file is a few hundred bytes.
const maxStartBytes = 64 * 1024

// maxCredentialBytes is the longest user name or password SOCKS5 can carry (RFC 1929).
const maxCredentialBytes = 255

// handshakeWait is how long the helper waits for the peer to answer the first handshake.
const handshakeWait = 15 * time.Second

// staleHandshake is how old the latest handshake may be before the tunnel counts as fallen. WireGuard rekeys at
// least every two minutes while traffic flows, and the helper always keeps the tunnel alive with keepalives
// (longestKeepalive), so a working tunnel never comes near it; 180 seconds is WireGuard's own reject-after time.
const staleHandshake = 180*time.Second + 30*time.Second

// healthInterval is how often the helper looks at the latest handshake.
const healthInterval = 5 * time.Second

// Exit and error codes, shared with Chainsaw (WireGuardConnector in the Network module).
const (
	codeStart         = "start"         // the first line on stdin is missing or wrong
	codeConfiguration = "configuration" // the WireGuard file is refused
	codeEndpoint      = "endpoint"      // the server's name cannot be resolved
	codeDevice        = "device"        // the tunnel cannot be created
	codeHandshake     = "handshake"     // the server did not answer
	codeListen        = "listen"        // the proxy cannot listen on 127.0.0.1
	codeLost          = "lost"          // the tunnel fell after it was up
)

// startRequest is the first line on stdin.
type startRequest struct {
	// Configuration is the WireGuard file.
	Configuration string `json:"configuration"`
	// Username and Password are the credentials the proxy demands.
	Username string `json:"username"`
	Password string `json:"password"`
}

// reporter writes the helper's lines on stdout, one at a time.
type reporter struct {
	mu  sync.Mutex
	out io.Writer
}

// send writes one JSON line.
func (r *reporter) send(message map[string]any) {
	r.mu.Lock()
	defer r.mu.Unlock()
	line, _ := json.Marshal(message)
	_, _ = r.out.Write(append(line, '\n'))
}

// fail reports why the tunnel could not be started. The message never holds a value of the configuration.
func (r *reporter) fail(code string, err error) {
	message := map[string]any{"error": code}
	var refused *configError
	if errors.As(err, &refused) {
		message["message"] = refused.Error()
	}
	r.send(message)
}

func main() {
	os.Exit(run(os.Stdin, os.Stdout))
}

// run starts the tunnel and serves until stdin closes or the tunnel falls. It returns the exit code.
func run(stdin io.Reader, stdout io.Writer) int {
	out := &reporter{out: stdout}
	input := bufio.NewReaderSize(stdin, 4096)
	start, err := readStart(input)
	if err != nil {
		out.fail(codeStart, err)
		return 2
	}

	cfg, err := parseConfig(start.Configuration)
	start.Configuration = ""
	if err != nil {
		out.fail(codeConfiguration, err)
		return 2
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	go func() {
		// Closed stdin is the signal to stop; anything after the first line is ignored.
		_, _ = io.Copy(io.Discard, input)
		cancel()
	}()

	endpoint, err := resolveEndpoint(ctx, cfg.peer.endpoint)
	if err != nil {
		out.fail(codeEndpoint, err)
		return 3
	}

	tun, tnet, err := netstack.CreateNetTUN(cfg.addresses, cfg.dns, cfg.mtu)
	if err != nil {
		out.fail(codeDevice, err)
		return 3
	}
	dev := device.NewDevice(tun, conn.NewDefaultBind(), device.NewLogger(device.LogLevelSilent, ""))
	defer dev.Close()
	if err := dev.IpcSet(cfg.uapi(endpoint)); err != nil {
		out.fail(codeDevice, err)
		return 3
	}
	if err := dev.Up(); err != nil {
		out.fail(codeDevice, err)
		return 3
	}

	if !waitForHandshake(ctx, dev) {
		if ctx.Err() == nil {
			out.fail(codeHandshake, nil)
		}
		return 3
	}

	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		out.fail(codeListen, err)
		return 3
	}
	defer listener.Close()

	proxy := newSocksServer(start.Username, start.Password, (&dialer{tnet: tnet}).dial)
	go func() { _ = proxy.serve(ctx, listener) }()
	out.send(map[string]any{"port": listener.Addr().(*net.TCPAddr).Port})

	if watchHandshakes(ctx, dev) {
		out.send(map[string]any{"stopped": codeLost})
		return 4
	}
	return 0
}

// readStart reads and checks the first line on stdin.
func readStart(input *bufio.Reader) (*startRequest, error) {
	var line []byte
	for {
		part, more, err := input.ReadLine()
		if err != nil {
			return nil, errors.New("no start line")
		}
		line = append(line, part...)
		if len(line) > maxStartBytes {
			return nil, errors.New("the start line is too long")
		}
		if !more {
			break
		}
	}

	var start startRequest
	if err := json.Unmarshal(line, &start); err != nil {
		return nil, errors.New("the start line is not JSON")
	}
	for _, credential := range []string{start.Username, start.Password} {
		if len(credential) == 0 || len(credential) > maxCredentialBytes {
			return nil, errors.New("a credential is empty or too long")
		}
	}
	return &start, nil
}

// resolveEndpoint gives the server's address. A name is resolved with the computer's resolver: it is the one
// lookup that cannot go through the tunnel, since the tunnel does not exist yet. It shows only the exit's own
// server name, as any VPN client does; IPv4 is preferred, as most exits' servers answer on it.
func resolveEndpoint(ctx context.Context, endpoint string) (netip.AddrPort, error) {
	host, portText, err := net.SplitHostPort(endpoint)
	if err != nil {
		return netip.AddrPort{}, err
	}
	port, err := strconv.ParseUint(portText, 10, 16)
	if err != nil {
		return netip.AddrPort{}, err
	}
	if address, err := netip.ParseAddr(host); err == nil {
		return netip.AddrPortFrom(address.Unmap(), uint16(port)), nil
	}

	lookup, cancel := context.WithTimeout(ctx, 10*time.Second)
	defer cancel()
	addresses, err := net.DefaultResolver.LookupNetIP(lookup, "ip", host)
	if err != nil || len(addresses) == 0 {
		return netip.AddrPort{}, errors.New("the endpoint's name cannot be resolved")
	}
	chosen := addresses[0]
	for _, address := range addresses {
		if address.Unmap().Is4() {
			chosen = address
			break
		}
	}
	return netip.AddrPortFrom(chosen.Unmap(), uint16(port)), nil
}

// waitForHandshake waits until the peer has answered a handshake, so that Chainsaw is only told the tunnel is up
// when it is. It gives false when the wait ends first.
func waitForHandshake(ctx context.Context, dev *device.Device) bool {
	deadline := time.Now().Add(handshakeWait)
	for time.Now().Before(deadline) {
		if age, ok := handshakeAge(dev); ok && age < staleHandshake {
			return true
		}
		select {
		case <-ctx.Done():
			return false
		case <-time.After(100 * time.Millisecond):
		}
	}
	return false
}

// watchHandshakes watches the tunnel until it falls (true) or the helper is told to stop (false).
func watchHandshakes(ctx context.Context, dev *device.Device) bool {
	ticker := time.NewTicker(healthInterval)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return false
		case <-dev.Wait():
			return true
		case <-ticker.C:
			if age, ok := handshakeAge(dev); !ok || age >= staleHandshake {
				return true
			}
		}
	}
}

// handshakeAge gives how long ago the peer's latest handshake was, from the device's own report.
func handshakeAge(dev *device.Device) (time.Duration, bool) {
	report, err := dev.IpcGet()
	if err != nil {
		return 0, false
	}
	return latestHandshake(report, time.Now())
}

// latestHandshake reads last_handshake_time_sec from the device's report and gives its age; false when no
// handshake has been made.
func latestHandshake(report string, now time.Time) (time.Duration, bool) {
	for _, line := range strings.Split(report, "\n") {
		value, found := strings.CutPrefix(line, "last_handshake_time_sec=")
		if !found {
			continue
		}
		seconds, err := strconv.ParseInt(value, 10, 64)
		if err != nil || seconds == 0 {
			return 0, false
		}
		return now.Sub(time.Unix(seconds, 0)), true
	}
	return 0, false
}
