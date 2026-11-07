package main

import (
	"bufio"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/netip"
	"strings"
	"testing"
	"time"

	"golang.org/x/net/dns/dnsmessage"
	"golang.zx2c4.com/wireguard/conn"
	"golang.zx2c4.com/wireguard/device"
	"golang.zx2c4.com/wireguard/tun/netstack"
)

// startLine builds the first line on stdin.
func startLine(t *testing.T, configuration string) string {
	t.Helper()
	line, err := json.Marshal(startRequest{Configuration: configuration, Username: "user", Password: "secret"})
	if err != nil {
		t.Fatal(err)
	}
	return string(line) + "\n"
}

// closeListener closes the proxy's listener when a test's helper ends; the real helper leaves it to the process.
func closeListener(listener net.Listener) { _ = listener.Close() }

// runWith runs the helper with a fixed stdin and gives its exit code and stdout.
func runWith(stdin string) (int, string) {
	var out strings.Builder
	code := run(strings.NewReader(stdin), &out, closeListener)
	return code, out.String()
}

func TestReadStartDemandsBothCredentials(t *testing.T) {
	for name, line := range map[string]string{
		"empty":       "\n",
		"not JSON":    "configuration\n",
		"no password": `{"configuration":"x","username":"user"}` + "\n",
		"too long":    `{"configuration":"x","username":"` + strings.Repeat("u", 256) + `","password":"p"}` + "\n",
		"huge":        strings.Repeat("x", maxStartBytes+10) + "\n",
	} {
		if _, err := readStart(bufio.NewReader(strings.NewReader(line))); err == nil {
			t.Errorf("%s was accepted", name)
		}
	}
}

func TestRunRefusesABadStartWithoutStarting(t *testing.T) {
	code, out := runWith("not json\n")

	if code == 0 || strings.TrimSpace(out) != `{"error":"start"}` {
		t.Fatalf("code %d, out %q", code, out)
	}
}

func TestRunRefusesABadConfigurationAndNeverRepeatsIt(t *testing.T) {
	file, private, _ := validFile(t)
	broken := strings.Replace(file, "DNS = 10.64.0.1, example.internal\n", "", 1)

	code, out := runWith(startLine(t, broken))

	if code == 0 || !strings.Contains(out, `"error":"configuration"`) {
		t.Fatalf("code %d, out %q", code, out)
	}
	if strings.Contains(out, private) || strings.Contains(out, "secret") {
		t.Fatal("the configuration or a credential reached stdout")
	}
}

func TestHandshakeSecondsReadsTheDeviceReport(t *testing.T) {
	seconds, ok := handshakeSeconds("public_key=ab\nlast_handshake_time_sec=999990\nlast_handshake_time_nsec=0\n")
	if !ok || seconds != 999990 {
		t.Errorf("seconds %d, ok %v", seconds, ok)
	}
	if _, ok := handshakeSeconds("last_handshake_time_sec=0\n"); ok {
		t.Error("no handshake yet")
	}
	if _, ok := handshakeSeconds(""); ok {
		t.Error("no peer")
	}
}

func TestHandshakeWatchFallsOnlyWhenNoNewHandshakeComes(t *testing.T) {
	start := time.Unix(5_000, 0)
	watch := newHandshakeWatch(start)
	report := func(seconds int) string { return fmt.Sprintf("last_handshake_time_sec=%d\n", seconds) }

	if watch.stale(report(100), start.Add(time.Minute)) {
		t.Fatal("a fresh tunnel is not stale")
	}
	// The report's wall clock says the handshake is ancient, as after a clock jump: only new handshakes count.
	if watch.stale(report(200), start.Add(3*time.Minute)) {
		t.Fatal("a new handshake keeps the tunnel up")
	}
	if watch.stale(report(200), start.Add(3*time.Minute+staleHandshake-time.Second)) {
		t.Fatal("stale too early")
	}
	if !watch.stale(report(200), start.Add(3*time.Minute+staleHandshake)) {
		t.Fatal("no new handshake for staleHandshake must count as fallen")
	}
	if !newHandshakeWatch(start).stale("", start.Add(staleHandshake)) {
		t.Fatal("a device without a handshake must fall too")
	}
}

// sourceAddress is the address of the fake source inside the test's tunnel. It is global, as a real source's is,
// but it exists only in the peer's in-memory network stack: no packet leaves the computer.
var sourceAddress = netip.MustParseAddr("93.184.216.34")

// startPeer runs a WireGuard peer on 127.0.0.1 with an in-memory network stack that answers names (UDP 53) and
// echoes TCP on port 443 at sourceAddress. It gives the peer's public key and UDP port.
func startPeer(t *testing.T, helperPublic string) (public string, port int) {
	t.Helper()
	private, public := testKeyPair(t)
	tun, tnet, err := netstack.CreateNetTUN([]netip.Addr{sourceAddress}, nil, 1420)
	if err != nil {
		t.Fatal(err)
	}
	dev := device.NewDevice(tun, conn.NewDefaultBind(), device.NewLogger(device.LogLevelSilent, ""))
	t.Cleanup(dev.Close)

	free, err := net.ListenPacket("udp4", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	port = free.LocalAddr().(*net.UDPAddr).Port
	free.Close()

	uapi := fmt.Sprintf("private_key=%s\nlisten_port=%d\npublic_key=%s\nallowed_ip=10.9.0.2/32\n",
		hexKey(t, private), port, hexKey(t, helperPublic))
	if err := dev.IpcSet(uapi); err != nil {
		t.Fatal(err)
	}
	if err := dev.Up(); err != nil {
		t.Fatal(err)
	}

	serveEcho(t, tnet)
	serveNames(t, tnet, map[string]netip.Addr{
		"source.example.": sourceAddress,
		"rebind.example.": netip.MustParseAddr("127.0.0.1"),
	})
	return public, port
}

// hexKey turns a base64 key into the hex form of the device's configuration.
func hexKey(t *testing.T, key string) string {
	t.Helper()
	raw, err := base64.StdEncoding.DecodeString(key)
	if err != nil {
		t.Fatal(err)
	}
	return hex.EncodeToString(raw)
}

// serveEcho echoes every TCP connection on sourceAddress:443 inside the peer's stack.
func serveEcho(t *testing.T, tnet *netstack.Net) {
	t.Helper()
	listener, err := tnet.ListenTCPAddrPort(netip.AddrPortFrom(sourceAddress, 443))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { listener.Close() })
	go func() {
		for {
			c, err := listener.Accept()
			if err != nil {
				return
			}
			go func() {
				defer c.Close()
				_, _ = io.Copy(c, c)
			}()
		}
	}()
}

// serveNames answers A questions from a table on sourceAddress:53 inside the peer's stack.
func serveNames(t *testing.T, tnet *netstack.Net, names map[string]netip.Addr) {
	t.Helper()
	udp, err := tnet.ListenUDPAddrPort(netip.AddrPortFrom(sourceAddress, 53))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { udp.Close() })
	go func() {
		buffer := make([]byte, 1500)
		for {
			n, from, err := udp.ReadFrom(buffer)
			if err != nil {
				return
			}
			if answer := answerName(buffer[:n], names); answer != nil {
				_, _ = udp.WriteTo(answer, from)
			}
		}
	}()
}

// answerName builds the answer to one question.
func answerName(query []byte, names map[string]netip.Addr) []byte {
	var parser dnsmessage.Parser
	header, err := parser.Start(query)
	if err != nil {
		return nil
	}
	question, err := parser.Question()
	if err != nil {
		return nil
	}
	builder := dnsmessage.NewBuilder(nil, dnsmessage.Header{ID: header.ID, Response: true, Authoritative: true})
	_ = builder.StartQuestions()
	_ = builder.Question(question)
	_ = builder.StartAnswers()
	if address, ok := names[question.Name.String()]; ok && question.Type == dnsmessage.TypeA {
		resource := dnsmessage.ResourceHeader{Name: question.Name, Type: dnsmessage.TypeA, Class: dnsmessage.ClassINET}
		_ = builder.AResource(resource, dnsmessage.AResource{A: address.As4()})
	}
	answer, _ := builder.Finish()
	return answer
}

// TestTunnelThroughALocalPeer runs the whole helper against a WireGuard peer on 127.0.0.1 in the same process: the
// handshake, the proxy's port on 127.0.0.1, a name resolved by the peer's name server inside the tunnel, a relayed
// connection, a name that resolves to the computer refused, and the stop when stdin closes. Nothing leaves the
// computer.
func TestTunnelThroughALocalPeer(t *testing.T) {
	helperPrivate, helperPublic := testKeyPair(t)
	peerPublic, peerPort := startPeer(t, helperPublic)
	file := fmt.Sprintf("[Interface]\nPrivateKey = %s\nAddress = 10.9.0.2/32\nDNS = %s\n\n"+
		"[Peer]\nPublicKey = %s\nEndpoint = 127.0.0.1:%d\nAllowedIPs = 0.0.0.0/0\n",
		helperPrivate, sourceAddress, peerPublic, peerPort)

	stdinReader, stdinWriter := io.Pipe()
	stdoutReader, stdoutWriter := io.Pipe()
	exited := make(chan int, 1)
	kept := make(chan net.Listener, 1)
	go func() {
		exited <- run(stdinReader, stdoutWriter, func(listener net.Listener) { kept <- listener })
		stdoutWriter.Close()
	}()
	if _, err := stdinWriter.Write([]byte(startLine(t, file))); err != nil {
		t.Fatal(err)
	}

	lines := bufio.NewScanner(stdoutReader)
	if !lines.Scan() {
		t.Fatal("no first line")
	}
	var ready struct {
		Port  int    `json:"port"`
		Error string `json:"error"`
	}
	if err := json.Unmarshal(lines.Bytes(), &ready); err != nil || ready.Port == 0 {
		t.Fatalf("first line %q", lines.Text())
	}
	proxy := fmt.Sprintf("127.0.0.1:%d", ready.Port)

	client := connectTo(t, proxy)
	authenticate(t, client, "user", "secret")
	expect(t, client, []byte{1, 0})
	_, _ = client.Write(connectRequest("source.example", 443))
	expect(t, client, []byte{5, 0, 0, 1, 0, 0, 0, 0, 0, 0})
	_, _ = client.Write([]byte("through the tunnel"))
	expect(t, client, []byte("through the tunnel"))

	refused := connectTo(t, proxy)
	authenticate(t, refused, "user", "secret")
	expect(t, refused, []byte{1, 0})
	_, _ = refused.Write(connectRequest("rebind.example", 443))
	expect(t, refused, []byte{5, replyNotAllowed, 0, 1, 0, 0, 0, 0, 0, 0})

	stdinWriter.Close()
	select {
	case code := <-exited:
		if code != 0 {
			t.Errorf("exit code %d after stdin closed", code)
		}
		// The helper never frees its port itself; only the end of the process does.
		listener := <-kept
		defer listener.Close()
		if probe, err := net.Dial("tcp4", proxy); err != nil {
			t.Errorf("the port was freed before the process ended: %v", err)
		} else {
			probe.Close()
		}
	case <-time.After(10 * time.Second):
		t.Fatal("the helper did not stop when stdin closed")
	}
}
