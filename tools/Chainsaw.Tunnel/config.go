package main

import (
	"bufio"
	"encoding/base64"
	"encoding/hex"
	"errors"
	"fmt"
	"net"
	"net/netip"
	"strconv"
	"strings"
)

// keyLength is the length in bytes of a WireGuard key (Curve25519).
const keyLength = 32

// defaultMTU is the MTU when the file gives none; the same default as wg-quick.
const defaultMTU = 1420

// smallestMTU and largestMTU bound the MTU that is accepted.
const (
	smallestMTU = 576
	largestMTU  = 9000
)

// longestKeepalive is the longest keepalive interval in seconds the helper uses. A shorter interval in the file is
// kept; a longer one, or none, becomes this, so that a healthy tunnel always has a recent handshake and a dead one is
// noticed (see watchHandshakes).
const longestKeepalive = 25

// ignoredInterfaceKeys are the wg-quick keys of [Interface] that only concern a kernel interface or run commands.
// They are accepted and ignored: the helper never runs a command from the file and has no interface of its own.
var ignoredInterfaceKeys = map[string]bool{
	"listenport": true, "fwmark": true, "table": true, "saveconfig": true,
	"preup": true, "postup": true, "predown": true, "postdown": true,
}

// knownKeys are the keys the helper reads; only these are ever named in an error.
var knownKeys = map[string]bool{
	"privatekey": true, "address": true, "dns": true, "mtu": true,
	"publickey": true, "presharedkey": true, "endpoint": true, "allowedips": true, "persistentkeepalive": true,
}

// key is a WireGuard key.
type key [keyLength]byte

// peer is the [Peer] section: the exit's server.
type peer struct {
	// publicKey is the server's public key.
	publicKey key
	// presharedKey is the optional preshared key; nil when the file has none.
	presharedKey *key
	// endpoint is the server's address as written, host:port; a name is resolved when the tunnel starts.
	endpoint string
	// allowedIPs are the networks that may be reached through the server.
	allowedIPs []netip.Prefix
	// keepalive is the keepalive interval in seconds the helper uses (see longestKeepalive).
	keepalive int
}

// config is a parsed WireGuard file with exactly one peer.
type config struct {
	// privateKey is the interface's private key.
	privateKey key
	// addresses are the tunnel's own addresses, without their prefix lengths.
	addresses []netip.Addr
	// dns are the name servers inside the tunnel; every name is resolved there and nowhere else.
	dns []netip.Addr
	// mtu is the tunnel's MTU.
	mtu int
	// peer is the exit's server.
	peer peer
}

// configError is a configuration that is refused. Its message names the line and the key, never a value, so that
// no secret of the file reaches a log.
type configError struct {
	// line is the line number, 1-based; 0 when the error concerns the whole file.
	line int
	// reason says what is wrong.
	reason string
}

// Error gives the reason with the line.
func (e *configError) Error() string {
	if e.line == 0 {
		return e.reason
	}
	return fmt.Sprintf("line %d: %s", e.line, e.reason)
}

// refuse builds a configError for a line.
func refuse(line int, format string, args ...any) error {
	return &configError{line: line, reason: fmt.Sprintf(format, args...)}
}

// parseConfig reads a WireGuard file in the wg-quick format. It demands one [Interface] with PrivateKey, Address and
// DNS (at least one IP address), and exactly one [Peer] with PublicKey, Endpoint and AllowedIPs.
func parseConfig(text string) (*config, error) {
	cfg := &config{mtu: defaultMTU}
	section := ""
	interfaces, peers := 0, 0
	seen := map[string]bool{}
	hasPrivateKey, hasPublicKey := false, false
	keepalive := 0

	scanner := bufio.NewScanner(strings.NewReader(text))
	scanner.Buffer(make([]byte, 0, 4096), 64*1024)
	line := 0
	for scanner.Scan() {
		line++
		raw := scanner.Text()
		if i := strings.IndexByte(raw, '#'); i >= 0 {
			raw = raw[:i]
		}
		raw = strings.TrimSpace(raw)
		if raw == "" {
			continue
		}

		if strings.HasPrefix(raw, "[") {
			if !strings.HasSuffix(raw, "]") {
				return nil, refuse(line, "a section header without ']'")
			}
			section = strings.ToLower(strings.TrimSpace(raw[1 : len(raw)-1]))
			switch section {
			case "interface":
				interfaces++
			case "peer":
				peers++
			default:
				return nil, refuse(line, "an unknown section")
			}
			seen = map[string]bool{}
			continue
		}

		name, value, found := strings.Cut(raw, "=")
		if !found {
			return nil, refuse(line, "a line without '='")
		}
		name = strings.ToLower(strings.TrimSpace(name))
		value = strings.TrimSpace(value)
		if section == "" {
			return nil, refuse(line, "a key before the first section")
		}
		if interfaces > 1 || peers > 1 {
			continue // Reported after the loop.
		}

		// Only a known key is named; anything else could be part of a value, such as a pasted key.
		repeatable := name == "address" || name == "dns" || name == "allowedips"
		if seen[name] && !repeatable && knownKeys[name] {
			return nil, refuse(line, "%s is given twice", name)
		}
		seen[name] = true

		var err error
		if section == "interface" {
			err = cfg.setInterface(line, name, value, &hasPrivateKey)
		} else {
			err = cfg.setPeer(line, name, value, &hasPublicKey, &keepalive)
		}
		if err != nil {
			return nil, err
		}
	}
	if err := scanner.Err(); err != nil {
		return nil, refuse(0, "the file cannot be read")
	}

	if err := cfg.checkComplete(interfaces, peers, hasPrivateKey, hasPublicKey); err != nil {
		return nil, err
	}
	cfg.peer.keepalive = longestKeepalive
	if keepalive > 0 && keepalive < longestKeepalive {
		cfg.peer.keepalive = keepalive
	}
	return cfg, nil
}

// setInterface sets one key of [Interface].
func (cfg *config) setInterface(line int, name, value string, hasPrivateKey *bool) error {
	switch name {
	case "privatekey":
		k, err := parseKey(value)
		if err != nil {
			return refuse(line, "PrivateKey is not a key")
		}
		cfg.privateKey = k
		*hasPrivateKey = true
	case "address":
		for _, item := range splitList(value) {
			prefix, err := netip.ParsePrefix(item)
			if err != nil {
				address, plainErr := netip.ParseAddr(item)
				if plainErr != nil {
					return refuse(line, "Address is not an IP address")
				}
				prefix = netip.PrefixFrom(address, address.BitLen())
			}
			cfg.addresses = append(cfg.addresses, prefix.Addr().Unmap())
		}
	case "dns":
		// wg-quick reads a value that is not an address as a search domain; names are never completed here, so it
		// is ignored.
		for _, item := range splitList(value) {
			if address, err := netip.ParseAddr(item); err == nil {
				cfg.dns = append(cfg.dns, address.Unmap())
			}
		}
	case "mtu":
		mtu, err := strconv.Atoi(value)
		if err != nil || mtu < smallestMTU || mtu > largestMTU {
			return refuse(line, "MTU must be a number from %d to %d", smallestMTU, largestMTU)
		}
		cfg.mtu = mtu
	default:
		if !ignoredInterfaceKeys[name] {
			return refuse(line, "an unknown key in [Interface]")
		}
	}
	return nil
}

// setPeer sets one key of [Peer].
func (cfg *config) setPeer(line int, name, value string, hasPublicKey *bool, keepalive *int) error {
	switch name {
	case "publickey":
		k, err := parseKey(value)
		if err != nil {
			return refuse(line, "PublicKey is not a key")
		}
		cfg.peer.publicKey = k
		*hasPublicKey = true
	case "presharedkey":
		k, err := parseKey(value)
		if err != nil {
			return refuse(line, "PresharedKey is not a key")
		}
		cfg.peer.presharedKey = &k
	case "endpoint":
		host, port, err := net.SplitHostPort(value)
		if err != nil || host == "" {
			return refuse(line, "Endpoint must be host:port")
		}
		if number, err := strconv.Atoi(port); err != nil || number < 1 || number > 65535 {
			return refuse(line, "Endpoint has no valid port")
		}
		cfg.peer.endpoint = value
	case "allowedips":
		for _, item := range splitList(value) {
			prefix, err := netip.ParsePrefix(item)
			if err != nil {
				return refuse(line, "AllowedIPs is not a list of networks")
			}
			cfg.peer.allowedIPs = append(cfg.peer.allowedIPs, prefix.Masked())
		}
	case "persistentkeepalive":
		if strings.EqualFold(value, "off") {
			*keepalive = 0
			return nil
		}
		seconds, err := strconv.Atoi(value)
		if err != nil || seconds < 0 || seconds > 65535 {
			return refuse(line, "PersistentKeepalive must be a number of seconds")
		}
		*keepalive = seconds
	default:
		return refuse(line, "an unknown key in [Peer]")
	}
	return nil
}

// checkComplete demands the sections and keys every tunnel needs.
func (cfg *config) checkComplete(interfaces, peers int, hasPrivateKey, hasPublicKey bool) error {
	switch {
	case interfaces != 1:
		return refuse(0, "the file must have exactly one [Interface]")
	case peers != 1:
		return refuse(0, "the file must have exactly one [Peer]")
	case !hasPrivateKey:
		return refuse(0, "[Interface] has no PrivateKey")
	case len(cfg.addresses) == 0:
		return refuse(0, "[Interface] has no Address")
	case len(cfg.dns) == 0:
		return refuse(0, "[Interface] has no DNS with an IP address; names must be resolved inside the tunnel")
	case !hasPublicKey:
		return refuse(0, "[Peer] has no PublicKey")
	case cfg.peer.endpoint == "":
		return refuse(0, "[Peer] has no Endpoint")
	case len(cfg.peer.allowedIPs) == 0:
		return refuse(0, "[Peer] has no AllowedIPs")
	}
	return nil
}

// parseKey reads a key in base64, as wg-quick writes it.
func parseKey(value string) (key, error) {
	var k key
	decoded, err := base64.StdEncoding.DecodeString(value)
	if err != nil || len(decoded) != keyLength {
		return k, errors.New("not a key")
	}
	copy(k[:], decoded)
	return k, nil
}

// splitList splits a comma-separated value and drops empty items.
func splitList(value string) []string {
	var items []string
	for _, item := range strings.Split(value, ",") {
		if item = strings.TrimSpace(item); item != "" {
			items = append(items, item)
		}
	}
	return items
}

// uapi gives the configuration in the form wireguard-go's device takes (the cross-platform userspace API), with
// the endpoint already resolved to an address.
func (cfg *config) uapi(endpoint netip.AddrPort) string {
	var b strings.Builder
	fmt.Fprintf(&b, "private_key=%s\n", hex.EncodeToString(cfg.privateKey[:]))
	b.WriteString("replace_peers=true\n")
	fmt.Fprintf(&b, "public_key=%s\n", hex.EncodeToString(cfg.peer.publicKey[:]))
	if cfg.peer.presharedKey != nil {
		fmt.Fprintf(&b, "preshared_key=%s\n", hex.EncodeToString(cfg.peer.presharedKey[:]))
	}
	fmt.Fprintf(&b, "endpoint=%s\n", endpoint.String())
	fmt.Fprintf(&b, "persistent_keepalive_interval=%d\n", cfg.peer.keepalive)
	b.WriteString("replace_allowed_ips=true\n")
	for _, prefix := range cfg.peer.allowedIPs {
		fmt.Fprintf(&b, "allowed_ip=%s\n", prefix.String())
	}
	return b.String()
}
