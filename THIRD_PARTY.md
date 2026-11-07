# Tredje part

Kod och idéer från andra som Chainsaw bygger på, med deras licenser.

## WireGuard-tunneln (`tools/Chainsaw.Tunnel`)

Chainsaws eget hjälpprogram för WireGuard, `chainsaw-tunnel.exe`, är skrivet i Go för Chainsaw och länkar in
bibliotek från andra. Ingen kod från Gawk används. Licenserna är granskade (2026-11-07): alla tillåter att programmet
byggs och skickas med Chainsaw, så länge licenstexterna följer med. De ligger i `tools/Chainsaw.Tunnel/licenses/` och
kopieras till `licenses/chainsaw-tunnel/` bredvid programmet.

| Modul | Version | Används till | Licens | Fil |
| --- | --- | --- | --- | --- |
| `golang.zx2c4.com/wireguard` (wireguard-go) | `v0.0.0-20261024220952-b3bc704ef0ee` | WireGuard i användarläge och nätstacken i minnet (`tun/netstack`) | MIT, Copyright (C) 2017–2025 WireGuard LLC | `wireguard-go.txt` |
| `gvisor.dev/gvisor` (gVisor netstack) | `v0.0.0-20250503011706-39ed1f5ac29c` | TCP/IP-stacken inne i tunneln | Apache 2.0, Copyright The gVisor Authors | `gvisor.txt` |
| `golang.zx2c4.com/wintun` | `v0.0.0-20230126152724-0fa3db229ce2` | Följer med wireguard-go; Wintun-drivrutinen används inte, eftersom tunneln inte har något nätverkskort | MIT, Copyright (C) 2017–2021 WireGuard LLC | `wintun-go.txt` |
| `github.com/google/btree` | `v1.1.2` | Används av gVisor | Apache 2.0, Copyright Google | `google-btree.txt` |
| `golang.org/x/crypto`, `x/net`, `x/sys`, `x/time` | `v0.58.0`, `v0.62.0`, `v0.49.0`, `v0.16.0` | Kryptografi (Curve25519, ChaCha20-Poly1305, BLAKE2s), DNS-meddelanden, systemanrop | BSD 3-Clause, Copyright The Go Authors | `go.txt` |
| Go (standardbiblioteket och körmiljön) | `go1.27.3` | Kompileras in i programmet | BSD 3-Clause, Copyright The Go Authors | `go.txt` |

Apache 2.0 kräver att licensen följer med och att en eventuell NOTICE-fil gör det; varken gVisor eller btree har någon.
Ingen modul har en licens med copyleft (GPL eller liknande).

## Bevakningens ljud (`src/Chainsaw.Watcher/Sounds/coin-swoosh.wav`)

Mynt-swooshet med skramlet är **Chainsaws eget**: det räknas fram ur siffror av `tools/sounds/coin-swoosh.py` (bara
Pythons standardbibliotek, med fast slumpfrö, så att samma fil kommer ut varje gång). Ingen inspelning eller ljudfil från
någon annan används, och ingen licens behövs.
