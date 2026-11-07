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
