# Backlog

Nya förmågor och de mindre fynden (M) från granskningarna, per område. **Allt här görs före 1.0** (användarens
beslut 2026-11-08); ordningen står i `docs/TODO.md` under *4. Backloggen*. En punkt tas bort när den byggs.
## Kedjor och data
- **Registret som anroparen ger till `IResponseTransfer.Seal(…, SealedResponseIndex)`** (granskningen av NC2a, M2):
  höjden och tx:erna i registret litas på som de ges. Nytt ärende ger det som källan själv räknade fram i samma
  process, men en framtida anropare kunde ge en höjd som får ett grunt svar att räknas som djupt. Registret kunde i
  stället räknas fram på nytt ur svaret av källan (en tolk per sort av svar), så att det aldrig tas emot utifrån.
- **Ethereum efter etapp 23b** (grafen, inspektören, startpunkterna, swap-pillret och kurserna för USDT och USDC är
  byggda). Kvar:
  - **Swappens pris och val** (SW1a, femte och sjätte granskningen): `StopAtSwap` läser hela ärendets Ethereum-del
    för att se att swappen finns, inte bara tx:ens svar; och exponeringen (EP1b) drar inte av den del som stannar i
    poolen när det som kom ut var värt mindre, och inte heller andelen när det var värt mer (spåret gör båda;
    *Varför?* i Exponering säger det).
  - **Swappar som inte känns igen** (SW1a, andra granskningen; hellre ingen swap än en felaktig): Uniswap V2 genom
    routern när bara historiken är hämtad (parets WETH till routern syns först när hela tx:en läses; att läsa swappens
    tx av sig själv vore ett uppslag per swap), och en aggregator som tar en avgift till en egen adress i samma tx (en
    lista över aggregatorernas avgiftsadresser skulle kunna räkna dem till bytets sida).
  - **Swap genom en känd DEX:** pillret sätts på mönstret (den som signerade skickade en tillgång och fick en annan i
    samma tx), utan lista över DEX-kontrakt. En lista över kända DEX:ar och aggregatorer kan visa namnet i *Varför?*.
  - **Tx:ens plats i blocket:** källornas historik ger inte `transactionIndex`, så två tx i samma block ordnas efter
    hash. Det påverkar bara FIFO när en adress både tar emot och betalar i samma block. Läs `transactionIndex` i
    Blockscout-tolkningen (Chains, etapp 25 arbetar där samtidigt).

    andelen i bytet är en bedömning (analys med namn och version) och behöver ett beslut om hur andelen räknas.
  - **Tx:ens plats i blocket för en hel tx:** `transactionIndex` läses också ur `gettxinfo` när svaret har det, men
    det är inte kontrollerat att Blockscouts `gettxinfo` ger fältet; utan det ordnas en start-tx som bara är hämtad hel
    efter hash i sitt block, efter de tx vars plats är känd. Pröva mot en riktig Blockscout (omgång 2).
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
  - **Kontrollerna (KF1a)** körs inte på Ethereum; listan säger det. Kontoheuristikerna och Tornado Cash nedan.
  - **Sanktionskontrollen av grafens adresser:** OFAC:s ETH-adresser läses och startpunkterna kontrolleras när ärendet
    skapas, för båda kedjorna, men adresser som kommer in vid en expansion kontrolleras inte (inte heller på
    Bitcoin). Kontrollera dem som de kända kontrakten, när grafen läses (omgång 2).
  - **Listan över kända kontrakt** (`KnownContracts`, v1 2026-11-08) har Tornado Cash Classic på Ethereum, Uniswap,
    1inch, 0x och bryggorna till Arbitrum One, OP Mainnet, Base och Polygon PoS, var och en ur operatörens egen sida.
    Fler (SushiSwap, Curve, Balancer, Paraswap, CoW, Stargate, Across, Hop, Wormhole, deBridge, THORChain) läggs till
    när deras adresser kan beläggas med en offentlig källa; höj då versionen.
  - **Länk till annan kedja från Ethereum** lagras i samma förseglade kolumner som ett mynt (`token:0x…` och
    positionen). Spårningen stannar fortfarande vid bryggan som *inte expanderad* eller dess etikett; länken ritas som
    lila linje men för inte de spårade pengarna vidare.
  - **Kontoheuristiker** (`IAccountHeuristic`): börsens insättningsadresser som töms till samma hot wallet, och
    adresser som betalar varandras gas. Kräver att kluster och bevis kan hålla `ChainAddress`.
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
- **Tornado Cash, de troliga utgångarna** (användarens fråga 2026-11-07): poolerna känns igen som mixer och blir *Spåret
  tar slut här* (omgång 2, `KnownContracts`). Poolerna har fasta belopp (0,1, 1, 10 och 100 ETH), så beloppet säger
  inget, och de troliga utgångarna kräver egna heuristiker ur forskningen: samma adress in och ut, ovanligt gaspris,
  adresser som betalar varandras gas, flera insättningar som tas ut tillsammans och tid. De blir streckade kandidater
  som mixerns. Uttagen går till en adress genom poolens kontrakt (eller en relayer), så en pools historik behöver läsas
  också för de uttag som inte rör grafens adresser.
- **TRON efter omgång 8** (källan, grafen, startpunkterna, sanktionerna, kurserna, bevakningen och testärendet är
  byggda). Kvar:
  - **Slutligt mot TRON:s fastställda block** (granskningen, M18): i dag är ett block slutligt 20 block under den
    högsta höjd Chainsaw känner, och den höjden rimlighetsprövas bara mot TRON:s takt (`ChainLimits`, 2,8 s). TRON
    säger själv vilket block som är fastställt (`walletsolidity/getnowblock`); att räkna slutligt mot det kräver en egen
    sorts uppslag och svar i ärendet, ett eget index för den höjden och att varje uppslag som avgör slutligt läser den.
  - **Tokens utom USDT i en hel tx:** TRON:s API ger en tokens decimaler bara i adressens tokenhistorik, så en hel tx
    räknar bara de tokens Chainsaw känner (USDT) och säger att de andra saknas. `decimals()` med
    `wallet/triggerconstantcontract` är kedjans eget svar men kräver POST, som `LookupQuery` inte har; lägg till POST i
    datalagret (en tolk och ett subjekt per kropp) och fråga kontraktet.
  - **Saldot för andra TRC-20 än USDT** (`v1/accounts`) visas inte, av samma skäl (inga decimaler); de räknas som
    överhoppade.
  - **Tokenhistorikens plats:** TronGrids TRC-20-lista ger varken block eller loggindex, så varje tx:s info hämtas för
    sig (`gettransactioninfobyid`). Det är ett uppslag per tx och ett per block för hashen, med TronGrids takt utan
    nyckel (1 i sekunden) en minut eller mer för 50 tx vid en expansion. Bevakningen hämtar bara info för rader nyare
    än blocket den läser till, men bakgrunden har ett nytt ärende i minnet varje varv, så en ny rad som inte kunde
    placeras frågas om nästa varv; att behålla svaren mellan varven saknas. TronGrids händelse-API per kontrakt
    (`v1/contracts/:usdt/events`, med blocknummer och index) eller en egen nod med en indexerare kan ersätta det.
  - **Tx:en i blocket:** Chainsaw hämtar bara blockets huvud (id, höjd, tid) och kontrollerar att id:t börjar med
    höjden, inte att tx:en finns i blocket (huvudet har bara transaktionsträdets rot). Samma som på Ethereum; en egen
    nod, eller blocket med `detail=true` och roten räknad, kan kontrollera det.
  - **Bevakningens tips i okända tokens:** sedan granskningen blir en token som Chainsaw inte känner aldrig en träff på
    TRON (skräp och adressförgiftning). Ett tips i en sådan token från en äldre bakgrund väntar; det kunde stängas med en
    egen journalrad.
  - **Sidor efter tid:** en sida som går tillbaka från en tid räknas som slutlig när källans klocka (`meta.at`) är
    tio minuter förbi; en källa som ljuger om sin klocka kunde få en ny sida att räknas som slutlig. Jämför med kedjans
    höjd när det går (tiden för blocket 20 under toppen).
  - **Svartlistan:** Tethers kontrakt på TRON har `isBlackListed(address)`. Att läsa den (POST som ovan) och visa
    *Fryst* bredvid snöflingan kräver en yta (TODO [skiss]) och ett beslut om hur ofta den frågas.
  - **Tidsfönstret i bevakningen:** `WatchRules.Lookback` är 6 block, 18 sekunder på TRON; TronGrids index kan ligga
    efter mer än så. Räkna bakåtblicken i tid per kedja.
  - **Interna tx:** TronGrid listar TRX som ett kontrakt skickar med tx:en i adressens tx-historik (`search_internal`),
    så historiken *Interna* är tom på TRON; tolkningen av `internal_transactions` i listan är prövad mot TronGrids svar
    men bara för `call_value._` (TRX). En TRC-10 i ett internt anrop räknas inte.
  - **TronScan** som andra sorts API (blocknummer i TRC-20-listan, men nyckel krävs för det mesta) och en egen
    java-tron-nod för `wallet/…` (adressens historik kräver ändå ett index).
  - **Typernas namn:** `EthereumAddress`, `EthereumTxHash`, `EthereumTransfer` med flera bär nu både Ethereum och TRON
    (adressen och tx:en vet sin kedja). Byt till `AccountAddress`, `AccountTxHash` och så vidare när ingen annan etapp
    rör Chains och Tracing; `IAccountSource` är redan kontraktets namn.
  - **Kontrollerna (KF1a)** och kontoheuristikerna körs inte på TRON, som på Ethereum.
- **Det som fortfarande är bundet till Bitcoin efter etapp 20** (står i DEV-LOG, etapp 20):
  - **Efter etapp 23b** (Ethereum byggt vid sidan av): grafens noder har `Chain`, `Accounts` och `Addresses`
    (`ChainAddress`) bredvid `GraphAddresses`, och kontomodellens del är `TraceGraph.Account` med ett eget resultat
    (`AccountTraceResult`). Kvar för Bitcoin-delen: spårningens resultat för mynt (`TraceSeed`, `TracedCoin`,
    `TracedTransaction.Txid`, `TracedAmount.Through`, `TraceEnd.Transaction`) och `UtxoGraph` med Bitcoins typer;
    heuristikkontraktet för konton (`IAccountHeuristic`) när kluster och bevis kan hålla andra adresser än Bitcoins;
    `ITracingService` med en källa per kedja i stället för `IBitcoinSource` och `IEthereumSource` var för sig;
    `GraphScene.Shown` (Bitcoin-nodernas summor räknar BTC) och *Kontroll* i inspektören (bara Bitcoins tx).
  - **Med nästa UTXO-kedja (Litecoin, Zcash):** `BitcoinAddress` bär sin kedja genom nätet (`BitcoinNetwork.Litecoin`,
    omgång 8b), men `UtxoGraph` saknar fortfarande sin kedja och tillgång (i dag räknar `TraceCalculator` alltid i
    BTC), och `CrossChainLink.From` blir ett mynt på vilken UTXO-kedja som helst (TODO, *Litecoin i grafen*).
- **Litecoin och BSC efter omgång 8b:**
  - **Litecoin och Taproot:** Chainsaw skriver Litecoins vittnesversion 1 med Bech32m (BIP 350) och MWEB:s versioner 8
    och 9 med Bech32, som litecoinspace.org gör; att litecoinspace.org skriver en Litecoin-Taproot-adress med Bech32m
    är inte prövat mot ett riktigt svar. Svarar källan annorlunda vägras tx:en (adressen stämmer inte med skriptet).
  - **OFAC:s token-poster utan kedja:** `USDT` och `USDC` med en `0x`-adress räknas som Ethereums, aldrig BSC:s, och
    `BNB`-poster (`bnb1…`, Beacon Chain) räknas inte alls. Samma nyckel styr samma 20 byte på båda kedjorna, så en
    post kunde visas som en bedömning också på BSC.
  - **Chainabuse på Litecoin och BSC:** leverantören frågas bara för Bitcoin och Ethereum (`ChainabuseProvider`).
  - **PancakeSwap och andra DEX:ar på BSC:** swappen känns igen på mönstret, men inget kontrakt på BSC står i
    `KnownContracts` (och `KnownContractLabels` sätter därför inga etiketter på BSC); läggs till när adresserna kan
    beläggas med en offentlig källa.
  - **En annan kurskälla för BNB:** Bitstamps `bnbeur` och `bnbusd` saknar historik före 2026 och handlas mycket tunt,
    så BNB och WBNB saknar ofta kurs, och en swap mot BNB kan inte prisprövas. En källa med längre historik (efter
    licens och med samma krav på att kursen gäller en tidpunkt) behövs.
  - **Litecoins råa bytes med MWEB-data** (flaggan 08, blockens HogEx) läses inte; sådana tx sägs inte gå att
    kontrollera mot bytes. Att läsa MWEB-delen behövs först när HogEx ska kontrolleras.
  - **BSC:s takt i `ChainLimits`:** 0,25 s per block som undre gräns (blocken har kommit snabbare med varje fork, under
    en sekund sedan 2025). Blir de ännu snabbare måste gränsen sänkas.
  - **Etiketter på BSC** lagras som `bsc:0x…` (`LabelSubject.Stored`), eftersom ämnet lagras utan kedja; en kolumn
    för kedjan vore renare.
- **Facit för Bitcoin (granskningen av etapp 20, M):** utöka `Flows` med en länk till annan kedja, en startpunkt med del
  av ett mynt, en adress som får två betalningar och en obekräftad tx, och spela in facit på nytt mot koden före
  etapp 20 (commiten med facit).
- **`ChainTransactionRef.Of(Txid)`** kontrollerar en txid som redan är kontrollerad med ett reguljärt uttryck, för varje
  överföring i varje bygge av grafen (granskningen av etapp 20, M). Billigt, men kan tas bort med en konstruktor som
  litar på `Txid`.
- **Solana efter omgång 8c** (källan, grafen, startpunkterna, sanktionerna, kurserna och bevakningen är byggda,
  DEV-LOG):
  - **Andra programs instruktioner:** bara System-, Token-, Token-2022- och ATA-programmen läses (det noden tolkar i
    `jsonParsed`). Ett program som flyttar lamports själv (stängda programkonton, bonding curves) eller Token-2022:s
    `transferCheckedWithFee` gör att saldona inte går ihop; tx:en sägs (`EthereumAddressPage.Unexplained`) och det som
    fattas visas inte. Att läsa fler program, eller visa resten som en namngiven bedömning (*netto ur saldona*,
    streckad, med *Varför?*), är nästa steg; det senare skissas först.
  - **En nyckel i endpointens sökväg** (Helius, QuickNode): Chainsaw skickar nyckeln bara som `Authorization: Bearer`
    och förseglar ett JSON-RPC-svar under värden ensam (sökvägen förseglas aldrig), men en leverantör som kräver
    nyckeln i sökvägen kan bara läggas till med nyckeln i adressen, som då ligger i inställningarna utanför valvet. En
    platshållare för valvet i adressen (`{nyckel}`) skulle hålla den där.
  - **Bevakningen i fas på Solana:** en endpoint räknas i fas bara med exakt den högsta sloten i sin omgång, och
    slotarna går 2,5 i sekunden; *efter* har en marginal på 75 slotar (`ChainSources.HeightMarginOf`), men *i fas*
    för bevakningen har det inte.
  - **En full sida i en enda slot** (granskningen av omgång 8c, M2): sidor efter slot kommer inte förbi en full sida
    som ligger i en slot (`SolanaSignaturePage.IsCutShort`), så historiken för en upptagen börs hot wallet med 25 tx
    eller fler i samma slot slutar där. Det sägs ärligt, men Solana kan bläddra efter signatur; en sida som skärs av
    borde behålla en signatur som markör och fortsätta därifrån.
  - **Ägare som saknas eller byts** (granskningen av omgång 8c, M3): äldre tx har inget `owner` i sina tokensaldon. Ett
    tokenkonto som skapas i tx:en får sin ägare ur `initializeAccount` eller ATA-`create` (rättat med I2), men ett äldre
    konto utan ägare faller tillbaka på tokenkontots adress, så samma ägare kan synas som två parter. Ett ägarbyte inne
    i tx:en (`setAuthority`) räknas helt till den nya ägaren. Inget av det sägs eller testas än.
  - **Token-2022 med avgift** (andra granskningen av omgång 8c, M1): `transferChecked` på en mint med överföringsavgift
    ritar bruttobeloppet på linjen (1000), medan mottagaren fick 990; tx:en sägs som oförklarad, men spårningen följer
    hela 1000. Dra av `feeAmount` eller rita linjen streckad med *Varför?*.
  - **Endpointens sökväg finns inte i ärendet** (M2): en egen nod på `https://node.lan/solana` förseglas som
    `https://node.lan/#…`; bara `endpoint_id` och namnet säger vilken endpoint det var. Försegla en hash av hela
    adressen, eller säg det i Guiden.
  - **Hash-jämförelsen hex eller Base58 står två gånger** (M3): `BlockChangeRule.HashComparison` och
    `MonitoringService.HashComparison`; en regel i Contracts.
  - **Ägarbyte inne i tx:en** (M4): `Know` håller ägaren från saldona före, så rörelser efter en `setAuthority` räknas
    till den gamla ägaren; märk åtminstone tx:en oförklarad när ägaren före och efter skiljer sig.
  - **Stängda tokenkonton:** tokenhistoriken läser ägarens tokenkonton som noden listar dem nu; ett stängt konto finns
    inte där, och dess rörelser hittas bara i tx:er som ägaren själv signerade.
  - **Kända tokens i tokenhistoriken** är USDT och USDC; andra SPL-tokens syns i hela tx:er och i ägarens egna
    signaturer.
  - **En sida kostar ett uppslag per tx** (signaturlistan säger inte vad som flyttades), och den publika noden tar tio
    `getTransaction` per tio sekunder. En köpt eller egen nod med högre takt behöver bara läggas till under *Källor*;
    en batch av JSON-RPC-anrop skulle spara tid men förseglas då som ett svar för flera tx.
  - **Sidorna efter signatur:** Solana bläddrar med `before` (en signatur), så en äldre sida kan bara frågas efter att
    den nyare har lästs i samma körning (`SolanaPageMemory`); läsarna gör alltid så, och ur ärendet läses sidan på sin
    slot.
  - **Gamla tx utan `blockTime`** (de första månaderna 2020) kan inte läsas: tiden saknas.
  - **Fryst:** saldot säger när ett tokenkonto är fryst (`state: frozen`), men grafen har ingen markering för det
    (TODO, [skiss] *Fryst*).
  - **Jupiter och Raydium som DEX i swap-pillret (DX1b):** swappen känns igen på mönstret och priset prövas, men
    programmens adresser står inte i `KnownContracts` med en offentlig källa; läggs till när de kan beläggas.
  - **Bryggor från Solana** (Wormhole, deBridge, Mayan) känns inte igen som slut på spåret; samma skäl.
  - **Chainabuse på Solana:** leverantören frågas bara för Bitcoin och Ethereum (`ChainabuseProvider`).
  - **Wrapped SOL som ägarens:** ett wrapped-SOL-kontos lamports räknas som ägarens (dess tokensaldo säger vem det
    är), också den hyra som ligger i kontot; det gör att en pools SOL och token möts hos samma ägare.
- **Zcash** (användarens fråga 2026-11-07): den genomskinliga delen (t-adresser) är en UTXO-kedja som Bitcoin och
  passar efter etapp 20 som de andra UTXO-kedjorna, men Esplora finns inte för Zcash, så källan blir en annan (en
  tjänst med API-nyckel eller egen nod med Zebra). Tx som går in i den skyddade poolen (Sapling, Orchard) blir *Spåret
  tar slut här* med orsaken skyddad pool. Senare kan pengar som kommer ut ur poolen föreslås som kandidater ur belopp
  och tid, streckade som mixerns troliga utgångar.
- **Privacy coins:** metoderna är beslutade (CLAUDE.md, *Spåret tar slut*) och byggs som förslag i omgång 9.
- **Cross-chain automatiskt:** hitta fortsättningen på den andra kedjan själv (belopp och tid hos swap-tjänster, bryggornas
  egna händelser). Kanten finns i modellen från början.
- **Direkt mot kedjan, resten** (Bitcoin Core och electrs som egen nod är byggt, 2026-11-09): Electrum-protokollet
  (Fulcrum, `blockchain.scripthash.get_history` över TLS) kräver en ny väg i `ILocalHttp` för rå TLS och en egen
  adressform (scripthash) bakom `IBitcoinSource`; Litecoin Core (samma RPC, registreras för Litecoin); den egna nodens
  eget Merkle-bevis (`gettxoutproof`, ett partiellt merkleträd) i stället för källans; Reth eller Erigon för Ethereum;
  java-tron för TRON.
- **Cross-chain, efter `cross-chain v1`** (byggt 2026-11-09, DEV-LOG): förslagen väger det grafen har på andra kedjor
  och mottagaren som ett memo i THORChains form namnger. Kvar:
  - **Bryggornas händelser på Ethereum:** bara `Transfer`-händelser läses ur loggarna, så en bryggas eget mål-id eller
    mottagare (till exempel THORChain-routerns memo i anropets data, eller en bryggas `Deposit`-händelse) syns inte.
    Kräver att källan läser loggarna eller anropets data.
  - **Tjänstens utbetalningsadresser på den andra kedjan:** en swap-tjänst med samma etikett på båda kedjorna kunde
    slås upp i tidsfönstret (deras historik är lång och dyr att läsa); i dag vägs bara det grafen redan har.
  - **Uppslag per tidsintervall:** ingen av källorna kan svara på *alla betalningar mellan två tider*; med egen nod och
    eget index går det.
  - **Bara mottagarens senaste sida** läses (Bitcoin 25 tx, Ethereum och TRON en sida per historik), så en gammal swap
    till en livlig adress kan saknas.
  - **Spåret slutar på en tx-etikett** (*Swap eller brygga* på en tx, inte en adress): sådana betalningar vägs inte än.
  - **Bryggorna till L2-kedjorna** (Arbitrum, OP Mainnet, Base, Polygon) leder till kedjor Chainsaw inte har.
  - **En betalning in på Litecoin** föreslås inte än: en länk från ett mynt är ett Bitcoin-myntes i Tracing
    (`CrossChainLink.FromChain`), så Litecoin behöver en kedja på myntet först. Betalningar *ut* på Litecoin vägs.
  - **En utbetalning i flera delar:** bara en kandidat kan godkännas (en betalning knyts till en tx åt gången), och
    delar som tillsammans är värdet föreslås inte.
  - **En THORName** i memot (ett namn i stället för en adress) slås inte upp; det sägs i *Varför?*.
  - **THORChains router och valv** står inte på listan över kända kontrakt: de läggs till när adresserna har lästs ur
    THORChains egen källa med datum (listan tar aldrig in en adress som inte är kontrollerad). Till dess räknas en
    utbetalning som från en brygga bara genom en etikett *Swap eller brygga* eller grafens noder.
  - **Kontrakt eller nyckel:** källorna ger ingen bytekod, så en adress räknas som ett kontrakt bara när listan,
    en etikett eller en intern utbetalning säger det; ett kontrakt som inget av det känner kan ge *samma nyckel*.
  - **Ett godkänt förslag vars betalning in sedan knyts om för hand** står kvar som godkänt i panelen tills nästa körning.
- **Direkt mot kedjan:** egen nod och eget index (Bitcoin Core med `txindex`, Fulcrum eller electrs eller ett eget index;
  Reth eller Erigon för Ethereum; java-tron för TRON). Kräver en server. Esplora går att köra själv, så Bitcoin blir bara en
  annan adress i datalagret.
- **Bevakning i realtid, resten** (nya block via mempool.space:s websocket är byggt, omgång 3): ZMQ från egen nod (en
  egen nod nås bara genom `ILocalHttp`, och dess websocket lyssnas inte på), Ethereum (Blockscout har websockets) och
  mempool.space:s `track-address` för obekräftade tx. Det sista skickar de bevakade adresserna tillsammans över en
  anslutning, så att källan ser att de hör ihop; ett beslut för användaren innan det byggs.

## Bitcoin-källan (Chains)
- **Svarshuvuden som ursprung:** se *Utgångar*. Behövs om källans `Date` ska stå bredvid Chainsaws egen tid.
- **Flera källor att jämföra:** samma tx från två Esplora-källor, och en varning om de skiljer sig.
- **Endpointens namn** (etapp 24): namnet tas ur adressens värd och kan inte ändras i sidan Källor.
- **Nyckeln som huvud** (etapp 24): Esplora skickar nyckeln som `Authorization: Bearer`. Huvudena som tillåts är
  `Authorization` och `X-Api-Key` (`ExitCredential`); en källa som vill ha något annat lägger till det i Network.
- **Varför en endpoint fick röd prick** (etapp 24, granskningen omgång 2, M): en nyckel som saknas i valvet gör att
  endpointen hoppas över och får röd prick med den allmänna texten *Ett uppslag genom den misslyckades …*; att nyckeln
  saknas står bara i uppslagets text. Raden kunde säga orsaken.
- **Tx på en adress sida kontrolleras inte mot sina bytes** (etapp 15): bara tx som expanderas för sig (och deras
  spenderare) hämtar `/tx/:txid/hex`. En adress historik med 50 tx skulle kosta 50 uppslag till. Kontrollera dem när
  de visas i inspektören, eller i bakgrunden?
- **Svårigheten på höjden** (etapp 15): att tx:en ligger i blocket kontrolleras nu med merkle-beviset
  (`VerifyInBlockAsync`, omgång 3) och att huvudets mål ligger inom nätets gräns, men om `bits` är nätets exakta
  svårighet på höjden kontrolleras inte; det kräver huvudena före (var 2016:e block). Görs med egen nod.
- **Kontrollpunkter för arbetet** (granskningen av omgång 3, I2): blockets hash bekräftas nu av en annan endpoint i
  fas (`InBlockCheck.ConfirmedElsewhere`), men inbyggda kontrollpunkter (lägsta `bits` per period om 2016 block, med
  källa och datum i koden) skulle låta ett huvud bära arbetet för sin höjd också med en enda källa. Kräver verifierade
  värden ur kedjan, inte ur minnet.
- **64 bytes ur de kontrollerade bytesen** (granskningen av omgång 3, M8): kontrollen av en tx på 64 bytes räknar
  storleken utan vittnesdata ur källans `weight` och `size`; de är kontrollerade mot bytesen men inte själva räknade ur
  dem. Räkna storleken ur de förseglade bytesen.
- **Bekräftelsen är en annan endpoint, inte en oberoende källa** (M7): en annan endpoint med en annan värd kan läsa
  samma nod; den heter *en annan endpoint* med namn, aldrig *oberoende*. Att jämföra värdarnas uppslagna adresser
  vore nästa steg.
- **Signet** har ett eget, lättare powLimit (`0x1e0377ae`) men inget eget värde i `BitcoinNetwork`; dess huvuden
  vägras i dag. Ett eget nätvärde behövs om signet ska användas.
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
- **Den egna noden som facit** (etapp 25, M1; provet utan utgång byggt 2026-11-09): den egna noden provas nu också
  när utgången är stängd och behålls, men *ojämförd* (`EndpointProbe.Unreferenced`), så att bevakningen inte använder
  den förrän ett prov genom utgången visat att den är i fas. Ett beslut återstår: ska den egna noden få vara facit för
  bevakningen utan utgång? Bitcoin Core vet själv om den är i fas (`getblockchaininfo`: `blocks` mot `headers` och
  `initialblockdownload`), vilket kunde vara underlaget.
- **Ett behållet prov åldras** (granskningen av etapp 25, omgång 5, M; lämnat i omgång 3 som ett avvägningsbeslut, det
  försiktiga valet står kvar): ett prov som behålls när endpointen bara väntade
  mäts mot den nya omgångens höjd, så block som kommit sedan räknas mot den. På Ethereum (block var tolfte sekund) blir
  ett behållet prov äldre än en halv minut *efter*, tills nästa *Prova alla nu*. Försiktigt för bevakningen, men kunde
  i stället bara ta bort *i fas* och behålla *svarar rätt*. Och en referenshöjd ur ett behållet prov följer med så länge
  endpointen fortsätter be om väntan, också om höjden var fel.
- **Spridningen i Prioriterad** (etapp 25): varje endpoint är en egen grupp, så användarens ordning gäller och nästa tas
  först när den första bett om väntan eller dess kö är längre än fem sekunder. I Snabbast sprids uppslagen inom den bästa
  gruppen (de som svarat rätt). Ändras om användaren vill ha spridning också i Prioriterad.
## Klustring och heuristiker
- **Efter omgranskningen av omgång 4:s ytor (2026-11-09, M):** BU1a förseglar en uppbrytning i användarens namn också
  för en adress som bara föll med när hela förslaget vägrades (förklara det i journalen och *Varför?*, eller bryt bara
  det valda); KV1a-rutan ska vara streckad (`ClusterWhyView.xaml:164` ritar hel linje); *Ångra* efter *Släpp
  slutsatsen* återställer inte den gamla slutsatsen om en körning hunnit emellan — säg det eller vägra; HB1a:s
  `HeldBackMatch.Touches` är O(N·E) per bild; vyn Etiketter kopierar `FullAddress` direkt också för en liknande adress.
- **PayJoin v2 och en plånbok som byter adresstyp** (slutgranskningen 2026-11-08, M1): en Taproot-plånbok som spenderar
  ett gammalt P2WPKH-mynt och ett P2TR-mynt, betalar till P2WPKH och lägger växeln på P2TR har samma form som BIP78 och
  ger ett förslag i stället för en sammanslagning (sonden i granskningen). Sällsynt och bara falskt negativt; lägg
  fallet som test och nämn det i `docs/guide/payjoin.md`. Ett eget test för ett ärende som vägts med v2 och sedan
  drar tillbaka v2 saknas också (M2).
- **Fler tecken för växeln** (fingeravtrycket är byggt i `change-address v2`): jämna belopp i SEK eller USD vid tx:ens
  tid (kräver kurser i klustringen, som i dag inte läser Pricing), ordningen på outputs som en del av fingeravtrycket,
  och adressens hela historik från källan i stället för bara det ärendet känner till (*ny adress* är nu *inte sedd i
  ärendet*).
- **Fler mönster för CoinJoin** (Wasabi 2:s standardbelopp och Ashigarus pooler är byggda i `coinjoin v2`):
  JoinMarkets avgifter och fidelity bonds (tidslåsta P2WSH-utgångar), och Whirlpools tx0 (förberedelsen före en mix).
- **Klustringens underlag ur ärendet** (granskningen etapp 6): `IClusteringService.Analyse` tar de tx som anroparen
  skickar, både som det som analyseras och som det analysen vet runt varje tx. En snävare körning kan inte ta tillbaka
  en slutsats vars underlag den saknar, men en tx som aldrig hämtats in i ärendet kan analyseras. Bättre: Clustering
  läser tx:en ur ärendet genom Chains (kontrakt för det i `Chains.Contracts`), och varje slutsats pekar på det
  förseglade råsvaret och blocket (höjd och hash) den bygger på (princip 1).
  Tills dess kan en tx som analyseras första gången i en snäv körning få en sammanslagning av växeln som en bredare
  körning skulle vägra (den betalda adressen har setts i en tx som inte skickades med); den står kvar tills en bredare
  körning ersätter den (granskningen etapp 6, omgång 3, M).
- **Stora ärenden i klustringen:** klustren byggs om ur alla förseglade slutsatser vid varje fråga, och en körning
  läser alla fingeravtryck. Kontrollerna läser nu alla tx:ers slutsatser i en fråga (`GetConclusions` för många tx),
  men för större ärenden behövs ett register bredvid (som Chains har) eller en cache av klustren som byggs om när
  något nytt förseglas.
- **En version som dras tillbaka, kvar** (byggd 2026-11-08 som `IClusteringService.Withdraw` och
  `TakeBackWithdrawal`): ett förslag som vägdes mot den tillbakadragna versionen räknas som förslag tills tx:en
  analyseras igen; att analysera om de berörda tx:erna direkt vid tillbakadragandet kräver att Clustering läser tx:erna
  ur ärendet (*Klustringens underlag ur ärendet* ovan).

- **Mönster: ett märkt offer håller hela gruppen** (femte granskningen av `pattern v1`, M-1): när mottagaren för
  vidare i klump räcker ett märkt offer bland alla avsändare för att *Samma insamling* står kvar för hela gruppen. I
  provet X3c sveps 20 insättningskluster till en adress som för vidare i klump, med bara 1 av 100 avsändare märkt som
  offer, och träffen gäller ändå 20 aktörer. Förslag: när mottagaren för vidare i klump, filtrera per insamling och
  behåll bara insamlingar med egna märkta offer, och kräv minst två sådana.
- **Mönster: fickpengar** (granskningen av `pattern v1`, N3''): en förälder som för över samma udda belopp varje dag
  till ett barn som sedan spenderar mynten tillsammans räknas som strukturering (samma mottagare, lika belopp,
  mynten samlas). Förslag: kräva en större summa för följden, eller att mottagaren för pengarna vidare till en börs,
  när kryptospåraren har sagt vilka belopp som brukar ligga under gränserna.

## Förvirringstekniker (användarens beslut 2026-11-07)
Allt som används för att förvirra spårningen ska kännas igen och analyseras, med så mycket automatiska kontroller och
förslag som möjligt (CLAUDE.md, *Förvirring analyseras*). Byggt 2026-11-08 (omgång 4): mixade mynt som slås ihop
igen (`coinjoin-merge`), peel chain till börs (`peel-chain-area v2`), uppdelning och ihopslagning (`split-merge`),
många hopp och fördröjning (`pass-through`), damm och adressförgiftning (`dust`), möjlig PayJoin (`payjoin-hint`),
tidsmönster (`active-hours`) och tjänster ur beteende (`service-behaviour`). Byggt 2026-11-09 (omgång 5): trolig
mixer ur beteende (`mixer-behaviour v1`), mixer efter mixer genom en trolig mixer, med delar tillsammans och grenar
(`mixer-chain v2`), uppdelning över flera tx och möten efter många mellanled (`split-merge v2`), hopp med avgift till
en tjänst (`pass-through v2`), tidsmönster i svensk tid för mottagare och avsändare med korrektion
(`active-hours v2`) och nästlade tjänster (`nested-service v1`). Kvar av katalogen:
- **Centrala mixers:** kända adresser och mönster per mixer, och kända avgifter per mixer så att troliga utgångar kan
  räknas ur belopp minus mixerns egen avgift. Kräver en källa för avgifterna (inga påhittade siffror); fråga 8 till
  kryptospåraren.
- **Tornado Cash och andra zk-mixers:** under *Kedjor och data*.
- **Privacy coins och skyddade pooler** (Monero, Zcash, Litecoin MWEB): slut på spåret; förslag på uttag ur poolen ur
  belopp och tid.
- **Jämna belopp i fiat:** ett belopp som är jämnt i SEK, EUR eller USD vid tx:ens tid som stöd för växeln och för vem
  som står bakom. Inte byggt i omgång 5: kontrollerna körs synkront vid varje läsning av grafen, och kurserna ur
  ärendet nås bara genom `IPriceService.GetPriceAsync`, en tidpunkt i taget. Att fråga per tx i varje körning (som
  `cross-chain` gör för sina få betalningar, synkront över asynkront) är varken rent eller snabbt. Det rena sättet är
  ett synkront kontrakt i `Pricing.Contracts` som läser de kurser ärendet redan har för många tidpunkter i en fråga,
  och som `CheckService` lägger i `CheckInput`. Dessutom skiljer börsens kurs vid köpet sig från Bitstamps vid blocket
  med upp till några procent, så toleransen gör ett "jämnt" belopp till ett svagt tecken; fråga kryptospåraren.
- **Uppdelning, kvar:** *Följa summan* är en mening i *Varför?*, inget förslag med en knapp.
- **Tjänst ur beteende, kvar** (granskningen omgång 4, M): en bedragare med tio eller fler offer som inte är märkta
  (varken startpunkt eller etikett) och betalningar över mer än ett dygn ser fortfarande ut som en tjänst. Fler
  tecken behövs, till exempel insättningsadresser som bara används en gång, utbetalningar i batch från samma hot
  wallet, eller att avsändarna själva är vanliga plånböcker.
- **Efter omgång 5 (2026-11-09, egna fynd):**
  - En trolig mixer utan etikett får inga egna förslag (`mixer-outputs`, `mixer-parts`): kedjan går genom den
    utbetalning som bäst liknar insättningen, och användaren sätter etiketten Mixer för att få förslagen. En kedja kan
    inte heller börja i en trolig mixer, bara passera den.
  - `mixer-behaviour` matchar en hel utbetalning eller ett par; med tät trafik (insättningar en timme isär) träffar
    par av andras delar lika ofta som de riktiga, och mixern känns inte igen (facit: `RoundFiveReviewTests`). Tre
    delar eller fler, och delar långt efter insättningen, prövas inte. Bättre: väga in att delarnas adresser är nya
    och att delarna kommer ur olika tx men samma pott, och mäta mot en baslinje per insättning.
  - Baslinjen för `mixer-behaviour` är snittet av fyra skalade belopp (80–125 %), och skillnaden ska vara minst
    2,5 gånger roten ur baslinjen. En börs vars kunder tar ut nästan allt de satt in strax efter (en börs som används
    som genomfart) kan ändå passera; granskningens prob finns, men inget facit än.
  - `mixer-behaviour` räknar andelen förklarade utbetalningar över alla som ligger inom en vecka efter någon
    insättning (granskningen 2, M1). Har grafen bara en del av mixerns insättningar men alla utbetalningar känns den
    inte igen (facit och Guiden säger det). Bättre: ett snävare fönster ur fördröjningarna i de egna träffarna, eller
    bara de utbetalningar som har en tänkbar insättning i grafen.
  - En mixer som tar samma avgift (en procentsats plus ett fast belopp) och betalar ut med jämn fördröjning (inom
    tre timmar) läses som betaltjänst och känns inte igen (facit: `SteadyMixer`). Omvänt passerar en betaltjänst som
    varierar sin avgift eller väntar mycket olika länge.
  - `mixer-chain` på en tät slumpgraf med en mixer med etikett (40 adresser, 1 500 tx) ger ett nittiotal kedjor genom
    troliga utgångar, eftersom `mixer-outputs` väger varje utbetalning i fönstret; så var det också i v1. Grenar,
    delar tillsammans och troliga mixrar ger inga fynd där.
  - `nested-service` kräver en börs med etikett och att minst hälften av pengarna går genom kontot åt båda hållen; en
    tjänst som bara delvis använder börsen syns inte.
  - `nested-service` känner bara igen en insättning till börsen när adressen redan är klustrad med börsen eller har
    dess etikett (granskningen, M3); utan börsens svep i grafen blir det ett falskt negativt. Facit knyter kontot till
    hot wallet för hand. Förslag: godta också en insättningsadress som `service-behaviour` har sett svepas till
    börsens hot wallet.
  - `split-merge`: nyckeln `spread:` är den tidigaste tx:en bland delarna i fönstret (granskningen, M4). Kommer en
    tidigare betalning in i grafen kan fönstret flytta och fyndet få en ny nyckel, medan det gamla står kvar förseglat.
    Lika löner, faucets och airdrops passar också mönstret (texten säger det). Förslag: en fast nyckel som inte beror
    på fönstret, till exempel den tidigaste tx:en bland delarna efter att bandet har bestämts över hela dygnet.
  - Tidsmönstret läses i svensk tid för alla; en inställning för användarens tidszon finns inte. Korrektionen gör att
    ett mönster kan försvinna när fler ägare kommer in i ärendet.

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
- **Kontrollerna körs vid varje läsning av grafen.** Slutsatserna läses nu i en fråga för alla tx, men klustren
  (ägarna) byggs om och alla analyser körs om varje gång; för stora ärenden behövs att bara köra om när grafen eller
  klustringen ändrats.
- **Ett förslag som analysen inte längre ger alls** (granskningen etapp 22, M): en avgjord kandidat följer nu med ur
  det senast förseglade när analysen inte längre ger den, men försvinner hela förslaget (till exempel när insättningen
  lämnar grafen eller adressen blir märkt som mixer) syns besluten inte längre (de står kvar förseglade).
- **Besluten i analysernas indata:** besluten läses om i skrivningen, så att kandidatlistan som förseglas alltid har
  ett beslut som förseglats under tiden, men analyserna som bygger på godkännanden (`mixer-chain`, `mixer-parts`) körs
  på besluten från före; nästa körning rättar det.
- **Historiken (AN1b, etapp 21)** bör visa beslut om förslag och egna ytor. `ICheckService.RemoveArea` tar bort en yta
  förseglat, och ett nytt beslut om samma kandidat ersätter det förra.

## Spårning (Tracing)
- **Efter tredje granskningen av en borta tx (2026-11-09, M):** slutet vilar på en enda källa också när en annan
  endpoint är påslagen men aldrig har svarat (ge hellre `GoneUncertain`, eller säg det i texten); ett sparat svar om
  mynten från lögnaren frågas aldrig om hos en annan värd (`Settled` bör kräva en värd som inte sa 404); mellan
  404-svaren och den andra värdens svar slutar spåret tillfälligt på en källa (beloppen hoppar); långa rader i
  `docs/guide/ersatt-tx.md`.
- **Efter slutkontrollen av SW1a (2026-11-09, M):** det utfällda valet *Följer / Följer inte* ritas inuti linjens
  toning, så det tonas ned när en annan nod är vald (prova på Windows, och att pillrets öppna läge ritas om när
  linjelagret är cachat); ett ovanligt specialfall i Exponering (länkad insättning till ämnet i en köpt tillgång,
  `_onOtherChain[(ämne, tillgång)]`) saknar test.
- **LIFO och poison** (etapp 7): kontraktet `ITracingMethod` tar emot fler metoder, men LIFO behöver en definition
  (inom en tx är ordningen redan FIFO:s; LIFO handlar om en adress saldo över tid), och poison ger inga belopp som går
  jämnt ut, så den behöver visas som *berört* i stället för ett belopp. Se `docs/KRYPTOSPARAREN.md`, fråga 1.
- **Tidsfönster för en adress historik** (etapp 7): expansionen tar de nyaste tx upp till gränsen (50 som förval).
  För en adress med lång historik behövs ett tidsfönster runt händelsen i stället.
- **Grafens prestanda, efter omgång 7:** när en drag släpps räknas hela layouten om (cirka 100 ms i hoppkolumnerna
  och 20 ms på den fria ytan vid 3 000 aktörer, `GraphScaleTests`), på UI-tråden. Den kunde räknas utanför UI-tråden
  och bytas in när den är klar. Under draget står de belopp som inte tillhör de dragna linjerna kvar där de stod.

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

- **En skrivning i kö när ärendet stängs:** lappen som skrivs i förseglas innan ärendet släpps, men en skrivning som
  redan står i kö (till exempel en ändring som väntar på disken) väntas inte in, eftersom den återkommer på UI-tråden.

## Attribution
- **Efter fjärde granskningen av omgång 5 (2026-11-09, M):** `StartPointPreview.IsPossiblySanctioned` läses ur
  tipsets text; bär `IsUncertain` som data.
- **Vyn Etiketter (ET1a), det som återstår:**
  - *Sprid inte* som slås på i efterhand tar etiketten ur biblioteket och hindrar att den ges vidare, men kopior som
    redan gått till andra ärenden ligger kvar där (toasten säger det).
  - *Ångra* i toasten efter *Ta tillbaka* (skissens Ctrl+Z) är inte byggt: en tillbakatagen etikett sätts på nytt, och
    då med den som ångrar som ursprung, inte den ursprungliga. Behöver ett eget förseglat *återinsatt*.
  - Raderna *Kom in*, *Gick ut* och *Sprids inte* under BIBLIOTEKET är bara antal; de kunde filtrera listan.
- **EU:s lista, märkningarna för kedjor** (granskningen av omgång 5, I5 och I-a): kedjan tas ur den närmaste
  märkningen före adressen i samma del av texten (delad vid `;`, komma och radbrytning, `EuFsfParser.Labels`). Listan
  med märkningar är Chainsaws egen och kan behöva fyllas på när en riktig EU-fil har setts; en adress utan säker
  märkning blir *Möjlig sanktion*.
- **EU:s lista (omgång 5, byggd 2026-11-08), det som återstår:** listan har ingen egen sort för kryptoadresser, så
  `EuFsfParser` känner igen en adress på formen (Bitcoin med kontrollsumma, `0x` och 40 hextecken) i identifieringar
  och anmärkningar. Det är inte kontrollerat mot en riktig fil om EU skriver adresser någon annanstans (till exempel i
  `nameAlias` eller i `regulation`s sammanfattning), och TRON-adresser (`T…`) känns igen men slås inte upp än, fast
  TRON har en adresstyp i Chains sedan omgång 8. Fler listor (FN:s, Storbritanniens OFSI) kan läggas till som en ny
  `ListKind`.
- **Ta bort en sanktionslista eller öppen källa i gränssnittet:** `ISanctionLists.Remove` och
  `IOpenSourceLists.Remove` finns (en lista med fel datum i framtiden kan tas bort), men ingen knapp; se TODO [skiss].
- **Chainabuse som underleverantör** (användarens beslut 2026-11-08): Chainabuse (TRM Labs,
  https://docs.trmlabs.com/guides/chainabuse/welcome-to-chainabuse-api) samarbetar med brottsbekämpande myndigheter.
  Chainsaw hämtar rapporterna för en adress ur deras API, genom utgången och med nyckeln i valvet; bara adressen
  skickas (princip 4), svaret förseglas i ärendet (princip 1) och rapporterna blir etiketter som är bedömningar med
  källa, aldrig fakta (princip 2), eftersom de skrivs in av allmänheten. **Hämtningen är byggd 2026-11-08**
  (`IAttributionProviders`, `ChainabuseProvider`): Bitcoin och Ethereum (TRON frågas inte än, fast etiketter går att
  sätta där sedan omgång 8), en
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
- **Öppna källor, granskade 2026-11-08 (omgång 5):**
  - **Ransomwhere är inbyggd** (CC BY 4.0 på Zenodo, post 13999026, Jack Cable): läses ur en sparad
    `ransomwhere.json` och ger *Rapporterad* med licensen i skälet. Datamängdens tx-hashar (lösensummorna) läses inte
    än; de kunde bli etiketter på tx (*betalning till ransomware*) och offrens sida i spåret.
  - **WalletExplorer** (äldre BTC-kluster) har inga uttalade villkor för datan och inget nedladdningsbart underlag, så
    den byggs inte in; att skrapa webbplatsen är inte tillåtet utan villkor.
  - **OpenSanctions** återpublicerar Ransomwhere och sanktionslistorna, men gratis bara för icke-kommersiellt bruk;
    för en myndighet krävs troligen en licens (som Bitstamps fråga under *Före tjänstebruk* i TODO). Läses inte.
  - Andra kandidater att granska: GraphSense TagPacks (licens per paket), Etherscans etiketter (villkoren förbjuder
    återanvändning), CryptoScamDB (arkiverad).
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
- **Allt sprids:** etikettens namn, skälet, anteckningen, ärendenumret och hela operatörsprofilen följer med till andra
  ärenden (Allt följer med). Gränssnittet ska säga det, och kanske varna när namnet liknar ett ärendenummer (granskningen etapp 8, M3).
- **Listan i ärendet:** ärendet sparar listans hash, datum och postens uid, inte listan. En kollega eller en annan dator
  kan inte kontrollera träffen utan filen. Försegla postens XML i ärendet? (granskningen etapp 8, M4). Detsamma för
  en öppen källas post.
- **Sanktionslistorna i appdata** (omgång 5): en trasig beskrivning skrivs om ur listan och ändringar utanför
  Chainsaw märks utan omstart (mappens ändringstid), men en lista vars fil fått fel hash försvinner fortfarande ur
  listningen utan att något säger det; vyn Etiketter kunde visa den som *ändrad*.
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
- **Etiketter på tx (omgång 5, modellen byggd 2026-11-08):** `ITransactionLabels` förseglar och tar tillbaka en
  etikett på en tx (Bitcoin och Ethereum), och `ClassifyTransaction` avslutar spåret vid en märkt Bitcoin-tx. Kvar:
  ytan (TODO [skiss]), att etiketter på tx följer med i biblioteket (*Etiketter smittar* gäller adresser), att en
  märkt Ethereum-tx avslutar spåret (Tracings klassning för konton tar bara adresser), och historiken (AN1b) för tx.
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
- **Svar utan kurs räknas som kurser** (omgång 5): ett slutligt svar före Bitstamps historik pekas ut i registret som
  en kurs, så *Sparas i ärendet* (`RateSummary`) räknar det. Ett eget register för *ingen kurs* vore rättare.
- **Före källans historik, efter granskningen av omgång 5 (prova mot ett riktigt anrop före 1.0):** ett svar från
  Bitstamp utan perioden är slutligt bara när svaret har perioder som alla börjar efter den (`HistoryStartsAfter`);
  att Bitstamp svarar så för en period före marknadens början är inte kontrollerat mot dokumentationen eller ett
  riktigt anrop. Riksbanken pekas aldrig ut som
  slutlig utan kurs (ett tomt svar visar inte att serien saknade kurs), så en dag före 1993 frågas igen efter
  väntetiden. Bevis för Riksbanken (kurser efter dagen men ingen före i samma svar) kan läggas till.
- **Riksbankens dygnsgräns** räknas sedan omgång 5 över omstarter på den här datorn (`Kurser\riksbank-requests.json`),
  men datorer bakom samma utgång delar gränsen utan att veta om det. En nyckel i valvet (200 anrop i minuten) höjer
  gränsen; den behöver en rad på sidan Källor (skiss).
- **Fler källor och tokens:** (ETH sedan etapp 23, USDT och USDC på Ethereum sedan etapp 23b, TRX och USDT på TRON
  sedan omgång 8, alla efter kontraktet och aldrig som en dollar av sig själva); en
  andra kryptokälla att jämföra med (till exempel Kraken eller Coinbase för nyare perioder); Riksbankens nyckel (200
  anrop i minuten) i valvet om gränsen på 5 i minuten blir ett problem.
- **Ett gemensamt lager för råsvar:** Chains, Pricing och nu Attribution (Chainabuse) har var sin tabell och kod för
  förseglade råsvar, så villkoret *när en tredje modul behöver det* är uppfyllt. Bedömt i omgång 5 och inte gjort än:
  kolumnerna skiljer sig (Chains har endpoint och block, Pricing sort och ämne, Attribution kedja och adress), en
  flytt ändrar tre moduler och tre hashkedjor samtidigt som andra arbetar i Chains och Cases, och vinsten är mindre
  kod, inte en ny förmåga. Görs som en egen etapp: ett kontrakt i Cases (`ISealedResponses` med källa, ämne,
  utgång, hash och kropp) och modulernas egna index ovanpå.

## Säkerhet
- **Klartext i minnet:** API-nycklarnas hemligheter och profilen ligger som `string` medan valvet är upplåst och kan inte
  nollställas (valvets JSON skrivs nu i en fäst buffert, omgång 6). `char[]` för hemligheterna ändrar `IApiKeyStore`
  och varje källa som läser en nyckel; görs när ingen annan omgång arbetar i Chains och Attribution.
- **Den gamla databasnyckeln i SQLite3 Multiple Ciphers minne** (granskningen av VT1a, M4): biblioteket kan ha kvar
  kopior av en databasnyckel i anslutningens minne (chiffrets tillstånd) efter att anslutningen stängts, också efter
  ett byte av filnyckeln. Chainsaw nollställer sina egna buffertar men når inte bibliotekets; ta reda på om
  `sqlite3_close` nollställer chiffrets nyckelmaterial, och annars om en egen allokerare eller en process per ärende
  behövs.

## Utgångar (Network)
- **Efter säkerhetsgranskningen av PX1a och KL3b (2026-11-08, M):** NTLM tillåts för proxyns inloggning (Negotiate kan
  falla tillbaka till NTLM; en förfalskad proxy kan få ett NTLMv2-svar) — överväg bara Kerberos eller säg det i
  Inställningar; ett utgånget nålat certifikat godtas för alltid — markera det i KL3b-raden när `NotAfter` passerat;
  nålen och flaggan *lokal* i `sources.json` saknar integritetsskydd (MAC under en nyckel i valvet); ett förseglat
  råsvar från en nålad nod säger inte att kontrollen var en nål — ta med fingeravtrycket i härkomsten.
- **Certifikatkedjor som saknar en mellanliggande nivå** som datorn aldrig sett: de som Windows redan har erbjuds
  (omgång 6), men inget hämtas runt utgången (AIA och spärrlistor), och `IExitHttp` tar bara https. Att hämta en
  mellannivå genom utgången över http är ett beslut för användaren (granskningen etapp 4, C).
- **Rötter som läggs till medan Chainsaw är igång:** de betrodda rötterna läses när en utgång ansluts. En rot som
  Windows lägger till senare gäller från nästa anslutning.
- **Rötternas begränsningar på Windows:** med Chainsaws egen rotlista följs inte Windows egna begränsningar för en rot
  (användningsområden, *distrust after*). Prova på Windows (TESTER-INFOR) och bestäm om de ska läsas in.
- **Inställningar som inte går att spara** (etapp 19, granskningen omgång 2, M): bara ett misslyckat val av VPN tar
  bort en fil som annars ger Windows vid nästa start; en strängare låsning (kortare tid, lås med Windows) som inte går
  att skriva ger den gamla tillbaka vid nästa start. En profil som inte gick att spara efter att dialogen stängts sägs
  inte, och två profilsparningar tätt efter varandra kan bli klara i fel ordning (de kunde köas som inställningarna).
  Rör inställningarna, som omgång 7 arbetar i.
- **100.64/10 räknas inte som eget nät** (etapp 25, granskningen I1): leverantörens delade adresser (CGNAT, också
  Tailscale) nås inte som egen nod, eftersom de kan ligga utanför användarens nät. Ändras bara efter användarens beslut.
- **Tunnelns hjälpprogram och Go:** `chainsaw-tunnel.exe` är byggd med Go 1.27.3 (låst i `go.mod`) och checkad in
  med sin hash; den byggs om med `tools/Chainsaw.Tunnel/build.sh` (som kör `govulncheck`) när Go eller beroendena får
  säkerhetsrättelser, minst när en Go-version slutar få stöd.
  `dotnet test` kör inte Go-testerna; de körs av byggskriptet. Signera programmet med organisationens certifikat när
  Chainsaw signeras.
## Ärenden
- **Vem som förseglade en post:** en modul kan försegla poster i en annan moduls kedja (inte i Cases egna), och posten
  säger inte vilken modul som skrev den. Att binda transaktionen till sin modul ändrar varje moduls sätt att försegla
  (en token per modul i stället för kedjans namn); görs när ingen annan omgång arbetar i modulerna (granskningen
  etapp 3, omgång 5, M).
- **Konflikten avgörs utan nyckel** (omgång 6, omgranskningen M): `FindConflict` och `OpenSettingAsideWorkingCopy`
  avgör med `MatchingBase` (bara basens hash), så när någon skrivit en egen hash i basen efter att ha ändrat huvudet
  får användaren *skadad* i stället för valet att lägga arbetskopian åt sidan. Låt dem kontrollera sigillet med
  valvets nyckel (`SealedHash`) när valvet är upplåst.
- **`CasesOptions` är publik i implementationsprojektet** (som `VaultOptions`), eftersom `AddCasesModule` tar den.
  Flytta båda till Contracts eller till ett eget alternativ-gränssnitt om regeln ska vara strikt.
- **Stora ärenden:** att öppna kopierar hela databasen (ett ärende som bara lästs skrivs inte om, omgång 6). Mät när
  ärendena blir stora (hundratals MB) och överväg ett eget VFS med huvudet i filen.
- **En återkallad nyckels rad skriven för hand** i `organization-recovery-key.pem` kan fortfarande bli fel; `revoke`
  i verktyget skriver den rätt (omgång 6), och tolkningen vägrar det mesta som är fel.

## Ärenden i appen (etapp 11)
- **Moduler som bidrar med kommandon:** `IShellCommand` ligger i SharedKernel, men ett kommando som behöver det öppna
  ärendet måste nu få det från appen (`CaseSession`). Flytta det öppna ärendet till ett kontrakt (till exempel
  `IOpenCase` i Cases.Contracts) när en modul behöver egna kommandon.
- **Ethereum-adressens kontrollsumma** (EIP-55, blandade versaler) kontrolleras inte i igenkänningen; hör till
  Ethereum (omgång 2). En TRON-tx ser ut som en Bitcoin-txid och känns bara igen med kedjan utskriven (i en länk).
- **Senast öppnade ärendet** föreslås som den `.cut` som sparades senast (filens tid), inte ur en egen lista, så att
  inget om ärenden sparas utanför ärendet. Ett ärende som sparats av en kopia eller synk hamnar då först.
- **Testklasserna saknar XML-dokumentation** (M10), som i de tidigare testprojekten.
- **Brickan *Etikett härifrån* i LN1a:** en streckad bricka vid det ärende i listan som etikettbiblioteket har en
  etikett på länkens adress från. Kräver en fråga till biblioteket per adress och ärende; inte byggd 2026-11-08.

## Exponering (EP1b)

- **M4 från granskningen:** Ethereums gasavgifter finns inte som poster i grafens linjer, så plånboksläsningen räknar
  dem inte; pengar som betalade gas står som kvar hos aktören.
- *Fått från* kör metoden en gång per inbetalning hos varje aktör på vägen; en aktör med tusentals överföringar kan
  behöva en metod som delar upp på källor i ett svep.

## Begäran (BG1b)
- **`Compose` körs på UI-tråden** (granskningen, M8): den läser bara ärendets nummer och profilen i minnet och går
  snabbt, men ett mycket stort uttag med hundratals insättningar kan kännas. Flytta den till `Task.Run` med en
  avbrytbar omgång per ändring om det märks.
- **En fil som inte gick att skriva klart** tas bort (M5), men det finns inget test som framkallar ett skrivfel.

## Rapport (RP1b)
- **Markeringen** är grafens val med Ctrl+klick eller en ruta, inte Shift-klick som skissen säger, eftersom Shift redan
  används i högen.

## Undermenyer i pillret (etapp 11b)
- **Sanktionslistan och det öppna ärendet:** en import kontrollerar inte ärendets adresser
  (`IAttributionService.CheckSanctions`, som i dag bara körs för startpunkterna när ärendet skapas); att köra den för
  grafens adresser efter en import skriver i ärendet och hör till Attribution (omgång 7 lät den vara).
- **Nytt → Etikett** är tonad som inte byggd (en etikett sätts sedan etapp 16 i inspektören med L); ändra
  `SubmenuNeed` i `Navigation/SubmenuTable.cs` till `OpenCase` och koppla raden i `ShellViewModel` när vyn Etiketter
  finns. Begäran är kopplad sedan BG1b.
- **Startpunkt med ett ärende** lägger bara markören i kommandoraden; ett eget fält (som i Nytt ärende) kan skissas.

## Låsskärmen och förstagångsinställningen (etapp 10)
- **Inställningarna, Utseende:** raden *Kättingen* (*Rör sig* eller *Står still*) är tonad med *Inte byggt än* i
  ST1a; figuren står still bara när Windows har animeringar avstängda.
- **Låsning mitt i inställningen** avslutar den: efter upplåsning visas skalet, och utgången och första ärendet erbjuds
  inte igen. En krasch mellan valvet och profilen ger ett valv utan profil, så att ärenden inte går att skapa.
  Inställningen kunde återupptas tills den är klar (granskningen, M).
- **WireGuard-filens text** läses som fästa tecken som töms (`Exits/WireGuardFile`, etapp 17), men blir en `string`
  i anropet till `IExitStore.Add`, som tar en (samma sak som *Klartext i minnet*). Kräver ett kontrakt med tecken.
- **En öppen filväljare** ligger kvar ovanpå låsskärmen om Chainsaw låses medan den är öppen; den visar bara filer.
- **Instabilt test:** `LinkCaseTests.After_the_unlock_the_picker_folds_out_with_the_link_at_the_top` faller ungefär
  två gånger av fem i molnmiljön (sett 2026-11-09, efter LN1a), och `Esc_throws_the_link_away` likaså: 5 av 20
  körningar av klassen faller på huvudgrenen utan grafens ändringar. Felen är dubbla eller tomma rader i
  `CasePickerViewModel` (`Chosen`), troligen en kapplöpning när ärendeväljarens lista fylls efter upplåsningen.

## Bevakning (etapp 26)
- **Mycket aktiva adresser:** en kontroll läser högst fem sidor bakåt. Har en adress fler nya tx än så går kontrollen
  vidare från det äldsta block den läste och sparar luckan (*tx under block N kan ha missats*) med kontrollen; tx i luckan
  kan ha missats. Samma när källan inte kan bläddra längre (en full sida i ett block) eller inte läst historiken
  färdigt. Läs luckan i efterhand med en markör. (Websocketen som byggdes i omgång 3 säger bara att ett nytt block
  kommit, så att kontrollen kommer tidigare och luckorna blir färre, men den läser inte luckan.)
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
  block. Ärendet växer med bevakningar som går länge; att slå ihop lika svar (samma hash) vore en ändring i Chains och
  ett beslut om princip 1 (ett svar som hämtats igen och var likadant sparas då bara en gång). Bakgrundens kö slår sedan
  omgång 3 ihop tysta kontroller till en post per bevakning, men behåller varje svar.

## Bevakning i bakgrunden (etapp 27)
- **Kluster i bakgrunden** följer klustringen som den var när planen skrevs (när ärendet senast var öppet), inte nya
  adresser som kommer med senare.
- **Ett ärende som tas bort eller flyttas** ligger kvar i planen och bevakas tills det öppnas och dess bevakningar
  pausas. Rensa planen från ärenden som inte längre finns (ärendenas id i `Ärenden`). Lämnat i omgång 3: ett ärende kan
  öppnas var som helst (`OpenWithPassword`, en kollegas fil), så ett id som saknas i `Ärenden` betyder inte att ärendet
  är borta, och att rensa det skulle tyst sluta bevaka. Ett beslut: planen kunde hålla filens sökväg (under DPAPI), eller
  `ICaseStore` lista id:n ur huvudena.
- **Höjden som inte ledde till någon kontroll** förs inte in i något ärende (den hör inte till ett ärende); höjden som en
  kontroll byggde på förs in med ärendets första post i varvet.
- **Endpoints med API-nyckel** fungerar inte i bakgrunden (nyckeln ligger i valvet); de hoppas över och nästa tas. Samma
  väg som för VPN-utgångarna (en kopia under DPAPI för de nycklar användaren väljer) går att bygga om användaren vill.
- **Aviseringen i Windows** kommer från en ikon i meddelandefältet (`Shell_NotifyIcon`), som Windows visar som en toast.
  En riktig toast med appens identitet (AUMID) kräver att Chainsaw registreras som app; pröva om det behövs.
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
- **Test saknas** för en omläsning av grafen medan en ångring sparas (granskningen omgång 3, M4).
- En adressnod visar inte en klusteretikett som sitter på en annan adress i ett kluster den inte är en del av i grafen
  (granskningen omgång 1, M5).

## Testärendet (/testdata)

- **Ethereum i testärendet, det som saknas:** bara bedragarens adress är expanderad; offret, ett bytt block och en
  borta tx på Ethereum, en brygga och USDC finns inte i den påhittade Ethereum-delen (`TestEthereumChain`). Swappen
  går genom Uniswaps router med USDT till routern själv, som den påhittade källan inte skiljer från paret.
- **Kollegans etiketter** (Garantitorget, Nordbörsen) ligger kvar i etikettbiblioteket på datorn efter `/testdata`. De
  gäller bara påhittade adresser och kan aldrig träffa en riktig, men biblioteket saknar ännu ett sätt att rensa dem.
- **Valvyn i testärendet:** Navet (66 tx, många motparter) och gruvbelöningens OP_RETURN finns i testärendet; en
  adress med en spårad ankomst, en mixer och en utbetalning nära det spårade beloppet bland det som hölls tillbaka
  saknas, så att markeringen och tidsfönstret runt ankomsten syns i `/testdata`.
- **Delarna tillsammans** i testärendet är en slump: kombinationen hittas bland skalningskedjans och navets
  utbetalningar. En avsiktlig utbetalning i delar ur en mixer (helst Virveln, med egna mynt att betala ur) vore
  tydligare.
- **Högar** i testärendet: grafens samlingsnod kommer först när användaren expanderar Navet, och startvyns högar
  (GR8a) sparas på datorn och läses bara när startvyn visas, så `/testdata` lägger inga ärenden i en hög.
- **Bara Bitcoin** i det bytta blocket: en tx på Ethereum som bytt block väntar på att Ethereums svar jämförs (TODO,
  *Bytt block på Ethereum*).
- **Samma insamling och Rundgång** (`pattern v1`) finns inte i testärendet: berättelsen har *Samma väg* och
  *Likadana belopp* (2026-11-09). En insamling (minst fem avsändare till en adress, två gånger, svepta till samma
  okända plånbok) och en rundgång genom två mellanhänder tillbaka vore nästa.
- **Adressförgiftningens par** är framsökt: fröna till Sydbörsens insättning och förgiftarens adress
  (`sydborsen-insattning-1849900` och `forgiftaren-754626`) hittades genom att pröva miljontals frön tills de fyra
  första och sista tecknen var lika. Byts adresstyp eller prefix i `TestAddress` måste paret sökas om.

## Övrigt
- **Guiden:** avsnitten skrivs medan funktionerna byggs i `docs/guide/` och byggs in i programmet. Ett nytt avsnitt
  läggs i indexet i `src/Chainsaw.App/Guide/GuideLibrary.cs` (annars hamnar det under *Övrigt*), och länkar mellan
  avsnitten skrivs `[text](fil.md)` eller `[text](fil.md#ankare)`.


## Vyerna efter 1.0

- **Tidslinje och Flöde, efter bygget 2026-11-08:** flödet läser aktörerna som plånböcker; på Bitcoin kan det följa
  mynten som spåret i grafen gör, så att siffrorna aldrig skiljer sig. LIFO och poison läggs i metodens val när
  Tracing har dem (fråga 1 till kryptospåraren). Avgifterna kan bli en egen grå tråd *Avgift* som i skissen. Tidslinjen
  kan gruppera banorna när grafen har hundratals aktörer, och ett klick på en bana kan välja aktören.
- **Plånböcker, efter granskningen 2026-11-08:** en avfärdad plånbok dras inte av som en godkänd, så en ny adress i
  samma grupp föreslås aldrig (`WalletCheck.KeyOf`); ett godkänt fynd utan tx i grafen har tomt underlag (prova på
  Windows); `PreviewUndoMerges` visar inte heuristiklänkar som slutar slå ihop efter en senare uppbrytning.
- **Soptunnan för lappar och högar, efter granskningen 2026-11-08:** en lapp som töms precis före Ctrl+W slängs inte
  (`SealBeforeClose` väntar inte på köade skrivningar); en lapp som redan slängts från ett annat fönster läser inte om
  soptunnan; en slängd hög kommer tillbaka till hälften när ett av dess ärenden sparas igen (`ForgetRewritten`);
  historiken säger *en nod utanför grafen* för en nod i soptunnan; `IGraphCommentService.RemoveComment` saknar anropare.
- **Etiketter, efter omgranskningen 2026-11-08:** skäl och anteckning följer med kopior, så en mening vid fältet
  Anteckning borde varna för att skriva offrets namn där; `HeldBack` gäller bara på den egna datorn (en kollegas
  bibliotek kan sprida en kopia tills ursprungsärendet öppnats där); vyn räknar *nära ett offer* på aktören, biblioteket på hela klustret.
- **Grafens kort och markeringar, efter omgranskningen 2026-11-08:** en grund tx hämtas inte om när den endpoint
  som är i fas väntar eller fallerar, och *i fas* bygger bara på provet vid start; schemasteg 1 ändrades på plats
  (`block_hash`, `chains_not_found`), så äldre testärenden ska slängas.
- **Rapporten, efter omgranskningen 2026-11-08:** provet mellan kontroll och försegling ser bara journalen; ett
  slut som nås genom en CoinJoin utan markerad avsändare blir *Börs A → Börs A*.
- **Ärendets kuvert, efter omgranskningen 2026-11-08:** kraschar Chainsaw mellan att grenen öppnats och att
  grenraden skrivits blir `/verify` röd för grenen (skriv raden under ett tillfälligt namn först); en ovanlig dubbel
  felväg i `OpenSettingAsideWorkingCopy` kan lämna arbetskopian i *Återställda* utan journalrad.

## Kodgranskningen 2026-11-08 (mindre fynd som inte rättades)

Rapporterna per varv ligger inte i repot; det som står kvar är detta.
- **Virtualisering som kräver skiss:** kontrollpanelens tre listor (*Att avgöra*, *Gjort av sig självt*, *Beslutade*)
  och papperskorgsvyns grupper är inte virtualiserade; ett stort ärende bygger en rad per post.
- **Rapportens aktörsbrickor** ligger i en `WrapPanel` utan tak; en lasso över en stor graf gör en bricka per aktör
  (förslag: tak på ungefär 50 och "+N till"). `TrashWarningViewModel.CountBelow`/`IsLast` är O(n²) på en stor kastning.
- **Endpoint före startprovet:** ett uppslag kan gå till en endpoint som inte har provats än; en fel kedja med lägre
  höjd (testnet4, Sepolia) fångas först när startprovet körs. Svar som redan ligger i äldre ärenden med orimliga höjder
  räknas inte längre (varv 5) men ligger kvar.
- **`/verify` och indexen:** indexen byggs inte om ur de förseglade svaren vid `/verify`; höjderna räknas bara med ett
  förseglat svar bakom sig och under det kedjan kan ha nått (varv 5), men en ändrad höjd upp till den gränsen syns
  inte.
- **CoinJoin v2:** en CoinJoin vars alla inputs kommer från en och samma tidigare tx känns inte igen (`funding < 2`).
- **Exponeringen på konton** tar inte med avgifterna, så FIFO och haircut kan skilja något från grafens spår.
- **Bevakningen:** en tips vars Ethereum-svar aldrig blir komplett frågas en gång i minuten utan slut; om importen inte
  kan läsa kön just nu kan rundans egen kontroll starta en ostartad bevakning högre än bakgrunden gjorde; planens skrivning
  vid stängning görs på UI-tråden; `CaseHolds.IsHeld` kan krocka med `Hold` (bara en extra avisering).
- **Ärendet:** en `schema_version` ändrad för hand ger `InvalidOperationException`, inte `Corrupt`; journalvyn kastar på
  en post vars tid ändrats med nyckeln (`/verify` rapporterar den); efter *behåll båda* kan lösenord och kuvert ändå
  journalföras i arbetskopian; ett lösenordskuvert utan `iterations` tas inte bort som svagt.
- **Bakgrundsprocessens namn:** mutex och stopphändelse är `Local\Chainsaw.Watcher[.Stop]` och kan tas först av en annan
  användares process i samma session (en privat objektnamnrymd vore rätt); samma för appens egen mutex.
- **Länkröret** börjar lyssna först efter värdens start; en länk under en start längre än 3 s tappas tyst.
- **Uppslag och kurser:** leverantörens Retry-After läser `UtcNow` i stället för `IClock` och visas i UTC; en 429 från
  Bitstamp eller Riksbanken läser inte Retry-After (avkylningen täcker det); `BlockscoutApi.ReadHash` kontrollerar inte
  blockets höjd; varje adressbyte i Windows nollställer återanslutningens väntan.
- **Begäran:** insättningarnas belopp nämner en token bara med symbolen; rapportens del heter "Belopp i SEK" också med
  en annan valuta; exponeringens förklaring skriver BTC i annat format än resten; en etikett som ser ut som en
  webbadress blir en länk i en GFM-förhandsvisning av exporten; rapportens toppvakt jämför bara journalraden.
- **Grafen:** "Uppbruten · N ut" räknar för lågt när en vald adress bara är länkad till en annan vald; animationer som
  redan rullar stoppas först vid nästa omritning när Windows animeringar stängs av; ett kort som hämtar ritar om hela
  grafen 60 gånger i sekunden.
- **Bakgrundens höjder (varv 8 och 9):** när ärendet inte hade någon höjd då bevakningen gjordes golvas en
  bakgrundsstart bara med tiden, så en förfalskad start kan ligga upp till ungefär dubbla tiden sedan bevakningen
  gjordes under den verkliga höjden; en ovanligt snabb följd av Bitcoin-block, eller en klocka som gick fel när
  bevakningen gjordes, kan i teorin lyfta golvet över en ärlig start; sätts alla bakgrundens poster för en bevakning
  undan som oläsliga startar ärendets kontroll vid toppen; en förfalskad väntande post håller indikatorn för köade
  träffar tänd tills bevakningen startas, återupptas eller tas bort; `MonitoringService.Start` läser inte om
  starthöjden i sin skrivning; upptagna bevakningar når `MaximumPages` oftare (ärligt märkt som delvis); `LowestAt`
  saknar ett eget test.
- **Instabilt test i LN1a:** `LinkCaseTests.After_the_unlock_the_picker_folds_out_with_the_link_at_the_top` fallerar
  ungefär en gång av fyra i hela sviten (också utan granskningens ändringar); troligen samma trådmönster som
  `PileTests` och `BackgroundLinkTests` hade (rättat med `UiThread` i `Fakes.cs`).

