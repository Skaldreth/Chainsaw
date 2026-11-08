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


## Etapp 5 (2026-11-07): Chains, Bitcoin

Modulen `Chains` utan gränssnitt: modellen för Bitcoin i Contracts, `IBitcoinSource` och Esplora-källan, registrerade i
`AddChainsModule`.

- **Modellen (fakta, princip 2):** `BitcoinTransaction` (txid, block eller obekräftad, inputs, outputs, avgift, storlek,
  vikt, version, locktime), `BitcoinInput` (föregående txid och vout, adress, skripttyp, belopp, coinbase, sequence),
  `BitcoinOutput` (index, skripttyp, adress, belopp, skriptet, spenderad av), `BitcoinBlock`, `BlockReference` (höjd,
  hash, tid) som följer med allt som bygger på en bekräftad tx, och belopp alltid i `Satoshi`.
- **Adresser och txid:** `BitcoinAddress` kan bara skapas genom kontroll: Base58Check med kontrollsumma och känd
  versionsbyte, Bech32 för SegWit version 0 och Bech32m från version 1 (BIP 173 och 350), mainnet och testnet.
  `Txid`, `BlockHash` och `BitcoinStartPoint` (känner igen en startpunkt i kommandoraden eller en `chainsaw:`-länk).
- **Skripttypen läses ur skriptets byte** (P2PK, P2PKH, P2SH, P2WPKH, P2WSH, P2TR, okänd witness-version, bar multisig,
  OP_RETURN, icke-standard), inte ur källans etikett. Chainsaw räknar fram adressen ur skriptet och vägrar svaret om
  källans adress inte stämmer.
- **Tolkningen kontrollerar att svaret går ihop:**
  - inputs = outputs + avgift, och en coinbase har ingen avgift;
  - varje belopp och varje summa ryms i 21 miljoner BTC;
  - vikten passar storleken;
  - en bekräftad tx har höjd, hash och tid;
  - sidan för en adress har bara tx som rör adressen;
  - svaret är den tx eller det block som efterfrågades.
- **Esplora genom `IExitHttp`, bara den vägen ut.** Bas-URL i `BitcoinSourceOptions`: mempool.space som förval,
  blockstream.info som val, och bara `https`. Förfrågan har bara sökvägen (tx, adress, block), aldrig något om ärendet.
- **Varje råsvar förseglas** i `chains.raw_responses` innan något byggs på det, med dessa uppgifter:
  - kedja och nät, källa, URL, sort och ämne;
  - status och medietyp;
  - tid för förfrågan och svar i UTC, lokal tid med tidszon;
  - utgångens id och vem som hämtade (operatörsprofilen);
  - SHA-256 och de råa byten.

  Också svar med felstatus och svar som inte går att tolka förseglas, men de används aldrig. Två register bredvid
  (som inte är förseglade) säger var svaren finns. Ett svar som läses tillbaka kontrolleras mot sin SHA-256, och mot den
  sort och det ämne registret pekade på.
- **Det som inte kan ha ändrats hämtas aldrig igen:**
  - en tx med minst sex bekräftelser;
  - ett block (efter hash);
  - svaret om vem som spenderar utgångarna, när varje utgång är spenderad djupt i kedjan;
  - en äldre sida för en adress, när alla dess tx är djupa.

  Djupet räknas mot den högsta höjd ärendet känner till. Höjden hämtas högst en gång i minuten när den behövs för att
  avgöra djupet. Adressens första sida, obekräftade tx och höjden hämtas alltid om.
- **Offline (princip 9):** utan utgång svarar ärendet. Det som kan ha ändrats får läget *FromCaseMayBeOutdated* och en
  text om när det hämtades, och det som saknas får *Offline* utan värde. Inget hittas på, och inget skickas.
- **Guiden:** `docs/guide/utxo-modellen.md` och `docs/guide/adresstyper.md`.

**Egna beslut:**
- **Ingen rad i journalen per uppslag.** Den förseglade tabellen har redan vem, när och vad, och en rad per uppslag
  skulle dränka journalen.
- **Inget hämtas utan operatörsprofil** eller med låst valv: det måste gå att säga vem som hämtade.
- **`IBitcoinSource` tar ärendet som argument** i varje anrop, i stället för att hålla ett *aktuellt ärende*.
- **`/tx/:txid/outspends` är med**, fast uppdraget inte nämnde den, eftersom kedjan själv inte säger vem som spenderar
  en output och spårning framåt behöver det. En tx får uppgiften ur ärendet (utan nytt uppslag), och en varning när
  den kan ha ändrats.
- **Sidbläddringen går tills en sida är tom**, i stället för att lita på källans sidstorlek (25 hos Esplora). Det
  kostar ett uppslag till per adress men skär aldrig av historiken.
- **404 med ett svar i ärendet** ger ärendets svar med en varning, inte *finns inte*. En tx som fallit ur mempoolen
  syns då som "kan ha ändrats".
- **Testdatan** görs av `tests/Chainsaw.Modules.Chains.Tests/Esplora/generate.py`, som har en egen Bech32 och Base58
  i Python. Det gör att testerna också jämför C#-koden mot en oberoende implementation. Hasharna är påhittade.

**Granskningen:** tre omgångar.
- **Omgång 1** gav två I:
  - Ett svar med ett tal utanför sitt intervall (till exempel `sequence: -1`) gav ett annat undantag än tolkningens
    eget. Undantaget gick ut ur källan, och svaret förseglades aldrig.
  - HTTP 400 lästes som *finns inte* och dolde ärendets svar.

  Den gav också sex M:
  - nätet saknades i registren;
  - ett sparat svar kontrollerades inte mot det registret pekade på;
  - gamla uppgifter om spenderade utgångar visades utan varning;
  - en tx med OP_RETURN får aldrig alla utgångar spenderade (står i TODO);
  - sidstorleken avgjorde var historiken slutade;
  - ett odokumenterat undantag.
- **Omgång 2:** inga C eller I. Tre M (undantag vid läsning ur ärendet, 2xx som inte var 200, långa rader) är rättade.
- **Omgång 3:** inga fynd.

Alla C och I är rättade och har regressionstester: 133 tester i Chains, 564 i hela lösningen.

**Kvar i TODO:**
- txid räknat ur tx:ens egna bytes och blockets huvud mot dess hash;
- omorganisation av kedjan;
- adressens sammanfattning;
- utgångar som aldrig kan spenderas;
- att jämföra flera källor.

**Återstår:** att kommandoraden och grafen använder källan, kurserna och den riktiga tunneln.

## Etapp 6 (2026-11-07): Clustering, heuristikerna och klustren

Modulen `Clustering` utan gränssnitt: `IClusteringHeuristic` (en analys som ett steg med namn och version),
`IClusteringService` (kör analyserna, sparar slutsatserna, bygger klustren, uppbrytningar och *varför?*) och
`ClusteringSchema`, registrerade i `AddClusteringModule`. En ny analys, till exempel en lokal modell senare
(princip 5), läggs till genom att registrera ännu en `IClusteringHeuristic`.

- **Fyra analyser i v1**, som körs i ordning på varje tx och vägs mot varandra innan något sparas:
  - **`coinjoin v1`** letar efter lika stora outputs (inte damm) till olika adresser, och inputs från flera adresser
    och flera tidigare tx.
    - Whirlpool, Wasabi och JoinMarket (med ett udda belopp) är *kända*. Spåret tar slut med orsak, och allt annat i
      tx:en stoppas.
    - Övriga lika belopp är en *möjlig* CoinJoin. Allt i tx:en blir då förslag, och spåret går vidare.
  - **`common-input-ownership v1`:** alla inputs i en tx slås ihop.
  - **`change-address v1`:** fem tecken räknas, men bara när de skiljer outputarna åt:
    - samma skripttyp som inputs;
    - de andra beloppen är jämna;
    - ny adress när de andra är kända;
    - optimal växel;
    - tillbaka till en inputs adress.

    Måttet är *n av 5 tecken, m mot*. Växeln slås ihop bara vid minst tre tecken, inget emot och två outputs
    (`AutomaticMergeSigns`, `AutomaticMergeOutputs`); annars blir det ett förslag.
  - **`peel-chain v1`** känner igen minst tre kända hopp i rad (en input, två outputs, den stora minst fyra gånger den
    lilla). Den **föreslår** det som går vidare som växel och sparar de andra hoppens txid.
- **Vägningen per tx:**
  - En CoinJoin sänker de andra analysernas förslag.
  - Två analyser som pekar ut olika växel, i ett förslag eller i ett fynd, tar ut varandras sammanslagningar.
- **Förseglat** i `clustering.evidence` och `clustering.splits`. Varje post har analys och version, tx, adresser,
  styrka, skäl, varför styrkan sänktes, fynd (JSON med enum som namn), underlag (de andra tx:erna), fingeravtryck och
  körningens journalrad.
  - En körning ger en rad i journalen (`clustering.analysed`), en uppbrytning likaså (`clustering.split`).
  - Det som räknas läses bara ur de förseglade tabellerna: per tx och analysnamn räknas den högsta versionen och dess
    nyaste körning. Äldre resultat står kvar förseglade.
- **Klustren byggs varje gång de efterfrågas** ur förslagen, i den ordning resultaten först dök upp, med alla
  uppbrytningar.
  - Varje medlem säger vilken länk som förde in den och från vilken medlem, och `ExplainConnection` ger vägen länk för
    länk.
  - En sammanslagning som skulle föra ihop två uppbrutna adresser slår inte ihop något av sina adresser. Kommer den
    från den heuristik uppbrytningen nämner vägras den (`RefusedBySplit`); kommer den från en annan syns den som
    förslag (`SuggestedAfterSplit`).
- **Guiden:** `common-input-ownership.md`, `vaxeladress.md`, `peel-chain.md` och `coinjoin.md`.

**Egna beslut:**
- **Klustren lagras inte**, bara slutsatserna och uppbrytningarna. Då kan inget kluster ändras utan att en förseglad
  post gör det, och en uppbrytning gäller också slutsatser som kommer senare.
- **Peel chain v1 slår aldrig ihop** (granskningen visade att formen inte räcker för att se var ägaren byter). Står i
  TODO som fråga till kryptospåraren.
- **Tröskelvärdena** (tre tecken, tre hopp, fyra gånger, 0,001 BTC som jämnt belopp) är valda av sessionen och står i
  TODO under *Väntar på beslut*.
- **En möjlig CoinJoin gör förslag**, en känd stoppar. Reglerna lutar åt att hellre se en CoinJoin än att slå ihop
  främlingar, men bara kända mönster avslutar spåret.
- **En uppbrytning nämner heuristiken** (namnet, alla versioner) och måste vara en som körs. Den får göras i förväg,
  också mellan adresser som inte är i samma kluster.
- **Underlaget kommer från anroparen:** `Analyse` tar de tx som ska analyseras och använder dem som det analysen vet.
  - Varje slutsats sparar vilka andra tx den bygger på.
  - En tx:s resultat byts ut tillsammans, och bara när körningen känner till hela underlaget; annars hålls de tillbaka
    (`HeldBack`), och det står i journalen.
- **En analys som misslyckas**, eller ger ett resultat som inte går ihop, gör att inget om den tx:en sparas. En
  misslyckad CoinJoin-analys får aldrig låta de andra slå ihop en CoinJoin.
- **Ett resultat som inte säger något** sparas inte första gången, bara när det ersätter ett som sade något.

**Granskningen:** tre omgångar.
- **Omgång 1** gav ett C och sex I:
  - **C:** peel chain slog ihop offret som betalade in i kedjan och börsens insättningsadress i dess slut, och genom
    börsens sweep alla andra kunder.
  - **I:** oenighet om växeln räknades inte när växeln bara stod i ett fynd.
  - **I:** lika jämna belopp (en batch med uttag) var en *känd* CoinJoin som avslutade spåret.
  - **I:** en ny versions CoinJoin nådde inte en äldre versions sammanslagningar.
  - **I:** en felaktig analys kunde försegla ett förslag som bröt alla kluster för alltid.
  - **I:** vilken länk som gav vika för en uppbrytning berodde på ordningen förseglingen skedde i.
  - **I:** slutsatser som byggde på andra tx sparade inte vilka, och en snävare körning kunde ta tillbaka dem.

  Den gav också sju M. Alla är rättade eller står i TODO.
- **Omgång 2** gav ett C och två I:
  - **C:** ett offers vanliga betalningar fick betalningen till bedragaren att se ut som ett inre hopp. Rättat genom
    att peel chain bara föreslår.
  - **I:** en snävare körning kunde ta tillbaka en oenighet och återställa en sammanslagning. Rättat: en tx:s resultat
    byts ut tillsammans.
  - **I:** en ny sorts fynd stoppade hela körningen. Rättat: det fäller bara den egna tx:en.
- **Omgång 3:** inga C eller I. M1 (oavgjort utan underlag), M3 (andra fel vid skrivning av fynd) och M4 (underlag
  från en version som inte räknas) är rättade; M2 och M5 står i TODO.

Alla C och I är rättade och har regressionstester: 86 tester i Clustering, 648 i hela lösningen. Testerna går mot ett
facit av syntetiska mönster:
- betalning med växel och med svaga tecken;
- konsolidering;
- börsens sweep med insättare;
- batchutbetalning;
- lika stora uttag;
- Whirlpool, Wasabi 1 och 2, JoinMarket och möjlig CoinJoin;
- damm;
- peel chain med offret före och börsen efter;
- uppbrytningar som hålls.

**Kvar i TODO:**
- att godkänna förslag och ångra uppbrytningar;
- underlaget ur ärendet, med länk till råsvaret och blocket;
- fler tecken och CoinJoin-mönster;
- att dra tillbaka en version;
- stora ärenden.

**Återstår:** att grafen kör `Analyse` när tx hämtas och visar kluster, förslag och *varför?* (skissas först).

## Etapp 7 (2026-11-07): Tracing, grafen som modell och spårningsmetoderna

Modulen `Tracing` utan gränssnitt: `ITracingService` (startpunkter, expansion, grafen, länkar till andra kedjor,
platser och spårning), `ITracingMethod` (en metod som ett steg med namn och version) och `ITrailEndClassifier` (det
som känner igen var spåret tar slut), registrerade i `AddTracingModule`.

- **Grafen** (`TraceGraph`) byggs varje gång ur ärendet:
  - en nod per aktör: kluster ur Clustering, en ensam adress, en tx vars inputs har flera ägare (till exempel en
    CoinJoin), ett mynt utan adress och en tx på en annan kedja;
  - en kant per par av aktörer med överföringarna summerade, deras tx, block och tid;
  - en kant *fortsätter på annan kedja* till tx:en som användaren knutit för hand;
  - varje nod vet sitt hopp från startpunkten (negativt före den), när pengarna först kom dit, hur långt den är
    expanderad, varför spåret tar slut där och var användaren ställt den i fri yta och i hoppkolumner;
  - varje kant vet om den går framåt, i sidled eller tillbaka (en båge).
- **Expansionen** är ett steg som läser genom `IBitcoinSource`: en adress historik (nyaste först, högst 50 tx som
  förval) eller en tx och de tx som spenderar dess utgångar, och för varje ny tx vart dess utgångar gick. Efteråt körs
  klustringen över grafens tx. Grafen växer bara så (princip 7).
- **Att visa grafen skickar ingenting** (princip 4 och 6): den läses med det nya läget `LookupMode.CaseOnly` i Chains.
  Det som kan ha ändrats säger det, och det som saknas är *inte expanderat ännu*.
- **FIFO v1 och haircut v1:**
  - FIFO lägger inputs efter varandra på en linje och klipper av outputs i ordning, med avgiften sist.
  - Haircut fördelar i proportion, i hela satoshi, med största rest för det som blir över.
  - Positionerna (`TaintSet`) gör att FIFO håller ihop pengar genom flera hopp.
  - Varje tx kontrolleras: det som gick in är exakt outputs plus avgift. Summan av alla slut kontrolleras mot det
    spårade. En metod som skapar eller tappar pengar vägras.
- **Ett spårat belopp är en slutsats** (`TracedAmount`): metod, version, tx det räknades genom och `Explain()`, som
  säger att det är en bedömning. Varje slut, mynt, kant och tx säger också om något det bygger på kan ändras (en
  obekräftad tx eller ett svar som inte kunde frågas om).
- **Slut på spåret med orsak** (`TrailEndReason`):
  - Inte slut än: *inte expanderad ännu*, *ligger kvar* och *uppgifterna går inte ihop*.
  - Riktiga slut: avgift till minerare, kan aldrig spenderas (OP_RETURN), CoinJoin (känd, ur Clusterings slutsats),
    mixer, privacy coin, annan kedja (med fortsättningen när den är knuten), börs och okänd tjänst.
  - Mixer, börs, brygga och okänd tjänst kommer från etiketter (Attribution) genom `ITrailEndClassifier`.
- **Lagring i ärendet:**
  - Vanlig data som får ändras: startpunkter, expansioner och platser. Startpunkter får en rad i journalen.
  - Förseglat: länkarna till andra kedjor och när de tas tillbaka (`tracing.cross_chain`), med en rad i journalen.
  - Inget som räknas fram ur kedjan sparas.
- **Guiden:** `sparningsmetoder.md` (*Spårningsmetoder: FIFO och haircut*) och `sparet-tar-slut.md` (*Varför ett spår
  tar slut*).

**Egna beslut:**
- **LIFO och poison byggs inte.** Kontraktet tar emot dem, men LIFO behöver en definition och poison ger inga belopp
  som går jämnt ut. Står i TODO.
- **`LookupMode.CaseOnly` och `LookupStatus.NotInCase` i Chains** (granskningen omgång 1): utan dem frågade varje
  visning av grafen nätet om varje tx och förseglade svaren igen.
- **En tx med inputs från flera aktörer är en egen nod**, eftersom det inte går att läsa vilken input som betalade
  vilken output. Växel tillbaka till samma aktör blir ingen kant.
- **Spåret följs inte in i en börs egna flyttar** (till exempel börsens sweep), eftersom pengarna då blandas med andra
  kunders.
- **En startpunkt i form av en tx** lägger aktörerna som betalar in i hopp 0. En startpunkt längre fram i samma flöde
  som en annan vägras när man spårar, så att inget räknas två gånger.
- **Ett mynt fortsätter på en annan kedja på ett ställe i taget.** En ny länk görs efter att den gamla tagits tillbaka.
- **En expansion av samma adress eller tx igen behåller det den förra tog med sig**; grafen krymper aldrig av sig själv.
- **En bekräftad tx som spenderar ett mynt vinner över en obekräftad**, som kan ha ersatts (RBF).
- **Spårningen är synkron** och snabb för hundratals tx. Gränssnittet anropar den utanför UI-tråden. En
  `TraceAsync` står i TODO.

**Granskningen:** fyra omgångar.
- **Omgång 1** gav tre I och nio M:
  - **I:** en ny expansion med lägre gräns tog tyst bort tx ur grafen.
  - **I:** varje visning frågade nätet om allt igen.
  - **I:** linjen in i en CoinJoin visade inget spårat belopp.
  - **M:** motstridiga uppgifter, obekräftade spenderare, `with` på grafen, långsamma sökningar, sidbläddring som inte
    går framåt, tom adress, coinbase, utbredning och dokument.
- **Omgång 2** gav ett I och sex M:
  - **I:** en ersatt obekräftad tx kunde vinna över den bekräftade, och slutet sade inte att det kunde ändras.
  - **M:** dubbla uppslag, gamla sidor, linjer in i en tx vid motstridiga uppgifter, guiden, dokumentation och
    tillbakatagna länkar.
- **Omgång 3** gav två I och tre M:
  - **I:** ett slut vid en tx som kan ersättas sade inte att det kunde ändras.
  - **I:** två länkar från samma mynt dubblade beloppet på linjerna.
  - **M:** tre mindre fynd. En obekräftad spenderare i grafen vinner fortfarande över ett sparat *ospenderad* och
    märks *kan ha ändrats*; granskaren godtog det.
- **Omgång 4:** inga C eller I. Tre M rättades:
  - ett slut vid en CoinJoin pekar på tx-noden;
  - kanter och tx säger om de kan ändras;
  - kanten till en annan kedja är en bedömning.

Ingen omgång hittade ett belopp som skapades eller försvann. Alla C och I är rättade och har regressionstester: 67
tester i Tracing (bland dem slumpade flöden för båda metoderna), 135 i Chains och 715 i hela lösningen.

**Kvar i TODO:**
- LIFO och poison;
- hur långt en expansion får breda ut sig;
- att fråga om alla mynt som kan ha flyttats;
- `TraceAsync`;
- stora grafer;
- tidsfönster för en adress historik;
- att spara vald metod;
- att hitta fortsättningen på en annan kedja själv.

**Återstår:** grafen (GR4) som ritar modellen, och Attribution som ger börser, mixers och bryggor.

## Etapp 8 (2026-11-07): Attribution, etiketter med ursprung, sanktionslistan och etikettbiblioteket

Modulen `Attribution` utan gränssnitt: `IAttributionService` (etiketter i ärendet, tillbakatagning, sanktionskontroll),
`ISanctionLists` (importerade listor), `ILabelLibrary` (biblioteket som smittar) och en `ITrailEndClassifier`,
registrerade i `AddAttributionModule`.

- **Etiketter** (`LabelEntry`, sammanslagna till `Label`): på en adress eller på klustret som håller den, med sort
  (de sjutton sorterna ur CLAUDE.md, med färggrupp) och namn, och alltid ett ursprung (`LabelOrigin`): källa (egen,
  sanktionslista, öppen källa, beteende, annat ärende), ursprunglig källa, källans namn och version, vem, när, varför
  och anteckning. `Label.Explain()` börjar alltid med *Bedömning, inte ett faktum*.
- **Förseglat:** varje etikett och varje tillbakatagning är en post i kedjan `attribution.labels` med en rad i
  journalen. En etikett ändras aldrig; den tas tillbaka och står kvar som tillbakatagen.
- **Sanktionslistan:** OFAC:s `sdn.xml` läses ur en fil användaren importerar (ingen DTD, inga externa entiteter, högst
  256 MB). Filen sparas byte för byte i `Sanktionslistor` med SHA-256 i namnet, och läses om och kontrolleras mot hash
  och datum innan en träff tas ur den. Varje träff blir *Sanktionerad* med postens namn, uid och program, och listans
  datum och hash som version. Adresser för andra valutor räknas men slås inte upp än.
- **Biblioteket** (`Etiketter\bibliotek.bin`, AES-256-GCM med syftesnyckeln `attribution/library`): `Absorb` tar in
  ett ärendes etiketter och ersätter det som kom därifrån; `ShareInto` ger ärendet bibliotekets etiketter för dess
  adresser, förseglade som *annat ärende*. Ärendenummer, anteckning och skäl stannar i biblioteket; in i ett annat
  ärende går bara vem, när, etiketten och sortens källa.
- **Bekräftelse:** samma etikett (adress, sort, namn) slås ihop. Oberoende ursprung räknas per person (operatörsprofil),
  per sanktionslista och per analys eller öppen källa, så att samma person två gånger eller en nyare version av samma
  lista inte blir en bekräftelse.
- **Spårets slut:** blå sorter ger *Börs, uttag funnet*, gula samt darknet och garantimarknad den nya orsaken
  `TrailEndReason.Service` (*Tjänst*, tillagd i Tracing.Contracts), mixer och CoinJoin sina egna, swap och brygga
  *Fortsätter på annan kedja*. Offer, sanktionerad och okänd avslutar inte spåret.
- **Guiden:** `etiketter.md` (*Etiketter och varifrån de kommer*), och `sparet-tar-slut.md` har fått *Tjänst*.

**Egna beslut:**
- **Sekretessen** byggs efter förslaget i TODO (*Väntar på beslut*): ärendenummer och anteckning till biblioteket men
  inte vidare. Också skälet stannar, och hela operatörsprofilen följer med som *vem*. Står kvar som fråga.
- **Offret sprids aldrig**, och inte heller någon etikett på offrets adresser eller kluster (till exempel offrets eget
  konto på en börs). Det går före *Sprid inte*.
- **En etikett som tas tillbaka där den sattes dras tillbaka överallt:** den ges aldrig igen, och kopior i andra
  ärenden tas tillbaka med en rad som inte säger något om det första ärendet. *Sprid inte* i efterhand görs så.
- **Ny orsak `Service`** i Tracing, eftersom *Okänd tjänst* vore fel för ett casino.
- **Biblioteket är en egen krypterad fil** med en syftesnyckel ur valvet, inte en hemlighet i valvet, eftersom valvets
  hemligheter har en gräns på 256 KiB.
- **Adresserna kommer från anroparen** (`ShareInto` och `CheckSanctions`): Attribution kan inte fråga Tracing om
  grafen, eftersom Tracing frågar Attribution om spårets slut.
- **Sanktionslistan som är aktuell** är den med senast publiceringsdatum, inte den senast importerade.

**Granskningen:** tre omgångar.
- **Omgång 1** gav sex I och åtta M:
  - **I:** etiketter på offrets adress (till exempel offrets börskonto) spreds;
  - **I:** en tillbakatagning stoppade inte kopior som redan nått andra ärenden;
  - **I:** samma etikett satt två gånger blev en bekräftelse;
  - **I:** två versioner av samma lista blev en bekräftelse;
  - **I:** en kopia av en sanktionsträff hindrade ärendets egen kontroll;
  - **I:** en lista som importerades under en kontroll kunde stå som ursprung för en annan listas träff.
- **Omgång 2** gav tre I och tre M:
  - **I:** närhet till ett offer drog tillbaka kopior överallt, för alltid och med fel skäl;
  - **I:** ärendets egen sanktionsträff visades inte bredvid en kopia;
  - **I:** skyddet mot dubbletter tappade ett ändrat *Sprid inte*.
- **Omgång 3:** inga C eller I. Fyra M är rättade:
  - en etikett som hölls utanför och sedan togs tillbaka dras tillbaka;
  - namnar räknas inte som samma person;
  - varje träff har sitt datum i journalen;
  - bibliotekets post nära ett offer tas bort.

Alla C och I är rättade och har regressionstester: 86 tester i Attribution och 799 i hela lösningen. Kvar i TODO:
- EU-listan och *Kan frysas*;
- öppna källor;
- att appen kopplar ihop det;
- texten för blå slut;
- streckade etiketter från andra ärenden;
- att namnet sprids;
- listan i ärendet;
- att ta bort en lista;
- minnet vid import;
- prestandan i grafen;
- bibliotekets underhåll;
- kopior som redan spritts före ett offer.

**Återstår:** vyn Etiketter och etiketterna i grafen (skissas först), och att appen läser in och ger tillbaka etiketter
när ett ärende öppnas.


## Etapp 9 (2026-11-07): organisationens återställningsnyckel

Ett kuvert i varje ärendefil för organisationens publika nyckel, och administratörens verktyg `tools/Chainsaw.Recovery`.
Inget gränssnitt i appen.

- **Konstruktionen** (`RecoveryEnvelopes`): ECIES på P-384 med .NET:s egna primitiver. Ett nytt tillfälligt nyckelpar
  per kuvert; ECDH mot den publika nyckeln (x-koordinaten, 48 byte); HKDF-SHA384 till en 32-byte nyckel; AES-256-GCM
  lindar filnyckeln under en slumpad nonce. Samma kontext är HKDF-info och associerad data: format, sort, algoritm,
  ärendets id, nyckelns fingeravtryck och den tillfälliga publika nyckeln som den står i filen. ECDH-hemligheten och
  AES-nyckeln nollställs direkt. **Varför ECIES P-384:** beprövat (SEC 1, ISO 18033-2), finns i .NET på Windows och
  Linux utan nya paket, ger en kort publik nyckel att bygga in, och appen kan lägga till kuvert utan att någonsin ha
  den privata nyckeln. RSA-OAEP hade fungerat men ger större nycklar och kuvert; HPKE finns inte i .NET 10.
- **Den publika nyckeln** (`RecoveryPublicKey` i Contracts): bara P-384 och okomprimerad punkt, läst strikt ur DER och
  importerad (punkten kontrolleras mot kurvan) och sparad i kanonisk form. Fingeravtrycket är SHA-256 av den formen,
  samma som OpenSSL räknar ut (testat med en nyckel gjord av OpenSSL). Den byggs in som resursen
  `organization-recovery-key.pem`; utan nyckel i filen är ingen konfigurerad, och en ogiltig nyckel eller en privat
  nyckel i filen stoppar appen.
- **När kuvertet läggs till:** när ärendet skapas, och när ett ärende som saknar kuvert för den inbyggda nyckeln öppnas
  (journalen `case.recovery-access-changed`, sparad med huvudet). `ICaseStore.OpenWithRecoveryKey` öppnar med den
  privata nyckeln och journalför `case.opened-with-recovery-key` med administratörens namn och fingeravtrycket.
- **Verktyget:** `keygen` (privata nyckeln som krypterad PKCS #8 med PBKDF2-HMAC-SHA512, 600 000 iterationer och
  AES-256-CBC, lösenord minst 16 tecken två gånger, aldrig överskrivning, aldrig i ett git-repo, 0600 på Unix, läses
  tillbaka med lösenordet), `fingerprint`, `check` (läser bara huvudet) och `open --share | --vault`.

**Egna beslut:**
- **Utan nyckel säger journalen det:** journalens första rad har `recoveryKey` med fingeravtrycket eller `null`, och
  `ICaseStore.RecoveryKey` är `null`. Arbetet stoppas inte, eftersom ingen nyckel är skapad än.
- **Byts nyckeln läggs ett nytt kuvert till och det gamla ligger kvar**; en röjd nyckel **återkallas** med en rad
  `REVOKED <fingeravtryck>` i resursen, och dess kuvert tas då bort ur varje ärende som öppnas (journalen
  `case.recovery-access-changed`). Kuvert för nycklar som bara är okända tas aldrig bort. Står i TODO under *Väntar
  på beslut*.
- **`open` ger en ny väg in på två sätt:** `--share` sätter ett delningslösenord från ett tillfälligt valv där
  administratören är operatör (valvet har en slumpad huvudnyckel och tas bort efteråt); `--vault` låser upp
  operatörens nya valv med huvudnyckeln operatören skriver och lägger till ett kuvert för det. Administratörens namn
  står i journalen i båda fallen.
- Lösenord och huvudnycklar tas aldrig från kommandoraden, och verktyget visar aldrig ett undantags detaljer.
- Ett test letar efter privata nycklar i hela repot och `.gitignore` utesluter `*.private.pem`.

**Granskningen:**
- Omgång 1: inga C, två I och nio M. I1: ett återställningskuvert i ett senare format eller skadat stoppade öppningen
  av hela ärendet; nu sparas det orört och hoppas över. I2: en bytt nyckel öppnade alla senare kopior för alltid; nu
  tas dess kuvert bort (ändrat i omgång 2). M rättade: gränsen på 64 kuvert, privat nyckel i resursen, stackspår
  från verktyget, kontroll av namnet och `--org`, konstanter. M i TODO: kuvertet kontrolleras bara med fingeravtrycket, ACL på Windows,
  lösenord från omdirigerad indata, egen kontroll av punkten.
- Omgång 2: inga C, ett I och tre M. I: att ta bort alla andra nycklars kuvert gjorde att en äldre version (eller en
  annan organisations) tog bort den nyare nyckelns kuvert och lade tillbaka den gamla; nu tas bara uttryckligen
  återkallade nycklar bort. M rättade: konstanter i stället för upprepade strängar, och verktyget städar sitt
  tillfälliga valv innan det säger att det lyckades.
- Omgång 3: inga C, ett I och fyra M, alla rättade. I: en återkallelse med en annan stavning (gemener, tabb, kolon,
  punktlista, CR) ignorerades tyst; nu läses varje rad som börjar med ordet, och en sådan rad utan giltigt
  fingeravtryck stoppar appen. M: två publika nycklar i resursen vägras, förklaringen i resursen innehåller inte
  längre nyckelordet, `check` säger *Nej* för en återkallad nyckel, och DEV-LOG har rätt journalkod.
- Omgång 4: inga C, ett I och två M, rättade. I: en återkallelse i backticks, efter ett tankstreck, i en numrerad
  lista eller med `=` ignorerades fortfarande; nu måste varje rad utanför nyckelblocket som nämner ordet ha exakt ett
  fingeravtryck (64 hexadecimala tecken, i grupper eller inte), annars startar inte appen. M: en kommentar med ordet
  stoppar appen (står i dokumentet), och verktyget förklarar en ogiltig inbyggd konfiguration.
- Omgång 5: inga C, ett I och tre M, rättade. I: ett nyckelblock skrivet på en rad (inklistrat ur ett mejl) dolde
  alla återkallelser efter sig; nu läses varje rad. M: ett block som aldrig stängs dolde resten, ett fingeravtryck på
  en rad utan ordet (stavfel, *ÅTERKALLAD*) stoppar nu appen, och spärren mot en privat nyckel bryr sig inte om
  versaler.
- Omgång 6: inga C, ett I och ett M, rättade. I: ett hexadecimalt ord (till exempel ett år) före ett grupperat
  fingeravtryck togs som dess första grupp och återkallade fel nyckel; nu får ingen hexadecimal siffra stå intill
  fingeravtrycket, och raden stoppar appen. M: ett fingeravtryck med kolon eller bindestreck på en rad utan ordet
  stoppar nu också appen.
- Omgång 7: inga C, ett I och fyra M. I: två fingeravtryck på samma rad kunde ge att det ena ignorerades. Rättat med
  en strikt regel i stället för att leta i fri text: en rad med ordet får bara innehålla ordet, ett fingeravtryck och
  dekoration, annars startar inte appen. M rättade: fler skiljetecken på rader utan ordet, fler radbrytningar. M kvar
  i TODO: ett fingeravtryck delat på två rader utan ordet; en nyckel inklistrad direkt efter text utan radbrytning
  stoppar appen (felar säkert).
- Omgång 8: inga C eller I (160 000 tolkningar av slumpade nycklar med och utan återkallelser utan fel). Tre M
  rättade: dokumentet säger vilka tecken som går bra, felet säger vilken rad, och en kommentar.

**Återstår:** gränssnittet som visar nyckelns fingeravtryck och när den saknas, att administratören skapar nyckeln och
bygger in den, och att byta ärendets filnyckel (för att stänga en röjd nyckel i kopior som redan lämnats ut).

## Etapp 10 (2026-11-07): låsskärmen och förstagångsinställningen

Byggt efter skisserna ID7a (rak kätting) och FS1a. Appen startar i förstagångsinställningen när inget valv finns och
på låsskärmen annars; skalet syns bara medan valvet är upplåst.

- **Teckenfältet** (`src/Chainsaw.App/Field/`, utan WPF): omskrivet efter asciify-engine (MIT, `THIRD_PARTY.md`) och
  skissens JavaScript: brus och fraktalt brus, scenerna Swell, Drift, Contour och Mesh, lyktan med spår medan Ctrl
  hålls, tyst runt formuläret (32 px marginal, 140 px övergång). Varje bildruta räknas ut och ritas till pixlar på
  trådpoolen (`FieldEngine`, `FieldRasterizer`, tecknen som masker i `GlyphAtlas`); UI-tråden tar bara musen och
  kopierar pixlarna. Med Windows animeringar avstängda står fältet still och ritas om bara när sidan, formuläret eller
  lyktan ändras; dolt eller minimerat vilar det.
- **Kättingen** (`ChainShape`): skarven ur `assets/logo/chainsaw-chain-joint.svg` (ett test håller dem lika) och
  länkens två raka stänger, 40 enheter, upprepad rakt ner vid x 330 i skala 0,9, stilla, i magenta som är lite mörkare
  till vänster och ljusare till höger, som i skissen. Täckningen räknas ut en gång per storlek med nio punkter per cell.
- **Låsskärmen** (`LockViewModel`, `Views/GateView`): titelraden med loggan, *Chainsaw* och fönsterknapparna,
  huvudnyckeln i en `PasswordBox`, fel huvudnyckel och andra fel i klartext, raderna om utgången och den automatiska
  låsningen, *Om Chainsaw · commit* (ur `AssemblyInformationalVersion`) med version, commit och fältets ursprung.
- **Huvudnyckeln** blir aldrig en sträng eller en egenskap: `PasswordEntry` läser `SecurePassword` rakt in i en fäst
  `char[]`, tömmer rutan, och vymodellen ger tecknen till `IVault` på trådpoolen och nollställer dem i `finally`. Dolda
  rutor töms när steget eller läget byts.
- **Förstagångsinställningen** (`FirstRunViewModel`): Välkommen (namn krävs), Huvudnyckel (minst 12 tecken, två gånger;
  skapar valvet och sparar profilen), Utgång (WireGuard-filen sparas genom `IExitStore` och väljs, eller *Senare*),
  Första ärendet (skapas genom `ICaseStore.Create` och blir det öppna ärendet, eller *Hoppa över*).
- **Låsning** (`GateViewModel`): Ctrl+L, efter inaktivitet (`AutoLock`, tiden i `settings.json`, förval 15 minuter),
  när Windows låses, sessionen kopplas från eller datorn går i viloläge. Skalet döljs först, kommandoraden töms, det
  öppna ärendet glöms och valvet låses. Skalets kortkommandon går bara när skalet visas (`WhenShown`).

**Egna beslut och avsteg från skisserna:**
- **Första ärendet har bara ärendenummer.** Cases har inget fält för titel, och startpunkten känns igen först med
  Chains i etapp 11; skissens *Titel* och *Startpunkt* (BTC) och raden om startpunkten är utelämnade (TODO).
- **Raden om bevakningen** (*En bevakning har slagit till*) visas inte, eftersom bevakningen inte finns.
- **Raden om utgången** säger skissens text när ingen utgång är ansluten, och *Utgången står kvar, men Chainsaw slår
  inte upp något medan det är låst* när den är ansluten (beslutet att en utgång står kvar vid låsning).
- **FS1a med ID7a:s kätting:** skissen FS1a har den äldre kättingen med ögat; samma fält och kätting används i båda.
- **Scenen är Drift**, som skissen visar först; skissens val av scen och rörelse finns inte i appen än (TODO).
- **En minsta bredd** på kolumnen till vänster om formulären, så att kättingen aldrig hamnar bakom formuläret i ett
  smalt fönster (kättingen tonar inte bort som fältet).
- **Ärendet i titelraden:** ett minimalt `CaseSession` håller det öppna ärendet och namnger det; att öppna och stänga
  ärenden kommer i etapp 11.
- **Inställningen för låsningen** ligger i `%LOCALAPPDATA%\Chainsaw\settings.json` (`AutoLockMinutes`, 1–480) och läses
  vid start; vyn Inställningar är inte byggd.

**Granskningen:**
- Omgång 1: inga C, ett I och tio M. I: inställningen kunde visa skalet med låst valv om en låsning kom medan första
  ärendet skapades; nu öppnas skalet bara om inställningen visas och valvet är upplåst, och ett ärende som skapats under
  en låsning stängs. M rättade: ett gammalt händelseanrop läser valvets läge igen, låsningen vid viloläge sker direkt
  på UI-tråden, ett misslyckat val av utgång rapporteras inte som en dålig fil, oväntade fel ger en text i stället för
  att krascha, kättingen hålls fri från formuläret, sökvägsläsaren stänger ringen vid Z och vägrar felaktig data, långa
  rader. M i TODO: låsning mitt i inställningen avslutar den, fältets loop stannar tyst vid ett fel, hel kopia av
  bildrutan per ruta på stora skärmar, inställningen bara i filen.
- Omgång 2: inga C eller I, fem M, alla rättade: Windows låsning eller viloläge under en pågående upplåsning låser nu
  valvet ändå (upplåsningen misslyckas), valet av utgång skriver valvet utanför UI-tråden och ett fel där ger aldrig en
  andra kopia, ett ärende som skapats under en låsning stängs på samma tysta sätt som annars, och ett valv som
  försvinner medan Chainsaw är låst visar förstagångsinställningen.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`), titel och startpunkt i första ärendet (etapp 11), vyn
Inställningar för låsningens tid och fältets scen, raden om bevakningen när Monitoring finns.


## Etapp 11 (2026-11-07): ärenden i appen

Byggt efter skissen NC1a och skalet som det ser ut i koden (SH1b med pillret, sidan utan eget huvud).

- **Ärendets titel i Cases:** en egen förseglad kedja `cases.title` (schemasteg 2) där den sista posten är titeln som
  gäller och de tidigare ligger kvar, `ICase.Title` (läses utan databasen) och `ICase.SetTitle`, som förseglar titeln
  och skriver `case.title-set` i journalen i samma transaktion. `CaseTitleRules`: högst 200 tecken, inga styrtecken,
  blanksteg slås ihop till ett.
- **Startpunkter på alla kedjor** (`ChainStartPoint` i Chains.Contracts): Bitcoin genom `BitcoinStartPoint`, Ethereum
  (`0x` och 40 eller 64 hex), TRON-adresser (Base58Check med versionsbyten 0x41) och, med kedjan utskriven, TRON-tx.
  Bara Bitcoin är `IsSupported`. Guiden har fått *Startpunkter*.
- **Kommandon** (`IShellCommand` och `CommandReply` i SharedKernel): appen och modulerna registrerar kommandon i
  containern, `CommandRegistry` hittar dem, fyller i med Tab, ger en ledtråd (*Tab: 2026-0117*) och har `/help`
  inbyggt. Text utan `/` går till `StartPointInput`: en Bitcoin-adress eller tx blir en startpunkt i det öppna ärendet
  genom Tracing, eller fyller i Nytt ärende när inget är öppet. Inget kastar: ett oväntat fel blir ett kort svar.
- **`/new`, `/open`, `/close`, `/verify`, `/lock`, `/help`:** `/open` föreslår ärendena i mappen med det senast sparade
  först; `/verify` säger i klartext vilken kedja som bryts vid vilken post och varför (`VerificationText`).
- **`CaseWorkflow`** öppnar, skapar och stänger ett ärende i taget utanför UI-tråden: ärendet som var öppet sparas och
  stängs alltid först (ett misslyckat sparande sägs, och arbetskopian tas tillbaka nästa gång), och ett ärende som
  öppnades medan Chainsaw låstes stängs igen i stället för att visas. När ett ärende väl finns hålls det öppet, och det
  som misslyckas efteråt (titel, en startpunkt, delningslösenordet) sägs i svaret.
- **Nytt ärende** (`NewCaseViewModel`, `Views/NewCasePanel`): panelen från höger som mörkar resten under titelraden,
  ärendenummer, titel, startpunkter en per rad med kedjans märke och vad det är under fältet (TRON tonad, okänt i
  rött), reglaget *Kan öppnas av en kollega* med två lösenordsfält, Avbryt, Esc och *Skapa ärende* (Ctrl+Enter).
- **Ärendelösenordet** blir aldrig en sträng: det läses ur `PasswordBox.SecurePassword` till en fäst `char[]`
  (`PasswordEntry`, som huvudnyckeln), bara när ärendet ska delas, kontrolleras, ges till `SetSharingPassword` och
  nollställs i `finally`. Fälten töms när panelen stängs, reglaget slås av eller Chainsaw låses.
- **Titelraden** visar *nummer · titel* eller *Inget ärende öppet*, och utgången ärligt ur `IExitConnection`: grön
  prick och platsen när den är ansluten, annars *Ingen utgång*; statusraden säger *Offline …* utan utgång.
- **Länken `chainsaw:`** (`Links/ChainsawLink`, `Interop/SingleInstance`, `ViewModels/LinkInbox`): ett Chainsaw per
  Windows-användare och session (en mutex), och en named pipe med `PipeOptions.CurrentUserOnly` som tar emot länken från
  en andra start (högst 512 byte UTF-8, två sekunder). Länken tolkas strikt (`chainsaw:<kedja>/<värde>`, bara bokstäver
  och siffror i värdet, värdet måste kännas igen på kedjan). Den väntar tyst medan Chainsaw är låst eller under
  förstagångsinställningen, och skriver aldrig något själv.
- **Förstagångsinställningen** har fått *Titel* och *Startpunkt* i första ärendet och skapar det genom `CaseWorkflow`.
- **Avslut:** `App.OnExit` sparar och stänger det öppna ärendet och väntar på det innan modulerna stoppas, eftersom den
  tidigare `async void` kunde låta processen ta slut före sparandet.

**Egna beslut och avsteg från skissen:**
- **Sidan har inget huvud** (orkestrerarens besked efter användarens påpekande, commit de84e93): skissens rad med
  vyns namn (*Graf*) finns inte; ärendets nummer och titel står i titelraden, och panelen fälls ut under den.
- **En länk skriver aldrig något själv.** Med ett ärende öppet läggs värdet i kommandoraden med *Från en länk: Enter
  lägger den som startpunkt i …*; utan ärende fylls Nytt ärende i. CLAUDE.md säger att användaren väljer ärende; så kan
  ett annat program som kan skicka länkar inte lägga startpunkter i ett ärende utan användaren. Bara den senaste länken
  väntar under låsningen, och låsskärmen säger inget om den.
- **Startpunkter på en kedja som inte är byggd** (TRON, Ethereum) i Nytt ärende skrivs i journalen
  (`app.start-point-noted`) i stället för att tappas, eftersom Tracing bara tar Bitcoin.
- **Det senast öppnade ärendet** föreslås ur filernas tid (den `.cut` som sparades senast), inte ur en egen lista i
  `settings.json`, som enligt sin beskrivning inte håller något om ärenden.
- **Högst 50 startpunkter** åt gången i Nytt ärende (princip 7); fler vägras med ett fel i stället för att kapas.
- **`/open` utan nummer** öppnar inget, utan föreslår det senaste ärendet (Tab fyller i det).
- **Utgångens namn:** titelraden visar utgångens plats (eller namn) först när den är ansluten; skissens *Stockholm* och
  *Esplora · senaste block* visas inte, eftersom tunneln och källans läge inte finns i appen än.
- **`/verify` som appens kommando:** kommandot ligger i appen och anropar `ICaseVerifier`, eftersom ett modulkommando
  som behöver det öppna ärendet kräver ett kontrakt för det (TODO).
- **Nytt** i pillret öppnar panelen direkt; menyn PN1 (Nytt fälls ut ur pillret) väntar på användaren.

**Granskningen:**
- Omgång 1: inga C, fyra I och tio M. I rättade: en TRON-tx från en länk blev en Bitcoin-startpunkt när den lades i
  raden eller panelen (värden som inte är Bitcoin skrivs nu med kedjan först, `tron:<hex>`, och läses tillbaka på
  samma kedja, `Links/StartPointText`); en skapelse som en låsning avbröt sades som om ärendet aldrig skapats (nu
  sägs att det finns, vad som inte hann göras och `/open`); första ärendets fel i inställningen visades aldrig (nu i
  skalet); ärendemappen lästes på UI-tråden vid varje tangent (nu en lista som läses utanför UI-tråden när skalet
  visas och efter varje öppning, skapelse och stängning). M rättade: ett felskrivet nummer eller ett nummer som finns
  stänger inte längre det öppna ärendet, avslutet tar bort värden vad som än misslyckas, ett ärende som en låsning
  stänger i bakgrunden väntas in innan det öppnas igen, en länk ersätter aldrig text i raden, titelns felmeddelande
  nämner styrtecken. M i TODO: avslut mitt i en skapelse, svar efter en låsning, välja befintligt ärende för en
  länk, pipens robusthet, testklassernas XML-dokumentation.
- Omgång 2: inga C eller I, fyra M, rättade: första ärendet som en låsning avbröt sägs i skalet efter upplåsningen,
  inställningens svar visas före en väntande länks förklaring, ett ärende som stängs efter en låsning under
  öppningen väntas också in (`CaseSession.Release`), kedjans namn i `tron:` tas ur samma tabell som länken, och
  avslutet släpper mutexen även om värden inte går att ta bort. I TODO: en länk som kommer medan raden har text sparas
  inte.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`), registrera länken i Inställningar, öppna en kollegas fil
med lösenord, ändra titeln i ett gränssnitt, sökning i kommandoraden, och punkterna under *Ärenden i appen* i TODO.

## Etapp 11b: undermenyer i pillret (PN1), 2026-11-07

**Byggt:**
- **En allmän undermeny för pillret** (skiss PN1, efter Gawks B9): en knapp med en rad i `Navigation/SubmenuTable.cs`
  fäller ut sin undermeny i stället för att visa en vy. Raderna (namn, kortkommando, vad raden behöver och ett ärligt
  verktygstips) ligger i tabellen utan WPF, så att fler knappar får undermenyer utan ny kod i vyn.
- **Formen** (`Navigation/SubmenuShape.cs`): flik och meny som en enda kontur, ritad som sökvägsdata ur skissens
  `shape` (fliken 51 px, 6 px förbi pillret, menyn 230 px, hörn 8 px, rader 30 px, linjer 9 px), och i
  `Views/PillSubmenu` ytan, kanten och skuggan (skuggan i ett eget lager) med fliken exakt över knappen.
- **Vymodellen** (`SubmenuViewModel`, `SubmenuRowViewModel`): öppna och stänga, markeringen som hoppar över linjer och
  tonade rader och går runt, Enter, bokstäverna, Esc och vart tangentbordet går när menyn stängs.
- **Tangentbordet och fokus:** Enter eller klick på knappen öppnar, ↑ ↓ Home End PgUp PgDn flyttar, Enter och Space
  väljer (inte en upprepad tangent), bokstäverna väljer direkt, Esc och ← stänger med fokus tillbaka på knappen, Tab
  och → stannar i menyn. Ett klick utanför stänger med fokus på knappen; tangentbordet till något annat i fönstret
  (Ctrl+K, en vy, låsningen) stänger utan att ta tillbaka det. Ett annat fönster stänger menyn, och fokus kommer
  tillbaka till knappen när fönstret blir aktivt. Menyn följer knappen när fönstret ändrar storlek.
- **Nytt:** Startpunkt (S) lägger markören i kommandoraden med en rad om vad som ska skrivas, eller öppnar Nytt ärende
  utan ärende; Importera sanktionslista … (I) öppnar Windows filväljare och importerar genom `ISanctionLists`
  (`SanctionListImport`, utanför UI-tråden, med svar för varje fel och *fanns redan* för samma fil); Ärende (Ctrl+N)
  öppnar panelen. Etikett, Bevakning och Begäran står tonade med *Inte byggt än: …*. Ctrl+N öppnar panelen direkt.
- **Automatisk låsning** hålls medan filväljaren är öppen (`AutoLock.Hold`), och en fil som väljs medan Chainsaw är
  låst släpps.
- `SanctionCurrency.Bitcoin` (`XBT`) i Attributions kontrakt, så att koden finns på ett ställe.

**Egna beslut och avsteg från skissen:**
- **Startpunkt är inte tonad utan ärende** (skissen tonar den): orkestrerarens uppdrag säger att den öppnar Nytt ärende
  utan ärende, eftersom en startpunkt startar ett ärende. Tabellen har ändå `SubmenuNeed.OpenCase` för rader som kräver
  ett ärende.
- **Etikett är tonad också med ett ärende,** eftersom inget gränssnitt för etiketter finns (grafen och vyn Etiketter
  är inte byggda).
- **Markören hoppar över tonade rader** med piltangenterna, och en tonad rads bokstav sväljs utan att något händer.
- **Esc, ← och ett klick utanför** lämnar fokus på knappen; Tab, Shift+Tab och → stannar i menyn.
- **Knappen under menyn döljs av den täckande formen,** inte med genomskinlighet som i skissen.
- **Fliken i en kortare meny:** konturen blir aldrig lägre än fliken och två hörn.

**Granskningen:**
- Omgång 1: inga C, fyra I och tio M. I rättade: en meny kunde öppnas osynligt bakom Nytt ärende (pillret nåddes med Tab
  genom panelen), menyn stod kvar när fönstret ändrade storlek, fokus försvann efter Alt+Tab, konturens kant och skugga
  klipptes. M rättade: Shift+Tab och → släppte ut fokus, en nedhållen Enter valde en rad, automatisk låsning bakom
  filväljaren, import som misslyckades tyst, *fanns redan*, `XBT` på två ställen, tonade raders färg, verktygstipsets
  plats, radlängder och testplanen.
- Omgång 2: inga C, ett I och tre M, rättade: fokus efter Alt+Tab ges till knappen också när Windows lämnat det på
  fönstret, en fil som väljs medan Chainsaw är låst importeras inte, *importeras redan* visas som ett fel, en rad i
  testplanen.
- Omgång 3: inga C eller I.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Undermenyer i pillret*); Etikett, Bevakning och Begäran
när deras gränssnitt finns; fler knappar med undermenyer.

## Etapp 12: grafen (GR4, GR3a och GR3c), 2026-11-07

**Byggt:**
- **Vyn Graf** (`Views/GraphView`) i skalet SH1b, utan sidrubrik: ytan, grafens hörn med växeln (fri yta och
  hoppkolumner), metoden (*FIFO* eller *haircut*, alltid synlig) och gallringen av beloppen (*Alla belopp*, *Spårade
  belopp*, *Inga belopp*), *?* med bildspråket nere till vänster, och inspektören (280 px) till höger.
- **Ytan** (`Controls/GraphCanvas`): ritar med `OnRender` bara det som syns (princip 6): hoppkolumnerna, linjerna som
  Bézierkurvor (framåt från höger sida till vänster, bakåt på fria ytan från vänster sida, och i hoppkolumnerna som en
  båge över noderna), beloppen mitt på linjerna och noderna (tunn ram, ikon i rund eller fyrkantig ram i sortens färg,
  namnet, antalet adresser, markeringar i hörnet och kedjans namn under hörnet). Ikonerna ur 24-rutnätet med linjen
  1,5 px på skärmen i alla zoomnivåer (`GridIcon.StrokeFor`). Klick väljer, drag flyttar en nod eller panorerar,
  hjulet zoomar runt pekaren; Esc, E, F, + och −.
- **Bildspråket i en tabell** (`Graph/VisualLanguage`): varje sort (GR3c: uttag blått, spenderat gult, risk rött,
  offer, kluster, okänd, swap, tx med flera ägare, mynt, annan kedja) med namn, ikon, färg, form och streckad ram, och
  markeringarna *Uttag funnet*, *Etikett från ett annat ärende* och *Sanktionerad*. Grafen, legenden och inspektören
  läser samma tabell. Färgerna i `Themes/Colors.xaml`, ikonerna i `Themes/Icons.xaml`.
- **Rena klasser utan WPF** (`Graph/`), testade i `tests/Chainsaw.App.Tests`: geometri och `Viewport` (panorering,
  zoom runt en punkt, passa in), `HopLayout` (kolumner per hopp, *Start*, *+1*, *−1*, *Utan hopp*), `GraphPlacement`
  (varje läge minns sina platser; i kolumnerna bara upp och ner), `EdgeCurve`, `EdgeWidth` (tjocklek ur det spårade
  beloppet mot det största), `AmountText` (svenska decimaler, minst fyra och aldrig avrundat till noll),
  `AmountLabelMode` (gallringen), `GraphHitTest` (träff och det som syns), `SeedPicker`, `GraphScene` och
  `InspectorContent`.
- **Vymodellen** (`ViewModels/GraphViewModel`): läser grafen ur ärendet genom `IGraphSource` (`Graph/GraphSource`:
  Tracing, Attribution och Clustering genom sina kontrakt, allt i `Task.Run`), byter metod och räknar om, expanderar
  den valda noden ett steg (en adress, varje adress i ett kluster, eller en tx), sparar en dragen nods plats i ärendet
  per läge, och släpper svar som kommer efter att ärendet bytts. Startpunkter från kommandoraden läser grafen på nytt
  (`CaseWorkflow.StartPointAdded`).
- **Inspektören och *Varför?*:** fakta (in, ut, spårat in, sort, hopp, tid i lokal tid och UTC), adresser, txid och
  block (höjd och hash) i sin helhet så att varje steg kan kontrolleras mot kedjan, *Expandera*, och *Varför?* med
  etikettens ursprung, klustrets länkar (heuristik med version, tx och skäl, ur Clustering), varför spåret tar slut,
  det spårade beloppet med metod, version och tx, och att en linjes belopp är fakta men aktörerna klustringens
  slutsats. Varje svar länkar till sitt avsnitt i Guiden.
- **Ärligt:** utan ärende, utan startpunkter, under läsning, när läsningen misslyckas, när det inte finns något att
  spåra än och offline säger grafen det i klartext. *Offline* står i hörnet utan utgång.
- **Guiden:** `grafen.md` (*Grafen och bildspråket*), länkad från legenden.

**Egna beslut och avsteg från skissen:**
- **Inget huvud med *Graf***: skissens rad med vyns namn finns inte (sidan har ingen rubrik); knapparna står i
  grafens övre högra hörn, och vad som spårats och det senaste svaret uppe till vänster bredvid pillret.
- **Vilka mynt som spåras** (skissen visar inte valet): alla mynt en startadress tagit emot i grafen, och en
  startpunkts-tx:s betalningar till andra än betalarna; ett mynt längre fram i samma flöde tas bort så att inget räknas
  två gånger. Står i TODO under *Väntar på beslut*.
- **Startpunkten är *Offer*** (rund, magenta) när ingen etikett säger annat, efter GR3a. I TODO under *Väntar på
  beslut*.
- **Namnen** är etikettens namn eller adressen förkortad (`bc1q…x0wl`), inte *K1* och *U2*, eftersom en numrering
  ändras när grafen växer. I TODO.
- **Den fria ytan börjar i hoppkolumnernas platser**, så att aktörerna först ligger i hoppens ordning.
- **Tidslinjen under grafen** i GR4 och **mixerns kandidater** är inte byggda (TODO). Markeringarna *Bevakad* och
  *Kan frysas* ritas inte och står inte i legenden, eftersom bevakningen och USDT inte finns.
- **Guiden som vy** finns inte: en länk visar vyn Guide (*Inte byggt än*) och svaret ovanför kommandoraden säger var
  avsnittet ligger.
- **Metod, läge och gallring sparas inte i ärendet** (Tracing sparar bara platserna); står i TODO.
- **Grafens tangenter** (Esc, E, F, + och −) gäller när ytan har fokus, och står i verktygstipset på *?* i stället för
  på hela ytan, som annars visade ett tips så fort pekaren vilade.

**Granskningen:**
- Omgång 1: ett C, fem I och tio M.
  - **C:** ett klick valde aldrig något, eftersom `ReleaseMouseCapture` avslutade trycket innan klicket lästes. Tryck,
    drag och klick ligger nu i en ren, testad klass (`Graph/GraphGesture`).
  - **I:** en linje mellan två noder i samma kolumn gick rakt genom den övre (nu en liten båge ut åt höger); ett fel i
    spårningens räkning (`InvalidOperationException`) tömde hela grafen (nu visas den utan belopp); en expansion kunde
    avbrytas av metodbytet eller skrivas över av en äldre läsning (nu kan metoden inte bytas under en expansion, en ny
    startpunkt väntar på den, och en äldre läsning släpps); ett kluster expanderades adress för adress utan gräns (nu
    högst 100 nya tx per tryck); utan spårning visades *Spårat 0,0000* som om metoden sagt det (nu *Ingen spårning* och
    skälet).
  - **M** rättade: inga nästlade ternärer, klustringens mening bara vid ett kluster, UTC med datum, blockhöjd utan
    mellanslag, död kod borttagen, nodens verktygstips på hovring, platserna på fria ytan står kvar när grafen växer,
    en tjänst vinner över en offer-etikett, expansionens svar och grafens problem visas båda, Shift med +, radlängder.
- Omgång 2: ett I och fem M.
  - **I:** en ny nod kunde hamna exakt ovanpå en gammal på fria ytan; nu går den under dem i sin kolumn.
  - **M** rättade: verktygstipset styrs av ytan, högst tio uppslag per expansion, bågen i en kolumn stannar i
    mellanrummet, enum-parametrar före kommandot i XAML, Shift bara för +.
- Omgång 3: inga C eller I. Två M rättade: första läsningen av ett återöppnat ärende undviker sparade platser, och
  verktygstipset stängs vid tryck och när ytan döljs.

**Tester:** 327 i appen (83 nya för grafen), 1 227 i hela lösningen. Bygget utan varningar.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Grafen*); tidslinjen under grafen, mixerns kandidater,
Guiden som vy, formulären för etiketter, länkar och uppbrytning, och att spara metod och läge i ärendet (TODO); besluten
om vilka mynt som spåras, startpunkten som offer och nodernas namn (TODO, *Väntar på beslut*).

## Etapp 14 (2026-11-07): Pricing, kurser vid en tidpunkt

Modulen `Pricing` utan gränssnitt: `IPriceService` ger priset på en kedjas valuta (BTC först) vid en tidpunkt i vald
valuta, med källa, upplösning och perioden kursen faktiskt gäller, registrerad i `AddPricingModule`.

- **Två sorters källor, ett kontrakt för varje** (princip 8): `ICryptoRateSource` (en börs prishistorik, perioder med
  första, högsta, lägsta och sista pris och volym) och `IFiatRateSource` (en centralbanks kurs per bankdag). En källa
  säger bara hur man frågar och hur svaret läses; att fråga genom utgången, försegla och läsa ur ärendet görs en gång,
  i `PriceService`. En källa som registreras före modulen tar Bitstamps eller Riksbankens plats.
- **Källorna** (kontrollerade 2026-11-07 mot dokumentationen och med ett riktigt anrop var, aldrig i testerna):
  - **Bitstamp** `GET /api/v2/ohlc/{btcusd|btceur}/?step=&limit=1&start=`: öppen utan nyckel, perioder på 60, 3 600
    och 86 400 sekunder (bland andra) och historik sedan 2011. Valt framför Kraken, vars öppna OHLC bara ger de senaste
    720 perioderna och alltså ingen historik. Gränsen är 10 000 anrop per 10 minuter.
  - **Riksbanken (SWEA)** `GET /swea/v1/Observations/{SEKUSDPMI|SEKEURPMI}/{från}/{till}`: kronor per en dollar eller
    euro, en kurs per bankdag, fritt att använda med Riksbanken som källa. Utan nyckel tillåts 5 anrop i minuten och
    1 000 om dagen per IP-adress; ett svar 429 säger ärligt att källan inte tar emot fler uppslag just nu. Utan kurs
    i intervallet svarar API:t 204 utan innehåll.
- **Kursen är en uppgift, omräkningen en uträkning** (princip 2 och 3): `CryptoRate` och `FiatRate` är vad källan sa,
  med sitt förseglade svar (`RateOrigin`). `Price` multiplicerar dem exakt (decimal) och `Price.Steps` visar stegen på
  svenska med källa, marknad, period och serie; `ValueOf(Satoshi)` värderar ett belopp och avrundar bara det som visas,
  till två decimaler.
- **Varje råsvar förseglas** i `pricing.raw_responses` innan det används (källa, URL, sort, ämne, status, medietyp,
  tider i UTC och lokalt med tidszon, utgång, vem som hämtade, SHA-256 och byten), också felsvar och svar som inte går
  att tolka, som aldrig används. Två register bredvid (inte förseglade) pekar ut slutliga svar; ett svar som läses
  tillbaka kontrolleras mot sin SHA-256 och mot den källa, sort och det ämne registret pekade på.
- **Aldrig hämtat igen:** en period räknas som slutlig först fem minuter efter sitt slut, både enligt datorns klocka
  (annars frågas inte alls) och enligt det förseglade svarets tid. En dag hos Riksbanken är slutlig när den har en egen
  kurs, eller när den har passerat (sex timmar efter midnatt i svensk tid) innan svaret kom. Slutligheten avgörs alltid
  ur det förseglade svaret, aldrig ur registren, som inte är förseglade.
- **Riksbankens gräns** (5 anrop i minuten, 1 000 om dagen utan nyckel): ett anrop i taget, minst 12 sekunder isär
  (`RiksbankInterval`), ärendet läses igen före varje anrop, ett svar utan slutlig kurs ges igen i 15 minuter i stället
  för ett nytt anrop, och dygnets anrop räknas sedan Chainsaw startade. Samtidiga uppslag av samma Bitstamp-period delar
  ett anrop. Ett anrop som har gått iväg körs alltid klart och förseglas, också om den som frågade slutar vänta.
- **Offline** (princip 9): utan utgång svarar ärendet, annars *Offline*; `PriceLookupMode.CaseOnly` läser bara ärendet.
  En tidpunkt i framtiden, en kedja utan källa och en period som inte är slut vägras innan något skickas.
- **Guiden:** `docs/guide/kurser.md`.

**Egna beslut:**
- **Sista priset i perioden där tidpunkten ligger**, och perioden visas alltid. Första priset ligger före tidpunkten
  och sista efter; sista priset är det vanliga måttet på en period, och avståndet syns.
- **En timme som förval**, eftersom en blocktid bara är ungefär rätt; minut och dag kan väljas.
- **Kronor via dollar som förval** (`KronaVia`, euro kan väljas): BTC/USD har mest handel. Ingen börs med lång historik
  har BTC/SEK. Räknar man i dollar eller euro används Bitstamps kurs direkt, utan Riksbanken.
- **Dagen räknas i svensk tid**, och för en helg eller helgdag används senaste bankdagen före (det står i stegen).
  Riksbankens kurs för en dag används för hela dagen, också före publiceringen; dagens kurs innan den är publicerad
  ger *inte publicerad än* i stället för gårdagens.
- **Ett Riksbanksanrop täcker 14 dagar före och 31 dagar efter** den efterfrågade dagen (högst till i går), så att ett
  anrop räcker för en månads tx inom Riksbankens gräns på 5 anrop i minuten.
- **Ingen rad i journalen per uppslag**, som i Chains: den förseglade tabellen har vem, när och vad.
- **Inget hämtas utan operatörsprofil** eller med låst valv.
- **Valutan ställs in** i `PricingOptions` (SEK som förval) eller per anrop; att spara valet i appens inställningar
  kommer med gränssnittet.

**Granskningen:** fyra omgångar.
- **Omgång 1:** tre I och nio M.
  - **I:** en period kunde sparas som slutlig i samma ögonblick som den tog slut (nu fem minuters marginal); registren,
    som inte är förseglade, avgjorde slutligheten (nu det förseglade svaret); inget skyddade Riksbankens gräns (nu ett
    anrop i taget med mellanrum, och dagens publicerade kurs pekas ut).
  - **M** rättade: volym noll syns i stegen, `CaseException` står i kontraktet, alla fel i en källas läsning förseglas,
    källornas namn för användaren kommer från källorna, helg i dag får rätt text, standardkällorna byggs först när de
    används, radlängder. Perioder före källans historik står i TODO.
- **Omgång 2:** två I och sex M.
  - **I:** svar utan slutlig kurs hämtades om och om igen (nu ges de igen i 15 minuter); samtidiga uppslag av samma
    Bitstamp-period skickade var sitt anrop (nu ett delat).
  - **M** rättade: mellanrummet räknas också efter ett fel på vägen, dygnsgränsen räknas, en dag räknas som passerad
    först sex timmar efter midnatt, en adress som inte kan byggas eller vägras av utgången ger ett svar, en skadad rad
    ger *Corrupt*, testernas fejkade utgång är trådsäker, nya tester.
- **Omgång 3:** ett I och nio M.
  - **I:** efter marginalen bedömdes en helgdag efter anropets sista dag i stället för dagen själv, så helgens uppslag
    misslyckades mellan midnatt och sex.
  - **M** rättade: ett anrop som utgången vägrade räknas inte, ett skickat Riksbanksanrop förseglas alltid, delade
    Bitstamp-anrop per öppet ärende, dygnets räkning beskriven ärligt, tester för avbrytning, vägrade adresser och det
    delade anropet. Perioder före källans historik (igen) står i TODO.
- **Omgång 4:** inga C eller I. Två M rättade: ett ihågkommet svar gäller bara samma passerade dag, och mellanrummet
  mäts med `Stopwatch`.

Alla C och I är rättade och har regressionstester: 126 tester i Pricing.

**Kvar i TODO:** kurserna i gränssnittet (kräver skiss), Bitstamps villkor för bruk i tjänsten (*Väntar på beslut*),
det som inte gick att kontrollera i dokumentationen, perioder före källans historik, dygnsgränsen över omstarter och
datorer, fler källor och tokens, stängnings- eller medelkurs, och ett gemensamt lager för råsvar.

**Återstår:** beloppen i SEK i grafen och inspektören, och valet av valuta i inställningarna.
## Etapp 15: datalagret och spårningen, 2026-11-07

**Byggt:**
- **Kontrollen mot kedjan (Chains):**
  - `VerifyTransactionAsync` hämtar tx:ens bytes (`/tx/:txid/hex`) en gång och förseglar dem. Chainsaw räknar fram
    txid (dubbel SHA-256 utan vittnet, `RawTransaction.cs`) och jämför källans beskrivning med bytesen i allt som
    txid binder: version, inputs, sekvenser, outputs och locktime.
  - En beskrivning som stämmer märks `BytesVerified`, också när den senare läses ur ärendet. En som motsäger bytesen
    används aldrig och hoppas över när ärendet läses, så att ett tidigare bra svar inte skyms.
  - `GetBlockAsync` hämtar blockets 80 bytes stora huvud (`BlockHeader.cs`). Huvudet ska ha blockets hash och nå det
    mål dess egna `bits` anger (`HeaderVerified`).
- **Omorganisation:** djupet räknas från den höjd ärendet kände till **när svaret förseglades**
  (`RawResponseStore.KnownHeight(reader, upTo)`), inte från i dag. Ett svar som var grunt när det hämtades hämtas
  därför om när blocket blivit djupt. Har tx:en då bytt block, eller blivit obekräftad, sägs det (`BitcoinLookup.Notes`).
- **OP_RETURN:** en utgång som aldrig kan spenderas (OP_RETURN, eller ett skript över 10 000 byte) håller inte längre
  svaret om spenderade utgångar öppet.
- **Adressens sammanfattning:** `GetAddressSummaryAsync` (`/address/:a`) ger antalet tx, mottaget och spenderat. Den
  hämtas varje gång och förseglas.
- **Spårningen (Tracing):**
  - `TraceAsync` räknar spårningen utanför den anropande tråden och kan avbrytas mellan stegen.
  - En expansion håller tillbaka det som skulle svämma över grafen (princip 7) och säger det
    (`ExpansionResult.HeldBack`): en adress ger sina 50 nyaste tx, och med sammanfattningen sägs hur många den har.
    En tx ger högst 50 spenderare, de som tar emot mest först.
  - Expansionen av en tx kontrollerar tx:en och dess spenderare mot deras bytes.
  - Klustren (`IClusteringService.GetClusters`) och sluten vid adresserna (`ITrailEndClassifier.ClassifyAddresses`)
    läses en gång per bygge.
  - Grafens läge (`GetView`/`SaveView`, tabellen `tracing_view`, schemasteg 2) sparar metod, vy, belopp och det som
    syns. Ett byte av metod får en rad i journalen.
- **Appen:**
  - Grafen öppnas som den lämnades. Vyns ändringar sparas en i taget, och det som syns sparas när ett drag slutar.
  - Etiketterna läses en gång per bygge (`IAttributionService.GetLabels` för många adresser), och spårningen körs
    med `TraceAsync`.
  - `ILabelLibrary.ShareInto` körs för grafens adresser, och grafen läses om när etiketter kom in.
  - `ILabelLibrary.Absorb` körs när ett ärende öppnas eller skapas och innan det stängs.
  - Inspektören visar på en linje om dess tx är kontrollerade mot sina bytes (*Kontroll*, en rad i den befintliga
    tabellen).
- **Guiden:** `kontroll-mot-kedjan.md` (*Hur Chainsaw kontrollerar svaren mot kedjan*).

**Egna beslut:**
- **Bytesen hämtas bara när en tx expanderas för sig** (och för dess spenderare), inte för tx på en adress sida. Det
  skulle kosta ett uppslag per tx; i dag syns det i inspektören, och resten står i TODO.
- **Djupet räknas från när svaret hämtades** i stället för att fråga `/block/:hash/status`. Det kostar ett uppslag
  till per tx som hämtades grund, men kräver ingen ny sorts svar och fångar också en tx som blivit obekräftad.
- **Vad som hölls tillbaka visas i den befintliga raden** överst i grafen. Att hämta resten, att vika ihop en
  batchutbetalning och att märka kontrollerade tx i grafen väntar på skiss (TODO).
- **Bara bytet av metod skrivs i journalen**, eftersom de spårade beloppen vilar på det. Läge, belopp och zoom är
  vanlig data som platserna.
- **Etikettbiblioteket stoppar aldrig ärendet:** ett låst valv, en saknad operatörsprofil eller ett bibliotek som inte
  går att läsa gör bara att etiketterna kommer senare (TODO: säga det).

**Granskningen:**
- **Omgång 1:** inga C, tre I och nio M.
  - **I1:** upplysningar (att svaret om spenderade utgångar var gammalt) gjorde nästan varje expansion *Delvis*. Nu
    skiljs det som gör expansionen ofullständig från upplysningar (`Notes`).
  - **I2:** en beskrivning som motsade bytesen spärrade tx:en för gott och skymde ett tidigare bra svar. Nu hoppas den
    över.
  - **I3:** att en tx var kontrollerad syntes ingenstans. Nu finns raden *Kontroll* i inspektören.
  - **M** rättade:
    - vyn kunde sparas i fel ärende medan det nya ärendets vy lästes;
    - ett mål över 256 bitar godtas inte;
    - `KnownHeight` hoppas över för svar som aldrig ändras;
    - långa rader.

  Övriga M står i TODO.
- **Omgång 2:** inga C eller I. Fyra M:
  - två rättade: en ändring av vyn i ett nyss öppnat ärende kunde gå förlorad medan förra ärendets sparning pågick,
    och dess fel kunde visas i det nya ärendet;
  - två i TODO: bytesen tolkas om vid varje läsning, och en omorganisation sägs bara i expansionens rad.

**Tester:** 1 323 gröna i hela lösningen vid granskningen, varav 42 nya för etappen:
- 23 i Chains (kontrollen mot bytes och huvud, OP_RETURN, sammanfattningen, omorganisationen);
- 11 i Tracing;
- 3 i Attribution;
- 2 i Clustering;
- 16 i appen.

Bygget är utan varningar.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Datalagret och spårningen*). Skiss för det som hölls
tillbaka och för märkningen av kontrollerade tx i grafen (TODO, *Väntar på skiss*).



## Etapp 13 (2026-11-07): WireGuard-tunneln

Chainsaws eget hjälpprogram för WireGuard, `tools/Chainsaw.Tunnel` (Go), kopplat in bakom `IExitConnector` i stället
för `UnavailableExitConnector`. Inte beroende av Gawk, och ingen kod från Gawk. Inget gränssnitt.

- **Hjälpprogrammet** kör wireguard-go med gVisors nätstack i minnet: inget nätverkskort, inga
  administratörsrättigheter. En process per tunnel.
  - **Konfigurationen och uppgifterna:** konfigurationen och ett slumpat användarnamn och lösenord (128 bitar
    vardera) kommer som en JSON-rad på stdin, aldrig på kommandoraden eller disken.
  - **Svaret:** den första raden på stdout är porten när servern har svarat på en handskakning, annars ett felkod.
  - **Stopp:** `{"stopped":"lost"}` när tunneln faller. Stängd stdin stoppar programmet, så det överlever aldrig
    Chainsaw.
  - **Tystnad:** stderr används inte, och inget loggas.
- **Proxyn:** SOCKS5 bara på `127.0.0.1`, bara användarnamn och lösenord (jämförda i konstant tid) och bara
  `CONNECT`. Fler än 64 anslutningar väntar på en ledig plats.
- **Namnuppslag:** namn slås upp inne i tunneln med filens `DNS =`. Adresser som inte är globala vägras, med samma
  regler som `LocalTargets`, också när ett namn pekar dit. Det enda namn datorn själv slår upp är serverns eget i
  `Endpoint`.
- **WireGuard-filen tolkas strikt:**
  - Exakt en `[Interface]` och en `[Peer]`.
  - `DNS =` med en IP-adress.
  - Okända nycklar vägras.
  - `PostUp` och de andra kommandona i wg-quick ignoreras och körs aldrig.
  - Felet nämner rad och nyckel, aldrig ett värde.
  - `ExitStore` gör samma kontroll av `DNS =` när en utgång läggs till.
- **Fallen tunnel:** keepalive var 25:e sekund (eller kortare om filen säger det). Tunneln räknas som fallen om ingen
  ny handskakning har setts på 270 sekunder (monoton klocka), om enheten stängs eller om proxyn slutar ta emot.
- **C#:**
  - `WireGuardConnector` startar hjälpprogrammet genom `HelperProcessStarter`, som först jämför dess SHA-256 med den
    inbyggda hashen och håller filen stängd för skrivning tills processen har startat.
  - `WireGuardTunnel` säger `Stopped` vid en senare rad, när stdout tar slut eller när processen avslutas, men inte
    vid dispose. Dispose stänger stdin och dödar processen efter tre sekunder.
  - En start som avbryts eller tar för lång tid (30 s) dödar processen, eftersom läsningar från rör inte alltid kan
    avbrytas på Windows.
  - Två nya skäl i `ExitFailure`: `ConfigurationRefused` och `NoAnswer`.
- **Bygget:**
  - `build.sh` och `build.ps1` kräver Go 1.27.3 (`toolchain` i `go.mod`) och kör `go vet`, testerna,
    `go mod tidy -diff` och `govulncheck` mot Windows-bygget.
  - Bygget är reproducerbart: samma källa ger samma hash.
  - `dist/chainsaw-tunnel.exe` och dess `.sha256` är incheckade. Appen kopierar programmet och `licenses/` till sin
    utdatamapp.
- **Licenserna** (MIT, Apache 2.0 och BSD-3, ingen copyleft) står i `THIRD_PARTY.md`.

**Egna beslut:**
- **En port med inloggning i stället för en tunnel i Chainsaws egen process.** Alternativen vägdes:
  - Ett Go-bibliotek i processen kräver cgo och en korskompilator för Windows.
  - En egen ström över stdin och stdout kräver ett eget protokoll med flödeskontroll.
  - Båda ändrar kontraktet `IExitTunnel`.
  - Porten görs i stället säker: hjälpprogrammet håller den ensamt (`SO_EXCLUSIVEADDRUSE`) och frigör den först när
    processen avslutas. Chainsaw frågar efter varje anslutning, innan något skickas, om programmet lever
    (`IExitTunnel.IsRunning`).
  - Kvar finns ögonblicket när ett kraschande program stänger sina handtag innan Windows räknar det som avslutat (TODO).
- **Uppgifterna slumpas av Chainsaw**, inte av hjälpprogrammet, så att de aldrig behöver läsas tillbaka.
- **Ansluten först efter en handskakning**, så att *ansluten* betyder att servern svarar.
- **Serverns namn i `Endpoint`** slås upp med datorns resolver, som i varje VPN-klient. IPv4 föredras.
- **`dist/` i stället för `bin/`**, som `.gitignore` utesluter.
- **Uppdaterade bibliotek:** x/crypto, x/net, x/sys och x/time är uppdaterade. gVisor står kvar på den version
  wireguard-go anger. govulncheck hittar en sårbarhet i x/crypto (`openpgp`), men den delen används inte.

**Granskningen:** tre omgångar.
- **Omgång 1:** inga C. Tre I och sju M.
  - **I:** porten kunde tas av ett annat program (nu ensam port och kontroll efter anslutningen).
  - **I:** Go 1.24 hade inte längre säkerhetsstöd (nu 1.27.3, låst, med govulncheck).
  - **I:** proxyn kunde dö tyst (nu en fallen tunnel).
  - **M rättade:** anslutningar väntar på plats, halvstängda reläer har en tidsgräns, monoton klocka, rören stängs,
    skälet till en vägrad fil följer med felet, och korta IPv4-former vägras.
  - **M dokumenterade:** testluckor för den riktiga processen (TESTER-INFOR) och sömn (TESTER-INFOR, TODO).
- **Omgång 2:** inga C. Ett I och sex M.
  - **I:** programmet stängde porten innan processen avslutades. Nu stängs den aldrig av programmet självt, och ett
    test visar det.
  - **M rättade:** gränsen 270 s, bara kända nycklar nämns i fel, strikt DNS-adress, govulncheck låst och mot
    Windows, `go.mod` städad, radlängder.
  - **M i TODO:** skälet till en vägrad fil behöver ett fält i `ExitStatus` för gränssnittet.
- **Omgång 3:** inga C eller I. Tre M.
  - IPv6 inom hakparenteser i `DNS =` vägras nu.
  - Testklassen har fått en sammanfattning.
  - Luckan när programmet kraschar står i TODO.

**Tester:** 197 i Network (42 nya) och 44 i Go, däribland en hel tunnel mot en WireGuard-peer i samma process på
`127.0.0.1`: handskakning, namn som slås upp av peerns namnserver inne i tunneln, relä, vägran mot ett namn som pekar på
datorn, stopp när stdin stängs och att porten inte frigörs före processens slut. 1 443 tester i hela lösningen. Bygget
utan varningar.

**Återstår:**
- Prova på Windows med en riktig WireGuard-fil (`docs/TESTER-INFOR-1.0.md`, *WireGuard-tunneln*).
- Gränssnittet för utgångar (skissas).
- Återanslutning efter en fallen tunnel.
- Skälet till en vägrad fil i `ExitStatus`.
- Luckan när programmet kraschar.
- Signering av programmet.


## Etapp 17 (2026-11-07): utgångarna (UT1a)

Byggt efter skissen UT1a i appens nuvarande skal, mot Network-kontrakten (`IExitStore`, `IExitConnection`); Network är
inte ändrat.

- **Utgången i titelraden** är en knapp (`Button.Chip`) med läget som prick, namnet och en liten pil, upplyst medan
  rutan är ute. Ett klick fäller ut rutan (`Views/ExitPanel`, 320 px) under den med högerkanten mot knappens. Esc, ett
  nytt klick, ett klick någon annanstans (också i titelraden), Ctrl+K, en annan vy, Nytt ärende och låsning fäller in
  den; att lämna fönstret (Windows filväljare) gör det inte.
- **Vägen** överst: datorn, utgången och källorna, grön och hel när en utgång är ansluten (med dess namn), röd och
  streckad med *Ingen* annars, också medan en utgång ansluter, eftersom inga uppslag går då.
- **Utgångarna i valvet** (`ExitPanelViewModel`, `ExitRowViewModel`): grå, gul (*Ansluter …*), grön (*Ansluten*) eller
  röd prick med orsaken ur `ExitFailure` (`Exits/ExitNames`), den valda med accentstrecket. Strömknappen väljer och
  ansluter, eller kopplar ner; att välja en annan utgång kopplar ner den förra i Network, så bara en är ansluten. Allt
  mot valvet och tunneln körs på trådpoolen. En senare anslutning eller en nedkoppling avbryter en anslutning som
  fortfarande startar (en `CancellationTokenSource` per försök, och en grind runt valet), och en äldre läsning av
  utgångarna ersätter aldrig en nyare.
- **Lägg till WireGuard-fil …**: Windows filväljare (`IFilePicker.PickWireGuardFile`, med låsningen hållen och en fil
  vald efter en låsning släppt), en snabb kontroll (`Exits/WireGuardCheck`) med *Giltig* eller vad som är fel, namnet
  ur filnamnet, *Krypteras i valvet*, Avbryt och Spara. Spara läser och kontrollerar filen igen och lägger den i valvet
  utan att välja den, så att en ansluten utgång står kvar.
- **Kontrollen** läser filen som hjälpprogrammet (`tools/Chainsaw.Tunnel/config.go`): rader vid `\n`, kommentarer,
  kända rubriker och nycklar, upprepade nycklar, en `[Interface]` och en `[Peer]` och de nycklar varje tunnel kräver,
  med samma regel för DNS-servern som `ExitStore`. Värdena (nycklar, adresser, portar) lämnas till hjälpprogrammet.
  Förstagångsinställningen använder samma kontroll.
- **WireGuard-filens text som hemlighet:** den läses utan buffert in i fästa byte och tecken (`Exits/WireGuardFile`)
  som nollställs direkt efter kontrollen och efter sparandet; rutan håller bara sökvägen mellan valet och Spara. Texten
  blir en `string` bara i anropet till `IExitStore.Add`, som tar en (TODO). Inga undantag eller filinnehåll visas;
  rutan har fasta svenska texter.

**Egna beslut och avsteg från skissen:**
- **Kontrollen av den offentliga adressen** (skissens sista rad, *Syns som …*) visas inte, eftersom den inte är byggd
  (TODO, *Kontroll av utgången*).
- **Den nya utgången väljs inte** när den sparas, så att Spara aldrig kopplar ner den som är ansluten.
- **Namnet i rutan** är utgångens namn; titelraden visar som förut platsen först (som är tom för filer som läggs till
  här). Ta bort, byta namn och ange plats finns inte i skissen (TODO).
- **Titelradens prick** är grå också vid fel, som i skissens variant A; orsaken står på raden i rutan.
- **XAML-raderna** följer projektets XAML, som har långa rader; C#-raderna håller sig kring 120 tecken.

**Granskningen:**
- Omgång 1: inga C eller I, elva M. Rättade: kontrollen godtog filer som hjälpprogrammet vägrar (okända rubriker och
  nycklar, rubrik utan `]`, rad utan `=`, nyckel före första rubriken, upprepade nycklar) och delade rader annorlunda
  (bara `\r`); en anslutning i kö kunde starta efter en nedkoppling; Ctrl+K och klick i titelraden lämnade rutan ute;
  ett läsfel kunde visas efter stängning och andra fel slapp ut; en magisk sträng; läsningar i fel ordning;
  förstagångsinställningen kontrollerade inte filen; fler tester. Långa XAML-rader lämnades (projektets stil).
- Omgång 2: inga C eller I, tre M, alla rättade: en DNS-adress inom hakparenteser (`[::1]`) godtogs av kontrollen men
  vägras av hjälpprogrammet och `ExitStore`; en rad på 64 KiB eller mer (hjälpprogrammets gräns) säger nu *För stor*;
  ersatta `CancellationTokenSource` frigörs.

**Tester:** 412 i appen (85 nya för utgångarna och kontrollen). Bygget utan varningar.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Utgångarna*); kontrollen av den offentliga adressen, ta
bort och byta namn på en utgång, ett kortkommando, raden i en vägrad fil och Windows som utgång i rutan (TODO).


## Etapp 18: Guiden (GD1b), 2026-11-07

**Byggt:**
- **Panelen bredvid arbetet** (`Views/GuidePanel`, `ViewModels/GuidePanelViewModel`): 440 px från höger över
  inspektören, utan att resten mörkas. Huvudet har pilen tillbaka, *Guide · grupp*, *Öppna i Guide* och ×. Överst
  står svaret för det valda (*Varför? · namn* och alla förklaringar, var och en med en länk till sitt eget avsnitt när
  det inte är det som visas). Under det står avsnittet, öppnat vid rubriken som svarar, markerad i accenten.
- **Varifrån panelen öppnas:**
  - *Varför?* i inspektören. Avsnittet väljs ur det mest talande svaret: ett slut går före en heuristik, som går före
    en startpunkt, en etikett och metoden.
  - Länken *Guiden: Grafen och bildspråket* bakom *?*.
  - Högerklick på spårningsmetoden, som öppnar vid metodens egen rubrik.
- **Panelen följer valet:** en annan nod blir ett steg framåt. Klustrets svar som kommer senare ersätter det tidigare
  svaret om samma sak där det ligger i historiken. Ingenting valt ändrar ingenting. Panelen stängs när ett annat ärende
  öppnas, när vyn byts, med × och med Esc. F1 med panelen öppen gör samma sak som *Öppna i Guide*.
- **När panelen är öppen** flyttas grafens hörn åt vänster om den, och kommandoraden centreras i ytan bredvid.
- **Vyn Guide (F1)** (`Views/GuideView`, `ViewModels/GuideViewModel`): avsnitten i grupperna Bitcoin, Klustring,
  Spårning och Etiketter, och texten i läsbredd. Från panelen kommer man till samma rubrik, med *Tillbaka till grafen*.
- **Avsnitten byggs in** ur `docs/guide/*.md` som resurser (`Guide/GuideLibrary`, med indexet). En egen liten
  Markdown-tolk (`Guide/GuideMarkdown`) läser rubriker med ankare, stycken, listor (också numrerade och inskjutna),
  citat, kodblock, tabeller, fetstil, kursiv, kod och länkar mellan avsnitt (`[text](fil.md#ankare)`); webblänkar
  följs aldrig. Renderaren är `Controls/GuideDocumentView`. Kodblock krymper i stället för att brytas, och *Läs också*
  visar avsnitten som texten länkar till.
- **Texterna:** hänvisningarna mellan avsnitten är nu länkar. Svaren som förut sa var texten låg är borta, och
  `ShellViewModel.ShowGuide` och den infällda Varför?-listan i inspektören är borttagna.
- **Logik utan WPF:** tolken, indexet, ankarna, historiken, förfrågningarna och båda vymodellerna, testade i
  `GuideTests` och `GraphViewModelTests`. Testerna kontrollerar att varje text är inbyggd och listad, att varje länk
  och varje rubrik grafen öppnar vid finns, och att inga tecken som `**` blir kvar.

**Egna beslut och avsteg från skissen:**
- **Metoden:** ett klick byter metod som förut, och ett högerklick öppnar Guiden.
- ***?*:** visar bildspråket som förut, och dess länk öppnar panelen. I skissen öppnade båda Guiden direkt. Står i TODO
  under *Väntar på beslut*.
- **Svaret står fast överst**, utanför avsnittets rullning, så att avsnittet kan öppnas vid rubriken utan att svaret
  rullar bort. Ett långt svar rullar i sin ruta.
- **Utan figurerna och sökrutan** ur skisserna (TODO).

**Granskningen:**
- **Omgång 1:** inga C, fem I och elva M.
  - Svarsrutan rullades bort.
  - Panelen behöll förra ärendets svar.
  - Esc stängde Guiden i stället för utgångarnas ruta.
  - Panelen täckte grafens hörn och kommandoraden.
  - F1 tappade platsen.
  - Rättade M: renderingen byggs en gång per ändring, fokusmarkering på länkar, *Tillbaka till* glöms när vyn lämnas,
    död kod borttagen, konstanter för resursnycklar och filändelsen, och en radbrytning i `coinjoin.md`.
  - Övriga M står i TODO.
- **Omgång 2:** inga C eller I.
  - Rättade M: inspektörens kolumn läses vid namn, och radlängder.
  - Kommandoraden vid minsta bredd står i TODO.

**Tester:** 433 i appen. Bygget utan varningar.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Guiden*). Beslutet om metoden och *?* (TODO, *Väntar på
beslut*). Figurerna, sökningen och det som återstår i panelen (TODO).

## Etapp 16 (2026-11-07): grafen efter GR5 (variant A) och IN1a

Startpunkten är neutral, nodernas namn har adressen under, och inspektören har formulären Etikett (L), Bryt upp (U) och
Annan kedja (K). Allt som skrivs går genom modulerna (Attribution `Add`, Clustering `Split`, Tracing
`LinkCrossChain`), som förseglar det med en rad i journalen; grafen läses sedan om ur ärendet.

- **GR5:** `GraphScene.SortOf` ger inte längre startpunkten sorten *Offer*; offret är bara en etikett. Startpunkten
  ritas med en ljus flagga uppe till vänster (`NodeBadge.StartPoint`, `Icon.Flag`, `Brush.Mark.Start`), först bland
  markeringarna i legenden, med *Startpunkt* i verktygstipset och under inspektörens rubrik.
- **Namnen:** `NodeView` har `Subject` (adressen, tx:en eller id:t i sin helhet) och `IsNamed`. Ytan skriver namnet
  och adressen nedtonad under, eller bara adressen i ljusgrått; adressen förkortas i mitten med `SubjectText.Fit`
  bara så mycket som bredden kräver (mätt med texten, början och de fyra sista tecknen kvar). Noden är kvar på
  132 × 40.
- **IN1a:** `InspectorFormsViewModel` (utan WPF) och `Views/InspectorFormsView.xaml`, under fakta i inspektören.
  Reglerna ligger i `Graph/InspectorForms.cs`, skrivvägen i `Graph/GraphEditor.cs` (`IGraphEditor`, i bakgrunden).
  L, U och K i grafen; Esc stänger formuläret före valet, Enter sparar.
  - **Etikett:** på en adress `ForAddress`, på ett kluster `ForCluster`. Sorterna är alla `LabelKind` utom
    *Sanktionerad*, offret och okänd först, sedan legendens ordning. *Okänd* med namn är ett eget namn.
  - **Bryt upp:** klustret läses med `GetCluster`, varje rad har heuristikens korta namn (*CIO v1*). `BreakOut` ger en
    `SplitRequest` per sammanslagen länk som går mellan en vald adress och en som stannar, med länkens heuristik.
    Klustret läses igen strax före sparningen (så att länkar som kommit sedan också bryts) och efteråt, och en adress
    som fortfarande är kvar sägs. Alla adresser kan inte väljas. Uppbrytningarna sparas en i taget; misslyckas en
    sägs hur många som sparades.
  - **Annan kedja:** Ethereum och TRON; Litecoin och Solana tonade med *Inte byggt än* (som i skissen). Tx:en känns
    igen när den skrivs; `0x` byter till Ethereum, men vanlig hex flyttas aldrig till TRON av sig själv (den kan vara
    båda). Mynten som kom in till aktören och inte redan är knutna erbjuds, största först; finns flera väljs ett.
- **Egna beslut:**
  - Formulären fälls ut **under fakta** (uppdraget), inte i stället för dem som i skissen.
  - Källorna *Beställningen* och *Svar från börs* sparas som egna etiketter med det först i skälet; en öppen källa
    får användarens namn på källan och *läst åååå-mm-dd* som version (TODO).
  - *Sprid inte* är tonad för *Offer*, eftersom Attribution aldrig sprider ett offers etiketter.
  - Annan kedja erbjuds bara på en adress eller ett kluster som tagit emot mynt.
- **Granskningen:** omgång 1 gav inga C, tre I (uppbrytning ur ett gammalt kluster, Enter klickade på en rad i stället
  för att spara, egen etikett *Sanktionerad* ritad som en träff i listan) och sex M; alla rättade utom M4 (test mot
  den riktiga klustringen), som står i TODO. Omgång 2 gav ett I: uppbrytningens egna varningar kunde försvinna med
  formuläret när grafen lästes om; nu läses grafen om först efter kontrollen, och det ett formulär som stängs har att
  säga flyttas till grafens rad. Fyra M rättade: en kontroll som inte gick att läsa sägs som sådan, bara den senaste
  läsningen av klustret visas, Enter sparar också efter ett klick på knapparna eller *Sprid inte*, och Esc stänger
  ett öppet formulär före Guidens panel (etapp 18). Omgång 3 gav inga C eller I; av två M rättades att en läst vägran inte kommer
  tillbaka i grafens rad (bara uppbrytningens varningar gör det, och läggs till raden i stället för att ersätta den),
  och den andra (Esc när fokus står utanför formuläret och grafen) står i TODO.
- **Återstår:** ta tillbaka en etikett eller länk i grafen och ångra en uppbrytning (behöver skiss), egna källor för
  beställningen och börsens svar. Inte provat på Windows (`docs/TESTER-INFOR-1.0.md`).


## Etapp 19 (2026-11-07): inställningarna (ST1a) och Windows som utgång

**Network: Windows eller VPN**
- `ExitRoute` (Vpn, Windows) och `IExitRoute` (`ExitRouteChoice`) i Network. Vägen vid start kommer ur
  `NetworkOptions.Route`, som appen sätter ur `settings.json`. Förvalet är VPN.
- `ExitHttp` tar en lease genom Windows bara när Windows är valt, under samma lås som byter vägen. Annars tar den
  utgångens lease, som förut.
  - Med VPN valt och ingen ansluten utgång vägras uppslaget innan något byggs. Windows-vägens pipeline byggs aldrig
    (testat).
  - När Windows lämnas avbryts uppslagen som pågår genom den, och pipelinen släpps.
  - Pipelinen byggs utanför låset (den läser certifikatarkiven), och ett fel där blir `ExitException(RequestFailed)`.
- **Pipelinen genom Windows** (`WindowsHandlerFactory`):
  - Den går genom systemets proxy (`HttpClient.DefaultProxy`, också PAC och automatisk identifiering).
  - Inga kakor, inga omdirigeringar, inga inloggningsuppgifter och inga spårhuvuden. HTTP/1.1 och inga återupptagna
    TLS-sessioner.
  - Samma kedjepolicy utan nedladdningar (AIA, spärrlistor, Windows Update).
  - Vid en direkt anslutning till källan slås namnet upp här, och adresser som inte är globala vägras, som i tunneln.
- **Princip 1, med minsta ingreppet i kontrakten:**
  - `ExitResponse` fick en positionell `Route` och de två hjälparna `ExitIdText` och `RouteText`.
  - Chains och Pricing förseglar en ny kolumn, `exit_route` (`vpn` eller `windows`). `exit_id` är NULL genom
    Windows.
  - Schemats steg 1 ändrades i stället för ett nytt steg (före 1.0 får schemat ändras). Gamla testärenden öppnas inte.
- **Pricing:** `IPriceDefaults` gör att valutan och upplösningen ur inställningarna gäller nästa uppslag utan omstart.

**Dialogen ST1a** (`Views/SettingsDialog`, `ViewModels/SettingsViewModel`, `Settings/SettingsMenu`)
- **Öppna och stänga:**
  - Ctrl+, eller kugghjulet i pillret öppnar dialogen över vyn som visas. Vyn byts inte, och pillrets markering står
    kvar.
  - Esc, ×, ett klick på det mörka, Ctrl+K, en annan vy, Nytt ärende och låsning stänger den.
  - Med Nytt ärende öppet öppnas den inte.
- **Nätverk:**
  - Korten Windows och VPN.
  - Under VPN står utgångarna genom samma `ExitPanelViewModel` som rutan i titelraden (`ShowInSettingsAsync`,
    `LeaveSettings`), med steget *Lägg till WireGuard-fil*.
  - Raden *Bara https · Alltid*.
- **Låsning:** 5, 15 och 30 min och 1 h (`AutoLock.Change`, räknat från senaste aktivitet), och brytaren *När Windows
  låses*. Viloläge och en frånkopplad session låser alltid.
- **Kurser:** SEK, USD eller EUR, minut, timme eller dag, och källorna.
- **Profil:**
  - Namn, titel och enhet läses när dialogen öppnas. De sparas när ett fält lämnas, med Enter och när dialogen
    stängs.
  - Ett tomt namn sparas inte.
  - *Byt …* fäller ut tre lösenordsfält mot `IVault.ChangeMasterKey`. Tecknen töms alltid.
- **Om Chainsaw:**
  - Version och commit.
  - Återställningsnyckelns fingeravtryck, eller *Ingen inbyggd nyckel*.
  - *Visa* fäller ut licenserna ur `THIRD_PARTY.md`, som byggs in och visas med Guidens renderare.
- **Utseende och Länkar** är tonade med *Inte byggt än*.
- **Ändringar gäller direkt** och skrivs till `settings.json` utanför UI-tråden, en skrivning i taget. Valen sparas
  som ord (`"ExitRoute": "Windows"`), och okända värden blir förval (VPN).
- **Läget Windows:**
  - Titelraden säger *Windows* (grön prick) och statusraden *Uppslag går genom Windows*.
  - Rutan UT1a visar vägen dator → Windows → källor. Utgångarna är tonade, med *Inställningar …* till sidan Nätverk.
  - Låsskärmen har en egen rad, och grafen är inte offline (`ExitWay` samlar vägen och utgångens läge på UI-tråden).

**Egna beslut:**
- **Inga Windows-uppgifter till proxyn** (`DefaultProxyCredentials` tomt): en proxy som hittas med WPAD kunde annars
  få dem. Står i TODO.
- **En ansluten utgång står kvar** när Windows väljs. Den används inte, men den används igen när VPN väljs.
- **Formuläret för huvudnyckeln** finns inte i skissen. Det står i TODO under *Väntar på beslut*.
- **Förstagångsinställningen** erbjuder inte valet. Det kräver en ändring av FS1a (TODO).
- **Inställningar i pillret är en dialog**, inte en sida, så `Show(AppView.Settings)` öppnar dialogen.
- **Om valet VPN inte kan skrivas** tas en `settings.json` som säger Windows bort, så att nästa start blir VPN.

**Granskningen:**
- **Omgång 1:** inga C, två I och tretton M. Inget uppslag kan gå direkt när VPN är valt.
  - **I:**
    - En profil som inte gick att spara försöktes aldrig igen, och felet försvann.
    - Ett misslyckat VPN-val kunde ge Windows vid nästa start.
  - **M rättade:**
    - Dokumentationen om pågående uppslag.
    - `Choose` efter dispose.
    - Pipelinen byggs utanför låset, och ett fel där är ett `ExitException`.
    - Upptaget-flaggan för huvudnyckeln vid en andra tryckning.
    - `ExitWay` följer innan den läser.
    - Ett oväntat fel stoppar inte senare sparningar.
    - En profil som skrivs sparas när dialogen stängs.
    - Radlängder.
  - **M i TODO:** felens texter om utgången genom Windows, grön prick med Windows utan nät, en ansluten utgång i läget
    Windows, och intranätnamn genom en proxy.

- **Omgång 2:** inga C eller I. Sju M.
  - **Rättade:**
    - Kugghjulet medan dialogen är öppen byter bara sida, så att profilen inte läses om och skriver tillbaka gammalt.
    - En fil som inte kan läsas strikt som VPN tas bort när ett VPN-val inte kunde skrivas.
    - Bara den senaste skrivningens fel visas.
    - Tester för valet av VPN som inte kan skrivas och för VPN som väljs medan Windows-pipelinen byggs.
  - **M i TODO:** strängare låsning som inte kan skrivas, profilens fel efter stängning och sparningarnas ordning.

**Tester:** Network 227 (ExitRouteTests), App 504 (SettingsTests), Chains och Pricing med den förseglade vägen.
1 651 i hela lösningen. Bygget utan varningar.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Inställningarna och Windows som utgång*), Utseende och
Länkar, valet i förstagångsinställningen, och besluten i TODO.

## Etapp 20 (2026-11-07): grafen och spårningen oberoende av kedjan

**Byggt:**
- **Tillgångar och belopp (SharedKernel):**
  - `Asset` är en kedjas eget mynt (BTC, ETH, TRX) eller en token med kontrakt eller mint och decimaler. Två
    tillgångar är samma när kedjan, sorten och kontraktet är det, aldrig efter symbolen. Ethereums kontrakt jämförs med
    små bokstäver; TRON och Solana skrivs som de är (base58 skiljer på stora och små).
  - `AssetAmount` är ett belopp i minsta enhet som `BigInteger` (wei kräver mer än 64 bitar). Belopp av olika
    tillgångar kan aldrig adderas, jämföras eller blandas; det kastar. `Satoshi` finns kvar för Bitcoin, med
    `ToAmount()` och `ToSatoshi()`.
  - `LedgerModel` (UTXO eller konton), `Chain.Model()` och `Chain.NativeAsset()`, `ChainAddress` (adress med kedja)
    och `ChainScope` (en modell och alla eller några av dess kedjor).
- **Grafen (Tracing):**
  - Kanterna är överföringar av en tillgång (`AssetTransfer`: tx som `ChainTransactionRef`, belopp, block och, på en
    UTXO-kedja, myntet som kedjans egen detalj). En kant summerar per par, sort och tillgång.
  - Det som bara UTXO-modellen har ligger i `TraceGraph.Utxo` (`UtxoGraph`: tx, spenderade mynt och slut vid tx).
    `Transactions`, `Spends` och `TransactionEnds` finns kvar på grafen och pekar dit.
  - Kantens nyckel är densamma som förut för bitcoin. Ett annat mynt lägger till kedjan, och en token kedjan och
    kontraktet.
  - Spårningens belopp (`TracedAmount`, `TraceResult.Traced`, `TracedTransaction`, `TraceSeed.Amount`) är
    `AssetAmount`.
- **Ett kontrakt per sorts kedja:**
  - `ITracingMethod` har namn, version och `Scope`. `IUtxoTracingMethod` (FIFO och haircut, för alla UTXO-kedjor)
    får bara beloppen (`UtxoFlow`). `IAccountTracingMethod` får ett kontos rörelser i en tillgång (in, ut och avgift)
    och ger det spårade i varje utgående överföring, i avgifter och det som ligger kvar.
  - `IClusteringHeuristic` har namn, version och `Scope`. `IUtxoHeuristic` har `Analyse` som förut. De fyra
    heuristikerna är registrerade för Bitcoin.
  - Varje kedja får de metoder och heuristiker som täcker den. En metod eller heuristik för en annan modell körs
    aldrig på Bitcoin och nämns inte i dess journal.
- **Appen** visar beloppen ur tillgångens egna decimaler och symbol (`AmountText.Number`, `WithUnit`). För BTC är
  texten exakt som förut. Nodernas summor och linjernas tjocklek räknas i Bitcoins mynt (`GraphScene.Shown`) tills
  tokens byggs efter TK1a.

**Bitcoin oförändrat:** ett facit spelades in **före** omskrivningen (commiten *Record Bitcoin's graph, traces and
sealed rows*). Det skriver ut skissernas påhittade ärende i sin helhet: grafen, FIFO och haircut med varje mynt, tx,
kant, slut och *Varför?*, journalen och varje rad i ärendet. Ärendets slumpade id och hashkedjans hashar maskeras.
`GoldenTests` jämför med facit, en gång som det är och en gång med en kontometod och en kontoheuristik med samma namn
som Bitcoins registrerade bredvid. Båda är lika. Inga förseglade källor eller scheman är ändrade.

**Egna beslut:**
- **`UtxoGraph` håller Bitcoins typer** (`Txid`, `Outpoint`, `BitcoinTransaction`), och `TraceCalculator` räknar i
  BTC. Spårningens resultat (`TraceEnd`, `TracedCoin`, `TracedTransaction`) och grafens noder har kvar `Txid` och
  `BitcoinAddress`. Att göra dem neutrala nu hade ändrat det mesta av appen och testerna utan att någon kedja kunde
  använda det. Det som återstår, och vid vilken kedja det behövs, står i TODO under *Kedjor och data*.
- **UTXO-heuristikerna läser `BitcoinTransaction`**. Nästa UTXO-kedja ger kontraktet en egen tx och egna adresser.
- **Kontomodellens heuristikkontrakt** byggs med Ethereum, när kluster och bevis kan hålla andra adresser.
- **En tillgång i grafen** visas: nodernas in och ut summerar bara Bitcoins mynt tills tokens byggs.
- **`UtxoFlow` vägrar outputs som är större än inputs.** Förut gav FIFO avgiften 0 för sådana uppgifter. Det kan
  inte hända på kedjan, bara i ett trasigt svar, och spårningen vägras då med ett fel i stället för att räknas.
- **Metoder och heuristiker kontrolleras för den kedja de täcker.** En felregistrerad metod för Ethereum stoppar
  inte Bitcoin.

**Granskningen:**
- **Omgång 1:** inga C, tre I och tio M. Inget ändrade Bitcoins resultat, texter, förseglade rader eller journal.
  - **I:**
    - `Asset` jämfördes också på symbolen, så samma token med två stavningar gav två kanter med samma nyckel.
    - Kontokontraktet saknade avgifter (gas, bränd energi).
    - Det som fortfarande är bundet till Bitcoin stod inte nedskrivet.
  - **M rättade:**
    - Kantens nyckel för ett annat mynt än bitcoin får kedjan.
    - `default(AssetAmount)` säger tydligt att den saknar tillgång.
    - Felet när en metod ger belopp som inte ryms.
    - Metoderna kontrolleras per kedja, som heuristikerna.
    - `ChainScope.Chains` går inte att ändra utifrån.
    - Radlängder.
  - **M i DEV-LOG eller TODO:** `UtxoFlow` som vägrar, ett negativt belopp i `Parse` (bara som skillnad, nu
    dokumenterat), facit med fler fall och ett reguljärt uttryck per överföring.
- **Omgång 2:** inga C eller I. Sju M.
  - **Rättade:** belopp av samma token med olika decimaler räknas inte ihop, en metod eller heuristik utan räckvidd
    får ett eget fel, en startpunkt med ett belopp utan tillgång vägras som de andra, och kontraktets text om
    kontrollen gäller UTXO-modellen i dag.
  - **M i TODO:** TRON:s två skrivsätt för kontrakt, att `AccountLedger` kontrollerar sina regler, och appens delar
    som bara räknar Bitcoin.

**Tester:** 1 690 gröna i hela lösningen efter rebasen på etapp 19, varav 39 nya för etappen: 2 i facit, 18 i
SharedKernel (tillgångar, belopp med stora tal, adresser och räckvidder), 9 i Tracing (metoder per modell och kedja,
kontomodellen, kantens nyckel, `UtxoFlow`), 3 i Clustering (heuristiker per kedja) och 7 i appen (belopp i andra
tillgångar). Bygget är utan varningar.

**Återstår:** inget att prova på Windows utöver det som redan står där; gränssnittet ska se ut som förut. Det som
fortfarande är bundet till Bitcoin står i TODO under *Kedjor och data*.

## Etapp 21 (2026-11-07): ta tillbaka och ångra (AN1b)

Inspektören har flikarna **Fakta** och **Historik · N**. Historiken visar nodens händelser i tidsordning, och det
användaren själv sparat kan tas tillbaka eller ångras som en **ny förseglad post** med vem, när och ett frivilligt skäl.
Inget tas bort; det gamla står kvar överstruket och tonat, med tillbakatagandet som en egen rad.

- **Attribution:** `Retract` tar ett frivilligt skäl, och tillbakatagandets post (samma kedja `attribution.labels`)
  sparar nu vem och när i de kolumner som fanns. `GetHistory(case, adresser)` ger etiketterna med sitt
  tillbakatagande (`LabelHistoryEntry`, `LabelRetraction`); `CanRetract` gäller egna etiketter och öppna källor.
  Biblioteket drar tillbaka en etikett överallt bara när den var användarens egen (egen eller öppen källa); en kopia, en
  sanktionsträff eller en analys etikett som tas tillbaka lämnar bara biblioteket från det ärendet.
- **Clustering:** schemasteg 2 med tabellen och den förseglade kedjan `clustering.split_undos`. `UndoSplits` ångrar
  alla eller inga under en journalrad (`clustering.split-undone`), `PreviewUndoSplits` säger utan att spara vilka
  adresser som slås ihop igen och med vilken heuristik (samma regler som klustren byggs med), och `GetSplitHistory`
  ger uppbrytningarna med sin ångring. Klustren räknar bara uppbrytningar som inte är ångrade. **En uppbrytning
  sparas nu som en enda:** `Split(case, lista)` förseglar alla eller inga under en journalrad, och appen använder den.
- **Tracing:** `UnlinkCrossChain` tar ett frivilligt skäl (sparat som tom text, eftersom kolumnen är text) och
  `GetCrossChainHistory` ger länkarna med sitt tillbakatagande.
- **Appen:** `Graph/NodeHistory.cs` skriver raderna (*Först in* med blocket, *Klustrad* per heuristik, etiketter,
  *Uppbruten · N ut* per journalrad, länkar och tillbakatagandena), `InspectorHistoryViewModel` (utan WPF) flikarna,
  formuläret och bekräftelsen, `GraphEditor` läser historiken och skriver genom modulerna, och efter en tillbakatagen
  etikett körs `ILabelLibrary.Absorb` direkt. Går biblioteket inte att uppdatera sägs det i grafens rad.
  `Views/InspectorHistoryView.xaml` ritar raderna på en tunn tidslinje med ikonens ram streckad för det automatiska.
  Enter sparar, Esc avbryter (också från grafen), och L, U eller K byter till Fakta.
- **Guiden:** *Historik: ta tillbaka och ångra* i `grafen.md`.
- **Facit från etapp 20** (`Golden/bitcoin-flow.txt`) är uppdaterat för klustringens nya schemasteg och kedja; inget
  annat i det skiljer.

**Egna beslut:**
- **Bekräftelsen är formuläret självt:** *Ångra* fäller ut *Slås ihop igen* med adresserna och heuristiken, och knappen
  går inte att trycka förrän listan är läst. Högst 12 adresser listas, resten räknas (*+N till*). Slås inget ihop
  (en annan uppbrytning håller, eller ingen heuristik binder) sägs det, och ångringen går ändå att spara.
- **Skälet är frivilligt** för att ta tillbaka och ångra (användarens beslut), men fortfarande krav när något sätts.
- **En uppbrytning är en journalrad:** etapp 16 sparade en uppbrytning per länk; nu sparas de tillsammans så att de kan
  visas och ångras som en. Äldre ärenden visar en rad per länk (schemat får ändras före 1.0).
- **Tjänsten låter fortfarande sanktionsträffar och kopior tas tillbaka** (etapp 8, testat); i historiken har bara de
  egna en knapp, som skissen säger.
- **Hämtad** för en adress visas inte: Chains har inget sätt att läsa en adress ur ärendet utan att fråga källan. I
  stället står *Först in* med blocket (ett faktum ur kedjan). TODO.
- **Egen yta** finns inte än (etapp 22 bygger dem samtidigt); att ta tillbaka en sådan står i TODO.

**Granskningen:** tre omgångar.
- **Omgång 1:** inga C, tre I och fem M. I: en uppbrytning grupperades per heuristik och kunde bara ångras till hälften;
  *N ut* ändrades efter en ångring och från andra sidan; en tillbakatagen etikett från en analys drogs tillbaka ur
  biblioteket överallt. M: skör gruppering, lång bekräftelse, en omläsning stängde formuläret, korta namn på
  heuristiken; en kollegas egen etikett räknas som användarens (frågan står i TODO). Alla utom den sista rättade.
- **Omgång 2:** inga C, ett I och fem M. I: en uppbrytning vars länkar inte delade adress lästes inte hel från den
  utbrutna adressen; rättat genom att läsa per journalrad i hela historiken. M rättade: gamla texter efter
  alla-eller-inga, en omläsning under en pågående sparning, en gammal bekräftelse som stod kvar; två står i TODO.
- **Omgång 3:** inga C eller I. Tre M rättade (en gammal `<returns>`, en gren som inte kunde nås, historiken lästes två
  gånger); ett test för omläsning under en pågående sparning står i TODO.

**Tester:** 1 739 gröna i hela lösningen, varav nya i Attribution (historik, frivilligt skäl, biblioteket), Clustering
(ångra, bekräftelsen, alla-eller-inga), Tracing (länkarnas historik) och appen (raderna, flikarna, formuläret,
bekräftelsen, biblioteket). Bygget är utan varningar.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Historik, ta tillbaka och ångra*).

## Etapp 24 (2026-11-07): källornas endpoints per kedja (KL1a)

**Byggt:**
- **Network:** `ExitRequest.Credential` (`ExitCredential`) skickar en källas API-nyckel som ett huvud. Bara
  `Authorization` och `X-Api-Key` tillåts, och värdet får bara innehålla skrivbar ASCII (inga radbrytningar). Annars
  vägrar `ExitHttp` innan något byggs. `ToString` döljer nyckeln. Nyckeln står aldrig i adressen och förseglas alltså
  aldrig.
- **Chains, endpoints per kedja** (`IChainSources`, `ChainSources`):
  - **Endpointen:** namn, https-adress, sort av API, namnet på en API-nyckel i valvet (aldrig nyckeln) och om den är
    påslagen.
  - **Filen:** endpointarna sparas i `sources.json` i appdata (`ChainSourcesOptions.Directory`), aldrig i ärendet.
    Skrivningen går via en temporär fil och en i taget, med läget när skrivningen börjar. En trasig fil ger förvalen.
    En rad med `http`, en okänd sort eller en sort för en annan kedja hoppas över.
  - **Kontroll av adressen:** `SourceAddress.TryRead` tar bara https utan användare, frågor och `#`.
- **Förval:** Bitcoin har mempool.space och Blockstream med fasta id, i läget Snabbast
  (`BitcoinSourceOptions.DefaultEndpoints`; `EsploraBaseUri` sätter en enda).
- **Sorter av API** (`ISourceApi`, `EsploraApi`):
  - En sort säger hur höjden och ett blocks hash frågas efter och hur nyckeln skickas. Esplora skickar den som bearer.
  - Ethereum och andra kedjor lägger bara till sin sort i `AddChainsModule`, och `Chains` (sidans chips) följer de
    registrerade sorterna.
- **Prov:**
  - `ProbeAsync` frågar alla påslagna endpoints parallellt, genom `IExitHttp`, bara efter `blocks/tip/height`, med 10 s
    gräns per endpoint. Den lägsta höjden under den högsta räknas som *efter* (`BlocksBehind`).
  - En omgång sparas inte i dessa fall, och de förra resultaten står kvar:
    - när utgången vägrar (`ExitNotConnectedException`);
    - när valvet är låst eller låses under omgången;
    - när omgången avbryts;
    - när en nyare omgång har börjat.
  - `TryAsync` (formulärets *Prova*) frågar också efter hashen vid den höjden (`block-height/:h`) och sparar inget.
- **Lägena:**
  - **Snabbast:** ordningen är de som svarade rätt (snabbast först), de som inte är provade, de som ligger efter och
    sist de vars prov misslyckades.
  - **Prioriterad:** användarens ordning, med de vars prov misslyckades efter de andra.
  - I båda lägena kommer en endpoint som ett uppslag misslyckades genom sist, tills den provas igen eller svarar.
  - **Av:** inga uppslag.
- **Källan för Bitcoin:**
  - `EsploraBitcoinSource` frågar endpointarna i den ordningen. Nästa endpoint frågas vid fel, vid timeout, när ett
    svar inte går att använda och när nyckeln saknas.
  - Varje förseglat råsvar, också de som misslyckades, har `endpoint_id` och `endpoint_name`, och `source` är sorten.
    Schemats steg 1 ändrades (före 1.0). `ResponseOrigin` har `EndpointId` och `Endpoint`.
  - **Av** ger *Offline* med ärendets svar och texten att källorna är avstängda.
- **Appen:**
  - **Sidan Källor** efter Nätverk (`SourcesViewModel`, `EndpointRowViewModel`, i `SettingsDialog.xaml`). Den har:
    - chips per kedja: Bitcoin, och Ethereum, TRON och Solana tonade med *Inte byggt än*;
    - läget och raderna med prick, adress, stapel och ms, ★, den gula markeringen *N block efter* (bara i Snabbast),
      brytare och ×;
    - handtag, platsnummer, pilar och Alt+↑/↓ i Prioriterad, där fokus följer raden;
    - *Lägg till endpoint …* med *Bara https.* direkt, sort, nyckel ur valvet och *Prova*;
    - *Provade hh:mm* och *Prova alla nu*.
  - **Provet vid start** görs en gång, när valvet är upplåst och utgången tillåter uppslag. En omgång där inget kunde
    skickas görs om nästa gång utgången eller valvet ändras.
  - `ExitWay.ChainsOff` följer läget Av, och grafens hörn säger *Av* med förklaring på hovring.

**Egna beslut:**
- **Formen på provet:** provet frågar bara efter höjden. Formulärets *Prova* frågar också efter hashen vid den
  höjden, så att höjd och hash hör ihop (skissen visar båda).
- **En endpoint som ligger efter** används sist i stället för aldrig. Annars kunde ett avstängt eller dött snabbaste
  lämna kedjan utan källa (granskningen). Den gula markeringen säger *Används bara när ingen annan svarar*.
- **När *finns inte* gäller:** *finns inte* (404) tros bara från en endpoint vars senaste prov svarade rätt, eller
  från en som inte är provad före kedjans första omgång. Annars frågas nästa. NotFound ges bara då, eller när alla
  tillfrågade sa 404. En trasig endpoint kan därmed aldrig säga att en tx inte finns, och en txid skickas inte till alla.
- **Nyckeln:** den skickas som `Authorization: Bearer`. Bara två huvuden tillåts i Network, så en nyckel aldrig kan
  sätta kakor, referer eller värd.
- **Ingen journalrad per prov:** proven skriver inget i ärendet, eftersom de inte säger något om det.
- **Solana** står som chip trots att `Chain` saknar Solana (skissen visar det). Att dra en rad med musen är inte
  byggt, och namnet tas ur värden (TODO).
- **Mindre konflikter med andra etapper:** sidan ligger i `SettingsDialog.xaml` (resurserna där behövs), och
  `SettingsViewModel` och `ExitWay` tar sidan och källorna som valfria argument, så att etapperna 21 och 22 inte
  behövde ändras.

**Granskningen:** tre omgångar och en sista rättelse.
- **Omgång 1:** inga C, tre I och sex M. Ingenting gick förbi utgången, och http eller nycklar läckte inte.
  - **I:**
    - En endpoint efter i höjd föll ur för gott.
    - En 404 från en trasig endpoint avslutade bytet till nästa.
    - Chipet och ★ var gröna när inget svarade.
  - **M:**
    - *Prova* kunde krascha om valvet låstes mellan frågorna.
    - Låset kontrollerades en gång per omgång.
    - En omgång som inget skickade kastade en pågående.
    - En saknad nyckel flyttade inte ner endpointen.
    - Markeringen *efter* visades i Prioriterad.
    - Radlängd.
- **Omgång 2:** inga C, ett I och fem M.
  - **I:** en otrodd 404 följd av ett fel gav ändå *finns inte*.
  - **M rättade:**
    - Proven före första omgången skickade en okänd txid till alla.
    - En omgång där valvet låstes sparades.
    - ★ i Prioriterad följde inte ordningen.
    - De döda före de som ligger efter, och chipets färg för en som ligger efter.
  - **M i TODO:** varför en saknad nyckel ger röd prick.
- **Omgång 3:** inga C eller I. Två M, båda rättade:
  - en endpoint som läggs till efter en omgång tros inte på sin 404 förrän den provats;
  - en omgång under vilken valvet låstes och låstes upp kastas också.

**Tester:** 1 800 gröna i hela lösningen efter rebasen på etapp 21. Av dem är 59 nya:
- 39 i Chains (endpointarna, lägena, proven, nyckeln, filen och uppslagen med byte av endpoint);
- 2 i Network (nyckeln som huvud och vägrade huvuden);
- 18 i appen (sidan Källor, provet vid start, Av i grafen).

Bygget är utan varningar.

**Återstår:**
- Prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Källornas endpoints*).
- Nytt prov under dagen, marginal för *efter* och en egen nod på det lokala nätet (TODO, *Bitcoin-källan*).
- Ethereums sort av API i etapp 23.

## Etapp 22 (2026-11-07): kontroller och förslag (KF1a) med områdena (KF1b)

**Byggt:**
- **Modulen Checks** (ny, `src/Modules/Checks`): `ICheckAnalysis` är en kontroll eller ett förslag som en analys med
  namn och version (`HeuristicId`), räckvidd per kedja och ett svenskt namn, som heuristikerna (princip 5).
  `ICheckService` kör analyserna på grafen ur ärendet, förseglar det som är nytt eller ändrat, förseglar användarens
  beslut och håller de egna ytorna. Registreras med `AddChecksModule`.
  - **Förseglat** i tre kedjor: `checks.results` (analys, version, nyckel, sort, fyndet som JSON med underlaget: tx med
    blockhöjd, blockhash och tid, och ett fingeravtryck), `checks.decisions` (godkänt eller avfärdat, kandidat, valfritt
    skäl, vem och när) och `checks.areas` (egna ytor: rita, byta namn, ta bort med valfritt skäl).
  - En körning som hittar något nytt ger **en rad i journalen** (`checks.ran`); ett resultat som inte ändrats förseglas
    inte igen. Ett beslut och varje ändring av en yta får sin egen rad.
  - **Ett beslut gäller sin version:** det nyckeln är analysens namn, version och fyndets nyckel. Ett avfärdat förslag
    kommer inte tillbaka i samma version; en ny version frågar igen. Det senaste beslutet om samma kandidat gäller.
  - En analys som misslyckas säger det i körningens problem, och de andras resultat förseglas ändå.
- **Fyra analyser** (facit med syntetiska tx i `tests/Chainsaw.Modules.Checks.Tests`):
  - `mixer-outputs v1` (förslag): för varje insättning till en adress som en etikett säger är en mixer prövas varje
    senare utbetalning i grafen. Avgiften 0–10 % (full poäng 0,5–5 %), högst 7 dygn senare, inte insättarens egna
    pengar och inte mixerns egna adresser. Poäng 0–1: beloppet 0,5, tiden 0,3, adresstypen 0,2; under 0,4 visas inte,
    högst fem per insättning. Varje kandidat har sitt skäl i klartext.
  - `coinjoin-area v1` och `peel-chain-area v1` (kontroller): klustringens CoinJoin- och peel chain-fynd, den högsta
    versionen, som kontroller med område; en peel chains hopp slås ihop till en kedja.
  - `wallet-proposals v1` (kontroll, bara område): klustringens förslag om samma ägare som inte slogs ihop.
- **Listan KF1a** (`ViewModels/ChecksViewModel`, `CheckRowViewModel`, `Views/ChecksPanel`): *Förslag · N* i grafens
  hörn och Ctrl+J fäller ut listan där inspektören står, i grupperna *Att avgöra*, *Gjort av sig självt* och *Avgjort*.
  J och K stegar, Enter godkänner (*Godkänn bästa* först, eller den kandidat man klickat på), X avfärdar, ? öppnar
  Guiden med *Varför?* (analysens förklaring, varje kandidats skäl och när det förseglades). Ett valfritt skäl skrivs i
  raden. Den valda raden tonar ned resten av grafen och visar bara sitt område. Inspektören visar *Här*.
- **Områdena KF1b** (`Graph/GraphAreas`, `AreaGeometry`, `AreaLooks`, `Controls/GraphCanvas.Areas.cs`): förvirring
  (rött för mixer och CoinJoin, sand för peel chain), plånböcker (grått), tjänster (blått, när samma tjänsts etikett
  sitter på flera aktörer), annan kedja (lila, användarens länkar) och tid (prickat, pengar inom två timmar). Menyn
  *Områden i grafen* väljer sorterna. Högst en fyllning per aktör; ett område på en fyllning ritas som kant närmare
  aktörerna. Streckad kant för ett förslag som väntar. Brickan på kanten har ikonen; namnet syns när den väljs.
  Streckade linjer i accentfärgen från mixern till kandidaterna, med poängen. Ytan ritar bara det som syns.
- **Egna ytor:** Ctrl+klick väljer aktörer (accentram), Z öppnar formuläret (`Views/AreaForm`) med namn, sort, färg,
  *Samma aktör?* och anteckning. Ytan har hel kant och en person på brickan, sparas med vem och när, och följer
  adresserna (inte noderna). Pennan och korgen på en vald egen ytas bricka byter namn och tar bort.
- **Guiden:** *Kontroller och förslag*, *Mixerns troliga utgångar* och *Områden i grafen*, i en ny grupp *Förvirring*.

**Egna beslut och avsteg från skissen:**
- **En egen modul** (Checks) i stället för att lägga kontrollerna i Clustering eller Tracing: de läser båda, och
  etapperna 21 och 24 arbetar i de modulerna samtidigt. CLAUDE.md:s träd har fått modulen.
- **Gruppen *Avgjort*** finns inte i skissen. Ett avgjort förslag försvinner annars ur listan, och beslutet ska kunna
  ses; den visas bara när något är avgjort.
- **Plånböckerna** listas inte (skissen har dem bara som område) och kan inte godkännas, eftersom en sammanslagning av
  användaren inte finns i Clustering (TODO).
- **Mixern** känns igen på att spåret tar slut vid en mixer (en etikett). Radens rad under titeln får mixerns namn när
  aktören har ett.
- **Ett godkänt förslag** står kvar streckat (*streckat betyder alltid osäkert*); det blir inte en del av spåret (TODO).
- **Formuläret** står under grafens hörn, inte vid aktörerna som i skissen, och **lasso** är inte byggt (TODO).
- **Menyns val** sparas inte i ärendet.
- **Tröskelvärdena** för mixerns kandidater och tidsområdet är valda av sessionen (TODO, *Väntar på beslut*).

**Granskningen:**
- **Omgång 1** gav ett C, två I och åtta M:
  - **C:** listans panel dolde aldrig inspektören (bindningen läste `Checks.IsOpen` mot listans egen DataContext).
  - **I:** ett andra Enter eller X under omläsningen efter ett beslut kunde avgöra samma kandidat igen och ändra ett
    godkännande till avfärdat. Nu är listan upptagen tills kontrollerna lästs om.
  - **I:** analysen klippte till fem kandidater utan att veta om besluten, så en godkänd kandidat kunde försvinna när
    bättre kom, och avfärdade tog platser. Nu ger analysen alla, och `CheckService` behåller varje avgjord kandidat och
    de fem bästa som väntar.
  - **M rättade:** avfärdande per kandidat i stället för per adress, fyllningen räknas efter menyns val, ett förslag
    är alltid streckat (också avgjort), ärendet kontrolleras efter varje väntan i formuläret och en borttagen yta sägs,
    svenska felmeddelanden och färgnamn, högst 10 000 adresser i en egen yta, en insättning på noll vägras och ett fynd
    som inte kan förseglas fäller bara sin analys, kandidaternas linjer ritas bara när de syns, och tester för upptaget,
    sena svar och menyn.
- **Omgång 2:** inga C eller I. Av tre M rättades att formuläret kontrollerar ärendet också när ytan var borta, och
  `CheckFinding` säger hur ett förslags adresser och underlag är ordnade; två står i TODO (en avgjord kandidat som
  analysen inte längre ger, och beslut mellan läsning och förseglning).

**Tester:** 48 i Checks (facit för mixerns kandidater, CoinJoin, peel chain och plånböcker med den riktiga
klustringen, förseglingen, besluten, versionerna och egna ytor) och 44 nya i appen (områden, geometri och listans
vymodell). Bygget är utan varningar.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Kontroller, förslag och områden*); resten av katalogen
under *Förvirringstekniker* och punkterna under *Kontroller, förslag och områden* i TODO.

## Etapp 23 (2026-11-07): Ethereum och ERC-20, första delen (TK1a)

**Byggt:**
- **Kontraktet för Ethereum** (Chains.Contracts): `IEthereumSource` med en adress historik (tx, interna tx och
  ERC-20-överföringar, var för sig och sida för sida bakåt från ett block), en tx med alla sina rörelser (ether, interna
  och tokens med kontrakt och decimaler), blocket på en höjd (hash och tid) och kedjans höjd.
  - `EthereumAddress` kan bara skapas genom kontroll: `0x` och 40 hex, och en adress i blandade versaler måste ha rätt
    EIP-55-kontrollsumma. `Keccak256` är Ethereums egen (inte SHA-3) och testas mot en oberoende Python-implementation
    (`Ethereum/keccak.py`) och EIP-55:s egna exempel.
  - `EthereumTransfer` (sort, från, till, `AssetAmount`, position, block), `EthereumTransaction` (avgift, lyckad,
    `IsComplete`), `EthereumAddressPage` (avgifter, nästa sida, `IsCutShort`, `IsIncomplete`), `EthereumBlockReference`.
- **Blockscout som sort av API** (`BlockscoutApi`, `blockscout`): Etherscan-kompatibla `?module=…` för historik, tx
  (`gettxinfo` med loggarna), interna tx och tokens, och `v2/blocks/:höjd` för blockets hash. Öppet utan nyckel, går
  att köra själv. Förval `https://eth.blockscout.com/api/` i läget Snabbast; sidan Källor visar Ethereum otonat.
- **Uppslagen** går genom `SourceLookups`, motorn som bröts ut ur `EsploraBitcoinSource` så att båda kedjorna följer
  samma regler: endpoints i lägets ordning, bara `IExitHttp`, varje råsvar förseglat med endpoint, utgång och tid innan
  det används (också felsvar), *finns inte* bara från en endpoint som kan tros, och ärendets svar offline.
  Ethereums svar ligger i samma förseglade tabell med kedjan `ethereum-mainnet`; inget schema ändrades.
- **Det som aldrig hämtas igen:** en tx, dess interna tx och blocket när blocket är 64 djupt (slutgiltigt); en äldre
  sida av en historik när dess block är det; en tokens decimaler. Första sidan och höjden hämtas alltid om.
- **Tolkningen kontrollerar** status 0/1/2 (tom lista eller fel som en hastighetsgräns), att svaret gäller det som
  frågades, att varje post rör adressen, att inget ligger över sidans block, `Transfer`-händelsen (tre ämnen och ett
  ord data; ERC-721 räknas inte) och decimalerna.
- **Tillgångar:** ETH och ERC-20 som `Asset` med kontrakt och decimaler. `KnownTokens` känner USDT och USDC på
  kontraktet (aldrig symbolen), med utgivaren (Tether, Circle) för *Kan frysas* och märket (₮, $, Ξ). USDT:s och USDC:s
  decimaler frågas aldrig efter.
- **Spårningen i kontomodellen** (Tracing): FIFO v1 och haircut v1 för alla kontokedjor (`AccountFifoMethod`,
  `AccountHaircutMethod`), och `AccountChecks` som kontrollerar en `AccountLedger` (samma tillgång, spårat högst det som
  kom in, avgifter bara i kedjans mynt) och vägrar en metod som skapar eller tappar pengar.
- **Kurser:** Bitstamp ger ETH i dollar och euro (`ethusd`, `etheur`), och `Price.ValueOf(AssetAmount)` värderar wei.
- **Appen:** `TokenMarks` (märket först i beloppet, raden för saldo per token med utgivare och snöflingan), startpunkter
  som kontrollerar EIP-55, och ett ärligt svar att grafen för Ethereum inte är byggd än.
- **Guiden:** gruppen *Ethereum* med *Kontomodellen*, *ERC-20 och tokens*, *Interna tx* och *Kan frysas*.

**Egna beslut:**
- **Blockscout i stället för Etherscan:** Etherscan tar API-nyckeln bara i adressen, som förseglas i ärendet; det bryter
  regeln att nycklar aldrig ligger i ärendet. Blockscout fungerar utan nyckel och går att köra själv. Etherscan står i
  TODO.
- **Sidor efter block** (`endblock`, nyaste först) i stället för sidnummer, så att en läst sida inte flyttas när nya tx
  kommer, och en äldre sida kan bli slutlig.
- **Avgiften** är tx:ens använda gas gånger källans gaspris, så som kedjan tar betalt; det är kedjans uppgifter och inget
  Chainsaw bedömer.
- **En token utan decimaler räknas inte:** tx:en och sidan säger att de är ofullständiga, i stället för att gissa.
- **FIFO för konton** lägger den spårade delen av en överföring in före den ospårade, och saldot före det lästa räknas
  som ospårat. **Haircut** avrundar nedåt; det som avrundningen lämnar ligger kvar på kontot.
- **Grafen, inspektören och startpunkterna för Ethereum** väntar till nästa del (23b): de kräver att grafens noder och
  spårningens resultat blir oberoende av Bitcoin, och etapp 22 bygger i grafen samtidigt.
- **Kurser för USDT och USDC** väntar: `PriceRequest` frågar per kedja. Till dess ingen kurs.
- **Det kedjan inte avgör räknas inte:** en överföring på noll (adressförgiftning), en NFT, en token som säger fler
  decimaler än Chainsaw räknar i, ett `delegatecall` eller `staticcall` och en logg med Transfer-ämnet i fel form. Inget
  av det gör ett helt svar oläsligt eller får endpointen att se trasig ut; tx:en eller sidan säger vad som inte räknades.

**Granskningen:** fyra omgångar.
- **Omgång 1:** inga C, fem I och elva M.
  - **I:** en skräptoken med 255 decimaler gjorde hela tokenhistoriken oläslig; överföringar på noll (adressförgiftning)
    räknades; en okänd tx (200 med status 0) blev *trasig källa* i stället för *finns inte*; en obekräftad tx visades
    som misslyckad; `delegatecall` räknades som ether som flyttades.
  - **M rättade:** sidstorleken i ämnet, en full sida av obekräftade poster, ether till ett nytt kontrakt, negativt
    saldo i raden, Guidens texter (dricksen, validatorernas uttag, kontrollen som inte används än), `Price.ValueOf`:s
    gräns, provet vid start i TESTER-INFOR, radbrytningar. **I TODO:** återställda underanrop, blob-gas, startpunktens
    text för fel kontrollsumma och `0X`.
- **Omgång 2:** inga C, två I och åtta M.
  - **I:** en logg som en penningtvättare skrivit (ämne som inte är en adress) gjorde tx:en oläslig; en misslyckad
    skapelse utan mottagare gjorde en sida med interna tx oläslig.
  - **M rättade:** decimaler som källan saknade sparades för gott, en obekräftad post fick en avgift, en tx utan
    `success` lästes som misslyckad, *finns inte* kräver status 0, gamla tokens Transfer-form sägs, frystexten säger
    *enligt historiken i ärendet*, långa rader i Guiden. **I TODO:** en endpoint som ligger efter.
- **Omgång 3:** inga C, två I och tre M.
  - **I:** loggar som källan delar i sidor (`next_page_params`) tappades; en post utan decimaler på en äldre sida
    sparades som slutlig.
  - **M:** en misslyckad skapelse utan mottagare i tx-historiken (rättad); decimalernas kantfall (TODO).
- **Omgång 4:** inga C eller I. Tre M: en regel för *lyckad* (rättad), och att en sida med en post utan decimaler frågas
  om och att det som inte räknades ska sägas i grafen (TODO).

Bitcoins facit (`GoldenTests`) är oförändrat i varje omgång.

**Tester:** 1 945 gröna i hela lösningen efter rebasen på etapp 22, varav nya:
- 64 i Chains (Keccak mot en oberoende Python-implementation, EIP-55, kända tokens, tolkningen, källan mot inspelade
  syntetiska Blockscout-svar: tx med alla rörelser, försegling, slutliga block, offline, *finns inte*, obekräftad,
  skräp som aldrig förstör ett svar, sidor);
- 9 i Tracing (FIFO och haircut för konton, slumpade huvudböcker, vägrade metoder);
- 3 i Pricing (ETH och wei);
- 5 i appen (tokenmärkena och saldoraden).

Bygget är utan varningar.

**Återstår:** etapp 23b (HANDOFF): grafen och inspektören för Ethereum, startpunkter, swap-pillret, kurser för USDT
och USDC, och sedan kontoheuristikerna och TRON. Det som står i TODO under *Ethereum-källan, det som återstår*.

## Etapp 23b (2026-11-07): Ethereum och ERC-20, andra delen: grafen och inspektören (TK1a)

**Byggt:**
- **Grafen oberoende av Bitcoin (Tracing):** en nod har `Chain`, `Accounts` och `Addresses` (`ChainAddress`) bredvid
  `GraphAddresses`, och `TraceGraph.NodeOf(ChainAddress)`. En Ethereum-adress är en egen nod (`account:Ethereum:0x…`,
  rund, sorten *Okänd* tills en etikett säger annat), eftersom kontomodellens heuristiker inte finns än.
  `TraceStartPoint`, `ExpansionRequest` och `Expansion` tar Ethereum-adresser och tx (lagrade som `ethereum-address`
  och `ethereum-transaction` i samma tabeller; inget schema ändrat).
- **Expansion genom `IEthereumSource`** (`EthereumReader`): en adress tre historiker sida för sida, de nyaste först
  upp till gränsen (50 tx, etapp 15), med vad som hölls tillbaka, klipptes, inte räknades eller inte var läst än; en tx
  med alla rörelser; blockens hash ur blockuppslaget där historiken saknar den (interna tx). Grafen läses ur ärendet
  med `LookupMode.CaseOnly`, så att visa den igen aldrig skickar något. `AccountData` slår ihop hela tx och historiker
  (varje rörelse en gång) och hittar swappar.
- **Kontomodellens del av grafen** (`TraceGraph.Account`, `AccountGraph`): rörelserna i kedjans ordning, avgifterna,
  hela tx, vilka adresser som är lästa och swapparna (`TokenSwap`). Linjerna summerar per par och tillgång med blocket
  (`BlockHash.TryParseEthereum`, `0x…`).
- **Spårning per tillgång** (`AccountTraceCalculator`, `ITracingService.TraceAccountsAsync`): varje läst adress bär
  pengarna vidare med FIFO v1 eller haircut v1 genom `AccountChecks.Run`, om och om igen tills inget ändras (pengar
  kan komma tillbaka). Pengar stannar som *inte expanderad* hos en oläst adress, med etikettens slut hos en börs eller
  mixer (`ITrailEndClassifier.ClassifyAccounts`), som *ligger kvar* och som avgift. Summan av sluten måste vara det
  spårade; frön i varandras flöde vägras i stället för att räknas två gånger.
- **Etiketter på Ethereum (Attribution, en agent i egen worktree):** `LabelSubject.ForAccount`, `Target`
  (`ChainAddress`), `GetLabels` och `GetHistory` för `ChainAddress`, biblioteket och `ClassifyAccounts`. Bitcoins
  lagrade text oförändrad.
- **Kurser för USDT och USDC (Pricing, en agent i egen worktree):** `PriceRequest.Asset`, Bitstamps `usdtusd`,
  `usdteur`, `usdcusd` och `usdceur`, efter kontraktet; en token utan källa får `Unsupported` och ingen kurs.
- **Appen (TK1a):** startpunkter för Ethereum i kommandoraden, Nytt ärende och `chainsaw:ethereum/…`; märket först i
  beloppet och värdet i kronor litet under (`EdgeValue`, bara när varje rörelse på linjen har en kurs); tjockleken mot
  den största spårade linjen av samma tillgång; swap-pillret **₮ → Ξ** på linjen den som bytte sålde på (ritas också
  med *Inga belopp*); snöflingan *Kan frysas* (`NodeBadge.CanBeFrozen`, `Icon.Snow`) och saldot per token
  (`NodeView.Balances`) med avgifterna dragna från ether. Inspektören visar *Kedja*, *Spårat in* per tillgång,
  tabellen Token · In · Ut · Kvar och frysrutan, och för en linje *Värde*, *Swap* och varje tx med block och hash.
  `AccountSeedPicker` väljer frön. Etikett (L), Historik och Annan kedja (K) fungerar för Ethereum-noder; en länk från
  en Ethereum-rörelse lagras i samma förseglade kolumner (`token:0x…` och positionen).
- **KF1a:** listans status säger att kontrollerna gäller UTXO och inte körs på Ethereum.
- **Guiden:** *ERC-20 och tokens* (värdet, swap), *Kan frysas* (bara expanderade adresser), *Kontomodellen* (i grafen),
  *Grafen* (snöflingan, tokens på linjerna) och *Startpunkter*.

**Egna beslut:**
- **Ethereum byggt vid sidan av Bitcoin** i stället för att göra om `GraphAddresses`, `TraceResult` och `UtxoGraph`:
  Bitcoins facit (`GoldenTests`) är oförändrat, och etapp 25 arbetar i Chains och Network samtidigt.
- **Varje Ethereum-adress är sin egen aktör** tills kontoheuristikerna finns.
- **Swappen sätts på den som signerade tx:en**, när den skickade exakt en tillgång och fick exakt en annan; poolen gör
  samma sak baklänges och får inget piller. Spårningen följer inte bytet än (TODO).
- **Saldot och snöflingan** bara för en expanderad adress; annars *Inte expanderad: saldot är okänt*.
- **Två tx i samma block** ordnas efter hash, eftersom källorna inte ger platsen i blocket (TODO).
- **Värdet** räknas per rörelse vid blockets tid; en linje utan kurs för varje rörelse får inget värde. Läsningen
  använder bara ärendets kurser; en expansion får be om högst 30.
- **En felregistrerad kontometod** stoppar inte Bitcoin; spårningen på Ethereum vägras då med skälet.
- **Delegerat:** Attribution och Pricing byggdes av två agenter i egna worktrees efter samma kontrakt och granskades med
  resten.

**Granskningen:**
- **Omgång 1:** inga C, fem I och sex M.
  - **I:** fröväljaren tappade frön vars egna pengar kom tillbaka till avsändaren (och sa då felaktigt att inget fanns
    att spåra); en adress som bara var delvis läst (hållen vid gränsen) bar pengarna vidare och fick snöflinga trots
    luckor i historiken; användarens länk till annan kedja följdes inte av Ethereums spårning; att läsa ärendet igen
    tappade rörelser på senare sidor och tidigare expansioners sidor efter en ny expansion; en bekräftad rörelse utan
    känd blockhash visades som obekräftad. Alla rättade: tidigaste ankomst per annat frö, `PartlyRead` som inte bär
    pengar och säger varför, länkade rörelser slutar som *Fortsätter på annan kedja*, läsningen går till slutet av
    sidorna, och `AssetTransfer.AccountBlock` håller höjd och tid utan hash.
  - **M rättade:** egen text när Ethereums spårning inte kan räknas, *Varför?* säger att värdet gäller hela linjen,
    ett frö till sig självt vägras med rätt skäl. **I TODO:** en start-tx som inte finns i ärendet, egna ytor med
    Ethereum-aktörer, länkens kolumner.
- **Omgång 2:** inga C, ett I och fyra M. **I:** omexpansionen kunde både läsa för mycket och tappa en tidigare swaps
  tokenrörelse; nu läser varje historik för sig ner förbi de tidigare tx:ernas lägsta block. **M rättade:** fröernas
  räckvidd räknas en gång, egen text när inget finns att spåra på Ethereum, Guiden om delvis lästa adresser; resten i
  TODO.
- **Omgång 3:** inga C eller I. Tre M (golvet för omexpansionen, kostnaden för en sen omexpansion och golvets läsning)
  står i TODO.

Bitcoins facit (`GoldenTests`) är oförändrat i varje omgång.

**Tester:** 2 129 gröna i hela lösningen efter rebasen på etapp 25, varav nya: 21 i Tracing (Ethereums graf,
expansion, läsning ur ärendet, gränsen, swappar, spårning per tillgång, slut, länkar, omexpansion, block utan hash),
12 i Attribution (etiketter på Ethereum), 39 i Pricing (USDT och USDC) och 18 i appen (märken, värden, pillret,
saldot och snöflingan, inspektören, frön, formulären, startpunkter, länken och grafkällan med kurser). Bygget är utan
varningar.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Ethereum, andra delen*); spårning genom en swap, kända
DEX:ar, tx:ens plats i blocket, aktiva adresser som bara är delvis lästa och resten under *Ethereum efter etapp 23b* i
TODO; kontoheuristikerna och TRON.

## Etapp 25 (2026-11-07): endpointarna, andra delen

**Byggt:**
- **Takt per endpoint** (Chains, `EndpointPacer`):
  - Varje endpoint har en egen takt (`SourceEndpoint.Interval`). Förvalen står i `SourcePace`:
    - högst fyra uppslag i sekunden per endpoint på internet (mempool.space, Blockstream, eth.blockscout.com och de
      användaren lägger till);
    - ingen paus för en egen nod.
  - Förvalen är **Chainsaws eget försiktiga val**, inte källornas publicerade gränser, och dokumenteras så.
  - Takten sparas i `sources.json` bara när den skiljer sig från förvalet. En endpoint på internet frågas aldrig fortare
    än förvalet, vad filen än säger.
- **Turer i förväg:** ett uppslag tar en tur, så att uppslag som körs samtidigt ställer sig i kö i stället för att
  fråga på en gång. En tur som inte används lämnas tillbaka, till exempel när uppslaget avbryts före sändningen, när
  nyckeln saknas, när utgången vägrar eller när källan bett om väntan under tiden.
- **Spridning:**
  - Ett uppslag tar den endpoint vars tur kommer först, men bara inom den bästa gruppen (`PlannedEndpoint.Tier`).
  - I Snabbast är grupperna, i ordning: de som svarat rätt, de som inte är provade, de som ligger efter, de vars prov
    misslyckades, och sist samma grupper igen för endpoints i paus.
  - I Prioriterad är varje endpoint en egen grupp, så användarens ordning gäller.
  - En sämre grupp tas bara när den bästas tur ligger mer än fem sekunder bort, till exempel när den bett om väntan.
- **`429` och `Retry-After`** (Network och Chains):
  - `ExitResponse.RetryAfter` läses som fördröjning eller datum, högst en timme. `ExitHttp` och `LocalHttp` sätter
    det.
  - Följande räknas som att källan ber om väntan:
    - `429`, med `Retry-After`, annars 30 sekunder och alltid minst en sekund;
    - `503` med en `Retry-After` på minst en sekund;
    - Blockscouts *rate limit* i en kropp med status 200 (`SourceBusyException`).
  - En endpoint som bett om väntan räknas inte som misslyckad. Den lämnas tyst för de andra och kommer tillbaka när
    tiden gått.
  - Ett uppslag som inte kan få en tur inom två minuter ger upp och svarar ur ärendet. Texten säger om en källa bett
    om väntan eller om Chainsaws egen kö är full.
- **Snygg reserv:** en endpoint som ett uppslag misslyckades genom får en paus. Pausen är en minut och fördubblas för
  varje misslyckande i rad, upp till 30 minuter. Under pausen frågas de andra först. Efter pausen frågas den på sin plats
  igen, och kommer tillbaka när den svarar. Varje försök förseglas som förut, också `429`.
- **Väntan syns ärligt, utan att UI-tråden väntar:**
  - `ChainSourcesView.WaitingUntil` och `EndpointView.WaitUntil`.
  - `ExitWay.ChainsWaiting` och `WaitText` ger statusraden *Väntar på källan · Bitcoin* medan ett uppslag väntar mer än
    en sekund.
  - Raden i Källor får den gula markeringen *Väntar* med tiden på hovring, och sidan visas igen när den första väntan
    är över.
- **Ett blocks marginal:**
  - En endpoint ett block under den högsta räknas som att den svarar rätt (`ChainSources.HeightMargin`), med
    `BlocksBehind` = 1.
  - **`LookupMode.InStep`** är bevakningens val i kontraktet: bara endpoints vars senaste prov svarade på den högsta
    höjden och som inte misslyckats sedan. Finns ingen skickas inget, och uppslaget säger det.
  - Bitcoins adresshistorik har fått en överlagring med läge (`IBitcoinSource`, med ett förval i gränssnittet som
    vägrar `InStep`, så att andra källor inte behöver ändras). Ethereums historik hade redan läget.
- **Egen nod på det lokala nätet:**
  - **Märkningen:** `SourceEndpoint.Local`, `IChainSources.Add`/`TryAsync` med `local`, och `SourceAddress.TryRead(text,
    local, …)`.
  - **Adressen:**
    - En adress på datorn eller ett privat nät kräver märkningen.
    - En lokal endpoint får inte ha en IP-adress utanför det egna nätet (`LocalAddresses.IsOwnNetwork`: loopback,
      10/8, 172.16/12, 192.168/16, länklokala och `fc00::/7`).
    - Bara https.
  - **Vägen:**
    - `ILocalHttp` (Network: `LocalHttp`, `LocalHandlerFactory`) går direkt, utan proxy. Den vägrar http och IP-adresser
      utanför det egna nätet innan något byggs, och slår upp ett namn och ansluter bara till dess adresser i det egna
      nätet.
    - `ChainSources.SendAsync` är det enda som väljer väg: `ILocalHttp` för en lokal endpoint, `IExitHttp` för alla
      andra. Utan `ILocalHttp` misslyckas en lokal endpoint och går aldrig genom utgången.
    - `ExitHttp` vägrar fortfarande privata adresser.
  - **Förseglingen:** råsvaret förseglas med `exit_route` = `local` och utan `exit_id` (`ExitResponse.IsLocal`).
  - **Utan ansluten utgång** frågas bara den egna noden. Det som frågades förut ger *Offline* som förut.
  - **Källor (KL1a):** brytaren *Lokal* i *Lägg till endpoint …* och märket *Lokal* på raden, i samma stil.
- `LocalTargets` har flyttats till Network.Contracts som `LocalAddresses`, så att Chains kan pröva adresser.

**Egna beslut:**
- **Siffrorna** för takten är Chainsaws egna och sägs aldrig vara källornas. `429` rättar dem.
- **Grupper för spridningen** i stället för fri spridning (granskningen): en endpoint som ligger efter eller är i paus
  frågas inte bara för att den bästa väntar på sin takt.
- **Ingen spridning i Prioriterad:** användarens ordning gäller. Nästa endpoint tas när den första ber om väntan eller
  har mer än fem sekunders kö. Står under *Väntar på beslut*.
- **100.64/10 räknas inte som eget nät** (CGNAT, Tailscale). Står under *Väntar på beslut*.
- **Ett prov som bara väntade** (källan bad om väntan, eller Chainsaws egen takt gav ingen plats inom provets tio
  sekunder):
  - Det säger inget om endpointen, så det förra provet står kvar, med pausen.
  - Alla prov mäts mot den högsta höjden, också den höjd ett behållet prov mättes mot, så att inget prov blir bättre
    än det var.
- **Väntan syns i statusraden**, inte i grafens hörn: grafen byggdes om i etapp 23b samtidigt.
- **Den egna nodens namn** slås upp av datorn. Certifikatet kontrolleras mot de installerade rötterna (TODO).

**Granskningen:** fem omgångar.
- **Omgång 1:** inga C, fem I och åtta M.
  - **I:**
    - Den egna noden tog emot allt som inte är globalt, också Teredo, 6to4, NAT64 och 100.64/10, som nås över
      internet. Nu används en positiv lista över det egna nätet.
    - En otrodd 404 med stängd utgång gav *finns inte*.
    - Spridningen valde endpoints som låg efter eller var i paus.
    - Bokade turer lämnades aldrig tillbaka, och texten sa att källan bett om väntan när kön var full.
    - Ett prov som bara väntade raderade ett bra prov.
  - **M rättade:** `503` utan väntan, takten noll i filen, en timer per kedja, dokumentation och radlängder.
  - **M i TODO:** den egna noden provas inte utan utgång, och dess namn och certifikat.
- **Omgång 2:** inga C, ett I och sex M.
  - **I:** ett behållet prov mättes inte mot den nya omgångens höjd.
  - **M rättade:**
    - Prov följer en väntan som noterats under deras tur och lämnar tillbaka turer.
    - En tur som skickats lämnas inte tillbaka.
    - Sidan visas igen vid den första väntan.
    - Ordalydelsen *eget nät*.
  - **M i TODO:** NotFound när alla tillfrågade är otrodda (etapp 24:s regel), och att *Prova* inte följer takten.
- **Omgång 3:** inga C, ett I och två M.
  - **I:** en ny lägre höjd mättes inte mot ett behållet högre prov.
  - **M:** en avbruten väntan och pausen för ett prov som bara väntade. Båda rättade.
- **Omgång 4:** inga C, ett I och inga M.
  - **I:** ett behållet prov kunde bli bättre än det var när inget högre svarade. Nu är dess gamla referenshöjd med.
- **Omgång 5:** inga C eller I. Tre M.
  - **Rättat:** ett mönster som inte användes, och ett test för ett behållet prov inom marginalen.
  - **I TODO:** att ett behållet prov åldras, och att en referenshöjd följer med.

Bitcoins facit (`GoldenTests`) är oförändrat.

**Tester:** 2 133 gröna i hela lösningen efter rebasen på etapp 23b. Av dem är 100 nya:
- 51 i Chains: takten, spridningen, `429` med och utan `Retry-After`, `503`, Blockscouts *rate limit*, reserven med
  pausen, avbrutna väntor, full kö, marginalen, `InStep`, behållna prov, den egna noden (adresserna, vägen, förseglingen,
  utan utgång, filen);
- 45 i Network: `LocalHttp`, det egna nätet (också Teredo, 6to4, NAT64 och 100.64/10), `Retry-After`, privata adresser genom
  utgången;
- 4 i appen: *Lokal* i formuläret, märket, *Väntar*, marginalen i pricken och väntan i `ExitWay`.

Bygget är utan varningar. Nätet används aldrig i testerna.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Endpointarna, andra delen*), bevakningen (Monitoring)
som ska använda `LookupMode.InStep`, och punkterna i TODO under *Bitcoin-källan* och *Utgångar*.

## Etapp 26 (2026-11-07): bevakningen, första delen (BV1a och BV2a)

**Byggt:**
- **Modulen Monitoring** (`AddMonitoringModule`):
  - **Kontrakten:** en bevakning har en nod (`WatchSubject`: adress, eller kluster på Bitcoin), och ett filter
    (`WatchFilter`): kedja, riktning (in, ut, båda), mottagare (1:n) och avsändare (n:1) där tom lista är alla,
    tillgång (Ethereum: alla eller en viss, efter kontraktet) och valfritt minsta belopp i den tillgången.
  - **Förseglat** i två kedjor: `monitoring.watches` (lagd till, ändrad, startad, pausad, återupptagen, tillbakatagen
    med valfritt skäl; vem, när och journalrad) och `monitoring.hits` (tx, blockhöjd, blockhash, blocktid, riktning,
    tillgång, belopp, motparter och de förseglade råsvar träffen bygger på, under kontrollens journalrad). Inget tas
    bort; en tillbakatagen bevakning och dess träffar står kvar.
  - **Vanlig data** bredvid (inte förseglad): när varje bevakning senast kontrollerades (`monitoring_checked`) och
    vilka träffar användaren sett (`monitoring_seen`).
  - **Kontrollen utan ärendet** (`IWatchCheck`, `IWatchFeed`): adresser och filter in, träffar ut, från vilket flöde
    som helst. Ärendets flöden (`BitcoinCaseFeed`, `EthereumCaseFeed`) frågar källan med `LookupMode.InStep`, sida för
    sida bakåt till där förra kontrollen var (högst fem sidor, annars sägs det). Etapp 27:s bakgrundsprocess tar med
    sig ett eget flöde och samma kontroll.
  - **Vad som räknas:** ut är det som lämnar de bevakade adresserna till andra (växel tillbaka räknas inte), in är det
    som kommer från andra i en tx där noden inte själv betalar. Belopp summeras per tx, riktning och tillgång, och
    minsta belopp prövas på summan. Bara bekräftade tx; en träff kräver blockets hash (Ethereums interna tx slår upp
    blocket, i fas, och väntar annars till nästa kontroll).
  - **Grinden** (`IWatchGate`, `MonitoringGate`): inga uppslag när valvet är låst, när VPN är valt och ingen utgång är
    ansluten, eller när kedjans källor är *Av*.
  - **Starten:** en ny bevakning får vid första kontrollen kedjans höjd som start, förseglat; bara pengar i senare block
    räknas. Varje kontroll läser om de sex senaste blocken (`WatchRules.Lookback`), och en träff förseglas en gång.
  - **Hur ofta** gäller per bevakning (`WatchPace` i filtret, förseglat med det; användarens beslut 2026-11-07):
    *Varje block* (förval), 1, 5 eller 15 minuter. Modulen avgör vilka bevakningar som står på tur (`IsDue`: ett nytt
    block sedan förra kontrollen och bevakningens tid), så att etapp 27 använder samma regel.
  - **Delvis läst:** hinner en kontroll inte ner till förra kontrollens block (fler än fem sidor, en historik källan
    inte gett färdigt, ett block utan hash) är den *delvis läst*. Den går vidare bara ovanför det den läste helt och
    sparar luckan som text; ett block utan hash håller kontrollen kvar så att det läses igen. En misslyckad kontroll
    läses om från samma block.
  - **Ut ur en CoinJoin:** har andra satt in pengar i samma tx räknas högst det de bevakade adresserna satte in, minus
    växeln.
  - Träffen förseglas bara om bevakningen fortfarande gäller oförändrad när kontrollen är klar, och citerar bara de
    råsvar den bygger på.
  - **Kluster** läses som klustringen säger vid varje kontroll (högst 50 adresser, resten sägs).
- **Appen:**
  - **Bakgrunden medan Chainsaw är öppet** (`WatchRunner`): var femte sekund ser den på det öppna ärendets aktiva
    bevakningar; per kedja med öppen grind frågas höjden högst en gång i minuten (genast för en ny bevakning), och
    modulen kontrollerar de bevakningar som står på tur. Ett stängt, bytt eller låst ärende lämnas i fred.
  - **`WatchBoard`** håller ärendets bevakningar, så att vyn, grafen och pillrets prick aldrig säger olika.
  - **Vyn Bevakning (BV1a):** listan, den valda med fakta och träffar, *Ny bevakning …* (B, också Nytt → Bevakning),
    Ändra, Pausa/Fortsätt, Ta bort (bekräftelse med valfritt skäl, förseglat tillbakatagande), Öppna i grafen, och i
    huvudet ärligt *Bevakar medan Chainsaw är öppet*, eller *Väntar på utgången* / *Källorna är av*.
  - **Formuläret** (`WatchFormViewModel`, `Views/WatchForm`) är samma i grafen och vyn: riktning, mottagare och
    avsändare som brickor (adress eller Ctrl+klick i grafen till den rad vars sikte är valt), tillgång på Ethereum,
    minsta belopp (komma eller punkt) och *Hur ofta*. Ett kluster som motpart tas med alla sina adresser (en bricka);
    noden själv och klustrets egna adresser vägras. Formuläret stängs när ett annat ärende öppnas.
  - **Grafen (BV2a):** högerklicksmenyn på en nod (Etikett, Bryt upp, Annan kedja, *Bevaka … (B)*, Expandera, Kopiera
    adress), ögat i nodens hörn (markeringen *Bevakad* också i bildspråket), prickade vaktlinjer i bevakningens egen
    dova ton (`Brush.Watch`) med ett öga mitt på, sken på linjer i bevakad riktning, strålar vid porten utan filter, och
    vid en träff en riktig linje med beloppet och **animerade ringar** tills träffen är sedd (stilla när Windows har
    animeringar avstängda). Grått när grinden är stängd. Bara det som syns ritas, och ringarnas tidur går bara medan
    något rör sig. Att välja noden räknar dess träffar som sedda; grafens rad säger *Bevakning slog till · …*.
  - **Pillrets öga** får en prick vid osedda träffar.
  - **Historiken (AN1b)** visar bevakningens poster, träffarna som fakta och *Ta tillbaka* på bevakningen.
  - **Vyns huvud** säger ärligt också *Väntar på utgången*, *Källorna är av*, *Uppslagen misslyckas · försöker igen*
    och *Kunde inte fråga källan*; raderna säger *Senaste uppslaget misslyckades*. Listorna är virtualiserade.
- **Guiden:** *Bevakning* i en egen grupp.

**Egna beslut:**
- **Hur ofta** byggdes först som en rad under Låsning i ST1a; användarens beslut (förmedlat av huvudsessionen under
  etappen) att den ställs per bevakning ersatte den, och raden är borttagen.
- **Varje nytt block** betyder: höjden frågas högst en gång i minuten och adresserna läses när den ökat. Det håller nere
  både uppslagen och det som förseglas (varje råsvar förseglas, princip 1); på Ethereum blir det i praktiken en gång i
  minuten.
- **Starten är kedjans höjd vid första kontrollen**, inte när bevakningen lades till, eftersom en bevakning ska gå att
  lägga till offline. En ändring behåller starten, så att inget som hänt sedan bevakningen startade tappas.
- **En pausad bevakning** som återupptas räknar också det som hände under pausen (inget tappas), läst bakåt så långt fem
  sidor räcker.
- **Senast kontrollerad och sedda träffar** är vanlig data, inte förseglad: de säger inget om kedjan.
- **Ctrl+klick** går till formuläret när det är öppet och annars till egna ytor (etapp 22).
- **Träffen i grafen** blir en linje bara till en motpart som finns i grafen; annars rör sig ringarna runt nodens öga.
  Beloppet står på linjen bara när träffen har en motpart, eftersom en träffs belopp är summan till alla.
- **Ringarna** går med 15 bilder i sekunden och bara medan en osedd träff syns på skärmen.
- **Aviseringssätten** (Windows, ljud, meddelande mitt på skärmen) och bakgrundsprocessen hör till etapp 27 och är inte
  byggda; inget i gränssnittet säger att bevakningen går när Chainsaw är stängt.

**Granskningen:** fem omgångar.
- **Omgång 1:** ett C, sex I och tio M.
  - **C:** B i vyn Bevakning tömde formuläret medan man skrev en adress (`bc1…`). Nu bara utanför textfält.
  - **I:** en kontroll som stannade efter fem sidor flyttade höjden ändå; pengar ut överskattades i en tx med andras
    inputs (CoinJoin); linjerna fick hela träffens belopp; ett kluster som motpart gav bara första adressen; ringarna
    ritade om hela grafen också utanför skärmen; *hur ofta* var global, inte per bevakning.
  - **M rättade:** höjden frågades var femte sekund efter ett fel, en träff från förra ärendet kunde sägas i nästa,
    tx-noder som motpart, onödiga blockuppslag på Ethereum, blockhashen visas med träffen, ett ärligt huvud, radlängd.
    **I TODO:** starten från en höjd som kan ligga efter, paus och omorganisation.
- **Omgång 2:** inga C, fem I och fyra M.
  - **I:** en delvis läst kontroll kom aldrig ikapp; ett kluster som motpart ritades och listades en gång per adress;
    misslyckade uppslag syntes som *Bevakar*; formuläret överlevde ett byte av ärende och kunde spara i fel ärende;
    listorna var inte virtualiserade.
  - **M rättade:** en träff förseglades för en bevakning som ändrats under kontrollen, takten blev dubbelt så lång,
    journalen sa *alla tillgångar* för BTC, klustrets egna adresser som motpart.
- **Omgång 3:** inga C, två I och sju M.
  - **I:** en delvis läst historik utan känt golv flyttade kontrollen förbi det som inte lästes; en timeout visades som
    att inget skickats.
  - **M rättade:** träffar citerade råsvar de inte byggde på, en delvis läst kontroll lästes om varje minut,
    bekräftelsen av en borttagning följde med till nästa bevakning, ringarna stod still efter att grafen visats igen,
    en klusterbevakning kunde ändras till egna adresser, alla avsändare namngavs. **I TODO:** CoinJoin-taket borde
    visas som *högst*.
- **Omgång 4:** inga C, ett I och två M. **I:** två block utan hash långt isär: kontrollen gick förbi det lägre. Nu
  håller det lägsta. **M rättade:** neutrala texter i huvudet, TODO:s ordalydelse.
- **Omgång 5:** inga C eller I. Två M: en text (rättad) och att golv och spärrar behöver hållas isär innan kluster på
  andra kedjor bevakas (dokumenterat, TODO).

Bitcoins facit (`GoldenTests`) är oförändrat: bevakningen lägger inget till i grafen eller spårningen.

**Tester:** 2 184 gröna i hela lösningen, varav nya:
- 46 i Monitoring (de två gamla ersatta): facit för vad som räknas (ut, in, växel, mottagare, avsändare, riktning,
  minsta belopp, obekräftat, Ethereum per överföring, CoinJoin), kontrollen utan ärendet, tjänsten med syntetiska källor
  (försegling, journal, start, en träff en gång, grinden, paus, misslyckat och inte frågat, kluster, Ethereum med
  blockuppslag, block utan hash, delvis läst, egna adresser, takten och `IsDue`), grinden, och ett inspelat test genom
  den riktiga Esplora-källan och endpointarna (bara en endpoint i fas frågas, råsvaren förseglas och citeras);
- 35 i appen: formuläret, vyn, löparen, grafens lager (vaktlinjer, sken, strålar, träffar, grått), menyn, Ctrl+klick,
  historiken, pillrets prick, Nytt → Bevakning och fallen från granskningen.

Bygget är utan varningar. Nätet används aldrig i testerna.

**Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Bevakningen, första delen*); etapp 27
(bakgrundsprocessen, aviseringarna); punkterna under *Bevakning (etapp 26)* i TODO.


## Etapp 27 (2026-11-07): bevakningen, andra delen (när Chainsaw är stängt)

**Byggt:**
- **Bakgrundsprocessen `src/Chainsaw.Watcher`** (eget projekt, `Chainsaw.Watcher.exe` bredvid `Chainsaw.exe`): en liten,
  tyst process utan fönster (WinExe, låg prioritet, en GC-heap utan bakgrundstråd, utan kulturdata), en per
  Windows-användare (namngiven mutex), som stoppas med en namngiven händelse (`--stop`, eller reglaget i
  inställningarna) och avslutar sig själv när inget ska bevakas. Varje 15:e sekund läser den planen (bara när den
  skrivits om), lämnar ärenden som är öppna i Chainsaw åt Chainsaw, frågar varje kedjas höjd högst en gång i minuten och
  kontrollerar de bevakningar som står på tur.
- **Samma kontroll som i Chainsaw:** `IWatchCheck`, `IWatchFeeds` (nytt kontrakt, samma flöden med `LookupMode.InStep`),
  endpointarna ur `sources.json` med sin takt och reserv, och `WatchSchedule` (nytt i kontrakten): när en bevakning står
  på tur, hur långt bakåt den läser och hur långt en kontroll lämnar den, skrivet en gång för båda.
- **Ärendet i minnet (`ScratchCase`):** källorna kräver ett ärende att försegla i. Bakgrunden ger dem ett ärende i
  minnet med bara Chains tabeller, kastat efter varje varv, så att källornas kod körs oförändrad. Inget sparas på disk
  utanför den skyddade kön.
- **Underlaget (planen) under DPAPI** (`IUserDataProtector` i Security, `CryptProtectData` för den inloggade
  användaren, med syftet som entropi): på eller av, utgången, aviseringssätten och per ärende (bara ärendets slumpade id)
  de aktiva bevakningarna med adresser (ett kluster som klustringen satte det när planen skrevs), filter, start, hur långt
  de lästs och de träffar ärendet redan har i bakåtläsningen. Appen skriver planen när vyn läser om bevakningarna (tillägg,
  ändring, paus, borttagning, efter varje egen kontroll) och när inställningarna ändras.
- **Kön** (en skyddad fil per kontroll, skriven helt och flyttad på plats): träffarna, hur långt bevakningen lästes och
  alla råsvar kontrollen hämtade och vilar på, med alla sina värden (`SealedResponse`) och SHA-256. En post med en ny
  träff märks i filnamnet, så att låsskärmen kan säga att en bevakning slagit till utan att läsa något.
- **Införandet i ärendet** (`IBackgroundWatches.Import`, när ärendet är öppet och upplåst, först i varje varv av appens
  `WatchRunner` så att de aldrig skriver samtidigt): råsvaren förseglas igen oförändrade efter kontroll av SHA-256
  (`IResponseTransfer` i Chains), en start sätts om bevakningen saknar en, träffarna förseglas med raden *slog till i
  bakgrunden* i journalen när bevakningen fortfarande gäller (filtret lika, takten oräknad) och träffen är ny och över
  ärendets start, hur långt den lästs skrivs bara när kontrollen räknade från ärendets start eller lägre, och postens id
  sparas (`monitoring_imported`, schemasteg 2), så att den aldrig förs in två gånger. Ärendet sparas innan posterna tas
  bort. En post som inte går att läsa läggs åt sidan och sägs.
- **Ärendet hålls** (`ICaseHolds`): medan ett ärende är öppet håller appen en fil under `held\` utan delning; bakgrunden
  lämnar det. Systemet släpper filen också när Chainsaw kraschar.
- **Aviseringen** säger bara *En bevakning har slagit till.* och *Öppna Chainsaw och lås upp för att se den.*: i Windows
  (en ikon i meddelandefältet med `NIF_INFO`, som Windows visar som toast, i en minut), som ljud (mynt-swooshet, gjort
  av `tools/sounds/coin-swoosh.py`, inbyggt) och, bakom samma gränssnitt men inte i inställningarna, som en meddelanderuta
  överst mitt på skärmen. Högst en gång per varv, och aldrig för en träff ärendet redan har.
  `Chainsaw.Watcher.exe --test-alert` prövar sätten.
- **Appen:** `BackgroundLink` håller det öppna ärendet, skriver planen, sätter `HKCU\…\Run` och startar eller stoppar
  processen (bara när den är på, en bevakning är aktiv och Windows är utgången), startar den igen högst en gång i minuten
  om den dött, för in kön och säger vad bakgrunden gör. **Inställningarna (ST1a, Låsning)** har tre rader:
  *Bevaka när Chainsaw är stängt*, *Avisering i Windows*, *Ljud*. **BV1a:s huvud** säger *Går i bakgrunden · genom
  Windows · i fas med …*, *Bakgrunden väntar på utgången* (VPN), *Bakgrunden är av*, *Bakgrunden går inte*, *Går i
  bakgrunden · uppslagen misslyckas* eller *… källorna är av* (hel eller streckad ram som i skissen); Chainsaws egna
  problem (utgången, källorna, misslyckade uppslag) går före. **Låsskärmen (ID8c)** har raden *En bevakning har slagit
  till. Den visas efter upplåsning.* med en rosa prick.
- **Guiden:** *När Chainsaw är stängt* i *Bevakning*.

**Utredningen om VPN:** utgångarnas WireGuard-filer (privat nyckel och peer) ligger krypterade i valvet
(`network/exits/…` genom `IVaultSecrets`) och kan bara läsas med huvudnyckeln. Bakgrunden har aldrig huvudnyckeln, så
den kan inte starta en tunnel. Att lägga en kopia under DPAPI utanför valvet skulle gå tekniskt, men är ett avsteg från
*Säkerhet* som bara användaren kan besluta (TODO, *Väntar på beslut*). Därför gör bakgrunden **inga uppslag med VPN
vald** (grinden säger `NoExit`, processen startas inte ens, och huvudet säger det). Medan Chainsaw är öppet bevakar
appen själv genom VPN som förut.

**Egna beslut:**
- **`src/Chainsaw.Watcher`, inte `tools/`:** processen skickas med appen och är en del av produkten; `tools/` är
  administratörens verktyg och hjälpprogram byggda för sig. Den bygger med appen (projektreferens, alla typer internal).
- **Ett ärende i minnet** i stället för ett nytt gränssnitt i Chains: källornas uppslag, takt, reserv och försegling körs
  oförändrade, och Chains fick bara överföringen av råsvar (`IResponseTransfer`, läs och försegla igen med SHA-256).
- **Vem som hämtade:** bakgrunden har ingen operatörsprofil (den ligger i valvet), så råsvaren säger *Chainsaw i
  bakgrunden*; journalraden som för in dem har operatören som öppnade ärendet. Utgången står som i alla råsvar
  (`windows`).
- **Planen bär också ärendets kända träffar i bakåtläsningen** (bevakning, tx, riktning, tillgång), så att en träff som
  Chainsaw redan förseglat aldrig aviseras igen (granskningen omgång 1). Det är mer än *adresserna och ärendets id*, men
  under samma DPAPI och sådant som står på kedjan.
- **Höjden** förs in med ärendets första post i varvet; en höjd som inte ledde till någon kontroll hör inte till något
  ärende och förs inte in.
- **Endpointarna provas** när processen startar och igen efter tio minuter om ingen är i fas. Ändras `sources.json`
  byggs tjänsterna om.
- **Endpoints med API-nyckel** fungerar inte i bakgrunden (nyckeln ligger i valvet).
- **Aviseringen i Windows** via meddelandefältet, inte en WinRT-toast, eftersom en toast kräver att Chainsaw registreras
  som app (AUMID); ikonen tas bort efter en minut.
- **Ljudet** är syntetiserat (brus genom ett svepande bandpassfilter som swoosh, sedan mynt som studsar: oharmoniska
  deltoner med exponentiell avklingning och ett klick, allt tätare och tystare), 1,45 s, stereo, och därmed Chainsaws
  eget (THIRD_PARTY).
- **Förval:** bakgrunden, aviseringen i Windows och ljudet är på; meddelandet mitt på skärmen är av och dolt.

**Granskningen:** tre omgångar.
- **Omgång 1:** ett C, sju I och tio M.
  - **C:** bara den första kedjan provades; en andra kedjas bevakningar frågade aldrig.
  - **I:** träffar fick inte alla sina ursprung när ett tidigare råsvar i varvet återanvändes; falska aviseringar för
    träffar ärendet redan hade; en krasch mellan start och framsteg kunde kasta bakgrundens träffar; ett skrivfel tog
    ner processen; en plan som inte kunde läsas för stunden avslutade den; huvudet visade inte vad processen sa;
    framsteget skrevs var 15:e sekund.
  - **M rättade:** höjden till den första post som faktiskt köas, införandets I/O, ikonen och trådarna vid avslut, takten
    i filterjämförelsen, radlängd, tester. **I TODO:** sammanfattningsraden efter en krasch, köns tillväxt, två takter
    mot samma endpoints, adresserna kvar när bakgrunden är av.
- **Omgång 2:** inga C, två I och fem M. **I:** en kontroll från en senare start markerade block som lästa (från
  rättelsen i omgång 1); en träff som indexerats sent aviserades aldrig. **M rättade:** aviseringen sägs innan den
  minns, en första plan som hålls vid inloggning, en gammal process status, dokumentationen.
- **Omgång 3:** inga C eller I. Två M rättade (en plan som aldrig går att läsa sägs som fel, radlängd); en lämnad (ett
  återanvänt process-id kan ge en gammal status ett varv).

**Tester:** 2 263 gröna i hela lösningen, varav 79 nya: 21 i det nya `Chainsaw.Watcher.Tests` (hela vägen från planen
genom processen till ärendet med den riktiga Esplora-källan bakom en låtsad utgång, VPN, hållna ärenden, start, två
kedjor, två bevakningar på en adress, kända och sent indexerade träffar, ärendet i minnet, aviseringssätten,
omstarterna), 26 i Monitoring (filerna, kön, schemat, införandet), 5 i Chains (överföringen), 4 i Security (DPAPI), 23 i
appen (länken, huvudet, låsskärmen, inställningarna, löparen). DPAPI, registret, aviseringen och ljudet går bara att
prova på Windows (`docs/TESTER-INFOR-1.0.md`). Bygget är utan varningar; nätet används aldrig i testerna.

**Återstår:** prova på Windows; skissa meddelandet mitt på skärmen; punkterna under *Bevakning i bakgrunden (etapp 27)*
i TODO; beslutet om WireGuard i bakgrunden.

## Låsskärmen ID8c (2026-11-07): kättingen som blir DNA

Användaren valde ID8c efter flera omgångar på canvasen (ID8a–d). En ren gränssnittsändring, granskad av sessionen mot
skissen.

- **Figuren** (`src/Chainsaw.App/Figure/`, utan WPF och testad): kättingen i loggans form på snedden (40°, skala 0,75,
  axeln möter sidans överkant vid x 640 på en sida på 1 440 px), som slutar efter en hel länk ungefär halvvägs. Den
  sista länkens stänger fortsätter som spiralens två strängar med stängernas bredd. Spiralen vidgas till 220 % mot
  nederkanten, och vridningen är integralen av en fast lutning genom radien, så att strängarna korsar varandra i samma
  vinkel hela vägen. Strängen bakom är mörkare, basparen är streck i två halvor, och partiklarna (110 åt gången, var
  sjunde en bas) lossnar från strängarna och driver ut. Värdena är skissens förval.
- **Kontrollen** `ChainFigure` ritar formerna en gång per storlek som en fryst ritning och flyttar bara partiklarna,
  ungefär 30 gånger i sekunden medan den syns. Med Windows animeringar avstängda står partiklarna still och timern vilar;
  minimerat flyttas ingenting.
- **Stängerna ritas som en egen form** som går två enheter in i skarvarna. I samma form tog överlappet ut skarven
  (vindningsregeln), och en kantlinje runt skarven drog streck över länkens hål; båda sågs i skissen.
- **Egna beslut:** figuren gäller också förstagångsinställningen, som delar sidan. I ett smalare fönster än 1 440 px
  flyttas axeln till vänster i samma proportion, så att kättingen inte krockar med formuläret. Lyktan under musen
  (Ctrl) följde med teckenfältet och är borta, eftersom ID8c inte har den.
- **Borttaget:** teckenfältet (motorn, scenerna, lyktan, glyferna och rastreringen) och dess tester, raden om
  asciify-engine i *Om Chainsaw* och i `THIRD_PARTY.md`.

**Tester:** 584 gröna i appens testprojekt efter rebasen på etapp 25, varav 17 nya för figuren och partiklarna. Bygget är utan varningar.
**Inte provat på Windows** (TESTER-INFOR, *Kättingen som blir DNA*).

## Ledtrådsnoden och samlingsnoden (EX1, EX2b), dolda OP_RETURN och grafen som använder ytan (2026-11-07)

TODO punkt 22, 30 och OP_RETURN-delen av punkt 4. En adress med 1 759 tx ritade 50 tx och ett par hundra motparter i
en lång kolumn; nu håller grafen sig i schack (princip 7) utan att något lämnar modellen (princip 1).

- **Tracing** (`PileRules`, `GraphPile`, `HeldBackClue`): reglerna räknas ut ur ärendet varje gång grafen byggs. En
  aktör hör till det som först tog in den (startpunkterna, sedan expansionerna i ordning), så en motpart som redan
  fanns flyttar aldrig in i en senare hög. Har en expanderad aktör **fler än åtta** (`GraphPile.MostLoose`) nya
  motparter på en sida läggs de i en hög där; åtta läser fortfarande som en solfjäder, ett par dussin är den långa
  kolumnen. Sidan avgörs av den första tx:en motparten är med i: *ut* när den expanderade aktören betalar i den,
  annars *in*. Aldrig i en hög: startpunkter, expanderade aktörer, aktörer där spåret tar slut (börs, mixer, sanktion)
  och länkar till annan kedja. En plats som användaren gett en aktör håller den inte utanför; det gör utplockningen,
  så att en utplockad och flyttad nod kan läggas tillbaka.
- **Det som hölls tillbaka** sparas nu med expansionen (`Expansion.HeldBack`: hur många som togs och hur många det
  finns), så att ledtråden finns kvar när ärendet öppnas igen. Ledtråden bär uppslaget som hämtar nästa 50
  (`HeldBackClue.NextPart`).
- **Urvalet** (`tracing_picked`) och valet att visa utgångarna utan mottagare (`tracing_view.show_unspendable`) är
  vanlig data som platserna, utan rad i journalen: högen är hur grafen ritas, inte en slutsats. Schemat är version 3;
  golden-facit ändrades bara i schemaversionen och tabellerna.
- **Appen** (`GraphFold`): högens medlemmar ritas som en fyrkantig nod (kortlek, *Avsändare* eller *Mottagare*, antal
  och summa), deras linjer summeras per par, sort och tillgång till en tjock linje med summan. **Egna beslut:** en
  medlem med etikett eller som spårade pengar når viks aldrig in, vad Tracing än säger (offrets pengar ska alltid
  synas); det står i stället för sonaren för negativ etikett, som väntar. Ledtråden är en streckad rund nod `+N tx` med
  en tunn streckad linje utan belopp, i aktörens hopp.
- **Valvyn** (`PileFanViewModel`, `PileFanLayout`, `GraphCanvas.Fan.cs`): ett klick på högen dimmar grafen och fäller
  ut korten, i en bred spiral (EX1a, störst närmast) eller i tidsbågar (EX1b, tidigast överst, bort från adressen).
  Platserna läggs ut så att inga kort överlappar (testat med 300). Listen längst ner har lägena, beloppsreglagen
  (logaritmiska mellan minsta och största), *Damm* (under 10 000 satoshi), räknaren, *Plocka ut* (Enter) och
  *Stäng* (Esc). Klick väljer, Shift-klick väljer fler, och ett kort som dras ut på den dimmade grafen plockas ut där
  det släpps, med de andra valda under. *Lägg tillbaka i högen* finns i ikonlisten för en utplockad nod.
- **Avsteg:** ett klick på ledtråden hämtar nästa 50 i stället för att fälla ut de tillbakahållna tx:erna, eftersom de
  inte är hämtade; de nya motparterna hamnar i högen när de är många. *In, ut eller alla* är två högar i stället för
  ett filter. Fördelningen bakom reglaget, tidsfönstret, motpartens sort, *Bara de som inte finns i grafen* och
  *Gruppera per motpart* är inte byggda. Allt står i TODO under *Ledtrådsnoden och samlingsnoden*.
- **OP_RETURN på 0 BTC döljs** (användarens beslut): noden och dess linje ritas inte. Efter en expansion som gav nya
  sådana säger en toast hur många, med *Visa dem*; den står 20 s (`ToastRequest.Lingers`, `Toaster.TimeLingering`)
  i stället för 6. Valet följer med grafens läge i ärendet, och *Dölj dem* kommer i en toast när de visas. En
  OP_RETURN som bränner pengar är ett faktum värt att se och döljs aldrig.
- **Grafen använder ytan** (punkt 30): ett hopp med fler än åtta aktörer sprids över kolumner sida vid sida, med så
  många rader att blocket blir ungefär kvadratiskt (`HopLayout.RowsFor`), uppifrån och ned i tidsordning;
  hoppkolumnens band är lika brett som blocket. Den fria ytan utgår från samma platser. Belopp som skulle hamna på
  varandra ritas inte; det under pekaren, sedan de nära det valda och de spårade går först, så att inget viktigt
  försvinner och resten syns på hovring.
- **Självgranskning** mot principerna: inget hämtat tas bort och inget förseglat ändras; högen och ledtråden är
  arrangemang, beloppen på linjerna och korten är summor av fakta; bara ikoner och korta ord i listen. Rättat under
  granskningen: en utplockad nod som också flyttats föll ur högens regel och kunde inte läggas tillbaka.

**Tester:** 11 nya i Tracing (högarna, sidorna, etiketterna, urvalet över omöppning, ledtråden och nästa uppslag,
valet att visa OP_RETURN), 15 nya i appen (vikningen, ledtråden, OP_RETURN, layouten, valvyn utan överlapp i båda
lägena, filtren, valet, vymodellen med högen, ledtråden och toasten) och ett för toastern. Tracing 138, Checks 48 och
appen 716 gröna; bygget är utan varningar. **Inte provat på Windows** (TESTER-INFOR, *Ledtrådsnoden och
samlingsnoden*, *Utgångar utan mottagare döljs*, *Grafen använder ytan*).

## Testärendet /testdata (TODO punkt 27, 2026-11-07)

Användarens önskemål: ett testärende där all funktionalitet syns utan att leta upp en adress. `/testdata` finns bara i
Debug (`#if DEBUG`, registrerat med `AddTestData()` sist i `App.xaml.cs`), och `/help` listar bara registrerade
kommandon, så det syns inte i Release.

- **Den påhittade källan (eget val och skäl):** en egen `ISourceApi` i Chains (`TestData/TestDataApi.cs`, kod
  `testdata`, endpointen *testdata (påhittade data, bara Debug)* på `https://testdata.invalid/esplora/`, utan takt) och
  fabriken `TestDataBitcoin.Create`, båda bara i Debug. Under den ligger den **riktiga** `EsploraBitcoinSource` med en
  egen `ChainSources`, och i stället för utgången en `IExitHttp` i appen (`TestChainHttp`) som svarar Esplora-JSON ur en
  påhittad kedja i processen. Så går testärendet genom samma läsning, försegling och kontroller som ett riktigt ärende
  (tx mot sina egna bytes, block mot sina huvuden, sidor, utgångarnas spenderingar, höjden och provet för bevakningen),
  och varje förseglat råsvar säger källan `testdata` och den endpointen (princip 1 ljuger inte). Den påhittade
  `ChainSources` registreras inte, så den syns aldrig i Källor och provas aldrig vid start. Valet framför en
  `IBitcoinSource` på modellnivå (som testernas): den skulle inte försegla något, och testärendet skulle då inte ha sina
  data i filen.
- **Bara i testärendet:** `TestDataBitcoinRouter` ersätter `IBitcoinSource` i Debug och väljer per ärende. Ett ärende är
  ett testärende bara om journalen har raden `testdata.created`, som `/testdata` skriver direkt efter att ärendet
  skapats och innan något slås upp; journalen kan bara fyllas på, så ett riktigt ärende kan aldrig bli ett. Testärendet
  frågar aldrig den riktiga källan, och ett riktigt ärende aldrig den påhittade. Adresserna är SHA-256 av en etikett med
  prefixet `chainsaw-testdata:` (giltiga mainnet-adresser, eftersom `BitcoinAddress` kräver det, men som ingen använt).
  `TestDataBackgroundWatches` håller testärendets bevakningar utanför bakgrundens plan, så att bakgrundsprocessen aldrig
  frågar de riktiga källorna efter en påhittad adress.
- **Kedjan** (`TestChain`, `TestTransaction`, `EsploraJson`): tx skrivna som på kedjan (utan witness), txid som dubbel
  SHA-256 av bytes, block för varje höjd med riktiga huvuden (lättaste målet, som regtest) och merkle-rot. Pengarna
  kommer från coinbase-tx i egna block före berättelsen.
- **Berättelsen som data** (`TestStory`, `TestStoryChain`, `TestSteps`): ett steg per handling genom modulernas
  kontrakt (startpunkt, expansion, etikett, biblioteket, länk till annan kedja, bevakning, kontroller, egen yta). Offret
  betalar bedragaren (startpunkten), tre offers pengar konsolideras (klustras med *Varför?*), en betalning med växel
  (tre tecken, slås ihop), en gruvbelöning med P2PK och OP_RETURN som spenderas i en delad tx, en fördelning till en
  mixer (troliga utgångar 2 % och 5 % lägre), en Whirlpool-CoinJoin, en peel chain genom spel, handel, presentkort,
  automat och kort till Nordbörsen (uttag funnet), en börs utan KYC, Navet med 66 tx och många motparter som betalar
  darknet (också sanktionerad), garanti, OTC, nästlad tjänst, swap (länkad till en Ethereum-tx) och en sanktionerad
  aktör, och ett nytt offer i det nyaste blocket som bevakningen fångar. Etiketterna är märkta med den öppna källan
  *testdata (påhittad)* och *Sprid inte*, utom offret och bedragarens plånbok, som är egna. Etiketten *från ett annat
  ärende* sätts i kollegans ärende `TEST-NNNN-KOLLEGA` och kommer in genom etikettbiblioteket.
- **Bevakningens träff:** `MonitoringService.CheckAsync` körs vid höjden före den nyaste (bevakningen börjar) och vid den
  nyaste (träffen), och träffen lämnas osedd. För att det ska gå också utan ansluten utgång öppnar `TestDataWatchGate`
  grinden bara i det flöde som bygger testärendet (`AsyncLocal`); allt annat ser den riktiga grinden.
- **Lämnat för användaren att expandera:** Navet (hålls tillbaka), mixerns utgång (vidare till Sydbörsen), offrets adress,
  Whirlpool-utgångarna och bedragarens andra plånbok.
- **Rör inte grafen:** inget i `GraphView`, `GraphViewModel`, `GraphCanvas` eller `ExitPanel` är ändrat; allt ligger i nya
  filer utom registreringen i `App.xaml.cs` och testprojektet i `Chainsaw.slnx`.

**Tester:** nytt testprojekt `tests/Chainsaw.App.TestData.Tests` (de riktiga modulerna, eftersom testärendet byggs genom
dem; `Chainsaw.App.Tests` håller sig till kontrakten), 10 gröna: kedjan går ihop och varje txid är sina bytes hash,
scenariot byggs utan problem och verifieras, varje sort och markering i `VisualLanguage` finns i grafen (utom *Kan
frysas*, se TODO; startpunkt och bevakning prövas för sig), träffen väntar, ett förslag och en egen yta finns, klustret
har *Varför?*, varje råsvar är märkt `testdata` och inget gick ut, ett riktigt ärende frågar aldrig den påhittade källan,
testärendet aldrig den riktiga och bevakningarna hålls ur bakgrunden, Navet håller tillbaka, nästa nummer, och
etiketten från kollegans ärende. Bygget är utan varningar i Debug och Release.

**Återstår (TODO, *Testärendet*):** Ethereum och *Kan frysas*, påhittade kurser, ett steg för uppbrytning och avgjort
förslag, att rensa kollegans etiketter ur biblioteket. **Inte provat på Windows** (TESTER-INFOR, *Testärendet*).

## Chainabuse som underleverantör (2026-11-08)

- `IAttributionProviders` i Attribution: flera underleverantörer, var och en frågas bara när den är på (namnen skickas
  in, så att strömbrytarna kan ligga i appens inställningar). Chainabuse är den första (`GET /v0/reports`, Basic med
  nyckeln som både namn och lösenord, nyckeln ur valvet under tjänsten *Chainabuse*).
- Varje svar förseglas i den nya kedjan `attribution.responses` (schemasteg 2), också 401, 403, 429 och fel, med
  utgång, tider, vem och SHA-256. Rapporterna blir en etikett per kategori med sorten `Reported` (röd, risk) och
  källan `LabelSource.Provider`; ursprunget pekar på det förseglade svaret. En kategori som redan har en etikett,
  också en tillbakatagen, sätts inte igen.
- Egna val: bara rapporter som nämner adressen används; en sida (50 rapporter) per uppslag för att spara anrop;
  en tillbakatagen rapport dras tillbaka också ur biblioteket, som en egen etikett.
- Återstår: sidan i inställningarna, när uppslagen görs (nyckelns gräns), TRON och grafens form för *Rapporterad*.

## Granskningen av valvyn för det som hölls tillbaka (2026-11-08)

Rättat efter en oberoende granskning av `ListHeldBackAsync`/`TakeHeldBackAsync` och valvyn (EX1, EX2):
- **Ett klusters ledtråd** räknade det som hölls tillbaka vid alla adresser men läste bara den första. Nu har
  `HeldBackClue.Addresses` alla expanderade adresser; valvyn läser alla, slår ihop dem (en tx en gång) och plockar ut
  varje tx i expansionen för den adress den lästes vid. En tx-expansions mottagare vid samma aktör får en egen ledtråd
  (`held-back-spends:`), så att räkningen aldrig tar med det valvyn inte kan visa.
- **Belopp ur tx med flera betalare** (CoinJoin, batch): adressens andel är det den satte in minus det som kom
  tillbaka (högst det som gick till andra), och när den fick tillbaka mer är tx:en en betalning in; utan inputs
  belopp andelen efter antal inputs. Beloppet märks `IsShared`, ritas streckat och sägs *osäkert* (princip 2).
- **Text som räknats fram** sägs inte som fakta: *Ut till 3 mottagare* när de är flera, *skillnad* i stället för
  avgift, och tx:ens riktiga avgift ur kedjan.
- **Negativ etikett** prövas på alla motparter och alla deras etiketter, också *Rapporterad*.
- **Obekräftade tx** ligger vid *nu*: inne i ett fönster som når nuet och alltid under *Allt*.
- **En upptagen adress:** når de nyaste 2 000 inte den spårade ankomsten läses historiken också bakåt från grafens tx
  vid adressen nära ankomsten (högst 500, sidorna förseglade genom källan som alla andra), `HeldBackHistory`.
- Mindre: exakt 2 000 tx säger inte *längre än*; utplockning utan tidigare expansion sparas som inte hel; en läsning
  som avbryts eller vars ärende stängts säger inget; en konsolidering är *Till sig själv* med beloppet som kom
  tillbaka, inte damm.
- Eget val: frövalet för läsningen runt ankomsten är grafens tx vid adressen, eftersom Esploras sidor bara kan börja
  vid en tx i adressens historik; utan en sådan tx läses bara de nyaste 2 000, och problemet säger det.
- Tester: sidor med en mempool-tx, offline ur ärendet och en saknad senare sida, gränsen och dess text, läsningen
  runt ankomsten, `HeldBackReader` (växel, flera inputs, saknade belopp, coinbase, CoinJoin, konsolidering),
  migreringen 3→4 och ett klusters ledtråd med två adresser; i appen negativ etikett på vilken motpart som helst,
  flera adresser, obekräftad tx, tipsen och en avbruten läsning.
- Återstår (TODO): om utplockning ska journalföras (fråga användaren) och att kunna avbryta en lång läsning.

## Kontroller och förslag KF2b (2026-11-08)

Panelen *Förslag* byggd om efter KF2b: översikten med *Gå igenom N att avgöra*, ett förslag i taget (huvudet *1 av N*
med pilar och förloppslist, raden under titeln, bilden av hur pengarna flyter, brickorna, den rangordnade tabellen,
*Varför?* och analysen med version, *Gjort av sig självt* hopfälld, beslutslisten Avfärda X, Senare J, Godkänn N Enter
och *+ Skäl, frivilligt*) och *Allt avgjort* med ringen, sammanfattningen och loggen med *Ångra*. Knappen i grafens hörn
är *Förslag* med antalet förslag i en accentbricka; panelen är minst 400 px.

- **Modulen Checks:** ny dom `Withdrawn` (Ångra), förseglad som de andra; det gamla beslutet står kvar. Ett förslag med
  kandidater kan avfärdas som helhet (kandidatnyckel null) men bara godkännas genom en kandidat.
  `StoredCheck.IsSettled`: avgjort när en kandidat godkänts, helheten avfärdats eller alla kandidater avgjorts;
  `Waiting` är 0 då. `DecisionFor` ger beslutet i kraft (null efter Ångra), och `KeepCandidates` räknar bara det.
- **Senare** skriver ingenting: köns ordning och omgången ligger i vymodellen.
- **Toasten** (TO1a) efter ett beslut har *Ångra · Ctrl+Z*; Ctrl+Z är bundet i grafens sida.
- **I grafen** har kandidatens linje sitt nummer mitt på (accentfyllt för den valda) och en grön bock när den godkänts;
  linjen förblir streckad (princip 2).
- **Eget val:** att godkänna en kandidat avgör förslaget (skissens flöde), de andra kandidaterna lämnas utan beslut.
- **Avsteg från skissen:** knappen *Föregående* är en spegelvänd chevron och knappen i översikten har chevron i stället
  för en triangel (ingen sådan ikon finns); analysens namn står som text, som i skissen; numren i grafen står mitt på
  linjen; brickorna saknar skissens siffror (*48 h*); växeladressens bild (band efter belopp) är inte byggd eftersom
  ingen analys ger växeladresser som förslag.
- **Tester:** modulen (helhet avfärdad, Ångra förseglad, inget att ta tillbaka) och vymodellen (översikt, kön, Senare,
  förloppslisten, bilden och tabellen, toasten, Ctrl+Z, loggens Ångra, Allt avgjort).
- Återstår (TODO): flera godkända kandidater, växeladressen, brickorna ur analysen, numren vid noden.

## Nytt ärende NC2a: startpunkterna som lista som hämtar i förväg (2026-11-08)

- Panelen efter skissen NC2a: ärendenumret stort med bock och filnamnet (`<nummer>.cut`), titeln som en tyst rad,
  startpunkterna som en rundad lista med en rad per startpunkt (kedjans märke, värdet förkortat i Cascadia Mono, sorten,
  det som hämtats i förväg och blockhöjden till höger), raden *+ Lägg till adress eller tx* (Enter, en paus i
  skrivandet eller Ctrl+V med flera rader), *via <utgång>* vid rubriken, delningen med reglaget till höger och foten
  med Ctrl+Enter. Den stora textrutan för startpunkter är borta (`TextBox.Area`).
- **Hur det förhandshämtade förseglas** (princip 1, 4 och 9): ärendet finns inte medan panelen är öppen, så
  `StartPointPrefetch` hämtar genom kedjornas vanliga källor (`IBitcoinSource`, `IEthereumSource`, alltså bara genom
  utgången och bara adressen eller tx:en) in i ett **ärende i minnet** (`IMemoryCase`). Källorna tar takt, svarar och
  förseglar där precis som i ett riktigt ärende. När ärendet skapas förseglar `CaseWorkflow` först alla råsvar i
  det nya ärendet genom `IResponseTransfer` (samma väg som bakgrundsbevakningens kö, SHA-256 kontrolleras) med en rad
  i journalen (`app.prefetch-sealed`); sedan släpps ärendet i minnet. Avbryt, Esc och låsning släpper det utan att
  något skrivits till disk. Allt som hämtades förseglas, också för en rad som tagits bort, eftersom det har hämtats.
  Pågående hämtningar stoppas innan förseglingen, så att det som förseglas är exakt det som hämtats.
- **Ärendet i minnet flyttades till Cases:** bakgrundsbevakningens `ScratchCase` blev `MemoryCase` bakom
  `IMemoryCases` i Cases.Contracts (`Create(sealedChain)` ger tabellerna för de moduler som förseglar i kedjan), så att
  appen och bevakningen delar en och samma (en regel en gång). `AddMemoryCases()` registrerar bara den, för
  bevakningen. Testerna flyttade med till `Chainsaw.Modules.Cases.Tests`.
- **Bara det källan gav:** en Bitcoin-adress visar saldot och antalet tx ur adressens sammanfattning och aktiviteten ur
  den nyaste sidan tx (första datumet bara när sidan har alla bekräftade, annars *senast …*). En Ethereum-adress har
  inget saldo i källan, så bara antal tx (med `+` när det finns äldre) och aktivitet. En tx visar beloppet, antal
  in och ut eller överföringar och dagen. Höjden är kedjans höjd när det hämtades; hovring visar också den senaste
  tx:ens block och dess hash (tip-uppslaget ger ingen hash, TODO 38).
- **Sanktion:** brickan kommer ur den importerade listan på datorn (inget skickas), också offline. Så att den inte bara
  syns i panelen kontrollerar `CaseWorkflow` nu de nya Bitcoin-startpunkterna mot listan (`CheckSanctions`), så att
  adressen får etiketten Sanktionerad med listan som ursprung (princip 2).
- **Utgången:** skalet säger till panelen när utgången byter läge (`FollowExit`); utan levande utgång skickas inget och
  raderna säger *Väntar på utgången*, med en rad under listan en gång. När utgången kommer hämtas de väntande raderna.
- **Takt:** två startpunkter hämtas åt gången (en inklistrad lista om 50 ska inte lägga 150 frågor på en gång);
  endpointarna tar takten som förut. Skrivande startar ingen hämtning: ett värde blir en rad först efter 0,7 s paus,
  och ett halvskrivet värde känns inte igen (fast form och kontrollsumma).
- Egna val: ett ärendenummer som redan finns sägs direkt under fältet i stället för först vid *Skapa ärende*; text som
  inte känns igen i en inklistring sägs i felraden i stället för att bli många röda rader; Enter på en rad som inte
  känns igen gör en rad som säger det (som i skissen), och *Skapa ärende* säger vilken som ska tas bort.
- Avsteg från skissen: värdenas något ljusare grå (`#C9CDCF`) finns inte i temat, så de ritas i textens färg med lägre
  opacitet; TRON-brickan under listan har märket i stället för ett `T`; *Upprepa* har ingen platshållare; skimret är
  tre streck som pulserar i stället för en glidande gradient.
- Tester: `StartPointPrefetchTests` (fakta ur källan, bara senaste datumet, inget skickas offline men listan läses,
  offline och okänd, TRON och okänt frågas aldrig, förseglingen med journal, Forget, panelen hämtar när utgången
  kommer, skapande och avbryt), `NewCaseViewModelTests` (inklistring, Enter, paus, nummer och fil, offline-raden,
  gränsen 50) och `MemoryCaseTests` i Cases.
- Återstår (TODO 38): Ethereums saldo, kedjans hash vid höjden, det förhandshämtade som cache och sanktionslistans
  Ethereum-adresser.

## Granskningen av KF2b rättad (2026-11-08)

- **Ångra pekar ut beslutet:** `DecisionRequest.Undoes` bär sekvensen för beslutet som tas tillbaka, och modulen
  vägrar ett Ångrat som inte gäller beslutet i kraft; sekvensen står i journalraden. Vymodellen sparar de förseglade
  besluten med ärendet (`MadeDecisions`) och tar bara tillbaka det som fortfarande är i kraft i samma ärende.
- **Modulen stoppar ett andra beslut:** Godkänn eller Avfärda vägras i transaktionen när förslaget redan är avgjort
  eller kandidaten har ett beslut i kraft. Vymodellen håller nyss avgjorda förslag utanför kön tills en körning som
  startat efter beslutet har läst ärendet.
- **Ctrl+Z bara med panelen öppen**, och alltid toasten *Beslutet ångrat*.
- Mindre: flera beslut förseglas i en transaktion (`ICheckService.DecideAll`, `ICheckSource.DecideAsync` tar en lista);
  ett Ångra som redan gjorts från loggen nollställer Ctrl+Z utan fel; *in*-beloppet är insättningens utgång ur
  fyndets nyckel och avgiften står som *trolig avgift*; ett förslag avfärdat som helhet behåller sina aktörer i loggen
  och under *Här*; långa rader brutna. `StoredCheck.Waiting` behölls, eftersom modulens tester läser den.

## Granskningen av NC2a rättad (2026-11-08)

- **Höjden byter aldrig sorts tal:** blockikonen i en rad visar bara kedjans höjd när det hämtades; en tx hämtar nu
  också kedjans höjd, och tipset skiljer *Hämtat vid block N* från *I block M* (tx:ens eget) och *Senaste tx i block
  M* (adressens). Utan kedjans höjd visas ingen höjd (`ShowsHeight`).
- **Inklistring per rad:** en rad slås upp bara när alla dess delar är adresser eller tx (princip 4); löptext med en
  hash i hämtas aldrig, och felraden säger det.
- **Skapandet väntar in hämtningarna** innan `SealInto` läser, och en låsning under skapandet släpper inte ärendet i
  minnet förrän skapandet är klart.
- Mindre: råsvaren läses ett i taget, och ett som inte går att läsa räknas i journalraden i stället för att stoppa de
  andra; ett IO-fel från sanktionslistan gör raden misslyckad; varje rads `CancellationTokenSource` tas bort när
  hämtningen är över.


## Bildspråkets ruta (GR7a, 2026-11-08)

- **En egen vy:** `Views/GraphLegendView` ersätter den gamla legenden i `GraphView` (mindre att slå ihop med de
  andra som arbetar i grafens fil). Vymodellen `LegendViewModel` (under `GraphViewModel.Legend`) håller det som hovras
  och raden längst ner; den glömmer det när rutan stängs.
- **En regel en gång:** orden står i `Graph/Legend.cs`, men form, färg, ikon och streckad ram kommer ur
  `VisualLanguage`, markeringarnas skivor ur `GraphPalette.MarkKeys` (som grafen nu också läser), linjernas bredder ur
  `EdgeWidth` och strecken ur den nya `Graph/GraphDashes` (som `GraphCanvas` nu läser i stället för egna konstanter).
  Raderna byggs ur färgen: sanktion hamnar under *Risk, slut* och kluster under *Okänt* för att deras färg hör dit.
- **Avsteg från skissen:**
  - *Rapporterad* (Chainabuse) finns med bland markeringarna, eftersom grafen ritar den; markeringarna står i grafens
    ordning (Bevakad sist), och deras ikon är 12 px (minsta storlek) i stället för skissens 10.
  - *Etikett från ett annat ärende* heter som i grafen, inte *Från annat ärende*; därför är mittkolumnen bredare
    (1,45 mot 1) i stället för tre lika breda kolumner.
  - *Annan kedja* (tx:en) har grafens ikon (swap), inte skissens tx-ikon, och skivornas färg är grafens (ögat mörkt
    på accenten).
  - Sorterna är 67 px breda i stället för 70, så att Uttags sex sorter ryms i rutans 600 px (skissen krymper dem med
    flex på samma sätt). Spårad linje är den tjockaste grafen ritar (5,2 px), inte 4.
  - Högen och ledtrådsnoden (*+N tx*) står inte med: de är grafens egen struktur, inte sorter av aktörer.
  - Avståndet mellan bokstäverna i rubrikerna (skissens `letter-spacing`) finns inte i WPF.

## Nytt ärende granskat mot NC2a (2026-11-08)

- Gått igenom panelen element för element mot skissen och användarens synpunkter (*inga linjer vid raderna*,
  *betydligt mer professionellt*). Logiken från granskningen (höjden är kedjans, inklistring per rad) är orörd.
- **Rättat:** linjen mellan raderna är borta (hovringens grund skiljer dem åt); en ny rad glider in 4 px på 0,22 s;
  skimret är en glidande ljusning över tre streck (1,4 s) i stället för en puls, och står still utan animeringar;
  snurran har sin svaga ring; frågetecknet för det okända är 17 px som TRON-märket; blockhöjden har siffror i fast
  bredd; × på en rad tar bort med Del när den har tangentbordet (tipset *Ta bort · Del*); plusrutan får den markerade
  grå på hovring och platshållaren *Lägg till adress eller tx* den svaga textens grå i stället för den svagaste;
  formerna före första startpunkten är ₿, Ξ och T som i skissen, med 6 px till namnet och tecknet i den mjukare grå;
  utgångens bricka har en röd kant på 40 % när den är offline (texten hel); panelens skugga faller 24 px åt vänster;
  × i huvudet har radie 8 och grå kant på hovring; reglaget glider på 0,15 s och lösenordsfälten fälls in; *Upprepa*
  har platshållaren *Samma igen*; *Skapa ärende* är nedtonad till 40 % (temats 60 % gånger knappens 67 %).
- **Avsteg kvar:** skissens grå `#C9CDCF` finns inte i temat, så värdena ritas i textens färg på 82 %; *Avbryt* följer
  temats `Button.Secondary` (grå text) i stället för skissens ljusare; bocken i numret syns så snart numret är giltigt,
  också medan man skriver; sanktionens ikon är 12 px (minsta storleken) i stället för 11.
- Inga nya tester: bara vyn ändrades (XAML och två händelser i koden bakom).

## Luft i testärendets graf (TODO 34, 2026-11-08)

- **Orsaken:** kolumnerna stod 158 enheter isär med noder på 132, så mellan två hopp fanns 26 enheter för beloppen,
  som därför låg på noderna; raderna stod 64 isär. Grafen passades in i sin helhet, ned till zoom 0,2, så att elva hopp
  blev små noder. Alla områden var påslagna i menyn från början och ritades över varandra.
- **Avstånden** (`GraphSizes`): kolumnerna 292 isär (160 luft, nog för *0,689997 av 1,0000*), raderna 104 isär. GR4:s
  158 och 120 var skissens; bredden följer nu de längsta beloppen. Gäller båda lägena, eftersom den fria ytan börjar
  från hoppkolumnerna.
- **Beloppen** (`AmountLayout`, utan WPF): varje linjes belopp får en plats där det inte rör ett annat belopp eller en
  nod (med 8 enheters marginal för markeringarna). Mitt på linjen först, sedan utåt längs linjen (0,14–0,86), sist
  precis ovanför eller under den. De spårade placeras först. Platsen räknas ur en uppskattning av textens storlek i
  överkant (6,4 enheter per tecken vid 11 px), så att reglerna kan testas; duken mäter den riktiga texten och centrerar
  den på samma punkt. En linje utan plats visar beloppet bara på hovring eller nära det valda, som förut. Ett rutnät på
  160 enheter håller placeringen snabb medan noder dras.
- **Två linjer mellan samma noder** (pengar fram och tillbaka) böjs isär 28 enheter vid mitten (`EdgeCurve.Bent`), så
  att de inte ligger på varandra och båda beloppen får plats.
- **Inpassningen** (`Viewport.Fit`) zoomar inte ut förbi 0,75 (`ReadableZoom`); en större graf visas från sitt övre
  vänstra hörn, där pengarna börjar. Gäller också *Visa i grafen* för ett område.
- **Områdena** ritas bara för de sorter som valts i menyn, och inga från början; ett kontrollområde syns annars bara
  för det visade förslaget. En egen yta som just ritats får sin sort påslagen. Valet gäller tills Chainsaw stängs och
  sparas inte (eget beslut: det säkraste mot brus; att spara det per dator kan läggas till). Kandidaternas streckade
  linjer följer inte menyn: de ritas för alla förslag (bara det visade förslagets under genomgången), så att en
  godkänd utgång ur en mixer står kvar i grafen.
- **Tester:** `TestCaseLayoutTests` (testärendet, båda lägena, med och utan navet expanderat: inga noder närmare än 24,
  varje belopp har en plats, inga belopp på varandra eller på noder) och `AmountLayoutTests`; tre tester i
  `ChecksViewModelTests` visar nu sorterna först.
- **Återstår:** en linje kan fortfarande gå genom en nod när ett hopp med fler än åtta aktörer sprids över två
  kolumner (navet till CoinJoin i testärendet); se TODO.

## Tidslinjen under grafen (TL1a, 2026-11-08)

Byggd efter skissen `TimelineA` utan ny etapp, eftersom den bara ändrar vad som syns: inget skrivs i ärendet.
Geometrin står utan WPF i `Graph/TimeAxis.cs`, `Graph/TimeBands.cs`, `Graph/TimeStripLayout.cs` och
`Graph/GraphTimeline.cs`; vymodellen är `GraphTimelineViewModel` (`GraphViewModel.Timeline`), banden ritas i
`GraphCanvas.Timeline.cs` och listen är kontrollen `TimeStrip`.

- **Egna beslut:** en aktörs tid är när pengarna först kom dit i grafen (`FirstReceived`), annars dess första
  utgående tx (en startpunkt som bara skickar). Aktörer och linjer utan bekräftad tx räknas som *nu* och bleknar vid en
  vald tid. Dygnen räknas i Windows tidszon (sommartid ger ett kort dygn). Linjens naturliga plats är den plats från
  vänster till höger som lämnar färst aktörer på fel sida, så att en utdragen aktör inte flyttar linjen utan passeras
  runt; samma regel gäller båda lägena, och i hoppkolumnerna blir linjerna därför i stort sett raka. Linjerna räknas
  om bara när platserna ändras och ritas bara där de syns (princip 6). Listen slutar en bit efter den sista tx:en
  (minst 12 h), eftersom ärendet inte vet senaste blockets tid. Ett klick nära ett streck väljer tx:ens tid, annars
  tiden under pekaren.
- **Avsteg från skissen:** *Till nu* visas bara när en tid är vald (princip 7: en knapp som inte går att använda
  visas inte). Statusradens *Tid:* finns inte, eftersom Chainsaw saknar statusrad. Listen står närmare nederkanten än
  i skissen, eftersom kommandoraden är dold. Kortet har en ritad skugga.
- **Återstår:** band i timmar när man zoomar in (skissens anteckning: timmar, dygn eller veckor; nu dygn och grupper
  av dygn), och listens slut vid senaste blocket.

## Inspektören efter IN2b, GR5, TK1a och AN1b (2026-11-08, TODO 39.2)

- **Egen vy:** inspektören är flyttad ur `GraphView.xaml` till `Views/InspectorView.xaml` (samma vymodell), så att
  andra ändringar i grafen inte krockar med den. `ChecksPanel` står kvar ovanpå i grafens vy.
- **Huvudet (IN2b):** sortens ikon i en ram på 38 px i nodens form (streckad när noden är det), rubriken 17 px, och
  under den `InspectorContent.Subtitle`: sorten, kedjan, *N adresser* för ett kluster och *startpunkt* (ersätter
  brickan *Startpunkt* med flaggan och fakta *Sort* och *Kedja*). En linje säger *Linje · Bitcoin* (och den andra
  kedjan när pengarna fortsätter där). En nod utan namn har rubriken i grått (GR5, `IsNamed`). *Varför?* är en rund
  knapp på 32 px.
- **Fakta:** *Tx* (antalet tx på nodens linjer i grafen) och *Senast* (*block N*, högsta blockhöjden på dem) är nya,
  som i skissen. Liksom *In* och *Ut* gäller de grafens linjer, inte adressens hela historik.
- **Adresserna (IN2b):** `InspectorDetail` (id och en not under), i en mörk ruta i Cascadia Mono med kopiera på 30 px;
  en linjes block står under sin txid i stället för som egen rad. `Details` finns kvar som platt lista.
- **Saldo per token (TK1a, listan):** märket i en rund bricka, snöflingan på sin skiva, utgivaren under, det som
  ligger kvar till höger och SEK under. SEK räknas till den senaste kursen ärendet har för tillgången
  (`GraphSnapshot.Rates`, ur samma uppslag som linjernas värden, inga nya uppslag); tipset visar kursens tid och
  stegen. Utan kurs visas ingen SEK. Avsteg: skissen säger *vid tiden*; dagens kurs kräver ett nytt uppslag och hör
  till kortets baksida. Rutan om frysning har tre nya färger i `Colors.xaml` (`Brush.Freeze.*`).
- **Avsteg:** *Begäran om frysning …* visas inte, eftersom ingen sådan begäran går att göra än (princip 7). Snöflingans
  skiva är 18 px med ikonen 12 px (minsta ikonstorleken), inte 16/9 som i skissen. Inspektörens bredd är 340 px som
  i IN2b (var 280).
- **Historik (AN1b):** runda markörer på 18 px, tiden i svag text, *Ta tillbaka*/*Ångra* som piller under raden (accentens
  ram när formuläret är öppet), och formuläret utan egen ruta. Klustringens slutsatser är nedtonade som fakta.
- 10 nya tester i `InspectorContentTests`; tre tester ändrade (*Sort* och *Kedja* står nu i raden under rubriken).

## Soptunnan (SO1a, 2026-11-08)

Byggd som en etapp, eftersom den skriver i ärendet.

- **Modellen (Tracing):** `IGraphTrash` med `TrashThrow`, `TrashMember` och regeln `TrashTree.Follow` i Contracts.
  Varje slängning och varje återläggning är en förseglad post i en egen kedja (`tracing_trash.records`, en egen
  `TrashSchema`-bidragare bredvid `TracingSchema`, så att versionerna inte krockar med grafens tillstånd) med en rad i
  journalen (`tracing.trashed`, `tracing.restored`) i samma transaktion. Det som ligger i soptunnan läses genom att
  spela upp posterna; inget tas någonsin bort, och grafens platser rörs inte, så det som läggs tillbaka står där det
  stod.
- **Vad som följer med:** det som bara nås genom det slängda, räknat på den **ritade** grafen (högar och ledtrådar
  ingår) och åt båda håll, så att en avsändare som bara syns vid den slängda följer med i stället för att bli kvar
  ensam. Startpunkterna är ankare; slängs den enda startpunkten står noder utan inkommande linje i deras ställe.
- **Nycklar:** en aktör på Bitcoin hittas igen på alla sina adresser (`TrashFilter.KeysOf`), så att ett kluster som
  växt eller fått en annan representant förblir dolt.
- **Grafen:** `GraphFold` tar bort det slängda (aktörer, högar med sina kort, ledtrådar vid en slängd aktör), och en
  linje med ena änden i soptunnan blir en kort streckad stump med *i soptunnan* vid den andra nodens port
  (`GraphCanvas.Trash.cs`). Medan varningen är öppen får det som följer med en streckad ram och resten tonas ned.
- **Bevakat slängs inte i tysthet:** varningen öppnas alltid när något bevakat följer med, och varje bevakad rad har
  en strömbrytare *Avsluta* (på som förval). De som lämnas på avslutas genom Monitoring (egen förseglad post, skälet
  *Slängdes i soptunnan*) innan slängningen skrivs; de andra fortsätter, och vyn visar *bevakas* på raden.
- **Startvyn:** ett lyft ärende får soptunnan att växa nere till vänster; släppt där döljs det på startsidan.
  Sparas i `%LOCALAPPDATA%\Chainsaw\trash.json` som högarna (`CaseTrashStore`), aldrig i ärendet, och filen ligger
  kvar på disken. Ett ärende som läggs tillbaka står bland de lösa igen oavsett ålder.
- **Egna beslut och avsteg från skissen:** vyn öppnas genom ett klick på soptunnan och lägger sig över grafen (ingen
  egen ikon i pillret, som i skissen); den har också en del *Ärenden från startsidan*. Skissens *pausas* för
  bevakningar är ersatt av frågan om att avsluta (CLAUDE.md, *Bevakat slängs inte i tysthet*). Noden krymper inte när
  den närmar sig soptunnan som i skissen. Ctrl+Z ångrar bara den senaste slängningen i sessionen. Delete slänger den
  valda noden, de valda korten i en utfälld hög eller aktörerna valda för en yta.
- **Återstår (TODO):** kommentarsnoder och egna ytor går inte att slänga än (kommentarsnoden byggs parallellt), en
  hel hög i startvyn kan inte dras till soptunnan, och testärendet (`/testdata`) har ingen slängning.
- Tester: 19 i `Chainsaw.Modules.Tracing.Tests` (`TrashTests`, `TrashTreeTests`) och 35 i `Chainsaw.App.Tests`
  (`TrashTests.cs`, `StartTrashTests.cs`).

## Delarna tillsammans och mixer efter mixer (2026-11-08)

- **Två nya analyser, inte en utvidgad `mixer-outputs`:** `mixer-parts v1` (*Delarna tillsammans*, förslag) och
  `mixer-chain v1` (*Mixer efter mixer*, kontroll). Skälet: `CheckCandidate` är en utgång, och en kombination av delar
  passar modellen bäst som ett eget förslag där varje del är en kandidat (KF2b-panelen och besluten per kandidat
  fungerar som de är). En egen analys har egen version och egna beslut, så `mixer-outputs v1` hittar exakt som förut
  (dess tester är orörda) och ett beslut om en hel utgång blandas inte ihop med ett om en del. Insättningar och
  utbetalningar läses på ett ställe (`MixerDeposits`), som de tre analyserna delar.
- **Delarna:** två till fyra utbetalningar inom 7 dygn, var och en minst en tiondel av insättningen (så att ingen del
  ensam kan vara en hel utgång; de ligger inte i båda förslagen), med summan inom insättningen minus 0–10 % avgift.
  Poäng: avgiften 0,45, tiden (medel) 0,2, spridningen (andel delar i egna block) 0,15, adresstypen (medel) 0,2; lägst
  0,5. Lika poäng avgörs av färre delar och sedan nycklarna. **Gränsen:** bara de 20 utbetalningarna närmast efter
  insättningen prövas, vilket ger högst 6 175 kombinationer per insättning, och grenar som redan är för stora följs
  inte. Bara den bästa kombinationen visas; `Why` säger hur många som passade.
- **Mixer efter mixer:** `CheckInput` har fått `Approved` (godkända kandidater med mynt), som `CheckService` bygger ur
  besluten i kraft. Kedjan följer godkänt före troligt, sedan poäng, och går genom hela utgångar och delar. En ny
  insättning i en känd mixer får mixerns förslag av sig själv (testat). En CoinJoin (klustringens fynd, igenkänd eller
  möjlig) avslutar kedjan. Högst 10 steg. Steget visar sin avgift: insättningen minus utgången, eller minus alla delar
  för en del. Kontrollen förseglas på nytt när ett godkännande ändrar vilken väg den följer.
- **Panelen:** nya brickor för båda; för delarna räknas *trolig avgift* mot alla delar som inte är avfärdade
  (`SuggestionWords.CandidatesAreParts`). Guiden har ett nytt avsnitt, *Mixer efter mixer och delarna*.
- **En granskare bör titta på:** att `Approved` byggs ur senaste beslutet per kandidat (`CheckService.ApprovedOf`) och
  att en ändring där inte kan göra resultaten icke-deterministiska; vikterna och gränserna i `MixerPartsCheck` (risken
  att en kombination passar av en slump i en stor graf); att `MixerOutputsCheck` efter omskrivningen till
  `MixerDeposits` ger samma resultat (samma tester gröna); att kedjans text aldrig kallar ett steg ett faktum.
- **Återstår:** se TODO (testärendet, okända mixers mitt i en kedja, delar som går in i nästa mixer tillsammans).
- **Efter granskningen (2026-11-08):** högst en del per tx, och aldrig två där den ena spenderar den andra
  (`MixerPartsCheck.Related`). `CheckInput` bär också `Dismissed` (avfärdade kandidater och hela förslag i kraft), och
  `CheckService.DecisionsOf` ger båda listorna. `mixer-parts` kombinerar aldrig en avfärdad del, väger först en
  kombination som håller de godkända delarna och visar beslutade delar utanför kombinationen för sig med poäng 0 och en
  mening om att de inte hör dit. `mixer-chain` följer aldrig en avfärdad kandidat eller ett helt avfärdat förslag,
  läser bara godkännanden i `mixer-outputs` och `mixer-parts` (namn och version), kallar en godkänd del utanför
  kombinationen *del* med avgiften mot alla godkända delar, slutar bara vid en igenkänd CoinJoin (formulerad som
  klustringens bedömning), säger när den kapats vid 10 steg och räknar aktörer med samma tjänst (mixerslutets
  förklaring i `KnownMixer.Service`) som samma mixer. Panelens avgift klipps vid 0, säger *Delarna är mer än
  insättningen* när delarna inte kan höra ihop, och visas inte när alla delar är avfärdade. Kandidater med samma poäng
  står i analysens ordning (delarna efter tid) i `KeepCandidates`. Brickan säger *samma block*, som poängen mäter.

## BV1a, TO1a och de instabila App-testerna (2026-11-08)

- **BV1a punkt för punkt:** bakgrundens läge står i en rundad ram i vyns huvud, hel när bevakningen går och streckad
  (gul när den väntar, grå när bakgrunden är av), eftersom streckat alltid betyder osäkert (`IsFrameDashed`). Varje rad
  och högerpanelens huvud ritar den bevakade noden som grafen gör (sortens ikon, färg och form, `SortLook` genom
  `GraphViewModel.LookIn`); finns noden inte i grafen ritas den som kluster eller okänd. Motparternas brickor har en
  pil för riktningen, läget i panelen står i lägets färg, och *Öppna i grafen* leder bland knapparna längst ner.
  Vyns röda felrad är borta: ett problem sägs en gång som en toast (TO1a).
- **TO1a punkt för punkt:** strecket i nederkanten finns på alla toasts, krymper för lyckat och information och står
  stilla för fel och varningar (`Toast.CountsDown`); ikonen 15 px på sin tonade skiva, texten överst bredvid ikonen,
  krysset dämpat och toasterna 76 px upp som i skissen, så att hörnets *?* går att nå.
- **De instabila testerna** hade tre orsaker, alla tester som läste tillstånd medan bakgrundsarbete fortfarande skrev:
  - `CaseWorkflow.RunAlone` listade ärendena i bakgrunden utan att vänta (`_ = RefreshCasesAsync()`), och
    ärendeväljaren och skalet listade en gång till. Med testernas UI-tråd som kör direkt fyllde två listningar
    väljarens rader samtidigt. Nu väntar `RunAlone` in sin listning innan svaret kommer, och de dubbla listningarna
    är borttagna.
  - `SettingsTests.Dispose` tog bort inställningsmappen medan inställningarna skrevs i bakgrunden (*Directory not
    empty*). `SettingsViewModel.Saving` ger skrivningen, och testerna väntar in den.
  - `ShellViewModelTests` väntade på utgångens namn men läste raden under, som skrivs strax efter; testet väntar nu på
    båda. Samma mönster i profilens tester (`WaitForTheProfile`).
  `tests/Chainsaw.App.Tests` kördes 60 gånger, två processer i taget, utan fel.
- Avsteg från skissen: huvudets räknare säger inte *sedan 09:10* (se TODO); storlekarna följer appens text, ett steg
  större än skissens.

## Kommentarsnoden (KN1a, 2026-11-08)

- **Modulen (Tracing):** `IGraphCommentService` med `GraphComment`, `CommentRules` och schemasteg 5. Den förseglade
  kedjan `tracing.comments` (`tracing_comments`) har en post per handling: *add* (text, tråd och första plats), *edit*
  (ny text), *anchor* (ny eller ingen tråd) och *remove* (texten som togs bort står kvar), var och en med operatören
  (`set_by`), journalrad (`tracing.comment-added`, `-edited`, `-anchored`, `-removed`) och tid i samma transaktion.
  Inget skrivs över. Flyttad plats och storlek är vanlig data i `tracing_comment_places`, som grafens platser.
  Samma text skriver inget; en tom text vägras (en tömd lapp tas bort i stället).
- **Tråden** sparas under `address:` och en av nodens adresser för en adress eller ett kluster, annars nodens nyckel,
  så att den hittar noden igen när klustret ändras (`CommentGeometry.AnchorOf` och `Resolve`).
- **Markdown:** Gawks parser övertagen till `Chainsaw.SharedKernel/Text/Markdown.cs` med Gawks tester och ett nytt för
  skissens lapp. Ritningen (`NoteMarkdownView`) är skriven för lappen efter Gawks `MarkdownView`; färgningen medan man
  skriver (`NoteColorizer`) efter Gawks `MarkdownColorizer`, i **AvalonEdit** (nytt NuGet-paket, MIT, i
  `THIRD_PARTY.md`). Bara `chainsaw:`-länkar följs (noden väljs och visas, inget slås upp); andra adresser ritas grå
  och följs aldrig (princip 4).
- **Grafen:** lapparna ligger i ett lager som `GraphCanvas` bär som barn, med vyns transform, så att de panoreras och
  zoomas med grafen och höger-, mittknapp och hjul går vidare till duken. Trådarna ritas av duken under noderna.
  N, dubbelklick på tom yta, ikonen *Kommentar* i nodens list och raden i högerklicket; Delete tar bort vald lapp, Esc
  släpper den; pricken vid nodens ände dras till en annan nod eller till tom yta. Medan en lapp skrivs i tar grafen
  inga tangenter.
- **Egna val:** en ny lapp är ett utkast som inte finns i ärendet förrän den lämnas med text (ett tomt utkast
  försvinner utan spår); en lapp som inte kunde sparas står kvar i skrivläge med sin text. N med vald nod knyter
  lappen till noden, som ikonen gör. Lappens naturliga bredd är 280 (skissen 270–300), handtaget ger 200–620 × 110–420,
  och höjden är en minsta höjd så att text aldrig döljs. `/testdata` har en lapp vid offret med länk till Nordbörsen.
- **Avsteg från skissen:** radavståndet medan man skriver är AvalonEdits; den streckade tomma lappen har handtaget och
  foten dolda; ledtexten *Dubbelklick på ytan · N* ovanför den tomma lappen är inte med (kortkommandot står i
  verktygstipset).
- **Tester:** `CommentTests` i Tracing (försegling med vem och när, historik, ändring, tråd, borttagning, plats som
  vanlig data, UPDATE och DELETE vägras, `/verify`), `MarkdownTests` i SharedKernel, `CommentTests` och
  `CommentGeometryTests` i App (utkast, lämna, misslyckad sparning, flytt och storlek, tråd, länk, nodens ikon och
  meny) och ett test av testärendets lapp. Facit i Tracings `GoldenTests` har fått schema 5 och den nya kedjan.
- **Återstår:** se TODO, *Kommentarsnoden (KN1a)*.

## Högen, ledtråden och valvyn: det som återstod (2026-11-08)

- **Scenen byggs i bakgrunden:** `GraphReading` (metoden, *Visa dem* och adresserna som hålls utanför högarna) går in
  i `IGraphSource.LoadAsync`, `ExpandAsync` och `TakeHeldBackAsync`, så att källan bygger scenen som den visas, en
  gång och utanför UI-tråden. *Visa dem*, *Dölj dem* och en ändrad bevakning bygger om scenen ur det den redan har
  (`GraphScene.DrawnAs`) med `Task.Run`; `Show` bygger aldrig om på UI-tråden. `TracingMethodKind` blir en
  `GraphReading` av sig själv, så att anrop med bara metoden står kvar.
- **Ritningen:** `DrawCache` (utan WPF, testad) håller pennor och mätta texter per det de görs av, med ett tak där den
  börjar om; `GraphCanvas.Cache.cs` använder den för högens kort, högen, pillen, valvyns prickar, sonar, hörnmärken,
  guider och mittkortet, och för beloppen på linjerna. Texterna mäts om när skärmens skala ändras. `AmountLayout`
  hade redan ett rutnät för platserna; det är utbrutet till `RoomGrid` och används också av `DrawAmount`, där
  krocktestet var O(k²).
- **Bevakat viks aldrig in:** `GraphWatches.KeptOutOfPiles` ger bevakade adresser, motparter i filtren och motparter
  som en träff för en gällande bevakning nådde; `GraphFold` lägger aldrig en sådan aktör i en hög. Tracing vet inget
  om det: aktören står kvar som medlem i högen i modellen och ritas bara för sig, och går tillbaka i högen när
  bevakningen tas bort.
- **Ledtrådens linje** har en egen sort, `GraphEdgeKind.HeldBack`, och räknas därför aldrig som en överföring.
- **Nycklarna:** formen på högens och ledtrådens nycklar är oförändrad (`pile:out:address:…`, `held-back:address:…`),
  så inget sparat ändras. Nyckeln är den under vilken användaren senast flyttade högen eller pillen, ur vilken som
  helst av ankarets adresser (`PileRules.StableKey`), och ankarets representant bara tills den flyttats. Platsen följer
  därför med när klustret växer eller får en annan representant.
- **Egna beslut:** en bevakad aktör hålls utanför högen också när bevakningen är pausad, så att ögat står kvar.
- **Återstår:** markeringen för dolda OP_RETURN (kräver en skiss); den streckade guidens geometri i valvyn och
  rombens geometri görs fortfarande per bildruta.
- 17 nya tester (`DrawCacheTests`, `PileTests` i App och Tracing, `GraphSourceTests`).

## Guidens figurer och analogier, spalten centrerad (2026-11-08)

- **Spalten centrerad:** vyn Guide håller textspalten (högst 620 px) mitt i ytan till höger om förteckningen; texten i
  den är vänsterställd. Panelen (GD1b) fyller sin bredd som förut.
- **Figurer:** `![Bildtext](figur:<id>)` på en egen rad blir `GuideFigure`, ritad av `GuideDocumentView` ur
  `DrawingImage` med nyckeln `Figure.<id>` i `Themes/GuideFigures.xaml` (25 figurer). Nya avsnitts figurer läggs i
  `Themes/GuideFigures.Sections.xaml` (tom, sammanfogad i `App.xaml`). En figur är högst 560 px och krymper i panelen.
  En okänd figur visar en streckad ram och *Bilden saknas*, aldrig ett fel. Bildtexten får innehålla länkar.
- **Figurerna är utan ord** (princip 7): form, färg och linje som i grafen, och bildtexten säger vad de visar. De är
  ritade med ett skript i sessionens scratchpad (inte incheckat) och provritade i Chromium före; vill man ändra en
  figur ändras XAML direkt.
- **Analogier:** `> **Som i vardagen:** …` blir `GuideAnalogy`, ritad som en ruta på upphöjd yta med en glödlampa
  (`Icon.Suggest`) i sandtonen (`Brush.Area.Sand`), inte i accenten, som betyder offrens pengar. Alla avsnitt utom
  *Mixerns troliga utgångar* (en annan agents) börjar med en. Etikettexterna sa *magenta*; ändrat till accentfärgen.
- **Återstår:** *Mixerns troliga utgångar* saknar analogi och figur. Figurer i *Startpunkter* och *Kontroller och
  förslag* tillfördes inte (texten räcker). Kryptospåraren bör titta på analogierna.
- Tester: `ReadsFigures`, `ReadsAnalogies`, `EveryFigureIsDrawn` (varje figur som en text nämner finns som nyckel i
  `GuideFigures*.xaml`), `SectionsOpenWithAnAnalogy` (21 fall) och `ViewAndPanelShowTheFigures`.

## Guiden: fler avsnitt med vardagsbilder och figurer (2026-11-08)

- 31 nya avsnitt i `docs/guide/` för det som är byggt: ärendet (Nytt ärende, ärendeväljaren och högarna, ärendefilen,
  journalen och `/verify`, offline, kommandoraden och länken), säkerhet och utgångar (huvudnyckeln och valvet, låsning,
  Windows eller VPN, WireGuard, källornas endpoints, återställningsnyckeln), arbetet i grafen (inspektören och
  ikonlisten, att röra sig, tidslinjen, det som hölls tillbaka och valvyn, historiken, uppbrytning, annan kedja,
  OP_RETURN), etikettbiblioteket och Chainabuse, börser och begäran, förvirring (uppdelning, fördröjning och hopp,
  bryggor och swappar, nästlade tjänster, privacy coins, USDT på TRON), vyerna, kortkommandon och en ordlista.
- Varje avsnitt börjar med en analogi (`> **Som i vardagen:**`) för den som inte är tekniker, och säger ärligt *inte
  byggt än* om det som inte finns (vyerna Begäran, Etiketter, Tidslinje, Flöde och Exponering, att öppna en kollegas
  fil, länken i Windows, TRON, kontrollerna för uppdelning och hopp, soptunnan, kommentarsnoden, innehavet med Alt).
- 29 figurer (`Figure.s-*`) i `Themes/GuideFigures.Sections.xaml`, sammanslagen i `App.xaml`, ritade ur ett skript
  på en yta om 560 × 200 med bara temats färger och bildspråket, utan text. Provritade som SVG i Chromium.
- Index: nya grupper i `GuideLibrary.Index`. Förvirringens nya avsnitt står under *Förvirring*,
  så att gruppen *Förvirring* (mixerns analyser) inte krockar.
- Tester: `GuideSectionsTests` (avsnitten i sina grupper, analogin först, varje figur som nämns finns ritad, ordboken
  sammanslagen).
- Avsteg: soptunnan, kommentarsnoden och innehavet på nodens baksida fick inga avsnitt, eftersom de inte är byggda;
  de skrivs när de byggs.
- Efter sammanslagningen: *Soptunnan* och *Kommentarer i grafen* med tre nya figurer (`s-soptunnan`,
  `s-soptunnan-vyn`, `s-kommentar`), i gruppen *Arbeta i grafen*; ärendeväljaren, kortkommandona, *Röra sig i
  grafen*, ordlistan och inspektören (× *Stäng · Esc*, ikonen *Kommentar*) uppdaterade.

## Granskningen av KN1a rättad (2026-11-08)

- **C1:** Markdown-läsaren har inte längre obegränsad rekursion: länkar nästlas högst 32 nivåer (djupare är text),
  `Flatten` går med egen stack, och en länks text och adress söks högst 2 000 tecken bort, så att en rad med många
  hakparenteser läses i linjär tid. Test på en tråd med 1 MB stack.
- **I1:** `CaseSession.Closing` (före Close, Forget och Open) förseglar lappen som skrivs i medan ärendet ännu är öppet;
  `Clear` nollställer skrivläget.
- **I2–I5:** skrivningarna köas per lapp (`Pending`, `IsStoring`); en misslyckad ändring ger tillbaka texten; ett
  utkast som sparas släpps aldrig av N; en lapp som öppnas medan den sparas visar det senast skrivna; ett utkast blir den
  sparade lappen direkt efter `AddAsync` (samma objekt), och ett fel med storleken säger bara att platsen inte sparades.
- Mindre: längden kontrolleras före skrivningen; färgningens mönster har en tidsgräns på 50 ms; en sparad ändring rör
  inte platsen eller storleken; en länk vars text namnger en annan adress visar vart den går (`NoteLinks.Misleads`),
  och länkar jämförs på kedja och exakt värde; Ctrl+V i en lapp tas inte av startpunktens fält; Esc stänger
  förslagspanelen före lappens val.

## Mjuk zoom i grafen (2026-11-08)

- Hjulet, pekplattan och + och − zoomar mjukt som i Gawk (`BuilderView`/`RelationsView`): zoomen glider mot ett mål
  med en exponentiell glidning på zoomens logaritm (tidskonstant 0,07 s, samma som Gawk), så att in och ut känns lika.
  Punkten under pekaren står still i varje bildruta, och snabba hack läggs till målet i stället för att hoppa.
- Logiken ligger utan WPF i `Motion/ZoomMotion.cs` (testad i `ZoomMotionTests`, 11 tester), och duken driver den
  med `FrameClock` i `Controls/GraphCanvas.Zoom.cs`. `Easing.Toward` och `Viewport.ZoomTo` tillagda; `Motion/` är nu
  med i testprojektet.
- Pekplattans små steg ger en andel av ett hack (`WheelStep` upphöjt till delta/120), inte ett helt hack som förut.
- Egna beslut: en zoom stoppar glidningen efter en panorering, och ett tryck (vänster, höger, mitt) stoppar zoomen där
  den är, som i Gawk. F, de egna ytornas inpassning och grafens sparade läge avbryter zoomen utan att spara mellanläget.
  Läget sparas en gång när zoomen landat, inte per bildruta. Utan animeringar i Windows hoppar zoomen direkt.
- Avsteg: inget gummiband vid zoomens gränser, eftersom Gawk inte har något där (bara vid kanterna när man rullar);
  zoomen stannar mjukt vid 20 % och 300 %. Varje bildruta skapar en ny `Viewport` (en liten record), som panoreringen
  redan gör; ritningen i övrigt använder `DrawCache` och ritar bara det som syns.

## Soptunnan efter granskningen (SO1a, 2026-11-08)

- **Ärendet följer med (C1, I3):** en slängning och en återläggning hör till ärendet som var öppet när de började.
  Ångra och Ctrl+Z vägras i ett annat ärende, och efter varje väntan kontrolleras att samma ärende är öppet. En
  läsning av soptunnan behålls bara om den är den senaste och ärendet är detsamma.
- **Slängningen först, bevakningarna sedan (I1):** en slängning som misslyckas avslutar ingen bevakning; en bevakning
  som inte kunde avslutas efteråt sägs. Toasten säger att Ångra inte lägger tillbaka bevakningarna, och knappen
  säger *Släng och avsluta N bevakningar* (M1). Också bevakningar som har det slängda som motpart frågas om (I5),
  och stumpen vid en bevakad nod säger *i soptunnan · bevakad*.
- **Dolt bara när allt pekar dit (I2):** en nod döljs när dess nyckel ligger i soptunnan eller alla dess adresser gör
  det (`TrashMember.IsHidden`, samma regel i modulen och appen); ett kluster som vuxit in i en slängd adress döljs inte.
- **Startpunkten (I2b):** en slängd startpunkt sägs med en varning och *Lägg tillbaka*, igen när en startpunkt läggs
  till; en graf där allt ligger i soptunnan säger det.
- **En slängning i taget (I4):** två snabba Delete slänger en gång, och *Det ligger redan i soptunnan* skiljs från ett
  skrivfel (modulen kastar `InvalidOperationException` för det).
- **Kommentarer (I6):** en lapp vars nod ligger i soptunnan döljs med den och kommer tillbaka med den.
- **Mindre:** `GraphPile.AccountKey` är den enda regeln för en nyckel på en kontokedja (M2); att lägga tillbaka en
  del tar med det den hänger under (M3); ett ärende som slängs innan soptunnan på datorn lästs sparas, och ett fel vid
  sparandet sägs (M4); trädet går utan rekursion (M7); Delete hanteras bara när något är valt (M9).

## Sortera grafen (2026-11-08)

- **Byggt efter användarens beskrivning utan skiss** (användarens ord: en *auto sort* som sorterar upp en stökig graf
  snyggt och prydligt, inte kompakt, med frågan *vill du verkligen* först, ikonen uppe till höger).
- `Graph/TidyLayout`: kolumner per hopp (som `HopLayout`, utan hopp sist), ordningen i varje kolumn med
  barycentermetoden (fyra svep fram och tillbaka mot grannarnas medelhöjd, stabil sortering, alltså deterministisk),
  varje kolumn centrerad kring samma mittlinje. Raderna står 136 enheter isär (hoppkolumnernas 104 + 32) för luft
  kring beloppen; kolumnerna `LaneSpacing`. Ett stort hopp sprids som i `HopLayout.RowsFor`, men fylls rad för rad så
  att aktörer som dras mot samma höjd står bredvid varandra.
- Ikonen visas bara på den fria ytan med ärende och minst två aktörer; **i hoppkolumnerna döljs den** (de är redan
  sorterade). Frågan är en liten ruta under hörnet (`Views/TidyConfirm`), Enter och Esc både i rutan och i ytan.
  Uträkningen görs utanför UI-tråden; flyttas ingen aktör sägs *Grafen är redan sorterad.* utan fråga.
- Platserna sparas i **en** skrivning: nytt `ITracingService.SavePositions` (en transaktion, allt eller inget, ingen
  journal, vanliga data som när noder dras) och `IGraphSource.SavePositionsAsync`. `SavePosition` går genom den.
- Högar och ledtrådsnoder är noder i layouten med sitt hopp och följer sin aktör genom linjen dit. Knutna kommentarer
  behåller avståndet till sin aktör (sparas var för sig som när de dras, genom `CommentsViewModel.Move`/`DropAsync`);
  lösa står kvar. `CommentsViewModel` och soptunnan är orörda.
- **Ångra** finns bara som knapp i meddelandet: Ctrl+Z hör redan till soptunnan och förslagen. Ångra lägger tillbaka
  varje aktörs och kommentars plats exakt (också de som inte hade någon sparad plats får den de visades på) och gäller
  bara i samma ärende.
- Tester: `TidyLayoutTests` (7) och `GraphTidyTests` (10) i App.Tests, två i Tracing.Tests (`StateTests`).
- Återstår: korsningar minimeras bara med barycentermetoden; linjer mellan kolumner som inte är grannar kan fortfarande
  korsa. Ingen animering mellan platserna.

## Välja flera med en ruta (2026-11-08)

- **Byggt efter användarens beskrivning av ett standardmönster, utan skiss** (*finns dra för att markera flera? som
  på skrivbordet på windows*). Formen följer Windows: en tunn ram på 1 px och en svag fyllning, här i accenten
  (`Brush.Accent`, ram 80 %, fyllning 10 %), eftersom accenten är det valda. Rött används inte.
- `Graph/BoxSelection` (utan WPF): rutan mellan två hörn åt vilket håll som helst, regeln *rör ramen* (kanten räknas),
  lägena Ersätt (ingen tangent), Lägg till (Shift) och Vänd (Ctrl, går före Shift), och farten vid kanten. En rörelse
  som rör samma aktörer som den förra ändrar inget och allokerar inget.
- Valet är **samma** som Ctrl+klick (`ChecksViewModel.Picked`), så Delete och Z fungerar direkt. Det bärs nu av
  `Graph/GraphSelection`, en ordnad mängd, så att ytan frågar `IsPicked` utan kostnad för varje nod den ritar (förut
  `List.Contains` per nod och bild). Namnet `Picked` står kvar för att hålla ändringen liten.
- **Eget beslut:** ett vanligt klick (utan Ctrl) släpper valet, också på en nod, som i Utforskaren. Förut låg
  Ctrl+klickens val kvar tills Esc.
- **Flytta flera:** en vald aktör som dras tar med de andra valda (`GraphViewModel.Selection.cs`), och platserna sparas
  i **en** skrivning (`SavePositionsAsync`). Knutna kommentarer följer med på den fria ytan, som vid sorteringen (en
  ensam nod som dras tar inte med sin kommentar; bara tråden följer, som förut). Släpps gruppen på soptunnan läggs
  den tillbaka och hela valet slängs.
- Rutan rullar ytan vid kanten (28 px, upp till 900 px/s), också när animeringar är avstängda, eftersom det är en
  funktion och inte en effekt.
- Träffprovet går mot alla placerade noder med en ren rektangeltest, inte bara de synliga, så att rutan också når det
  som rullat in under kantrullningen; kostnaden är densamma som `VisibleNodes`, som också går igenom alla.
- Tester: `BoxSelectionTests` (13) i App.Tests. Tracing är orört.
- Återstår: Shift+klick på en nod väljer inget intervall (det finns ingen ordning att välja intervall i).

## Nytt ärende NC2a: Ethereums saldo, kedjans hash, cachen och sanktionslistans Ethereum (2026-11-08)

- **Kedjans hash vid höjden:** `IBitcoinSource.GetTipAsync` och `IEthereumSource.GetTipAsync` ger `ChainTip` (höjd,
  hash, tid). Bitcoin frågar `/blocks/tip/hash` (alltid på nytt) och läser höjd och tid ur blocket självt
  (`GetBlockAsync`, kontrollerat mot huvudet; blocket ändras aldrig, så det hämtas en gång per nytt toppblock).
  Ethereum frågar Blockscouts `v2/blocks?type=block` och tar höjd, hash och tid ur samma post, så att höjden och hashen
  alltid hör ihop. Raden visar kedjans höjd som förut; tipset har kedjans hash under *Hämtat vid block N* och tx:ens
  egen hash under *I block M*.
- **Egna val:** Ethereums topp är Blockscouts indexerade topp (kan ligga ett block efter `eth_block_number`), eftersom
  noden kan ha ett block som indexet inte hunnit läsa och ett uppslag av hashen då skulle misslyckas. Bitcoins block
  läggs nu i registret med sin höjd (som Ethereums block redan gjorde): ett block på höjd N säger att kedjan nått N.
  De nya metoderna har standardimplementationer i kontrakten (svarar *Misslyckad*, skickar inget), så att andra
  modulers testfejk inte behövde ändras.
- **Ethereums saldo:** `IEthereumSource.GetBalanceAsync` ger `EthereumBalance` ur `balance` (wei) och `tokenlist`
  (ERC-20 med decimaler och saldo över noll; NFT och token utan decimaler räknas som överhoppade, USDT och USDC känns
  igen på kontraktet och står först). Båda svaren förseglas; ett saldo hämtas alltid på nytt och kommer ur ärendet
  offline. Går tokens inte att läsa ges ether ändå, med `IsComplete = false` och en anteckning. Raden i Nytt ärende
  visar ETH (fyra decimaler, `≈` när fler skars bort, `< 0,0001` för damm) och USDT/USDC; andra tokens visas inte,
  eftersom vem som helst kan skicka en token till en adress (princip 7).
- **Blockscouts takt:** den öppna instansen svarar `Too many requests …` med status 0, vilket tidigare räknades som
  ett fel. Nu väntar Chainsaw på det som på `Max rate limit reached`.
- **Det förhandshämtade som cache:** `IResponseTransfer.ReadIndex` och `Seal(…, SealedResponseIndex)` för med
  registret (höjden och tx:erna i svaret) när ett råsvar förseglas igen. `StartPointPrefetch.SealInto` gör det för
  varje svar som besvarade ett uppslag i minnet; bakgrundsbevakningen för fortfarande in sina svar som bara bevis.
  Registret är det källan själv räknade fram ur svaret i samma process, och uppslagen hittar ett svar bara när svaret
  självt har rätt kedja, sort och ämne. Toppen hämtas nu **först** för varje startpunkt, så att en tx som hämtas efter
  den vet sitt djup: en tx djupt i kedjan tas sedan ur ärendet vid grafens första expansion. Det som kan ha ändrats
  (adressens första sida, saldot, vem som spenderar) hämtas som förut.
- **Sanktionslistans Ethereum-adresser:** `ISanctionLists.Find(EthereumAddress)` och
  `IAttributionService.CheckSanctions(ICase, IReadOnlyCollection<EthereumAddress>)`. Koderna ETH, USDT och USDC
  (`SanctionCurrency.OnEthereum`) räknas; BSC, ARB och ETC är andra kedjor och räknas inte. En ETH-adress med fel
  EIP-55-kontrollsumma räknas som överhoppad; en USDT-adress som inte är en Ethereum-adress (TRON) hoppas bara över.
  `CaseWorkflow` märker nu också Ethereum-startpunkter. Tipset säger *(listad som USDT)* för en token.
- Tester: `TipAndCacheTests` (11) och `EthereumBalanceTests` (16) i Chains, `EthereumSanctionTests` (4) i
  Attribution, nya fall i `StartPointPrefetchTests` (ordningen, Ethereum-raden, saldo som saknas, sanktion, `Holding`,
  förseglingen med register) och `CaseWorkflowTests`.
- Återstår: Ethereums saldo visas bara i Nytt ärende, inte på kortets baksida eller i inspektören.


## Granskningen av NC2a (Ethereums saldo, hashen, cachen) rättad (2026-11-08)

- **M1:** sanktionslistans Ethereum-adresser nycklas på gemener utan kontroll av EIP-55, så att en adress med fel
  kontrollsumma ändå ger träff (ett missat sanktionsfynd är värre än ett falskt); bara text som inte är `0x` och 40
  hex räknas som överhoppad.
- **M3:** finns inte blocket för toppens hash (en endpoint ett block efter), frågas hashen en gång till med sitt block.
- **M4:** `CaseWorkflow` kontrollerar sanktionslistan i ett steg per kedja; felet säger vilken kedja.
- **M5:** `tokenlist` med status 2 ger ett ofullständigt saldo med en anteckning.
- **M6:** tester för en skräptoken som heter USDT, wei över `long.MaxValue` och toppen följd av överföring med
  registret på både Bitcoin och Ethereum. M2 står i `docs/BACKLOG.md`.
