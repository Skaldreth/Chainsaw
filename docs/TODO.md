# Att göra

Backloggen: det som inte är byggt, sorterat efter vad det väntar på. **Ta bort en punkt när den är byggd.** Här står allt
som diskuterats, så att inget tappas bort medan vi bygger MVP:n.

## Väntar på beslut

- **Återkallade återställningsnycklar** (etapp 9): en bytt nyckels kuvert ligger kvar, och tas bort först när nyckeln
  återkallas med en rad `REVOKED` i `organization-recovery-key.pem`. Att ta bort alla andra nycklars kuvert vid ett
  byte prövades men vägrades i granskningen: en äldre version eller en annan organisations Chainsaw tog då bort den
  nyare nyckelns kuvert. Bekräfta att återkallelse för hand räcker.
- **Ordningen bland uttagen** (CLAUDE.md, *Sorterna efter vad som händer med pengarna*): fråga kryptospåraren vilka uttag
  som är vanligast i de svenska ärendena, och om några sorter kan slås ihop.

- **Vad av ett annat ärende som följer med en etikett** (CLAUDE.md, *Etiketter smittar*; byggt i etapp 8 efter
  förslaget, bekräfta med användaren): ärendenumret, anteckningen och skälet följer med till biblioteket men inte vidare
  in i ett annat ärende; där står bara vem (hela operatörsprofilen: namn, titel, enhet), när, etiketten med namn och
  vilken sorts källa. En etikett kan märkas *Sprid inte*. Sessionens egna tillägg: en etikett på ett offer, och varje
  etikett på offrets adresser eller kluster, sprids aldrig; en etikett som tas tillbaka där den sattes dras tillbaka ur
  alla kopior. Ska *Sprid inte* också gå att sätta för ett helt ärende? Ska titel och enhet följa med?
- **Hur bevakningen säger till** (den går också när Chainsaw är stängt, CLAUDE.md *Bevaka*): bara en avisering i Windows,
  eller också ljud, e-post eller telefon? Hur ofta källan frågas (var femte minut, varje block) och om en viktig bevakning
  ska kunna frågas oftare.
- **Rapporten:** hur redovisningen byggs (som i Gawk med bilagor, eller enklare underlag för underrättelse). Bestäms när vi
  kommer dit.
- **Tröskelvärdena i klustringen** (etapp 6, valda av sessionen): växeln slås ihop automatiskt vid minst tre tecken
  och inget emot, i en tx med två outputs; en peel chain kräver tre kända hopp och att det som går vidare är minst fyra
  gånger det som skalas av; ett jämnt belopp är en multipel av 0,001 BTC; en möjlig CoinJoin (två lika stora outputs,
  eller lika jämna belopp utan känt mönster) gör allt i tx:en till förslag. Pröva med kryptospåraren.
- **Peel chain slår inte ihop** (etapp 6, granskningen): formen räcker inte för att se var ägaren byter (ett offers
  vanliga betalningar ser ut som hopp), så `peel-chain v1` bara föreslår. Fråga kryptospåraren vad som skiljer en
  riktig peel chain: samma skripttyp i varje hopp, liknande andel som skalas av, kort tid mellan hoppen?
- **Spårningsmetod som förval:** FIFO, LIFO, haircut eller poison. Användaren frågar kryptospåraren vilken som används i praktiken (planerat 2026-11-08).
- **Vilka mynt spåret startar i** (etapp 12, valt av sessionen): grafen spårar från alla mynt som en startadress tagit
  emot i grafen, och från en startpunkts-tx:s betalningar till andra än betalarna; ett mynt längre fram i samma flöde
  tas bort. Ska användaren välja mynten själv (till exempel offrets betalningar, eller ett belopp av ett mynt), och hur
  ska det se ut? Behöver en skiss.
- **Bitstamp som kurskälla i tjänsten** (etapp 14, valt av sessionen): öppen historik utan nyckel, men villkoren för
  kommersiellt bruk är oklara (se *Kurser*). Bekräfta att Bitstamp får användas, eller välj en annan källa; källan kan
  bytas utan att något annat ändras.

## Bygga härnäst


## Väntar på skiss




- **Det som hölls tillbaka och det som kontrollerats** (etapp 15): en expansion tar högst 50 tx (en adress nyaste,
  en tx:s spenderare efter belopp) och säger i raden längst upp hur många som hölls tillbaka. Grafen behöver ett sätt
  att hämta resten, och kanske att vika ihop en batchutbetalnings många outputs i en nod. Inspektören bör visa om en
  tx är kontrollerad mot sina bytes och om den bytt block (omorganisation). Skissas.
- **Tidslinjen under grafen** (GR4): prickar per händelse under ytan, där ett klick visar grafen vid en tx (det som
  kom senare bleknar) och *Till nu* går tillbaka. Inte byggd i etapp 12; skissen finns, men hur händelserna väljs i en
  stor graf (en prick per tx blir för många) behöver bestämmas.
- **Mixerns troliga utgångar** i grafen (GR3a: streckad linje och streckad nod, *Kandidat*), med godkänn och avfärda.
  Kräver analysen som föreslår dem (belopp minus avgift, tid, adresstyp).
- **Guiden som vy:** *Varför?* och *?* länkar till avsnitten, men vyn Guide är inte byggd; svaret säger var texten finns.
- **Länka till en annan kedja och etiketter i grafen:** inspektören visar länkar och etiketter men kan inte sätta dem
  än (Tracing och Attribution har stödet). Behöver skiss för formulären.
- **Bryta upp ett kluster** från inspektören (Clustering har `Split`). Behöver skiss.
- Skalet: sidopanelen och vyernas ordning.
- Vyerna: Tidslinje, Flöde (Sankey), Tx-inspektör, Adresskort och kluster, Exponering, Bevakning, Etiketter, Terminal, Guide.
- Begäran om uppgifter till börser: formuläret och mallarna.

## Senare (efter MVP:n)

### Kedjor och data
- **Ethereum och ERC-20:** native ETH, interna tx (traces) och tokenöverföringar (Transfer-eventet). Kräver en källa med
  adresshistorik och traces.
- **TRON och USDT (TRC-20).**
- **Fler kedjor** efter behov: Litecoin, BSC, Solana.
- **Privacy coins:** först *Spåret tar slut här*. Senare forskningsmetoderna (till exempel tidsanalys och kända svagheter i
  äldre Monero-tx, Zcash-tx som går in och ut ur den skyddade poolen med samma belopp).
- **Cross-chain automatiskt:** hitta fortsättningen på den andra kedjan själv (belopp och tid hos swap-tjänster, bryggornas
  egna händelser). Kanten finns i modellen från början.
- **Direkt mot kedjan:** egen nod och eget index (Bitcoin Core med `txindex`, Fulcrum eller electrs eller ett eget index;
  Reth eller Erigon för Ethereum; java-tron för TRON). Kräver en server. Esplora går att köra själv, så Bitcoin blir bara en
  annan adress i datalagret.
- **Bevakning i realtid:** websocket där källan har det (mempool.space har bevakning av adresser), senare ZMQ från egen nod.

### Bitcoin-källan (Chains)
- **Svarshuvuden som ursprung:** se *Utgångar*. Behövs om källans `Date` ska stå bredvid Chainsaws egen tid.
- **Flera källor att jämföra:** samma tx från två Esplora-källor, och en varning om de skiljer sig.
- **Tx på en adress sida kontrolleras inte mot sina bytes** (etapp 15): bara tx som expanderas för sig (och deras
  spenderare) hämtar `/tx/:txid/hex`. En adress historik med 50 tx skulle kosta 50 uppslag till. Kontrollera dem när
  de visas i inspektören, eller i bakgrunden?
- **Tx:en i blocket** (etapp 15): bytesen binder beskrivningen till txid, men att tx:en ligger i just det blocket
  vilar på källan. `/tx/:txid/merkle-proof` och blockets merklerot skulle binda dem; och om `bits` är nätets rätta
  svårighet på höjden kontrolleras inte.
- **Svaret om spenderade utgångar och omorganisationer** (etapp 15): en tx som bytt block säger det, men ett svar om
  vem som spenderar en output jämförs inte med det förra.

### Klustring och heuristiker
- **Troliga utgångar ur en mixer** (CLAUDE.md, *Mixers är vanliga*): belopp minus avgift, tidsfönster, adresstyp och kända
  mönster per mixer. Kandidater som användaren godkänner eller avfärdar. Fråga kryptospåraren vilka mixers som syns oftast.
- **Tjänster ur beteende:** en börs insättningsadresser (många insättningar som sveps till samma hot wallet),
  utbetalningar i batch, konsolidering. Etapp 6 slår ihop insättningsadresserna i en sweep men säger inte *börs*.
- **Godkänna ett förslag och ångra en uppbrytning** (etapp 6): förslag syns men kan inte göras till en
  sammanslagning, och en uppbrytning kan inte tas tillbaka. Båda behöver en egen förseglad post med vem och varför
  (en manuell sammanslagning som en egen *analys*, `user v1`). Byggs med gränssnittet för kluster.
- **Fler tecken för växeln:** jämna belopp i SEK eller USD vid tx:ens tid (kräver kurser), plånbokens fingeravtryck
  (tx-version, locktime, RBF, ordningen på outputs), och adressens hela historik från källan i stället för bara det
  ärendet känner till (*ny adress* är nu *inte sedd i ärendet*).
- **Fler mönster för CoinJoin:** Wasabi 2:s exakta lista över standardbelopp, nyare Whirlpool-pooler (efterföljarna
  till Samourai), JoinMarkets avgifter och fidelity bonds, och PayJoin (syns inte på kedjan; kan bara anas).
- **Peel chain med fler inputs** (ett hopp som fylls på) och kedjor där växeln inte är den största outputen.
- **Nya sorters fynd:** en ny analys med en egen sorts fynd måste deklarera den på `HeuristicFinding` i
  Contracts (fyndet förseglas som JSON med sin sort); annars misslyckas dess tx för sig. En allmän sort med namn och
  JSON skulle göra det öppet utan ändring i Contracts.
- **Slutsatser som hålls tillbaka** (`ClusteringRun.HeldBack`): när en körning saknar en tx som en sparad slutsats
  bygger på sparas inget nytt för den tx:en. Kan en sådan tx aldrig skickas med igen (till exempel en obekräftad tx
  som ersatts) står den gamla slutsatsen kvar; gränssnittet bör visa det.
- **En version som dras tillbaka:** visar det sig att en heuristik i en version slog ihop fel ska dess slutsatser kunna
  sluta räknas utan att tas bort (princip 8), till exempel med en förseglad post *dras tillbaka* per version. Nu räknas
  alltid den högsta version som har sparats för ett namn, så en avsiktlig återgång till en äldre version räknas inte.
- **Klustringens underlag ur ärendet** (granskningen etapp 6): `IClusteringService.Analyse` tar de tx som anroparen
  skickar, både som det som analyseras och som det analysen vet runt varje tx. En snävare körning kan inte ta tillbaka
  en slutsats vars underlag den saknar, men en tx som aldrig hämtats in i ärendet kan analyseras. Bättre: Clustering
  läser tx:en ur ärendet genom Chains (kontrakt för det i `Chains.Contracts`), och varje slutsats pekar på det
  förseglade råsvaret och blocket (höjd och hash) den bygger på (princip 1).
  Tills dess kan en tx som analyseras första gången i en snäv körning få en sammanslagning av växeln som en bredare
  körning skulle vägra (den betalda adressen har setts i en tx som inte skickades med); den står kvar tills en bredare
  körning ersätter den (granskningen etapp 6, omgång 3, M).
- **Journalen och körningar som hålls tillbaka:** varje körning som håller tillbaka något skriver en rad i journalen,
  också när inget sparas. Om appen anropar `Analyse` ofta med få tx kan journalen fyllas; överväg då att bara skriva
  när något sparas eller att samla raderna (granskningen etapp 6, omgång 3, M).
- **Uppbrytning av ett par som inte hör ihop:** en uppbrytning får göras i förväg, mellan två adresser som inte är i
  samma kluster. Gränssnittet bör föreslå länken på vägen mellan dem (`ExplainConnection`) och varna när de inte hör
  ihop.
- **Stora ärenden i klustringen:** klustren byggs om ur alla förseglade slutsatser vid varje fråga, och en körning
  läser alla fingeravtryck. Det räcker för tusentals tx; för större ärenden behövs ett register bredvid (som Chains
  har) eller en cache som byggs om när något nytt förseglas.
- **Mönsterigenkänning** (kanske en lokal modell senare): läggs till som ännu en analys med namn och version.

### Spårning (Tracing)
- **LIFO och poison** (etapp 7): kontraktet `ITracingMethod` tar emot fler metoder, men LIFO behöver en definition
  (inom en tx är ordningen redan FIFO:s; LIFO handlar om en adress saldo över tid), och poison ger inga belopp som går
  jämnt ut, så den behöver visas som *berört* i stället för ett belopp. Fråga kryptospåraren vad som används.
- **Fråga om det som kan ha ändrats** (etapp 7): grafen visas bara ur ärendet (`LookupMode.CaseOnly`). Ett mynt som
  låg kvar visas som *kan ha flyttats* tills noden expanderas igen. En knapp eller ett kommando som frågar om alla
  sådana mynt på en gång, och bevakningen som gör det i bakgrunden, behövs.
- **Tidsfönster för en adress historik** (etapp 7): expansionen tar de nyaste tx upp till gränsen (50 som förval).
  För en adress med lång historik behövs ett tidsfönster runt händelsen i stället.
- **Markeringarna Bevakad och Kan frysas** (GR3a, GR3c) ritas när Monitoring och kedjorna med USDT/USDC finns.
- **Grafens prestanda vid tusentals noder** (etapp 12): ytan ritar bara det som syns, men varje drag räknar om alla
  kurvor och varje hovring prövar alla linjer. Ett rutnät (spatial index) och att bara räkna om linjerna till den
  dragna noden behövs om graferna blir mycket stora.
- **Hitta fortsättningen på en annan kedja själv** (CLAUDE.md, *Cross-chain*): belopp och tid hos swap-tjänster,
  bryggornas egna händelser. I v1 knyts den för hand.

### Attribution
- **EU:s sanktionslista** (och andra) bredvid OFAC SDN, som etapp 8 läser ur en sparad XML-fil.
- **Kan frysas** (CLAUDE.md, *Sorterna*): markeringen för adresser med USDT eller USDC hör till Ethereum och TRON. SDN
  har redan adresser för `USDT`, `ETH` och `TRX`, som etapp 8 läser in och räknar men ännu inte slår upp.
- **Öppna källor:** till exempel ransomwhe.re (öppen datamängd med ransomware-adresser), WalletExplorer (äldre BTC-kluster),
  Chainabuse (kräver nyckel). Varje källa granskas för licens och kvalitet innan den byggs in. `LabelSource.OpenSource`
  finns i kontraktet.
- **Sanktionslistan mot grafens adresser** (`CheckSanctions`) och vyn Etiketter (skissas först). Biblioteket är
  kopplat sedan etapp 15: `Absorb` när ett ärende öppnas och innan det stängs, `ShareInto` när grafen läses.
- **Ett bibliotek som inte går att läsa syns inte** (etapp 15): grafen och ärendet fungerar, men etiketterna från andra
  ärenden kommer inte, och inget säger varför. Ett meddelande i statusraden eller i vyn Etiketter behövs.
- **Slutet *Börs, uttag funnet*** gäller alla blå sorter (också börs utan kundkontroll, P2P, automat och kort), där
  en begäran inte alltid går att göra; sorten står i förklaringen. En egen orsak eller text per sort? (granskningen
  etapp 8, M1).
- **Etiketter från ett annat ärende i grafen** avslutar spåret som en egen etikett; grafen bör rita dem streckade
  (`Label.IsOnlyFromOtherCases`) tills någon i ärendet bekräftat dem (granskningen etapp 8, M2).
- **Namnet sprids:** etikettens namn, en analys eller källas namn och hela operatörsprofilen följer med till andra
  ärenden. Gränssnittet ska säga det, och kanske varna när namnet liknar ett ärendenummer (granskningen etapp 8, M3).
- **Listan i ärendet:** ärendet sparar listans hash, datum och postens uid, inte listan. En kollega eller en annan dator
  kan inte kontrollera träffen utan filen. Försegla postens XML i ärendet? (granskningen etapp 8, M4).
- **Sanktionslistorna i appdata:** ingen funktion för att ta bort en lista (en lista med fel datum i framtiden förblir
  aktuell), en trasig beskrivning gör att listan försvinner tyst ur listningen, och ändringar utanför Chainsaw märks
  först vid omstart (granskningen etapp 8, M5).
- **Minnet vid import:** en `sdnEntry` läses in hel; en konstruerad fil med miljontals `id` i en post kan ta mycket
  minne. Läs id:n strömmande eller sätt ett tak per post (granskningen etapp 8, M6).
- **Biblioteket:** ärenden som tagits bort ligger kvar som innehavare, två kopior av samma ärende (samma id) skriver
  över varandra, och ett bibliotek som inte går att läsa (skadat eller från ett annat valv) stoppar varje inläsning
  tills det byggs om. Bygg om biblioteket ur ärendena, och ta bort ett ärendes etiketter (granskningen etapp 8, M8).
- **Offret märks efter att en etikett spridits** (granskningen etapp 8, omgång 2 och 3): etiketten och bibliotekets
  post för adressen tas bort ur biblioteket när ärendet med offret läses in, men kopior som redan nått andra ärenden
  ligger kvar där (och kommer tillbaka i biblioteket när de ärendena läses in) tills etiketten tas tillbaka där den
  sattes. Att dra tillbaka dem automatiskt skulle avslöja för de andra ärendena när någon märkte ett offer.
  `NearVictim` räknar hela offrets kluster; är offrets adress en börs insättningsadress kan klustret vara börsens,
  och då hålls alla etiketter på det klustret i det ärendet utanför biblioteket (säkert, men brett).
- **Minnet för tillbakadragna etiketter** (`Withdrawn` i biblioteket) får varje egen etikett som tas tillbaka och
  rensas aldrig (granskningen etapp 8, omgång 2, M). Räcker för tusentals etiketter.
- **Samma person på två datorer:** en egen etikett räknas som oberoende per operatörsprofil (namn, titel, enhet). En
  profil som ändras räknas som en annan person; ett stabilt operatörs-id i Security vore bättre.
- **Etiketter på tx** (till exempel en känd brygga eller swap): `ClassifyTransaction` ger inget från etiketter än.

### Kurser (etapp 14)
- **Kurserna i gränssnittet:** beloppen i SEK på grafens linjer och i inspektören, stegen bakom *Varför?*, och valet av
  valuta och upplösning i inställningarna (nu bara `PricingOptions` och per anrop). Kräver skiss.
- **Bitstamps villkor för data:** API-sidan säger både att data får användas och spridas vidare för kommersiella syften
  och att företag som vill använda den kommersiellt ska teckna ett licensavtal (partners@bitstamp.net). Användarvillkoren
  gick inte att läsa (tom sida 2026-11-07). Kontrollera vad som gäller för en myndighets interna, icke-kommersiella
  bruk innan Chainsaw används i tjänsten.
- **Inte kontrollerat i dokumentationen** (bara med ett riktigt anrop 2026-11-07): att Riksbankens adress
  `Observations/{serie}/{från}/{till}` är den dokumenterade (FAQ:n nämner datumintervall men visade inte formen), att
  värdet är kronor per *en* enhet (stämmer med nivån, cirka 10 kr per dollar), vilken tid på dagen kursen sätts, och
  att Bitstamps perioder börjar på jämna multipler av sin längd från 1970 och att en period utan handel har volym noll
  och föregående pris.
- **Perioder före källans historik** (granskningen, M6): ett svar utan kurs (Bitstamp före 2011, Riksbanken före 1993)
  sparas men pekas inte ut som slutligt, så varje uppslag frågar igen och sparar ett nytt svar. Kan pekas ut som
  *ingen kurs* när perioden är slut.
- **Fler källor och tokens:** ETH och TRX (och USDT/USDC, som i praktiken är dollar) när Ethereum och TRON byggs; en
  andra kryptokälla att jämföra med (till exempel Kraken eller Coinbase för nyare perioder); Riksbankens nyckel (200
  anrop i minuten) i valvet om gränsen på 5 i minuten blir ett problem.
- **Medelkurs eller stängningskurs:** Chainsaw tar sista priset i perioden. Fråga kryptospåraren om rapporterna brukar
  använda dagens stängning, medelpris eller något annat.
- **Ett gemensamt lager för råsvar:** Chains och Pricing har var sin likadan tabell och kod för förseglade råsvar. Kan
  bli ett kontrakt i Cases när en tredje modul behöver det.

### Begäran om uppgifter
- **Mallar** för en begäran till börser, på svenska och engelska, med tx, tider, belopp och insättningsadresser.
- **Börsernas kontaktvägar** för brottsbekämpande myndigheter (portaler och adresser). Varje uppgift ska verifieras och ha en
  källa och ett datum; inga påhittade kontaktvägar.

### Säkerhet
- **Klartext i minnet:** API-nycklarnas hemligheter och profilen ligger som `string` medan valvet är upplåst och kan inte
  nollställas, och JSON-bufferten vid kryptering är inte fäst (pinned). Kan skärpas med en egen serialisering till en
  fäst buffert och `char[]` för hemligheterna (granskningen etapp 2, M).
- **Sparning som en låsning hinner före:** filen skrivs men anroparen får inget besked (konsekvent, eftersom nästa
  upplåsning läser filen). Kan bli ett `Locked`-fel efter skrivningen om gränssnittet behöver veta (granskningen, M).
- **Unicode-normalisering av huvudnyckeln** (NFC) innan den härleds, så att samma nyckel skriven med olika
  inmatningsmetoder ger samma resultat.
- **DPAPI för bevakningsprocessen** (CLAUDE.md, *Bevaka*): bevakningens adresser krypteras för Windows-användaren, utanför
  valvet. Byggs med bevakningen.

### Utgångar (Network)
- **Tunnelns port på andra system än Windows:** på Windows hålls porten exklusivt (`SO_EXCLUSIVEADDRUSE`) och
  Chainsaw frågar efter varje anslutning om hjälpprogrammet lever, så en anslutning kan inte ha nått något annat
  program. Byggs Chainsaw någon gång för ett annat system behöver samma garanti prövas där.
- **Återanslutning:** när tunneln faller (*förbindelsen förlorad*, till exempel efter att datorn sovit) ansluter
  Chainsaw inte igen av sig själv. Viktigt för bevakningen.
- **Svarshuvuden som ursprung:** `ExitResponse` har bara status, medietyp och den råa kroppen. Behöver Chains huvuden
  som `Date` eller `ETag` som en del av ursprunget läggs de till då (granskningen etapp 4, M).
- **Certifikatkedjor som saknar en mellanliggande nivå:** för att operativsystemet inte ska hämta något runt utgången
  (AIA och spärrlistor) hämtas inga certifikat och spärrar kontrolleras inte. En källa som skickar en ofullständig kedja
  går då inte att nå. Spärrkontroll genom utgången kan byggas senare om det behövs (granskningen etapp 4, C).
- **Rötter som läggs till medan Chainsaw är igång:** de betrodda rötterna läses när en utgång ansluts. En rot som
  Windows lägger till senare gäller från nästa anslutning.
- **Rötternas begränsningar på Windows:** med Chainsaws egen rotlista följs inte Windows egna begränsningar för en rot
  (användningsområden, *distrust after*). Prova på Windows (TESTER-INFOR) och bestäm om de ska läsas in.
- **Stoppen när programmet avslutas:** `Dispose` väntar (högst fem sekunder) på tunnelstoppen som finns när den
  börjar, men inte på ett anslutningsförsök som lämnar över en tunnel efteråt (den stoppas ändå direkt), och det finns
  ett litet fönster mellan att den anslutna utgången tas och att dess stopp registreras. Viktigt när tunneln är en
  egen process (granskningen etapp 4, omgång 5, M).
- **Gränssnittet för utgångar:** lägga till en WireGuard-fil, välja utgång, ansluta och koppla ner, och läget i
  titelraden (skissas först). Felen har egna skäl sedan etapp 13: *filen godtas inte* (`ConfigurationRefused`),
  *servern svarar inte* (`NoAnswer`), *tunneln finns inte* och *förbindelsen förlorad*.
  Vilken rad i en vägrad fil som är fel står bara i `ExitConnectException` (hjälpprogrammet nämner rad och nyckel,
  aldrig ett värde); `ExitStatus` behöver ett fält för det när gränssnittet ska visa det (granskningen etapp 13, M).
- **Tunnelns hjälpprogram och Go:** `chainsaw-tunnel.exe` är byggd med Go 1.27.3 (låst i `go.mod`) och checkad in
  med sin hash; den byggs om med `tools/Chainsaw.Tunnel/build.sh` (som kör `govulncheck`) när Go eller beroendena får
  säkerhetsrättelser, minst när en Go-version slutar få stöd.
  `dotnet test` kör inte Go-testerna; de körs av byggskriptet. Signera programmet med organisationens certifikat när
  Chainsaw signeras.
- **Tunnelns IPv6:** servern nås över IPv4 när `Endpoint` är ett namn med båda sorterna. En utgång som bara svarar på
  IPv6 når man med en adress i `Endpoint`.
- **Bevakningsprocessen och utgången:** bakgrundsprocessen (CLAUDE.md, *Bevaka*) ska gå genom Chainsaws utgång utan
  valvet. Hur den får konfigurationen (DPAPI för Windows-användaren) bestäms med bevakningen.
- **Kontroll av utgången:** fråga en källa vilken offentlig adress uppslagen kommer ifrån, så att användaren ser att
  utgången verkligen används (till exempel när den ansluts).

### Ärenden
- **Återställningskuvertet kontrolleras bara med fingeravtrycket:** ett skadat kuvert med rätt fingeravtryck räknas
  som täckt, både av appen och av `check`, och upptäcks först när administratören försöker öppna. En MAC över kuvertet
  med en nyckel ur filnyckeln skulle låta appen se det och ersätta det; `check --key` kan prova den privata nyckeln på
  riktigt (granskningen etapp 9, M).
- **Antalet kuvert:** `AddOperatorAccess` och delningskuvertet kontrollerar inte gränsen på 64 kuvert innan de lägger
  till, så en fil kan bli oläsbar (`WriteHeader` vägrar nu i stället för att skriva den). Återställningskuvertet
  hoppas över vid gränsen (granskningen etapp 9, M).
- **Återkallelser i `organization-recovery-key.pem`:** ett fingeravtryck delat på två rader där ordet `REVOKED`
  också saknas (stavfel eller svenska) märks inte. En grupp som tappas och ersätts av ett annat ord med fyra
  hexadecimala tecken ser ut som ett giltigt fingeravtryck och återkallar en nyckel som inte finns. Ett eget kommando i verktyget som skriver raden (`revoke
  <publik.pem>`) skulle ta bort risken för skrivfel (granskningen etapp 9, omgång 7, M).
- **Gränssnittet för återställningsnyckeln:** visa i inställningarna eller *Om Chainsaw* om en nyckel är inbyggd och
  dess fingeravtryck (`ICaseStore.RecoveryKey`), och ärligt när den saknas. Skissas med inställningarna.
- **Verktyget `Chainsaw.Recovery`:** på Windows får den privata nyckelns fil mappens rättigheter, inte en egen ACL
  bara för ägaren; med omdirigerad indata läses lösenordet som en `string` som inte kan nollställas; punkten på kurvan
  kontrolleras av plattformen (OpenSSL, CNG), inte av en egen kontroll (granskningen etapp 9, M).

- **Spara ofta:** ett öppet ärende arbetar i en krypterad arbetskopia och `.cut` skrivs vid sparning och stängning.
  Appen bör spara efter skrivningar när det är lugnt (till exempel efter några sekunder utan nya), så att `.cut` som
  lämnas vidare är färsk. Arbetskopian tas tillbaka efter en krasch, så inget går förlorat.
- **Ankra hashkedjan utanför ärendet:** verifieringen ger varje kedjas sista hash. Den bör stå i rapporter och underlag
  (och kanske i en egen logg utanför ärendet), så att den som har filnyckeln inte kan skriva om hela kedjan oupptäckt.
- **Stora ärenden:** att öppna och spara kopierar hela databasen. Mät när ärendena blir stora (hundratals MB) och
  överväg att spara bara när något ändrats, eller ett eget VFS med huvudet i filen.
- **Unicode-normalisering av delningslösenordet** (NFC), som för huvudnyckeln.
- **Hela filen autentiserad:** varje sida i databasen är autentiserad (Poly1305), men inte vilken version av sidan.
  Den som saknar nyckeln kan i princip byta in en äldre sida ur samma ärende, och huvudet med okända kuvert är inte
  autentiserat. En HMAC över huvudet och databasen vid varje sparning, med en nyckel ur filnyckeln, skulle stänga det
  (granskningen etapp 3, M).
- **Vem har tillgång:** operatörens kuvert bär inget namn (så att filen inte visar vems den är), okända kuvert sparas
  orörda, och att ta bort delningslösenordet byter inte filnyckeln. Den som en gång haft lösenordet och en kopia av
  filen kan alltså läsa den kopian. Ett *byt filnyckel* (nyckelrotation) behövs om tillgång ska kunna dras tillbaka.
- **Konflikt med arbetskopian:** när en arbetskopia inte hör till ärendefilen (filen har bytts ut) vägras öppningen och
  båda lämnas orörda. Gränssnittet behöver visa det och låta användaren välja (öppna filen, eller spara arbetskopian
  som ett eget ärende).
- **Ankarna i valvet:** ankarfilen kan tas bort eller bytas mot en äldre (tillsammans med en äldre `.cut`) av den som
  kan skriva i `%LOCALAPPDATA%`. En borttagen fil märks och skrivs i journalen när valvet har ett eget kuvert i ärendet,
  men en återspelad äldre fil märks inte. Det kräver en räknare per ärende i valvet (en ändring i Security), så att
  ankaret inte kan bli äldre (granskningen etapp 3, omgång 2).
- **Ankarfil som inte går att läsa:** journalen säger då *Unreadable* utan att lista kedjornas lägen; en återställning
  som sker samtidigt syns som en ersättning av ankarna men inte med vilka poster som saknas. Ankarna kan skrivas med
  kedjornas huvuden i detaljerna (granskningen etapp 3, omgång 3, M).
- **Vem som förseglade en post:** en modul kan försegla poster i en annan moduls kedja (inte i Cases egna), och posten
  säger inte vilken modul som skrev den. Bind transaktionen till sin modul eller skriv modulen i posten
  (granskningen etapp 3, omgång 5, M).
- **`CasesOptions` är publik i implementationsprojektet** (som `VaultOptions`), eftersom `AddCasesModule` tar den.
  Flytta båda till Contracts eller till ett eget alternativ-gränssnitt om regeln ska vara strikt.
- **Ankarna och flera datorer:** ankarna är per valv och dator. Ett ärende som arbetats med på en annan dator och sedan
  kopieras tillbaka är fortfarande en förlängning av kedjan och godtas, men ett ärende som två datorer ändrat
  parallellt får en rad *case.anchors-replaced* i journalen på den ena när det öppnas, med de ankare som inte nåddes.
  Hur det ska förklaras i gränssnittet bestäms med `/verify`.

### Ärenden i appen (etapp 11)
- **Öppna en kollegas fil** med ärendelösenordet (`ICaseStore.OpenWithPassword`) och lägga till ett eget kuvert
  (`AddOperatorAccess`): ett kommando eller en filväljare, och NC1a som ärendets egenskaper (titel, delning). Ändra
  titeln går i Cases men har inget gränssnitt än.
- **Sökning i kommandoraden** (etiketter, adresser i ärendet, kluster): text som inte är en adress eller tx svarar nu
  *Sökning är inte byggd än*.
- **Moduler som bidrar med kommandon:** `IShellCommand` ligger i SharedKernel, men ett kommando som behöver det öppna
  ärendet måste nu få det från appen (`CaseSession`). Flytta det öppna ärendet till ett kontrakt (till exempel
  `IOpenCase` i Cases.Contracts) när en modul behöver egna kommandon.
- **Ethereum-adressens kontrollsumma** (EIP-55, blandade versaler) kontrolleras inte i igenkänningen; behövs när
  Ethereum byggs. En TRON-tx ser ut som en Bitcoin-txid och känns bara igen med kedjan utskriven (i en länk).
- **Senast öppnade ärendet** föreslås som den `.cut` som sparades senast (filens tid), inte ur en egen lista, så att
  inget om ärenden sparas utanför ärendet. Ett ärende som sparats av en kopia eller synk hamnar då först.
- **Avslut mitt i en öppning eller skapelse** (granskningen etapp 11, M2): `OnExit` sparar det öppna ärendet, men
  väntar inte på en skapelse som pågår; Cases stänger ärendet när värden tas bort, och resten (titel, lösenord) blir
  ogjort utan att sägas. Låt arbetsflödet visa uppgiften som pågår och vänta på den, med en gräns.
- **Svar efter en låsning** (M5): ett kommando eller en skapelse som pågick när Chainsaw låstes sätter sitt svar efter
  låsningen, och det står kvar efter upplåsning; en panel som öppnats på nytt kan tömmas av den gamla skapelsens slut.
  En räknare som låsningen ökar, och svar från en äldre omgång kastas.
- **Välja ett befintligt ärende för en länk** (M7): en låsning stänger ärendet, så efter upplåsning öppnar en länk
  alltid Nytt ärende. Användaren kan `/open` först och klistra in, men ett val (*lägg i ärende …*) vore bättre.
- **Pipens robusthet** (M8): mellan två anslutningar kan en annan användare hinna skapa pipen med samma namn, och
  länkar går då förlorade (inget annat händer); en andra start medan den första startar värden (mer än tre sekunder)
  tappar sin länk. Lyssna innan värden byggs, med en kö, och skapa nästa instans av pipen innan den förra hanteras.
- **En länk medan raden har text** (granskningen etapp 11, omgång 2): länken läggs inte in och svaret försvinner vid
  nästa tangent. Spara den som ett väntande förslag (i ledtråden) tills raden är tom.
- **Testklasserna saknar XML-dokumentation** (M10), som i de tidigare testprojekten.

### Undermenyer i pillret (etapp 11b)
- **Nytt → Etikett, Bevakning och Begäran** är tonade som inte byggda; ändra `SubmenuNeed` i
  `Navigation/SubmenuTable.cs` till `OpenCase` och koppla raden i `ShellViewModel` när gränssnittet finns.
- **Startpunkt med ett ärende** lägger bara markören i kommandoraden; ett eget fält (som i Nytt ärende) kan skissas.
- **Sanktionslistan och det öppna ärendet:** en import kontrollerar inte ärendets adresser
  (`IAttributionService.CheckSanctions`); det görs när grafen byggs.
- **Utgångens namn i titelraden** visas som platsen (eller namnet) först när utgången är ansluten; tunneln är inte
  byggd, så det har inte kunnat provas.

### Låsskärmen och förstagångsinställningen (etapp 10)
- **Raden om bevakningen** på låsskärmen (*En bevakning har slagit till*) när Monitoring finns.
- **Vyn Inställningar:** låsningens tid (nu bara `AutoLockMinutes` i `settings.json`, läst vid start) och fältets
  scen och rörelse (skissens val; nu Drift och stilla kätting).
- **Låsning mitt i inställningen** avslutar den: efter upplåsning visas skalet, och utgången och första ärendet erbjuds
  inte igen. En krasch mellan valvet och profilen ger ett valv utan profil, så att ärenden inte går att skapa.
  Inställningen kunde återupptas tills den är klar (granskningen, M).
- **Fältets loop** stannar tyst om en bildruta kastar ett fel; kunde loggas och startas om. Hela bildrutan kopieras på
  UI-tråden varje ruta (cirka 33 MB per ruta i 4K); kunde sänka takten på stora ytor (granskningen, M).
- **WireGuard-filens text** blir en `string` på vägen till `IExitStore` (samma sak som *Klartext i minnet*).
- **En öppen filväljare** ligger kvar ovanpå låsskärmen om Chainsaw låses medan den är öppen; den visar bara filer.

### Övrigt
- **Registrera länken `chainsaw:`** för användaren (HKCU\Software\Classes\chainsaw med `URL Protocol` och
  `shell\open\command` till `Chainsaw.exe "%1"`), som ett val i vyn Inställningar, och ta bort registreringen där.
  Mottagandet är byggt i etapp 11 (ett exemplar per användare, named pipe, väntar på upplåsning).
- **Guiden:** avsnitten skrivs medan funktionerna byggs i `docs/guide/` (UTXO, adresstyper, common-input-ownership,
  växeladress, CoinJoin och peel chain finns; kvar är spårningsmetoderna och varför ett spår tar slut).
- **Testdata:** `/testdata` med påhittade ärenden och syntetiska tx-mönster, bara i Debug.
