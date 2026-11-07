# Tredje part

Kod och idéer från andra som Chainsaw bygger på, med deras licenser.

## asciify-engine

Teckenfältet bakom låsskärmen och förstagångsinställningen (`src/Chainsaw.App/Field/`) bygger på fältet i
asciify-engine: scenerna Swell, Drift, Contour och Mesh, brus och fraktalt brus, och lyktan under musen. Det är
omskrivet för Chainsaw i C# och körs på processorn i stället för grafikkortet; ingen kod är kopierad.

```
MIT License

Copyright (c) 2026 ayangabryl

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

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
| `golang.org/x/crypto`, `x/net`, `x/sys`, `x/time` | `v0.37.0`, `v0.39.0`, `v0.32.0`, `v0.7.0` | Kryptografi (Curve25519, ChaCha20-Poly1305, BLAKE2s), DNS-meddelanden, systemanrop | BSD 3-Clause, Copyright The Go Authors | `go.txt` |
| Go (standardbiblioteket och körmiljön) | `go1.24.7` | Kompileras in i programmet | BSD 3-Clause, Copyright The Go Authors | `go.txt` |

Apache 2.0 kräver att licensen följer med och att en eventuell NOTICE-fil gör det; varken gVisor eller btree har någon.
Ingen modul har en licens med copyleft (GPL eller liknande).
