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
  Tunneln byggdes i etapp 13. Inget gränssnitt.
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
- **Etapp 10, låsskärmen och förstagångsinställningen** (2026-11-07, DEV-LOG): kättingen som blir DNA (ID8c, ersatte
  teckenfältet och ID7a samma dag),
  upplåsning genom `IVault`, förstagångsinställningens fyra steg (FS1a) genom Security, Network och Cases, och låsning
  med Ctrl+L, efter inaktivitet och när Windows låses. Appen startar i förstagångsinställningen eller på låsskärmen.
  **Inte provat på Windows**, se `docs/TESTER-INFOR-1.0.md`.
- **Etapp 11, ärenden i appen** (2026-11-07, DEV-LOG): Nytt ärende som panel (NC1a) med titel, startpunkter som känns
  igen per kedja och delningslösenord, kommandoraden med `/new`, `/open`, `/close`, `/verify`, `/lock` och `/help`
  (Tab fyller i), ärendet och utgången i titelraden, ärendets titel förseglad i Cases, och länken `chainsaw:` till ett
  enda Chainsaw per användare som väntar på upplåsning. **Inte provat på Windows**, se `docs/TESTER-INFOR-1.0.md`.
- **Etapp 11b, undermenyer i pillret** (2026-11-07, DEV-LOG): en allmän undermeny som fälls ut ur en knapp i pillret
  (PN1), med raderna ur `Navigation/SubmenuTable.cs`, och Nytt som första: Startpunkt, Importera sanktionslista och
  Ärende; Etikett, Bevakning och Begäran tonade som inte byggda. **Inte provat på Windows.**
- **Etapp 12, grafen** (2026-11-07, DEV-LOG): vyn Graf efter GR4, GR3a och GR3c, med fri yta och hoppkolumner,
  bildspråket, belopp med gallring, FIFO/haircut, expansion i bakgrunden, inspektören med *Varför?* och legenden bakom
  *?*. 1 227 tester. **Inte provat på Windows.** Tre val väntar på användaren (TODO, *Väntar på beslut*).

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
| 11b | **Undermenyer i pillret** (PN1, godkänd 2026-11-07, efter Gawks B9): en allmän kontroll för pillret där en knapp kan ha en undermeny som fälls ut som en del av knappen (flik förbi pillrets kant, rundat inre hörn, en kant och skugga, tangentbord och Esc). Första användningen är **Nytt**: Startpunkt (S), Etikett (E), Bevakning (B), Begäran (R), Importera sanktionslista … (I) och Ärende (Ctrl+N); det som kräver ärende tonat utan ärende, det som inte är byggt tonat | Klar |
| 12 | **Grafen** (GR4, GR3a, GR3c): Tracing-modellen ritad som fri yta med växel till hoppkolumner, bildspråket, belopp på linjerna, expansion, FIFO/haircut, inspektören med *Varför?* | Klar |

## Eftermiddagskörningen 2026-11-07

Samma arbetssätt, men etapper utan gränssnitt körs **parallellt**, var och en i en egen session på samma gren. Före push:
`git pull --rebase origin claude/busy-cray-wj01k4`, lös konflikter i dokumenten genom att behålla båda sidor, och bygg och
testa igen. Huvudsessionen skissar under tiden det som väntar på skiss.

| Etapp | Innehåll | Läge |
| --- | --- | --- |
| 13 | **WireGuard-tunneln:** Chainsaws eget hjälpprogram (`tools/Chainsaw.Tunnel`), kopplat bakom `IExitConnector`, bara `127.0.0.1`, konfigurationen ur valvet utan disk, namnuppslag i tunneln | Klar |
| 14 | **Kurser (Pricing):** kursen vid en tidpunkt i vald valuta (SEK som förval) genom `IExitHttp`, med källa och råsvar förseglat i ärendet, offline ur ärendet | Klar |
| 15 | **Datalagret och spårningen:** txid mot tx:ens bytes, utgångar som aldrig kan spenderas, adressens sammanfattning, spårningen i bakgrunden, gränser för expansionen, spårningsmetod, läge och zoom sparade i ärendet, etikettbiblioteket när ett ärende öppnas | Klar |
| 16 | **Grafen GR5 och IN1a:** startpunkten neutral med flagga, nodernas namn med adressen nedtonad, formulären Etikett (L), Bryt upp (U) och Annan kedja (K) i inspektören. Startad när 15 var klar | Klar |
| 17 | **Utgångarna UT1a:** rutan under utgången i titelraden, anslut och koppla ner, lägg till WireGuard-fil, vägen till källorna | Klar |
| 18 | **Guiden GD1b:** panelen bredvid arbetet ur `docs/guide/`, öppnad från *Varför?*, *?* och spårningsmetoden, och vyn Guide (F1) | Klar |
| 19 | **Inställningarna ST1a och Windows som utgång:** dialogen med Nätverk (Windows eller VPN), Låsning, Kurser, Profil och Om; `IExitHttp` genom Windows; utgångarnas ruta och titelraden i läget Windows | Klar |
| 20 | **Grafen och spårningen oberoende av kedjan:** belopp med kedja och tillgång, kanter som överföringar, spårningsmetoderna och heuristikerna bakom ett kontrakt per sorts kedja (UTXO eller konton); Bitcoin oförändrat, inget nytt gränssnitt. Parallellt med 19 | Klar |
| 21 | **Ta tillbaka och ångra (AN1b):** fliken Historik i inspektören, tillbakatagande som förseglad post i Attribution, Clustering och Tracing, biblioteket följer, ångrad uppbrytning med bekräftelse. Startad när 19 var klar | Klar |
| 22 | **Kontroller och förslag (KF1a) med områden:** ramverket för kontroller och förslag (analys med namn och version, förseglade), listan Ctrl+J, områdena och egna ytor (Z), först med CoinJoin, peel chain och mixerns kandidater. Startad när 20 var klar | Klar |
| 23 | **Ethereum och ERC-20 (TK1a), första delen:** kontraktet `IEthereumSource` och Blockscout-källan (EIP-55, ETH, interna tx, ERC-20 med kontrakt och decimaler, förseglat och offline), USDT och USDC med *Kan frysas*, FIFO och haircut för konton, kurser för ETH, tokenmärkena, Källor och Guiden | Klar |
| 23b | **Ethereum och ERC-20 (TK1a), andra delen:** grafen och inspektören för Ethereum (noder som `ChainAddress`, expansion genom `IEthereumSource`, spårning per tillgång med kontometoderna, märket och SEK i beloppet, swap-pillret, saldo per token och snöflingan), startpunkter för Ethereum, kurser för USDT och USDC. Kontoheuristiker och TRON efter den | Klar |
| 24 | **Källornas endpoints (KL1a):** sidan Källor i ST1a, lägga till, prioritera och ta bort endpoints per kedja, lägena Snabbast (prov vid start genom utgången), Prioriterad och Av, endpointen i varje förseglat råsvar. Startad när 20 var klar | Klar |
| 25 | **Endpointarna, andra delen:** rate limit per endpoint (egen takt, `429` och `Retry-After`, uppslagen spridda), snygg reserv mot de andra, ett blocks marginal utom för bevakningen, egen nod på det lokala nätet (märkt lokal, bara https). Startad när 23 var klar | Klar |
| 26 | **Bevakningen, första delen (BV1a, BV2a):** modulen Monitoring (bevakning med riktning, mottagare 1:n och avsändare n:1, tillgång, minsta belopp; förseglad i ärendet; frågar källan med `LookupMode.InStep` genom utgången; träffar förseglade), bevakning medan Chainsaw är öppet, vyn BV1a, högerklick *Bevaka …* i grafen och BV2a med animerade ringar vid träff | Klar |
| 27 | **Bevakningen, andra delen:** bakgrundsprocessen när Chainsaw är stängt (startar med Windows, vet bara adresserna och ärendets id under DPAPI, genom utgången, kö som förs in i ärendet när det öppnas, avisering som bara säger att en bevakning slagit till). Startad när 26 var klar | Klar |

## Nästa steg

**Läget 2026-11-08 natt.** Polerlistan i `docs/TODO.md` (*Polering först*) gäller fortfarande; punkt 31–39 är nya.
Klart och pushat i dag: startvyns högar (GR8a), Chainabuse som underleverantör (hämtningen, granskad), streck i
pixlar, inspektören bara när något är valt, pulsen borta från Nytt ärende och startpunkten, Graf-ikonen borta utan
ärende, foten utan *Utgång*.

**Agenter som arbetar** (i worktrees under `.claude/worktrees/`, committar men pushar inte; slå ihop med
`git merge worktree-agent-<id>`, behåll båda sidor i dokumenten, bygg, kör berörda tester och pusha):
- **Valvyn och högen byggs om efter EX2b** (`HeldBack2B.dc.html`): filterpanelen, ringen av prickar, Spiral/Tidsbåge,
  Lasso, nederlisten, kortleken med dimning. Rättar också granskningens fel i EX1/EX2b.
- **Granskning av alla byggda vyer mot skisserna**, rapport i scratchpadens `skissgranskning.md` (sessionens
  scratchpad). Blir en ordnad lista att bygga om efter.
- **Skisserna NC2a/NC2b (Nytt ärende med startpunkter som lista som förhandshämtar) och KF2a/KF2b (Kontroller och
  förslag)** ritas i scratchpadens `canvas/project/`; de läggs ut på canvasen under Ärenden och Kontroller och
  förslag, och byggs först när användaren valt.

**Väntar på användaren:** KL2a eller KL2b (sidan Källor); NC2 och KF2 när de är utlagda; när Chainabuse ska frågas
(knapp eller automatiskt, nyckelns gräns är 10 anrop i månaden); Ctrl+W.

**Godkänt och inte byggt, i ordning:** GR7a, KN1a, SO1a (soptunnan, också i startvyn), resten av EX2 efter
ombygget, grafens offlinetext för Windows utan nät, testärendet som är för trångt (punkt 34).

**Canvasen:** läs in den levande `project/canvas.json` med Artifact `read` innan något läggs ut, och slå ihop.
Artefakten är https://claude.ai/code/artifact/9dd2fd6d-b424-4bdb-aac7-38a36a2494d8 (samma som
https://claude.ai/artifact/LVMLEqgJteM68jjc7vuq1h). Provrendering: lägg `artifact-type/dc-runtime.js` som
`support.js` bredvid tavlorna och servera mappen över http.
