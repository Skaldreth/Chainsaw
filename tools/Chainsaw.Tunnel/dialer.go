package main

import (
	"context"
	"errors"
	"net"
	"net/netip"
)

// errLocalTarget is returned when every address of a target is on the computer, a private network or reserved.
var errLocalTarget = errors.New("the target is not a global address")

// errNotFound is returned when a name cannot be resolved inside the tunnel.
var errNotFound = errors.New("the name cannot be resolved inside the tunnel")

// tunnelNet is what the dialer needs from the tunnel's network stack: a name lookup with the tunnel's own name
// servers and a TCP connection to an address. The tests give a fake one.
type tunnelNet interface {
	// LookupContextHost resolves a name with the name servers of the WireGuard file, inside the tunnel.
	LookupContextHost(ctx context.Context, host string) ([]string, error)
	// DialContext connects to an address (never a name) inside the tunnel.
	DialContext(ctx context.Context, network, address string) (net.Conn, error)
}

// dialer connects to a target through the tunnel. Names are resolved inside the tunnel, never with the computer's
// resolver, and addresses that are not global are refused, also when a name resolves to one.
type dialer struct {
	// tnet is the tunnel's network stack.
	tnet tunnelNet
}

// dial connects to host:port. host is an IP address or a name.
func (d *dialer) dial(ctx context.Context, host string, port uint16) (net.Conn, error) {
	candidates, err := d.resolve(ctx, host)
	if err != nil {
		return nil, err
	}

	var last error
	for _, address := range candidates {
		target := netip.AddrPortFrom(address, port).String()
		conn, err := d.tnet.DialContext(ctx, "tcp", target)
		if err == nil {
			return conn, nil
		}
		last = err
		if ctx.Err() != nil {
			break
		}
	}
	return nil, last
}

// resolve gives the global addresses of host: the address itself, or those its name resolves to inside the tunnel.
// Addresses that are not global are dropped; when none is left the target is refused.
func (d *dialer) resolve(ctx context.Context, host string) ([]netip.Addr, error) {
	if address, err := netip.ParseAddr(host); err == nil {
		if address.Zone() != "" || isLocal(address) {
			return nil, errLocalTarget
		}
		return []netip.Addr{address.Unmap()}, nil
	}

	answers, err := d.tnet.LookupContextHost(ctx, host)
	if err != nil {
		return nil, errNotFound
	}

	var global []netip.Addr
	for _, answer := range answers {
		address, err := netip.ParseAddr(answer)
		if err == nil && address.Zone() == "" && !isLocal(address) {
			global = append(global, address.Unmap())
		}
	}
	if len(global) == 0 {
		if len(answers) == 0 {
			return nil, errNotFound
		}
		return nil, errLocalTarget
	}
	return global, nil
}
