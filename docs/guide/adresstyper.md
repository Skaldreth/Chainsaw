# Adresstyper

En adress är ett kort sätt att skriva ett **låsskript** (scriptPubKey): det som krävs för att spendera en output.
Typen syns på hur adressen börjar. Chainsaw läser typen ur skriptets byte, inte ur källans etikett, och kontrollerar att
källans adress stämmer med skriptet.

| Typ | Börjar med | Vad den låser till | Kodning |
| --- | --- | --- | --- |
| **P2PKH** | `1` | hashen av en publik nyckel (den äldsta vanliga typen) | Base58Check |
| **P2SH** | `3` | hashen av ett skript, ofta multisig eller inlindad SegWit | Base58Check |
| **P2WPKH** | `bc1q` (42 tecken) | SegWit version 0, hashen av en nyckel | Bech32 |
| **P2WSH** | `bc1q` (62 tecken) | SegWit version 0, hashen av ett skript | Bech32 |
| **P2TR** | `bc1p` | Taproot (SegWit version 1) | Bech32m |

Utan adress:

- **OP_RETURN:** data på kedjan. Kan aldrig spenderas och har oftast beloppet 0.
- **P2PK:** betalning direkt till en publik nyckel. Vanligt i de tidigaste blocken.
- **Bar multisig:** nycklarna står i skriptet.

## Kontrollsummor

Alla adresser har en kontrollsumma, så ett felskrivet tecken upptäcks:

- **Base58Check** (`1…`, `3…`) avslutas med fyra byte ur SHA-256 två gånger. Tecknen `0`, `O`, `I` och `l` finns inte,
  för att de är lätta att förväxla.
- **Bech32** (`bc1q…`) och **Bech32m** (`bc1p…`) har sex tecken kontrollsumma och skrivs med bara gemener eller bara
  versaler. Version 0 använder Bech32 och version 1 och senare Bech32m. En adress med fel sorts kontrollsumma är
  ogiltig.

Testnätets adresser börjar med `m`, `n`, `2` eller `tb1`. Chainsaw blandar aldrig ihop dem med kedjans riktiga adresser.

## Vad typen säger i en utredning

Typen är ett fakta om outputen, men den används också i bedömningar: växeln har ofta **samma typ** som inputs, och en
plånbok använder oftast en och samma typ. Sådant är slutsatser (princip 2) och visas alltid med sin heuristik.
