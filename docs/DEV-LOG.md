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
