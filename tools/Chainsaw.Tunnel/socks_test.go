package main

import (
	"bytes"
	"context"
	"errors"
	"io"
	"net"
	"strconv"
	"sync"
	"testing"
	"time"
)

// startTestProxy runs the proxy on 127.0.0.1 with a dial function and gives its address.
func startTestProxy(
	t *testing.T, dial func(context.Context, string, uint16) (net.Conn, error),
) string {
	t.Helper()
	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	t.Cleanup(func() {
		cancel()
		listener.Close()
	})
	go func() { _ = newSocksServer("user", "secret", dial).serve(ctx, listener) }()
	return listener.Addr().String()
}

// echoDial records the target and gives a connection that echoes what it gets.
type echoDial struct {
	mu      sync.Mutex
	targets []string
}

// dial records the target and starts an echo on the other end of a pipe.
func (e *echoDial) dial(_ context.Context, host string, port uint16) (net.Conn, error) {
	e.mu.Lock()
	e.targets = append(e.targets, net.JoinHostPort(host, strconv.Itoa(int(port))))
	e.mu.Unlock()
	client, server := net.Pipe()
	go func() {
		defer server.Close()
		_, _ = io.Copy(server, server)
	}()
	return client, nil
}

// connectTo opens a connection to the proxy with a deadline.
func connectTo(t *testing.T, address string) net.Conn {
	t.Helper()
	conn, err := net.Dial("tcp4", address)
	if err != nil {
		t.Fatal(err)
	}
	_ = conn.SetDeadline(time.Now().Add(5 * time.Second))
	t.Cleanup(func() { conn.Close() })
	return conn
}

// expect reads exactly len(want) bytes and compares them.
func expect(t *testing.T, conn net.Conn, want []byte) {
	t.Helper()
	got := make([]byte, len(want))
	if _, err := io.ReadFull(conn, got); err != nil {
		t.Fatalf("read: %v", err)
	}
	if !bytes.Equal(got, want) {
		t.Fatalf("got %v, want %v", got, want)
	}
}

// authenticate offers user name and password and sends the credentials.
func authenticate(t *testing.T, conn net.Conn, user, pass string) {
	t.Helper()
	_, _ = conn.Write([]byte{5, 1, 2})
	expect(t, conn, []byte{5, 2})
	message := append([]byte{1, byte(len(user))}, user...)
	message = append(message, byte(len(pass)))
	message = append(message, pass...)
	_, _ = conn.Write(message)
}

// connectRequest builds a CONNECT for a name.
func connectRequest(name string, port uint16) []byte {
	request := append([]byte{5, 1, 0, 3, byte(len(name))}, name...)
	return append(request, byte(port>>8), byte(port))
}

func TestProxyRefusesAClientWithoutCredentials(t *testing.T) {
	address := startTestProxy(t, (&echoDial{}).dial)
	conn := connectTo(t, address)

	_, _ = conn.Write([]byte{5, 1, 0})

	expect(t, conn, []byte{5, 0xFF})
}

func TestProxyRefusesTheWrongPassword(t *testing.T) {
	dials := &echoDial{}
	address := startTestProxy(t, dials.dial)
	conn := connectTo(t, address)

	authenticate(t, conn, "user", "guess")

	expect(t, conn, []byte{1, 1})
	if len(dials.targets) != 0 {
		t.Error("nothing may be dialled for a client that failed")
	}
}

func TestProxyRefusesTheWrongUserName(t *testing.T) {
	address := startTestProxy(t, (&echoDial{}).dial)
	conn := connectTo(t, address)

	authenticate(t, conn, "other", "secret")

	expect(t, conn, []byte{1, 1})
}

func TestProxyRelaysThroughTheTunnelAfterAuthentication(t *testing.T) {
	dials := &echoDial{}
	address := startTestProxy(t, dials.dial)
	conn := connectTo(t, address)

	authenticate(t, conn, "user", "secret")
	expect(t, conn, []byte{1, 0})
	_, _ = conn.Write(connectRequest("source.example", 443))
	expect(t, conn, []byte{5, 0, 0, 1, 0, 0, 0, 0, 0, 0})
	_, _ = conn.Write([]byte("hello"))
	expect(t, conn, []byte("hello"))

	if len(dials.targets) != 1 || dials.targets[0] != "source.example:443" {
		t.Errorf("targets %v", dials.targets)
	}
}

func TestProxyServesOnlyConnect(t *testing.T) {
	address := startTestProxy(t, (&echoDial{}).dial)
	conn := connectTo(t, address)
	authenticate(t, conn, "user", "secret")
	expect(t, conn, []byte{1, 0})

	request := connectRequest("source.example", 443)
	request[1] = 2 // BIND
	_, _ = conn.Write(request)

	expect(t, conn, []byte{5, replyCommand, 0, 1, 0, 0, 0, 0, 0, 0})
}

func TestProxySaysWhenTheTargetIsNotAllowed(t *testing.T) {
	refusing := func(context.Context, string, uint16) (net.Conn, error) { return nil, errLocalTarget }
	address := startTestProxy(t, refusing)
	conn := connectTo(t, address)
	authenticate(t, conn, "user", "secret")
	expect(t, conn, []byte{1, 0})

	_, _ = conn.Write([]byte{5, 1, 0, 1, 127, 0, 0, 1, 1, 187})

	expect(t, conn, []byte{5, replyNotAllowed, 0, 1, 0, 0, 0, 0, 0, 0})
}

func TestReadRequestReadsEveryAddressForm(t *testing.T) {
	cases := map[string]struct {
		request []byte
		host    string
	}{
		"IPv4": {[]byte{5, 1, 0, 1, 93, 184, 216, 34, 1, 187}, "93.184.216.34"},
		"IPv6": {append(append([]byte{5, 1, 0, 4}, net.ParseIP("2606:4700::1111")...), 1, 187), "2606:4700::1111"},
		"name": {connectRequest("source.example", 443), "source.example"},
	}
	for name, c := range cases {
		host, port, reply := readRequest(bytes.NewReader(c.request))
		if reply != replySucceeded || host != c.host || port != 443 {
			t.Errorf("%s: %q %d %d", name, host, port, reply)
		}
	}

	if _, _, reply := readRequest(bytes.NewReader([]byte{5, 1, 0, 9})); reply != replyAddressType {
		t.Errorf("an unknown address type gave %d", reply)
	}
	if _, _, reply := readRequest(bytes.NewReader([]byte{5, 1, 0, 1, 1, 2, 3, 4, 0, 0})); reply != replyNotAllowed {
		t.Errorf("port 0 gave %d", reply)
	}
}

// temporaryError is an accept failure that may pass.
type temporaryError struct{}

func (temporaryError) Error() string   { return "temporary" }
func (temporaryError) Temporary() bool { return true }
func (temporaryError) Timeout() bool   { return false }

// scriptedListener gives the accept results it is told, then fails for good.
type scriptedListener struct {
	results []error
	calls   int
}

func (l *scriptedListener) Accept() (net.Conn, error) {
	l.calls++
	if l.calls <= len(l.results) {
		return nil, l.results[l.calls-1]
	}
	return nil, errors.New("broken for good")
}

func (l *scriptedListener) Close() error   { return nil }
func (l *scriptedListener) Addr() net.Addr { return &net.TCPAddr{} }

func TestServeRetriesATemporaryFailureAndStopsOnAPermanentOne(t *testing.T) {
	listener := &scriptedListener{results: []error{temporaryError{}, temporaryError{}}}

	err := newSocksServer("user", "secret", (&echoDial{}).dial).serve(context.Background(), listener)

	if err == nil || listener.calls != 3 {
		t.Fatalf("err %v after %d accepts", err, listener.calls)
	}
}

func TestServeEndsQuietlyWhenTheListenerCloses(t *testing.T) {
	listener := &scriptedListener{results: []error{net.ErrClosed}}

	if err := newSocksServer("user", "secret", (&echoDial{}).dial).serve(context.Background(), listener); err != nil {
		t.Fatalf("err %v", err)
	}
}

func TestAConnectionBeyondTheLimitWaitsForAFreeSlot(t *testing.T) {
	address := startTestProxy(t, (&echoDial{}).dial)
	held := make([]net.Conn, 0, maxConnections)
	for range maxConnections {
		conn := connectTo(t, address)
		authenticate(t, conn, "user", "secret")
		expect(t, conn, []byte{1, 0})
		held = append(held, conn)
	}

	waiting := connectTo(t, address)
	_, _ = waiting.Write([]byte{5, 1, 2})
	_ = waiting.SetReadDeadline(time.Now().Add(200 * time.Millisecond))
	if _, err := waiting.Read(make([]byte, 2)); err == nil {
		t.Fatal("a connection beyond the limit was served at once")
	}
	held[0].Close()
	_ = waiting.SetReadDeadline(time.Now().Add(5 * time.Second))

	expect(t, waiting, []byte{5, 2})
}
