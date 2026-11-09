package main

import (
	"context"
	"crypto/hmac"
	"crypto/sha256"
	"crypto/subtle"
	"errors"
	"io"
	"net"
	"sync"
	"time"
)

// SOCKS5 (RFC 1928) and its user name and password authentication (RFC 1929): the parts the helper uses.
const (
	socksVersion       = 0x05
	authVersion        = 0x01
	methodUserPassword = 0x02
	methodNone         = 0xFF
	commandConnect     = 0x01
	addressIPv4        = 0x01
	addressName        = 0x03
	addressIPv6        = 0x04
)

// SOCKS5 reply codes.
const (
	replySucceeded       = 0x00
	replyGeneralFailure  = 0x01
	replyNotAllowed      = 0x02
	replyHostUnreachable = 0x04
	replyCommand         = 0x07
	replyAddressType     = 0x08
)

// proofDomain is put in front of Chainsaw's nonce in the proof, so that the proof cannot be taken for anything else.
const proofDomain = "chainsaw/tunnel/proof/v1\n"

// nonceSize is the size of the nonce Chainsaw sends before the SOCKS5 handshake.
const nonceSize = 32

// handshakeTimeout bounds how long a client may take to authenticate and ask; a connection that stalls is closed.
const handshakeTimeout = 10 * time.Second

// dialTimeout bounds how long a connection through the tunnel may take to open.
const dialTimeout = 20 * time.Second

// maxConnections bounds the connections served at once, so that a flood cannot exhaust the helper. A connection
// beyond it waits in the listener's queue for a free slot.
const maxConnections = 64

// halfClosedTimeout bounds how long one direction of a relay may stay open after the other has ended, so that a
// target that never closes cannot hold a slot for ever.
const halfClosedTimeout = 60 * time.Second

// longestAcceptPause is the longest pause before accepting again after a temporary failure.
const longestAcceptPause = time.Second

// errProtocol is a client that does not speak the protocol, or speaks it wrongly.
var errProtocol = errors.New("the client broke the SOCKS5 protocol")

// errAuthentication is a client with the wrong user name or password.
var errAuthentication = errors.New("wrong user name or password")

// socksServer is a SOCKS5 proxy that demands the tunnel's user name and password and only connects (CONNECT) to
// targets through the tunnel. It offers no other method, so a program without the credentials gets nothing.
type socksServer struct {
	// username and password are the credentials Chainsaw chose for this tunnel.
	username, password []byte
	// dial connects to a target through the tunnel.
	dial func(ctx context.Context, host string, port uint16) (net.Conn, error)
	// slots bounds the connections served at once.
	slots chan struct{}
}

// newSocksServer creates the proxy.
func newSocksServer(
	username, password string, dial func(context.Context, string, uint16) (net.Conn, error),
) *socksServer {
	return &socksServer{
		username: []byte(username),
		password: []byte(password),
		dial:     dial,
		slots:    make(chan struct{}, maxConnections),
	}
}

// serve accepts connections until the listener is closed (nil) or accepting fails for good (the error). A
// temporary failure, such as too few free handles, is retried after a short pause that grows, as net/http does.
func (s *socksServer) serve(ctx context.Context, listener net.Listener) error {
	pause := time.Duration(0)
	for {
		// A slot first, so that a connection beyond the limit waits instead of being cut off.
		select {
		case s.slots <- struct{}{}:
		case <-ctx.Done():
			return nil
		}
		conn, err := listener.Accept()
		if err != nil {
			<-s.slots
			if errors.Is(err, net.ErrClosed) || ctx.Err() != nil {
				return nil
			}
			if !isTemporary(err) {
				return err
			}
			pause = min(max(2*pause, 5*time.Millisecond), longestAcceptPause)
			select {
			case <-time.After(pause):
			case <-ctx.Done():
				return nil
			}
			continue
		}
		pause = 0
		go func() {
			defer func() { <-s.slots }()
			s.handle(ctx, conn)
		}()
	}
}

// isTemporary tells whether an accept failure may pass, as net/http judges it.
func isTemporary(err error) bool {
	var temporary interface{ Temporary() bool }
	return errors.As(err, &temporary) && temporary.Temporary()
}

// handle serves one client: the proof, authentication, the request and then the relay in both directions.
func (s *socksServer) handle(ctx context.Context, client net.Conn) {
	defer client.Close()
	_ = client.SetDeadline(time.Now().Add(handshakeTimeout))

	if err := s.prove(client); err != nil {
		return
	}
	if err := s.authenticate(client); err != nil {
		return
	}
	host, port, reply := readRequest(client)
	if reply != replySucceeded {
		writeReply(client, reply)
		return
	}

	dialCtx, cancel := context.WithTimeout(ctx, dialTimeout)
	target, err := s.dial(dialCtx, host, port)
	cancel()
	if err != nil {
		writeReply(client, replyFor(err))
		return
	}
	defer target.Close()

	if !writeReply(client, replySucceeded) {
		return
	}
	_ = client.SetDeadline(time.Time{})
	relay(client, target)
}

// prove answers Chainsaw's nonce with HMAC-SHA256 under the tunnel's password, before anything of SOCKS5 is said.
// Chainsaw sends the credentials only once the proof holds, so a program that has taken the port after this helper
// ended (it does not know the password) is found before it learns them or a single request.
func (s *socksServer) prove(client net.Conn) error {
	nonce := make([]byte, nonceSize)
	if _, err := io.ReadFull(client, nonce); err != nil {
		return errProtocol
	}
	_, err := client.Write(proofFor(s.password, nonce))
	return err
}

// proofFor gives the proof of a nonce under a password.
func proofFor(password, nonce []byte) []byte {
	mac := hmac.New(sha256.New, password)
	mac.Write([]byte(proofDomain))
	mac.Write(nonce)
	return mac.Sum(nil)
}

// authenticate offers only user name and password and checks them in constant time.
func (s *socksServer) authenticate(client net.Conn) error {
	header := make([]byte, 2)
	if _, err := io.ReadFull(client, header); err != nil || header[0] != socksVersion || header[1] == 0 {
		return errProtocol
	}
	methods := make([]byte, header[1])
	if _, err := io.ReadFull(client, methods); err != nil {
		return errProtocol
	}
	offered := false
	for _, method := range methods {
		offered = offered || method == methodUserPassword
	}
	if !offered {
		_, _ = client.Write([]byte{socksVersion, methodNone})
		return errAuthentication
	}
	if _, err := client.Write([]byte{socksVersion, methodUserPassword}); err != nil {
		return err
	}

	version := make([]byte, 1)
	if _, err := io.ReadFull(client, version); err != nil || version[0] != authVersion {
		return errProtocol
	}
	username, err := readShortString(client)
	if err != nil {
		return err
	}
	password, err := readShortString(client)
	if err != nil {
		return err
	}

	// Both are compared whatever the first gives, so the time says nothing about which was wrong.
	userOK := subtle.ConstantTimeCompare(username, s.username)
	passOK := subtle.ConstantTimeCompare(password, s.password)
	if userOK&passOK != 1 {
		_, _ = client.Write([]byte{authVersion, 0x01})
		return errAuthentication
	}
	_, err = client.Write([]byte{authVersion, 0x00})
	return err
}

// readShortString reads a string with a length byte in front, as RFC 1929 and the name form of RFC 1928 write it.
func readShortString(r io.Reader) ([]byte, error) {
	length := make([]byte, 1)
	if _, err := io.ReadFull(r, length); err != nil {
		return nil, errProtocol
	}
	value := make([]byte, length[0])
	if _, err := io.ReadFull(r, value); err != nil {
		return nil, errProtocol
	}
	return value, nil
}

// readRequest reads the client's request. Only CONNECT is served; the reply code says why a request is refused.
func readRequest(r io.Reader) (host string, port uint16, reply byte) {
	header := make([]byte, 4)
	if _, err := io.ReadFull(r, header); err != nil || header[0] != socksVersion || header[2] != 0 {
		return "", 0, replyGeneralFailure
	}
	if header[1] != commandConnect {
		return "", 0, replyCommand
	}

	switch header[3] {
	case addressIPv4, addressIPv6:
		size := net.IPv4len
		if header[3] == addressIPv6 {
			size = net.IPv6len
		}
		raw := make([]byte, size)
		if _, err := io.ReadFull(r, raw); err != nil {
			return "", 0, replyGeneralFailure
		}
		host = net.IP(raw).String()
	case addressName:
		name, err := readShortString(r)
		if err != nil || len(name) == 0 {
			return "", 0, replyGeneralFailure
		}
		host = string(name)
	default:
		return "", 0, replyAddressType
	}

	raw := make([]byte, 2)
	if _, err := io.ReadFull(r, raw); err != nil {
		return "", 0, replyGeneralFailure
	}
	port = uint16(raw[0])<<8 | uint16(raw[1])
	if port == 0 {
		return "", 0, replyNotAllowed
	}
	return host, port, replySucceeded
}

// replyFor gives the reply code for a failed connection.
func replyFor(err error) byte {
	switch {
	case errors.Is(err, errLocalTarget):
		return replyNotAllowed
	case errors.Is(err, errNotFound):
		return replyHostUnreachable
	default:
		return replyGeneralFailure
	}
}

// writeReply answers the request. The bound address is always 0.0.0.0:0: the client needs nothing more, and the
// tunnel's own address stays inside the helper.
func writeReply(w io.Writer, reply byte) bool {
	_, err := w.Write([]byte{socksVersion, reply, 0x00, addressIPv4, 0, 0, 0, 0, 0, 0})
	return err == nil
}

// relay copies in both directions until both are done; when one side ends, the other is told by a half close.
func relay(client, target net.Conn) {
	var wg sync.WaitGroup
	wg.Add(2)
	copyHalf := func(to, from net.Conn) {
		defer wg.Done()
		_, _ = io.Copy(to, from)
		if half, ok := to.(interface{ CloseWrite() error }); ok {
			_ = half.CloseWrite()
			// The other direction gets a while to finish, never for ever.
			_ = to.SetReadDeadline(time.Now().Add(halfClosedTimeout))
		} else {
			_ = to.Close()
		}
	}
	go copyHalf(target, client)
	go copyHalf(client, target)
	wg.Wait()
}
