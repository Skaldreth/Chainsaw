# Utvecklingslogg

En post per byggd etapp: egna beslut, avsteg och det som återstår.

## Etapp 1 (2026-11-07): skalet SH1b och temat

Byggt efter skissen SH1b och besluten om identiteten (ID5 B, Magenta, GR3a, GR3c).

- **Temat:** Gawks gråskala, accenten Magenta `#E070B0`, grafens färger efter vad som händer med pengarna, typsnitten
  (`Themes/Colors.xaml`), ikonerna för vyerna (`Themes/Icons.xaml`) och kättingloggan som en `DrawingImage`
  (`Themes/Logo.xaml`), exporterad ur skissens ritning så att appen och canvasen visar samma logga.
- **Skalet:** egen titelrad (logga, ärendet, utgången, fönsterknappar), ikonlisten med tooltips och prick för det som
  väntar, sidans huvud, en tom sida per vy och den flytande kommandoraden längst ner. Statusraden längst ner.
- **Kortkommandon** ur `ViewTable`: Ctrl+N, Ctrl+1–7, Ctrl+0, F1, Ctrl+, och Ctrl+K/Ctrl+F till kommandoraden.

**Egna beslut och avsteg från skissen:**
- Inget är påhittat: titelraden säger *Inget ärende öppet* och *Ingen utgång*, statusraden *Ingen källa ansluten*,
  bevakningens öga i titelraden är utelämnat tills bevakningen finns, och varje vy visar *Inte byggt än* i stället för
  skissens graf.
- Kommandoraden svarar *Kommandon och sökning är inte byggda än* på Enter och töms med Esc.
- Skuggan under kommandoraden ligger i ett eget lager, eftersom en effekt på raden gör texten suddig i WPF.
- `Sök` finns inte som ikon i listen (användarens beslut: sök bara längst ner); Ctrl+F leder till kommandoraden.

**Återstår:** låsskärmen med kättingen och teckenfältet (ID7), förstagångsinställningen, att prova allt på Windows.

## Etapp 2 (2026-11-07): Security, valvet

Modulen `Security` utan gränssnitt: `IVault` (skapa, lås upp, lås, byt huvudnyckel, läge och händelse), `IApiKeyStore`,
`IOperatorProfileStore` och `IPurposeKeys`, registrerade i `AddSecurityModule`.

- **Kryptografin:** en slumpad datanyckel (32 byte) lindas in med AES-256-GCM under en nyckel ur huvudnyckeln
  (PBKDF2-SHA512, 600 000 iterationer, 32 byte salt som är nytt för varje huvudnyckel). Innehållet krypteras med
  AES-256-GCM under en ny slumpad nonce vid varje sparning. Salt och iterationer står i filen, och associerad data binder
  varje låda till sin plats och formatversion.
- **Filen** `vault.json` har format, version, `kdf` och två krypterade lådor. Den skrivs till en `.tmp`, flushas till
  disk och flyttas över den gamla. En misslyckad skrivning lämnar både filen och minnet som de var.
- **Låst och upplåst:** datanyckeln ligger i en fäst (pinned) array och nollställs vid låsning och när containern
  stängs. Två lås: ett för filoperationerna och ett kort för läget i minnet, så att läget, läsningar och låsning aldrig
  väntar på PBKDF2. En låsning under en pågående upplåsning vinner.

**Egna beslut:**
- Innehållet krypteras med en **undernyckel** ur datanyckeln (HKDF-SHA256, `chainsaw/vault/content-key/v1`), inte med
  datanyckeln direkt, så att datanyckeln bara används för att härleda och syftesnycklarna (`chainsaw/purpose/v1/<syfte>`)
  aldrig kan sammanfalla med innehållsnyckeln. Syftets namn är begränsat till `a-z 0-9 / - . _`.
- Huvudnyckeln tas som `ReadOnlySpan<char>`, eftersom en `string` inte går att nollställa. Metoderna är synkrona (en
  span kan inte gå in i en async-metod), så appen kör dem utanför UI-tråden.
- En API-nyckel per tjänst (skiftlägesokänsligt); `ApiKey.ToString` döljer hemligheten.
- Iterationerna i filen används vid upplåsning; en fil med färre än förvalet lindas om vid nästa upplåsning. Filer över
  1 MiB och iterationer över 10 miljoner avvisas som skadade.
- Fel huvudnyckel och en ändrad inlindad nyckel går inte att skilja åt (GCM), och båda ger `WrongMasterKey`.
- Byte av huvudnyckel vägrar (`Corrupt`) om filen har bytts ut medan valvet var upplåst, så att senare sparningar inte
  krypteras med fel nyckel.

**Granskningen:** tre omgångar.
- Omgång 1 gav två I: PBKDF2 kördes under samma lås som läsningarna, och byte av huvudnyckel kunde ske mot en utbytt
  fil. Den gav också nio M.
- Omgång 2 gav ett C: en händelsehanterare som kastade fick datanyckeln att nollställas medan valvet var upplåst. Den gav
  också två I: händelsen höjdes under låset, och sparningen skrev till disk under läslåset.
- Omgång 3 hittade inga C eller I.

Alla C och I är rättade, och de flesta M. Kvar i TODO: klartext i minnet (strängar och en JSON-buffert som inte är fäst),
och att en sparning som en låsning hinner före inte säger det till anroparen.

**Återstår:** förstagångsinställningen och låsskärmen som använder valvet, låsning efter inaktivitet och när Windows
låses, och modellen för ärendefilens nyckel (TODO, *Väntar på beslut*).

## Etapp 3 (2026-11-07): Cases, ärendefilen, journalen och hashkedjan

Modulen `Cases` utan gränssnitt: `ICaseStore` (skapa, öppna, öppna med delningslösenord, lista), `ICase` (läs och
skriv i en transaktion, journalen, delningskuvert, eget kuvert, spara, stäng), `ICaseTransaction` (journal och
förseglade poster i samma transaktion), `ICaseSchemaContributor` (schemasteg per modul) och `ICaseVerifier` (det
`/verify` anropar), registrerade i `AddCasesModule`.

- **Ärendefilen** `<ärendenummer>.cut`: magi `CHSWCASE`, formatversion (högre vägras), ett okrypterat JSON-huvud med
  ärendets id och nyckelkuverten, och sedan databasen krypterad av SQLite3 Multiple Ciphers (ChaCha20-Poly1305, rå
  256-bitars nyckel som aldrig blir en sträng). Huvudet visar inget om ärendet utom id:t.
- **Nycklarna** (orkestrerarens beslut): en slumpad filnyckel per ärende. Kuvert med AES-256-GCM: ett eller flera för
  valv (syftesnyckeln `cases/envelope`, utan namn, så att filen inte visar vems den är) och högst ett delningskuvert
  (PBKDF2-SHA512, 600 000 iterationer, salt 32 byte; associerad data binder sort, ärende, salt och iterationer).
  Okända sorters kuvert sparas orörda. Databasens nyckel härleds ur filnyckeln med HKDF och ärendets id.
- **Schemasteg per modul:** körs en gång, i ordning, varje steg i en egen transaktion med sin version. Ett ärende från
  en nyare version vägras.
- **Förseglade tabeller** (journalen och senare till exempel hämtade råsvar): STRICT-tabeller med `sequence`,
  innehåll och `hash`, där hashen är SHA-256 över föregående hash, kedjans namn, numret och värdena i en entydig,
  typmärkt kodning. Triggers stoppar UPDATE och DELETE, kedjans huvud ligger i `chain_head`, och varje kedja
  registreras i ärendet så att också en okänd moduls kedjor skyddas och kontrolleras.
- **Journalen:** tid i UTC och lokal tid med tidszon, operatören ur valvet, en kod (`case.*` bara för Cases), en rad
  och valfria detaljer.
- **`/verify`:** varje sida i databasen (quick_check), ärendets uppgifter mot journalens första rad, triggers, varje
  kedja post för post och datorns ankare. Svaret säger var en kedja bryts och varför.

**Egna beslut och avsteg:**
- **Arbetskopia i stället för huvud i databasen.** SQLite3MC krypterar hela filen och har ingen plats för kuvert
  (`apndvfs` finns inte i paketet). Medan ärendet är öppet arbetar SQLite i en krypterad arbetskopia `.cut.work`
  bredvid, som skrivs tillbaka vid sparning och stängning (tmp, flush, flytt). En arbetskopia efter en krasch tas
  tillbaka med en rad i journalen, men bara om den hör till `.cut` som den ligger nu (`.cut.work.base` med SHA-256);
  annars *Konflikt* och båda lämnas orörda.
- **Ärendena ligger i `%LOCALAPPDATA%\Chainsaw\Ärenden`**, inte i Dokument, eftersom Dokument ofta synkas till
  OneDrive (princip 4). Står i TODO under *Väntar på beslut*.
- **Vakt i SQLite** (authorizer): mellan operationerna vägras allt; en modul får skriva bara i sina egna tabeller,
  aldrig ändra nyckel eller inställningar, bifoga databaser, skapa tillfälliga objekt eller savepoints; vakten gäller
  bara den tråd som arbetar. Moduler får kommandon och läsare som inte lämnar ut anslutningen och slutar fungera när
  arbetet är klart.
- **Ankare utanför ärendet:** varje valv sparar kedjornas huvuden per ärende i `%LOCALAPPDATA%\Chainsaw\Ankare`,
  autentiserade med en syftesnyckel. Når ärendet inte sina ankare när det öppnas (äldre kopia, kortad eller omskriven
  kedja, borttagna ankare) skrivs det i journalen (`case.anchors-replaced`) och `/verify` visar det därefter.
- **Låsning av valvet** spärrar öppna ärenden direkt och sparar och stänger dem i bakgrunden (princip 6).
- En ändrad sida i databasen upptäcks när den läses eller av `/verify`, inte vid varje öppning, så att stora ärenden
  öppnas snabbt.

**Granskningen:** fyra omgångar.
- Omgång 1 gav ett C (ett kommando som en modul behöll kördes med Cases rättigheter och kunde ta bort krypteringen
  med `PRAGMA rekey`) och sex I: en gammal arbetskopia skrevs över en nyare fil, okända modulers kedjor var oskyddade,
  index och triggers på skyddade tabeller, ingen upptäckt av tillbakarullning, Dokument i OneDrive, låsning på
  UI-tråden. Den gav också elva M.
- Omgång 2 gav ett C (en läsare som modulen lämnade öppen körde sina sista satser med Cases rättigheter och kunde
  förfalska en journalrad) och fem I: vakten delades mellan trådar, en misslyckad sparning blev konflikt, ankarna gick
  att ta bort och frös efter en avvikelse, öppningen skrev utan lås. Den gav också åtta M.
- Omgång 3 gav ett C (en moduls lista med värden kördes medan Cases förseglade) och två I: två misslyckade sparningar
  gav konflikt, och `/verify` visade inte en återställning som journalen redan skrivit in. Den gav också sju M.
- Omgång 4 gav tre C: en modul kunde försegla direkt i journalens kedja, parallella anrop på en transaktion kunde
  lämna vakten i Cases läge, och en bidragsgivare kunde svara annorlunda efter kontrollen (namn och kolumner). Rättat
  med kedjor per modul, transaktioner bundna till arbetets tråd och kopior av bidragsgivarna.
- Omgång 5 gav ett C: en läsare som stängdes från en undantagshanterare medan Cases förseglade körde sina sista satser
  med Cases rättigheter. Rättat: läsare och kommandon kör ingenting utanför arbetet, förseglingen har ett eget smalt
  läge, och en operation kan inte startas inifrån en annan.
- Omgång 6 gav ett I (en misslyckad BEGIN lämnade ärendet låst för alltid) och fyra M.
- Omgång 7 hittade inga C eller I; två M (byta namn på en tabell till en annan moduls förseglade tabell, och arbetets
  eget fel som skymdes) är rättade.

Alla C och I är rättade och har regressionstester (150 tester i modulen). Kvar i TODO: ankarfil som återspelas
(kräver en räknare i valvet), HMAC över hela filen, nyckelrotation, vem som förseglade en post, hur en konflikt och
ankarnas avvikelser visas i gränssnittet, och var ärendena ska ligga.

**Återstår:** gränssnittet för att skapa, öppna, dela och verifiera ärenden; att appen sparar ofta; `/verify` i
kommandoraden; att andra moduler (Chains i etapp 5) registrerar sina tabeller och förseglar sina råsvar.

## Etapp 4 (2026-11-07): Network, utgångarna och den enda vägen ut

Modulen `Network` utan gränssnitt: `IExitStore` (utgångarna och den valda), `IExitConnection` (ansluta, koppla ner,
läget och en händelse), `IExitConnector` och `IExitTunnel` (där tunneln kopplas in) och `IExitHttp`, den enda vägen ut
för andra moduler, registrerade i `AddNetworkModule`. Security fick `IVaultSecrets`, namngivna hemligheter i valvet.

- **Utgångarna** sparas som hemligheter i valvet (`network/exits/<id>`): namn, plats, sort och WireGuard-filen,
  krypterade tillsammans. Också den valda utgången ligger i valvet. Inget om utgångarna står i klartext.
- **Anslutningen** går genom `IExitConnector`. Tunneln ger en SOCKS5-port på loopback med ett slumpat användarnamn och
  lösenord. En port som inte är loopback, eller som saknar inloggningsuppgifter, vägras. Faller tunneln blir läget
  *misslyckades* (förbindelsen förlorad) direkt. Att välja en annan utgång eller ta bort den anslutna kopplar ner
  först, och ett pågående anslutningsförsök avbryts av en nedkoppling.
- **`IExitHttp`** vägrar med `ExitNotConnectedException` (svensk text) innan något byggs när ingen utgång är
  ansluten. Den skickar bara metod, adress, `Host`, en neutral User-Agent (`Mozilla/5.0`) och, när det behövs,
  `Accept` och en kropp. Den har inga cookies, ingen referer, inga omdirigeringar och inga spårhuvuden
  (`traceparent`). Svaret har råa bytes, status, medietyp och tider, och storleken och tiden är begränsade.
- **Ingen väg runt utgången**, i flera lager:
  - Proxyn går aldrig förbi.
  - Hanterarens enda socket kan bara gå till tunnelns port.
  - SOCKS5 skickar namnet, så datorn slår inte upp något själv.
  - HTTP/1.1 är låst (ingen QUIC över UDP).
  - TLS-kedjan byggs utan nedladdningar och spärrkontroll, och bara mot de rötter som redan finns installerade. Annars
    hämtar operativsystemet saknade certifikat och rötter direkt från datorns nät.
  - Adresser på datorn och i privata, reserverade eller dokumentationsnät vägras.

**Egna beslut:**
- **Bara https.** `http` vägras, så att utgången aldrig ser innehållet. Står i TODO under *Väntar på beslut*.
- **User-Agent `Mozilla/5.0`**, det mest allmänna värdet, så att en källa inte kan skilja Chainsaw från en annan
  klient.
- **Den valda utgången ligger i valvet**, inte i en fil i klartext. Bevakningsprocessen, som ska gå utan valvet, får
  sin konfiguration med DPAPI när den byggs.
- **WireGuard-filen måste ha `DNS =`**, så att namn kan slås upp inne i tunneln.
- **En låsning av valvet kopplar inte ner utgången.** Står i TODO under *Väntar på beslut*.
- **Ofullständiga certifikatkedjor misslyckas** och spärrar kontrolleras inte. Det är priset för att inget hämtas
  runt utgången.
- Ett fel i en hanterare av `StatusChanged` stoppar aldrig anslutningen. Händelserna kommer i ordning, och den
  senaste kommer alltid.

**Granskningen:** fem omgångar.
- **Omgång 1:**
  - Två I: `traceparent` skickades när diagnostik lyssnade, och tunnelns port saknade inloggning.
  - Tio M, bland annat händelser i fel ordning, en hanterare som kastade och lämnade tunneln igång, hemligheter som
    serialiserades två gånger, och återupptagna TLS-sessioner.
- **Omgång 2:**
  - Ett C: när en kedja saknade ett mellanliggande certifikat hämtade TLS det från certifikatets AIA-adress direkt
    från datorns nät. Det gick runt utgången och visade den riktiga adressen. Rättat med en kedjepolicy utan
    nedladdningar, och ett test med https genom den riktiga vägen som misslyckas utan rättelsen.
  - Två I: ett försök som väntade på sin tur avbröts inte av en nedkoppling, och https saknade test.
  - Åtta M.
- **Omgång 3:**
  - Tre I: en avbrytning just efter publiceringen lämnade en död utgång som *ansluten*, Windows automatiska
    uppdatering av rötter kunde hämta runt utgången, och namnuppslaget i tunneln stod inte i kontraktet.
  - Fyra M, bland annat adresser som bär IPv4 i IPv6 och HTTP/1.1 som inte var låst.
- **Omgång 4:**
  - Ett I: `Dispose` väntade inte på stopp som en nedkoppling hade börjat.
  - Sex M.
- **Omgång 5:** inga C eller I. Fyra M: ett stopp som hänger höll anslutningen för alltid, förlorad förbindelse
  visades först när stoppet var klart, och IPv6-dokumentationsnät och Teredo släpptes igenom. De är rättade. Det
  fjärde, luckor i `Dispose`, står i TODO.

Alla C och I är rättade och har regressionstester: 155 tester i Network och 93 i Security.

**Kvar i TODO:**
- tunneln själv, med namnuppslag i tunneln och helst i samma process (porten kan annars tas över);
- gränssnittet för utgångar;
- svarshuvuden som ursprung;
- rötternas begränsningar på Windows;
- kontrollen av `DNS =`;
- `Dispose` och anslutningsförsök som pågår.

**Återstår:** tunneln, gränssnittet och att Chains (etapp 5) gör sina uppslag genom `IExitHttp`.

