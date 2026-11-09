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

## Godkänna en plånbok (2026-11-08, etapp, KF2b)

**Byggt:**
- **Clustering:** användarens egna sammanslagningar, `user v1` (`UserMerges`, `ClusterMerge`, `MergeRequest`,
  `MergeOrigin`). Schema 3 har två nya förseglade kedjor: `clustering.merges` (adresserna, tx:en den vilar på,
  förslagets analys och version, vad förslaget sa, valfritt skäl, ursprunget, journalrad och tid) och
  `clustering.merge_undos`. Journalkoderna `clustering.merged` och `clustering.merge-undone`.
  - `Merge`, `UndoMerges`, `GetMergeHistory` och `GetConclusions` finns i en form som tar en `ICaseTransaction` eller
    `ICaseReader`, så att Checks kan försegla beslutet och sammanslagningen **i samma transaktion** (som
    `SealedResponse.Seal`).
  - `ClusterGraph` lägger användarens sammanslagningar **först**, före heuristikerna, så att en heuristik aldrig går
    före användarens beslut. En sammanslagning av användaren bryts av **varje** uppbrytning den skulle korsa
    (*Bryt upp* skriver uppbrytningar med namnet `user`, som nu godtas av `Split`).
  - En sammanslagning som en gällande uppbrytning skulle stoppa vägras när den görs, i stället för att sparas utan
    verkan; hela transaktionen rullas tillbaka.
- **Checks:** `wallet-proposals v1` är ett **förslag** (var en kontroll) utan kandidater. `Decide`/`DecideAll` följer
  ett beslut om en plånbok i Clustering i samma transaktion: godkänt slår ihop förslagets tx:er (en sammanslagning per
  förslag, med sin tx), och ett tillbakatagande ångrar de sammanslagningar beslutet gjorde (`MergeOrigin` pekar på
  `checks.decisions`). `TakeBackMerges` ångrar från historiken genom att ta tillbaka godkännandet, så att panelen och
  klustren alltid säger samma sak.
- **Appen:** inget nytt utseende. Plånboken står i KF2b:s kö med brickorna *Samma ägare troligt* och *Din bedömning*;
  Enter, X, Ctrl+Z och *Ångra* som för andra förslag. `ChecksViewModel.ClustersChanged` läser om grafen efter ett
  godkännande eller tillbakatagande. Historiken (AN1b) har raden *Sammanslagen av användaren* med *Ångra*
  (`HistoryUndo.UndoMerge`, genom `GraphEditor.UndoMergesAsync` → `ICheckService.TakeBackMerges`), och *Klustrad*
  räknar inte användarens länkar två gånger. Kortnamnet är *Egen*.
- Guiden: *En plånbok att godkänna* i *Kontroller och förslag*.

**Egna beslut:**
- Sammanslagningen vilar på förslagets tx (CLAUDE.md: varje sammanslagning sparar heuristik, version och tx), därför
  en sammanslagning per förslag och inte en för hela plånboken.
- Användarens sammanslagning går före heuristikerna: har användaren brutit ut växeln från A och sedan godkänt att
  växeln hör till B, visas heuristikens länk A–B som förslag (*SuggestedAfterSplit*) i stället för att stoppa
  användarens beslut.
- Ett godkännande av adresser som användaren har brutit isär vägras (*ångra uppbrytningen först*). Panelen säger då
  bara det allmänna *kunde inte sparas*.
- Bryts sammanslagningen upp (U) står godkännandet kvar i panelen; uppbrytningen är användarens senare beslut och
  syns i historiken.
- Versionen är kvar på `wallet-proposals v1` (före 1.0 får det ändras fritt); resultat som förseglats som kontroll
  förseglas om som förslag vid nästa körning.

**Tester:** `MergeTests` (15) i Clustering.Tests, `WalletApprovalTests` (14) i Checks.Tests, och i App.Tests sex i
`ChecksViewModelTests`, tre i `InspectorHistoryTests`, två i `GraphEditorTests`, en i `InspectorFormsTests` och en i
`GraphSceneTests`. Facit i `Tracing.Tests/Golden` har schemat 3.

**Efter granskningen (två I och sex M rättade):**
- **I1:** en sammanslagning av användaren vägras (`MergeRefusedException`) både när den korsar en uppbrytning och
  när den, med klustren som de står, skulle få en länk som slår ihop nu att sluta göra det; annars kunde ett
  godkännande i tysthet bryta upp ett kluster som användaren aldrig brutit upp. Att ångra en sammanslagning i
  *Historik* visar först vad en heuristik slår ihop igen (`PreviewUndoMerges`, samma bekräftelse som för en
  uppbrytning), eftersom en tx som analyseras efter godkännandet kan ha hållits tillbaka av det.
- **I2:** ett godkännande slår bara ihop förslag vars adresser alla finns i plånboken som den förseglades och visades,
  och vägras om de inte tillsammans håller ihop hela den. `WalletCheck` visar en godkänd plånbok som *Godkänd plånbok*
  under nyckeln den godkändes med (`CheckInput.ApprovedWallets`), drar av de förslag dess sammanslagningar täcker och
  föreslår det nya runt den som ett eget förslag, med nyckeln efter den minsta adressen som ingen godkänd plånbok har.
- **M1:** panelen säger varför ett godkännande vägrades. **M2:** *Varför?* på ett kluster säger *användarens
  bedömning* när en länk är användarens. **M3:** en godkänd plånbok vars sammanslagning brutits upp heter *Godkänd
  plånbok, uppbruten* och säger det i *Varför?*. **M4:** texten om förslaget står inte kvar efter godkännandet, och
  ytan ritas inte när adresserna står i ett kluster. **M5:** *Ångra* i historiken läser om grafen och KF2b-panelen
  (testat). **M6:** `TakeBackMerges` med en del av ett godkännande tar tillbaka hela och säger hur många som ångrades.

**Återstår:**
- Ctrl+Z och *Ångra* i KF2b-panelen visar ingen bekräftelse av vad som slås ihop igen; det gör bara *Historik*. Det
  behöver en skiss (en bekräftelse i panelen eller i toasten).
- Att slå ihop för hand utan förslag (välja aktörer och *Slå ihop*) är inte skissat; `Merge` tar redan emot det.
- `/testdata` har ingen godkänd plånbok än.


## Kommentarsnodens skrivande och tråd, Guidens panel (2026-11-08)

- **Skrivandet:** Gawks `MarkdownEdits` övertagen till `Chainsaw.SharedKernel/Text/MarkdownEdits.cs` med bara det
  lappen behöver: Enter i en lista eller ett citat fortsätter (nästa nummer; en tom punkt avslutar listan eller går ut
  en nivå), Tab och Shift+Tab drar in och ut (två mellanslag). Till skillnad från Gawk tål reglerna `\r\n`, eftersom
  AvalonEdit skriver Windows radbrytningar; den nya raden får dokumentets egen radbrytning. Varje ändring är ett steg
  för Ctrl+Z. Shift+Tab hanteras alltid, så att tangentbordet aldrig lämnar lappen.
- **Radavståndet** medan man skriver är 1,55 som i den ritade lappen: redigeraren får ett sammansatt typsnitt
  (`FontFamily` med `LineSpacing` 1,55 och baslinjen skalad i samma proportion som en `TextBlock` med `LineHeight`),
  över temats `Font.Ui` och `Font.Mono`. AvalonEdit har inget eget radavstånd. Ska provas på Windows.
- **Hjulet:** redigerarens egen rullning tar varje hjulsnurr; lappen rullar aldrig, så redigeraren släpper snurret
  vidare (`handledEventsToo`) och grafen zoomar.
- **Tråden:** `CommentGeometry.Resolve` läser adressen i nyckeln på alla kedjor (`address:` för Bitcoin, `account:`
  med kedjan för kontokedjorna, `AddressOf`) och letar upp aktören med `TraceGraph.NodeOf(ChainAddress)`. Ligger
  aktören i en ritad hög går tråden till högen. `IsThrown` använder samma uppslag.
- **Guidens panel, länkar:** `GuideRequest.IsChoice` skiljer ett val från ett fylligare svar. Grafen säger själv till
  vid varje val, också när svaren är desamma, så att samma nod vald igen efter en följd länk visar svaret som ett nytt
  steg. Ett svar som kommer senare (klustrets länkar, grafen läst om) uppdaterar bara svaret där det ligger i vägen
  tillbaka, och avsnittet och rubriken står kvar (`GuideHistory.UpdateLatest`), så att det man läser aldrig byts.
  Ett sådant svar om något annat än det som ligger i vägen tillbaka ändrar ingenting (förut blev det ett steg).
- **Tangentbordet:** Shift+F1 i grafen öppnar metodens avsnitt, som högerklick på metoden (Windows tangent för hjälp
  om det som står framför). Metodens knapp tar inte fokus, så tangenten sitter på grafen; verktygstipset och
  *Kortkommandon* säger det.
- **Tolkaren:** en avslutande `*` eller `**` letas efter förbi kod, som texten läses (`Closes`).
- **Smalt fönster:** kommandoraden är högst 640 px och smalnar av med 24 px luft mot fönstrets kant och mot panelen,
  i stället för att ha fast bredd och kapas.
- **Egna beslut:** Shift+F1 som tangent (skissen har ingen); luften kring kommandoraden 24 px.
- Tester: `MarkdownEditsTests` i SharedKernel (7), i App `CommentGeometryTests` (hög, Ethereum, nyckelns adress),
  `GuideTests` (länk och nytt val, fylligare svar behåller avsnittet, stjärna i kod, historiken) och
  `GraphViewModelTests` (valet igen, Shift+F1 i tipset).
- **Återstår:** inget av punkterna; *Historiken* för lappen är kvar i TODO.

## Spara ofta (2026-11-08)

Ett öppet ärende skriver arbetskopian tillbaka till `.cut` av sig självt efter skrivningar, så att en fil som lämnas
vidare är färsk. Förut skrevs `.cut` bara vid sparning och stängning.

- **När:** fem sekunder efter den senaste skrivningen utan nya, och senast en minut efter den första osparade, så att
  ett ärende som skrivs i hela tiden ändå sparas (`CasesOptions.AutosaveQuietPeriod` och `AutosaveMaximumDelay`,
  interna). En skrivning är en `Write` som gått igenom, en rad i journalen och en ny titel; en `Write` som kastar räknas
  inte. Utan nya skrivningar sparas inget.
- **Hur:** `AutosaveSchedule` (Cases, internal) bestämmer bara när, med en timer ur en `TimeProvider`, så att testerna
  flyttar tiden själva. Själva sparningen är den befintliga `SaveUnlocked`, som skriver en ny fil bredvid (`.cut.tmp`)
  och flyttar den på plats i ett steg, så `.cut` blir aldrig halvskriven, och som för in den nya filens hash i basen
  före flytten, så att en krasch mitt i blir en återtagning och aldrig en konflikt.
- **Krockar inte:** timerns tråd väntar aldrig på ärendets grind (`Lock.TryEnter`). Är ärendet upptaget av en annan
  läsning eller skrivning provas det igen en sekund senare, och kommer timern mitt i en operation på samma tråd sparas
  inget då. Ett ärende som håller på att stängas (låsning av valvet, `BeginClosing`) lämnas åt stängningen, som
  sparar själv. Timern startar först när ärendet har öppnats färdigt (`StartAutosave` i `FinishOpening`), så öppningen
  och återtagningen efter en krasch sköter sin egen sparning, och ett ärende som inte gick att öppna sparas aldrig av en
  timer. Timern tas bort när ärendet stängs eller överges.
- **En sparning som misslyckas** (till exempel en fil som ett antivirus håller) lämnar `.cut` orörd och det osparade
  kvar, och provas igen efter en minut; misslyckas den igen fördubblas väntan, högst tio minuter (1, 2, 4, 8, 10).
  Inget undantag lämnar timerns tråd: en låsning som hinner emellan kontrollerna och sparningen avslutar den tyst, och
  allt annat fångas ytterst. Den syns inte i gränssnittet än (TODO *Säga till när sparningen inte går*).
- **Eget beslut:** sparningen journalförs inte, som en sparning för hand inte gör det; den lägger inget till i ärendet,
  den skriver bara det som redan förseglats till filen.
- **Avsteg:** inget gränssnitt. Medan en sparning pågår väntar en annan operation på ärendet på grinden, som vid en
  sparning för hand; `ICase` ska anropas utanför UI-tråden, så gränssnittet låses inte.
- Tester: `AutosaveTests` (17) och `AutosaveScheduleTests` (13) i Cases.Tests, med `FakeTimeProvider` och utan riktiga
  väntetider: sparning efter tystnaden och inte före, en kopia av filen medan ärendet är öppet har det nya, ny skrivning
  flyttar sparningen, senast efter en minut, inget utan skrivningar, alla sorters skrivningar, sparning för hand, ett
  upptaget ärende (läsning på en annan tråd och timern mitt i en skrivning), en misslyckad sparning, stängning,
  låsning av valvet, också mellan kontrollerna och sparningen, krasch före och efter en sparning av sig självt, och
  väntan som växer efter fel i rad.

## Soptunnan och startvyns högar: det som återstod (SO1a, GR8a, 2026-11-08)

- **Hög på hög:** en hög lyfts och dras som ett ärende (samma vridna bild, nu av högen). Släppt på en annan hög läggs
  dess ärenden överst i den, under namnet på högen man släppte på, eller den dragnas när den saknade namn; släppt på
  ett löst ärende tar högen in ärendet. Vad ett släpp betyder står i vymodellen (`CanDrop`, `DropOn`, `MergePiles`);
  vyn frågar bara. En hög som dras får inte soptunnan att växa, eftersom en hel hög inte går att slänga än.
- **Tab:** högarna på ringen tar tangentbordet som ärendena, med accentens ram; Enter och blanksteg fäller ut, F2
  skriver namnet.
- **Grafens ring på startsidan:** `FanOutLayer` lägger korten med `PileFanLayout.Ring` (EX2b) i stället för spiralen,
  och korten är grafens brickor (156 × 40, dokumentet i en streckad fyrkant, numret och *ändrad …*). I ett litet
  fönster dras ringen in (`FitRing`), högst till 70 %.
- **Samma stöt och centrering i grafen:** tider, radier och ease ligger i `Graph/FanShock` (utan WPF, testad), som
  både `FanOutLayer` och grafens yta använder. Grafen ritar de två ringarna från högen och glider ytan så att högen
  står i mitten, och tillbaka när högen fälls ihop. **Avsteg:** inte genom `FanOutLayer`, eftersom grafens brickor
  ritas i en `DrawingContext` med sitt eget drag och val; samma värden i stället för samma kontroll. Ytan bakom trycks
  inte ut (står i TODO). Egna beslut: ytan går bara tillbaka om användaren inte panorerat eller zoomat under tiden, och
  en panorering eller zoom stoppar glidningen där den är.
- **Ett nytt ärende med ett slängt nummer:** soptunnan döljer ett ärende bara som filen var när det slängdes
  (`CaseTrash.Hides`: skrivet senast då). En fil som skrivits efter slängningen tas ut ur soptunnan när mappen listas
  (`ForgetRewritten`), så den står inte heller i soptunnans vy. Eget beslut: ett slängt ärende som öppnas och sparas
  igen kommer också tillbaka, eftersom man arbetar med det.
- **Noden krymper mot soptunnan** som i skissen: till 55 % vid soptunnan och 95 % (75 % när ett släpp slänger) i
  opacitet, mot punkten man håller i, så att den stannar under pekaren. En dragen grupp krymper tillsammans
  (`TrashBinViewModel.DraggedScale`, `DraggedOpacity`).
- **Bevakningsträff in i soptunnan:** `GraphWatches` ger `WatchTrashHit` när en träffs motpart inte finns i grafen för
  att den ligger i soptunnan (`TrashFilter.HidesAddress`). Den ritas vid porten som en kort hel linje i accenten med
  beloppet (bara när träffen hade en motpart, som en vanlig träff), ringarna tills den är sedd och *i soptunnan* i
  änden, och tar stumpens plats på den sidan.
- **Test:** grafen med bara cykler och utan startpunkter (`TrashTreeTests`, två fall).
- Tester: 2 i Tracing.Tests, 22 nya i App.Tests (`CasePileTests`, `StartTrashTests`, `TrashHitTests`,
  `FanShockTests`).

## Små rättelser mot 1.0: Källor, Bevakningens huvud, offline, `chainsaw:` och tunneln (2026-11-08)

- **Källors felrad till toastern:** `SourcesViewModel.ErrorText` sägs i toastern när den kommer, som Bevakningens
  `Problem`, och raden på sidan Källor är borttagen. Samma fel två gånger i rad sägs en gång.
- **Bevakningens huvud (BV1a):** `monitoring_seen` fick kolumnen `seen_utc` (migrering 3, `ALTER TABLE`, så att äldre
  ärenden öppnas). `MarkSeen` skriver tiden när en nyare träff ses, och `IMonitoringService.GetLastSeen` ger den
  senaste. Huvudet säger *2 nya träffar sedan 09:10* med `WatchWords.When` (klockslag i dag, annars datum). Tiden är
  användarens arbete i ärendet, inte en fakta- eller bedömningsrad, och förseglas inte, som `monitoring_seen` förut.
- **En regel för offline:** `OfflineWords` (text, orsak, *förrän …*, tips och väntetext) och `ExitWay.Offline`, som
  väljer mellan ingen utgång, Windows utan nät och tunneln som ansluts igen. Titelraden, grafens hörn och notiser,
  tomma grafens fot och startpunkt, Nytt ärendes tagg och rader, och Bevakningens huvud och rader läser den. Grindens
  `MonitoringGate` säger nu `NoExit` också när Windows är utgången och datorn inte når internet, så att bevakningen
  inte frågar förgäves.
- **Länken `chainsaw:`:** `Links/LinkRegistration` (utan Windows) med `ILinkRegistry`, och `Interop/
  CurrentUserLinkRegistry` som skriver `HKCU\Software\Classes\chainsaw` (`URL Protocol`, `DefaultIcon`,
  `shell\open\command` = `"Chainsaw.exe" "%1"`). Raden under Länkar är en strömbrytare. Eget beslut: en länk som pekar
  på en annan kopia av Chainsaw visas som av med en rad som säger det, och strömbrytaren tar över den.
- **Återanslutning av tunneln:** `ExitReconnector` i Network (`IExitReconnect` i Contracts). Den börjar bara när en
  ansluten tunnel faller (`ConnectionLost`), väntar 2, 5, 15 och 30 s och sedan en minut åt gången, försöker direkt
  när nätet kommer tillbaka (`INetworkPresence`), och slutar när utgången är ansluten, när felet inte går över med
  tiden (konfigurationen vägrad, tunneln saknas), när användaren ansluter eller kopplar från, eller när ett försök
  avbryts. Före varje försök läses utgångens läge, så att ett sent event aldrig gör ett försök som ingen vill ha. Ett
  låst valv väntas ut. Titelraden säger *Ansluter igen …* och verktygstipset nästa försöks klockslag.
- Tester: `ExitReconnectorTests` (15, utan nät, med `FakeTimeProvider` och den riktiga `ExitConnection`),
  `MonitoringGateTests` (+1), `MonitoringServiceTests` (+1), `OfflineWordsTests` (9), `WatchHeadTests` (2),
  `LinkRegistrationTests` (6) och `SourcesTests` (+1).
- Återstår: utgångsrutan (UT2b) visar bara *Offline* medan tunneln ansluts igen (TODO). Väntetiderna går inte att
  ställa.

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

## Grafens rester (2026-11-08, GR4, GR5, GR6c, TK1a, KF1b och KF2b)

Alla punkter under *Grafen* i `docs/TODO.md`, *2. Resterna i det som är byggt*, byggda direkt mot skisserna.

- **Nodens bredd (GR5):** `Graph/NodeWidths` räknar bredden ur namnets tecken (7,4 px per tecken som skissen), från
  132 px upp till 214 px (skissens *Misstänktes plånbok*). `NodeView.Width`, `GraphScene.FrameOf` och
  `GraphViewModel.FrameAt` bär bredden till träffprovet, linjerna, beloppen, markeringarna, områdena, rutvalet,
  tidsbanden, bevakningens portar, soptunnans stumpar och kommentarstrådarna. Hoppkolumnerna flyttar nästa kolumn med
  den bredaste noden (`HopLayout.ColumnGap`), så att luckan för beloppen står kvar.
- **Eget beslut:** bara ett namn breddar noden. En adress (under namnet eller som titel) förkortas i mitten som förut,
  eftersom en graf av adresser utan namn annars blev 214 px bred överallt; skissen GR5 förkortar också adresserna.
- **Linjer genom noder:** `Graph/LineRouting` böjer en linje i hoppkolumnerna som skulle gå genom en annan nod, med så
  liten böj som räcker (en kvarts rad i taget, upp till två rader åt vardera hållet), in i luckan mellan raderna.
  Ändarna står kvar vid portarna. Valt framför att flytta aktörer mellan kolumnerna, eftersom tidsordningen nedåt i
  kolumnen då står kvar.
- **Tokenmärket (TK1a):** en rund bricka på 14 px (`Brush.SurfaceHover`, kant `Brush.BorderStrong`, tecknet i
  `Brush.Text`) först i beloppet; beloppet behåller linjens färg.
- **Swappen (TK1a):** ett piller mitt på linjen med två brickor och en pil (`Icon.ArrowRight`), som i skissen.
  `AmountLayout.SwapBox` ger dess plats, och beloppen läggs aldrig på det. Gallringen döljer aldrig pillret.
  `Brush.Graph.SwapPill` används nu bara av Guidens figur.
- **Kedjornas rubriker (TK1a):** `Graph/ChainSections` ger varje kedjas del av ytan när grafen har aktörer på mer än en
  kedja, med kedjans namn i versaler i `Brush.TextFaint` 34 px ovanför delens översta aktör. Ytan delas inte upp av
  sig själv: rubriken står över där kedjans aktörer står.
- **Dolda OP_RETURN:** hörnraden har en markering (överstruket öga med prick) så länge grafen har sådana utgångar, med
  antalet i verktygstipset; ett klick visar dem (öppet öga, tänt) och döljer dem igen. Ingen skiss för markeringen;
  formen följer hörnradens andra ikoner med prick.
- **GR6c, tredje läget:** `Graph/StartGuide` och `GraphCanvas.Reach.cs` ritar *Expandera* bredvid en ensam startpunkt
  som inte är expanderad och saknar linjer, med accentens sonar (ringarna står still utan animeringar), en prickad
  linje, raden under och *Hitta uttaget* svagt efter. Ett klick på *Expandera* väljer och expanderar startpunkten.
  *Passa in* tar med guiden. Offline säger raden det ärligt i stället för att något blir grått (avsteget i TODO).
- **Förmågor utan synlig väg:** dubbelklick på en nod expanderar (`GraphViewModel.ExpandAt`). Högerklick på tom yta
  öppnar en meny i samma form som nodens: *Kommentar … N* (där man klickade), *Passa in F*, *Zooma in +* och *Zooma ut
  −* (`ViewMove`). Det ger zoomen och *Passa in* en synlig väg utan ny krom i hörnet.
- **Egen yta (KF1b):** formuläret står bredvid aktörerna det gäller (`ChecksViewModel.FormActors`), till höger eller
  annars till vänster, och följer med vid panorering och zoom. Utan placerade aktörer står det under hörnet som förut.
- **Kandidaternas nummer (KF2b):** numret står i en skiva på 20 px på kandidatens nod uppe till vänster, ifylld i
  accenten för den valda och med `Brush.TextMuted` som kant annars. **Avsteg:** skissen sätter den godkändas gröna bock
  i övre högra hörnet; här står den där efter nodens andra markeringar, så att den inte täcker dem.
- **Esc i Guidens panel:** `MainWindow.CancelGraphForm` stänger ett öppet formulär i grafen (Etikett, Bryt upp, Annan
  kedja, historikens formulär, Bevaka och egen yta) före panelen, var tangentbordet än står. Nästa Esc stänger panelen.
- Tester: `GraphReachTests` (14) i App.Tests; `TestCaseLayoutTests` mäter nu nodernas verkliga bredd.
- **Återstår:** *Sortera grafen* (`TidyLayout`) räknar med 132 px breda noder, och en ny kommentar vid en bred nod
  (`CommentGeometry.PlaceOfNew`) placeras efter samma bredd. Båda står i `docs/TODO.md`.

## Begäran, BG1b (2026-11-08)

- **Modulen Requests:** `IRequestService` (Compose, Export, ListExports, ContactsFor) med mallar på svenska och
  engelska (`RequestTexts`, `RequestTemplates`). Dokumentet är block (`RequestBlock`: titel, rubrik, stycke, notering,
  tabell), och både förhandsvisningen och filen görs av samma block, så att det som syns är det som exporteras.
  Markdown skrivs av `RequestMarkdown`, som escapar det en etikett kunde göra till markup.
- **Bara det begäran behöver** (princip 4): mottagaren, adresserna, de valda insättningarna (tid lokal med offset och
  UTC till sekunden, exakt belopp, adress, hela txid, blockhöjd och blockhash), spåret i en mening om det tas med,
  ärendenumret och operatörsprofilen. Aldrig ärendets titel, offrets namn eller anteckningar.
- **Spårets steg** är förkortade adresser och bara slutet heter med sitt namn (`RequestExits`), så att ett offers
  eller en misstänkts namn aldrig kan hamna i en begäran. Skissen skriver *Kluster 7*; eget beslut av sekretesskäl.
- **Export:** `RequestFiles.WriteNew` skriver med `FileMode.CreateNew` och tar nästa nummer, `(2)`, `(3)`, när namnet
  är taget (spara-dialogen frågar aldrig om att skriva över). SHA-256 av filens byte journalförs
  (`requests.exported`) och förseglas i kedjan `requests.exports` i samma transaktion; misslyckas det tas filen bort
  igen, så att ingen fil finns som ärendet inte känner till.
- **Kontaktvägar:** katalogen (`ExchangeContacts.Catalogue`) är tom. Inga börsers sidor för myndigheter gick att
  hämta och kontrollera från molnmiljön, och ingen väg läggs in ur minnet. Vyn säger ärligt att ingen verifierad
  kontaktväg finns.
- **Frysning** (TK1a): en egen sorts begäran till utgivaren (Tether, Circle) med token, kontrakt och det som ligger
  kvar enligt ärendets historik, sagt som en bedömning. Den nås med inspektörens *Begäran om frysning …* och står
  som en isblå rad efter uttagen i listan (ett tillägg till skissen).
- **Vyn** (`RequestsView`, `RequestsViewModel`, `Controls/RequestPreview`) följer grafens scen: uttag är de blå
  sorterna som en spårad linje når, också aktörer som ligger i en hög. Det slängda (soptunnan) är inte med.
  Insättningarna är fakta ur grafens linjer; spårad är en insättning vars mynt spåret håller (Bitcoin) eller vars
  linje bär spårade pengar (Ethereum, som spåras per linje).
- **Avsteg från skissen:** språkknappen *Svenska/English* bredvid Exportera (skissen har ett språk); raden under
  förhandsvisningen om kontaktvägen; förhandsvisningen rullar när dokumentet är längre än rutan; tider skrivs ISO
  (`2026-10-28 21:14:05 +01:00`) i stället för *28 okt 2026 21:14*, så att en börs kan söka i sina loggar.
  Grafens menyrad *Begäran …* har ingen tangent, eftersom R är underleverantörernas.
- **Inte byggt:** börsens markering i grafens hörn när en begäran är exporterad (grafen byggdes om samtidigt av en
  annan agent), se `docs/TODO.md`.
- Tester: 35 i `Chainsaw.Modules.Requests.Tests` (mallar, Markdown, export utan överskrivning, försegling, journal,
  verifiering, kontaktvägar) och `RequestsTests` i App.Tests (uttagen ur scenen, spåret utan namn, vymodellen,
  exporten, pillret).
- **Efter granskningen (två I, åtta M):**
  - **I1:** en begäran gäller bara de adresser spårade pengar nått i uttaget (`RequestExits.Reached`), och de
    insättningar som gick dit, också ospårade till samma adress. Klustrets andra adresser och andra kunders
    insättningar kommer aldrig med; `Draft()` bygger mottagarens adresser av de valda insättningarna.
  - **I2:** ett namn i en begäran kommer bara ur den etikett som gjorde aktören till ett uttag (`SafeName`), annars
    den förkortade adressen. Detsamma gäller spårets slut i en frysning, så ett offers namn kan inte nå utgivaren.
  - **M1–M9:** en transaktionsnod (CoinJoin) är varken steg eller hopp; radens nyckel är kind och adress, och
    *Exporterad* matchar alla aktörens adresser; en frysning per token, och exporten förseglas under adress och token
    (`RequestDraft.ExportKey`); `~`, `&` och radinledande `-`, `+` och `1.` escapas och noter trimmas; en fil som
    inte kunde skrivas klart tas bort; exporten skriver *upprättad* med förhandsvisningens tid (`MadeUtc`); knappen
    *Begäran om frysning …* syns bara när det finns en frysning; begäran säger att mottagarens sort och spåret är
    bedömningar. `RequestExits.Find` cachas per scen (M8); `Compose` körs fortfarande på UI-tråden
    (`docs/BACKLOG.md`).


## Granskningen av NC2a (Ethereums saldo, hashen, cachen) rättad (2026-11-08)

- **M1:** sanktionslistans Ethereum-adresser nycklas på gemener utan kontroll av EIP-55, så att en adress med fel
  kontrollsumma ändå ger träff (ett missat sanktionsfynd är värre än ett falskt); bara text som inte är `0x` och 40
  hex räknas som överhoppad.
- **M3:** finns inte blocket för toppens hash (en endpoint ett block efter), frågas hashen en gång till med sitt block.
- **M4:** `CaseWorkflow` kontrollerar sanktionslistan i ett steg per kedja; felet säger vilken kedja.
- **M5:** `tokenlist` med status 2 ger ett ofullständigt saldo med en anteckning.
- **M6:** tester för en skräptoken som heter USDT, wei över `long.MaxValue` och toppen följd av överföring med
  registret på både Bitcoin och Ethereum. M2 står i `docs/BACKLOG.md`.

## Det som inte gick att slänga och kommentarsnodens historik (2026-11-08, SO1a, GR8a, KN1a och AN1b)

- **Kommentarer i soptunnan:** Delete, ett drag mot soptunnan och en lapp som töms slänger den i grafens soptunna
  (`IGraphTrash`) med nyckeln `comment:<id>` och sorten `Comment`, i stället för den förseglade borttagningen. Lappen
  döljs bara (`CommentNote.IsInTrash`, också tråden), och *Lägg tillbaka* ger den tillbaka med den text som är
  sparad. `CommentsViewModel.ThrowAsync` gör kastet i lappens skrivkö och säger det med `Thrown`, som grafen visar med
  toast och Ctrl+Z. En lapp som töms medan ärendet stängs slängs innan det stängs. `IGraphComments.RemoveAsync` är
  borta ur appen; modulens `RemoveComment` och raden *Borttagen* i historiken finns kvar för äldre ärenden.
- **Egna ytor i soptunnan:** Delete med en egen yta vald (dess bricka) slänger den med nyckeln `own:<id>` och sorten
  `Area`; `GraphAreas` hoppar över en slängd yta, aktörerna står kvar. **Eget beslut:** krysset på brickan tar
  fortfarande bort ytan förseglat med skäl (KF1b); soptunnan är det andra sättet, som kan läggas tillbaka.
- **Soptunnans vy och räkningen:** `TrashRowViewModel.LookOf` ritar en lapp och en yta i egen form (`Icon.Note`,
  `Icon.Area`), och `TrashViewModel.CountText(members)` säger *1 kommentar*, *2 ytor*, *3 aktörer och 1 yta*, så att
  en lapp aldrig kallas aktör. *Dolda i grafen* räknar med lappar och ytor.
- **Historiken i modulen:** `IGraphTrash.GetHistory(case, key)` läser varje kast och tillbakaläggning av en nyckel,
  också de som lagts tillbaka för länge sedan (`TrashRecord`). Inget nytt schema; posterna fanns redan förseglade.
- **Kommentarens inspektör och historik (AN1b):** en vald lagrad lapp visas i inspektören (`InspectorContent.ForComment`:
  första raden som rubrik, *Kommentar* under, vem och när, tråden), utan *Varför?* (`HasWhy`). En vald nod och en vald
  lapp utesluter varandra. Fliken *Historik* (`InspectorHistoryViewModel.FollowComment`, `CommentHistory.Build`) visar
  *Kommentar skriven*, *Texten ändrad*, *Tråd till …*, *Tråden lossad*, *Slängd i soptunnan* och *Lagd tillbaka ur
  soptunnan* i tidsordning. **Eget beslut:** raderna har inga knappar; en lapp läggs tillbaka från soptunnans vy, där
  den ligger, och att ångra en textändring har ingen skiss.
- **Startvyn:** en lyft hög får soptunnan att växa och slängs hel (`CaseTrash.ThrowPile`, posterna bär högens id och
  namn i `trash.json`); högen själv står kvar i `piles.json` och kommer tillbaka hel. Kort i en utfälld hög slängs med
  drag mot soptunnan eller Shift-klick och Delete; de stannar i högen, dolda, och kommer tillbaka dit. Soptunnans vy
  visar en slängd hög som ett träd under *Ärenden från startsidan* med *Lägg tillbaka högen*. **Avsteg:** SO1a har ingen
  ruta för högar; formen följer grafens träd i samma vy.
- Tester: `GraphThrowMoreTests`, `CommentInspectorTests` och `StartPileTrashTests` (33) i App.Tests, två nya i
  Tracing.Tests (`TrashTests`), och de befintliga kommentarstesterna följer slängningen.
- **Återstår:** testärendet saknar en slängd lapp och yta (`docs/BACKLOG.md`, *Testärendet*).


## Sökningen i kommandoraden, KR1a (2026-11-08)

- **Byggt:** `Search/` i appen: `SearchIndex` byggs en gång per graf utanför UI-tråden ur grafens etiketter
  (en post per namn), adresser, tx (på linjerna och de hämtade utan linje) och kluster (som också hittas på sina
  adresser), och Guidens avsnitt (titel, id och rubriker). En sökning är en genomgång av indexet utan att röra
  ärendet; en nyare sökning avbryter den gamla. Rangordningen: hel träff, början, ordets början, inuti (bara från
  tre tecken), sedan sorten (etikett, kluster, adress, tx, Guide) och om noden finns i grafen.
- `CommandSearchViewModel` håller listan, valet, Tab och det okända värdet; `GraphViewModel.ShowFound` och
  `GraphCanvas.Search` glider och ritar ringen. `/guide <begrepp>` är ett kommando (`GuideCommand`) och samtidigt
  ett läge i sökningen. `GuidePanelViewModel.OpenSection` öppnar panelen vid ett avsnitt.
- **Egna beslut:** mörkningen bakom kommandoraden är borttagen och raden står nära överkanten, som i skissen, så att
  grafen syns under; noden landar på 78 % av höjden. Zoomen behålls, utom när den är för liten för att läsa noden.
  En nod i en hög visas vid högen. En rad som inte finns i grafen gör ingenting med grafen, och Enter stänger bara.
  En Guide-rad öppnar panelen när den väljs och fäller in den igen om man väljer något annat, men bara om sökningen
  öppnade den. Tab fäller ut högst 100 rader, så att listan förblir lätt. Etikettens rad säger *N adresser* eller
  *N kluster* efter etikettens ämnen; ett annat ärendes nummer finns inte i ursprunget, så källan heter *ett annat
  ärende*.
- **Avsteg:** skissens rad *Kraken · etikett från 2026-0098* kan inte skriva ärendenumret (se ovan). Det slängda i
  soptunnan söks inte. Raden för en okänd adress säger ärligt att inget hämtas förrän noden expanderas, eftersom
  Enter bara lägger till startpunkten.
- Tester: 26 i `CommandSearchTests` (träffkvalitet och klippning, rangordning och grupper, adress, kluster, tx i och
  utanför grafen, Guiden och `/guide`, korta ord, avbrott, 200 000 poster, vymodellens val, pilar, Tab, okänd och
  känd adress, utan ärende, ny graf, klick, Enter i skalet).


## Inställningarna mot 1.0: BV4a, ST2a, FS2a och UT3b (2026-11-08)

- **BV4a:** de tre sätten är rader under Låsning med ikon på en bricka. Kortet mitt på skärmen är ett eget fönster
  (`AlertCardWindow`), överst och utan mörkning. Bakgrundsprocessen är inte WPF, så den startar `Chainsaw.exe
  --watch-alert`, som bara visar kortet (inget valv, ingen värd, ingen spärr för en enda instans). Utan Chainsaws
  program bredvid visas Windows ruta som förut. Kortet vet inte om Chainsaw är låst, bara om det är igång, och säger
  därför *Detaljerna står i Bevakning, efter upplåsning* med *Visa Chainsaw*. Raden *Bevaka när Chainsaw är stängt*
  står kvar ovanför sätten (skissen har den inte, men valet finns). *Meddelande mitt på skärmen* är av från början,
  eftersom det täcker arbetet (skissen har alla tre på); *Prova* och *Lyssna* finns, utan skissens *Prova*-lägen.
- **ST2a:** `ICaseStore.RevokedRecoveryKeys` läser de återkallade ur Cases nyckelfil. *Om Chainsaw* är en egen vy
  (`AboutPage`, `AboutViewModel`) som både inställningarna och låsskärmen visar; låsskärmens version saknar menyn.
- **FS2a:** Windows är förvalt och väljs när steget lämnas eller ett kort väljs (genom inställningarna, så det sparas).
  Steget stannar kvar när filen är tillagd, med strömknappen; *Senare* är borttaget, eftersom *Fortsätt utan utgång*
  säger samma sak.
- **UT3b:** `IExitStore.Describe` byter namn och plats, `IExitCheck` frågar `https://1.1.1.1/cdn-cgi/trace` genom
  `IExitHttp` och sparar det senaste svaret per utgång i valvet (`network/exit-sightings/<id>`), aldrig i ärendet; det
  tas bort med utgången. Platsen är *land · stad*, med landet ur en lista (`ExitPlace`). Bara en ansluten utgång kan
  kontrolleras. *Kontrollera vid anslutning* (`AppSettings.CheckExitOnConnect`, på) frågar en gång per anslutning, och
  fel land säger toastern.
- **Kvar:** se *Inställningarnas rester* i `docs/TODO.md`.

## Etiketter, ET1a (2026-11-08)

- **Byggt:** vyn Etiketter i pillret (Ctrl+7, inte längre nedtonad): källorna som filter med antal och en rad om vad
  källan är nu, bibliotekets antal (Hela biblioteket, Kom in, Gick ut, Sprids inte), växeln Ärendet/Biblioteket,
  filterfältet, en virtualiserad lista med sanktionsträffarna i grafen överst i rött, och den valda etiketten med
  ursprungen, *Visa i grafen* (G), *Sprid inte*, *Ta tillbaka …* med frivilligt skäl och *Varför?* (Guiden om
  etiketter). *Nytt → Etikett* (E) visar grafen och öppnar etikettformuläret på den valda noden.
- **Modulen:** `IAttributionService.SetDoNotSpread` förseglar en egen post (`spread`) med journalrad
  (`attribution.label-spread-changed`); den senaste posten gäller och etikettens egen post rörs aldrig.
  `GetHistory(ICase)` läser alla etiketter med tillbakatagandet. `IGraphEditor.SetDoNotSpreadAsync` låter biblioteket
  följa direkt, som `RetractLabelAsync` (gemensam `FollowLibrary`).
- **Egna beslut:** samma etikett (subjekt, sort, namn) från flera ursprung är en rad, som i grafen; en rad listas
  under källan för det första ursprunget som inte är en kopia. Ett filter döljer raden när dess källa är av, och en
  rad med en kopia från ett annat ärende också när *Andra ärenden* är av (som skissen). *Sprid inte* och *Ta tillbaka*
  gäller alla radens etiketter som användaren får ändra. *Gick ut* räknas som bibliotekets etiketter med ärendets
  nummer. Tider skrivs `yyyy-MM-dd HH:mm (HH:mm UTC)` som i Begäran.
- **Avsteg från skissen:** *Ångra* i toasten efter *Ta tillbaka* är inte byggt (se `docs/BACKLOG.md`).
- Tester: 6 i `Chainsaw.Modules.Attribution.Tests` (`SpreadTests`), 16 i `LabelsTests`, en i `GraphEditorTests` och
  en för *Nytt → Etikett* i `SubmenuTests`.
- **Granskningen (två I, tio M), rättad:**
  - **I1:** biblioteket minns ursprung som hålls tillbaka med *Sprid inte* (`HeldBack`, bredvid `Withdrawn`). När
    ärendet där etiketten sattes absorberas läggs ursprunget dit och posten tas bort; `ShareInto` och `Absorb` hoppar
    över det, också när det kommer genom en kopia i ett annat ärende. Kopior som redan finns rörs inte. Ursprunget
    släpps när etiketten får spridas igen. Testat med tre ärenden.
  - **I2:** raderna byggs utanför UI-tråden när läsningen eller grafen ändras; filterfältet, källorna och växeln
    filtrerar bara de färdiga raderna. Grafens aktörer slås upp i en `Dictionary` per scen (en `ConditionalWeakTable`).
  - **M1:** en etikett på samma aktör eller adress som ett offer säger *Nära ett offer i grafen: når aldrig
    biblioteket*, och brytaren går inte att ändra. **M2:** grafen och vyn läses om också när en försegling
    misslyckas. **M3:** toasten säger att kopian från det andra ärendet står kvar. **M4:** en regel för *Sprids inte*
    i märket, brickan och brytaren. **M5:** en sanktionsträff på en aktör i en hög eller i soptunnan står också överst,
    med en liten markering (högen eller soptunnan); *Visa i grafen* väljer högen. **M8:** tester för Ethereum och för
    att kopior, sanktionsträffar, analyser och Chainabuse inte kan märkas. **M9:** piltangenterna rullar valet in i
    bild. **M10:** ett fel när ärendet läses sägs i listan i stället för en tom vy.
  - **M6, eget beslut:** en sanktionsträff som kommit som kopia från ett annat ärende står under *Andra ärenden*, inte
    överst. Överst står bara träffar som ärendets egen kontroll mot listan gjort på grafens adresser; körs kontrollen
    här får adressen sin egen träff.
  - **M7:** posten `spread` läses inte av en äldre version av Chainsaw, som då visar etiketten som den sattes. Före 1.0
    får schemat ändras fritt, så ingen migrering görs.

## Allt följer med (2026-11-08, CLAUDE.md *Etiketter smittar*)

- **Byggt:** en etikett som biblioteket ger ett annat ärende bär nu med sig skälet, anteckningen, hela
  operatörsprofilen och numret på ärendet där den sattes (`LabelOrigin.CaseNumber`). Biblioteket minns det ärendet
  (`OriginCase`) också när etiketten kom in genom en kollegas kopia, och journalraden för etiketterna nämner numret.
- **Lagringen:** numret förseglas i en egen kedja, `attribution.label_cases` (schemasteg 3), med kopians sekvens, så att
  etiketter som redan är förseglade behåller sina hashar. En kopia som kom in före detta saknar nummer och visar *Från
  ett annat ärende*.
- Vyn Etiketter skriver *Från ärende 2026-0098 · ett annat ärende*; *Varför?* skriver *Från ärende …*.
- Offrets etiketter och etiketter nära ett offer sprids fortfarande aldrig, och *Sprid inte* gäller som förut.
- Guiden (*Etikettbiblioteket*) och `docs/BACKLOG.md` är rättade; testerna som sa att skäl, anteckning och nummer aldrig
  följer med är omskrivna till motsatsen.

### Granskningen av inställningarna (BV4a, ST2a, FS2a, UT3b) rättad (2026-11-08)

- **M1:** `IExitStore.DescribeAsync` och sparandet av en kontroll (`ExitCheck`) går under samma lås som
  `RemoveAsync` (`ExitConnection.ChangeExitsAsync`), så att en borttagen utgång aldrig skrivs tillbaka.
- **M2:** kontrollen vid anslutning hoppas över när valvet är låst.
- **M3:** steget Utgång förväljer det val som inställningsfilen redan har (`AppSettingsStore.SavedRoute`), annars
  Windows.
- **M4:** `FirstRunViewModel.Later()` är borttagen; *Fortsätt* gör samma sak.
- **M5:** kortet mitt på skärmen (`Chainsaw.exe --watch-alert`) kan startas av vilken process som helst hos
  användaren, inte bara av bakgrundsprocessen. Det är godtaget: kortet säger bara att en bevakning har slagit till,
  visar inget ur ärendet eller valvet och kan som mest störa användaren med ett meddelande.
- **M6:** tester för kontrollen mot den riktiga `ExitHttp` utan ansluten utgång (inget skickas), för Windows vid
  anslutning och för att förhandsvisningens kort stängs när allt stängs vid låsning.

## Kurserna, KU1a (2026-11-08)

Kronan följer Riksbanken (användarens beslut 2026-11-08): Bitstamps kurs i euro räknas om med Riksbankens dagskurs för
EUR/SEK, och båda svaren förseglas i ärendet var för sig.

- **Pricing:** `PricingOptions.KronaVia` är nu euro (var dollar). Riksbankens öppna API (SWEA) nåddes härifrån och
  svarar som källan redan läser: `GET /swea/v1/Observations/SEKEURPMI/{från}/{till}` ger en lista med `date` och
  `value`, och `Series/SEKEURPMI` beskriver serien (*Euroland euro*, sedan 1993). Inget nät i testerna; ett syntetiskt
  svar `riksbank-sekeurpmi.json`.
- `RateOrigin.Route` läser den förseglade kolumnen `exit_route`, så att *Varför?* kan säga genom vilken sorts utgång
  kursen hämtades. `IPriceService.Summarize` räknar kurserna i ärendet (`RateSummary`) utan att skicka något.
- **Grafen:** alla linjer värderas, inte bara Ethereums (`EdgeValues`, `EdgeValuePart`): varje överföring vid sin
  blocktid, och en linje får ett värde bara när varje överföring har en kurs. Annars får den `EdgeValueGap`:
  **– kr** med en ikon (överstruket nät för offline, klocka för inte hämtad, inte satt eller obekräftad) och på hovring
  vilken kurs som saknas. En tillgång som ingen källa prissätter får inget under beloppet.
- **Varför? (Guidens panel):** ett klick på beloppet väljer linjen och öppnar `GuideRequest.ForValue` med
  `GuideValuation`: per överföring *Beloppet* (fakta: tx och utgång, block med hash, blocktid i UTC och lokal tid),
  *Kursen BTC → EUR* (perioden, *Timmens stängning*, hämtad och genom vilken utgång, SHA-256 i grupper med kopiera),
  *Växelkursen EUR → SEK* (dagen, serien, hämtad, SHA-256) och uträkningen; en linje med flera överföringar slutar
  med summan. Avsnittet *Kurser och omräkning* öppnas under, vid *Så ser stegen ut*. Inspektörens *Värde* och
  *Varför?* gäller nu också Bitcoin.
- **Inställningar › Kurser:** raderna Valuta, Upplösning (*Dygn* i stället för *Dag*), Källa för kryptokurser, Källa
  för växelkursen och *Sparas i ärendet* (`RatesSummaryViewModel`, räknat utanför UI-tråden när sidan visas).
- **Guiden:** `docs/guide/kurser.md` om euro och Riksbanken, stegen i panelen och **– kr**.

**Egna beslut och avsteg från skissen:**
- Skissens *Hämtad … genom Stockholm* blir *genom VPN* eller *genom Windows*: råsvaret förseglar sortens utgång och
  utgångens id, inte dess namn.
- *Sparas i ärendet* räknar kurserna från Bitstamp (en per period); svaren från Riksbanken (ett per månad) står på
  hovring.
- Skissen visar ett värde för en obekräftad tx; Chainsaw visar **– kr** med en klocka, eftersom kursen gäller
  blockets tid.
- Ett klick på **– kr** väljer bara linjen (det finns inga steg att visa).

**Granskningen** (oberoende, inga C):
- **I1:** taket på 30 uppslag räknades också för kurser ur ärendet. *Nu:* ärendet frågas först, och bara ett svar
  `NotInCase` räknas mot taket (`GraphSource.ValuesAsync`).
- **I2:** `ComposeAsync` väntade på alla kursuppslag. *Nu:* grafen och expansionen visas direkt med ärendets kurser;
  `IGraphSource.FillRatesAsync` hämtar de saknade (`EdgeValueGap.IsPending`) i bakgrunden, ett tak i taget, och
  `GraphViewModel.FillRatesAsync` fyller i linjerna utan att läsa grafen igen, tills inget mer går framåt. Offline
  frågas inget och linjerna säger *Offline* (M9); när utgången kommer tillbaka hämtas de.
- **Omgranskningen, I3:** hämtningen fortsatte så länge färre linjer väntade, också när varje kurs misslyckades, och
  ett misslyckat svar frågades om vid varje läsning. *Nu:* grafen fortsätter bara när en omgång gav minst en ny kurs
  och slutar direkt när källan svarar 429 (`PriceLookup.IsThrottled`, `GraphRates.IsThrottled`; källan frågas då inte
  mer i omgången). Pricing minns en period vars uppslag misslyckats och frågar inte om den förrän
  `PricingOptions.FailureCooldown` (fem minuter) har gått; under tiden svarar den *Failed* utan att något skickas.
  Den nya scenen byggs utanför UI-tråden och visas bara om ingen nyare hämtning eller läsning kommit (M1), och
  `FollowReadingAsync` körs efteråt så att läsningens val står kvar (M2).
- **M:** *dagskurs* (M1), utan fasta gröna prickar (M2), *En kurs som är satt hämtas aldrig igen* (M3), summeringen
  fångar allt utom avbrott och räknar om när ärendet byts (M4), en blocktid före datorns klocka skiljs från en
  tillgång utan källa (M5), valutan läses en gång per körning (M6), träffytan är den ritade rutan (M7) och ett test
  för euro på en lördag (M8).

**Återstår:** testärendet `/testdata` har inga påhittade kurser, så dess linjer hämtar riktiga kurser genom utgången.

Tester: 9 nya i Pricing (`EuroTests`) och 20 i App (`RatesTests`).

## Exponering, EP1b (2026-11-08)

- **Analysen** `exposure v1` ligger i Tracing (`IExposureAnalysis`, `ExposureAnalysis`): varje aktör läses som en
  plånbok med sina överföringar i en tillgång i tidsordning (inkommande före utgående i samma ögonblick), och
  kontomodellens registrerade metod (FIFO eller haircut, grafens val) säger hur mycket som följer med varje överföring.
  *Betalat till* bär klustrets utbetalningar framåt; *Fått från* kör metoden en gång per inbetalning hos aktören och
  delar ut det som gick mot klustret i den proportionen. Pengarna stannar hos en tjänst (räknas), en aktör bortom
  djupet eller som inte är en aktör (*Nådde ingen tjänst*), hos klustret (*Gick tillbaka*), och blir kvar hos en
  aktör (ut) eller kom före grafens historik (in). Tjänsterna och resten summerar alltid till totalen (testat med
  slumpade grafer). En tx-nod (CoinJoin) lägger inte till ett hopp. En aktör läses om högst åtta gånger när mer
  pengar kommer till den senare (cirklar).
- **Vad som är en tjänst** avgörs i appen (`ExposureMap`): varje aktör vars sort har en betydelse (offer, uttag,
  spenderas, risk, annan kedja); gråa är okända. Det slängda är inte med i grafens scen och räknas därför inte som
  tjänst.
- **Vyn** (`ExposureView`, `ExposureViewModel`, `Controls/ExposureRings`, `Controls/SortMark`, layouten i
  `Graph/ExposureLayout`) följer skissen: ringarna per hopp, utbetalt åt höger och inkommet åt vänster, tjänsterna
  större ju större andel, aktörer på vägen som små punkter, linjer i accent med sista steget i tjänstens färg, djupet
  1–3, metoden streckad i huvudet, andelarna per färg för båda hållen, tjänsterna och den valda vägen med tx,
  underlaget som brickor, *Varför?* (Guidens panel, som nu också får stå öppen i Exponering) och *Visa vägen i
  grafen* (G), som väljer tjänsten i grafen, tonar ner allt utom vägen och passar in den.
- **Egna beslut:** tillgången väljs med en liten växel i huvudet bara när ämnet har fler än en (belopp i olika
  tillgångar adderas aldrig). Listan av tjänster visar utbetalt, som skissen; inkommet väljs i ringarna. Ett nytt
  avsnitt i Guiden, *Exponering*.
- **Avsteg och det som återstår** (`docs/TODO.md`): kronorna under beloppen (väntar på KU1a), mixerns troliga utgångar
  som streckade förslag, *Tillbaka · Esc* i grafen och *Exponering …* i nodens meny. Uträkningen körs på UI-tråden
  när grafen läses om (`docs/BACKLOG.md`).
- Tester: 21 i `Chainsaw.Modules.Tracing.Tests` (facit med syntetiska överföringar för FIFO och haircut, båda hållen,
  djupet, kvar, tillbaka, före historiken, vägen, tx-nod, annan tillgång, summan) och 18 i App.Tests (kartan,
  layouten, vymodellen, pillret och grafens väg).

### Granskningen av EP1b rättad (2026-11-08)

- **I1:** på Bitcoin följs pengarna nu mynt för mynt (`UtxoExposureRun`) med den UTXO-metod som är registrerad för
  kedjan, med `UtxoGraph.Spends` och metoden per tx; aktörerna är summor av sina mynt. Metoden väljs efter sitt
  `Scope` (`TracingMethods.For`). Kontokedjorna läses som förut plånbok för plånbok (`AccountExposureRun`); det
  gemensamma (hopp, vägar, resultat) ligger i `ExposureTally`. En CoinJoin-nod som är en tjänst stoppar pengarna där.
- **I2:** nytt skäl `NotExpanded` (*Aktören är inte expanderad*) för en aktör som inte är expanderad eller har
  tillbakahållna tx; `Remains` bara för helt lästa.
- **I3:** vyn räknar bara när den visas (`IsShown`); en ny scen när den är dold markerar den inaktuell. Analysen körs
  i `Task.Run` med avbrott, den senaste vinner, och tillgångarna indexeras per nod i ett svep. Ett fel i analysen
  sägs ärligt i vyn.
- **I4:** tjänsterna tas ur `Snapshot.Graph` och etiketterna (`ExposureMap.Looks`, med `GraphScene.SortOf`), också för
  det som ligger i soptunnan.
- **M1–M3, M5, M6, M8:** det som fortfarande är på väg efter åtta omläsningar är ett eget skäl (*StillMoving*), utan
  justering i efterhand; tjänsterna och resten måste bli totalen exakt, annars kastas resultatet; avgifterna är ett eget
  skäl och en tx-nods bok ordnas efter index; en tjänsts tx är bara de som pengarna dit gick genom; *klustret* eller
  *adressen* efter ämnet; Guiden har ett avsnitt om rundturer. Facit för cykel, omläsning och `MaxRuns`.
- M4 och M7 står i `docs/BACKLOG.md`. Kronorna under beloppen väntar på KU1a, som inte finns på huvudgrenen än.

## Grafen: kortets baksida, markeringarna och tidslisten som lins (IN4a, GR9a och TL2a, 2026-11-08)

- **IN4a:** Alt + klick vänder en adress eller ett kluster runt sin lodräta axel (0,6 s, still utan animeringar), och
  baksidan växer till 260 px med en rad per tillgång. Flera kort kan stå vända; de ritas över noderna i ett eget pass
  (`GraphCanvas.Cards.cs`). Saldot hämtas genom utgången (`IGraphChainState`, `GraphChainState`): Bitcoin ur
  adressens sammanfattning (bekräftat mottaget minus spenderat, för högst tio adresser i ett kluster), Ethereum ur
  `IEthereumSource.GetBalanceAsync` med ETH och de tokens vars utgivare Chainsaw känner (USDT, USDC). Dagens kurs är
  den senaste avslutade timmen hos Bitstamp (eller dagen före när den inte finns än), förseglad i ärendet som varje
  kurs; offline den senaste kursen i ärendet med datum och kronorna streckade under. Höjden i foten är kedjans topp
  när saldot hämtades.
- **GR9a, bytt block:** `IBitcoinSource.GetBlockChangesAsync` läser bara ärendet: tx:er där ett senare förseglat svar
  visar en annan höjd, ingen höjd eller där källan svarat `404`, och jämför sedan det första svaret som hade tx:en i
  ett block med det nyaste (hash, inte bara höjd). **Inga nya skrivningar i ärendet:** båda svaren fanns redan
  förseglade (Chains hämtar om ett svar i ett grunt block), och jämförelsen görs vid läsning. Märket sitter i
  beloppets övre hörn, gult för *Bytte block* och *Tillbaka i mempool*, rött med överstruket belopp för *Finns inte
  längre*; rutan har förut och nu med höjd, hash och tid, och *?* öppnar Guiden (nytt ämne `ChainCheck`).
- **GR9a, Väntar på källan:** brickan i grafens huvud visas bara när varje påslagen endpoint för en kedja ber om
  väntan eller inte svarar, och minst en ber om väntan (`SourceWait`). Ringen töms till den första väntan är över,
  och vyn räknar ned varje sekund. Noden som expanderas får ett timglas som andas.
- **TL2a:** valvyns fönster blir listens lins när valvyn stängs (±48 h, ±7 d, ±6 h, sedan hela ärendet med brickan).
  Linsen tar 56 % av listen med dygn och timmar, resten trycks ihop med bara måndagarna. Listen slutar prickad vid det
  nyaste block ärendet har (`GetTipAsync(…, LookupMode.CaseOnly)`, inget uppslag). Inzoomade dygnsband (minst 320 px)
  får prickade timlinjer vid 06, 12 och 18.
- **Avsteg:** foten säger *Hämtar kurs genom utgången*, inte utgångens namn. En tx som inte längre finns streckar inte
  linjerna och aktörerna efter sig. Valvyns fönster har inte ±6 h, så det finns bara i listens steg. Rutan över
  *Väntar på källan* är verktygstipset, inte en egen panel.
- **Återstår:** se `docs/TODO.md` under *Grafen: kortet, markeringarna och listen*.

## Granskningen av IN4a, GR9a och TL2a rättad (2026-11-08)

- **C1, en endpoint som ligger efter:** ett grunt svar hämtas om hos en endpoint i fas med kedjan när någon är känd
  (`SourceLookups.FetchAsync(…, preferInStep)`), annars som förut. Ett nyare svar utan block, eller ett *finns inte*,
  räknas bara när dess endpoint enligt sina egna förseglade svar (kedjans höjd och tx:ernas block) hade kommit minst
  till blocket ärendet först såg tx:en i (`RawResponseStore.EndpointHeight`); annars hoppas svaret över.
  Märkets texter är bedömningar: *Källan svarar nu …, troligen …* (princip 2).
- **I1:** bara en `404` som uppslaget godtog (en betrodd endpoint, eller alla tillfrågade) noteras, i ett nytt register
  `chains_not_found` bredvid de förseglade svaren. En `404` från en endpoint som inte trodds, följd av ett fel, blir
  aldrig *Finns inte längre*.
- **I2:** `chains_bitcoin_seen` har `block_hash`, och kandidaterna jämförs på hash, så att ett byte till ett annat
  block på samma höjd syns också i en adresslista. `SealedResponseTransaction.BlockHash` följer med när ett svar
  förseglas om i ett annat ärende. Schemat ändrades i version 1 (före 1.0).
- **I3:** `GraphChainStateTests` provar den riktiga `GraphChainState` med fakes: klustrets summa och taket på tio
  adresser, en adress som fallerar, den äldsta tiden, reserven från timme till dygn, Ethereums kända tokens och att
  markeringarna läses bara ur ärendet. `BlockChangeTests` visar att läsningen inte skickar något, och `ChainStateTests`
  att timglaset står under en expansion.
- **M1–M7:** höjdens verktygstips säger blockets hash och om saldot hämtades nu eller lästes ur ärendet; det som väntar i
  mempool står som en liten klocka på tillgångens rad; foten säger *Ingen ny kurs* när utgången är ansluten men ingen
  kurs kom; läsningen av markeringarna har ett löpnummer, och ett äldre svar släpps; varje vändning har ett id, så att
  ett svar till en tidigare vändning släpps; en paus som slutar senare än den förra börjar ringen om; rutan visar
  endpointen och hashen med sex tecken först och åtta sist, och är 340 px bred.

## Omgranskningen av GR9a rättad (2026-11-08)

- **I1, en endpoint med flera noder:** en publik endpoint (mempool.space) har flera noder bakom en adress, och en av
  dem kan ligga efter sekunden efter ett nytt block, fast samma endpoint redan visat blocket. *Tillbaka i mempool* och
  *Finns inte längre* sätts därför bara när två svar i rad, efter det senaste som hade tx:en i ett block, säger samma
  sak: från olika endpoints, eller med minst ett nytt block i ärendet mellan dem (`EsploraBitcoinSource.Confirms`).
  Varje svar måste fortfarande komma från en endpoint som visat minst blockets höjd. Ett ensamt sådant svar ger inget
  märke; ett *Bytte block* (ett annat block) kräver ingen bekräftelse.
- **M1:** registret `chains_not_found` räknas bara när det förseglade svaret har status `404` och samma kind och subject,
  både i kandidatfrågan och när svaren läses.
- **M2:** en `404` som uppslaget godtog följer med när svaret förseglas om i ett annat ärende
  (`SealedResponseIndex.NotFound`), och läggs där i registret, aldrig som ett svar.
- **M5:** ett test med enbart utgående i mempool (en negativ väntande summa med −).
- **M3 och M4** ur omgranskningen är inte rättade här; de står i granskningens rapport till huvudsessionen och förs
  in i `docs/BACKLOG.md` därifrån.

## Rapporten, RP1b (2026-11-08)

- **I modulen Requests**, inte en egen modul: rapporten delar block, Markdown, `RequestFiles` (skriver aldrig över)
  och sättet att försegla med Begäran. `IReportService` (Check, Compose, Export, ListExports) med `ReportDraft`
  (spåren, slutsatserna, etiketterna, metoden, delarna) och `ReportTemplate`. Exporten journalförs som
  `requests.report-exported` och förseglas i en egen kedja, `requests.reports` (schemats steg 2), med SHA-256,
  transaktionerna och delarna.
- **Blocken** fick tre nya sorter, `Item`, `Reason` (*Varför:* under slutsatsen, `  >` i filen) och `Hash`, och en
  rubrik kan ha en bricka (`Tag`, `TagTone`) som `RequestPreview` ritar i färgen för var spåret slutar.
- **Hashkedjans topphash** är journalens (`cases.journal`) senaste förseglade rad ur `ICaseVerifier`, eftersom varje
  skrivning i ärendet går genom journalen. Exporten kontrollerar på nytt precis före skrivningen; förhandsvisningen
  kontrolleras när ärendet öppnas, vyn visas och efter en export. En bruten kedja sägs ärligt i filen.
- **Ur markeringen** (`ReportSelection`, utan WPF): aktörerna är grafens val (Ctrl+klick eller ruta). Ett spår går
  längs spårade linjer mellan markerade aktörer, från närmaste start (startpunkt eller markerad aktör som inget spårat
  når) till varje markerad aktör som pengarna inte lämnar för en annan markerad. En transaktionsnod passeras och är
  varken steg eller hopp. Varje steg har de överföringar som bär spårade pengar (samma regel som Begäran,
  `RequestExits.IsTraced`), annars alla linjens.
- **Sekretess** (fynden från Begäran): ett namn kommer bara ur en tjänsts etikett av aktörens egen sort, aldrig ur
  ett offers, en okänds eller en rapport till en underleverantör (Chainabuse kan namnge personer); annars förkortad
  adress. Etiketternas skäl, anteckning och vem som satte dem följer aldrig med, bara källa, version och dag. Ärendets
  titel kommer aldrig med, bara numret.
- **Slutsatserna:** kluster (heuristik med version och tx ur `IClusteringService`, läst utanför UI-tråden), etiketter
  (källorna) och spårningsmetoden. Spårat belopp och SEK sägs vara bedömningar; SEK bara när grafen har en kurs för
  linjen, annars säger delen ärligt att ingen kurs finns.
- **Vyn** (`ReportView`, `ReportViewModel`, `Controls/ReportMiniGraph`): material och dokument byggs i `Task.Run`,
  och en äldre omgång visas aldrig över en nyare. Rå Markdown är virtualiserad.
- **Avsteg från skissen:** markeringen är Ctrl+klick eller ruta (Shift används i högen); ingen *← Rapport*-knapp i
  grafen och ingen *Visa i mappen* i toasten (`docs/BACKLOG.md`); tider ISO som i Begäran; de exporterade filerna står
  med namn och SHA-256 i verktygstipset under förhandsvisningen.
- **Inte med:** godkända förslag (troliga utgångar) som slutsatser. Testärendet behöver inget nytt: rapporten byggs ur
  grafen.
- Tester: 21 nya i `Chainsaw.Modules.Requests.Tests` (mall, delar, sekretess i Markdown, export utan överskrivning,
  försegling, journal, topphash, återöppning) och 23 i `ReportTests` i App.Tests (spåren ur scenen, offrets och
  rapportens namn, kluster, bilden, vymodellen, exporten, pillret).

## Granskningen av RP1b rättad (2026-11-08)

- **I1:** kronvärdet räknas på den spårade delen, inte hela linjen: varje linjes spårade andel av dess spårade
  överföringar, var och en till ärendets kurs vid sin tid (`IPriceService`, bara ur ärendet, aldrig ut). Saknas en
  kurs, en tid eller blandas valutor blir värdet `null`. Andelen antas lika för linjens överföringar (en bedömning).
- **I2 och M2:** ett spår är delgrafen av följda linjer som leder till slutet (`ReportTrails`): alla starter, grenar och
  pengar som vänder står med, och *Spårat* summeras per tillgång över alla linjer in i slutet. Ett slut är en
  markerad aktör där pengarna lämnar kedjan eller spåret slutar, eller som pengarna bara går vidare från till aktörer
  som skickar tillbaka; av aktörer som pengarna cirkulerar mellan står den första för alla.
- **I3a:** topphashen är SHA-256 över de sorterade raderna `kedja:sekvens:hash` för alla förseglade kedjor ur
  `ICaseVerifier`, med antalet kedjor och journalens rad. **I3b:** *inte kontrollerad* (ingen kontroll) skiljs från
  *bruten* (`LedgerBroken`); ett sunt ärende utan journalkedja ger `CaseError.Corrupt`.
- **M1:** har journalen vuxit mellan kontrollen och sigillet rullas exporten tillbaka, filen tas bort och den görs om
  (högst tre gånger; den sista förseglas ändå, eftersom filen är sann för den rad den nämner). Kontrollen läser bara
  journalens sekvens i transaktionen, så en kedja som växer utan journalrad fångas inte.
- **M3:** steg vars överföring inte kan sägas spårad märks *(ej spårad)*. **M4:** `CaseChains.Journal` i
  Cases.Contracts. **M5:** rapporten byggs och topphashen kontrolleras bara när vyn visas. **M6:** exporten skriver det
  utkast som den visade rapporten gjordes av. **M8:** tester för två starter, romb, cykel, värdet, saknad kurs, ospårade
  steg, flera tillgångar, topphashen, bruten och saknad kedja, ny export efter en ändring, dold vy och visat utkast.
- **M7** (operatören i huvudet) står kvar som i Begäran.


## Ärendets kuvert, ÄR1b (2026-11-08)

Kollegans fil med ärendelösenordet, ärendets egenskaper med fliken *Kuvert*, och valet av gren efter en krasch.

- **Cases:** `ICaseStore.ImportWithPassword` provar lösenordet där filen ligger (fel lösenord kopierar inget), kopierar
  till Ärenden utan att skriva över (`.tmp` och flytt utan överskrivning), kontrollerar att kopian har samma id och
  öppnar den med samma filnyckel; en kopia som inte kan öppnas tas bort, källan rörs aldrig. `DescribeEnvelopes`
  (på filen och på ett öppet ärende) beskriver kuverten utan att ändra dem: ditt (bara om det öppnar till ärendets
  filnyckel), andra valv, delning, återställningsnycklar (inbyggd, återkallad, annan) och okända. Varje filnyckel som
  öppnas på vägen nollställs.
- **Konflikten:** `FindConflict` (utan valv), `OpenSettingAsideWorkingCopy` flyttar arbetskopian, dess journal, bas
  och `.tmp` orörda till `Återställda/<nummer> <UTC>Z/` (ett nytt namn om det finns) och öppnar filen med journalraden
  `case.working-copy-set-aside` (mappen och arbetskopians SHA-256). En arbetskopia som hör till filen tas tillbaka som
  vanligt i stället. `SaveWorkingCopyAsCase` gör arbetskopian till ett eget ärende (se granskningen nedan). Valvets kuvert
  krävs för båda valen.
- **Härdning:** en rollback-journal utan arbetskopia tas bort innan en ny arbetskopia görs, så att SQLite aldrig rullar
  in en främmande journal.
- **App:** `CaseDialogsViewModel` med `CasePasswordViewModel`, `CasePropertiesViewModel` och `CaseBranchViewModel`, och
  vyn `CaseDialogs`. `CaseWorkflow.OpenAsync` erbjuder lösenordet vid `NoAccess` och grenarna vid `Conflict`. Ett
  lösenord läses bara ur `PasswordBox` när det används (`ISecretEntry`) och nollställs direkt. Ärendeväljaren har
  *Egenskaper* (Ctrl+I) och *Öppna en kollegas fil …*. Ett ärende heter efter sin fil (`CaseSession.NumberOf`), så
  att en gren heter `-B`.
- **Egna beslut:** arbetskopians gren är förvald, som i skissen (inget går förlorat med något av valen). Kollegans
  fil öppnas först och det öppna ärendet stängs efteråt, så att ett fel lösenord inte kostar något. Egenskaper har
  också flikarna *Egenskaper* (titeln) och *Information* (filen och id:t).
- **Avsteg från skissen:** kollegans kuvert heter *Ett annat valv* (kuverten bär inget namn); grenbilden visar inte
  journalraden och hashen där grenarna gick isär eller antalet rader per gren; datum för när kuverten lades till visas
  inte. Se `docs/BACKLOG.md`.
- Tester: 26 i `Chainsaw.Modules.Cases.Tests` (`EnvelopeTests`, `BranchTests`) och 31 i App.Tests
  (`CaseEnvelopesTests`).
- **Granskningen (ett C, tre I, sex M), rättad:**
  - **C1:** en gren fick samma id som ursprunget, så ankarna växlade och `/verify` blev röd. Nu kopieras arbetskopian,
    får nytt id och nummer i `case_info` och krypteras om under en ny filnyckel (`CaseDatabase.Rebind`, på en egen
    anslutning utan vakten och innan den öppnas som ärende), och grenen får nya kuvert för valvet och den inbyggda
    återställningsnyckeln. Raden `case.branch-saved` namnger ärendet den kom ur (nummer och id), det nya id:t och
    vilka kuvert som inte kunde följa med (ärendelösenordet, andra valv, andra nycklar, okända). `CaseChecks` följer
    identiteten från första raden genom grenraderna, så `/verify` är grön för båda, också för en gren av en gren.
    Ursprungets arbetskopia flyttas till `Återställda` först när grenen är öppen, så ett misslyckande lämnar
    konflikten som den var. Med eget id är också **I1** (fel nummer i exporter) och **I2** (krockar i bevakningen och
    etikettbiblioteket) borta.
  - **I3:** *Öppna filen* som misslyckas (skadad `.cut`, låsning) flyttar tillbaka arbetskopian, och raden skrivs
    först när ärendet är öppet och känt av förrådet.
  - **M1:** härdningen provas med en verklig het journal från ett annat ärende mitt i en skrivning (testet faller utan
    härdningen). **M2:** lösenordet provas också på kopian och huvudena jämförs. **M3:** importen journalförs med
    filnamnet och SHA-256 av det som kopierades. **M4:** `.tmp` tas bort i `finally`, och `IsFree` räknar med den.
    **M4b:** ett ärende vars id redan finns i mappen under ett annat namn tas inte in igen. **M5:** grenens och
    flyttens rader skrivs efter `Track`, under ärendets lås. **M6:** tester för felvägarna.
  - **Kvar:** kraschar Chainsaw mellan att grenen öppnats och grenraden skrivits står grenen utan raden, och
    `/verify` blir röd för den. Ursprunget är då orört.


## Grafens rester: breda noder, högen, bytt block och utgångsrutan (2026-11-08)

- **Breda noder:** `TidyLayout.Arrange` tar nodernas bredd (`GraphScene.WidthOf`) som `HopLayout`: en kolumn med en
  bred nod stegar så mycket längre, så att beloppen behåller sin lucka. `CommentGeometry.PlaceOfNew` tar nodens ram
  som den ritas i stället för en punkt.
- **Högen i grafen trycks ut:** ytan bakom en öppen hög skalas runt högens mitt med `FanShock.Push` (1,1, samma
  konstant som startvyns `FanOutLayer`), lika långt som högen är öppen, så att den följer öppningens och
  hopfällningens lätta. Den öppna högen ritas inte i den skalade ytan, bara ovanpå det dimmade.
- **Linjerna efter en tx som inte finns:** `GoneTrail` följer pengarna framåt från linjerna med *Finns inte längre* och
  behåller de aktörer som ingenting utanför spåret når (pengar som går i ring inom spåret räknas inte; en startpunkt
  streckas aldrig). Linjen själv blir grå (TextDim) och streckad, linjerna efter streckade, båda 70 %; aktörerna efter
  får streckad ram och 60 %, som i skissen. Bara ritningen ändras; Tracing räknar fortfarande med det sparade svaret
  (står kvar i TODO).
- **Bytt block på Ethereum:** jämförelsen är en regel för båda kedjorna (`BlockChangeRule`, utbruten ur
  `EsploraBitcoinSource` utan ändrat beteende): samma skydd mot endpoints som ligger efter (`EndpointHeight`) och
  samma krav på två svar i rad från olika endpoints eller med ett nytt block emellan. `BlockChange` bär nu
  `TransactionId` (sträng) och `Chain`; Bitcoin har en egen konstruktor med `Txid`. Ethereums svar på `gettxinfo`
  saknar blockets hash, så hashen läses ur blocksvaret som hämtades närmast efter (annars före) tx-svaret; utan hash
  jämförs höjden. Ett *finns inte* räknas bara när det förseglade svaret verkligen säger det (`404`, eller
  Etherscan-stilens `status 0` och *not found*). Inga nya skrivningar i ärendet. `BlockMarks.ForLines` matchar på
  kedja och id.
- **Utgångsrutan:** mittringens text är *Ansluter igen …* och under den, litet, *Nästa försök kl. HH:mm:ss* (eller
  *Försöker nu*) ur samma regel som titelraden (`OfflineWords.NextAttempt`).
- **Avsteg och egna beslut:** ingen skiss visar återanslutningen i UT2b; ringen och linjerna står kvar röda och
  streckade, eftersom inga uppslag går (det säkraste), och bara texten ändras. Kommentarer (WPF-element) och
  verktygstips följer inte med när ytan trycks ut, eftersom de ligger under det dimmade.

## Testärendet påfyllt och TESTER-INFOR ordnad (2026-11-08)

- **Byggt:** `/testdata` (berättelse version 2) har nu påhittade kurser (`TestDataPricing` i Pricing, Debug, med
  `TestRatesHttp` i appen och `TestDataPriceRouter`), så att grafen i testärendet aldrig skickar kursuppslag; ett bytt
  block på sista skalningshoppet (svaret i en adresslista säger ett föräldralöst block på samma höjd, tx:ens eget svar
  det riktiga, `TestChain.Orphaning`); en sanktionsträff ur en påhittad lista (`TestDataSanctionLists`, bara medan
  testärendet byggs, så ingen riktig lista importeras eller ersätts); Virveln, en andra mixer som mixerns näst
  troligaste utgång går in i (*Mixer efter mixer*); en möjlig CoinJoin vid Sydbörsen vars plånbok godkänns; mixerns
  troligaste utgång godkänd med resten kvar att avgöra; påfyllnaden uppbruten; kommentaren ändrad; och en kommentar
  och en egen yta i soptunnan. Åtta nya tester i `TestCaseStoryTests` (kurser, bytt block, sanktion, besluten,
  soptunnan, begäran och rapport, exponering), 27 i projektet.
- **Rättat på vägen:** att godkänna en plånbok med många förslag (en skalningskedja, sex adresser) föll på journalens
  gräns på 1 000 tecken i `ClusteringService.Merge`. Raden säger nu antal adresser och delar när allt inte får plats;
  detaljerna har alltid varje del.
- **Egna beslut:** plånboken som godkänns är en egen liten möjlig CoinJoin vid Sydbörsen, inte skalningskedjans:
  att slå ihop skalningskedjan till en nod gömmer kedjan i grafen och trängde ihop beloppen runt Navet. Uppbrytningen
  gäller påfyllnaden (inte bedragarens tre adresser, vars enda länk annars bryts helt). Sanktionen kommer ur en lista i
  stället för som öppen källa, så att den hamnar under SANKTIONSTRÄFFAR i ET1a.
- **TESTER-INFOR:** ordnad efter område (start, skalet, ärenden, utgångar, inställningar, grafen, förslag, etiketter,
  bevakning, begäran och rapport, Guiden, testärendet). Struket: förord om att gränssnittet saknas, statusraden,
  Terminal, NC1a:s startpunktsfält, IN1a:s knappar i inspektören, det första Bevaka-formuläret (BV3a gäller),
  magenta och rosa (terrakotta och bevakningens turkos), *Skälet krävs*, Ethereum *inte byggt än*, och UT1a:s rad.
  *Små rättelser* är fördelade på sina områden. Sonaren står kvar i valvyn (EX2) och tidslinjen (TL1a), där
  CLAUDE.md fortfarande säger den.
- **Kvar:** se `docs/BACKLOG.md`, *Testärendet (/testdata)*.

## Resterna efter valen, omgång 4 (Begäran, sparningen, plånboken, Exponering, utgången, sökningen, 2026-11-08)

- **Börsens markering (BG1b):** ett nytt märke `NodeBadge.RequestExported` (blått, `Icon.Request`, *Begäran
  exporterad* i bildspråkets ruta). Det ritas inte ur scenen utan ur ärendets exporter: `RequestsViewModel.ExportedNodes`
  (uttag med en exporterad begäran om uppgifter, inte frysningar) ges grafen med `GraphViewModel.ShowRequested`, och
  `MarksOf` lägger märket efter scenens; ögat och kandidatens bock räknar med det.
- **Sparningen som inte går:** `ICase.SaveStateChanged` (`CaseSaveState`, antalet misslyckade i rad, noll när en
  sparning går igen) höjs från bakgrundssparningen och när en sparning lyckas efter misslyckanden.
  `AutosaveSchedule.Failed` och `Saved` säger antalet och om det var misslyckat förut. `SaveAlarm` i appen visar en
  varning med *Försök igen* efter tre i rad (`FailuresBeforeWarning`, ungefär 7 minuter), en gång, tar bort den när en
  sparning går och säger *Ärendet är sparat igen*. *Försök igen* sparar på trådpoolen. Eget beslut: tre i rad.
- **Ett ändrat förslag:** `SuggestionChangedException` (en `ArgumentException`, i Checks.Contracts) när klustringens
  förslag inte längre fogar ihop plånboken; panelen kör kontrollerna igen och säger `ChangedText`. Bekräftelsen vid
  Ctrl+Z väntar på en skiss (`docs/TODO.md`).
- **Exponering, kronorna (KU1a):** `ExposureValues` värderar pengarna på linjerna in till tjänsten efter linjens värde
  vid tiden (`GraphSnapshot.Values`), i proportion till pengarnas del av linjen; saknar en linje värde står inga
  kronor. Skrivs efter beloppet i den valda tjänsten (`0,2500 BTC · ≈ 23 400 kr`, som i skissen) och i tipset på
  andelarna per färg. Avsteg: skissen har kronorna också i tipset på ringarnas noder; det tipset finns inte i appen.
- **Exponering, vägen tillbaka:** `GraphViewModel.ReturnTo` (en vy) med en knapp *‹ Exponering Esc* i grafens huvud
  som skissen, och Esc i grafen; glöms när grafen lämnas. Allmän, så att Rapportens *← Rapport* kan använda den.
- **Exponering i nodens meny:** raden *Exponering …* (`NodeMenuAction.Exposure`) för ett kluster eller en adress som
  betalat eller fått (`ExposureMap.Subjects`); den öppnar vyn med noden i mitten.
- **Exponeringens stoppregler:** `SpendRules` är regeln en gång för `TraceCalculator` och `UtxoExposureRun`: en
  länk till annan kedja (pengarna når den andra kedjans nod, med linjen dit), `TransactionEnds` (CoinJoin som inte
  räknas som tjänst) och en spend som inte går ihop. Två nya skäl i `ExposureRest`: `TrailEnds` och `Conflicting`.
  Bakåt (*Fått från*) stoppar en tx där spåret tar slut och en ingång vars spend pekar annanstans; länken gäller bara
  framåt, eftersom den säger vart myntets pengar fortsatte, inte varifrån utgångarna kom. På kontokedjan går en länkad
  överföring till den andra kedjan i stället för in i plånboken, som `AccountTraceCalculator`. `exposure v2`.
- **Utgångens tips (UT3b):** `ExitPanelViewModel.SeenTip` (`ExitCheckWords.TipText`), och titelradens tips slutar med
  `syns som <adress>, <land>`. Windows som utgång kontrolleras en gång när datorn är online och Chainsaw upplåst, om
  *Kontrollera vid anslutning* är på; svaret hålls bara i sessionen. Avsteg: raden *Kontrollera vid anslutning* står
  under VPN i inställningarna, så den ändras där också för Windows.
- **Sökningen och soptunnan (KR1a, SO1a):** det slängda söks (`SceneEntries.ReadTrashed`), raden säger *I soptunnan*
  och Enter öppnar soptunnan. *Lägg tillbaka* på raden är inte byggt.
- **Det instabila testet:** `CaseEnvelopesTests.A_case_this_vault_cannot_open_asks_for_its_case_password` föll ungefär
  en gång av åtta: dialogen läser kuverten med `Task.Run`, och utan UI-tråd i testet ritades korten om på trådpoolen
  medan testet läste dem. `CaseDialogsViewModel.Showing` håller visningen, och testet väntar på den.
- **Inte byggt:** mixerns troliga utgångar i Exponering (skrivet som [skiss] i `docs/TODO.md`).
- Tester: 7 i `SaveAlarmTests`, nya i `ExitManageTests` (5), `ExposureTests` (3), `RequestsTests` (1),
  `GraphViewModelTests`, `ChecksViewModelTests` och `CommandSearchTests` (1 vardera) i App.Tests; 2 i
  `Chainsaw.Modules.Cases.Tests`; 4 i `Chainsaw.Modules.Tracing.Tests`.
- **Granskningen (ett I, sex M), rättad:**
  - **I1:** en länkad Bitcoin-coin går till den andra kedjan också när mottagaren är en tjänst eller ligger bortom
    djupet (`LinkedOn` före `goesOn`). I *Fått från* räknas en länkad ingång inte som källa: den får vikt noll i
    uppdelningen, som en länkad överföring utanför plånboken på kontokedjan. Betalades en utgång bara av länkade
    ingångar räknas dess pengar som *Nådde ingen tjänst*. **Omgranskningen:** den länkade ingången behåller sin vikt i
    uppdelningen, och dess andel blir ett eget skäl, `ExposureRest.OtherChain` (*Fortsätter på annan kedja*), så att
    ingen annan ingång får mer än den betalade; samma skäl när den andra kedjans nod saknas. *De är körda igen* står
    före det den nya körningen själv sa.
  - **M1:** en varning som användaren stängt glöms, så att nästa misslyckande varnar igen. **M2:**
    `SaveStateChanged` höjs för varje lyssnare för sig (`SayHowSavesGo`), och en lyssnare som kastar räknas aldrig
    som en misslyckad sparning. **M3:** *Försök igen* fångar alla fel (utom minnesbrist), noterar bara feltypen med
    `Trace` och visar varningen igen. **M4:** *De är körda igen* sägs bara när kontrollerna gick att köra. **M5:**
    Begärans exporter töms när ärendet byts. **M6:** Esc i grafen släpper först det valda och går tillbaka först när
    inget är valt (`ReturnsOnEscape`).


## Ethereum klart, omgång 2 (2026-11-08)

- **Kända kontrakt:** `KnownContracts` (Chains.Contracts, bredvid `KnownTokens`) är Chainsaws lista, version
  `v1 2026-11-08`: Tornado Cash Classics 19 pooler på Ethereum, routern och den gamla proxyn (mixer); Uniswaps V2-,
  V3-, SwapRouter02- och Universal Router-kontrakt (DEX), 1inch Router v6 och 0x Exchange Proxy (aggregator); och
  bryggorna till Arbitrum One, OP Mainnet och Polygon PoS. Varje adress är kopierad ur operatörens egen sida (källan
  och dagen står på posten), Tornados också kontrollerade mot OFAC:s åtgärd 2022-08-08; inga adresser är påhittade.
  En adress i blandade versaler läses med EIP-55-kontroll, så ett kopieringsfel stoppar Chainsaw vid start (och testet).
- **Etiketterna:** `IKnownContractLabels` (Attribution) sätter *Mixer* på en pool och *Swap eller brygga* på en
  brygga, som `LabelSource.OpenSource` med listans namn och version, en rad i journalen
  (`attribution.known-contracts-matched`) och ursprunget `known-contract/<adress>`, som är detsamma i alla listans
  versioner. En etikett som redan finns i ärendet på något sätt, också tillbakatagen eller från ett annat ärende, sätts
  inte igen. `GraphSource` frågar listan om grafens Ethereum-adresser varje gång grafen läses (som etikettbiblioteket)
  och läser om grafen bara när en etikett sattes; ett fel stoppar aldrig grafen. Spåret tar slut genom
  `LabelTrailEnds`, som förut.
- **Swappens DEX:** `TokenSwap.Venue` är den kända DEX eller aggregator som tx:en skickades till (när hela tx:en är
  läst) eller som adressen skickade till eller fick från i den; en mixer eller brygga på andra sidan räknas inte.
  *Varför?* för swappen säger namnet, kontraktet och källan.
- **Plats i blocket:** `EthereumBlockReference.TransactionIndex` läses ur Blockscouts historik (`transactionIndex`).
  `AccountData.Combine` sprider platsen till tx:ens alla rörelser och avgiften (de interna tx:ernas lista saknar den),
  och `AccountGraph.ChainOrder` ordnar efter block, plats, hash och position. En tx utan känd plats står efter dem i
  samma block som har en.
- **Egna ytor (Z) med Ethereum:** `CheckRules.MemberOf(ChainAddress)` och `CheckRules.AddressOf` tar en Ethereum-adress
  (`address:0x…`, gemener) bredvid Bitcoins; `GraphAreas` läser medlemmarna med kedja och hittar noderna genom
  `Node.Addresses`. Formatet i ärendet är detsamma för Bitcoin.
- **Start-tx som inte finns i ärendet:** `CaseWorkflow.FetchStartTransactionsAsync` hämtar en start-tx (Bitcoin eller
  Ethereum) som ärendet saknar, genom samma expansion som E, när startpunkten lagts till eller ärendet skapats, och
  grafen läses om. En Bitcoin-tx följs då framåt som vid E (dess mottagare, högst 50).
- **Länkens kolumner** (`from_txid`, `from_output` för en Ethereum-rörelse) beskrivs vid `TracingSchema.CrossChainColumns`.
- **OFAC:s ETH-adresser** var redan byggda (läses och kontrolleras för startpunkterna); punkten är struken ur BACKLOG,
  och att grafens övriga adresser inte kontrolleras står kvar där, för båda kedjorna.
- **Guiden:** *Bryggor och swappar* har ett stycke om de kända kontrakten.
- **Skriver i ärendet (granskas):** etiketterna från listan (förseglade i `attribution.labels`, journalrad), och de
  expansioner som start-tx:en hämtas med (som E). Egna ytors medlemmar kan nu vara Ethereum-adresser.
- **Avsteg och egna beslut:** en DEX:s router får ingen etikett, eftersom ingen sort i bildspråket passar (står i TODO
  som [skiss]). Tornado Cash är *Mixer*, inte *Sanktionerad*: OFAC tog bort Tornado Cash ur SDN-listan 2025-03-21, och
  en sanktion kommer bara ur listan användaren importerat.
- **Kvar (BACKLOG):** spårningen genom en swap (väntar på beslut), Tornados troliga utgångar, kontoheuristikerna
  (`IAccountHeuristic`, kräver kluster med `ChainAddress`), kontrollerna och plånböckerna på Ethereum, saldot vid ett
  block ur källan, regeln om täckning för en aktiv adress, omexpansionens golv (höjderna sparade med expansionen),
  Etherscan med nyckel (Network) och fler kända kontrakt.


## Granskningen av Ethereum omgång 2 rättad, och Tornado Cash tidigare sanktionerad (2026-11-08)

- **I1–I3, start-tx:** hämtningen är en egen klass, `StartTransactionFetches` (App). Den kör en hämtning i taget i
  bakgrunden, försöker igen när ärendet öppnas och när utgången ansluts (`GraphViewModel` följer `ExitWay`), stoppas
  när ärendet stängs (`CloseCurrentAsync` väntar på den) och när Chainsaw låses (`IVault.StateChanged`). En start-tx
  som inte kunde hämtas sägs i en varning i toastern med skälet ur Tracing och att Chainsaw försöker igen. Ett
  oväntat fel loggas bara med typen (`Trace.TraceWarning`), aldrig med meddelandet. E väntar på hämtningen
  (`CaseWorkflow.StartTransactionsSettled`), så att samma tx aldrig hämtas två gånger.
- **I2:** `KnownContractLabels.Check` läser först (`ICase.Read`) och öppnar en skrivning bara när något nytt ska
  sättas, så att ärendet inte sparas om för varje läsning av grafen.
- **M1:** Arbitrums *Bridge* och Base (*L1 Standard Bridge*, *Optimism Portal*) är med, ur Arbitrums och Bases egna
  sidor. Listan heter fortfarande v1, eftersom den inte är ihopslagen än.
- **M2:** `transactionIndex` läses också ur `gettxinfo` när svaret har fältet. **De spårade beloppen i ett befintligt
  Ethereum-ärende kan ändras** med den nya ordningen: två tx i samma block ordnades förut efter hash, nu efter sin
  plats i blocket, och FIFO räknar då det som kom in före det som gick ut i den ordning kedjan tillämpade dem.
- **M4:** att en kopia från ett annat ärende (samma ursprung) gör att listan inte sätter sin egen är ett avsiktligt
  val: samma bedömning ska aldrig stå två gånger. Det står i koden och har ett test.
- **M5:** `GraphSource.LabelKnownContracts` fångar `CaseException` tyst och loggar andra fel med bara typen.
- **M6:** journalraden säger *1 adress är ett känt kontrakt* i singular.
- **Tidigare sanktionerad** (användarens beslut 2026-11-08): Tornado Cashs 21 kontrakt får, utöver *Mixer*, en
  etikett av den nya sorten `LabelKind.FormerlySanctioned` med namnet *OFAC SDN 2022-08-08 – 2025-03-21*, källan
  Chainsaws kända kontrakt och skälet med båda datumen och OFAC:s egna meddelanden
  (`ofac.treasury.gov/recent-actions/20220808` och `/20250321`). Båda datumen och att alla 21 adresser finns i båda
  meddelandena är kontrollerade mot OFAC:s sidor 2026-11-08. I grafen är det markeringen `NodeBadge.FormerlySanctioned`
  i hörnet: sanktionens markering med samma ikon, men en mörk dov röd skiva och en dämpad röd ikon. En sanktion som
  gäller tar över, så då ritas bara den. Rutan med bildspråket har raden *Tidigare sanktionerad*, och inspektören har
  faktat *Sanktion*. Sorten kan inte väljas i Etikett (L), och den avslutar aldrig spåret. Testärendet har en påhittad
  tidigare sanktion på mixern.
- **Skriver i ärendet (granskas):** den nya etiketten (förseglad som de andra), och hämtningarna av start-tx, som nu
  också görs när ärendet öppnas och när utgången ansluts.

## Spåret genom en swap, SW1a (2026-11-08)

Användarens beslut 2026-11-08: genom en swap följer det spårade **värdet** med, och utredaren väljer per swap att det
inte gör det (skissen SW1a).

- **Regeln på ett ställe:** `SwapValue` (Tracing.Contracts) är analysen `swap-value v1`: den spårade andelen av det
  som gick in i bytet (`TokenSwap.Sold`, summerat) blir samma andel av varje överföring som kom ut, avrundat nedåt
  (`SwapValue.Follow`), så att regeln aldrig skapar spårade pengar. `TokenSwap.IsSold`/`IsBought` säger vilka rörelser
  som hör till bytet. Spårningen, exponeringen, grafen, inspektören och rapporten använder samma regel.
- **Spårningen:** `AccountTraceCalculator` spårar nu alla tillgångar tillsammans, runda för runda, tills inget ändras:
  varje tillgångs konton med metoden (FIFO eller haircut, kontrollerad av `AccountChecks.Run` som förut), och sedan
  varje följd swap, som sätter det spårade på det som kom ut. Det som gick in i en swap går aldrig in i poolens
  plånbok (som en länk till annan kedja), så att inget räknas två gånger. Slutet för den sålda tillgången är det nya
  `TrailEndReason.Swapped` (inte ett slut, pengarna fortsätter i den nya tillgången) eller `SwapStopped` (ett riktigt
  slut, användarens val). `AssetTrace.SwappedIn` och `Total` säger hur mycket som kom genom swappar; ändarna summerar
  till `Total`. `AccountTraceResult.Swaps` har en `SwapConversion` per swap som spårade pengar nådde, med in, ut,
  metoden och valet. `TracedAmount.Swaps` säger vilka swappar ett belopp kan vila på (generöst: en utgående rörelse
  ärver från alla tidigare spårade inkommande), och `RestsOnSwap` gör linjen streckad. `Explain()` nämner analysen.
- **Valet:** `ITracingService.StopAtSwap`/`FollowSwap`/`GetSwapStopHistory`. Ett val och dess tillbakatagande är var
  sin förseglad rad i den nya kedjan `tracing.swap_stops` (schemats version 6) med journalrad
  (`tracing.swap-stopped`, `tracing.swap-followed`), som länkarna till annan kedja; skälet är frivilligt. Att välja
  *Följer inte* igen för samma swap lämnar valet som redan gäller. `TraceGraph.SwapStops` bär valen som gäller.
- **Exponeringen** (`exposure v3`): `AccountExposureRun` läser plånböckerna per tillgång och följer pengarna genom en
  swap åt båda hållen (ut: det som gick in blir det som kom ut; in: det som kom ut leder tillbaka till det som gick
  in). Varje överföring bär pengarna i sina egna enheter och deras **värde** i ämnets tillgång, som är det som räknas;
  värdet som gick in i en swap är exakt värdet som kom ut (resten av avrundningen läggs på den sista överföringen).
  Ämnets egna swappar läses som en plånbok i de andra tillgångarna. *Följer inte* stoppar pengarna som *Spåret tar
  slut*. Summan av tjänsterna och resten är fortfarande exakt totalen.
- **Grafen (SW1a):** `EdgeView.SwapStops`, `SwapConversions` och `IsSwapStopped`; en linje vars spårade belopp vilar på
  en följd swap är streckad i accent. `GraphCanvas.Swaps.cs`: ett klick på pillret fäller ut *Följer* och *Följer
  inte* och **?** (Guiden), hovring visar rutan med stegen (`SwapPill.Box`, utan WPF); vid *Följer inte* blir pilen ett
  stopp (`Icon.Swap.Stop`), en person sitter i pillrets hörn och ett kort streck korsar linjen efter pillret.
  `GraphViewModel.ChooseSwapAsync` sparar valet genom `IGraphEditor` och läser om grafen.
- **Historik (AN1b):** adressen som bytte har raden *Följer inte genom swappen · ₮ → Ξ* med *Ta tillbaka*
  (`HistoryUndo.FollowSwap`), och tillbakatagandet som egen rad.
- **Rapporten:** en slutsats per swap på spåren, med andelen och analysen, eller att spåret slutar i swappen; skälet
  tas aldrig med. Begäran följer de spårade linjerna och får därför uttag efter en swap av sig själv.
- **Guiden:** nytt avsnitt *Spåret genom en swap* (`swappar.md`, under Ethereum); *ERC-20*, *Varför ett spår tar slut*
  och *Bryggor och swappar* hänvisar dit.
- **Skriver i ärendet (granskas):** valen och deras tillbakatagande (förseglade, journalförda). Spårade belopp på
  Ethereum ändras: det som tidigare stannade som *inte expanderat* hos poolen följer nu med i den nya tillgången.
- **Avsteg och egna beslut:** rutan säger *Ditt val* med tiden, inte namnet (namnet står i Historik, som läser
  journalen). Skissens kortkommando F i pillret är inte byggt. Har en linje flera swappar gäller valet dem alla, och
  rutan visar den första. Poolens överföringar som kom ut ur en swap räknas inte i poolens egen plånbok i exponeringen.
  Golden-facit för Bitcoin är omskrivet bara för schemats version och den nya kedjan.
- **Kvar:** testärendet har ingen Ethereum-källa, så swappen syns inte i `/testdata`. Exponeringens *Varför?* säger
  inte när vägen gick genom en swap (bara analysens version).

## Granskningen av SW1a rättad (2026-11-08)

Den oberoende granskningen fann inga C, tre I och fyra M; alla är rättade, och granskarens prober är tester
(`SwapEdgeTests`).
- **I1, tredje part i swappens tx:** bara det som gick till bytets andra sida (en adress som skickade tillbaka den
  köpta tillgången, kontraktet tx:en anropade eller den kända DEX:en) räknas som insatt; finns ingen sådan och bara en
  överföring i den sålda tillgången, är det den. Annars visas ingen swap. `TokenSwap.SoldMovements` säger vilka
  rörelser det är, och pillret sitter bara på deras linje. En swap identifieras av tx och konto.
- **I2, en länkad överföring:** `SwapValue.GoesIn` är regeln på ett ställe för spårningen och exponeringen: en
  överföring som användaren länkat till annan kedja är aldrig en del av en swap.
- **I3 och M3:** Begäran nämner `swap-value v1`, andelen och swappens tx för varje swap på spåret dit
  (`RequestTrail.Swaps`), och exponeringens *Varför?* säger när vägen gick genom en swap (`ExposureReach.Swaps`).
- **M1:** inspektörens *Spårat hit* nämner analysen när beloppet kom genom en swap, och *Följer med genom en swap*
  skrivs som antal swappar, aldrig som ett summerat belopp som *stannar här*.
- **M2:** startpunkter på båda sidor av en swap vägras som startpunkter i varandras flöde.
- **M4:** `StopAtSwap` vägrar en swap som ärendets graf inte visar; `tracing.swap_stops` har kolumnen `chain`;
  swapparnas rörelser är indexerade, och spårningen räknar om bara de tillgångar som ändrades.

## Andra granskningen av SW1a rättad (2026-11-08)

Inga C; två I med samma rot i hur en swap känns igen, och fyra M.
- **Hellre ingen swap än en felaktig** (`AccountData.SwapOf`): reserven *exakt en utgående överföring* är borta. Det är
  en swap bara när adressen fick tillbaka en tillgång den inte också skickade (en återbetalning i samma tillgång är
  aldrig ett köp), skickade en tillgång och allt av den gick till bytets sida (`TradeSide`: det anropade kontraktet, de
  som skickade tillbaka den köpta tillgången, en känd DEX, och adresser som i samma tx syns föra den köpta tillgången
  eller WETH dit), och ingen motpart är en känd brygga eller mixer eller har en etikett som börs, mixer eller brygga
  (`GraphBuilder.IsSwapEndpoint`). `KnownTokens.EthereumWeth` är ny.
- **Tester** (`SwapRecognitionTests`): granskarens tre prober (ingen swap; pengarna når bryggans pool, handlaren och
  paret), och positiva fall: Uniswap V2 genom routern när tx:en är läst hel, en V3-pool direkt och 1inch. V2 genom
  routern ur bara historiken känns inte igen (parets WETH syns inte); det står i Guiden och BACKLOG, liksom en
  aggregators avgift till en egen adress.
- **M:** exponeringens *Varför?* ger ämnets andel i siffror per swap (`ExposureSwap`); `StopAtSwap` kontrollerar i
  skrivtransaktionen att swappens tx finns i grafens tillstånd, utan att läsa om grafen; tester för Begärans spår
  genom en swap och för inspektörens nya rader.

## Tredje granskningen av SW1a rättad (2026-11-08)

- **I, damm som gör en betalning till en swap:** det som kom tillbaka måste ha ett värde Chainsaw känner (ether, WETH
  eller en token igenkänd på sitt kontrakt), eller bytet ha gått genom en känd DEX eller aggregator; annars ingen
  swap, och pengarna når mottagaren. Granskarens prob är ett test.
- **M-1:** en tx som skulle vara en swap men har en motpart som spåret ska nå (känd brygga eller mixer, eller en
  etikett som börs, mixer eller brygga, också satt senare) sparas som `SwapRefusal` (`AccountGraph.RefusedSwaps`).
  *Varför?* på linjen och noderna säger *Ingen swap i tx …* med etiketten och källan, och ett förseglat *Följer inte*
  utan swap står i Historik med *ingen swap längre* och kan tas tillbaka.
- **M-2:** exponeringens *Varför?* läst in säger andelen av det som kom ut.
- **M-3:** `StopAtSwap` läser ärendets Ethereum-fakta (inte hela grafen) och kräver att just den tx:en är en swap för
  just det kontot.

## Fjärde granskningen av SW1a rättad (2026-11-08)

- **I, en egen pool med en påhittad token genom den riktiga routern:** det som kom tillbaka måste alltid ha ett känt
  värde (ether, WETH, en token igenkänd på kontraktet); en känd DEX räcker aldrig, eftersom vem som helst kan öppna en
  pool där. Granskarens prob är ett test (med symbolen FAKE och en falsk USDC).
- **Sidofyndet:** en token som inte känns igen på kontraktet står med symbolen och ett frågetecken (`USDC?`) i pillret,
  i inspektören och på linjen (`SwapMarks.MarkOf`, `SwapMarks.UnknownTokenMark`).
- **M:** `StopAtSwap` frågar etiketterna som när grafen byggs (`GraphBuilder.SwapEndpoint`, en regel för båda), så att
  en swap som en etikett vägrar inte heller kan väljas.

## Femte granskningen av SW1a rättad: priset prövas (2026-11-09)

- **I, en riktig token till ett absurt pris i en egen pool:** kontrollen `swap-price v1` (`SwapValue.CheckPrice`)
  prövar värdet av det som gick in mot det som kom ut vid swappens tid, med kurserna ärendet redan har (Pricing,
  bara ur ärendet, inget uppslag). Skiljer de mer än `SwapValue.MostPriceGap` (20 %, för avgifter, slippage och att en
  kurs gäller en timme) är det ingen swap i spåret eller exponeringen: pengarna går till poolen som en vanlig
  betalning. Pillret är streckat i riskens färg med ett stopp, inget val går att göra, och *Varför?* och rutan visar
  båda beloppen och värdena. Tracing hämtar inga kurser själv: anroparen ger dem som `SwapPricing` (i
  `AccountTraceRequest` och `ExposureRequest`), och GraphSource läser dem ur ärendet; WETH värderas som ether.
- **Utan kurs, eget beslut:** värdet följer med som förval, och pillret är streckat i en lugn ton med *priset inte
  prövat* i rutan och *Varför?*. Skälet: användarens beslut är att värdet följer med, kurserna för ether, USDT och
  USDC finns oftast i ärendet så snart linjerna har värderats, och ett offline-ärende ska inte tyst tappa spår som
  annars följts; osäkerheten syns i stället, som allt annat osäkert, streckad.
- **Tester:** granskarens två prober (1 000 USDT mot 1 wei WETH; 0,000001 USDT mot 1 000 ETH, också i exponeringen)
  och en swap till rimligt pris som följer, och samma utan kurser (inte prövat, följer).
- **Kvar (BACKLOG):** `StopAtSwap` läser fortfarande hela Ethereum-delen (inte bara tx:ens svar), och prisprövningen
  görs om först när grafen läses om efter att kurserna hämtats i bakgrunden.

## Sjätte granskningen av SW1a rättad: kurserna för swappen hämtas (2026-11-09)

- **Kurserna:** `GraphSource.FillRatesAsync` hämtar först kurserna vid varje swaps tid, för båda sidor och med WETH
  som ether, inom `MostRateLookups`, och sedan linjernas. Kom en kurs som ärendet saknade (`GraphRates.SwapRatesFetched`)
  läses grafen om, så att spåret räknas med prövningen.
- **M-1, aldrig tyst:** ändras spåret av prövningen (orimligt pris, eller en del som stannar i poolen) säger toastern
  det en gång, *Spåret genom en swap räknades om efter kurserna*, med swappen och prövningens resultat
  (`GraphViewModel.RecalculatedSwaps`).
- **Begäran och rapporten** säger alltid prövningens resultat: `RequestSwap` har `PriceCheck` och `PriceGap`, och
  begäran skriver *prövat … skiljer x %* eller *inte prövat: kursen vid tx:ens tid saknas i ärendet* (M-3: också
  offline och när källan ber Chainsaw vänta). Rapportens slutsats har `SwapPriceCheck.Summary`.
- **M-2, stannar i poolen:** inom 20 % blir skillnaden i värde ett eget slut, `TrailEndReason.KeptBySwapPool`
  (*Stannar i poolen*), vid poolen: det spårade gånger (1 − ut/in). Det som följer genom swappen är resten, så att
  summan av sluten fortfarande är det spårade. Eget beslut: det köpta som följer räknas som förut på mängden, eftersom
  det som kom ut redan är värt mindre.
- **Tester:** granskarens prob genom GraphSource med en falsk kurskälla (WETH till ett absurt pris vägras efter att
  kurserna hämtats, och sägs som omräknad), ett rimligt pris med 2,5 % i poolen, en källa som ber Chainsaw vänta, och
  i Tracing 1 000 USDT mot 0,32 ETH (40 USDT stannar i poolen, 960 följer, summan 1 000).
- **Kvar (BACKLOG):** exponeringen drar inte av poolens del.

## Sjunde granskningen av SW1a rättad: värdet växer aldrig genom en swap (2026-11-09)

- **I, mer ut än in inom bandet:** regeln är symmetrisk. Var det som kom ut värt mer än det som gick in följer bara
  andelen (värdet in / värdet ut) av det köpta som spårat, avrundat nedåt (`SwapPriceCheck.FollowShare`,
  `SwapPriceCheck.ShareOf`); resten är inte spårat. Granskarens prob (10 000 kr in, 12 000 kr ut) är ett test bredvid
  fallet med mindre ut.
- **M-4:** toastern säger också när beloppen ändrades av detta (`SwapPriceCheck.ChangesAmounts`).
- **M-1:** *Varför?* i Exponering säger att poolens del inte dras av där (BACKLOG).
- **M-2:** grafen läses om för swapparnas kurser högst en gång per läsning.
- **M-3:** `HasUntestedSwapPrices` gäller bara swappar som spårade pengar går genom.

## Åttonde granskningen av SW1a: de tre M rättade (2026-11-09)

- **M-a:** andelen genom en swap räknas exakt med heltal (värdena läses som exakta bråk), avrundat nedåt
  (`SwapPriceCheck.FollowingOf` och `KeptOf`), i stället för med tolv decimaler.
- **M-b:** grafen läses om för swapparnas kurser så länge den förra omläsningen prövade fler priser, och swapparna på
  spåret får sina kurser först.
- **M-c:** jämförelsen inför toasten glöms när omläsningen ersattes eller väntar på en expansion.
## Vyerna Tidslinje (TL3a) och Flöde (FL1a) (2026-11-08)

- **Byggt:** vyn *Tidslinje* (Ctrl+2) med en bana per aktör, en tx som ett lodrätt streck i färgen för vad pengarna
  blev och tjockt som beloppet, dygn med tomma sträckor bortklippta (sicksack och antal dygn), täta tx samlade med en
  siffra som zoomar till timmar, ärendets egna händelser (egna etiketter, beslut om förslag och bevakningens träffar)
  på en egen bana, brickor som döljer sorter, listen med hela ärendet och inspektören med fakta ur kedjan, *PÅ BANAN*,
  bedömningen i en streckad ruta med *Varför?* och *Visa i grafen*. Vyn *Flöde* (Ctrl+3) som Sankey: en kolumn per
  hopp, trådar i färgen för var pengarna till sist hamnade, grå trådar för pengar som inte är offrens, stumpen för det
  som ligger kvar, randigt och streckat bortom något osäkert, metoden som räknar om delningen, *FRÅN OFFREN*, vägarna
  med tx:erna och underlaget, *Varför?* och *Visa vägen i grafen*. Båda läser utanför UI-tråden bara medan de syns,
  skriver inget i ärendet och visar ärligt när det inte finns något. `Graph/TimelineReading`, `TimelineScale`,
  `TimelineLayout`, `TimelinePicture`, `FlowReading`, `FlowLayout`; vymodellerna `TimelineViewModel` och
  `FlowViewModel`; kontrollerna `TimelineChart`, `TimelineStripView` och `FlowChart`. Guiden har avsnitten *Vyn
  Tidslinje* och *Flödet*. 26 nya tester i `TimelineViewTests` och `FlowViewTests`.
- **Egna beslut:** flödet är en egen analys, `flow v1`, som läser varje aktör som en plånbok och följer grafens
  överföringar i tidsordning med Tracings metod (FIFO eller haircut); varje del behåller sin väg, så att tråden kan
  färgas efter var den hamnade. Offren är startpunkterna och aktörerna med etiketten offer; pengar från någon annan
  som når ett offer räknas som offrets, eftersom spåret börjar där. Pengar som aldrig möter offrens tas inte med.
  En aktör med fler än 120 delar slår ihop de minsta (och vyn säger det), och delar under 0,2 % ritas inte men räknas.
  Kronorna i flödet räknas per del ur kursen på dess första linje och visas bara när varje del har en.
  Tidslinjens banor ordnas efter hopp och sedan första händelse; ett tomt avsnitt längre än två dygn klipps bort.
  Besluten läses ur `ChecksViewModel.Stored`, bevakningens träffar ur `WatchBoard`.
- **Avsteg från skisserna:** metodens val har bara FIFO och Haircut, eftersom LIFO och poison inte finns i Tracing
  (TODO, fråga 1 till kryptospåraren). Avgiften är ingen egen tråd: den ligger kvar hos den som betalade. Tidslinjens
  bevakningsträff har bevakningens egen turkos ton (`Brush.Watch`) i stället för skissens dova guld. En obekräftad tx
  står vid dygnens högra kant, streckad. *Varför?* i tidslinjen öppnar Guidens nya avsnitt i stället för klustringens.
- **Kvar:** se `docs/BACKLOG.md`, *Tidslinje och Flöde, efter bygget*.


## Omgång 6: säkerhet, ärenden och utgångar (backloggen, 2026-11-08)

- **Hela filen förseglad (Cases):** `.cut` har nu format 2: efter databasen står ett sigill, `CHSWSEAL` och
  HMAC-SHA256 under en nyckel ur filnyckeln och ärendets id (`CaseFileSeal`), över prefixet, huvudet med alla kuvert
  (också okända) och databasen. `CaseFiles.Extract` kontrollerar sigillet innan arbetskopian får sitt namn; en fil
  vars sigill inte håller (en äldre sida inbytt, ett kuvert ändrat utan nyckeln, en avkortad fil, något tillagt
  efter) är `Corrupt` och blir ingen arbetskopia. Format 1 vägras som `OutdatedFormat` i stället för att läsas utan
  sigill, eftersom en fil annars kunde ha fått sitt sigill borttaget och kallas 1 (gamla ärenden får avvisas före 1.0).
  En återställd arbetskopia kontrolleras som förut mot den förseglade filens SHA-256.
- **Gränsen för kuvert:** `SetSharingPassword` och `AddOperatorAccess` vägrar med `TooManyEnvelopes` innan något
  journalförs, när huvudet redan har 64 kuvert (ett delningslösenord får ersätta det som finns).
- **Unicode-normalisering:** huvudnyckeln och delningslösenordet härleds i NFC (`SecretText` i SharedKernel, i en
  fäst `char[]` som nollställs). Ett valv utan `normalization` i `kdf` (skrivet före) provas som det skrivs och sedan
  i NFC, och lindas om i NFC vid upplåsningen, som `StrengthenIfWeak`; ingen låses ute. Delningskuverten behöver ingen
  migrering, eftersom filer i format 1 inte öppnas.
- **Klartext i minnet:** valvets innehåll serialiseras direkt in i en fäst buffert som nollställs
  (`PinnedBufferWriter`). API-nycklarna och profilen är fortfarande `string` medan valvet är upplåst (BACKLOG).
- **Sparning som en låsning hinner före:** `Save` kastar `LockedWhileSaving` efter skrivningen när låsningen kom under
  den: filen är sparad, men ärendet stängs.
- **Ankarna:** ankarfilen har en generation som HMAC:en täcker, och valvet minns den senaste genom
  `IVaultSecrets` (`cases/anchors/<namn ur ankarnyckeln>`). En ankarfil som är äldre än valvet säger
  (`RolledBack`) eller saknas fast valvet har skrivit ankare (också utan eget kuvert) journalförs och ersätts, och
  `/verify` räknar äldre ankare som oläsliga. Varje `case.anchors-replaced` har nu kedjornas huvuden (`heads`) i
  detaljerna, så också *Unreadable* säger hur långt ärendet hade kommit. Filen skrivs före valvet, så att en krasch
  emellan lämnar filen före valvet (godtas). Utan `IVaultSecrets` (en värd utan valvets hemligheter) kontrolleras
  ankarna som förut. Gränsen: den som också kan lägga tillbaka en äldre valvfil hindras inte; rapportens topphash är
  ankaret utanför datorn.
- **Återställningskuverten:** varje nytt kuvert har en `check`, HMAC-SHA256 under en nyckel ur filnyckeln över
  kuvertets fält. Ett kuvert för den inbyggda nyckeln vars check inte håller räknas inte som täckt: ett nytt läggs
  till och de skadade tas bort, med en rad i journalen (`replaced`). `check --key` i verktyget provar den privata
  nyckeln på riktigt och sigillet över filen (`ICaseStore.TryRecoveryKey`), utan att ändra något.
- **Verktyget `Chainsaw.Recovery`:** `revoke <publik.pem> [--to <fil>]` skriver raden `REVOKED <fingeravtryck>` ur
  nyckeln själv (och vägrar filens egen nyckel); den privata nyckelns fil får på Windows en egen ACL (bara ägaren,
  inget arv) när den skapas; omdirigerad indata läses tecken för tecken, aldrig som `string`. `RecoveryPublicKey`
  kontrollerar själv att punkten ligger på P-384 (fält och kurvekvation), före plattformen.
- **Kuvertens namn (ÄR1b):** ett valvs kuvert bär operatörens namn krypterat med AES-GCM under en nyckel ur
  filnyckeln och bundet till kuvertets inlindade nyckel (`EnvelopeLabels`). Den som inte kan öppna filen ser inget
  namn, men kollegans kort i *Kuvert* visar namnet. Ett okänt kuvert visar sin sort (kortad, utan styrtecken).
- **Var grenarna gick isär (ÄR1b):** `FindConflict` jämför journalerna i kopior av filen och arbetskopian (med valvets
  nyckel; ingen av dem rörs, kopiorna tas bort) och ger sista gemensamma raden, dess hash och antalet rader efter på
  varje gren. Grenbilden säger *Gick isär vid rad N* med hashen på hovring och *+N rader* vid varje gren.
- **Stora ärenden:** ett ärende som bara lästs skrivs inte om när det stängs. Att öppna kopierar fortfarande hela
  databasen (mäts när ärendena blir stora, BACKLOG).
- **Utgångar (Network):**
  - **Hjälpprogrammets bevis:** innan något av SOCKS5 sägs skickar Chainsaw 32 slumpade byte och kräver
    HMAC-SHA256 under tunnelns lösenord tillbaka (`ProveAsync`, `prove` i `socks.go`). Luckan när ett kraschat
    hjälpprogram släpper porten innan Windows räknar det som avslutat är stängd, och garantin gäller på alla system.
  - **Tunnelns IPv6:** ett namn i `Endpoint` med både IPv4 och IPv6 provas med IPv4 och sedan IPv6, med halva
    väntetiden var (`handshakeOnAny`), så startgränsen på 30 s håller. Hjälpprogrammet är ombyggt med Go 1.27.3.
  - **Certifikatkedjor:** mellanliggande certifikat som datorn redan har (Windows lager *Intermediate Certification
    Authorities*) erbjuds kedjan, utan att något hämtas; spärrade (Disallowed) tas bort. En källa som utelämnar en
    mellannivå datorn aldrig sett når man fortfarande inte.
  - **Namn bakom systemets proxy:** med Windows och en proxy slås källans namn upp på datorn först, och en adress (eller
    ett namn med någon adress) på datorn eller ett privat nät vägras (`ProxiedNameGuard`). Ett namn datorn inte
    känner lämnas till proxyn.
  - **Felen genom Windows** säger *datorns nät* och *Vägen ut byttes* i stället för utgången (`ExitException.Route`).
  - **Den vägrade raden** i en WireGuard-fil följer med till rutan: *Filen godtas inte (rad 4)*.
  - **Svarshuvuden:** `ExitResponse.ServerDate` och `ETag`.
  - **Stoppen vid avslut:** `Dispose` väntar (inom samma gräns) på operationen i tur, så en tunnel som ett försök
    lämnar över efter avslutet är stoppad innan `Dispose` returnerar.
  - **Namnuppslag med Windows** är meningen med valet och står kvar som det är (punkten borttagen ur BACKLOG).
- **Appen:**
  - **Pipen:** lyssnar från första stund (före värden) och håller länkarna tills mottagaren finns (`MessageHold`, högst
    16); nästa instans av pipen skapas innan en ansluten avsändare läses, så att namnet aldrig står utan en instans.
  - **Svar efter en låsning:** ett kommando eller en skapelse som pågick när Chainsaw låstes svarar inte efter
    upplåsningen (en räknare per låsning i skalet och panelen).
  - **En länk medan raden har text** väntar och kommer in när raden töms.
  - **Avslut mitt i en öppning eller skapelse:** `OnExit` väntar (högst 15 s, med UI-tråden igång) på arbetet i
    `CaseWorkflow.Running` innan ärendet sparas.
  - **Ctrl+U** fäller ut och in utgångsrutan; verktygstipset säger det.
- **Avsteg och det som återstår:** se BACKLOG (det som väntar på beslut eller hör till andra omgångar) och TODO
  avsnitt 4 (det som behöver skissas). *Vem som förseglade en post* är inte byggd: varje moduls sätt att försegla
  måste ändras, vilket görs när ingen annan omgång arbetar i modulerna.
- **Granskningen (inga C, ett I, tre M), rättad:**
  - **I1:** en arbetskopia som tas tillbaka kontrolleras nu också mot sigillet: filens hash räknas i samma läsning
    som sigillet (`CaseFiles.SealedHash`), och först därefter jämförs den med basen. Ett huvud som ändrats utan nyckeln
    med en egen hash i basen är `Corrupt`, och arbetskopian ligger kvar.
  - **M1:** huvudet som läses i samma läsning som sigillet måste vara det som nyckeln öppnades ur
    (`CaseContainer.Same`), både vid uttaget och vid återtagandet.
  - **M2:** ett ensamt surrogattecken blir U+FFFD före normaliseringen i stället för ett undantag; PBKDF2 vägrade
    sådana förut, så inget äldre valv kan ha en sådan nyckel. Ett försök med en sådan nyckel mot ett äldre valv ger
    *fel huvudnyckel*, och `StrengthenIfWeak` behåller den gamla filen vid fel.
  - **M3:** `IVaultSecrets` krävs av `AddCasesModule` (`GetRequiredService`); bevakningsprocessen registrerar redan sin
    egen, och testernas värdar har fått en i minnet.

## Låsskärmen LS1b, bandet ID8f och felfärgen (2026-11-08)

Byggt efter skisserna ID8f och LS1b (omgång 2) och användarens beslut samma dag.

- **Figuren:** `Figure/ChainBand.cs` ersätter den diagonala `DnaFigure`: två kättingar och spiralen vågrätt under
  formuläret, axeln 204 px ovanför nederkanten (högst 55 % ner på en låg sida). Spiralens vridning rundas till hela varv,
  så att den högra kättingen står upprätt när den vridits klart; under vridningen snurrar den runt sin axel
  (`RightSquash`). Strängarna tonas i 16 steg efter djupet som förut.
- **Rörelsen:** `Figure/BandMotion.cs` (utan WPF, testad): starten (kättingarna glider in, darr, släpper med en fjäder),
  det strama repet (tre svängningar av en sträng fäst 60 px utanför fönstrets kanter, 2,2 Hz, ett häng på 7 px som gör
  att även ett ryck i sidled svänger bågen, högst 18 px per svängning), nyckelns svar och vägen till noden.
  `DnaParticles` följer bandet och har fått pusten (`Burst`) och basen för ett tecken (`LetGoBase`).
- **Ritaren:** `ChainFigure` ritar om bandet ungefär 60 gånger i sekunden medan det syns och läser fönstrets
  acceleration ur `Window.Left/Top`. Med animeringar avstängda står figuren i vila och timern vilar.
- **Formuläret LS1b:** loggan och namnet ovanför ett fält med ledtext; knappen *Lås upp · Enter* syns bara med text i
  fältet (dold med `Hidden`, så att inget hoppar); kanten är dämpad, accent med text och röd efter fel nyckel. *?* nere
  till vänster visar förklaringen, utgången, låsningen och en bevakningsträff. Hörnet säger *Återställningsnyckel ·
  commit* och öppnar Om Chainsaw som förut.
- **Vägen till skalet:** `MainWindow.ShowGate` låter låsskärmen ligga kvar över skalet när valvet öppnas från
  låsskärmen (`GateView.PlayUnlock`). Noden *Nytt ärende* hittas i grafens startsida (`GraphStartView.NewCaseNode`)
  efter en layout av skalet, och linsen i spiralens mitt blir den: bandet sträcks, spiralen vrids åt höger till nästa
  lins, linsen trycks ihop till en rundad rektangel, kättingarna och resten glider ut, och rektangeln krymper till
  nodens ram med dess yta under. Sedan tonar sidan bort och den riktiga noden syns under den ritade.
- **Felfärgen:** `Brush.Error` är `#FF6B5E` (klart rött, 4,6:1 eller mer mot botten, panelerna och det flytande),
  tonerna `ErrorTint` och `ErrorEdge` följer med, och `Color.ErrorPulse` `#D7322B` är låsskärmens puls (bara former,
  aldrig text). Där felfärgen betydde risk i grafen (motpart med negativ etikett i det som hölls tillbaka och i högen)
  används nu `Brush.Kind.Risk`.

**Egna beslut och avsteg från skissen:**
- Raderna under formuläret (utgången, låsningen, bevakningens träff) finns inte längre på sidan; de står under *?*, så
  att inget försvinner men sidan är ren. Bevakningens prick är borttagen (användarens beslut).
- Felraden *Fel huvudnyckel …* står kvar under fältet, eftersom den är ärlig och läses av skärmläsaren; skissen visade
  bara rycket och den röda kanten.
- Noden ritas inte med plus, namn och sonar i figuren: den riktiga noden syns när sidan tonar bort, så att de två aldrig
  skiljer sig. Står skalet inte på startsidan krymper linsen mot mitten.
- Förstagångsinställningen ligger nu ovanför bandet; dess högsta steg kan nå en bit ned i bandets övre del i ett lågt
  fönster.
- Partiklarna tonas inte röda vid pulsen (de ritas med färdiga tecken i accenten).

- **Risken också röd** (användarens beslut samma dag): `Kind.Risk` och `Area.Red` är `#FF5A4E` (4,95:1 mot botten,
  ljusare än accenten), `Kind.Sanctioned` är `#FF4545` och den streckade ramen vid slutet på spåret `#7A3B36`.

**Återstår:** att prova allt på Windows (`docs/TESTER-INFOR-1.0.md`). Risken och sanktionerad ligger nära varandra i
ljushet; sanktionerad skiljs fortfarande av sin markering i hörnet.

## Inspektören IN5a (2026-11-08)

Byggt efter skissen IN5a (användarens val), med mer luft mot skuggan än skissen.

- **Panelen under arbetet:** `Brush.Underlay` (`#1B1E21`) är inspektörens panel. I grafvyn ligger den först, och grafens
  egen botten (`Sheet`) ligger över den med rundat nedre hörn och en skugga åt höger. `GraphView.InspectorReach` och
  `InspectorReachChanged` säger fönstret hur bred panelen är, och `MainWindow.ShowSheet` låter titelraden sluta där:
  den får rundat hörn och en egen skugga, och under den syns panelens ton (`TitleUnderlay`). Fönstrets knappar ligger
  nu i ett eget lager i hörnet, på titelraden eller på panelen.
- **Innehållet:** sorten som liten rubrik i accentfärgen och versaler (`InspectorContent.Kicker`), namnet i 22 px,
  meningar om vad som hänt (`InspectorContent.NodeSummary`: in och ut i grafens tx, spårat in eller ingen spårning,
  och startpunkten), *Varför?* och *Historik* / *Fakta* som rundade knappar (ersätter flikarna), en tunn linje och
  sedan fakta, saldot, *Här* och adresserna. Vänsterkanten är 40 px, högerkanten 26 px.

**Egna beslut:** meningarna finns bara för noder; en linje och en kommentar säger det med rubriken och fakta. Antalet tx
i meningen är grafens (*i grafen*), inte adressens alla. Stängknappen står kvar sist på rubrikraden.

**Återstår:** prova på Windows, särskilt titelradens hörn och skugga mot panelen, och att fönstret går att dra i panelens
topp.

## Omgång 4: klustring, förvirring och kontroller (2026-11-08)

- **Klustringen** (Bitcoin):
  - `coinjoin v2`: Wasabi 2 känns igen på standardbeloppen (potenser av 2 och 3, 2·3ⁿ, 1, 2 och 5 gånger tiopotenser,
    5 000 satoshi till 2³⁷), minst hälften av utgångarna och minst tre olika belopp; Whirlpool har också Ashigarus
    pooler 0,025 och 0,25 BTC. Nytt mönster `CoinJoinPattern.Wasabi2`.
  - `change-address v2`: tecknet `SameFingerprintSpent` (version, sorts låsning, RBF): den output som spenderas av en
    tx med samma fingeravtryck, när de andra spenderas av en med ett annat. Spenderarna räknas till underlaget.
  - `peel-chain v2`: ett hopp får fyllas på med fler inputs (`PeelChainFinding.ExtraInputs`), och den output som nästa
    hopp spenderar går vidare även när den är den mindre. Förslaget gäller adressen som kom från hoppet före.
  - `GenericFinding`: en ny analys kan spara ett fynd med sort och JSON utan att Contracts ändras.
  - **En version dras tillbaka** (`IClusteringService.Withdraw`, `GetWithdrawals`, tabellen `clustering_withdrawals`,
    förseglad, schema 4): dess slutsatser räknas inte längre, inget tas bort, och den högsta version som inte dragits
    tillbaka räknas i stället. Journalraden `clustering.version-withdrawn`.
  - `GetConclusions` för många tx i en läsning; en körning som bara håller tillbaka något skriver inte i journalen.
- **Kontrollerna** (alla `CheckKind.Check`, Bitcoin, med område, *Varför?* och Guide-text):
  `service-behaviour v1` (svep av insättningsadresser till en hot wallet, utbetalning i batch), `coinjoin-merge v1`
  (mixade mynt och toxic change som spenderas tillsammans), `split-merge v1` (uppdelning i lika delar och delarna som
  möts igen, genom högst tre mellanled), `pass-through v1` (många hopp och fördröjning), `dust v1` (damm och
  adressförgiftning), `payjoin-hint v1` (en onödig input bland sammanslagna inputs), `active-hours v1` (aktiva timmar
  och trolig tidszon per ägare) och `peel-chain-area v2` (*Peel chain till börs*, påfyllda hopp).
  `CheckInput` har nu `Services` (aktörer med etiketten börs eller tjänst) och `Owners` (klustrets minsta adress).
- **Fynden under Kontroller:** en avgjord kandidat som analysen inte längre ger följer med ur det senast förseglade;
  besluten och resultaten läses om i skrivningen, så att ett beslut som förseglats under tiden får sin kandidat i
  listan; slutsatserna läses i en fråga för alla tx.
- **Appen:** Guidens ämnen för de nya analyserna (`GuideTopic`, `TopicOf`), ikonerna och färgerna i `AreaLooks`, och
  Guidens nya avsnitt *Tjänster ur beteende*, *PayJoin*, *Damm och adressförgiftning* och *Tidsmönster*; *CoinJoin*,
  *Växeladress*, *Peel chain*, *Uppdelning*, *Fördröjning och många hopp* och *Börser* uppdaterade.
- **Tester:** facit i `RoundFourTests` (Clustering, 13) och `RoundFourChecksTests` (Checks, 19), med en liknande adress
  som malts fram så att de fyra första och sista tecknen efter `bc1q` är lika. Golden-filen i Tracing är omskriven för
  de nya versionerna (bara versionerna och växelns nya underlag skiljer).
- **Egna beslut:** alla nya analyser är kontroller, inte förslag, eftersom inget i KF2b ännu kan göra något av ett
  godkännande av dem (en etikett ur en trolig tjänst kräver en form, TODO [skiss]). `coinjoin-merge` räknar bara
  kända CoinJoins, inte möjliga, som kan vara batchutbetalningar. `service-behaviour` säger *trolig tjänst*, aldrig
  *börs*. Gränsvärdena står som frågor till kryptospåraren (6b).
- **Skriver i ärendet** (granskas oberoende): klustringens nya versioner förseglar nya resultat, tillbakadragandet är
  en ny förseglad tabell med journalrad, de nya kontrollerna förseglas som de andra, och `CheckService.Run` läser och
  förseglar nu besluten och resultaten i samma skrivning.
- **Avsteg och kvar:** jämna belopp i fiat (kräver kurser i kontrollerna), centrala mixers med kända avgifter (kräver
  en källa), JoinMarkets avgifter och fidelity bonds, adressens hela historik från källan och klustringens cache står
  kvar i BACKLOG. Ytor (dra tillbaka en version, slutsatser som hålls tillbaka, tona ned damm, fälla ihop hopp, en
  trolig tjänst som etikett) står i TODO avsnitt 4 som [skiss], och testärendet som [direkt].
- **Rättat efter den oberoende granskningen** (1 C, 4 I, 7 M, alla rättade):
  - **C1:** fingeravtrycket räknas aldrig för en automatisk sammanslagning (bara de starka tecknen mot
    `AutomaticMergeSigns`); det kan bara avgöra lika läge eller stärka ett förslag, och *Varför?* säger det. Test med
    offret och bedragaren ur granskningen.
  - **I1:** `peel-chain v2` följer den mindre outputen bara när ärendet vet att ett hopp av samma form (efter storlek)
    spenderar den och att en tx som inte är ett hopp spenderar den större; en betalning följs aldrig bara för att den
    är den enda kända spenderingen. Test med offret som betalar bedragaren.
  - **I2:** de äldre versionerna (`coinjoin`, `change-address`, `peel-chain` v1) är registrerade, och ett ärende kör
    den högsta version det inte dragit tillbaka. Ett förslag som vägdes mot en tillbakadragen version räknas som
    förslag (aldrig stoppat av den) och vägs om vid nästa analys. `Withdraw` tar bara registrerade versioner och aldrig
    den sista i kraft; `TakeBackWithdrawal` tar tillbaka en tillbakadragning som egen förseglad post
    (`clustering_withdrawal_undos`, journalrad `clustering.withdrawal-taken-back`). Ingen yta (KV1 skissas).
  - **I3:** `service-behaviour` kräver minst två svep, tio insättningsadresser från tio oberoende avsändare och minst
    ett dygn, och räknar aldrig adresser som en startpunkt eller ett offer (etikett) betalade (`CheckInput.Protected`,
    offren ur Attribution när modulen finns). Texten säger *kan gälla dem*. Test med tre offer som bedragaren sveper.
  - **I4:** `active-hours` kräver tjugo tx och ett permutationstest med fast frö (2 000 dragningar, högst 1 % chans).
    Test med slumpade tider (fast frö) och 100 slumpade ägare.
  - **M1:** spenderarna är underlag för växeln bara när fingeravtryckets tecken håller; peel chains spenderare bara när
    de kan avgöra. Golden-filen har inga dubbla rader längre. **M2:** Wasabi 2 kräver minst två belopp utanför
    1-2-5-serien. **M3:** peel chains avgörande spenderare är underlag. **M4:** `FindingTxids` borttagen, det senast
    förseglade avkodas bara för fynd med beslut, och kandidaterna hålls om bara när något förseglats under tiden.
    **M5:** `GenericFinding` skrivs och läses tillbaka i ett test. **M6:** `active-hours` nycklas på adressen som
    spenderade först och `pass-through` på första hoppet, stabila när spåret följs framåt. **M7:** damm av bara 330 och
    546 satoshi heter *Damm eller inskriptioner*; PayJoin är nu en klustringsanalys som kontrollen visar.
  - **Andra granskningen (I):** `payjoin` håller sammanslagningar som förslag bara vid starka tecken (`payjoin v2`:
    två eller tre inputs, mottagarens input av en adresstyp som ingen annan input har men som exakt en output har, den
    outputen större än inputen, och inputen onödig). Den breda regeln (en onödig input) ändrar aldrig klustringen och
    syns bara som *Onödig input*. `payjoin v1` noterar utan veto, så ett ärende stänger av vetot genom att dra
    tillbaka version 2. Tester åt båda hållen: en riktig PayJoin blir förslag, granskarens prob (bedragaren som betalar
    en börs från tre insättningar) och en vanlig plånboks betalning med ett extra mynt slås ihop.

<<<<<<< HEAD
## Omgång 3: Bitcoin-källan, bevakningen och bakgrunden (backloggen, 2026-11-08)

- **VPN i bakgrunden** (användarens beslut 2026-11-08): `IBackgroundExits` i Network håller en kopia av en utgångs post
  ur valvet (namn, plats och WireGuard-fil) under DPAPI i `Bevakning\exits.bin`, bara för utgångar användaren tillåter,
  och programmets valda utgång. Bakgrundsprocessen läser utgångarna bara därifrån (`NetworkOptions.ExitsFromBackground`,
  `BackgroundExitSecrets`), så att samma `ExitConnection`, hjälpprogram och SHA-256-kontroll startar tunneln utan valvet.
  Den ansluter bara den valda utgången när den är tillåten, högst en gång i minuten efter ett misslyckande, kopplar ner
  när tillåtelsen tas tillbaka eller en annan utgång väljs, och gör inga uppslag utan ansluten utgång (princip 4).
  Kopian tas bort när utgången tas bort (`ExitStore.RemoveAsync`), när reglaget slås av och när bakgrunden slås av
  (`BackgroundLink`), och följer namnbyte och val. `BackgroundLink` startar bakgrunden också med VPN när den valda
  utgången är tillåten. Reglaget är en enkel rad under Nätverk (*Bevaka genom utgången när Chainsaw är stängt*).
- **Nya block i realtid:** `IExitWebSocket` (Network) öppnar bara `wss`, genom samma utgång som `IExitHttp`, med samma
  User-Agent och utan kakor, och stängs när utgången går. `IBlockNews` (Chains) frågar en Esplora-endpoints
  `/api/v1/ws` bara efter `blocks` och räknar nya block; en endpoint utan websocket (Blockstream) hoppas över en timme.
  `WatchRunner` och bakgrunden frågar höjden genast när ett nytt block hörts, i stället för att vänta på minuten, och
  slutar lyssna när grinden stängs, ärendet stängs eller utgången saknas. Nyheten sparas aldrig och tros aldrig: varje
  kontroll frågar som förut och förseglar svaren (princip 1). Reserven är de vanliga frågorna en gång i minuten.
- **Bevakningen:** höjden en bevakning startar och räknar från kommer bara från en endpoint i fas
  (`GetTipHeightAsync(…, LookupMode.InStep)`; källor utan valet vägrar `InStep` i stället för att strunta i det). En
  träff vars tx hittas i ett annat block förseglas igen med det nya blocket under journalraden `monitoring.hit.moved`
  (*… förut i block N (omorganisation)*); `GetHits` ger träffen en gång, med `MovedFrom`.
- **Bakgrundens kö:** tysta kontroller (inga träffar) slås ihop till en post per bevakning och körning, med varje svar
  kvar (inget som hämtats släpps), högst 600 svar per post.
- **Bitcoin-källan:** provet vid start frågar också blockets hash vid höjden (KL2b; en hash som inte kommer fäller inte
  provet); *finns inte* bara från endpoints som inte går att lita på blir *misslyckades* med orsaken; *Prova* på en
  adress som redan är en endpoint går i dess takt; huvudets mål måste ligga inom nätets gräns (powLimit, mainnet
  `0x1d00ffff`, testnät signets `0x1e0377ae`) och beskrivningens `bits` och `nonce` vara huvudets; en beskrivning som
  motsäger huvudet hämtas igen i stället för att vägras för gott; `VerifyInBlockAsync` kontrollerar med
  `/tx/:txid/merkle-proof` att tx:en ligger i blocket (roten räknas av Chainsaw och jämförs med huvudets);
  svaret om spenderade utgångar jämförs med det förra och en ändring sägs.
- **Tester:** `BackgroundExitsTests`, `ExitWebSocketTests` (Network), `BlockNewsTests`, `MerkleProofTests` (block 170 och
  ett syntetiskt block), `SpendChangeTests` och nya fall i `VerificationTests`, `EndpointPaceTests`,
  `EndpointPhaseAndLocalTests` och `ChainSourcesTests` (Chains), `VpnInBackgroundTests` och nya fall i
  `WatcherLoopTests` (Watcher), omorganisationen och höjden i fas (Monitoring), `BackgroundLinkTests` och
  `WatchRunnerTests` (App). Inget test når nätet.
- **Egna beslut:** websocketen frågar bara efter block, aldrig `track-address`, eftersom det skickar de bevakade
  adresserna tillsammans över en anslutning (BACKLOG). Bakgrunden går bara genom den valda utgången, aldrig en annan
  tillåten. De inspelade testblocken är brutna i regtest-svårighet, så Chains-testernas harness slår av gränsen
  (`BitcoinSourceOptions.CheckWorkLimit`, internal); gränsen prövas för sig mot Bitcoins första block.
- **Skriver i ärendet** (granskas oberoende): `monitoring.hit.moved` och en ny träffpost i `monitoring.hits`;
  merkle-beviset förseglas som `bitcoin.merkle-proof`; provets hash och den nya hämtningen av en motsagd blockbeskrivning
  förseglas som andra svar; bakgrundens hopslagna köposter förs in som förut. Princip 4: `exits.bin`, websocketen och
  bakgrundens tunnel.
- **Kvar:** ytorna står i TODO (*Ur omgång 3*, [skiss]); i BACKLOG med en notering: den egna noden utan utgång, ett
  behållet prov som åldras, ärenden som flyttats ur planen, sammanslagning av lika svar, luckor hos mycket aktiva
  adresser, svårigheten på höjden, realtid för Ethereum och egen nod, och resten under Bitcoin-källan (prestanda,
  jämförelse mellan källor, svarshuvuden).
- **Rättat efter den oberoende granskningen** (0 C, 2 I, 6 M, alla rättade):
  - **I1:** `Forbid` stänger säkert: en `exits.bin` som inte går att läsa eller ändra tas bort hel, och kastar bara om
    inte heller det går; `RemoveAsync` släpper också den valda utgången i kopian (`Follow(null)`); reglaget går efter
    användarens avsikt. Tester med en trasig fil.
  - **I2:** `InBlockVerified` är ersatt av `InBlockCheck` (`NotChecked`, `SourceOnly`, `ConfirmedElsewhere`): blockets
    hash vid höjden frågas av en annan endpoint i fas (`FetchAsync(…, except:)`, `bitcoin.block-height`), och det som
    bara vilar på en källa sägs så. powLimit beskrivs ärligt (svårighet 1 säger inget om kedjan). Kontrollpunkter i koden
    står i BACKLOG.
  - **M1:** `VerifyInBlockAsync` kontrollerar bytes först, vägrar en tx på 64 bytes utan vittnesdata, kräver att
    bevisets djup och plats passar blockets antal tx (blocket kontrollerat mot sitt huvud) och vägrar en dubblerad nod
    som egen plats (CVE-2012-2459).
  - **M2:** testnet har mainnets gräns `0x1d00ffff`; signet (eget värde saknas) står i BACKLOG.
  - **M3:** nyheterna knuffar högst var 30:e sekund, tror inte på ett hopp över tre block eller en omöjlig höjd, och
    en misslyckad höjd håller sin minut oavsett nyheter.
  - **M4:** en träff flyttas bara på en fullständig kontroll i fas och högst en gång per tio minuter (dämpning).
  - **M5:** en hopslagen post skrivs om under samma id med den senaste kontrollens tid; finns två filer efter en krasch
    läses den nyaste och båda tas bort vid införandet.
  - **M6:** `exits.bin` tas bort när valvet skapas på nytt (`FirstRunViewModel`).
  - **Omgranskningen** (0 C, 0 I, 4 M): M7 bekräftelsen kräver en annan värd och säger vilken endpoint; M9 en
    hopslagen post får ett nytt id (det äldre kan ha förts in); M10 den första höjden från en socket hålls mot
    provens högsta och knuffar inget utan referens. M8 och resten av M7 står i BACKLOG.
## Låsskärmens figur flyter (2026-11-08)

Användaren provade på Windows: kedjan ryckte konstigt när den kom in, och rörelsen kändes som 25 bilder i sekunden.

- **Takten:** `ChainFigure` följer nu skärmens bildrutor (`CompositionTarget.Rendering`, ett steg per ny `RenderingTime`)
  i stället för en `DispatcherTimer` på 16 ms, som i Windows tickar i steg om ungefär 15,6 ms och därför gav ojämna
  rutor (16 eller 31 ms).
- **Ingen layout per ruta:** varje ruta spelas in i en `DrawingGroup` som elementet visar en gång, i stället för
  `InvalidateVisual`, som begär en ny layout.
- **Färre lager:** partiklarna ritas med styrkan i tecknets egen färg (24 steg, gjorda en gång) i stället för en
  `PushOpacity` per tecken; länkar, strängar och steg får ett lager bara när de tonar. Pennorna görs en gång per
  nyans, ton och bredd.
- **Bandet räknas ut en gång per ruta** (`BandMotion.Current`) och återanvänds när rutan ritas. Modellen tar ungefär
  0,5 ms per ruta på en sida som är 1920 px bred.
- **Partiklarna ritas som former**, inte som text: WPF lägger text på hela bildpunkter, så ett tecken som driver några
  bildpunkter i sekunden hoppade en bildpunkt med några rutors mellanrum och såg ut som 25 bilder i sekunden när den
  stora animationen var klar. Tecknens former (`FormattedText.BuildGeometry`) görs en gång och står mellan
  bildpunkterna; styrkan ligger i penseln.
- **Darret** när den långa länken spänns är lugnare: 8 svängningar i sekunden och 0,9 px i stället för 15 och 1,4 px,
  som vid 60 bilder i sekunden bara fick fyra bilder per svängning och såg ut som skakigt brus.

## Panelerna under arbetet (2026-11-08)

Användarens krav: allt som står på inspektörens plats ska ligga under arbetet som IN5a.

- **`Controls/UnderPanel`** är en `Decorator` som ritar panelens ton (`Brush.Underlay`), bladets skugga längs
  vänsterkanten (en toning, inte en effekt, så att texten förblir skarp) och bladets rundade hörn vid foten, och
  lägger innehållet med 40 px luft till vänster. De paneler som visas räknas, och `UnderPanel.Reach` säger hur långt in
  från fönstrets högerkant de når; `MainWindow.ShowSheet` tar det största av det och grafens inspektör, så att
  titelraden slutar där panelen börjar och fönstrets knappar står på den.
- **Ombyggda:** Guidens panel (GD1b), vyn Bevakningens valda bevakning, Tidslinjens inspektör, Flödets och
  Exponeringens sidor och Etiketternas valda etikett. Vyernas sidor var kort inne i marginalen; nu står de i kanten i
  full höjd, och vyns övriga innehåll har kvar sin luft upptill och nedtill. Förslagspanelen (KF2b) står redan i
  grafens inspektör och har fått panelens ton och luften.

**Egna beslut:** Nytt ärende (NC2a) och dialogerna mörkar resten och ligger över arbetet; de är oförändrade.
Begärans och Rapportens förhandsvisningar är dokument, inte inspektörer, och står kvar som kort.


## Kodgranskningen i sju varv (2026-11-08)

Användaren bad om en fullständig granskning med minst sex varv: semantiska fel, säkerhet och kodkvalitet med *less is
more*. Varje varv kördes av parallella granskande agenter, som rättade C och I med tester; huvudsessionen byggde och
testade varje del ovanpå grenen och pushade (commits *Audit round 1 … 6*). Under granskningen pushade en annan session
drygt 250 commits till samma gren; varv 1 granskade därför den äldre koden och varv 2 hela den nya (ungefär 150 000
rader).

- **Varv 1, per modul:** bland annat en fientlig kö från bakgrundsprocessen som kunde stoppa ärendets bevakning för
  gott eller försegla en träff filtret inte kunde ge (C), en sanktionslista daterad i framtiden som skuggade alla
  senare, en nyare heuristikversion som inte kunde ta tillbaka en äldre sammanslagning, `/verify` som kastade på en
  ändrad första post och en kvarlämnad SQLite-journal som spelades in i en ny arbetskopia.
- **Varv 2, per modul på den nya koden:** en endpoint med orimlig höjd eller fel genesis (testnet, eller en som ljuger)
  som fick alla svar att se djupa ut och dolde omorganiseringar; en Ethereum-expansion med förlorade tx som räknades
  som läst; exponeringen som vägrade vid en rundtur; grafen som byggdes i O(noder × linjer); Bryt upp som bara sparade
  den första valda adressen; zoom och panorering som kunde vägras för gott; råa felmeddelanden på ett tiotal ställen.
- **Varv 3, flöden tvärs över modulerna:** en ny titel som läste om alla vyer, en tillbakatagen etikettkopia som syntes
  kvar, samma token-träff två gånger under olika symbol, en borttagen bevakning som bakgrunden fortsatte med, uppslag
  genom Windows med VPN valt när planen inte kunde skrivas, högar på Ethereum i BTC, exporter av token utan kontrakt,
  och det instabila exporttestets orsak.
- **Varv 4, användarens tre beslut och säkerhet:** bakgrundens träffar är tips tills Chainsaws eget uppslag ger dem
  (`monitoring.tips`, grå och streckade medan de väntar); internetkontrollen bara med Windows som väg; en ändrad
  ärendefil skrivs aldrig över (arbetet sparas som gren); kopior utanför urklippshistoriken och molnet; filer inte i
  Senaste; planterade utlösare rapporteras av `/verify`; en länk kan inte visa ett falskt bevakningskort.
- **Varv 5, *less is more* och integritet:** omkring 400 rader död eller dubblerad kod borta med oförändrat beteende;
  indexhöjder räknas bara med ett förseglat svar bakom sig och under det kedjan kan ha nått; `/verify` prövar varje
  förseglat svar mot sin SHA-256; bakgrundens lästa höjd begränsas; ett försvagat delningslösenordskuvert vägras och tas
  bort med journalrad.
- **Varv 6, granskning av granskningen:** varje rättelse lästes om efter sammanslagningarna. Varv 5:s höjdfråga hade
  gjort cachade uppslag 100 gånger långsammare (rättad); Tidslinjen kopierade förbi urklippsskyddet (ny kod); bakgrundens
  starthöjd begränsas som den lästa höjden; exponeringen ordnar en kontos poster som spåret, så Graf och Exponering
  stämmer; det instabila högtestet berodde på testets trådar, inte produkten.
- **Varv 7:** varv 6:s gräns för bakgrundens starthöjd tog ärendets gamla höjd, så att ett år gamla pengar kunde
  bli träffar, eller tappade bakgrundens träff när ärendet inte sett kedjan. Nu väntar en sådan post i kön tills
  ärendet läst så långt, och rundan tar in kön igen efter höjden. Det instabila BackgroundLink-testet berodde på
  testets trådar.
- **Varv 8**, bara bakgrundens höjder, i 19 fall: en förfalskad kontroll med en tid i framtiden fick ärendets egen
  kontroll att aldrig köras (C). Rättelsen förenklar: bakgrunden flyttar aldrig hur långt ärendet läst eller när det
  kontrollerades; ärendets kontroll läser från sin egen senaste läsning, och bakgrundens träffar bekräftas som tips.
  En start golvas också när ärendet inte hade någon höjd då bevakningen gjordes, och väntar medan bevakningen är
  pausad.
- **Varv 9:** kontroll av varv 8 utan C eller I. Granskningen avslutades där.

**Användarens beslut under granskningen:** txid:er får stå i bakgrundens plan; bakgrundens träffar kontrolleras mot
kedjan innan de förseglas; internetkontrollen bara när Windows är vägen.

**Egna beslut:** en ändrad ärendefil på disken räknas som en konflikt (inte som ett beslut för användaren), eftersom
det säkra valet var givet; bakgrundens start- och lästa höjder följer användarens beslut att bakgrunden bara ger tips.

**Kvar:** frågorna 12–19 i TODO (*Från kodgranskningen*) och de mindre fynden i BACKLOG (*Kodgranskningen
2026-11-08*). Det som ska provas på Windows står i TESTER-INFOR.

## VT1a, LN1a och AK1a (2026-11-08): byt filnyckel, en länk till ett befintligt ärende och ankaret

Byggt efter skisserna VT1a, LN1a och AK1a (användarens val 2026-11-08).

- **VT1a, byt filnyckel** (`ICase.ReplaceFileKey`, `Case.FileKey.cs`): huvudnyckeln kontrolleras igen
  (`IVault.VerifyMasterKey`, nytt, med ett förval som kastar `NotSupportedException` så att testernas valv inte
  behöver ändras), ärendet sparas, arbetskopian kopieras till `.work.rekey` där journalraden
  `case.file-key-replaced` förseglas under den gamla nyckeln och kopian krypteras om (SQLite3 Multiple Ciphers rekey)
  under en ny slumpad filnyckel. Den nya filen (nytt huvud med ditt valvs kuvert, den inbyggda återställningsnyckelns
  och vid behov ett nytt ärendelösenord, kopian och sigillet under den nya nyckeln) skrivs bredvid, arbetskopian
  flyttas undan till `.work.old-key` och den nya filen tar den gamlas plats i en flytt. Sedan blir kopian
  arbetskopian och ärendet fortsätter under den nya nyckeln; ankarna flyttas fram som vid varje sparning.
  - **En krasch** före flytten lämnar den gamla filen hel (arbetskopian tas ut på nytt nästa gång), efter flytten den
    nya; det som blir kvar (`.rekey`, `.old-key`) tas bort vid nästa öppning. Ett fel före flytten lämnar ärendet
    öppet under den gamla nyckeln; ett fel efter flytten stänger det, och det öppnas från den nya filen.
  - **Testerna** (`FileKeyTests`) tar en bild av mappen vid varje steg, som en krasch skulle lämna den, och öppnar
    den: gamla filen till och med *set-aside*, nya från *replaced*. Kollegans valv, det gamla lösenordet och en
    ändrad byte (sigillet) öppnar inte; organisationens nyckel, ett nytt lösenord och en tidigare utlämnad kopia gör det.
  - **Fliken Kuvert:** *Byt filnyckel …* under korten (bara med eget kuvert), korten får *Lindas om* eller *Faller
    bort* (streckat och överstruket), bekräftelsen fälls ut under korten med huvudnyckeln och strömbrytaren för ett
    nytt ärendelösenord, och efteråt står en streckad rad med det som drogs tillbaka och journalraden (läst ur
    journalen, så den står kvar nästa gång).
- **LN1a:** `LinkInbox` säger när en länk börjar och slutar vänta (`WaitingChanged`), och låsskärmen säger då bara
  *En länk väntar*. Efter upplåsningen fälls ärendeväljaren ut med länken överst (`CasePickerViewModel.OpenForLink`);
  Enter eller ett andra klick öppnar ärendet och lägger adressen som startpunkt med raden
  `app.start-point-from-link` i journalen (`CaseWorkflow.AddLinkedStartPointAsync`), *Nytt ärende med adressen …*
  (Ctrl+N) öppnar NC2a med adressen, och Esc slänger länken. En länk medan Chainsaw är upplåst går som förut.
- **AK1a:** `AnchorReplacement` (Contracts) läser raden `case.anchors-replaced`. *Verifiera* säger *Hashkedjan är
  hel* med rader, tid och topp, och ett bytt ankare som en gul rad med *Troligast: …*; *Varför?* öppnar Guidens panel
  (`GuideAnchorChange`) med kedjan som prickar, före och efter och orsaken som en streckad bricka. `/verify` säger
  också att kedjan är hel och ankaret byttes, i stället för ett fel. Guideavsnittet `docs/guide/ankaret.md`.

**Egna beslut och avsteg från skisserna:**
- **Kuvert som faller bort:** också andra (icke inbyggda) återställningsnycklars och okända kuvert, eftersom Chainsaw
  inte kan linda den nya nyckeln åt dem; de skulle annars bära en nyckel som inte öppnar något.
- **Inga steg med förlopp** under bytet (skissens list med fyra steg): Cases säger inte hur långt det kommit, så en
  rad säger vad som görs och att ärendet är låst under tiden, i stället för påhittade steg.
- **Länkens ursprung:** skissen säger *Länk från Gawk*, men länken bär inte vilket program som skickade den, så det
  står *Länk från ett annat program*. Brickan *Etikett härifrån* (etikettbiblioteket per ärende) är inte byggd.
- **Två datorer** avgörs ur raderna: ett ankare längre fram än filens topp är en äldre kopia, annars två datorer.
  Raden där de gick isär (*samma rader fram till 402*) står inte, eftersom raden bara sparar ankaret och toppen.

**Granskningen** (oberoende, nyckeln, de gamla sidorna, varje kraschsteg och ankarna): inga C eller I, fyra M:
- **M1:** `/verify` svarar nu med en gul varning (`CommandReply.Warning`, nytt) när ankaret byttes av ett skäl i filen
  (`AnchorReplacement.ConcernsTheFile`: Diverged, RolledBack, okänt), och lugnt *klart* bara när det gäller datorn
  (ankaret saknades eller gick inte att läsa).
- **M2:** andra fel från valvet blir `CaseError.VaultFailed` (nytt), och ett SQLite-fel när journalraden skrivs i kopian
  blir `Corrupt`, så att fliken Kuvert säger det i stället för ett ohanterat fel.
- **M3:** den nya filen som skrivits bredvid (`.cut.tmp`, med den nya nyckeln och ett nytt lösenord) tas bort med de
  andra resterna. Fanns det rester från ett byte och är journalens sista rad inte bytet, säger öppningen det med raden
  `case.file-key-replacement-interrupted` och en varning som ligger kvar: filen har kvar sina gamla kuvert.
- **M4** står i BACKLOG under *Säkerhet* (den gamla databasnyckeln i bibliotekets minne).

**Återstår:** brickan *Etikett härifrån* i LN1a, och att prova allt på Windows.


## PX1a, UT4a och KL3b: proxy med inloggning, en ansluten utgång i läget Windows, den egna nodens certifikat (2026-11-08)

Byggt efter skisserna PX1a, UT4a och KL3b (användarens val samma dag). PX1a och KL3b rör princip 4 och väntar på en
oberoende granskning.

- **PX1a, proxyn under Windows** (`IWindowsProxy`, `WindowsProxyLogin`, `WinHttpProxyReader`, `ProxyLoginViewModel`):
  Chainsaw läser Windows proxyinställning som .NET själv gör (`WinHttpGetIEProxyConfigForCurrentUser`) och ändrar inget.
  Automatisk identifiering (WPAD) eller ett skript (PAC) gör inställningen *automatisk*, också när en proxy dessutom är
  inställd för hand, eftersom Windows då frågar nätet först. Bara en proxy inställd för hand får raden *Logga in med
  Windows-kontot*. Påslagen sparas adressen (`host:port`) i `settings.json` (`ProxyLoginFor`) och i bakgrundens plan.
  Röret genom Windows får då `DefaultProxyCredentials` som en grind: den svarar bara när proxyn som frågar (.NET skickar
  proxyns egen adress till `GetCredential`, provat mot en proxy på loopback) är exakt den adressen, bara medan Windows
  inställning fortfarande är just den proxyn inställd för hand (läses om vid varje fråga, så att ett byte till WPAD eller
  en annan adress gäller direkt), och bara för Negotiate, Kerberos och NTLM, där lösenordet aldrig lämnar datorn
  (Basic och Digest får inget). Källorna får aldrig några uppgifter (`Credentials` är fortsatt `null`). Att slå av,
  på eller flytta inloggningen bygger röret genom Windows på nytt, så att ingen inloggad anslutning står kvar.
  En `407` från proxyn noteras (`LoginAskedAt`, också från provet om datorn når internet) och sägs i raden; ett
  uppslag som får `407` ger `ExitError.ProxyLoginRequired`, som räknas som utgångens fel och aldrig märker en endpoint.
  Bevakningen i bakgrunden följer valet genom samma grind (`WatcherLoop.FollowProxyLogin`).
- **UT4a, en ansluten VPN-utgång i läget Windows** (`ExitPanelViewModel.ShowsExits`, `ExitRowViewModel.IsIdle` och
  `IsToned`): listan visas när en utgång är ansluten, tonad, och bara den anslutna raden har färg, en ihålig grön prick,
  *Ansluten · används inte* under namnet och strömknappen. Verktygstipset säger sedan när den är ansluten. När den
  kopplas ner försvinner listan; de andra raderna har ingen knapp.
- **KL3b, den egna nodens certifikat** (`CertificatePin`, `NodeCertificate`, `EndpointCertificate`,
  `ILocalHttp.ReadCertificateAsync`, `IChainSources.PinAsync`/`UnpinAsync`/`ReadCertificateAsync`/`CertificateRefused`,
  `NodePinViewModel`): fingeravtrycket är **SHA-256 av hela certifikatet** i DER-form, eftersom det är det Windows
  certifikatdialog, webbläsarna och `openssl x509 -fingerprint -sha256` visar, så att användaren kan jämföra det med
  det noden visar, och eftersom det fäster exakt det certifikat användaren tittade på; ett förnyat certifikat med samma
  nyckel blir då ett byte som syns och bekräftas, aldrig ett som går tyst. Bara endpoints märkta lokala fästs. Fästet
  sparas i `sources.json` med endpointen (inställningarna, aldrig ärendet), med vem och när, och de tidigare fästena
  (högst tio) med när och av vem de byttes. Varje uppslag till noden bär fästet i `ExitRequest.CertificatePin`, och
  `LocalHttp` har ett eget rör per fäste som godtar exakt det certifikatet, betrott av Windows eller inte, och vägrar
  alla andra innan något av förfrågan skickas (`ExitError.CertificateChanged`); utan fäste gäller Windows rötter som
  förut. `IExitHttp` vägrar en förfrågan med fäste. Att läsa certifikatet gör bara TLS-handskakningen, direkt till noden
  i det egna nätet. Ett byte sägs en gång per endpoint och fäste i toastern (*Egen nod vägras* med *Visa*), noden
  vägras tills *Lita på det nya …* har bekräftats, och de andra endpointarna används under tiden.

**Egna beslut och avsteg:**
- PX1a: raden under de tre utfällda raderna (vilken metod proxyn godtog och när) är inte byggd, eftersom .NET inte säger
  vilken metod som användes; rutan säger *Kerberos eller NTLM*. Utan proxy står bara raden *Proxy* med *Ingen*.
  Den automatiska proxyns adress visas inte (den blir känd först när en förfrågan frågar efter den); chipet säger
  *WPAD eller skript*. *Proxy i Windows* öppnar `ms-settings:network-proxy`.
- PX1a: en inloggning vars proxy har bytts i Windows slås av och sparas som av när sidan Nätverk visas; tills dess
  gäller den ändå inte (grinden läser om inställningen).
- KL3b: ett fäst certifikat godtas också när det har gått ut eller har ett annat namn: det är just det certifikatet som
  användaren litar på. *Ta bort fästet …* frågar först, som *Lita på det nya …*.

**Återstår:** att prova på Windows (`docs/TESTER-INFOR-1.0.md`), särskilt en riktig proxy med Kerberos och NTLM.

## Ytorna för omgång 4 (2026-11-08): KV1a, HB1a, BU1a, DM1a, HP1a och TJ1b

Byggt efter skisserna som valdes 2026-11-08 (andra omgången), på backend från omgång 4.

- **Clustering:** `PreviewWithdraw` räknar ut klustren före och efter ett tillbakadragande ur det som är sparat
  (`ClusterChanges`), utan att spara något. Det en körning håller tillbaka sparas som arbetsläge i
  `clustering_held_back` (ersätts av nästa körning som håller tillbaka något annat, inte förseglat eftersom det alltid
  kan räknas ut igen), och `GetHeldBack` ger slutsatserna med vad som saknades och senaste beslutet.
  `DecideHeldBack` förseglar *Låt stå* eller *Släpp slutsatsen* i den nya kedjan `clustering.held_back_decisions`
  (schema version 5) med en rad i journalen; adresserna sparas med beslutet så att historiken kan visa det också
  efter att slutsatsen släppts. En släppt tx:s slutsatser före beslutet räknas inte längre (som ett tillbakadragande
  för en tx), och nästa körning sparar det den kommer fram till nu.
- **KV1a:** `ClusterWhyViewModel` och `ClusterWhyView` i inspektören, `ChangeBarView` för staplarna, spöket under
  noden och nedtoningen i `GraphCanvas.Rounds`. *Ta tillbaka* i Historik och *Ångra* på toasten.
- **HB1a:** ringen och hovringen i `GraphCanvas.Rounds`, kortet i Historik (`HistoryUndo.DecideHeldBack`) med *Låt stå*
  bredvid *Släpp slutsatsen*.
- **BU1a:** `PairPath` (stegen, den svagaste länken och vad ett snitt bryter ut) och `InspectorFormsViewModel.Pair`.
  Ett snitt sparas som uppbrytningen av det som lämnar (`InspectorForms.BreakOut`), så att samma regler gäller som
  för att bryta ut adresser. Två noder valda med Ctrl+klick i grafen ger paret när U öppnas.
- **DM1a och HP1a:** `ObfuscationMarks` läser `dust v1` och `pass-through v1` ur de sparade kontrollerna;
  `GraphViewModel.Obfuscation` tonar ned, fäller ihop och frågar före kopiering. Bara visning, inget sparas.
- **TJ1b:** `InspectorFormsViewModel.Check` öppnar etikettformuläret från kontrollen med källan som
  `LabelSource.Behaviour` (`service-behaviour`, `v1`), förseglad av Attribution som andra etiketter.

**Egna beslut och avsteg från skisserna:**
- Enum-namnet `ConclusionChoice` (inte `HeldBackChoice`, som redan finns i appen för EX1).
- KV1a: bekräftelsen fälls ut under raderna i inspektören, inte fast längst ner; raden längst ner säger när men inte
  vem. Förhandsvisningen räknar på det som är sparat (TODO).
- HB1a: hovringen säger vad slutsatsen byggde på, inte vad som ersatte den (väntar på *Ersatt av vilken tx*).
- BU1a: Ctrl+klick sker i formulärets lista över adresser, eftersom klustret inte öppnas som kort i grafen här;
  varningen nås genom två valda noder i grafen.
- DM1a: jämförelsen står överst i inspektören (som skissen säger) och kopiering av den liknande lyser upp den, i
  stället för en egen ruta vid noden; dammets linjer är oförändrade.
- HP1a: utfällt står noderna på sina platser och *Fäll ihop* är en bricka ovanför raden.
- TJ1b: *Som etikett …* står i inspektörens rad, eftersom `service-behaviour` är en kontroll som inte står i
  KF2b-panelen (fråga i TODO); panelen tonas därför inte.
- Testärendet fylls inte på än (TODO).

**Tester:** Clustering 6 nya (förhandsvisningen, det som hålls tillbaka, *Låt stå*, *Släpp* och nästa körning),
App 14 nya i `RoundFourSurfacesTests`. Ett befintligt test räknar en läsning till av klustret (Varför?).

**Granskningens rättelser (4 I, 9 M):**
- **BU1a:** ett snitt räknas som klustringen gör: uppbrytningen vägrar hela förslaget (alla adresser i det), så
  klustret delas om tills inget mer faller bort, och allt som inte når adressen som stannar lämnar, i de plånböcker
  det faller isär i (`PairPath.Cut`).
- **TJ1b och etiketterna:** en etikett ur en analys av beteende sätts bara när användaren godkänner den, så den är
  användarens egen (`LabelSources.IsUsersOwn`): den kan tas tillbaka i Historik och överallt i biblioteket och märkas
  *Sprid inte*. Tre äldre tester som sa motsatsen är ändrade.
- **DM1a:** inspektörens kopieringsknapp frågar också vid den liknande adressen, och dess id-ruta visas inte.
- **KV1a:** bekräftelsen säger streckat att tx som den andra versionen aldrig analyserat syns efter nästa körning, och
  toasten att fler kan följa. *Dra tillbaka* erbjuds när en annan version står kvar. *Ångra* på toasten säger till
  när det redan var taget tillbaka.
- **HB1a:** besluten kan tas tillbaka med *Ångra* (`ReopenHeldBack`, ett nytt förseglat beslut med `undoes`). Det som
  saknades kontrolleras mot de förseglade slutsatsernas grund innan beslutet förseglas, så en ändrad arbetsrad kan
  inte förseglas. En tx med bara fynd (till exempel CoinJoin) visas också, och ett släpp säger att fynden följer med.
  Arbetsraden jämförs som mängd, så ordningen ensam skriver inget.
- **ClusterChanges** räknar också länkar som börjar eller slutar slå ihop utan att förslaget ändras (efter en
  uppbrytning). **HP1a** fäller bara bort noder vars alla linjer hör till raden.

## Omgång 7: vyerna och appen (2026-11-08)

Byggt ur BACKLOG och TODO (omgång 7, användarens beslut att allt i BACKLOG görs före 1.0).

- **Grafens prestanda vid tusentals noder.** `SceneLayout` håller platserna, kurvorna och beloppen med ett index efter
  plats (`GraphIndex`, celler på 256 enheter; en linje som når fler än 32 celler hålls åt sidan och prövas alltid).
  Hovring, klick och det som syns frågar indexet i stället för alla noder och linjer. Under ett drag räknas bara det om
  som beror på de dragna noderna: deras linjer, i hoppkolumnerna bara de linjer vars väg runt noderna tittade i en cell
  som noden lämnar eller kommer in i (`LineRouting.Route` säger vilka celler den tittade i), och de linjernas belopp
  mot ett rutnät som hålls kvar (`RoomGrid.Remove`). Kurvorna blir exakt desamma som när allt räknas om (test), och
  hela layouten räknas om när draget släpps, så att beloppen hittar sin plats igen. Ytan cachar områdena per
  platser och scen.
  - **Mätt** med ett påhittat ärende på 3 000 aktörer och 3 427 linjer (`GraphScaleTests.MeasureLargeGraph`, i
    molnmiljön, ms per gång, före → efter): ett steg i ett drag 19 → 0,5 på den fria ytan och 123 → 1,3 i
    hoppkolumnerna; en hovring 2–3 → under 0,5; det som syns 2–3 → 0,1–0,3. Hela layouten när draget släpps: 20 ms
    fritt och 100 ms i hoppkolumnerna (förut betalades det vid varje steg).
  - **Eget beslut:** beloppen som inte tillhör de dragna linjerna står kvar under draget, också där den dragna noden
    nu står, och flyttas först när draget släpps. **Kvar** (BACKLOG): hela layouten efter draget räknas på UI-tråden.
- **Exponering:**
  - **Mixerns troliga utgångar** (TODO): en godkänd kandidat (KF2b) ritas bortom tjänsten pengarna betalades till, på
    nästa ring eller bortom den sista (`ExposureLayout.Beyond`), som en streckad nod med en prickad linje och *förslag*
    i stället för andelen. Den räknas aldrig och går inte att välja. Kandidaterna läses ur kontrollerna
    (`ChecksViewModel.ApprovedPayouts`), också när panelen visar annat.
  - **Kontokedjan och länkade insättningar:** läst bakåt på Ethereum räknas den länkade insättningens del av det
    plånboken betalade vidare som *Fortsätter på annan kedja*, som på Bitcoin, i stället för *Kom före historiken*.
  - *Pengar i cirkel i Fått från* var redan rättat och testat (omgång 2) och är struket.
- **Rapport:** en godkänd trolig utbetalning ur en markerad aktör är en slutsats med analysens namn och version och
  tx:en, kandidaten med förkortad adress. Toasten efter exporten har *Visa i mappen* (`IFolderView`, Utforskaren med
  filen vald). *Ändra i grafen* ger *‹ Rapport Esc* i grafens huvud. Utan delen *Hashkedjornas topphash* bär utkastet
  ingen gammal kontroll, så exporten provar inte om i onödan och journalraden har ingen rad för kontrollen
  (`ReportService`). Den spårade andelen räknas utan att tokenbelopp görs om till `decimal`.
- **Kommentarsnoden:** lagret har en och samma transform, skuggan är delad och ritas i en bitmapp
  (`BitmapCache`), och en lapp som skrivs i när högen eller valvyn fälls ut släpper tangentbordet. En lapp som tappar
  musgreppet sparas där den står, och soptunnan krymper.
- **Valvyn:** de prickade guiderna görs en gång per vy och rombens hörnmärke är delat.
- **Vyerna:** *Visa i grafen* hittar ett kort i en hög och fäller ut högen; Exponering gör plats för Guidens panel.
- **Sökningen:** Enter på en rad *I soptunnan* lägger tillbaka den direkt (`TrashFilter.PlaceOf`), med tipset
  *Lägg tillbaka*.
- **Strukna som redan byggda eller inaktuella:** Nytt → Etikett (kopplad), fältets loop (teckenfältet finns inte
  längre), äldre ärenden före etapp 21 (schemat får ändras före 1.0).
- **Skriver i ärendet** (för den oberoende granskningen): rapportens export utan topphashen (journalradens detaljer,
  `ReportService.TryExport`), och Enter på en rad i soptunnan (en återläggning, samma förseglade väg som soptunnans
  vy). Exponeringens kontokedja och de godkända kandidaterna i rapporten och ringarna ändrar bara det som visas och
  exporteras.
- **Lämnat** (står kvar i BACKLOG med varför): `Compose` i Begäran utanför UI-tråden (testerna och vymodellen bygger på
  att det sker direkt), sanktionslistans import mot grafen (Attribution), testärendets nya delar (en annan omgång
  bygger in kontrollerna i berättelsen), att återuppta förstagångsinställningen efter en låsning, och gasavgifterna i
  plånboksläsningen.
- **Rättat efter granskningen** (0 C, 1 I, 5 M, alla rättade):
  - **I:** Enter på en sökrad i soptunnan lägger bara tillbaka det användaren själv slängde, under nodens egen nyckel,
    och bara när noden då syns igen (`TrashFilter.PlaceOf`); annars öppnas soptunnan med trädet. Tester med
    granskarens fall (klustret slängt, en adress slängd för sig och sedan sammanslagen).
  - **M2:** en sökrad vars plats i soptunnan inte längre gäller säger det och öppnar soptunnan
    (`GraphViewModel.PutBackFoundAsync`). **M3:** en nod som bara följde med en slängning öppnar soptunnan.
  - **M4:** flera troliga utgångar vid kanten flyttas som en grupp i stället för att klämmas en och en.
  - **M5:** på sidan *Fått från* heter resten *Kom från en tx som fortsatte på annan kedja*.
  - **M6:** en tokenmängd som är för stor för `decimal` ger spåret utan värde i stället för ett undantag, och en
    rapport som inte gick att bygga om säger det i en toast.

## Grafens fart (2026-11-08)

Användaren bad om så många bildrutor som möjligt i grafen. Inget provat på Windows än.

- **Scenen i ett eget lager:** allt som ritas på grafens enheter (hoppkolumner, tidsband, områden, linjer, belopp,
  bevakningar, noder och kort) ritas i en egen `DrawingVisual` över skärmen och en marginal runt den (en fjärdedel av
  bredden och höjden åt varje håll). Panorering, glidning och zoom ändrar bara lagrets transform
  (`GraphCanvas.MoveView`, utan ny layout), så renderingstråden får en matris i stället för hela grafen.
  `SceneWindow` avgör när scenen ritas om: när något ändras (`Redraw`), när skärmen lämnar det ritade, och när en
  zoom landar. Under själva zoomen skalas den färdiga ritningen.
- **Eget lager, inte `OnRender`:** WPF släpper det förra `OnRender` ritade innan det tar emot det nya, så en ritning
  som sparas mellan rutorna skickas ändå om i sin helhet om den ritas där. Det som står på skärmen över scenen
  (dygnens namn, rutan, blockmärkets ruta, valvyn) ritas i ett andra lager i varje ruta, och lapparna ligger
  överst som förut.
- **Inget görs per ruta som kan göras en gång:** temats penslar slås upp en gång och fryses (`Ink`), ikonerna slås
  upp en gång (`GlyphOf`), alla pennor i ritvägen är frysta och sparade (`PenOf`, `RoundPen`), texterna mäts en gång
  (`Text`, också nodernas namn, som förut mättes om i en slinga i varje ruta), och varje linjes kurva görs en gång.
  Textcachen rymmer 16 384 texter, så att ett stort ärende inte tömmer den varje ruta.
- **Avsteg:** en scen ritas om med marginal, så en ändring (hovring, val) ritar lite mer än skärmen. Medan något
  rör sig av sig självt (ringar, ett kort som vänds, timglaset, valvyn, högen) ritas utan marginal, som förut.
- **Återstår:** prova på Windows (`docs/TESTER-INFOR-1.0.md`, *Grafens fart*). Räcker det inte är nästa steg
  grafytan med SkiaSharp på grafikkortet (`docs/TODO.md`).

### Granskningen och den eventstyrda slingan (samma dag)

- **Granskningen** (inga C): lagren tar inte emot träfftester (WPF gick annars igenom hela scenen vid varje
  musrörelse), en ändring ritas utan marginal (nästa panorering tar den), ringar och timglas håller bildrutor bara
  när de syns på skärmen, och bevakningens vaktlinje använder den sparade kurvan.
- **En bildruteslinga** (användarens krav: strukturerat och eventstyrt): `FramePump` samlar det som begärs
  (`GraphChange`) och rörelserna (`GraphMotion`: glidning, zoom, sökningens glidning, rutans rullning, valvy och hög,
  ringar, kort). Varje rörelse är ett steg som flyttar sitt läge med tiden sedan förra steget och säger vad som
  ändrades. Den ersätter fem egna krokar på `CompositionTarget.Rendering` och två `DispatcherTimer`. Ritkoden noterar
  bara vad som rör sig, och slingan startar och stoppar rörelserna efter att scenen ritats. Pekarens rörelser under en
  panorering slås ihop till en per bildruta. Ett test läser källan och vaktar att ingen annan del håller bildrutor,
  kör timrar eller anropar `InvalidateVisual`.
- **Smått:** en kurvas gränser räknas utan att fyra punkter läggs i en lista (det görs för varje linje i varje
  träfftest och varje scen).
- **Återstår:** när en nod dras ritas hela scenen om i varje ruta. Nästa steg är ett eget draglager för de dragna
  noderna och deras linjer, så att resten står still.

### Draglagret och ingen förlust i kvalitet (samma dag)

Användarens krav: optimeringarna får inte minska kvaliteten alls.

- **Zoomen ritas exakt i varje ruta igen.** Den skalade förut den färdiga ritningen under svepet, så att ikonernas
  linjer blev en aning tjockare eller tunnare. Ikonernas linjebredd avrundas inte längre. Bara panorering och
  glidning flyttar den färdiga ritningen, vilket ger exakt samma bild.
- **Scenen ligger i fyra lager i sin ritordning:** linjernas lager (hoppkolumner, tidsband, rubriker, områden,
  lapparnas trådar, linjer, belopp och bevakningar), nodernas lager, det lösa lagret (dragna noder) och lagret över
  noderna (kort, soptunnans varning, kandidater, områdenas brickor, guiden, högen och sökringen).
- **Draglagret:** medan noder dras skickar vymodellen `Moved` (de noder som fått ny plats och de dragna) i stället
  för `Redraw`. Varje ruta ritar linjernas lager, det lösa lagret och lagret över noderna igen, medan de stilla
  noderna, den dyraste delen, behåller sin ritning. Bilden blir exakt densamma som en hel ritning. Överlappar en
  dragen nod en nod som ritas efter den, ritas hela scenen, så att den stilla noden ligger kvar ovanpå.
- **Fler bäckar:** opacitet 1 öppnar inget lager, en linjes opacitet ligger i penseln (samma bildpunkter för en
  enda stråke), `Near` räknas en gång per ritning i stället för en gång per lager, och kurvor som inte ändrats
  behåller sin instans, så att deras geometri används igen.

### Musen, animationerna och lagren som kan ritas var för sig (2026-11-09)

- **Musen:** varje rörelse gjorde hela träfftestet två gånger, och dessutom letades tips upp i guiden, korten,
  blockmärkena och beloppen, upp till 1000 gånger i sekunden med en snabb mus. Nu noteras bara var pekaren är, och
  slingan tittar en gång per bildruta, med ett enda träfftest (`HoverAt` ger träffen).
- **Lagren ritas var för sig:** en ändring säger vilka lager den rör (`GraphChange.Lines`, `Loose`, `OverNodes`;
  ett drag är alla tre). Ringarnas steg ritar linjerna och lagret över noderna, ett korts skimmer bara lagret över
  noderna, och timglasets andning bara de noder som andas, som görs lösa för den. Förut ritades hela scenen om i
  varje sådant steg. Ett kort som vänds ritar fortfarande hela scenen, eftersom noden döljs och syns igen.
- **Ikonerna förblir vektorer:** en bitmapp blir suddig vid zoom, och att göra stråkarna till färdiga ytor ger små
  avrundningsskillnader. Geometrin, pennorna och penslarna görs redan en gång.

### Varje nod en egen visual, rutnätet och granskningen (2026-11-09)

- **Som en spelmotor:** varje nod är en egen visual i nodlagret, i ritordningen. Ett drag, en hovring eller ett
  timglas som andas ritar om bara sina noder, som behåller sin plats i ordningen. Det lösa lagret och kontrollen
  av överlapp behövs inte längre, och bilden är exakt densamma som en hel ritning.
- **Hovring:** vymodellen skickar `Hovered` (noden som lämnades, noden som nåddes och om linjen bytte) i stället för
  `Redraw`. En ny nod under pekaren ritar om två noder, och en ny linje ritar om linjernas lager. Hovring på ett
  kort ritar bara lagret över noderna, och på ett blockmärke bara överlagret.
- **Rutnätet `GraphIndex`:** noderna och linjerna ligger i celler om 512 enheter, och vad som syns och vad som
  ligger under pekaren slås upp bara i de celler som berörs. Svaren är exakt desamma som den linjära genomgångens,
  i samma ordning, vilket ett test visar med 1200 slumpade prov. Långa linjer läggs åt sidan och prövas alltid.
  Indexet görs om när scen, platser eller kurvor byts.
- **Granskningen av draglagret** (inga C eller I): en linjes opacitet ligger åter i ett eget lager, eftersom en
  blekt pensel kan skilja en nyans (1/255) mot förut. Zoomen ritar utan marginal, och pennornas cache rymmer 16 384,
  så att zoomens pennor inte tömmer den.
- **Medvetet inte gjort:** LOD (förenklad ritning vid utzoomning), strömning i rutor och bitmappsikoner, eftersom
  de ändrar bilden. LOD kan bli ett val om användaren vill.
- **Andra granskningen av nodernas visuals och rutnätet:** ett kort som vändes tillbaka kunde lämna sin nod
  osynlig (C), och hovring på en linje stoppade sökringen och sonaren (I). Kortens turer avgörs nu innan noderna
  ritas, en tur som tagit slut ritar hela scenen, och ringarna noteras per lager. Rutnätet räknar celler i `long`,
  tål NaN och absurda koordinater, slår upp en hårsmån förbi cellgränserna och följer ett drag genom att flytta bara
  det som flyttats.

### Sammanslagning med omgång 7: grafen vid tusentals aktörer (2026-11-09)

Huvudgrenen fick samtidigt modellsidan av samma arbete från en annan session: `SceneLayout` med ett eget index
(`GraphIndex`, celler om 256 enheter) och ett drag som räknar om bara de dragna linjerna och placerar beloppen igen
när draget slutar. Den behölls, och renderingen från denna gren ligger ovanpå: lagren, nodernas egna visuals, den
eventstyrda slingan och cacharna. Mitt eget rutnät och diffningen av platser (`GraphMoved.Between`,
`KeepSameCurves`) togs bort; ett drag skickar `Moved` med de dragna noderna i stället för `Redraw`.

=======
## Omgång 5: attribution och kurser (backloggen, 2026-11-08)

- **EU:s konsoliderade lista** (`EuFsfParser`): läses ur en sparad FSF-fil i XML (`export` med `sanctionEntity`)
  bredvid OFAC:s SDN-lista, med genereringsdatumet som listans datum, och sparas orörd som `eu-fsf-<sha256>.xml`.
  **Eget beslut:** EU:s lista har ingen egen sort för kryptoadresser (kontrollerat mot den publicerade strukturen),
  så en adress känns igen på formen, en Bitcoin-adress med giltig kontrollsumma eller `0x` och 40 hextecken, i en
  identifierings nummer eller i en anmärkning, och etikettens skäl säger var den stod och att den känts igen på
  formen. Listan kan sakna adresser helt; det sägs ärligt i TESTER-INFOR.
- **Varje sorts lista har sin aktuella** (`ISanctionLists.CurrentLists`, `FindAll`): en adress slås upp på alla, så
  en nyare OFAC-lista skuggar aldrig EU:s. `Current` och `Find` finns kvar för den nyaste listan, och de nya
  medlemmarna har standardimplementationer så att andras attrapper fortsätter att fungera. Journalens rad nämner alla
  listor träffarna kom från.
- **Fynden i appdata (etapp 8, M5 och M6):** `Remove` (utan yta, TODO [skiss]), en trasig beskrivning skrivs om ur
  listan i stället för att listan försvinner, ändringar utanför Chainsaw märks på mappens ändringstid, och en post
  läses del för del med ett tak på 20 000 delar (`SanctionXml.Parts`), så en konstruerad post kan inte fylla minnet.
- **Öppna källor** (`IOpenSourceLists`, `RansomwhereParser`): granskade för licens och kvalitet. Ransomwhere ligger
  på Zenodo under CC BY 4.0 och byggs in; varje träff blir *Rapporterad* (`Ransomware: <familj>`) med
  `LabelSource.OpenSource`, källans datum och hash som version och licensen i skälet. WalletExplorer (inga villkor)
  och OpenSanctions (icke-kommersiellt) byggs inte in; det står i BACKLOG.
- **Sanktionskontroll vid en expansion, båda kedjorna:** `GraphSource` kontrollerar grafens adresser mot listorna och
  de öppna källorna varje gång grafen läses eller expanderas (`CheckSanctions` för `ChainAddress`,
  `CheckOpenSources`), så en adress som kommer in märks direkt, och en lista som importeras medan ärendet är öppet
  gäller vid nästa läsning. Ett fel i en lista hindrar aldrig grafen.
- **Etiketter på tx** (`ITransactionLabels`): förseglas i en egen hashkedja (`attribution.transaction_labels`, schema
  steg 4, så att adressetiketterna behåller sina hashar), tas tillbaka som en ny post, och `LabelTrailEnds`
  avslutar spåret vid en märkt Bitcoin-tx efter samma regler som för adresser. Ingen yta än (TODO [skiss]).
- **Importen** (`SanctionListImport`): samma menyrad tar en JSON-fil som öppen källa och säger det i svaret;
  filväljaren visar `*.xml;*.json`.
- **Kurser:** ett svar som kom efter att perioden var slut och saknar kurs (före Bitstamps eller Riksbankens historik)
  pekas ut som slutligt och besvaras ur ärendet utan att frågas igen, med ett svar som säger det; bara ett lyckat svar
  (2xx) kan bli ett sådant. Riksbankens dygnsgräns räknas över omstarter i `Kurser\riksbank-requests.json`
  (`PricingOptions.StateDirectory`).
- **Avsteg och kvar:** fler tokens (TRX och USDT på TRON väntar på TRON-agentens kedja), en andra kryptokälla och
  Riksbankens nyckel i valvet står kvar i BACKLOG. Det gemensamma lagret för råsvar bedömdes och sköts upp (BACKLOG,
  med skälen). Testärendet visar inte omgångens delar än (BACKLOG, [direkt]).
- **Skriver i ärendet** (granskas oberoende): träffar på EU:s lista och i öppna källor förseglas som etiketter med
  journalrad (`attribution.sanctions-matched`, `attribution.open-sources-matched`), etiketter på tx i en ny
  förseglad tabell (`attribution.transaction-label-added`, `-retracted`), grafens läsning skriver nu träffar, och
  Pricing pekar ut svar utan kurs i registret (oförseglat, som förut, men läst bara när det förseglade svaret är
  lyckat och kom efter perioden).

- **Rättat efter den oberoende granskningen av omgång 5** (0 C, 7 I, 9 M, alla rättade):
  - **I1:** `CheckSanctions` och `CheckOpenSources` läser först och öppnar en skrivning bara när något är nytt; test
    med en räknande `ICase`.
  - **I2:** Riksbankens tomma svar är aldrig slutliga (eget beslut: utan bevis, i stället för ett bevis som bara
    fungerar vid seriens början).
  - **I3:** Bitstamps svar utan perioden är slutligt bara när svaret har perioder som alla börjar efter den
    (`ICryptoRateSource.HistoryStartsAfter`); ett tomt svar aldrig.
  - **I4:** listorna slås upp en gång per anrop (`ISanctionLists.FindMany`, `IOpenSourceLists.Find`) och varje lista
    för sig; en lista som tappat sin hash hoppas över, minns som trasig (läses inte om) och nämns i
    `SanctionCheck.Unusable`. Grafen säger det i sina problem, som blir en toast en gång. Sanktionslistorna och de
    öppna källorna kontrolleras var för sig.
  - **I5:** EU:s lista tar bara adresser på huvudnätet och hoppar över en adress när texten runt den nämner en annan
    kedja; de räknas som överhoppade.
  - **I6:** etikettens skäl citerar ungefär 100 tecken runt adressen.
  - **I7:** licensen och erkännandet för Ransomwhere står i *Varför?*, i vyn Etiketter och i rapporten
    (`OpenSourceLicenses`); öppna källor har ett eget filter, *Öppna källor*, med jordgloben (`Icon.Globe`, en ikon som
    redan fanns), och *Varför?* visas för dem.
  - **M1** delarna räknas med allt inuti, **M2** `CurrentLists` en gång per anrop, **M3** etiketterna på tx läses en
    gång per öppet ärende och glöms när de skrivs, **M4** en trasig beskrivning av en öppen källa skrivs om, **M5**
    datum efter i dag vägras för öppna källor, **M6** TRON-adresser och testnät räknas som överhoppade, **M7** en fil
    med BOM känns igen, **M8** dygnsräknaren nollställs inte när klockan ställs bakåt och skriver genom en unik
    temporär fil, **M9** ytan för etiketter på tx står redan i TODO som [skiss].
- **Andra granskningen av omgång 5** (0 C, 2 nya I, 3 M, alla rättade):
  - **I-a:** EU:s lista tar kedjan ur den närmaste märkningen före adressen i samma del av texten (delad vid `;`,
    komma och radbrytning): XBT/BTC/Bitcoin och ETH/Ethereum/ERC-20 ger en träff, BCH, LTC, TRX/TRC-20, BEP-20/BSC med
    flera hoppas över, och allt annat (ingen märkning, ett namn som Polygon som kan vara ett företags, en märkning som
    inte passar formen, en Base58-adress utan märkning) blir **Möjlig sanktion** (`LabelKind.PossiblySanctioned`),
    aldrig en sanktion och aldrig ett slut på spåret. En SegWit-adress (`bc1…`) är Bitcoins och en träff utan
    märkning. Importens svar säger *tvetydiga* i stället för *ogiltiga*. Test med EU:s typiska rad med flera kedjor.
    Markeringen i grafen skissas (TODO).
  - **I-b:** en räknad dag mer än en dag efter i dag glöms och skrivs i loggen, så att Riksbanken aldrig stängs till
    det datumet; test.
  - **M-b** lås runt cachen för etiketter på tx, **M-c** licensen hör till den inbyggda källans ursprung (id som börjar
    med `open/`), aldrig bara namnet, **M-d** en fil som saknas är inte ändrad (`Missing`) och frågas igen; M-a står i
    BACKLOG märkt *prova mot ett riktigt anrop före 1.0*.
- **Tredje granskningen av omgång 5** (0 C, 2 I, 4 M, alla rättade):
  - **I-1:** förhandshämtningen i Nytt ärende visar en säker träff först och skriver *Möjlig sanktion enligt …* för en
    tvetydig; brickan säger då *Möjlig sanktion* och är nedtonad.
  - **I-2:** vyn Etiketter avgör sanktionen på etikettens sort: `IsSanction` och träffarna överst gäller bara
    *Sanktionerad*; en möjlig sanktion står streckad med brickan *Möjlig: kedjan står inte säkert*, utanför den röda
    gruppen. Test.
  - **M-1** en märkning gäller över kommatecken tills nästa märkning (kedjan delas vid `;` och radbrytning), **M-2**
    tjänsten vägrar en sanktion, möjlig eller tidigare ur ett utkast (`LabelRules.IsFromListsOnly`, också för
    etiketter på tx); testärendets sanktion på Skuggtorget kommer nu ur den påhittade listan, och *Tidigare
    sanktionerad* står i BACKLOG, **M-3** en lista som saknas säger *saknas på datorn* (`Missing`), **M-4** *etc* och
    *base* är borttagna ur de svaga orden.
>>>>>>> worktree-agent-a9532d92522604c5d

## DX1b: en DEX:s router som markering i swap-pillret (2026-11-09)

Byggt efter skissen DX1b (användarens val 2026-11-08). Bara visning i App; spåret genom swappen är oförändrat.

- **`SwapRoute`** (`Graph/SwapRoute.cs`, utan WPF): läser vägen genom bytet ur tx:ens överföringar för en swap vars
  `TokenSwap.Venue` är ett känt kontrakt (`KnownContracts`, DEX eller aggregator): den sålda tillgången först, sedan
  varje ny tillgång i den ordning tx:en betalade ut den med adressen som betalade (poolen, med namnet ur listan om
  kontraktet är känt, annars `pool 0x…`), och den köpta sist med allt som kom tillbaka. Ether och WETH räknas som en
  tillgång, eftersom routern bara lindar om. Antalet byten är stegen minus ett. `EdgeView.SwapRoute` sätts för linjens
  första swap, som pillrets ruta (SW1a).
- **Markeringen** (`GraphCanvas.SwapRoutes.cs`): en liten grå rundad fyrkant i pillrets övre högra hörn med routerns
  ikon (`Icon.Swap.Router`, ur skissen), eller antalet byten för en aggregator. Hovring visar rutan ovanför pillret:
  routerns namn (*· aggregator*), *Känt kontrakt · byter token, inte ägare* eller *N byten i en tx*, vägen som
  brickor med belopp och pool under, *Spåret går igenom* (eller *Spåret slutar i swappen*), *Klicka för Varför?* och
  kontraktet och tx:en i Cascadia Mono. Ett klick öppnar *Varför?* i Guidens panel (`OpenSwapRouteWhy`) med fyra
  rader: vad som skickades genom vilken router, vägen, regeln och listans källa och version, vid rubriken *En DEX:s
  router* i avsnittet om swappar (ny text i `docs/guide/swappar.md`).

**Egna beslut och avsteg från skissen:**
- **Klicket ligger på markeringen, inte på pillret.** I skissen öppnar ett klick på pillret *Varför?*, men i SW1a
  fäller samma klick ut valet *Följer* / *Följer inte*. Pillret behåller SW1a:s klick, och markeringen är en egen
  träffyta med egen hovring; pillrets ruta och routerns ruta visas aldrig samtidigt.
- **Poolernas namn** (skissens *Uniswap v3 · USDT/WETH 0,05 %*) finns inte i kedjans överföringar och inte i listan,
  så poolen visas med adressen förkortad; inget hittas på.
- **Vägen kan vara ofullständig:** är tx:en bara känd ur adressens historik (inte läst hel) säger rutan *vägen kan vara
  ofullständig*, och *Varför?* ber användaren expandera tx:en.
- När användaren valt *Följer inte* står personmarkeringen i pillrets **vänstra** hörn, eftersom routern tar det högra.
- Markeringen byter inte färg vid hovring (den ritas i scenens linjelager, och hovringen ritar bara lagret över).

**Återstår:** testärendet har ingen Ethereum-källa, så markeringen går inte att se i `/testdata` än.
