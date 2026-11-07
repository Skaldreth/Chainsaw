package main

import (
	"context"
	"errors"
	"net"
	"net/netip"
	"testing"
)

func TestIsLocalRefusesEveryAddressThatIsNotGlobal(t *testing.T) {
	local := []string{
		"0.0.0.0", "10.1.2.3", "100.64.0.1", "127.0.0.1", "169.254.1.1", "172.16.0.1", "172.31.255.255",
		"192.0.0.1", "192.0.2.1", "192.88.99.1", "192.168.1.1", "198.18.0.1", "198.51.100.1", "203.0.113.1",
		"224.0.0.1", "255.255.255.255",
		"::", "::1", "fe80::1", "fc00::1", "fd12::1", "ff02::1", "2001:db8::1", "2001:0:1::1",
		"::ffff:127.0.0.1", "::ffff:10.0.0.1", "::10.0.0.1", "::ffff:0:10.0.0.1", "64:ff9b::10.0.0.1",
		"2002:0a00:0001::1",
	}
	global := []string{"8.8.8.8", "1.1.1.1", "172.32.0.1", "100.128.0.1", "2606:4700::1111", "2002:0808:0808::1"}

	for _, text := range local {
		if !isLocal(netip.MustParseAddr(text)) {
			t.Errorf("%s must be refused", text)
		}
	}
	for _, text := range global {
		if isLocal(netip.MustParseAddr(text)) {
			t.Errorf("%s is global", text)
		}
	}
	if !isLocal(netip.Addr{}) {
		t.Error("an invalid address must be refused")
	}
}

// fakeNet is a tunnel network stack that answers names from a table and records what is dialled.
type fakeNet struct {
	answers map[string][]string
	failing map[string]bool
	dialled []string
	lookups []string
}

// LookupContextHost answers from the table.
func (f *fakeNet) LookupContextHost(_ context.Context, host string) ([]string, error) {
	f.lookups = append(f.lookups, host)
	answer, ok := f.answers[host]
	if !ok {
		return nil, errors.New("no such host")
	}
	return answer, nil
}

// DialContext records the address and gives one end of a pipe, or fails for an address in failing.
func (f *fakeNet) DialContext(_ context.Context, network, address string) (net.Conn, error) {
	f.dialled = append(f.dialled, network+" "+address)
	if f.failing[address] {
		return nil, errors.New("refused")
	}
	client, server := net.Pipe()
	go server.Close()
	return client, nil
}

func TestDialResolvesNamesInsideTheTunnel(t *testing.T) {
	tnet := &fakeNet{answers: map[string][]string{"source.example": {"93.184.216.34"}}}

	conn, err := (&dialer{tnet: tnet}).dial(context.Background(), "source.example", 443)

	if err != nil {
		t.Fatal(err)
	}
	conn.Close()
	if len(tnet.lookups) != 1 || len(tnet.dialled) != 1 || tnet.dialled[0] != "tcp 93.184.216.34:443" {
		t.Errorf("lookups %v, dialled %v", tnet.lookups, tnet.dialled)
	}
}

func TestDialRefusesANameThatResolvesToTheComputerOrAPrivateNetwork(t *testing.T) {
	tnet := &fakeNet{answers: map[string][]string{
		"rebind.example": {"127.0.0.1", "10.0.0.5", "::1", "fe80::1%eth0"},
	}}

	_, err := (&dialer{tnet: tnet}).dial(context.Background(), "rebind.example", 443)

	if !errors.Is(err, errLocalTarget) || len(tnet.dialled) != 0 {
		t.Fatalf("err %v, dialled %v", err, tnet.dialled)
	}
}

func TestDialDropsLocalAnswersAndTriesTheGlobalOnesInOrder(t *testing.T) {
	tnet := &fakeNet{
		answers: map[string][]string{"mixed.example": {"192.168.0.1", "93.184.216.34", "2606:4700::1111"}},
		failing: map[string]bool{"93.184.216.34:443": true},
	}

	conn, err := (&dialer{tnet: tnet}).dial(context.Background(), "mixed.example", 443)

	if err != nil {
		t.Fatal(err)
	}
	conn.Close()
	want := []string{"tcp 93.184.216.34:443", "tcp [2606:4700::1111]:443"}
	if len(tnet.dialled) != 2 || tnet.dialled[0] != want[0] || tnet.dialled[1] != want[1] {
		t.Errorf("dialled %v", tnet.dialled)
	}
}

func TestDialRefusesALocalAddressWithoutLookingItUp(t *testing.T) {
	for _, host := range []string{"127.0.0.1", "::1", "10.0.0.1", "::ffff:192.168.1.1", "fe80::1%1"} {
		tnet := &fakeNet{}

		_, err := (&dialer{tnet: tnet}).dial(context.Background(), host, 443)

		if !errors.Is(err, errLocalTarget) || len(tnet.dialled) != 0 || len(tnet.lookups) != 0 {
			t.Errorf("%s: err %v, dialled %v, lookups %v", host, err, tnet.dialled, tnet.lookups)
		}
	}
}

func TestDialSaysWhenANameCannotBeResolved(t *testing.T) {
	for name, answers := range map[string]map[string][]string{"unknown": {}, "empty": {"x.example": {}}} {
		tnet := &fakeNet{answers: answers}

		_, err := (&dialer{tnet: tnet}).dial(context.Background(), "x.example", 443)

		if !errors.Is(err, errNotFound) {
			t.Errorf("%s: err %v", name, err)
		}
	}
}
