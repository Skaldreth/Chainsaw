# Att göra

Det som är kvar för att Chainsaw ska vara färdigt (1.0). **Ta bort en punkt när den är byggd.** Det som kommer efter
1.0 och de mindre fynden från granskningarna står i `docs/BACKLOG.md`.

Filen har tre delar:
1. **Väntar på dig:** frågor som bara användaren kan avgöra. Till dess gäller det som står efter *Nu*.
2. **Vägen till 1.0:** det som ska vara gjort för att Chainsaw ska vara färdigt, i den ordning det tas.
3. **Före tjänstebruk:** sådant som inte är kod men måste vara klart innan Chainsaw används i tjänsten.

Varje punkt i *Vägen till 1.0* är märkt med hur den byggs (CLAUDE.md, *Processen efter storleken*):
- **[direkt]:** en rest eller rättelse inom en godkänd skiss, som byggs och granskas mot skissen av sessionen själv.
- **[skiss]:** ritas först på canvasen, och byggs när användaren sagt ok.
- **[etapp]:** skriver i ärendet eller rör principerna, och byggs med oberoende granskning.
- **[pågår]:** byggs just nu av en agent (omgång 4, 2026-11-08).

## Väntar på dig

Frågorna till kryptospåraren står i `docs/KRYPTOSPARAREN.md`.

**Grafen på grafikkortet** (användarens önskemål 2026-11-08): WPF ritar redan genom DirectX, men varje ruta byggs
på processorn. Efter optimeringen 2026-11-08 (scenen i ett eget lager som bara flyttas vid panorering och zoom)
provas grafen först på Windows med ett stort ärende. Räcker det inte är nästa steg att rita grafytan med **SkiaSharp
på grafikkortet** bakom samma `GraphCanvas`, så att inget annat ändras. Samma prov avgör om nodernas ritning ska
göras på flera trådar. *Väntar på:* provet på Windows med prestanda-HUD:en (Ctrl+Shift+P, Debug).

**Från bygget 2026-11-08**, så som det är byggt nu:
1. **Kandidaternas linjer** i mixerns förslag ritas alltid. Förut ritades de bara när områdena var på. *Nu:* alltid.
2. **SEK för det som ligger kvar i inspektören** räknas till den senaste kursen ärendet har för tillgången. Kortets
   baksida (IN4a) visar dagens kurs genom utgången. *Nu:* inspektören tar den senaste kursen i ärendet.
3. **Soptunnans vy ritar noderna i sortens färg**, så en mixer blir röd, fast soptunnan aldrig ska använda rött.
   *Förslag:* neutralt grått i soptunnan.
4. **N med en vald nod** knyter den nya lappen till noden. Skissen säger det bara om ikonen i nodens list. *Nu:*
   lappen knyts.
5. **Lappen och noden:**
   - När en grupp dras följer de knutna lapparna med, men när en ensam nod dras står lappen kvar och bara tråden
     följer.
   - I hoppkolumnerna står lapparna kvar på sin plats från den fria ytan, så tråden kan bli lång.

   Ska en knuten lapp alltid följa sin nod?
6. **Ett vanligt klick tömmer markeringen**, också ett klick på en nod, som i Utforskaren. Förut låg en markering
   gjord med Ctrl+klick kvar tills Esc. *Nu:* klicket tömmer.
7. **Chainabuse och flera adresser:** får ett anrop (av tio i månaden) användas för att prova två adresser
   kommaseparerade? Går det kan ett helt kluster frågas i ett anrop (`docs/BACKLOG.md`, *Attribution*).
8. **Pausad bevakning:** det som hände under pausen räknas som träffar när bevakningen återupptas, så att inget
   tappas. *Nu:* det räknas. Det andra valet är att räkna från återupptagandet.
9. **En godkänd mixerkandidat** förseglas som beslut men blir inte en ny startpunkt eller en kant i spåret. Ska den
   kunna bli en startpunkt med beloppet?
10. **Kopior och sanktionsträffar i historiken** har ingen knapp, eftersom skissen bara visar det egna. Ska en etikett
    som kopierats från ett annat ärende kunna avvisas där?
11. **Etiketter och offer:**
    - En etikett på ett offer sprids aldrig till andra ärenden. Den regeln är sessionens egen och står kvar tills du
      säger annat.
    - Ska *Sprid inte* också gå att sätta för ett helt ärende?
12. **En trolig tjänst** (TJ1b): kontrollen `service-behaviour` är en kontroll, inte ett förslag, så den står inte i
    KF2b-panelen. *Nu:* *Som etikett …* står i inspektören vid noden. Ska svepet bli ett förslag som väntar i panelen?

**Från Litecoin och BSC 2026-11-09** (DEV-LOG, *Omgång 8b*):
20. **BSC:s källa:** det finns ingen öppen Blockscout för BSC (Blockscouts egen lista har ingen för kedja 56), BscScans
    API har gått upp i Etherscans, som för BSC kräver en **betald** nyckel och tar den i adressen, och BSC:s öppna
    JSON-RPC-noder har ingen historik per adress (allt prövat 2026-11-09). *Nu:* BSC har ingen endpoint från början;
    användaren lägger till en egen eller köpt Blockscout för BSC under *Källor*, med nyckeln i valvet. Ska Chainsaw
    tala Etherscans API (v2, `chainid=56`) med en betald nyckel? Det kräver att nyckeln kan skickas i adressen men
    förseglas utan den (BACKLOG, *Etherscan med API-nyckel*).
21. **USDT och USDC på BSC** är Binances bryggade tokens (*Binance-Peg*), inte Tethers och Circles egna. *Nu:* de känns
    igen på kontraktet, har **inte** snöflingan *Kan frysas* (inget känt sätt att frysa dem), och kursen tas från
    Bitstamps marknad för den token de står för (USDT, USDC). Stämmer det med hur kryptospåraren ser på dem?

**Från Litecoin i grafen och Zcash 2026-11-09** (DEV-LOG, *Omgång 8c*):
22. **Zcashs förvalda källa** är Zelcores öppna Blockbook (`blockbook.zec.zelcore.io`), ett plånboksföretags, utan
    nyckel; det var den enda öppna som svarade (BACKLOG, *Flera UTXO-kedjor efter omgång 8c*). Duger den som förval,
    eller ska Zcash vara utan endpoint tills en egen eller köpt läggs till (som BSC)?
23. **Ett ärende med flera UTXO-kedjor** spårar bara den första startpunktens kedja och säger det. Räcker det till 1.0?

**Äldre, där sessionen valt det säkraste:**
- **Återkallade återställningsnycklar** (etapp 9):
  - När nyckeln byts ligger den gamla nyckelns kuvert kvar. Det tas bort först när nyckeln återkallas med en rad
    `REVOKED` i `organization-recovery-key.pem`.
  - Att ta bort alla andra nycklars kuvert vid ett byte vägrades i granskningen. En äldre version, eller en annan
    organisations Chainsaw, tog då bort den nyare nyckelns kuvert.

  Bekräfta att det räcker att återkalla för hand.
- **Endpointarna** (etapp 25):
  - **Takten:** Chainsaw frågar en endpoint på internet högst fyra gånger i sekunden. Ska takten gå att ställa per
    endpoint?
  - **Tailscale och CGNAT** (100.64.0.0/10) räknas inte som det egna nätet, så en nod där kan inte märkas lokal.
  - **Prioriterad** sprider inte uppslagen: nästa endpoint tas först när den första ber Chainsaw vänta.
- **Guiden från spårningsmetoden** (etapp 18):
  - Ett klick på spårningsmetoden byter metod, och ett högerklick öppnar Guiden vid metoden.
  - *?* visar bildspråket, och dess *Guiden F1* öppnar panelen.

  I GD1b öppnade båda Guiden direkt. Bekräfta.

## Vägen till 1.0

Ordningen: först provet på Windows, sedan resterna i det som redan är byggt. Allt som valdes på sidan *Mot 1.0* är
byggt; det som står här är rester. Inget nytt byggs förrän det som finns känns bra (användarens beslut 2026-11-07).

### 1. Provet på Windows

**Bygget och testsviten går på Windows** (användarens körning 2026-11-08): 3 790 tester, alla gröna, på 119 s. Kvar är
att prova appen för hand efter `docs/TESTER-INFOR-1.0.md`.

Inget sedan etapp 1 är provat på Windows. Användaren provar efter `docs/TESTER-INFOR-1.0.md`, ordnad uppifrån och ner, och det som inte fungerar rättas före allt annat nedan. Särskilt:
- **Kraschen vid start** (`NewCasePanel`) är rättad men inte bekräftad.
- **Expansionen gav *Inget nytt att hämta*** med mempool vald (rättat 2026-11-07, väntar på prov).
- **Allt som byggdes 2026-11-08** är osett: KF2b, NC2a, KN1a, SO1a, GR7a, TL1a/TL2a, IN4a, GR9a, BG1b, ET1a, EP1b,
  RP1b, KU1a, BV4a, ST2a, FS2a, UT3b, ÄR1b, KR1a, Spara ofta, plånböckerna och soptunnan för lappar och högar.

### 2. Kvar efter omgång 4

- **En tx som inte finns längre, resterna** (Bitcoin byggt 2026-11-09):
  - **Ethereum** [etapp]: GR9a ritar en borta Ethereum-tx, men kontomodellens spårning räknar fortfarande med den.
  - **En obekräftad tx som aldrig låg i ett block** och försvinner ur mempool räknas inte som borta, eftersom
    regeln (`BlockChangeRule`) jämför med det första svaret i ett block [etapp].
- **Sökningen:** en etikett från ett annat ärende kan inte säga vilket, eftersom ursprunget saknar ärendenumret
  [etapp].
- **Begäran:** kontaktvägar till börserna (katalogen är tom tills en väg kan kontrolleras mot sin källa, med källa
  och datum) och fler mallar per börs, om kryptospåraren vill det.
- **Chainabuse automatiskt** [etapp], när en partnernyckel finns: en vanlig nyckel ger tio anrop i månaden, så nu
  frågas den bara med R.

### 3. Backloggen (användarens beslut 2026-11-08: allt före 1.0)

Hela `docs/BACKLOG.md` byggs före 1.0, i omgångar där agenterna inte rör samma filer. Varje omgång tar bort det den
byggt ur BACKLOG; det som skriver i ärendet granskas oberoende.
1. **Skisserna är valda** (2026-11-08): TL3a, FL1a, PL1b och SW1a (CLAUDE.md, *Valt för 1.0*).
2. **Ethereum klart:** Tornado Cash och kända DEX:ar och bryggor, OFAC:s ETH-adresser, kontoheuristikerna, kontrollerna och plånböckerna på Ethereum, egna ytor, Etherscan med nyckel
   och fynden från etapp 23 och 23b.
3. **Bitcoin-källan, bevakningen och bakgrunden:** byggt 2026-11-08 (DEV-LOG, *Omgång 3*): VPN i bakgrunden, nya
   block i realtid, starten i fas, omorganisationer, merkle-beviset och de andra fynden. Det som är kvar står i
   BACKLOG med en notering om beslutet det väntar på, och ytorna nedan.
4. **Klustring, förvirring och kontroller:** tjänster ur beteende, fler tecken för växeln, CoinJoin och peel chain,
   centrala mixers, damm och adressförgiftning, jämna belopp och tidsmönster, och fynden under *Kontroller*.
   Omgång 5 (2026-11-09, DEV-LOG) byggde trolig mixer, mixer efter mixer med grenar, uppdelning över flera tx, hopp
   med avgift, tidsmönstret i svensk tid och nästlade tjänster. Kvar:
   - **Testärendet** [direkt]: `/testdata` visar inte en betalning i jämna kronor (`round-fiat v1`, med kurserna
     sparade i testärendet).
5. **Attribution och kurser:** EU:s sanktionslista, öppna källor (efter licens), etiketter på tx, och fynden under
   *Attribution* och *Kurser*.
6. **Säkerhet, ärenden och utgångar:** byggt 2026-11-08 (DEV-LOG, *Omgång 6*). Kvar, som behöver ritas först:
7. **Vyerna och appen:** byggt 2026-11-08 (DEV-LOG, *Omgång 7*). Kvar, som behöver ritas först:
   - [skiss] **Trolig mixer eller vidarebetalande tjänst** (`mixer-behaviour`, omgång 5): fyndet ritas i dag som en
     mixer, rött, som betyder risk. Ska det tvetydiga fallet se annorlunda ut (en annan ton eller en egen ikon)?
   - [skiss] **Fråga om det som kan ha ändrats:** grafen visas bara ur ärendet, så ett mynt som låg kvar står som *kan
     ha flyttats* tills noden expanderas igen. En knapp eller ett kommando som frågar om alla sådana mynt på en gång.
   - [skiss] **En `chainsaw:`-länk i en lapp till något som inte finns i grafen** säger det bara; den kunde bli en
     startpunkt.
   - [skiss] **Fler noder med storlekshandtag** (användarens önskemål 2026-11-07): kommentarsnoden har det; vilka fler
     som får det (samlingsnoden EX2b, egna ytor).
   - [skiss] **Startpunkt med ett ärende** (Nytt → Startpunkt) lägger bara markören i kommandoraden; ett eget fält som
     i Nytt ärende.
   - [skiss] **Ta tillbaka en egen yta i Historik** (KF1b, Z): samma rad och formulär som för en etikett.
   - Resterna i BACKLOG under Exponering, Begäran, Låsskärmen, Historik, Testärendet och *Vyerna efter 1.0* står kvar
     där, med varför.
8. **TRON med USDT** är byggt (omgång 8, DEV-LOG, resterna i BACKLOG under *TRON efter omgång 8*), **Litecoin** och
   **BSC** (omgång 8b), **Solana** (omgång 8c, DEV-LOG, resterna i BACKLOG under *Solana efter omgång 8c*), och
   **Litecoin i grafen** och den genomskinliga delen av **Zcash** med förslagen ur de skyddade poolerna (omgång 8c,
   DEV-LOG, resterna i BACKLOG under *Flera UTXO-kedjor efter omgång 8c*). Litecoin, Zcash och BSC, kvar:
   - [skiss] **Zcash i ytorna:** startpunktsnodens brickor (GR6c) och ett eget märke; raden i *Nytt ärende* och
     *Väntar på källan* visar `Z` som text, som `Ł` och `B`.
   - [skiss] **Summorna på en Litecoin- och Zcash-nod:** grafens summor per aktör räknar bara BTC
     (`GraphScene.Shown`), som för Ethereum; hur en nod på en annan UTXO-kedja visar sin summa skissas med tokens.
   - [skiss] **Välja kedja för en `0x`-adress:** en BSC-adress skrivs som Ethereums, så i *Nytt ärende*, i formuläret
     *Bevaka* och i kommandoraden blir `0x…` Ethereum om inte `bsc:` står före. En väljare (Ethereum, BSC) vid raden
     eller fältet när texten passar båda.
   - [skiss] **Startpunktsnodens brickor (GR6c)** visar inte Litecoin och BSC.
   - [skiss] **Ett eget märke för Litecoin och BSC:** raden i *Nytt ärende* och *Väntar på källan* visar `Ł` och `B`
     som text, inte en ritad ikon som TRON:s.

   Solana, kvar:
   - [skiss] **Ett eget märke för Solana:** raden i *Nytt ärende* och *Väntar på källan* visar `S` som text, som `B`
     för BSC; SOL har stålets färg på kortets baksida som ether.
   - [skiss] **Startpunktsnodens brickor (GR6c)** visar inte Solana.
   - [skiss] **Fryst på Solana:** saldot säger redan när ett tokenkonto är fryst (`state: frozen`); en markering
     *Fryst* bredvid snöflingan skissas med TRON:s.
   - [skiss] **En tx som inte kunde paras:** en Solana-tx med flera avsändare och mottagare av samma tillgång sägs i
     inspektörens text; hur den syns i grafen (en streckad nod för tx:en?) skissas först.

   TRON:s ytor som väntar:
   - [skiss] **Fryst:** Tethers kontrakt på TRON har en svartlista (`isBlackListed`); en adress som står där kunde få
     en markering *Fryst* bredvid snöflingan *Kan frysas*, med när den lästes och ur vilket svar. Hur den skiljer sig
     från snöflingan (som bara säger att utgivaren *kan* frysa) och hur ofta den frågas skissas först.
   - [skiss] **TRON:s brickor i den tomma startpunktsnoden (GR6c):** brickorna Bitcoin-adress, Ethereum-adress,
     Bitcoin-tx och Ethereum-tx lyser när texten passar, men TRON har ingen bricka än; en TRON-adress läggs ändå till.
   - [skiss] **TRX:s egen färg på kortets baksida:** TRX har stålets färg som ether (en nod har aldrig båda), och
     *Väntar på källan* visar `T` för TRON; en egen ton och ett eget märke skissas om det behövs.
   - [skiss] **TRX:s avgift i inspektören:** TRON bränner TRX för energi och bandbredd (ofta noll med låst TRX);
     inspektören visar avgiften som på Ethereum, men skillnaden mellan energi och bandbredd syns inte.
9. **Egen nod och eget index**. Mönsterigenkänningen som analys (`pattern v1`, princip 5) är byggd 2026-11-09
   (DEV-LOG), och att hitta fortsättningen på en annan kedja själv är byggt som `cross-chain v1` (2026-11-09, DEV-LOG;
   resterna i BACKLOG under *Cross-chain, efter `cross-chain v1`*). Ytor som väntar:
   - [skiss] **Mönstrets utseende:** kontrollerna `pattern` ritas i dag som förvirring (sand med mixerns ikon), som
     inte passar *samma insamling* och *samma väg*. En egen ikon och ton för mönster (och om de ska ha en egen rad i
     menyn *Områden i grafen*) skissas.
   - [skiss] **Delarna som liknar varandra:** ett mönster bygger på flera delar av ärendet (två insamlingar, två vägar),
     men området ritas som ett. Att visa delarna var för sig i KF2b-bilden och i grafen (en ton per del, eller *1 av 2*
     som stegar mellan delarna) skissas.
   - [skiss] **Avfärda en kontroll:** en kontroll (*Gjort av sig självt*) har inget beslut, så en falsk träff ur
     `pattern` står kvar. Att kunna avfärda en kontroll (som ett förslag, med skäl) skissas i KF2b.
   - [skiss] **Mönstret i rapporten och i Begäran:** om *samma insamling* och *samma väg* ska kunna tas med i
     rapporten (RP1b) som en egen del.
   - [skiss] **Fortsättningen på annan kedja i grafen:** en betalning in till en brygga ritar inte sitt förslags
     kandidater på den andra kedjan (de finns bara i KF2b-panelen), och ett förslag vars betalning in är på Ethereum
     eller TRON har inget område i grafen, eftersom områdena bara ritas kring Bitcoin-adresser. Hur kandidaterna på
     den andra kedjan visas vid den lila noden (streckade linjer till noder som inte finns i grafen än) skissas först.
   - [skiss] **Flödesbilden i KF2b för två tillgångar:** bilden visar banden och beloppen, men ingen avgift i procent
     (värdet jämförs med kurserna, och skälet säger avgiften); en bild med båda tillgångarna och kronorna skissas.

**Ur omgång 4, ytorna KV1a, HB1a, BU1a, DM1a, HP1a och TJ1b** (byggda 2026-11-08, rester):
- [skiss] **En slutsats som inte dras medan en tx är borta** (HB1a, 2026-11-09): en tx vars nya slutsats skulle vila
  på en borta tx dras inte (`ClusteringRun.NotDrawn`, och `notDrawn` i journalen när mängden ändras, sparad i
  arbetsläget `clustering_not_drawn`), men i grafen och i Historik syns det inte. En tyst rad i Historik, *Väntar:
  byggde på en tx som försvunnit*, eller HB1a-ringen utan beslut, skissas först (det som står kvar är inte byggt på
  den borta tx:en, så *Låt stå* och *Släpp* passar inte).
- **Förhandsvisningen av ett tillbakadragande** (KV1a) räknar på det som är sparat: det v1 slår ihop i tx som v1
  aldrig kört syns först efter nästa körning. Raden längst ner säger när, men inte vem (det står i journalen).
- **Dammets linjer** (DM1a) är inte hårfina och prickade och har kvar beloppet; noderna tonas ned och förgiftningens
  mask står i full styrka. Den liknande adressen visar inte mer av mitten på noden (*TQ5nX7…zmK8e*).
- **Hoppen utfällda** (HP1a): noderna står på sina vanliga platser och linjerna utan väntan; *Fäll ihop* är en
  bricka ovanför raden, inte en båge.
- **Som etikett …** (TJ1b) står i inspektörens rad med *Varför?* och *Historik*, inte i KF2b-panelen: kontrollen
  `service-behaviour` är en kontroll och inte ett förslag, så den står inte i panelen (*Väntar på dig*, 12). Ingen toast med *Ångra* efter etiketten (den tas tillbaka i Historik).

**Ur omgång 3 (Bitcoin-källan, bevakningen och bakgrunden), väntar på en yta** [skiss]:
- **VPN i bakgrunden per utgång:** i dag en brytare under Inställningar › Nätverk (*Bevaka genom utgången när Chainsaw
  är stängt*) som gäller den valda utgången. Ska valet stå per rad i utgångarnas tabell (UT3b), och ska det synas
  vilka utgångar som har en kopia under DPAPI, skissas raden.
- **En träff som bytt block** (omorganisation, `WatchHit.MovedFrom`, journalraden `monitoring.hit.moved`): träffen
  vilar nu på sitt nya block, men listan i Bevakning och vaktlinjen säger det inte. Förslag: GR9a:s runda märke med
  förut och nu.
- **Kontrollerad mot blocket** (`IBitcoinSource.VerifyInBlockAsync`, `BitcoinTransaction.InBlock`: *bara källans svar*, *bekräftad av en annan endpoint* eller *bekräftad av den egna noden*): beviset
  finns men inget anropar det; var det visas i inspektören (som *kontrollerad mot bytes*) och om det körs när tx:en
  visas eller i bakgrunden skissas.
- **Den egna Bitcoin Core i sidan Källor** (2026-11-09): användarnamnet och lösenordet är i dag en nyckel i valvet
  skriven `användare:lösenord`, vald i *Lägg till endpoint …* som andra nycklar; ett eget fält för nodens uppgifter (och
  kakfilen) skissas om det behövs. En egen nod som provats utan utgång (`EndpointProbe.Unreferenced`) syns bara i
  pricken och dess verktygstips; en egen markering (*inte jämförd med kedjan*) skissas.
- **Pengar ut ur en CoinJoin som *högst …*** (BACKLOG, *Bevakning*): markeringen och den streckade formen i listan.

**Ur omgång 5 (attribution och kurser), väntar på en yta** [skiss]:
- **Etikett på en tx:** `ITransactionLabels` finns (förseglad, tas tillbaka, avslutar spåret vid en märkt Bitcoin-tx),
  men ingen knapp. Var den sätts (ikonlisten vid tx:ens linje eller swap-pillret, en ruta som IN1b) och hur den syns
  i grafen (en markering på linjen) skissas.
- **Möjlig sanktion i grafen:** en tvetydig adress på EU:s lista märks `LabelKind.PossiblySanctioned` och syns i
  inspektören och vyn Etiketter, men har ingen markering i grafen än; en streckad, nedtonad sanktionsmarkering
  (streckat betyder osäkert) skissas.
- **Listorna på datorn:** en rad per importerad sanktionslista och öppen källa (namn, datum, hash, antal adresser,
  licens) med *Ta bort …* (`Remove` finns), till exempel på sidan Källor under *Etiketter* eller i vyn Etiketter; och
  menyraden *Importera sanktionslista …* tar nu också Ransomwhere, så den kan behöva heta *Importera lista …*.

**Beslutat 2026-11-08** (står i CLAUDE.md): spårningen genom en swap (byggd, SW1a), VPN i bakgrunden (byggd i
omgång 3) och metoderna för privacy coins (omgång 9). **LIFO och poison** väntar på kryptospåraren
(`docs/KRYPTOSPARAREN.md`, fråga 1).

## Före tjänstebruk

Inte kod, men klart innan Chainsaw används i tjänsten:
- **Organisationens återställningsnyckel** skapas och byggs in. Ingen nyckel är inbyggd än, se
  `docs/ATERSTALLNINGSNYCKEL.md`.
- **Bitstamps villkor för data:** API-sidan säger både att data får spridas för kommersiella syften och att företag
  ska teckna licensavtal (partners@bitstamp.net). Kontrollera vad som gäller för en myndighets interna,
  icke-kommersiella bruk.
- **Signera** Chainsaw och `chainsaw-tunnel.exe` med organisationens certifikat.
- **Kryptospårarens frågor** (`docs/KRYPTOSPARAREN.md`), till exempel LIFO och poison.
- **Ljudet:** lyssna på myntets swoosh och justera skriptet (`tools/sounds/coin-swoosh.py`) efter användarens öra.
