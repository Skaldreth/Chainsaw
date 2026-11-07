# Chainsaw.Tunnel

Chainsaws eget hjälpprogram för WireGuard (`chainsaw-tunnel.exe`). Det kör WireGuard i användarläge, med wireguard-go
och gVisors nätstack i minnet, så det behöver inget nätverkskort och inga administratörsrättigheter. Tunneln erbjuds
Chainsaw som en SOCKS5-proxy på `127.0.0.1`, med ett användarnamn och lösenord som Chainsaw slumpar för varje tunnel.
Det är inte beroende av Gawk, och ingen kod från Gawk används.

## Hur Chainsaw talar med det

Allt går över programmets standardströmmar; ingenting står på kommandoraden eller skrivs till disk.

| Ström | Innehåll |
| --- | --- |
| stdin, första raden | `{"configuration": "<WireGuard-filen>", "username": "…", "password": "…"}` ur valvet |
| stdout, första raden | `{"port": N}` när servern har svarat på en handskakning, annars `{"error": "<kod>"}` |
| stdout, senare | `{"stopped": "lost"}` när tunneln har fallit; programmet avslutas |
| stdin stängs | programmet stoppar. När Chainsaw avslutas, hur det än sker, stängs röret, så programmet lever aldrig kvar |

Felkoderna är `start`, `configuration`, `endpoint`, `device`, `handshake` och `listen` (se `main.go`). Programmet
skriver aldrig till stderr och loggar ingenting.

## Vad det vägrar

- Allt utom SOCKS5 med användarnamn och lösenord, och allt utom `CONNECT`.
- Adresser på datorn, i privata nät och i reserverade nät (samma regler som `LocalTargets` i Chainsaw), också när ett
  namn pekar dit.
- En WireGuard-fil utan `DNS =` med en IP-adress under `[Interface]`, med mer än en `[Peer]`, med en okänd nyckel eller
  en trasig nyckel. `PostUp` och de andra kommandona i wg-quick ignoreras och körs aldrig.

Namn slås upp inne i tunneln med filens `DNS =`. Det enda namn som slås upp med datorns egen resolver är serverns eget
namn i `Endpoint`, eftersom tunneln inte finns innan dess.

## Bygga

`./build.sh` (eller `build.ps1` på Windows) kör `go vet` och `go test` och bygger sedan `dist/chainsaw-tunnel.exe` för
Windows x64 tillsammans med dess SHA-256. Bygget är reproducerbart med samma version av Go (`go.mod`). Chainsaw startar
bara programmet om hashen stämmer med den Chainsaw byggdes med (`HelperProcessStarter`), så bygg om Chainsaw efteråt
och checka in båda filerna. Appen kopierar programmet och licenserna (`licenses/`) till sin utdatamapp.

Licenserna står i `THIRD_PARTY.md` i roten.
