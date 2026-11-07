# Chainsaw – överlämning

Senast uppdaterad: 2026-11-06.

Läget och nästa steg, kort. Specifikation, arkitektur och arbetssätt står i `CLAUDE.md`, backloggen i `docs/TODO.md`.

## Läget

- **Skelettet** (2026-11-07): `Chainsaw.slnx` med SharedKernel (`IClock`, `Chain`, `Satoshi`), de tio modulerna och ett
  testprojekt per modul.
- **Etapp 1, skalet SH1b och temat** (2026-11-07, DEV-LOG): titelraden, ikonlisten, tomma sidor per vy, kommandoraden
  längst ner och statusraden. 41 tester gröna. **Inte provat på Windows**, se `docs/TESTER-INFOR-1.0.md`.
- **Etapp 2, Security** (2026-11-07, DEV-LOG): valvet med huvudnyckel, inlindad datanyckel, API-nycklar,
  operatörsprofilen och nycklar per syfte (`IPurposeKeys`) till etapp 3. Inget gränssnitt.
- **Etapp 4, Network** (2026-11-07, DEV-LOG): utgångarna krypterade i valvet, anslutningen till den valda utgången
  och `IExitHttp`, den enda vägen ut, som bara går genom tunnelns SOCKS5-port och vägrar utan ansluten utgång.
  Tunneln själv (WireGuard) är inte byggd, så varje anslutning misslyckas ärligt. Inget gränssnitt.
- **Etapp 3, Cases** (2026-11-07, DEV-LOG): ärendefilen `.cut` med egen filnyckel och nyckelkuvert (valvet och ett
  valfritt delningslösenord), schemasteg per modul, journalen och de förseglade tabellerna i en hashkedja,
  verifieringen bakom `/verify`. Inget gränssnitt.

- **Etapp 5, Chains** (2026-11-07, DEV-LOG): Bitcoin-modellen, adresser och txid med kontrollsummor,
  `IBitcoinSource` och Esplora-källan genom `IExitHttp`, varje råsvar förseglat i ärendet, det som inte kan ändras
  hämtas aldrig igen, och offline svarar ärendet. Guiden har fått *UTXO-modellen* och *Adresstyper*. Inget gränssnitt.
- **Etapp 6, Clustering** (2026-11-07, DEV-LOG): fyra heuristiker som steg med namn och version (CoinJoin stoppar,
  common-input-ownership, växeladress med tecken och tröskel, peel chain som bara föreslår), slutsatser och
  uppbrytningar förseglade, kluster som förklarar varje adress och uppbrytningar som håller. Guiden har fått fyra
  avsnitt. Inget gränssnitt.

## Nattkörningen 2026-11-07

Användaren sover. Huvudsessionen (`session_013CLefr2kGog3dCFdfqN1yx`) orkestrerar, och varje etapp byggs i en egen ny
session. Etapperna i ordning; en etapp som är klar markeras här av huvudsessionen.

| Etapp | Innehåll | Läge |
| --- | --- | --- |
| 2 | **Security:** huvudnyckeln (PBKDF2-SHA512, 600 000 iterationer), datanyckeln (AES-256-GCM) som lindas in, valvet i `%LOCALAPPDATA%\Chainsaw\` för API-nycklar, operatörsprofilen, låst och upplåst läge | Klar |
| 3 | **Cases:** ärendefilen `.cut` (SQLite3 Multiple Ciphers, nyckel ur valvet), schemamigreringar per modul, journalen och hashkedjan som bara fylls på, `/verify` som tjänst | Klar |
| 4 | **Network:** utgångarna som modell och `IExitHttp`, som **vägrar** när ingen utgång är ansluten (princip 4); WireGuard-tunneln själv senare | Klar |
| 5 | **Chains:** Bitcoin-modellen (block, tx, inputs, outputs, adresser i `Satoshi`), `IBitcoinSource`, Esplora-källan genom `IExitHttp`, varje råsvar sparat med SHA-256 och blockhöjd i ärendet, offline ur ärendet; tester mot syntetiska svar | Klar |
| 6 | **Clustering:** heuristikerna common-input-ownership v1, CoinJoin v1 (stoppar sammanslagning), växeladress v1 och peel chain v1, med namn, version och tx; kluster med sammanslagningar och uppbrytningar som minns; tester mot ett facit av syntetiska mönster | Klar |
| 7 | **Tracing:** grafen som modell (aktörer, adresser, tx, kanter, kant till annan kedja), spårningsmetoderna FIFO och haircut, slut på spåret med orsak; tester | Väntar |
| 8 | **Attribution:** etiketter med ursprung, OFAC-listans kryptoadresser ur en sparad fil, etikettbiblioteket som smittar mellan ärenden (CLAUDE.md, *Etiketter smittar*); tester | Väntar |

Gränssnittet byggs inte i natt utöver det som redan är godkänt. Huvudsessionen skissar i slutet av natten det som behövs
härnäst (förstagångsinställningen och Nytt ärende) så att användaren kan välja på morgonen.

### Så byggs etapperna

1. Läs `CLAUDE.md`, denna fil och `docs/DEV-LOG.md`. Installera .NET med `apt-get update && apt-get install -y
   dotnet-sdk-10.0`. Hämta grenen `claude/busy-cray-wj01k4` och arbeta på den.
2. Bygg bara etappens innehåll. Modulär monolit: publika typer i `*.Contracts`, `internal` i implementationen, moduler
   beror bara på varandras Contracts. Varje typ och metod har XML-dokumentation (också `internal` och `private`). Koden
   och kommentarerna på engelska, professionell C#, rader under ungefär 120 tecken.
3. Skriv tester för allt som byggs (xUnit, i modulens testprojekt). Heuristiker testas mot ett facit av syntetiska tx.
   Inga riktiga adresser eller ärenden i testerna; nätet används aldrig i testerna.
4. `dotnet build Chainsaw.slnx` utan varningar och `dotnet test Chainsaw.slnx` gröna.
5. **Oberoende granskning:** starta en granskande agent (Agent-verktyget) som läser diffen mot principerna i CLAUDE.md
   och letar fel, och märker fynden C (kritiskt), I (viktigt) eller M (mindre). Rätta alla C och I; M rättas eller skrivs
   i `docs/TODO.md`. Granska igen tills inga C eller I återstår.
6. Skriv en post i `docs/DEV-LOG.md` (egna beslut, avsteg, det som återstår), uppdatera tabellen ovan (bara den egna
   etappens rad: *Klar*), lägg öppna punkter i `docs/TODO.md` och det som ska provas på Windows i
   `docs/TESTER-INFOR-1.0.md`.
7. Committa med ett beskrivande meddelande och pusha till `claude/busy-cray-wj01k4`.
8. Skicka en rapport till huvudsessionen med `send_message` (session `session_013CLefr2kGog3dCFdfqN1yx`): vad som byggts,
   antal tester, granskningens resultat och commit. Stötte etappen på ett beslut som bara användaren kan ta: välj det
   säkraste, skriv det i TODO under *Väntar på beslut* och säg det i rapporten.

## Nästa steg

1. **Prova skalet på Windows** efter `docs/TESTER-INFOR-1.0.md` och skicka skärmdumpar.
2. **Låsskärmen** (ID7: kättingen i tecken och teckenfältet) och förstagångsinställningen med huvudnyckeln.
3. **MVP** (överenskommen 2026-11-06): Bitcoin via Esplora genom en utgång, ett ärende, mata in en adress, expandera grafen
   för hand, automatisk klustring med förklaring, egna etiketter, export av ett enkelt underlag med hashkedja.
