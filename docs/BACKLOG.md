# Backlog

Det som kommer efter 1.0: nya förmågor och de mindre fynden (M) från granskningarna, per område. Det som ska vara
klart till 1.0 står i `docs/TODO.md`. En punkt här flyttas dit om den visar sig behövas, och tas bort när den byggs.
## Kedjor och data
- **Registret som anroparen ger till `IResponseTransfer.Seal(…, SealedResponseIndex)`** (granskningen av NC2a, M2):
  höjden och tx:erna i registret litas på som de ges. Nytt ärende ger det som källan själv räknade fram i samma
  process, men en framtida anropare kunde ge en höjd som får ett grunt svar att räknas som djupt. Registret kunde i
  stället räknas fram på nytt ur svaret av källan (en tolk per sort av svar), så att det aldrig tas emot utifrån.
- **Ethereum efter etapp 23b** (grafen, inspektören, startpunkterna, swap-pillret och kurserna för USDT och USDC är
  byggda). Kvar:
  - **Spårning genom en swap:** pillret visar bytet, men spårningen räknar varje tillgång för sig, så spårade USDT blir
    inte spårad ether hos den som bytte (de stannar som *inte expanderad* hos poolen). Att föra över den spårade
    andelen i bytet är en bedömning (analys med namn och version) och behöver ett beslut om hur andelen räknas.
  - **Swap genom en känd DEX:** pillret sätts på mönstret (den som signerade skickade en tillgång och fick en annan i
    samma tx), utan lista över DEX-kontrakt. En lista över kända DEX:ar och aggregatorer kan visa namnet i *Varför?*.
  - **Tx:ens plats i blocket:** källornas historik ger inte `transactionIndex`, så två tx i samma block ordnas efter
    hash. Det påverkar bara FIFO när en adress både tar emot och betalar i samma block. Läs `transactionIndex` i
    Blockscout-tolkningen (Chains, etapp 25 arbetar där samtidigt).
  - **Välja vad som spåras:** frön väljs av `AccountSeedPicker` (allt som kom in till startadressen, eller en start-tx:s
    rörelser till andra än den som signerade); att välja dem i grafen saknas, som för Bitcoin.
  - **Saldot och snöflingan** räknas ur grafens linjer för en expanderad adress, alltså bara så långt historiken är
    läst (gränsen 50 tx). En adress med längre historik kan visa fel saldo; källans saldouppslag (`balance`,
    `tokenbalance`) vid ett block skulle ge kedjans eget svar.
  - **Kurs per överföring:** varje rörelse värderas vid sitt blocks tid med Pricings förvalda upplösning; en expansion
    ber om högst 30 kurser, resten hämtas vid nästa. Värdet visas bara för linjer på Ethereum (Bitcoins linjer har inget
    värde än, TODO under *Kurser*).
  - **En aktiv adress för inte pengarna vidare:** en expansion som hölls tillbaka vid 50 tx gör adressen delvis läst,
    och spårningen stannar där (granskningen av etapp 23b, M2). En regel om täckning (adressen bär när det lästa når
    bakom den första spårade ankomsten och saldot före är känt) eller en högre gräns för startadressen behövs. Att läsa
    grafen går också igenom alla ärendets sidor för varje expanderad adress (M3); gå bara ner till expansionens
    lägsta block. Fröväljaren lämnar också ett frö när en annan frös pengar når avsändaren genom en adress som inte bär
    pengarna vidare, och säger det inte (M1).
  - **Omexpansionens golv** (granskningen av etapp 23b, omgång 3): golvet tas ur det ärendets sidor når nu, så tidigare
    tx som inte längre nås (ett ärende från före rättelsen, eller en läsning som stannade vid 50 sidor) läses inte om;
    spara höjderna med expansionen. Att expandera en adress igen efter lång tid kan kosta upp till 150 uppslag; visa
    det som väntan. Golvet räknas med en läsning av hela Ethereum-delen; det räcker att läsa adressens egna sidor.
  - **Egna ytor (Z) och områdena** tar inte Ethereum-noder (`OwnArea` lagrar Bitcoin-adresser och txid;
    `GraphAreas.MembersOf` läser bara `GraphAddresses`), så valda Ethereum-aktörer ger en tom yta (granskningen av
    etapp 23b, M4). Använd `Node.Addresses`, eller säg att det inte går.
  - **En start-tx som inte finns i ärendet** (Ethereum, och likadant för Bitcoin) blir bara en rad i grafens problem;
    det finns ingen nod att expandera, så den hämtas inte från appen. Hämta den när startpunkten läggs till, eller visa
    en nod som kan expanderas (granskningen av etapp 23b, M1).
  - **Den förseglade länkens kolumner** `from_txid` och `from_output` håller för en Ethereum-rörelse `token:0x…` och
    positionen; det står i koden men inte i någon beskrivning av tabellerna för den som kontrollerar raderna för hand
    (granskningen av etapp 23b, M6).
  - **Kontrollerna (KF1a)** körs inte på Ethereum; listan säger det. Kontoheuristikerna och Tornado Cash nedan.
  - **OFAC:s ETH-adresser** (`Digital Currency Address - ETH`) läses inte; sanktionskontrollen är Bitcoins.
  - **Länk till annan kedja från Ethereum** lagras i samma förseglade kolumner som ett mynt (`token:0x…` och
    positionen). Spårningen stannar fortfarande vid bryggan som *inte expanderad* eller dess etikett; länken ritas som
    lila linje men för inte de spårade pengarna vidare.
  - **Kontoheuristiker** (`IAccountHeuristic`): börsens insättningsadresser som töms till samma hot wallet, och
    adresser som betalar varandras gas. Kräver att kluster och bevis kan hålla `ChainAddress`.
  - **Tornado Cash** och andra kända kontrakt (DEX, bryggor) som etiketter och slut på spåret (se nedan).
- **Ethereum-källan, det som återstår** (etapp 23):
  - **Etherscan med API-nyckel:** Etherscan tar nyckeln bara som `apikey` i adressen, som förseglas i ärendet (CLAUDE.md:
    nycklar aldrig i ärendet). Byggs först när Network kan skicka en nyckel i adressen och adressen förseglas utan den.
    Blockscouts nyckel skickas som `Authorization: Bearer`, vilket inte är kontrollerat mot en Blockscout med nyckel.
  - **Blockets hash för en tx** hämtas ur `v2/blocks/:höjd` (Blockscouts eget API), eftersom `gettxinfo` saknar den.
    Byts blocket på höjden mellan uppslagen (en omorganisation) kan hashen höra till ett annat block; ovanligt när
    blocket är slutgiltigt efter 64 block. En egen nod (JSON-RPC `eth_getTransactionReceipt`) ger hashen direkt.
  - **Decimaler** kommer från källan (`getToken`), inte ur kedjan; JSON-RPC `eth_call decimals()` är kedjans eget svar.
  - **Interna tx i en adress historik** saknar blockets hash; den hämtas med blocket.
  - **En sida i ett enda block** (fler än sidstorleken i samma block) kan inte bläddras förbi; sidan säger det
    (`IsCutShort`).
  - **Interna tx under ett anrop som återställdes:** bara anrop som själva är märkta som misslyckade, och
    `delegatecall`, `staticcall` och `callcode`, räknas bort. Ett anrop under ett återställt anrop kan vara omärkt hos
    källan; kontrollera mot en riktig trace från Blockscout och räkna bort hela grenen (granskningen av etapp 23).
  - **Blob-gas** (tx av typ 3) ingår inte i avgiften, och validatorernas uttag och dricksen till blockets föreslagare
    finns i ingen av de tre historikerna.
  - **Startpunkten med fel kontrollsumma** säger bara *känns inte igen*; den borde säga att EIP-55-kontrollsumman inte
    stämmer. `0X` (stort X) tas inte längre som Ethereum (granskningen av etapp 23, M).
  - **En Blockscout som ligger efter** kan ge en kort äldre sida som räknas som slutlig, eftersom djupet räknas från
    den högsta höjden ärendet känner (också från en annan endpoint). Samma modell som Bitcoin. Och en sida där en
    tokens decimaler saknades (källan har inte läst tokenen än) blir slutlig med posten överhoppad (`Skipped`).
    Etapp 25:s marginal räknar en endpoint ett block efter som i fas för vanliga uppslag, så frågan om djupet kvarstår.
  - **Decimaler** (granskningen av etapp 23, M): en token som säger fler decimaler än Chainsaw räknar i frågas om vid
    varje uppslag (den kunde sparas som *räknas inte*), ett kontrakt kan i princip byta decimaler, och en post utan
    decimaler i en tokenhistorik (en token källan inte läst, eller en NFT om Blockscout listar dem där) håller sidan
    öppen så att den frågas om, vid varje uppslag så länge posten står kvar (en token utan `decimals()` kan skickas
    till vilken adress som helst). Pröva mot en riktig Blockscout, och ge upp efter några försök eller när tokenens
    eget uppslag säger att den saknar decimaler.
  - **`BitcoinLookup<T>`** används av båda kedjorna och borde heta `ChainLookup<T>`; byts när ingen annan etapp rör
    Chains.
- **Tornado Cash** (användarens fråga 2026-11-07) kommer med Ethereum: kontrakten känns igen som mixer och blir *Spåret
  tar slut här*. Poolerna har fasta belopp (0,1, 1, 10 och 100 ETH), så beloppet säger inget, och de troliga utgångarna
  kräver egna heuristiker ur forskningen: samma adress in och ut, ovanligt gaspris, adresser som betalar varandras gas,
  flera insättningar som tas ut tillsammans och tid. De blir streckade kandidater som mixerns.
- **TRON och USDT (TRC-20)**, efter Ethereums andra del: återanvänder kontomodellen, `IAccountTracingMethod`,
  `KnownTokens.TronUsdt` och *Kan frysas*.
- **Fler kedjor** efter behov: Litecoin, BSC.
- **Det som fortfarande är bundet till Bitcoin efter etapp 20** (står i DEV-LOG, etapp 20):
  - **Efter etapp 23b** (Ethereum byggt vid sidan av): grafens noder har `Chain`, `Accounts` och `Addresses`
    (`ChainAddress`) bredvid `GraphAddresses`, och kontomodellens del är `TraceGraph.Account` med ett eget resultat
    (`AccountTraceResult`). Kvar för Bitcoin-delen: spårningens resultat för mynt (`TraceSeed`, `TracedCoin`,
    `TracedTransaction.Txid`, `TracedAmount.Through`, `TraceEnd.Transaction`) och `UtxoGraph` med Bitcoins typer;
    heuristikkontraktet för konton (`IAccountHeuristic`) när kluster och bevis kan hålla andra adresser än Bitcoins;
    `ITracingService` med en källa per kedja i stället för `IBitcoinSource` och `IEthereumSource` var för sig;
    `GraphScene.Shown` (Bitcoin-nodernas summor räknar BTC) och *Kontroll* i inspektören (bara Bitcoins tx).
  - **Med TRON:** kontrakt och adresser skrivs både i base58 (`T…`) och hex (`41…`); de görs om till base58 i källan
    eller i `Asset` och `ChainAddress`, så att samma kontrakt aldrig blir två tillgångar.
  - **Med nästa UTXO-kedja (Litecoin, Zcash):** `UtxoGraph` får sin kedja och tillgång (i dag räknar
    `TraceCalculator` alltid i BTC), och UTXO-kontraktet en egen tx och egna adresser (eller `BitcoinTransaction` och
    `BitcoinAddress` en kedja), eftersom `BitcoinAddress` inte tar `ltc1` eller `t1`. `CrossChainLink.From` blir ett
    mynt på vilken UTXO-kedja som helst.
- **Facit för Bitcoin (granskningen av etapp 20, M):** utöka `Flows` med en länk till annan kedja, en startpunkt med del
  av ett mynt, en adress som får två betalningar och en obekräftad tx, och spela in facit på nytt mot koden före
  etapp 20 (commiten med facit).
- **`ChainTransactionRef.Of(Txid)`** kontrollerar en txid som redan är kontrollerad med ett reguljärt uttryck, för varje
  överföring i varje bygge av grafen (granskningen av etapp 20, M). Billigt, men kan tas bort med en konstruktor som
  litar på `Txid`.
- **Solana och SOL** (användarens fråga 2026-11-07), efter Ethereum (uppskattningsvis 3–4 etapper): kontomodellen från
  etapp 20 och Ethereum, men tokens (SPL, Token-2022) ligger i egna tokenkonton som måste knytas till ägaren, en tx har
  många instruktioner som läses per program (System, Token, DEX-swappar som Jupiter och Raydium), och historiken kräver
  en arkiverande RPC-källa med API-nyckel i valvet. USDC och USDT på Solana får *Kan frysas*. Swappar mellan tokens och
  bryggor (Wormhole, deBridge, Mayan) är vanlig förvirring och hör till *Förvirringstekniker*.
- **Zcash** (användarens fråga 2026-11-07): den genomskinliga delen (t-adresser) är en UTXO-kedja som Bitcoin och
  passar efter etapp 20 som de andra UTXO-kedjorna, men Esplora finns inte för Zcash, så källan blir en annan (en
  tjänst med API-nyckel eller egen nod med Zebra). Tx som går in i den skyddade poolen (Sapling, Orchard) blir *Spåret
  tar slut här* med orsaken skyddad pool. Senare kan pengar som kommer ut ur poolen föreslås som kandidater ur belopp
  och tid, streckade som mixerns troliga utgångar.
- **Privacy coins:** först *Spåret tar slut här*. Senare forskningsmetoderna (till exempel tidsanalys och kända svagheter i
  äldre Monero-tx, Zcash-tx som går in och ut ur den skyddade poolen med samma belopp).
- **Cross-chain automatiskt:** hitta fortsättningen på den andra kedjan själv (belopp och tid hos swap-tjänster, bryggornas
  egna händelser). Kanten finns i modellen från början.
- **Direkt mot kedjan:** egen nod och eget index (Bitcoin Core med `txindex`, Fulcrum eller electrs eller ett eget index;
  Reth eller Erigon för Ethereum; java-tron för TRON). Kräver en server. Esplora går att köra själv, så Bitcoin blir bara en
  annan adress i datalagret.
- **Bevakning i realtid:** websocket där källan har det (mempool.space har bevakning av adresser), senare ZMQ från egen nod.

## Bitcoin-källan (Chains)
- **Svarshuvuden som ursprung:** se *Utgångar*. Behövs om källans `Date` ska stå bredvid Chainsaws egen tid.
- **Flera källor att jämföra:** samma tx från två Esplora-källor, och en varning om de skiljer sig.
- **Endpointens namn** (etapp 24): namnet tas ur adressens värd och kan inte ändras i sidan Källor.
- **Blockhash i provet vid start** (KL2b, 2026-11-08): kortet för den endpoint som används visar *Höjd 921 404* men
  hashen bara när den är känd, eftersom provet vid start och *Prova alla nu* bara frågar efter höjden (bara formulärets
  *Prova* frågar efter hashen). Skissen visar båda; provet kunde fråga efter blockets hash vid höjden också, ett uppslag
  till per endpoint.
- **Nyckeln som huvud** (etapp 24): Esplora skickar nyckeln som `Authorization: Bearer`. Huvudena som tillåts är
  `Authorization` och `X-Api-Key` (`ExitCredential`); en källa som vill ha något annat lägger till det i Network.
- **Varför en endpoint fick röd prick** (etapp 24, granskningen omgång 2, M): en nyckel som saknas i valvet gör att
  endpointen hoppas över och får röd prick med den allmänna texten *Ett uppslag genom den misslyckades …*; att nyckeln
  saknas står bara i uppslagets text. Raden kunde säga orsaken.
- **Solana i sidan Källor** (etapp 24): chipet står tonat, men `Chain` har inget värde för Solana än.
- **Tx på en adress sida kontrolleras inte mot sina bytes** (etapp 15): bara tx som expanderas för sig (och deras
  spenderare) hämtar `/tx/:txid/hex`. En adress historik med 50 tx skulle kosta 50 uppslag till. Kontrollera dem när
  de visas i inspektören, eller i bakgrunden?
- **Tx:en i blocket** (etapp 15): bytesen binder beskrivningen till txid, men att tx:en ligger i just det blocket
  vilar på källan. `/tx/:txid/merkle-proof` och blockets merklerot skulle binda dem; och om `bits` är nätets rätta
  svårighet på höjden kontrolleras inte.
- **Målet i blockhuvudet** (granskningen etapp 15, M2): huvudet måste nå det mål dess egna `bits` anger, men `bits`
  prövas inte mot nätets gräns (powLimit, `0x1d00ffff` på mainnet), och blockets `bits` och nonce i källans svar
  jämförs inte med huvudet.
- **Ett block som en gång motsäger sitt huvud** (granskningen etapp 15, M3): både blocket och huvudet ändras aldrig,
  så `GetBlockAsync` vägrar för gott. Hämta blockets beskrivning igen i stället (`GetBlockAsync` används inte än).
- **Svar som hämtades när blocket var grunt** (granskningen etapp 15, M4): de räknas aldrig som säkra vid läsning bara
  ur ärendet, och står som *kan ha ändrats* tills noden expanderas igen. Fråga om dem i bakgrunden när utgången är
  ansluten.
- **Grafens läsning gör om arbete per tx** (granskningen etapp 15, M5): bytesen tolkas och hashas, tx:en tolkas om för
  OP_RETURN, och `KnownHeight` söker igenom registren per uppslag. Spara tolkningen per bygge och ett index på höjd.
- **Bytesen tolkas om vid varje läsning** (granskningen etapp 15, omgång 2, M): `StoredTransaction` tolkar och hashar
  de förseglade bytesen, och `GetTransactionAsync` gör det igen. Ge tillbaka tolkningen med tx:en.
- **Takten per endpoint** (etapp 25): förvalet är Chainsaws eget (högst fyra uppslag i sekunden per endpoint på
  internet, ingen paus för en egen nod), inte källornas publicerade gränser. Takten står bara i `sources.json`
  (`IntervalMilliseconds`, aldrig snabbare än förvalet för en endpoint på internet); sidan Källor har inget val för den.
  Skissas om det behövs.
- **Den egna noden provas inte utan utgång** (etapp 25, granskningen M1): ett prov där utgången vägrar sparas inte, också
  när den egna noden svarade, och provet vid start väntar på en ansluten utgång (eller Windows). Uppslagen går ändå till
  noden, men den kan inte bli *i fas* för bevakningen förrän ett prov med utgång gjorts. Prova lokala endpoints för sig.
- **NotFound när alla tillfrågade är otrodda** (granskningen av etapp 25, omgång 2, M; regeln är etapp 24:s): sa varje
  endpoint som frågades 404, ges *finns inte* också när ingen av dem kunde tros (till exempel en enda Blockscout vars
  sökväg ändrats). Kunde i stället ge *misslyckades* med texten att källan inte känner till det.
- ***Prova* i formuläret följer inte takten** (granskningen av etapp 25, omgång 2, M): en adress som prövas innan den
  lagts till har ingen takt, också om samma adress redan finns som endpoint. Två frågor per tryck.
- **Ett behållet prov åldras** (granskningen av etapp 25, omgång 5, M): ett prov som behålls när endpointen bara väntade
  mäts mot den nya omgångens höjd, så block som kommit sedan räknas mot den. På Ethereum (block var tolfte sekund) blir
  ett behållet prov äldre än en halv minut *efter*, tills nästa *Prova alla nu*. Försiktigt för bevakningen, men kunde
  i stället bara ta bort *i fas* och behålla *svarar rätt*. Och en referenshöjd ur ett behållet prov följer med så länge
  endpointen fortsätter be om väntan, också om höjden var fel.
- **Spridningen i Prioriterad** (etapp 25): varje endpoint är en egen grupp, så användarens ordning gäller och nästa tas
  först när den första bett om väntan eller dess kö är längre än fem sekunder. I Snabbast sprids uppslagen inom den bästa
  gruppen (de som svarat rätt). Ändras om användaren vill ha spridning också i Prioriterad.
- **Svaret om spenderade utgångar och omorganisationer** (etapp 15): en tx som bytt block säger det, men ett svar om
  vem som spenderar en output jämförs inte med det förra.

## Klustring och heuristiker
- **Tjänster ur beteende:** en börs insättningsadresser (många insättningar som sveps till samma hot wallet),
  utbetalningar i batch, konsolidering. Etapp 6 slår ihop insättningsadresserna i en sweep men säger inte *börs*.
- **Godkänna ett förslag** (etapp 6): förslag syns men kan inte göras till en sammanslagning. Det behöver en egen
  förseglad post med vem och varför (en manuell sammanslagning som en egen *analys*, `user v1`), och kommer med
  kontrollerna och förslagen (KF1a, etapp 22). Att ångra en uppbrytning byggdes i etapp 21.
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

## Förvirringstekniker (användarens beslut 2026-11-07)
Allt som används för att förvirra spårningen ska kännas igen och analyseras, med så mycket automatiska kontroller och
förslag som möjligt (CLAUDE.md, *Förvirring analyseras*). Katalogen, med det som redan står ovan och under Spårning:
- **Centrala mixers:** kända adresser och mönster per mixer, troliga utgångar ur belopp minus avgift och tid.
- **Mixer efter mixer** (byggt 2026-11-08 som `mixer-chain v1`). Återstår: en mixer utan etikett mitt i kedjan syns
  inte (förslag: känna igen en trolig mixer ur beteende); när flera delar går in i nästa mixer tillsammans följs bara
  en av dem, och stegets belopp är den delen; kedjan grenar sig inte när två utgångar går till olika mixers.
- **CoinJoin** (Wasabi, Whirlpool och efterföljare, JoinMarket): finns som heuristik (etapp 6), fler mönster ovan.
  Förslag: utgångar ur CoinJoin som slås ihop igen efteråt (toxic change, ihopslagning av mixade mynt).
- **Tornado Cash och andra zk-mixers:** under *Kedjor och data*.
- **Peel chain:** finns (etapp 6), med fler inputs ovan. Kontroll: en lång peel chain som slutar vid en börs.
- **Uppdelning och ihopslagning** (smurfing, fan-out och fan-in): kontroll som visar när ett belopp delas i många
  likstora delar eller samlas igen, och förslag att följa summan.
- **Många hopp och fördröjning** (layering): kontroll av hopp där pengarna bara passerar (in och ut snabbt, nästan
  samma belopp), som kan fällas ihop till en kedja i grafen.
- **Byte av kedja och token** (bryggor, swap-tjänster utan konto, DEX): kända kontrakt och tjänster känns igen som
  *Fortsätter på annan kedja*; förslag på fortsättningen ur belopp och tid (under Spårning).
- **Nästlade tjänster** (en tjänst som har konto hos en börs): beteende som insättningar och uttag i mönster.
- **Privacy coins och skyddade pooler** (Monero, Zcash, Litecoin MWEB): slut på spåret; förslag på uttag ur poolen ur
  belopp och tid.
- **PayJoin och ändrade fingeravtryck:** kan bara anas; kontroll som säger när klustringen är osäkrare.
- **Damm och adressförgiftning** (dusting, address poisoning): små belopp som skickas för att förvirra eller lura;
  kontroll som tonar ner dem så att de inte drar spåret fel.
- **Jämna belopp i fiat och tidsmönster** (tidszon ur aktiva timmar): stöd för växeln och för vem som står bakom.
Ramverket, listan (KF1a) och områdena (KF1b) är byggda i etapp 22 med CoinJoin, peel chain, plånböcker och mixerns
kandidater; resten av katalogen ovan läggs till som nya analyser (`ICheckAnalysis`) med namn och version.

## Kontroller, förslag och områden (etapp 22)
- **Mixerns kandidater ur kedjan:** `mixer-outputs v1` prövar bara det som finns i grafen. Att söka igenom blocken i
  tidsfönstret efter insättningen (genom utgången, med gränser) behövs för att hitta kandidater som inte är hämtade.
  Också kända avgifter och belopp per mixer.
- **Delarna tillsammans** (byggt 2026-11-08 som `mixer-parts v1`). Återstår: fler än fyra delar och fler än de 20
  första utbetalningarna prövas inte; samma utbetalning kan stå både som hel utgång i `mixer-outputs` och, för en
  annan insättning, som del; bara direkta spenderingar mellan delar utesluts, inte längre led.
- **Mixer efter mixer, kvar efter granskningen 2026-11-08:** en tx som betalar in till två mixeradresser följs bara
  på den första (den andra blir en egen kedja); avgifterna längs kedjan anges i procent av den första insättningen,
  fast varje stegs avgift räknas mot sin egen; `mixer-chain` räknar om `Weigh`, `MixerPartsCheck.Best` och vem som
  spenderar ett mynt per insättning (spara per insättning och bygg ett index över spenderade mynt).
- **Växeladressen i KF2b** (bandens bredd efter belopp, kolumnen *SEDD*) är inte byggd: det finns ingen analys som
  föreslår växeladresser som förslag (Clustering gör det själv). Bilden kan bara rita band efter poäng.
- **Brickorna under förslaget** står i en tabell i appen per analys (`SuggestionWords`), utan siffror (skissens *48 h*
  är analysens tidsfönster, som bor i modulen). En analys bör själv kunna ge sina brickor.
- **En kandidats tid** räknas ur blocken förslaget bygger på; en kandidat vars tx inte finns bland dem får ingen tid.
- **Menyns val sparas inte** i ärendet; alla sorter visas när ärendet öppnas.
- **Kontrollerna körs vid varje läsning av grafen**, och varje körning frågar Clustering om varje tx
  (`GetConclusions`). Det räcker för hundratals tx; för stora ärenden behövs en fråga för alla tx på en gång och att
  bara köra om när grafen ändrats.
- **Beslut om en kandidat som analysen inte längre ger** (granskningen etapp 22, M): en avgjord kandidat behålls bara
  om körningen ger den. Blir dess adress märkt som mixer, eller lämnar dess tx grafen, syns beslutet inte längre (det
  står kvar förseglat). Tjänsten bör bära med sig avgjorda kandidater ur det senast förseglade.
- **Beslut mellan läsning och förseglning** (granskningen etapp 22, M): `CheckService.Run` läser besluten före
  analyserna; ett beslut som förseglas under tiden gör att körningen förseglar en kandidatlista utan det. Nästa körning
  rättar det, med en extra förseglad post.
- **Historiken (AN1b, etapp 21)** bör visa beslut om förslag och egna ytor. `ICheckService.RemoveArea` tar bort en yta
  förseglat, och ett nytt beslut om samma kandidat ersätter det förra.

## Spårning (Tracing)
- **LIFO och poison** (etapp 7): kontraktet `ITracingMethod` tar emot fler metoder, men LIFO behöver en definition
  (inom en tx är ordningen redan FIFO:s; LIFO handlar om en adress saldo över tid), och poison ger inga belopp som går
  jämnt ut, så den behöver visas som *berört* i stället för ett belopp. Se `docs/KRYPTOSPARAREN.md`, fråga 1.
- **Fråga om det som kan ha ändrats** (etapp 7): grafen visas bara ur ärendet (`LookupMode.CaseOnly`). Ett mynt som
  låg kvar visas som *kan ha flyttats* tills noden expanderas igen. En knapp eller ett kommando som frågar om alla
  sådana mynt på en gång, och bevakningen som gör det i bakgrunden, behövs.
- **Tidsfönster för en adress historik** (etapp 7): expansionen tar de nyaste tx upp till gränsen (50 som förval).
  För en adress med lång historik behövs ett tidsfönster runt händelsen i stället.
- **Grafens prestanda vid tusentals noder** (etapp 12): ytan ritar bara det som syns, men varje drag räknar om alla
  kurvor och varje hovring prövar alla linjer. Ett rutnät (spatial index) och att bara räkna om linjerna till den
  dragna noden behövs om graferna blir mycket stora.
- **Hitta fortsättningen på en annan kedja själv** (CLAUDE.md, *Cross-chain*): belopp och tid hos swap-tjänster,
  bryggornas egna händelser. I v1 knyts den för hand.

### Ledtrådsnoden, valvyn och samlingsnoden (EX1, EX2, EX2b, omgjorda 2026-11-08)

Omgjort efter skissen 2026-11-08: valvyn med spiral och tidsbåge, filterpanelen, markeringarna och den cirkulära stöten,
högen som kortlek och korten i en ring. Rättat ur granskningen 2026-11-07: ledtrådens räkning för en tx-expansion,
`held_back` som nollställdes av en delvis omexpansion, högens summa per riktning, valvyns filter och urval som
nollställdes, *Lägg tillbaka* under alla medlemmar, `first_expanded_utc`, högerklick över valvyn och högen, reglagen
som filtrerade två gånger och villkoret för sidan. Kvar:
- **Byggt 2026-11-08:** scenen byggs i bakgrunden med *Visa dem* (`GraphReading`), pennor och texter i högen, valvyn
  och beloppen cachas (`DrawCache`) och beloppens krocktest ligger i ett rutnät (`RoomGrid`), bevakade adresser och
  bevakningarnas motparter viks aldrig in i en hög, ledtrådens linje är `GraphEdgeKind.HeldBack`, och högens och
  ledtrådens nycklar följer var de senast flyttades i stället för ankarets representant. Kvar av det:
  - Valvyns streckade guide och rombens hörnmärke byggs som ny geometri per bildruta; de kan cachas per vy.
  - En bevakad aktör i en hög är kvar som medlem i Tracings hög och ritas bara för sig. Om en sådan ska räknas i
    högens *N mottagare* i modellen (till exempel i en begäran) avgörs när det behövs.
- **Valvyn finns bara för en Bitcoin-adress:** en tx-expansions mottagare och en Ethereum-adress hämtar fortfarande
  nästa del med ett klick. Historiken läses högst 2 000 tx bakåt (`HeldBackList.Most`), och högst 500 till runt den
  spårade ankomsten när grafen har en tx vid adressen att läsa bakåt från; längre historik behöver ett tidsfönster
  mot källan (se *Tidsfönster för en adress historik*).
- **Avsteg från skissen:** pillen står där hopplayouten lägger den i adressens kolumn, inte alltid rakt under adressen;
  högens linje har summan mitt på linjen, inte vid högen; inspektörens kolumn står kvar bredvid valvyn; motpartens
  grupp *Spel* omfattar all gul sort (spel, handel, presentkort) som i skissen. Etiketten *Rapporterad* (Chainabuse)
  har ingen form än och sorteras som okänd i valvyn, men räknas som negativ och pulserar.
- **In, ut eller alla** i högen är fortfarande två högar, en på var sida om adressen; valvyn har filtret.

## Kommentarsnoden (KN1a, byggd 2026-11-08)

- **Länkar:** `chainsaw:` till något som inte finns i grafen säger det bara; kanske ska det kunna bli en startpunkt.
- **Kvar efter granskningen av KN1a 2026-11-08** (tråden och soptunnan står i `docs/TODO.md`): när en hög
  eller valvy är utfälld döljs lagret med `Opacity=0` men fokus kan ligga kvar i en dold redigerare;
  `PlaceComments` gör en ny `MatrixTransform` per omritning och `DropShadowEffect` per lapp kostar vid panorering
  med många lappar.
- **Fler noder med storlekshandtag** (användarens önskemål 2026-11-07): kommentarsnoden har det; vilka fler som får
  det (samlingsnoden EX2b, egna ytor) avgörs när de skissas.
- **En skrivning i kö när ärendet stängs:** lappen som skrivs i förseglas innan ärendet släpps, men en skrivning som
  redan står i kö (till exempel en ändring som väntar på disken) väntas inte in, eftersom den återkommer på UI-tråden.

## Attribution
- **EU:s sanktionslista** (och andra) bredvid OFAC SDN, som etapp 8 läser ur en sparad XML-fil.
- **Kan frysas** (CLAUDE.md, *Sorterna*): markeringen för adresser med USDT eller USDC hör till Ethereum och TRON. SDN
  har redan adresser för `USDT`, `ETH` och `TRX`, som etapp 8 läser in och räknar men ännu inte slår upp.
- **Chainabuse som underleverantör** (användarens beslut 2026-11-08): Chainabuse (TRM Labs,
  https://docs.trmlabs.com/guides/chainabuse/welcome-to-chainabuse-api) samarbetar med brottsbekämpande myndigheter.
  Chainsaw hämtar rapporterna för en adress ur deras API, genom utgången och med nyckeln i valvet; bara adressen
  skickas (princip 4), svaret förseglas i ärendet (princip 1) och rapporterna blir etiketter som är bedömningar med
  källa, aldrig fakta (princip 2), eftersom de skrivs in av allmänheten. **Hämtningen är byggd 2026-11-08**
  (`IAttributionProviders`, `ChainabuseProvider`): Bitcoin och Ethereum (TRON när etiketter går att sätta där), en
  etikett *Rapporterad* per kategori, varje svar förseglat också vid avslag. **Sidan i inställningarna är byggd
  2026-11-08** (Källor, under *Etiketter*: strömbrytaren, av som förval, och nyckeln i valvet; `AttributionSwitches`
  har namnen på dem som är på, sparade i `settings.json`, för grafen att skicka till `LookupAsync`). Knappen R i
  ikonlisten är byggd; automatiska uppslag står i `docs/TODO.md`. **Batchning är oklar:** ett anrop är en fråga
  om en adress med högst 50 rapporter (en sida); `address` är en sträng och ingen batch-endpoint finns dokumenterad,
  fast guiden säger *one or more* (fråga 7 under *Väntar på dig*). Sorten *Rapporterad* ritas som okänd i
  grafen tills den har en form (rött, risk). Rapportens beskrivning, förluster och kategorier som API:t lägger till
  läses inte än. Efter granskningen 2026-11-08: etikettens skäl står kvar med det första antalet rapporter när fler
  kommer in (bara journalen och det nya förseglade svaret visar det nya antalet); en tillbakatagen rapport hålls
  borta ur biblioteket för alla ärenden, också för senare rapporter i samma kategori; ett ärende som stängs mitt i ett
  uppslag tappar svaret utan att försegla det. *Rapporterad* är en röd markering i hörnet (`NodeBadge.Reported`),
  inte en sort, så att noden behåller sin sort.
- **Flera attributionskällor samtidigt, var och en med en strömbrytare** (användarens beslut 2026-11-08): Chainsaw
  hämtar från alla källor som är på, inte bara en, och varje källa kan stängas av.
- **Öppna källor:** till exempel ransomwhe.re (öppen datamängd med ransomware-adresser), WalletExplorer (äldre BTC-kluster). Varje källa granskas för licens och kvalitet innan den byggs in. `LabelSource.OpenSource`
  finns i kontraktet.
- **Etiketterna läses två gånger per bygge** (granskningen etapp 15, M6): `LabelTrailEnds` och grafens etiketter läser
  var för sig, och klustren byggs om för etiketterna. Ett gemensamt underlag per bygge.
- **Biblioteket vid avslut och låsning** (granskningen etapp 15, M8): `Absorb` körs när ett ärende öppnas och stängs
  med `/close` eller byts, men inte när Chainsaw avslutas eller låses; etiketterna kommer då in nästa gång ärendet
  öppnas. `ShareInto` körs vid varje läsning av grafen, och etiketter som bara tagits tillbaka läser inte om grafen.
- **Ett bibliotek som inte går att läsa syns inte** (etapp 15, granskningen M7): grafen och ärendet fungerar, men etiketterna från andra
  ärenden kommer inte, och inget säger varför. En toast eller en rad i vyn Etiketter behövs.
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
- **Uppbrytningen mot den riktiga klustringen** (granskningen etapp 16, M4): `InspectorForms.BreakOut` testas mot
  ett handbyggt kluster i App.Tests; ett test i Clustering.Tests som sparar bevis, bryter ut genom `Split` och läser
  klustret efteråt (blandat val i en CIO-länk med tre adresser, en senare länk med annan heuristik, valda adresser som
  bara hänger ihop genom en som stannar) vore säkrare. Formuläret läser ändå klustret efteråt och säger till om en
  adress är kvar.
- **Källorna *Beställningen* och *Svar från börs*** (etapp 16, valt av sessionen): formuläret sparar dem som egna
  etiketter (`LabelSource.Own`) med *Ur beställningen:* eller *Ur svar från börs:* först i skälet, och en öppen källa
  med användarens namn på källan och *läst åååå-mm-dd* som version. Egna värden i `LabelSource` (och kanske en
  hänvisning till dokumentet) vore tydligare i *Varför?* och i rapporten.

## Kurser (etapp 14)
- **Inte kontrollerat i dokumentationen** (bara med ett riktigt anrop 2026-11-07): att Riksbankens adress
  `Observations/{serie}/{från}/{till}` är den dokumenterade (FAQ:n nämner datumintervall men visade inte formen), att
  värdet är kronor per *en* enhet (stämmer med nivån, cirka 10 kr per dollar), vilken tid på dagen kursen sätts, och
  att Bitstamps perioder börjar på jämna multipler av sin längd från 1970 och att en period utan handel har volym noll
  och föregående pris.
- **Perioder före källans historik** (granskningen, omgång 1 M6 och omgång 3 M-3): ett svar utan kurs (Bitstamp före
  2011, Riksbanken före 1993) sparas men pekas inte ut som slutligt, så varje uppslag som inte sker samtidigt frågar
  igen och sparar ett nytt svar. Kan pekas ut som *ingen kurs* när perioden är slut.
- **Riksbankens dygnsgräns** räknas bara sedan Chainsaw startade och bara på den här datorn; datorer bakom samma utgång
  delar gränsen utan att veta om det. Kan sparas i appdata, och en nyckel i valvet höjer gränsen.
- **Fler källor och tokens:** TRX när TRON byggs (ETH sedan etapp 23, USDT och USDC på Ethereum sedan etapp 23b, efter
  kontraktet och aldrig som en dollar av sig själv; TRON:s USDT har ingen källa än); en
  andra kryptokälla att jämföra med (till exempel Kraken eller Coinbase för nyare perioder); Riksbankens nyckel (200
  anrop i minuten) i valvet om gränsen på 5 i minuten blir ett problem.
- **Ett gemensamt lager för råsvar:** Chains och Pricing har var sin likadan tabell och kod för förseglade råsvar. Kan
  bli ett kontrakt i Cases när en tredje modul behöver det.

## Säkerhet
- **Klartext i minnet:** API-nycklarnas hemligheter och profilen ligger som `string` medan valvet är upplåst och kan inte
  nollställas, och JSON-bufferten vid kryptering är inte fäst (pinned). Kan skärpas med en egen serialisering till en
  fäst buffert och `char[]` för hemligheterna (granskningen etapp 2, M).
- **Sparning som en låsning hinner före:** filen skrivs men anroparen får inget besked (konsekvent, eftersom nästa
  upplåsning läser filen). Kan bli ett `Locked`-fel efter skrivningen om gränssnittet behöver veta (granskningen, M).
- **Unicode-normalisering av huvudnyckeln** (NFC) innan den härleds, så att samma nyckel skriven med olika
  inmatningsmetoder ger samma resultat.
- **DPAPI för bevakningsprocessen** (CLAUDE.md, *Bevaka*): bevakningens adresser krypteras för Windows-användaren, utanför
  valvet. Byggs med bevakningen.

## Utgångar (Network)
- **Tunnelns port på andra system än Windows:** på Windows hålls porten exklusivt (`SO_EXCLUSIVEADDRUSE`) och
  Chainsaw frågar efter varje anslutning om hjälpprogrammet lever, så en anslutning kan inte ha nått något annat
  program. Byggs Chainsaw någon gång för ett annat system behöver samma garanti prövas där.
  En lucka finns kvar (granskningen etapp 13, omgång 3, M): när hjälpprogrammet kraschar stänger Windows troligen
  dess handtag (och porten) innan processen räknas som avslutad, så under ett ögonblick kan ett program som körs som
  samma användare ta porten medan `IsRunning` ännu är sant. Stängs med en kontroll av vem som äger anslutningens andra
  ände (`GetExtendedTcpTable` med processens id) eller med ett bevis från hjälpprogrammet (HMAC över en nonce med en
  nyckel från stdin) innan Chainsaw skickar något.
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
- **Raden i en vägrad WireGuard-fil:** vilken rad som är fel står bara i `ExitConnectException` (hjälpprogrammet nämner
  rad och nyckel, aldrig ett värde); rutan UT1a säger bara *Filen godtas inte*. `ExitStatus` behöver ett fält för raden
  om rutan ska visa den (granskningen etapp 13, M).
- **En proxy som kräver inloggning** (etapp 19): med Windows som utgång går uppslagen genom systemets proxy, men
  Chainsaw skickar aldrig Windows-användarens uppgifter till den (`DefaultProxyCredentials` är tomt), eftersom en
  proxy som hittas automatiskt (WPAD) annars kunde få dem. En sådan proxy svarar 407 och uppslaget misslyckas ärligt.
  Om det behövs i tjänsten: ett val i inställningarna, bara för en proxy som är inställd för hand.
- **Felen nämner utgången också genom Windows** (etapp 19, granskningen M): *Svaret kom inte i tid genom utgången*
  och *Utgången kopplades ner medan uppslaget pågick* (`ExitException`) står också när uppslaget gick genom Windows
  eller när vägen ut byttes. Texter efter vägen (leasen vet den) behövs.
- **En ansluten utgång i läget Windows** (etapp 19, granskningen M): listan i rutan är tonad och går inte att använda,
  så en ansluten utgång kopplas ner bara genom att välja VPN igen. Strömknappen kunde vara kvar för den som är på.
- **Inställningar som inte går att spara** (etapp 19, granskningen omgång 2, M): bara ett misslyckat val av VPN tar
  bort en fil som annars ger Windows vid nästa start; en strängare låsning (kortare tid, lås med Windows) som inte går
  att skriva ger den gamla tillbaka vid nästa start. En profil som inte gick att spara efter att dialogen stängts sägs
  inte, och två profilsparningar tätt efter varandra kan bli klara i fel ordning (de kunde köas som inställningarna).
- **Intranätnamn genom en proxy** (etapp 19, granskningen M): med Windows och en proxy slår proxyn upp namnet, så
  Chainsaws kontroll av adresser som inte är globala gäller bara direkta anslutningar.
- **Namnuppslag med Windows** (etapp 19): källans namn slås upp av datorn (eller proxyn), som i varje program på
  datorn; det är meningen med valet. Adresser som inte är globala vägras ändå vid en direkt anslutning.
- **Den egna nodens namn och certifikat** (etapp 25, granskningen M4): namnet på en endpoint märkt *Lokal*
  (`esplora.lan`) slås upp av datorns resolver, utanför utgången; bara namnet lämnar datorn, och bara till den
  resolver datorn använder. Certifikatet kontrolleras mot rötterna som är installerade i Windows, så en nod med
  självsignerat certifikat måste ha sin rot installerad. Fäst nodens certifikat (pinning) i Källor om det behövs.
- **100.64/10 räknas inte som eget nät** (etapp 25, granskningen I1): leverantörens delade adresser (CGNAT, också
  Tailscale) nås inte som egen nod, eftersom de kan ligga utanför användarens nät. Ändras bara efter användarens beslut.
- **Ett kortkommando för utgångarna** (etapp 17): rutan öppnas bara med musen eller Tab till utgången i titelraden.
- **Tunnelns hjälpprogram och Go:** `chainsaw-tunnel.exe` är byggd med Go 1.27.3 (låst i `go.mod`) och checkad in
  med sin hash; den byggs om med `tools/Chainsaw.Tunnel/build.sh` (som kör `govulncheck`) när Go eller beroendena får
  säkerhetsrättelser, minst när en Go-version slutar få stöd.
  `dotnet test` kör inte Go-testerna; de körs av byggskriptet. Signera programmet med organisationens certifikat när
  Chainsaw signeras.
- **Tunnelns IPv6:** servern nås över IPv4 när `Endpoint` är ett namn med båda sorterna. En utgång som bara svarar på
  IPv6 når man med en adress i `Endpoint`.
- **Bevakningsprocessen och utgången:** bakgrundsprocessen (CLAUDE.md, *Bevaka*) ska gå genom Chainsaws utgång utan
  valvet. Hur den får konfigurationen (DPAPI för Windows-användaren) bestäms med bevakningen.

## Ärenden
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
- **Verktyget `Chainsaw.Recovery`:** på Windows får den privata nyckelns fil mappens rättigheter, inte en egen ACL
  bara för ägaren; med omdirigerad indata läses lösenordet som en `string` som inte kan nollställas; punkten på kurvan
  kontrolleras av plattformen (OpenSSL, CNG), inte av en egen kontroll (granskningen etapp 9, M).

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

## Ärenden i appen (etapp 11)
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

## Begäran (BG1b)
- **`Compose` körs på UI-tråden** (granskningen, M8): den läser bara ärendets nummer och profilen i minnet och går
  snabbt, men ett mycket stort uttag med hundratals insättningar kan kännas. Flytta den till `Task.Run` med en
  avbrytbar omgång per ändring om det märks.
- **En fil som inte gick att skriva klart** tas bort (M5), men det finns inget test som framkallar ett skrivfel.

## Rapport (RP1b)
- **Godkända förslag i slutsatserna:** en godkänd trolig utgång ur en mixer (KF2b) kommer inte med som slutsats med
  analysens namn och version; rapporten tar kluster, etiketter och spårningsmetoden.
- **Tillbaka till rapporten från grafen:** skissen har en knapp *← Rapport Esc* i grafen när man kommit dit med
  *Ändra i grafen*; nu tar Ctrl+8 tillbaka.
- **Visa i mappen** i toasten efter exporten (skissen) finns inte; filen står under förhandsvisningen med sökväg och
  SHA-256 i verktygstipset.
- **Markeringen** är grafens val med Ctrl+klick eller en ruta, inte Shift-klick som skissen säger, eftersom Shift redan
  används i högen.

## Undermenyer i pillret (etapp 11b)
- **Nytt → Etikett** är tonad som inte byggd (en etikett sätts sedan etapp 16 i inspektören med L); ändra
  `SubmenuNeed` i `Navigation/SubmenuTable.cs` till `OpenCase` och koppla raden i `ShellViewModel` när vyn Etiketter
  finns. Begäran är kopplad sedan BG1b.
- **Startpunkt med ett ärende** lägger bara markören i kommandoraden; ett eget fält (som i Nytt ärende) kan skissas.
- **Sanktionslistan och det öppna ärendet:** en import kontrollerar inte ärendets adresser
  (`IAttributionService.CheckSanctions`); det görs när grafen byggs.

## Låsskärmen och förstagångsinställningen (etapp 10)
- **Inställningarna, Utseende:** fältets scen och rörelse (skissens val; nu Drift och stilla kätting) är tonade med
  *Inte byggt än* i ST1a.
- **Låsning mitt i inställningen** avslutar den: efter upplåsning visas skalet, och utgången och första ärendet erbjuds
  inte igen. En krasch mellan valvet och profilen ger ett valv utan profil, så att ärenden inte går att skapa.
  Inställningen kunde återupptas tills den är klar (granskningen, M).
- **Fältets loop** stannar tyst om en bildruta kastar ett fel; kunde loggas och startas om. Hela bildrutan kopieras på
  UI-tråden varje ruta (cirka 33 MB per ruta i 4K); kunde sänka takten på stora ytor (granskningen, M).
- **WireGuard-filens text** läses som fästa tecken som töms (`Exits/WireGuardFile`, etapp 17), men blir en `string`
  i anropet till `IExitStore.Add`, som tar en (samma sak som *Klartext i minnet*). Kräver ett kontrakt med tecken.
- **En öppen filväljare** ligger kvar ovanpå låsskärmen om Chainsaw låses medan den är öppen; den visar bara filer.

## Bevakning (etapp 26)
- **Starten från en höjd i fas** (granskningen omgång 1, M5): starten är kedjans höjd från `GetTipHeightAsync`, som
  frågar vilken endpoint som helst; ligger den ett block efter kan pengar i det nyaste blocket före bevakningen räknas
  som en träff. Höjden borde komma från en endpoint i fas (eller provets högsta höjd).
- **Omorganisation** (granskningen omgång 1, M6): en träff förseglas en gång per tx; byter tx:en block står den gamla
  hashen kvar. Kontrollera träffarnas block mot kedjan och säg det.
- **Mycket aktiva adresser:** en kontroll läser högst fem sidor bakåt. Har en adress fler nya tx än så går kontrollen
  vidare från det äldsta block den läste och sparar luckan (*tx under block N kan ha missats*) med kontrollen; tx i luckan
  kan ha missats. Samma när källan inte kan bläddra längre (en full sida i ett block) eller inte läst historiken
  färdigt. Läs luckan i efterhand med en markör, eller med källans egen bevakning (websocket).
- **Flera adresser på Ethereum** (granskningen omgång 5, M): `WatchCheckResult.CompleteAbove` tar den högsta över
  adresserna, så en adress som håller kontrollen tillbaka (ett block utan hash) kan köras över av en annans högre golv.
  Kan inte hända i dag (bara Bitcoin har kluster); håll isär golv och spärrar innan kluster på andra kedjor bevakas.
- **Motparter som kluster** följer inte klustringen: en Ctrl+klickad klusternod lägger dess adresser då, inte de som
  kommer senare (noden själv följer klustringen vid varje kontroll).
- **Ringarna** ritas genom att grafen ritas om (15 gånger i sekunden medan en osedd träff syns). Ett eget lager för
  ringarna skulle bara rita dem; pennorna skapas per bildruta.
- **Pengar ut ur en CoinJoin** räknas högst till det de bevakade adresserna satte in, minus växeln; vem av utgångarna
  som är deras säger kedjan inte, och linjen får inget belopp när träffen har flera motparter. Det taket är en
  övre gräns, inte ett faktum (granskningen omgång 3, M4): träffen borde bära en markering och visas som *högst …*
  (streckat) i stället för som fakta i historiken och listan.
- **Varje råsvar förseglas** också för bevakningen: höjden en gång i minuten och adressernas första sida vid varje nytt
  block. Ärendet växer med bevakningar som går länge; att slå ihop lika svar (samma hash) vore en ändring i Chains.

## Bevakning i bakgrunden (etapp 27)
- **VPN i bakgrunden:** WireGuard-filerna ligger krypterade i valvet, så bakgrunden kan inte starta en tunnel utan
  huvudnyckeln och gör inga uppslag med VPN vald. Att lägga en utgångs nycklar under DPAPI utanför valvet kräver
  användarens beslut (*Väntar på beslut*).
- **Kluster i bakgrunden** följer klustringen som den var när planen skrevs (när ärendet senast var öppet), inte nya
  adresser som kommer med senare.
- **Ett ärende som tas bort eller flyttas** ligger kvar i planen och bevakas tills det öppnas och dess bevakningar
  pausas. Rensa planen från ärenden som inte längre finns (ärendenas id i `Ärenden`).
- **Höjden som inte ledde till någon kontroll** förs inte in i något ärende (den hör inte till ett ärende); höjden som en
  kontroll byggde på förs in med ärendets första post i varvet.
- **Endpoints med API-nyckel** fungerar inte i bakgrunden (nyckeln ligger i valvet); de hoppas över och nästa tas.
- **Aviseringen i Windows** kommer från en ikon i meddelandefältet (`Shell_NotifyIcon`), som Windows visar som en toast.
  En riktig toast med appens identitet (AUMID) kräver att Chainsaw registreras som app; pröva om det behövs.
- **Kön växer** med en post per kontroll (på Ethereum ungefär en i minuten per bevakning), med alla råsvar, så länge
  ärendet inte öppnas (granskningen omgång 1, M3). Slå ihop poster per bevakning eller sätt ett tak, och säg det.
- **Sammanfattningsraden i journalen** (*Bakgrundens bevakning förd in*) skrivs efter posterna; kraschar Chainsaw
  mitt i kan den saknas för de poster som redan förts in (granskningen omgång 1, M2). Träffarna har alltid sin egen rad.
- **Två takter mot samma endpoints** (granskningen omgång 1, M7): medan Chainsaw är öppet har appen och bakgrunden var
  sin takt per endpoint, för olika ärenden. Tillsammans kan de fråga fortare än en endpoint vill; `429` följs ändå.
- **Adresserna ligger kvar i planen** (under DPAPI) när bakgrunden slås av, så att den kan slås på igen utan att varje
  ärende öppnas (granskningen omgång 1, M8). Överväg att tömma planen när den är av.

## Historik, ta tillbaka och ångra (etapp 21)
- **Hämtad i historiken:** en adress visar *Först in* med blocket men inte när den hämtades, eftersom
  `IBitcoinSource.GetAddressTransactionsAsync` inte kan läsa ur ärendet utan att fråga källan. Ett läge bara ur ärendet
  (som för tx) skulle ge raden *Hämtad · block · källa*.
- **Ta tillbaka en egen yta** (KF1b, Z) när ytorna finns (etapp 22): samma rad och samma formulär i historiken.
- **Äldre ärenden** (före etapp 21) har en journalrad per uppbrytning och visar en rad per länk.
- **Test saknas** för en omläsning av grafen medan en ångring sparas (granskningen omgång 3, M4).
- En adressnod visar inte en klusteretikett som sitter på en annan adress i ett kluster den inte är en del av i grafen
  (granskningen omgång 1, M5).

## Testärendet (/testdata)

- **Kan frysas** syns inte i testärendet: det kräver USDT eller USDC på Ethereum, och Blockscout-källan går inte lika
  lätt att fejka som Esplora (tx, kvitton med Transfer-loggar, tokenuppgifter). En påhittad Ethereum-källa på samma sätt
  (en egen `ISourceApi` i Debug) och en del av berättelsen på Ethereum fyller luckan; TRON när den kedjan byggs.
- **Kurser** för testärendets tider hämtas från Bitstamp som i ett riktigt ärende (bara tiden skickas, aldrig
  adresserna). En påhittad kurskälla i Debug skulle göra testärendet helt fritt från nätet.
- **Uppbrytning** och **ett avgjort förslag** finns inte från början; användaren provar dem själv. Lägg gärna till ett
  steg för var och en, så att historiken (AN1b) har något att visa direkt.
- **Kollegans etiketter** (Garantitorget, Nordbörsen) ligger kvar i etikettbiblioteket på datorn efter `/testdata`. De
  gäller bara påhittade adresser och kan aldrig träffa en riktig, men biblioteket saknar ännu ett sätt att rensa dem.
- **Valvyn i testärendet:** Navet (66 tx, många motparter) och gruvbelöningens OP_RETURN finns i testärendet; en
  adress med en spårad ankomst, en mixer och en utbetalning nära det spårade beloppet bland det som hölls tillbaka
  saknas, så att markeringen och tidsfönstret runt ankomsten syns i `/testdata`.
- **Det som byggts 2026-11-08** saknas: en insättning som betalas ut i delar (`mixer-parts`), mixer efter mixer
  (`mixer-chain`), något i soptunnan, en kommentarsnod och en egen yta.

## Övrigt
- **Guiden:** avsnitten skrivs medan funktionerna byggs i `docs/guide/` och byggs in i programmet. Ett nytt avsnitt
  läggs i indexet i `src/Chainsaw.App/Guide/GuideLibrary.cs` (annars hamnar det under *Övrigt*), och länkar mellan
  avsnitten skrivs `[text](fil.md)` eller `[text](fil.md#ankare)`.


## Vyerna efter 1.0

- **Tidslinje** och **Flöde (Sankey)** är bekräftade vyer men kommer efter 1.0 (användarens val 2026-11-08). Tidslinjen
  under grafen (TL1a, TL2a) och linjernas tjocklek visar mycket av det tills dess. De skissas först.
