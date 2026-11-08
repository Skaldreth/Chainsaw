package main

import (
	"context"
	"net/netip"
	"strings"
	"testing"
)

// TestOrderEndpointsPutsIPv4FirstAndKeepsOneIPv6 checks the order the server's addresses are tried in.
func TestOrderEndpointsPutsIPv4FirstAndKeepsOneIPv6(t *testing.T) {
	addresses := []netip.Addr{
		netip.MustParseAddr("2001:db8::1"),
		netip.MustParseAddr("2001:db8::2"),
		netip.MustParseAddr("::ffff:192.0.2.1"),
		netip.MustParseAddr("192.0.2.2"),
	}

	ordered := orderEndpoints(addresses, 51820)

	want := []string{"192.0.2.1:51820", "[2001:db8::1]:51820"}
	if len(ordered) != len(want) {
		t.Fatalf("got %v, want %v", ordered, want)
	}
	for index, endpoint := range ordered {
		if endpoint.String() != want[index] {
			t.Fatalf("got %v, want %v", ordered, want)
		}
	}
}

// TestOrderEndpointsWithOnlyIPv6 checks that a server with only IPv6 addresses is still tried.
func TestOrderEndpointsWithOnlyIPv6(t *testing.T) {
	ordered := orderEndpoints([]netip.Addr{netip.MustParseAddr("2001:db8::7")}, 1)

	if len(ordered) != 1 || !ordered[0].Addr().Is6() {
		t.Fatalf("got %v", ordered)
	}
}

// TestResolveEndpointsTakesALiteralAsItIs checks that an address in the file is used without a lookup.
func TestResolveEndpointsTakesALiteralAsItIs(t *testing.T) {
	endpoints, err := resolveEndpoints(context.Background(), "[2001:db8::9]:51820")

	if err != nil || len(endpoints) != 1 || endpoints[0].String() != "[2001:db8::9]:51820" {
		t.Fatalf("got %v, %v", endpoints, err)
	}
}

// TestUapiEndpointOnlyMovesThePeer checks that the change names the peer and its new address and nothing else.
func TestUapiEndpointOnlyMovesThePeer(t *testing.T) {
	private, public := testKeyPair(t)
	cfg, err := parseConfig(strings.Join([]string{
		"[Interface]",
		"PrivateKey = " + private,
		"Address = 10.0.0.2/32",
		"DNS = 10.0.0.1",
		"[Peer]",
		"PublicKey = " + public,
		"Endpoint = 192.0.2.10:51820",
		"AllowedIPs = 0.0.0.0/0",
	}, "\n"))
	if err != nil {
		t.Fatal(err)
	}

	change := cfg.uapiEndpoint(netip.MustParseAddrPort("[2001:db8::1]:51820"))

	if !strings.HasPrefix(change, "public_key=") || !strings.Contains(change, "update_only=true\n") ||
		!strings.HasSuffix(change, "endpoint=[2001:db8::1]:51820\n") || strings.Contains(change, "private_key") {
		t.Fatalf("unexpected change: %q", change)
	}
}
