package main

import (
	"crypto/rand"
	"encoding/base64"
	"encoding/hex"
	"net/netip"
	"strings"
	"testing"

	"golang.org/x/crypto/curve25519"
)

// testKeyPair makes a fresh WireGuard key pair, base64 as in a WireGuard file, so no key is kept in the repository.
func testKeyPair(t *testing.T) (private, public string) {
	t.Helper()
	var p [32]byte
	if _, err := rand.Read(p[:]); err != nil {
		t.Fatal(err)
	}
	p[0] &= 248
	p[31] = (p[31] & 127) | 64
	pub, err := curve25519.X25519(p[:], curve25519.Basepoint)
	if err != nil {
		t.Fatal(err)
	}
	return base64.StdEncoding.EncodeToString(p[:]), base64.StdEncoding.EncodeToString(pub)
}

// validFile builds a WireGuard file as an exit's provider would give it, with documentation addresses.
func validFile(t *testing.T) (file, private, public string) {
	t.Helper()
	private, _ = testKeyPair(t)
	_, public = testKeyPair(t)
	file = `# An exit
[Interface]
PrivateKey = ` + private + `
Address = 10.64.0.2/32, fd00::2/128
DNS = 10.64.0.1, example.internal
MTU = 1380
PostUp = echo never run

[Peer]
PublicKey = ` + public + `
Endpoint = 192.0.2.10:51820
AllowedIPs = 0.0.0.0/0, ::/0
`
	return file, private, public
}

func TestParseConfigReadsAValidFile(t *testing.T) {
	file, private, public := validFile(t)

	cfg, err := parseConfig(file)
	if err != nil {
		t.Fatalf("refused: %v", err)
	}

	if got := base64.StdEncoding.EncodeToString(cfg.privateKey[:]); got != private {
		t.Error("the private key was not read")
	}
	if got := base64.StdEncoding.EncodeToString(cfg.peer.publicKey[:]); got != public {
		t.Error("the public key was not read")
	}
	wantAddresses := []netip.Addr{netip.MustParseAddr("10.64.0.2"), netip.MustParseAddr("fd00::2")}
	if len(cfg.addresses) != 2 || cfg.addresses[0] != wantAddresses[0] || cfg.addresses[1] != wantAddresses[1] {
		t.Errorf("addresses %v", cfg.addresses)
	}
	if len(cfg.dns) != 1 || cfg.dns[0] != netip.MustParseAddr("10.64.0.1") {
		t.Errorf("dns %v; the search domain must be ignored", cfg.dns)
	}
	if cfg.mtu != 1380 || cfg.peer.endpoint != "192.0.2.10:51820" || len(cfg.peer.allowedIPs) != 2 {
		t.Errorf("mtu %d, endpoint %q, allowed %v", cfg.mtu, cfg.peer.endpoint, cfg.peer.allowedIPs)
	}
	if cfg.peer.keepalive != longestKeepalive {
		t.Errorf("keepalive %d, want %d when the file has none", cfg.peer.keepalive, longestKeepalive)
	}
}

func TestParseConfigGivesTheDeviceItsConfiguration(t *testing.T) {
	file, private, public := validFile(t)
	cfg, err := parseConfig(file)
	if err != nil {
		t.Fatal(err)
	}

	uapi := cfg.uapi(netip.MustParseAddrPort("192.0.2.10:51820"))

	decodedPrivate, _ := base64.StdEncoding.DecodeString(private)
	decodedPublic, _ := base64.StdEncoding.DecodeString(public)
	for _, want := range []string{
		"private_key=" + hex.EncodeToString(decodedPrivate) + "\n",
		"public_key=" + hex.EncodeToString(decodedPublic) + "\n",
		"endpoint=192.0.2.10:51820\n",
		"persistent_keepalive_interval=25\n",
		"allowed_ip=0.0.0.0/0\n",
		"allowed_ip=::/0\n",
	} {
		if !strings.Contains(uapi, want) {
			t.Errorf("missing %q", want)
		}
	}
	if !strings.HasPrefix(uapi, "private_key=") {
		t.Error("the device's own key must come first")
	}
	if strings.Contains(uapi, "preshared_key") {
		t.Error("no preshared key was given")
	}
}

func TestParseConfigReadsAPresharedKeyAndAShortKeepalive(t *testing.T) {
	file, _, _ := validFile(t)
	shared, _ := testKeyPair(t)
	file += "PresharedKey = " + shared + "\nPersistentKeepalive = 10\n"

	cfg, err := parseConfig(file)
	if err != nil {
		t.Fatal(err)
	}

	if cfg.peer.presharedKey == nil || cfg.peer.keepalive != 10 {
		t.Errorf("preshared %v, keepalive %d", cfg.peer.presharedKey != nil, cfg.peer.keepalive)
	}
	if !strings.Contains(cfg.uapi(netip.MustParseAddrPort("192.0.2.10:1")), "preshared_key=") {
		t.Error("the preshared key must reach the device")
	}
}

func TestParseConfigKeepsALongKeepaliveShort(t *testing.T) {
	file, _, _ := validFile(t)

	cfg, err := parseConfig(file + "PersistentKeepalive = 300\n")
	if err != nil {
		t.Fatal(err)
	}

	if cfg.peer.keepalive != longestKeepalive {
		t.Errorf("keepalive %d, want %d", cfg.peer.keepalive, longestKeepalive)
	}
}

func TestParseConfigRefusesAFileThatCannotBeUsedSafely(t *testing.T) {
	file, _, public := validFile(t)
	secondPeer := "\n[Peer]\nPublicKey = " + public + "\nEndpoint = 192.0.2.11:51820\nAllowedIPs = 0.0.0.0/0\n"
	cases := map[string]string{
		"no DNS":                   strings.Replace(file, "DNS = 10.64.0.1, example.internal\n", "", 1),
		"only a search domain":     strings.Replace(file, "DNS = 10.64.0.1, example.internal", "DNS = example.internal", 1),
		"DNS under the peer":       strings.Replace(file, "DNS = 10.64.0.1, example.internal\n", "", 1) + "DNS = 10.64.0.1\n",
		"two peers":                file + secondPeer,
		"no peer":                  file[:strings.Index(file, "[Peer]")],
		"no address":               strings.Replace(file, "Address = 10.64.0.2/32, fd00::2/128\n", "", 1),
		"a bad address":            strings.Replace(file, "10.64.0.2/32", "ten", 1),
		"a short key":              strings.Replace(file, public, "AAAA", 1),
		"an unknown section":       file + "[Other]\n",
		"an unknown key":           file + "Something = 1\n",
		"a line without a value":   file + "Endpoint\n",
		"an endpoint without port": strings.Replace(file, "192.0.2.10:51820", "192.0.2.10", 1),
		"a port out of range":      strings.Replace(file, "192.0.2.10:51820", "192.0.2.10:70000", 1),
		"a tiny MTU":               strings.Replace(file, "MTU = 1380", "MTU = 100", 1),
		"a key given twice":        file + "PublicKey = " + public + "\n",
		"a key before a section":   "MTU = 1380\n" + file,
	}

	for name, text := range cases {
		t.Run(name, func(t *testing.T) {
			if _, err := parseConfig(text); err == nil {
				t.Fatal("accepted")
			}
		})
	}
}

func TestParseConfigNeverPutsAValueInItsError(t *testing.T) {
	file, private, _ := validFile(t)
	broken := strings.Replace(file, "PrivateKey = "+private, "PrivateKey = "+private+"x", 1)

	_, err := parseConfig(broken)

	if err == nil || strings.Contains(err.Error(), private) {
		t.Fatalf("error %v", err)
	}
	if !strings.Contains(err.Error(), "line 3") {
		t.Errorf("the error should name the line: %v", err)
	}
}
