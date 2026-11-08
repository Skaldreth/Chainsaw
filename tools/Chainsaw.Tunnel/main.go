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

// staleHandshake is how long the helper may go without seeing a new handshake before the tunnel counts as fallen.
// The helper always sends keepalives (longestKeepalive), so a working tunnel makes a new handshake at the latest
// when its keys expire after 180 seconds (WireGuard's reject-after time) and the next keepalive goes out: about 205
// seconds when the server began the session, plus a few retries of 5 seconds. 270 seconds leaves room for that.
const staleHandshake = 270 * time.Second

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
	// The listener is never closed: the port is released only when the process ends, so it is never free while
	// Chainsaw may still count the helper as running (see listenLoopback).
	os.Exit(run(os.Stdin, os.Stdout, func(net.Listener) {}))
}

// run starts the tunnel and serves until stdin closes or the tunnel falls. It returns the exit code. The proxy's
// listener is handed to keep instead of being closed, since closing it before the process ends would free the port
// while the helper still lives; only the tests close it.
func run(stdin io.Reader, stdout io.Writer, keep func(net.Listener)) int {
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

	endpoints, err := resolveEndpoints(ctx, cfg.peer.endpoint)
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
	if err := dev.IpcSet(cfg.uapi(endpoints[0])); err != nil {
		out.fail(codeDevice, err)
		return 3
	}
	if err := dev.Up(); err != nil {
		out.fail(codeDevice, err)
		return 3
	}

	if !handshakeOnAny(ctx, dev, cfg, endpoints) {
		if ctx.Err() == nil {
			out.fail(codeHandshake, nil)
		}
		return 3
	}

	listener, err := listenLoopback(ctx)
	if err != nil {
		out.fail(codeListen, err)
		return 3
	}
	defer keep(listener)

	proxy := newSocksServer(start.Username, start.Password, (&dialer{tnet: tnet}).dial)
	serveFailed := make(chan struct{})
	go func() {
		// A proxy that can no longer accept is a fallen tunnel, never a silent one.
		if proxy.serve(ctx, listener) != nil {
			close(serveFailed)
		}
	}()
	out.send(map[string]any{"port": listener.Addr().(*net.TCPAddr).Port})

	if watchHandshakes(ctx, dev, serveFailed) {
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

// resolveEndpoints gives the server's addresses to try, in order. A name is resolved with the computer's resolver: it
// is the one lookup that cannot go through the tunnel, since the tunnel does not exist yet. It shows only the exit's
// own server name, as any VPN client does. The first IPv4 address comes first, as most exits' servers answer on it,
// and then the first IPv6 address, so that a server that only answers on IPv6 is still reached.
func resolveEndpoints(ctx context.Context, endpoint string) ([]netip.AddrPort, error) {
	host, portText, err := net.SplitHostPort(endpoint)
	if err != nil {
		return nil, err
	}
	port, err := strconv.ParseUint(portText, 10, 16)
	if err != nil {
		return nil, err
	}
	if address, err := netip.ParseAddr(host); err == nil {
		return []netip.AddrPort{netip.AddrPortFrom(address.Unmap(), uint16(port))}, nil
	}

	lookup, cancel := context.WithTimeout(ctx, 10*time.Second)
	defer cancel()
	addresses, err := net.DefaultResolver.LookupNetIP(lookup, "ip", host)
	if err != nil || len(addresses) == 0 {
		return nil, errors.New("the endpoint's name cannot be resolved")
	}
	return orderEndpoints(addresses, uint16(port)), nil
}

// orderEndpoints keeps the first IPv4 and the first IPv6 address of a name, IPv4 first.
func orderEndpoints(addresses []netip.Addr, port uint16) []netip.AddrPort {
	var four, six []netip.AddrPort
	for _, address := range addresses {
		address = address.Unmap()
		switch {
		case address.Is4() && len(four) == 0:
			four = append(four, netip.AddrPortFrom(address, port))
		case address.Is6() && len(six) == 0:
			six = append(six, netip.AddrPortFrom(address, port))
		}
	}
	return append(four, six...)
}

// handshakeOnAny tries the endpoints in turn until the peer answers a handshake, sharing handshakeWait between them,
// so that Chainsaw's own limit for the start still holds. It gives false when none answered.
func handshakeOnAny(ctx context.Context, dev *device.Device, cfg *config, endpoints []netip.AddrPort) bool {
	each := handshakeWait / time.Duration(len(endpoints))
	for index, endpoint := range endpoints {
		if index > 0 {
			if err := dev.IpcSet(cfg.uapiEndpoint(endpoint)); err != nil {
				return false
			}
		}
		if waitForHandshake(ctx, dev, each) {
			return true
		}
		if ctx.Err() != nil {
			return false
		}
	}
	return false
}

// waitForHandshake waits until the peer has answered a handshake, so that Chainsaw is only told the tunnel is up
// when it is. It gives false when the wait ends first.
func waitForHandshake(ctx context.Context, dev *device.Device, wait time.Duration) bool {
	deadline := time.Now().Add(wait)
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

// watchHandshakes watches the tunnel until it falls (true) or the helper is told to stop (false). The tunnel falls
// when the device closes, the proxy stops accepting, or no new handshake has been seen for staleHandshake.
func watchHandshakes(ctx context.Context, dev *device.Device, serveFailed <-chan struct{}) bool {
	ticker := time.NewTicker(healthInterval)
	defer ticker.Stop()
	watch := newHandshakeWatch(time.Now())
	for {
		select {
		case <-ctx.Done():
			return false
		case <-dev.Wait():
			return true
		case <-serveFailed:
			return true
		case <-ticker.C:
			report, err := dev.IpcGet()
			if err != nil || watch.stale(report, time.Now()) {
				return true
			}
		}
	}
}

// handshakeWatch tells whether the peer's handshakes have stopped. It measures with the helper's own monotonic
// clock from when a new handshake was last seen, not with the wall clock of the device's report, so that a jump of
// the computer's clock does not count as a fallen tunnel.
type handshakeWatch struct {
	// lastValue is last_handshake_time_sec as last read.
	lastValue int64
	// lastChange is when lastValue last changed, on the monotonic clock.
	lastChange time.Time
}

// newHandshakeWatch starts watching just after the first handshake was seen.
func newHandshakeWatch(now time.Time) *handshakeWatch {
	return &handshakeWatch{lastValue: -1, lastChange: now}
}

// stale reads the device's report and tells whether no new handshake has come for staleHandshake.
func (w *handshakeWatch) stale(report string, now time.Time) bool {
	if value, ok := handshakeSeconds(report); ok && value != w.lastValue {
		w.lastValue = value
		w.lastChange = now
	}
	return now.Sub(w.lastChange) >= staleHandshake
}

// handshakeAge gives how long ago the peer's latest handshake was, from the device's own report.
func handshakeAge(dev *device.Device) (time.Duration, bool) {
	report, err := dev.IpcGet()
	if err != nil {
		return 0, false
	}
	seconds, ok := handshakeSeconds(report)
	if !ok {
		return 0, false
	}
	return time.Since(time.Unix(seconds, 0)), true
}

// handshakeSeconds reads last_handshake_time_sec from the device's report; false when no handshake has been made.
func handshakeSeconds(report string) (int64, bool) {
	for _, line := range strings.Split(report, "\n") {
		value, found := strings.CutPrefix(line, "last_handshake_time_sec=")
		if !found {
			continue
		}
		seconds, err := strconv.ParseInt(value, 10, 64)
		if err != nil || seconds == 0 {
			return 0, false
		}
		return seconds, true
	}
	return 0, false
}
