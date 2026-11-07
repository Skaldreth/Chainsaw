# Chainsaw – överlämning

Senast uppdaterad: 2026-11-07.

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
- **Etapp 7, Tracing** (2026-11-07, DEV-LOG): grafen som modell (aktörer, kluster, tx med flera ägare, kanter,
  hopp och tid, kant till annan kedja), expansion genom `IBitcoinSource`, FIFO och haircut som aldrig skapar eller
  tappar pengar, slut på spåret med orsak, länkar till andra kedjor förseglade, och grafen visas ur ärendet utan att
  något skickas (`LookupMode.CaseOnly` i Chains). Guiden har fått två avsnitt. Inget gränssnitt.
- **Etapp 8, Attribution** (2026-11-07, DEV-LOG): etiketter med ursprung, förseglade och tillbakatagna med journal,
  OFAC SDN ur en sparad fil med hash och datum, det krypterade etikettbiblioteket som smittar mellan ärenden utan
  ärendenummer, anteckning eller skäl, och etiketter som avslutar spåret. Guiden har fått *Etiketter och varifrån de
  kommer*. Inget gränssnitt.
- **Etapp 9, återställningsnyckeln** (2026-11-07, DEV-LOG): ECIES P-384-kuvert i varje ärendefil för den inbyggda
  publika nyckeln (ingen nyckel är inbyggd än), och `tools/Chainsaw.Recovery` för administratören
  (`docs/ATERSTALLNINGSNYCKEL.md`). Inget gränssnitt.
- **Etapp 10, låsskärmen och förstagångsinställningen** (2026-11-07, DEV-LOG): teckenfältet med kättingen (ID7a),
  upplåsning genom `IVault`, förstagångsinställningens fyra steg (FS1a) genom Security, Network och Cases, och låsning
  med Ctrl+L, efter inaktivitet och när Windows låses. Appen startar i förstagångsinställningen eller på låsskärmen.
  **Inte provat på Windows**, se `docs/TESTER-INFOR-1.0.md`.
- **Etapp 11, ärenden i appen** (2026-11-07, DEV-LOG): Nytt ärende som panel (NC1a) med titel, startpunkter som känns
  igen per kedja och delningslösenord, kommandoraden med `/new`, `/open`, `/close`, `/verify`, `/lock` och `/help`
  (Tab fyller i), ärendet och utgången i titelraden, ärendets titel förseglad i Cases, och länken `chainsaw:` till ett
  enda Chainsaw per användare som väntar på upplåsning. **Inte provat på Windows**, se `docs/TESTER-INFOR-1.0.md`.

## Nattkörningen 2026-11-07 (klar)

Etapperna 2–8 byggdes var och en i en egen session, granskades oberoende tills inga C eller I återstod, och
kontrollerades av huvudsessionen efter varje etapp (bygget utan varningar, alla tester gröna). Efter etapp 8: **799
tester**. Detaljerna står i `docs/DEV-LOG.md`. Modulerna är byggda men **inte inkopplade i appen**; det kräver gränssnitt
som väntar på skisser (förstagångsinställningen FS1a/FS1b, Nytt ärende NC1a/NC1b). Besluten som väntar på användaren
står i `docs/TODO.md` under *Väntar på beslut*. Under natten byttes också loggan av användaren i en annan session (två
länkar som hakar i varandra, `assets/logo/`).

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

## Dagkörningen 2026-11-07

Samma arbetssätt som nattkörningen (*Så byggs etapperna*): en etapp i taget, var och en i en ny session, orkestrerad av
huvudsessionen (`session_013CLefr2kGog3dCFdfqN1yx`). Gränssnitt byggs efter godkända skisser: ID7 med kättingen i loggans
form (godkänd), FS1a och NC1a (valda), SH1b, GR3a, GR3c och GR4.

| Etapp | Innehåll | Läge |
| --- | --- | --- |
| 9 | **Organisationens återställningsnyckel:** kuvert i varje ärendefil (ECIES P-384 eller motsvarande) för en inbyggd publik nyckel, och verktyget `tools/Chainsaw.Recovery` (skapa nyckelpar, fingeravtryck, kontrollera, öppna ett ärende) | Klar |
| 10 | **Låsskärmen och förstagångsinställningen** (ID7, FS1a): teckenfältet och kättingen i tecken, upplåsning och låsning genom Security, automatisk låsning, förstagångsinställningens fyra steg | Klar |
| 11 | **Ärenden i appen** (NC1a): Nytt ärende som panel, öppna ärende och kommandon i kommandoraden (`/new`, `/open`, `/verify`, `/lock`), ärendet i titelraden, startpunkter som känns igen | Klar |
| 11b | **Undermenyer i pillret** (PN1, godkänd 2026-11-07, efter Gawks B9): en allmän kontroll för pillret där en knapp kan ha en undermeny som fälls ut som en del av knappen (flik förbi pillrets kant, rundat inre hörn, en kant och skugga, tangentbord och Esc). Första användningen är **Nytt**: Startpunkt (S), Etikett (E), Bevakning (B), Begäran (R), Importera sanktionslista … (I) och Ärende (Ctrl+N); det som kräver ärende tonat utan ärende, det som inte är byggt tonat | Väntar |
| 12 | **Grafen** (GR4, GR3a, GR3c): Tracing-modellen ritad som fri yta med växel till hoppkolumner, bildspråket, belopp på linjerna, expansion, FIFO/haircut, inspektören med *Varför?* | Väntar |

## Nästa steg

1. **Användarens val:** FS1a eller FS1b, NC1a eller NC1b, låsskärmens kätting (två varianter, TODO), och besluten i TODO
   under *Väntar på beslut* (nycklarna och kollegans fil, återställningsnyckel, var ärendena ligger, tröskelvärden m.fl.).
2. **Koppla in modulerna i appen** efter de valda skisserna: förstagångsinställningen och upplåsningen (Security),
   Nytt ärende och att öppna ett ärende (Cases), startpunkter i kommandoraden (Chains), grafen enligt GR4 (Tracing,
   Clustering, Attribution).
3. **WireGuard-tunneln** (Chainsaws eget hjälpprogram), så att något kan hämtas på riktigt.
4. **Prova på Windows** efter `docs/TESTER-INFOR-1.0.md`.
5. Senare moduler: Pricing (kurser), Monitoring (bevakning, också när appen är stängd), Requests (begäran till börser).
