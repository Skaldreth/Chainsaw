package main

import "net/netip"

// isLocal tells whether an address is anything but a global unicast address: loopback, unspecified, private,
// shared, link-local, multicast, reserved, documentation, or an IPv6 address that carries such an IPv4 address.
// A lookup never needs them, and through the tunnel they would reach the exit's own network. The rules are the
// same as Chainsaw's own check before a request (LocalAddresses in the Network module), so that the helper refuses
// also when a name resolves to such an address.
func isLocal(address netip.Addr) bool {
	if !address.IsValid() {
		return true
	}
	if address.Is4() || address.Is4In6() {
		return isLocalV4(address.Unmap().As4())
	}

	bytes := address.As16()
	if embedded, ok := embeddedIPv4(bytes); ok {
		return isLocalV4(embedded)
	}

	// 2001::/32 (Teredo, which hides an IPv4 address) and 2001:db8::/32 (documentation) are never a source.
	if bytes[0] == 0x20 && bytes[1] == 0x01 {
		teredo := bytes[2] == 0 && bytes[3] == 0
		documentation := bytes[2] == 0x0D && bytes[3] == 0xB8
		if teredo || documentation {
			return true
		}
	}

	// Only 2000::/3 is global unicast; everything else (fe80::/10, fc00::/7, ff00::/8 and the rest) is refused.
	return bytes[0]&0xE0 != 0x20
}

// isLocalV4 checks the IPv4 ranges that are not global: 0/8, 10/8, 100.64/10, 127/8, 169.254/16, 172.16/12,
// 192.0.0/24, 192.0.2/24, 192.88.99/24, 192.168/16, 198.18/15, 198.51.100/24, 203.0.113/24 and everything from
// 224/4 up (multicast, reserved and broadcast).
func isLocalV4(b [4]byte) bool {
	switch {
	case b[0] == 0 || b[0] == 10 || b[0] == 127:
		return true
	case b[0] == 100 && b[1] >= 64 && b[1] <= 127:
		return true
	case b[0] == 169 && b[1] == 254:
		return true
	case b[0] == 172 && b[1] >= 16 && b[1] <= 31:
		return true
	case b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2):
		return true
	case b[0] == 192 && b[1] == 88 && b[2] == 99:
		return true
	case b[0] == 192 && b[1] == 168:
		return true
	case b[0] == 198 && (b[1] == 18 || b[1] == 19):
		return true
	case b[0] == 198 && b[1] == 51 && b[2] == 100:
		return true
	case b[0] == 203 && b[1] == 0 && b[2] == 113:
		return true
	case b[0] >= 224:
		return true
	}
	return false
}

// embeddedIPv4 gives the IPv4 address inside an IPv6 address that carries one: IPv4-compatible (::/96), translated
// (::ffff:0:0:0/96), NAT64 (64:ff9b::/96) and 6to4 (2002::/16). IPv4-mapped addresses are unmapped by the caller.
func embeddedIPv4(b [16]byte) ([4]byte, bool) {
	var v4 [4]byte
	if b[0] == 0x20 && b[1] == 0x02 {
		copy(v4[:], b[2:6])
		return v4, true
	}

	nat64 := [12]byte{0x00, 0x64, 0xFF, 0x9B}
	zeroes := func(from, to int) bool {
		for _, x := range b[from:to] {
			if x != 0 {
				return false
			}
		}
		return true
	}
	compatible := zeroes(0, 12)
	translated := zeroes(0, 8) && b[8] == 0xFF && b[9] == 0xFF && b[10] == 0 && b[11] == 0
	if compatible || translated || [12]byte(b[:12]) == nat64 {
		copy(v4[:], b[12:16])
		return v4, true
	}
	return v4, false
}
