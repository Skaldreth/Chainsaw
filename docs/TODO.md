# Att göra

Backloggen: det som inte är byggt, sorterat efter vad det väntar på. **Ta bort en punkt när den är byggd.** Här står allt
som diskuterats, så att inget tappas bort medan vi bygger MVP:n.

## Väntar på beslut

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

## Bygga härnäst

- **Organisationens återställningsnyckel** (användarens beslut 2026-11-07, CLAUDE.md *Säkerhet*): ett kuvert i varje
  ärendefil för organisationens publika nyckel, och ett verktyg för administratören (skapa nyckelpar, fingeravtryck,
  kontrollera, öppna ett ärende med den privata nyckeln). Den privata nyckeln finns aldrig i repot.

## Väntar på skiss


- Identiteten: loggan, accentfärgen, låsskärmen och förstagångsinställningen med teckenfältet.
- Grafen (hjärtat): noder för adress, tx och kluster, expandering, slut på spåret, spårat belopp på kanterna, brus-kontroll.
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
- **Kontrollera txid mot tx:ens bytes:** hämta `/tx/:txid/hex` och räkna dubbel SHA-256, så att Chainsaw själv vet att
  svaret är den tx som efterfrågades, och kontrollera blockets huvud (`/block/:hash/header`) mot dess hash. Då vilar
  också `/verify` av ett spår på kedjans egen kryptografi, inte bara på källan.
- **Omorganisation av kedjan:** en tx under sex bekräftelser hämtas om, men en tx som en gång setts i ett block som
  sedan föll bort märks först när den hämtas om. Blockets plats i kedjan (`/block/:hash/status`) kan kontrolleras.
- **Adressens sammanfattning** (`/address/:a`: antal tx, mottaget och spenderat), så att sidbläddringen kan visa hur
  mycket som återstår.
- **Svarshuvuden som ursprung:** se *Utgångar*. Behövs om källans `Date` ska stå bredvid Chainsaws egen tid.
- **Utgångar som aldrig kan spenderas** (granskningen etapp 5, M): en tx med en OP_RETURN-output får aldrig alla
  utgångar spenderade, så dess `/outspends` hämtas om varje gång. Bortse från OP_RETURN när det avgörs om svaret kan
  ändras (kräver tx:ens skripttyper bredvid svaret).
- **Flera källor att jämföra:** samma tx från två Esplora-källor, och en varning om de skiljer sig.

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
- **Hur långt en expansion får breda ut sig** (etapp 7, princip 7): en batchutbetalning eller en börs historik ger en
  nod per adress. En budget per expansion, eller att många outputs viks ihop i en nod, behöver skissas med grafen.
- **Fråga om det som kan ha ändrats** (etapp 7): grafen visas bara ur ärendet (`LookupMode.CaseOnly`). Ett mynt som
  låg kvar visas som *kan ha flyttats* tills noden expanderas igen. En knapp eller ett kommando som frågar om alla
  sådana mynt på en gång, och bevakningen som gör det i bakgrunden, behövs.
- **Spårningen i bakgrunden** (etapp 7, princip 6): `ITracingService.Trace` är synkron och snabb för grafer i
  storleken hundratals tx; gränssnittet anropar den utanför UI-tråden. En `TraceAsync` med avbrytning när grafen blir
  stor.
- **Stora grafer** (etapp 7): klustren hämtas en gång per kluster ur Clustering, som bygger om dem ur alla slutsatser
  varje gång. Behöver Clusterings register eller cache (se *Klustring och heuristiker*).
- **Tidsfönster för en adress historik** (etapp 7): expansionen tar de nyaste tx upp till gränsen (50 som förval).
  För en adress med lång historik behövs ett tidsfönster runt händelsen i stället.
- **Spårningsmetoden i ärendet:** vald metod sparas inte än; gränssnittet håller den. Spara den med grafens läge
  när grafen byggs.
- **Hitta fortsättningen på en annan kedja själv** (CLAUDE.md, *Cross-chain*): belopp och tid hos swap-tjänster,
  bryggornas egna händelser. I v1 knyts den för hand.

### Attribution
- **EU:s sanktionslista** (och andra) bredvid OFAC SDN, som etapp 8 läser ur en sparad XML-fil.
- **Kan frysas** (CLAUDE.md, *Sorterna*): markeringen för adresser med USDT eller USDC hör till Ethereum och TRON. SDN
  har redan adresser för `USDT`, `ETH` och `TRX`, som etapp 8 läser in och räknar men ännu inte slår upp.
- **Öppna källor:** till exempel ransomwhe.re (öppen datamängd med ransomware-adresser), WalletExplorer (äldre BTC-kluster),
  Chainabuse (kräver nyckel). Varje källa granskas för licens och kvalitet innan den byggs in. `LabelSource.OpenSource`
  finns i kontraktet.
- **Appen kopplar ihop det** (etapp 8 har inget gränssnitt): när ett ärende öppnas `ILabelLibrary.Absorb`, när grafen
  har adresser `ShareInto` och `CheckSanctions`, och vyn Etiketter (skissas först).
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
- **Etiketterna läses en gång per adress** när grafen byggs (O(adresser × etiketter)). Läs dem en gång per bygge
  (granskningen etapp 8, M7).
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

### Kurser
- Kursen vid tx:ens tid i vald valuta (SEK som förval), med källa, sparad i ärendet. Riksbankens kurser för SEK mot USD och
  EUR kan vara ett bra underlag; kryptokursen från en källa med historik per minut eller timme. Välj källor och skriv in dem.

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
- **Tunneln:** Chainsaws eget exemplar av hjälpprogrammet för WireGuard (CLAUDE.md, *Säkerhet*), kopplat in bakom
  `IExitConnector`. Det ska köra WireGuard i användarläge (som wireguard-go med en SOCKS5-port, i stil med wireproxy),
  lyssna bara på `127.0.0.1`, ta konfigurationen ur valvet utan att skriva den till disk, säga till genom
  `IExitTunnel.Stopped` när tunneln faller, och skickas med Chainsaw i stället för att hämtas. Tills dess misslyckas
  varje anslutning ärligt med *TunnelUnavailable*. Välj implementation och granska licensen.
- **Tunnelns port kan tas över** (granskningen etapp 4, I): SOCKS5-porten på `127.0.0.1` kräver nu ett slumpat
  användarnamn och lösenord per tunnel, så att andra program inte kan använda den. Men om tunnelns process dör och ett
  annat program hinner lyssna på samma port innan `Stopped` slår till, kan Chainsaws nästa uppslag gå till fel proxy.
  Bäst är att köra tunneln i Chainsaws egen process och låta `ConnectCallback` ge en ström direkt ur tunneln i stället
  för en lokal port. Bestäms när tunneln byggs.
- **Svarshuvuden som ursprung:** `ExitResponse` har bara status, medietyp och den råa kroppen. Behöver Chains huvuden
  som `Date` eller `ETag` som en del av ursprunget läggs de till då (granskningen etapp 4, M).
- **Certifikatkedjor som saknar en mellanliggande nivå:** för att operativsystemet inte ska hämta något runt utgången
  (AIA och spärrlistor) hämtas inga certifikat och spärrar kontrolleras inte. En källa som skickar en ofullständig kedja
  går då inte att nå. Spärrkontroll genom utgången kan byggas senare om det behövs (granskningen etapp 4, C).
- **Tunnelns namnuppslag:** tunnelns proxy ska slå upp namn inne i tunneln (med WireGuard-filens `DNS =`), aldrig
  med datorns resolver, och vägra ansluta till en adress på datorn eller ett privat nät också när ett namn pekar dit
  (kontraktet i `IExitTunnel` säger det). Prova det med den riktiga tunneln.
- **Rötter som läggs till medan Chainsaw är igång:** de betrodda rötterna läses när en utgång ansluts. En rot som
  Windows lägger till senare gäller från nästa anslutning.
- **Kontrollen av `DNS =`** godtar raden också under `[Peer]` och ett värde som inte är en adress. Kan skärpas till
  `[Interface]` och en giltig IP-adress när tunneln byggs (granskningen etapp 4, M).
- **Rötternas begränsningar på Windows:** med Chainsaws egen rotlista följs inte Windows egna begränsningar för en rot
  (användningsområden, *distrust after*). Prova på Windows (TESTER-INFOR) och bestäm om de ska läsas in.
- **Stoppen när programmet avslutas:** `Dispose` väntar (högst fem sekunder) på tunnelstoppen som finns när den
  börjar, men inte på ett anslutningsförsök som lämnar över en tunnel efteråt (den stoppas ändå direkt), och det finns
  ett litet fönster mellan att den anslutna utgången tas och att dess stopp registreras. Viktigt när tunneln är en
  egen process (granskningen etapp 4, omgång 5, M).
- **Gränssnittet för utgångar:** lägga till en WireGuard-fil, välja utgång, ansluta och koppla ner, och läget i
  titelraden (skissas först).
- **Bevakningsprocessen och utgången:** bakgrundsprocessen (CLAUDE.md, *Bevaka*) ska gå genom Chainsaws utgång utan
  valvet. Hur den får konfigurationen (DPAPI för Windows-användaren) bestäms med bevakningen.
- **Kontroll av utgången:** fråga en källa vilken offentlig adress uppslagen kommer ifrån, så att användaren ser att
  utgången verkligen används (till exempel när den ansluts).

### Ärenden
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

### Övrigt
- **Länken `chainsaw:`** registrerad för användaren, ett exemplar av Chainsaw som tar emot länkar, och vänta på upplåsning.
- **Guiden:** avsnitten skrivs medan funktionerna byggs i `docs/guide/` (UTXO, adresstyper, common-input-ownership,
  växeladress, CoinJoin och peel chain finns; kvar är spårningsmetoderna och varför ett spår tar slut).
- **Testdata:** `/testdata` med påhittade ärenden och syntetiska tx-mönster, bara i Debug.
- **Låsskärmens kätting:** formen är godkänd (CLAUDE.md). Väntar på användarens val mellan ID7a (rak) och ID7b (på snedden) innan låsskärmen byggs.
