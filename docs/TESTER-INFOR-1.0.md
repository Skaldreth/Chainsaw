# Att prova på Windows före 1.0

Det som är byggt men inte kan provas i molnmiljön (Linux), per område: vad man gör och vad som ska hända.

## Start

- `dotnet run --project src/Chainsaw.App`: fönstret öppnas mitt på skärmen, mörkt, med egen titelrad och utan fel.
- Utan valv (`%LOCALAPPDATA%\Chainsaw\vault.json` saknas) öppnas förstagångsinställningen; med valv öppnas låsskärmen.
  Skalet (ikonlisten, kommandoraden) syns aldrig förrän valvet är upplåst.

## Skalet (etapp 1, SH1b)

- Programikonen (kättingen) syns i Aktivitetsfältet, i Alt+Tab och på Chainsaw.exe i Utforskaren, skarp i alla storlekar.
- Titelraden: kättingloggan (två länkar i varandra, diagonalt) är skarp i 16 px och mellanrummen mellan länkarna syns, *Chainsaw*, *Inget ärende öppet* och *Ingen utgång* med grå prick.
- Fönstret går att dra i titelraden, dubbelklick maximerar, Windows fäster det mot kanterna, och kanterna ändrar storlek.
- Maximerat går fönstret inte utanför skärmen; minimera, maximera och stäng fungerar, stäng blir röd vid hovring.
- Ikonlisten: elva ikoner, skarpa i 20 px, Nytt överst med en linje under, Terminal, Guide och Inställningar längst ner.
- Hovring visar namnet och kortkommandot till höger om ikonen, och ikonen blir ljusare på grå bakgrund.
- Klick på en ikon visar vyn: huvudet får vyns namn, sidan vyns ikon, namnet och *Inte byggt än*, och ikonen markeras.
- Ctrl+N, Ctrl+1 till Ctrl+7, Ctrl+0, F1 och Ctrl+, öppnar rätt vy.
- Ctrl+K och Ctrl+F lägger markören i kommandoraden längst ner; ramen blir magenta.
- Text i kommandoraden och Enter: *Kommandon och sökning är inte byggda än* visas ovanför raden; Esc tömmer allt.
- Kommandoradens text är skarp (skuggan får inte göra den suddig).

## Valvet (etapp 2, Security)

Valvet har inget gränssnitt än; det provas när förstagångsinställningen och låsskärmen byggs.

- Valvet hamnar i `%LOCALAPPDATA%\Chainsaw\vault.json` och innehåller bara format, version, salt, iterationer och
  krypterade fält (Base64), inga namn eller nycklar i klartext.
- Att låsa upp tar under ungefär en sekund på en vanlig arbetsstation (PBKDF2-SHA512, 600 000 iterationer), och
  gränssnittet fryser inte under tiden.
- Ingen `vault.json.tmp` ligger kvar efter att valvet sparats.

## Ärendefilen (etapp 3, Cases)

Ärendena har inget gränssnitt än; det provas när *Nytt ärende* och öppna ärende byggs.

- Ett nytt ärende hamnar i `%LOCALAPPDATA%\Chainsaw\Ärenden\<ärendenummer>.cut`. Medan det är öppet ligger
  `<ärendenummer>.cut.work` (krypterad arbetskopia), `.cut.work.base` och `.cut.lock` bredvid; alla försvinner när
  ärendet stängs.
- Ankarna hamnar i `%LOCALAPPDATA%\Chainsaw\Ankare\`, en fil per ärende med ett namn som inte visar ärendet.
- Filen börjar med `CHSWCASE` och ett kort JSON-huvud med ärendets id och kuverten (Base64). Inget annat i filen går att
  läsa: inte ärendenumret, operatörens namn eller ordet `SQLite`.
- Ett ärende går inte att öppna i två Chainsaw samtidigt (*Används redan*).
- SQLite3 Multiple Ciphers (`SQLite3MC.PCLRaw.bundle`) laddar sitt inbyggda bibliotek för win-x64 utan fel.
- En `.cut` som kopieras till en annan dator och öppnas med delningslösenordet går att läsa där.
- Låsning av valvet (Ctrl+L, Windows låses) medan ett ärende är öppet: låsskärmen kommer direkt, och ärendet sparas
  och stängs i bakgrunden.
- Antivirus som låser `.cut` medan den sparas: sparningen misslyckas utan att filen förstörs, och
  arbetskopian tas tillbaka nästa gång ärendet öppnas.

## Utgångarna (etapp 4, Network)

Utgångarna har inget gränssnitt för att ansluta än; tunneln själv provas för sig (*WireGuard-tunneln*, etapp 13).

- `vault.json` innehåller inget om utgångarna i klartext: inte namnet, platsen, id:t eller WireGuard-filens text.
- Att ansluta en utgång utan `chainsaw-tunnel.exe` bredvid programmet ger *misslyckades* (tunneln finns inte), och ett uppslag ger *Ingen utgång är
  ansluten* utan att något går ut på nätet (kontrollera med Resursövervakaren eller Wireshark att Chainsaw inte öppnar
  någon anslutning).
- När tunneln finns: med en ansluten utgång går uppslaget bara till tunnelns SOCKS5-port på `127.0.0.1`, och datorn
  gör ingen DNS-fråga om källans namn (Wireshark på det vanliga nätkortet visar ingenting från Chainsaw).
- En proxy i Windows inställningar eller i `HTTPS_PROXY` påverkar inte Chainsaws uppslag.
- Certifikatkedjor byggs utan nedladdningar och bara mot de rötter som redan finns i Windows certifikatarkiv. Kontrollera
  med Wireshark att uppslag mot en källa vars rot saknas lokalt inte får Windows att hämta roten från Windows Update
  (`ctldl.windowsupdate.com`) och inte hämtar AIA eller spärrlistor; uppslaget ska i stället misslyckas.
- En utgång vars WireGuard-fil saknar `DNS =` med en IP-adress under `[Interface]` vägras när den läggs till.
- Rötterna på Windows: på en ny användarprofil (få rötter installerade) går uppslag mot vanliga källor att göra? Syns
  rötterna under *Tredjeparts rotcertifikatutfärdare* (AuthRoot) i det Chainsaw läser? Följs Windows egna
  begränsningar för en rot (spärrade certifikat i *Ej betrodda certifikat*, begränsade användningsområden,
  *distrust after*-datum)? Chainsaw tar bort rötter som ligger i *Ej betrodda certifikat*, men inte de andra.


## WireGuard-tunneln (etapp 13)

Hjälpprogrammet `chainsaw-tunnel.exe` ligger bredvid `Chainsaw.exe` (och licenserna i `licenses\chainsaw-tunnel\`).
Appen har inget gränssnitt för att ansluta än, så programmet provas för sig i PowerShell med en riktig WireGuard-fil
från utgångens leverantör (filen ligger redan på disken; Chainsaw själv skriver den aldrig dit):

```powershell
cd <Chainsaws mapp>
$line = @{ configuration = (Get-Content -Raw .\utgang.conf); username = 'u1'; password = 'p1' } | ConvertTo-Json -Compress
$psi = New-Object System.Diagnostics.ProcessStartInfo '.\chainsaw-tunnel.exe'
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true
$p = [System.Diagnostics.Process]::Start($psi)
$p.StandardInput.WriteLine($line); $p.StandardInput.Flush()
$p.StandardOutput.ReadLine()          # {"port":NNNNN} inom 15 sekunder, eller {"error":"..."}
curl.exe --socks5-hostname u1:p1@127.0.0.1:NNNNN https://api.ipify.org   # utgångens adress, inte datorns
$p.StandardInput.Close()              # programmet ska avslutas inom någon sekund
```

- Första raden är `{"port":…}` när servern har svarat. Med fel nyckel, en server som inte svarar eller utan nät:
  `{"error":"handshake"}` eller `{"error":"endpoint"}` inom ungefär 15 sekunder, och programmet avslutas.
- `curl` visar utgångens offentliga adress. Wireshark på det vanliga nätkortet visar bara UDP till serverns port (och en
  DNS-fråga om serverns eget namn om `Endpoint` är ett namn), ingen DNS-fråga om `api.ipify.org` och ingen TCP.
- Fel lösenord (`u1:fel@…`) och ingen inloggning (`--socks5-hostname 127.0.0.1:NNNNN`) vägras.
- `curl.exe --socks5-hostname u1:p1@127.0.0.1:NNNNN https://127.0.0.1/`, `https://192.168.1.1/` och ett namn som pekar
  på `127.0.0.1` (till exempel `https://localtest.me/`) vägras utan att något går ut.
- `netstat -ano | findstr NNNNN` visar att porten bara lyssnar på `127.0.0.1`.
- Programmet har inget fönster, skriver ingenting på stderr och skapar inga filer (Process Monitor).
- Avsluta PowerShell-fönstret i stället för att stänga stdin: programmet ska avslutas av sig självt.
- Dra ur nätkabeln medan tunneln är uppe: inom ungefär fem minuter skriver programmet
  `{"stopped":"lost"}` och avslutas.
- Byt ut `chainsaw-tunnel.exe` mot en annan fil med samma namn: Chainsaw vägrar starta den (*tunneln finns inte*) när
  anslutningen byggs i appen. Prova också att filen inte kan skrivas över medan Chainsaw startar den.
- Frågar Windows-brandväggen om något första gången tunneln startas (programmet skickar UDP till servern)?
- Stoppas ett osignerat `chainsaw-tunnel.exe` av Defender, SmartScreen, AppLocker eller WDAC på en tjänstedator?
- Låt datorn sova i mer än fyra minuter med tunneln uppe: efter uppvaknandet ska tunneln antingen fortsätta (ny
  handskakning) eller skriva `{"stopped":"lost"}`, aldrig hänga.
- När utgångarnas gränssnitt finns: anslut, gör ett uppslag, och kontrollera att `chainsaw-tunnel.exe` försvinner ur
  Aktivitetshanteraren när utgången kopplas ner, när Chainsaw stängs och när Chainsaw avslutas i Aktivitetshanteraren.

## Bitcoin-källan (etapp 5, Chains)

Chains har inget gränssnitt än. Det provas när kommandoraden eller grafen kan slå upp en adress, och först med en
ansluten utgång.

- Ett uppslag mot mempool.space och ett mot blockstream.info (bas-URL i inställningarna) ger samma tx med samma
  belopp, avgift, blockhöjd och blockhash som webbplatsernas egna sidor.
- En adress med många tx bläddras sida för sida tills historiken tar slut, och antalet stämmer med webbplatsen.
- En obekräftad tx visas som obekräftad, och efter att den bekräftats visar nästa uppslag blocket.
- Utan utgång: ett ärende med hämtade tx öppnas och visar dem, och det som inte är hämtat säger *Ingen utgång är
  ansluten* utan att något går ut på nätet.
- `/verify` på ett ärende med hämtade svar är grönt, och svarens SHA-256 stämmer med `sha256sum` av samma svar hämtat
  med `curl` genom samma utgång (så länge svaret inte har ändrats).

## Kurserna (etapp 14, Pricing)

Pricing har inget gränssnitt än. Det provas när beloppen i SEK visas, och först med en ansluten utgång.

- Kursen för en känd tx stämmer med Bitstamps egen graf för samma timme (sista priset) och med Riksbankens kurs för
  samma dag på riksbank.se.
- En tx på en lördag får fredagens kurs från Riksbanken, och stegen säger det.
- Samma kurs hämtas inte två gånger: två uppslag för samma timme ger bara ett nytt svar i ärendet.
- Utan utgång: en kurs som redan är hämtad visas ur ärendet, och en ny säger *Offline* utan att något går ut.
- Många tx på en gång (en graf med tx över flera månader) ger inga fel 429 från Riksbanken.

## Klustringen (etapp 6, Clustering)

Klustringen har inget gränssnitt än. Det provas när grafen kan hämta tx och visa kluster, helst tillsammans med
kryptospåraren.

- En känd Whirlpool-mix, en Wasabi 2-runda och en JoinMarket-tx känns igen som CoinJoin, spåret tar slut där, och
  ingen av deras inputs slås ihop.
- En börs sweep slår ihop insättningsadresserna, men kunderna som betalade in hamnar inte i börsens kluster.
- En batchutbetalning från en börs gör ingen kund till en del av börsen.
- En känd peel chain känns igen när minst tre hopp är hämtade, och bara de stora outputarna slås ihop.
- *Varför?* på varje adress i ett kluster visar heuristik, version och tx, och tx:en går att slå upp i en annan
  blockutforskare.
- En uppbrytning står kvar efter att ärendet stängts och öppnats, och efter att samma tx hämtats och analyserats igen.
- Jämför några kluster med kryptospårarens verktyg: slår Chainsaw ihop för mycket eller för lite? Skriv ner varje
  skillnad, så att tröskelvärdena kan prövas (TODO, *Väntar på beslut*).
- `/verify` är grönt efter klustring och uppbrytningar.

## Spårningen (etapp 7, Tracing)

Spårningen har inget gränssnitt än. Det provas när grafen (GR4) är byggd, helst tillsammans med kryptospåraren och ett
ärende där svaret redan är känt.

- Startpunkten (en tx eller en adress) står i första kolumnen, och varje expansion lägger till ett hopp; inget annat
  växer i grafen.
- Att öppna ett ärende och visa grafen skickar ingenting ut på nätet (kontrollera utgångens logg). Ett mynt som låg
  kvar visas som *kan ha flyttats* tills noden expanderas igen.
- FIFO och haircut ger samma belopp som kryptospårarens verktyg på samma spår, eller så går skillnaden att förklara
  (avgifter, ordning av inputs, avrundning). Skriv ner varje skillnad.
- Summan av alla slut är exakt det spårade beloppet, på satoshin, med avgifterna som eget slut.
- Spåret slutar vid en känd CoinJoin med orsaken CoinJoin, och linjen in i mixen visar beloppet.
- En adress utan expansion visas som *Inte expanderad ännu*, aldrig som ett slut.
- En länk till en tx på Ethereum eller TRON visas som en lila nod och står kvar efter att ärendet stängts och
  öppnats; tas den tillbaka står den kvar förseglad, och `/verify` är grönt.
- Nodernas platser i fri yta och i hoppkolumner står kvar efter att ärendet stängts och öppnats.


## Etiketterna (etapp 8, Attribution)

Etiketterna har inget gränssnitt än. Det provas när grafen och vyn Etiketter är byggda.

- Importera OFAC:s riktiga SDN-lista (`sdn.xml`, sparad från OFAC:s webbplats): importen går på några sekunder,
  antalet adresser per valuta stämmer ungefär med listan, och filen ligger orörd i
  `%LOCALAPPDATA%\Chainsaw\Sanktionslistor` med sin SHA-256 i namnet. Jämför hashen med `Get-FileHash`.
- En känd sanktionerad adress (ur listan) som expanderas i ett ärende märks *Sanktionerad*, och *varför?* visar listans
  datum, hash, postens uid och program.
- En egen etikett *Börs* på en adress gör att spåret slutar där med *Börs, uttag funnet*, och *varför?* säger att det är
  en bedömning med vem, när och varför.
- Två ärenden: en etikett i det första syns i det andra som *från ett annat ärende* när samma adress finns där, utan
  ärendenummer, anteckning eller skäl. En etikett märkt *Sprid inte* och en etikett på ett offer syns inte.
- En kollegas ärendefil (öppnad med delningslösenordet) ger sina etiketter till biblioteket, och de syns sedan i egna
  ärenden med samma adresser.
- `%LOCALAPPDATA%\Chainsaw\Etiketter\bibliotek.bin` går inte att läsa i en texteditor (krypterad).
- `/verify` är grönt efter etiketter, tillbakatagningar och etiketter från andra ärenden.

## Återställningsnyckeln (etapp 9)

Verktyget `tools/Chainsaw.Recovery` körs på Windows; appen har inget gränssnitt för nyckeln än. Se
`docs/ATERSTALLNINGSNYCKEL.md`.

- `keygen` till ett USB-minne: lösenordet syns inte när det skrivs, båda filerna skapas, och ett nytt `keygen` till
  samma mapp vägrar. `keygen` till en mapp i repot vägrar.
- Den privata nyckelns fil går att läsa med `openssl pkey -in chainsaw-recovery.private.pem` och lösenordet (samma
  format som andra verktyg), och vem som får läsa filen följer mappens rättigheter (på Windows ärvs de; på ett
  USB-minne med FAT finns inga).
- `fingerprint` utan fil säger ärligt att ingen nyckel är inbyggd. När den publika nyckeln är inklistrad i
  `organization-recovery-key.pem` och appen byggd visar `fingerprint` samma fingeravtryck som `keygen`.
- Ett ärende som skapas efter det har kuvertet (`check` säger *Ja*); ett äldre ärende får det när det öppnas.
- `open --share` på ett ärende som är öppet i Chainsaw vägrar (*öppet i Chainsaw*). På ett stängt ärende: operatören
  öppnar det med det nya delningslösenordet, och journalen visar administratörens namn och nyckelns fingeravtryck.
- `open --vault %LOCALAPPDATA%\Chainsaw` på operatörens dator, efter ett nytt valv: operatören öppnar sedan ärendet
  med sin nya huvudnyckel.
- Inmatning med åäö i lösenorden (konsolens teckentabell) fungerar i både `keygen` och `open`.

## Låsskärmen och förstagångsinställningen (etapp 10, ID8c och FS1a)

Jämför med skisserna ID8c och FS1a på canvasen, gärna i 1440 × 900 och i ett mindre fönster.

**Kättingen som blir DNA (ID8c)**
- Kättingen kommer in på snedden uppifrån i magenta, med loggans skarv och en rak bit med hål mellan skarvarna, och
  står still. Ungefär mitt på diagonalen släpper länkarna: den sista länkens två stänger fortsätter utan skarv eller
  glipa som spiralens två strängar, lika breda som stängerna.
- Spiralen vrider sig, vidgas mot nederkanten och går ut genom nedre vänstra hörnet. Strängen bakom är mörkare, och
  basparen är streck mellan strängarna, ljusa vid den ena och mörkare vid den andra. Inga tunna streck över länkarnas
  hål och inga mörka skarvar mellan skarven och stängerna.
- Inget teckenfält bakom; sidan är slät.
- Partiklarna: små tecken (`. : · + *`) och ibland en bas (A, T, G, C) lossnar från spiralens strängar, driver sakta
  utåt och tonar in och ut. Kättingen släpper inga.
- I ett smalare fönster flyttar kättingen till vänster och krockar inte med formuläret.
- Stäng av animeringar i Windows (Inställningar, Hjälpmedel, Visuella effekter, *Animeringseffekter* av): partiklarna
  står still.
- Minimera fönstret och lås upp: Chainsaw använder ingen märkbar processor (Aktivitetshanteraren) medan figuren inte syns.
- Figuren och partiklarna är skarpa på en skärm med 150 % skalning, och när fönstret flyttas mellan skärmar.
- Partiklarna hackar inte och gränssnittet känns inte trögt medan de rör sig (skriv i fältet för huvudnyckeln).

**Låsskärmen**
- Titelraden har bara loggan, *Chainsaw* och fönsterknapparna; fönstret går att dra, maximera och stänga.
- Markören står i fältet för huvudnyckeln. Fel nyckel och Enter: *Fel huvudnyckel. Valvet öppnades inte.*, fältet töms
  och markören står kvar. Rätt nyckel: skalet visas efter ungefär en sekund, utan att fönstret fryser (knappen säger
  *Låser upp …* under tiden).
- Raderna under formuläret: utgången (*Utgången ansluts efter upplåsning …*) och *Låses automatiskt efter 15 minuter
  utan aktivitet.*
- *Om Chainsaw · <commit>* i hörnet, där commit är samma som `git log -1`; klick visar version och commit.

**Förstagångsinställningen**
- Strecken överst fylls steg för steg, och *Steg n av 4* står under dem. Tillbaka och Nästa går mellan stegen, Enter i
  ett fält går vidare.
- Välkommen utan namn går inte vidare. Huvudnyckel med färre än 12 tecken, eller två olika, ger ett fel och töms.
- Efter Huvudnyckel finns `vault.json`, och operatörsprofilen är sparad (syns i journalen när ett ärende skapas).
- Tillbaka till Huvudnyckel efter att valvet är skapat säger *Huvudnyckeln är vald och valvet är skapat.* och har inga
  fält.
- Utgång: *Lägg till en WireGuard-fil* öppnar Windows filväljare. En fil med `DNS =` sparas (inget om den i klartext i
  `vault.json`) och nästa steg säger att utgången är sparad; en fil utan `DNS =` ger ett fel. *Senare* går vidare.
- Första ärendet: ett ärendenummer skapar `%LOCALAPPDATA%\Chainsaw\Ärenden\<nummer>.cut` och titelraden visar numret;
  ett nummer med `/` vägras; *Hoppa över* går till skalet utan ärende.

**Låsning**
- Ctrl+L i skalet: låsskärmen visas direkt, kommandoradens text är borta efter upplåsning, och ett öppet ärende är
  stängt (titelraden säger *Inget ärende öppet*).
- Lås Windows (Win+L) och logga in igen: Chainsaw visar låsskärmen. Samma efter viloläge.
- Låt Chainsaw stå orört i 15 minuter: låsskärmen visas. Ändra `AutoLockMinutes` i `%LOCALAPPDATA%\Chainsaw\settings.json`
  till 1, starta om, och kontrollera att låsningen kommer efter en minut och att raden på låsskärmen säger *1 minut*.
- Medan låst: Ctrl+1 till Ctrl+7, Ctrl+N, Ctrl+K och Ctrl+F gör ingenting, Tab når inget i skalet, och Alt+Tab och
  Aktivitetsfältets förhandsbild visar bara låsskärmen.
- Skärmläsaren (Berättaren) läser bara låsskärmen när Chainsaw är låst.
- Huvudnyckeln går inte att hitta i en minnesdump av Chainsaw efter upplåsning (Aktivitetshanteraren, *Skapa
  dumpfil*, sök efter nyckeln som UTF-16 med till exempel `strings -el`). Gör det med en nyckel som bara används för
  provet.

## Runda hörn och pillret (2026-11-07)

- Fönstret har runda hörn och en tunn grå kant i Windows 11, som Gawk. Maximerat är hörnen raka.
- Vyernas ikoner ligger i ett flytande piller vid vänsterkanten, centrerat i höjd, med skugga. Knapparna är runda,
  den valda vyn har grå bakgrund, namnet och kortkommandot visas på hovring, och pricken sitter i knappens hörn.
- Sidan har ingen rubrik under titelraden; vyn börjar direkt under den, och pillret får plats i fönstrets minsta höjd (600 px).

## Ärenden i appen (etapp 11, NC1a)

**Nytt ärende**
- Ctrl+N, *Nytt → Ärende* i pillret och `/new` öppnar panelen från höger; resten av fönstret mörkas. Esc, Avbryt, × och
  ett klick på det mörka stänger den, och nästa gång är fälten tomma.
- `/new 2026-0117` fyller i ärendenumret. Ctrl+Enter skapar.
- Startpunkter, en per rad: under fältet står `BTC` och *Bitcoin-adress* eller *Bitcoin-tx* för varje rad, en
  TRON-adress (börjar med `T`) står tonad med *TRON-adress · inte byggt än*, och en rad som inte känns igen står i rött.
  En rad som inte känns igen hindrar att ärendet skapas och felet säger vilken rad.
- Ett ärende med titel och startpunkter: titelraden visar *nummer · titel*, grafen visas, och svaret ovanför
  kommandoraden säger att ärendet skapades. Journalen har *Titeln sattes till …* och en rad per startpunkt, och en
  TRON-startpunkt står som *Startpunkt på TRON noterad, men TRON är inte byggt än* (`app.start-point-noted`).
- *Kan öppnas av en kollega*: reglaget visar två lösenordsfält. Kortare än 12 tecken eller två olika ger ett fel och
  båda fälten töms. Ett delat ärende kan öppnas av en kollega med lösenordet (det finns inget gränssnitt för att öppna
  med lösenord än; prova med verktyget eller vänta på det).
- Slå av reglaget, stäng panelen eller lås med Ctrl+L: lösenordsfälten är tomma när de visas igen.
- Ärendelösenordet går inte att hitta i en minnesdump efter att ärendet skapats (som för huvudnyckeln).
- Ett ärendenummer som finns ger *Det finns redan ett ärende med det numret.* och fälten står kvar.

**Kommandoraden**
- `/help` listar kommandona. `/hjälp` ger *Okänt kommando*. Fel står i rött ovanför raden; ett lyckat kommando tömmer
  raden, ett misslyckat lämnar texten kvar.
- `/op` och Tab blir `/open`. `/open ` visar *Tab: <ärendet du arbetade med senast>*; Tab fyller i det och Tab igen går
  till nästa. Markören står sist.
- `/open <nummer>` sparar och stänger det öppna ärendet och öppnar det andra; titelraden följer med. Samma ärende igen
  säger att det redan är öppet. Ett nummer som inte finns ger ett kort fel.
- `/close` sparar och stänger; titelraden säger *Inget ärende öppet*. Kontrollera att `.cut`-filens ändringstid är ny.
- `/verify` på ett orört ärende: *Inget har ändrats: n kedjor och m poster stämmer.* Utan ärende: *Inget ärende är
  öppet.* (Ett brutet ärende provas i testerna, eftersom filen är krypterad.)
- `/lock` låser som Ctrl+L.
- En Bitcoin-adress eller tx utan `/`: med ett ärende öppet blir den en startpunkt (*Startpunkten … lades till*, och
  *redan en startpunkt* andra gången); utan ärende öppnas Nytt ärende med den ifylld. En TRON-adress ger *TRON är inte
  byggt än*. Annan text ger *Sökning är inte byggd än*.
- Stäng Chainsaw med ett ärende öppet (krysset): `.cut` är sparad och arbetskopian (`.work`) är borta.

**Titelraden och statusraden**
- Utan utgång: grå prick och *Ingen utgång* i titelraden, *Offline: ingen utgång ansluten …* i statusraden. Tunneln är
  inte byggd, så *ansluten* går inte att prova än.

**Länken `chainsaw:`**
- Länken är inte registrerad än. Prova med argumentet: `Chainsaw.exe chainsaw:bitcoin/<adress>`.
- Chainsaw stängt: Chainsaw startar på låsskärmen och visar inget om länken. Efter upplåsning öppnas Nytt ärende med
  startpunkten ifylld; inget ärende skapas förrän du klickar.
- Chainsaw igång och upplåst med ett ärende öppet: kör kommandot igen. Inget nytt fönster öppnas; Chainsaws fönster kommer
  fram och adressen står i kommandoraden med *Från en länk: Enter lägger den som startpunkt i …*. Inget läggs till
  förrän du trycker Enter.
- Chainsaw låst: kör kommandot. Låsskärmen visar inget om länken; efter upplåsning visas den som ovan.
- Under förstagångsinställningen: länken väntar tills inställningen är klar.
- `Chainsaw.exe chainsaw:bitcoin/hej`, `chainsaw:monero/…` och `chainsaw:bitcoin/abc?x=1` gör ingenting.
- Två Windows-användare på samma dator (eller en annan användare med `runas`): den andras Chainsaw får inte länkarna.

**Förstagångsinställningen**
- Första ärendet har nu *Titel* och *Startpunkt*, med raden om kedjan under startpunkten.

## Undermenyer i pillret (etapp 11b, PN1)

**Formen**
- Klicka på *Nytt*: knappens runda form växer till en flik förbi pillrets högra kant och går över i menyn, som hänger
  från flikens överkant. Under fliken möts de i ett rundat inre hörn. Flik och meny är en form med samma yta, en tunn
  kant runt hela och en skugga under, och kanten är skarp (inte suddig) i 100 % och 150 % skalning.
- Fliken ligger exakt över knappen (ikonen hoppar inte), och pillrets knapp syns inte igenom.
- Raderna: Startpunkt S, Etikett E, Bevakning B, Begäran R, linje, Importera sanktionslista … I, linje, Ärende Ctrl+N.
  Kortkommandona står till höger i grått.

**Tonat och ärligt**
- Utan ärende: Etikett, Bevakning och Begäran är tonade (grå text, vanlig pil, ingen markering på hovring), och
  hovring visar *Inte byggt än: …*. Startpunkt, Importera sanktionslista och Ärende går att välja.
- Med ett ärende öppet: samma rader är tonade, eftersom Etikett, Bevakning och Begäran inte är byggda.
- Ett klick på en tonad rad gör inget och menyn står kvar.

**Tangentbordet**
- Tab till *Nytt* och Enter (eller Space) fäller ut menyn; Startpunkt är markerad. ↓ och ↑ hoppar över linjer och
  tonade rader och går runt; Home och End går till första och sista. Enter väljer.
- Medan menyn är öppen väljer S, I direkt; E, B och R gör inget. Ctrl+N öppnar Nytt ärende och stänger menyn.
  S med ett öppet ärende skriver inte *s* i kommandoraden.
- Esc och ← stänger, och fokus står på *Nytt* igen (Enter öppnar igen). Tab, Shift+Tab och → lämnar inte menyn.
- Håll Enter nedtryckt på *Nytt*: menyn öppnas, men ingen rad väljs av att tangenten upprepas.
- Med Nytt ärende öppet: Tab går runt i panelen och når aldrig pillret bakom den.
- Ctrl+1 … Ctrl+7 stänger menyn och visar vyn. Ctrl+K stänger menyn och lägger markören i kommandoraden.
- Ctrl+L med menyn öppen: låsskärmen visas, och efter upplåsning är menyn stängd.

**Musen**
- Ett klick utanför menyn stänger den; ett klick på en annan knapp i pillret stänger menyn och visar den vyn.
- Ett klick på fliken stänger menyn. Byt till ett annat fönster (Alt+Tab): menyn är stängd när du kommer tillbaka,
  och fokus står på *Nytt* (Enter öppnar menyn igen).
- Med menyn öppen: maximera med Win+↑ eller ändra fönstrets storlek. Fliken följer med och ligger kvar över knappen.
- Ligg kvar i filväljaren längre än tiden för automatisk låsning: Chainsaw låses inte bakom den.

**Raderna**
- *Startpunkt* utan ärende öppnar Nytt ärende. Med ett ärende öppet står markören i kommandoraden och ovanför står
  *Skriv en adress eller en tx och tryck Enter, så blir den en startpunkt i …*; står det redan text i raden lämnas den.
- *Importera sanktionslista …* öppnar Windows filväljare (XML). Välj en sparad SDN-lista från OFAC: svaret ovanför
  raden säger *OFAC SDN publicerad … är importerad: n adresser, m för Bitcoin.* En annan XML-fil ger *Filen är inte en
  SDN-lista i XML.* Samma lista igen säger *… fanns redan: …*. Avbryt säger inget.
  Listan ligger i `%LOCALAPPDATA%\Chainsaw` (etapp 8). Tryck Ctrl+L … (eller lås Windows) medan filväljaren är öppen
  och välj en fil medan Chainsaw är låst: inget importeras.
- *Ärende* öppnar Nytt ärende.

## Grafen (etapp 12, GR4, GR3a och GR3c)

Förberedelse: ett ärende med en Bitcoin-adress som startpunkt och en ansluten utgång (utan tunnel: prova det som går
offline och se att det sägs ärligt). Grafen är förstavyn.

**Tomt och läsning**
- Utan ärende: mitt på sidan står *Inget ärende öppet. Ctrl+N skapar ett, och /open öppnar ett.*; ingen graf ritas.
- Ett ärende utan startpunkter: *Inga startpunkter än. Skriv en adress …*. Skriv en adress i kommandoraden och tryck
  Enter: grafen läses om och visar startpunkten som en rund grå nod (*Okänd*) med en ljus flagga uppe till vänster
  (etapp 16, GR5), blek eftersom den inte är expanderad,
  och överst till vänster *Inget att spåra än: … Välj startpunkten och expandera den.*
- Inget hackar medan grafen läses eller expanderas: pillret, kommandoraden och fönstret svarar hela tiden.

**Expandera**
- Klicka på startpunkten: ramen blir magenta, resten bleknar, och inspektören till höger visar namnet, *In*, *Ut*,
  *Spårat in*, *Sort*, *Hopp* och adressen i sin helhet i Cascadia Mono. Knappen *Expandera* (eller E med fokus i
  grafen) hämtar ett steg: nya noder och linjer kommer, och överst står *n nya tx i grafen.*
- Utan utgång: *Offline* står i grafens hörn; *Expandera* på något ärendet inte har säger *Inte expanderad. …* och
  *Offline: ingen utgång ansluten …*. Ingenting påhittat visas.
- Överst till vänster står *Spårat x BTC från n mynt · FIFO v1* när startpunktens mynt finns i grafen.

**Bildspråket**
- Noderna är tunna, 132 × 40, med en ikon i en rund (okänd, offer, CoinJoin, mynt) eller fyrkantig (tjänst, kluster)
  ram i sortens färg, namnet (förkortad adress, eller etikettens namn) och antalet adresser för ett kluster. Ett
  kluster har en andra ram bakom sig. Ikonerna är skarpa i alla zoomnivåer (linjen 1,5 px).
- Sätt etiketter (när det går, eller i ett ärende med etiketter): en börs blir blå och fyrkantig, en mixer röd med
  streckad ram, spel gult med två spelkort. Börs med spårade pengar får en blå bock i hörnet (*Uttag funnet*), en
  sanktionerad adress med annan etikett en röd markering, en etikett från ett annat ärende en grå etikettmarkering.
- Linjerna: spårade pengar i magenta och tjockare ju större belopp, andras pengar tunna och grå, en obekräftad tx
  streckad. Beloppet står mitt på linjen, *0,1570 av 0,2400* när bara en del är spårad.
- Knappen *Alla belopp* växlar till *Spårade belopp* (bara magenta linjer har belopp) och *Inga belopp*. Håll pekaren
  över en linje: beloppet visas ändå. Välj en nod: bara linjerna runt den har belopp.
- *?* nere till höger öppnar bildspråket: sorterna med ikon och färg, markeringarna och linjerna, och en länk till
  Guiden. Hovring över *?* visar grafens tangenter.

**Fri yta och hoppkolumner**
- Dra noder åt alla håll på den fria ytan. Växla till hoppkolumner (ikonen med fyra streck): noderna glider in i
  kolumner med namnen *Start*, *+1*, *+2* (och *−1* före startpunkten). Dra en nod: den går bara upp och ner.
  Pengar som går tillbaka ritas som en båge över noderna.
- Växla tillbaka: varje nod står där du lämnade den på den fria ytan. Växla igen: kolumnernas höjder är kvar.
- Stäng och öppna ärendet: platserna i båda lägena är kvar (de sparas i ärendet).
- Dra den tomma ytan: grafen flyttas. Hjulet zoomar runt pekaren. F passar in hela grafen, + och − zoomar, Esc väljer
  inget. En stor graf (hundratals noder) ritas och dras utan att det hackar.

**Metoden och Varför?**
- Knappen *FIFO* i hörnet: verktygstipset säger *Spårningsmetod: FIFO v1. Klicka för haircut. …*. Klicka: knappen säger
  *haircut*, beloppen räknas om och tjocklekarna ändras.
- Välj en linje: inspektören visar *Belopp*, *Spårat*, *Tid* (lokal tid och UTC), *Tx*, varje txid i sin helhet och
  *Block nnn · hash*. Kontrollera en txid och blockhash mot en blockutforskare.
- *Varför?* öppnar Guidens panel (etapp 18) med förklaringarna överst: för en linje att beloppet är summan av
  överföringar (fakta) och det spårade beloppet med metod, version och tx (en bedömning); för en nod etiketten och
  dess ursprung, klustrets heuristiker med version och tx, varför spåret tar slut.

**Efter granskningen**
- Ett klick på en nod eller linje väljer den (inspektören fylls); ett drag flyttar noden och sparas en gång.
- Växelns två ikoner och länken *Guiden: Grafen och bildspråket* i legenden är klickbara direkt när vyn visas (inte
  tonade).
- Håll pekaren över en nod: ett verktygstips med sort, markeringar och om den är expanderad; flytta till en annan
  nod: tipset byter innehåll.
- Expandera så att en ny nod kommer i en kolumn som redan har noder på den fria ytan: den hamnar under dem, aldrig ovanpå.
- Två noder i samma kolumn med en linje mellan sig: linjen går som en liten båge ut till höger, inte genom noderna.
- Expandera ett kluster med många adresser: högst tio uppslag och högst 100 nya tx per tryck, och svaret säger att
  gränsen nåddes.

## Datalagret och spårningen (etapp 15)

Förberedelse: ett ärende med en Bitcoin-adress som startpunkt och, när tunneln finns, en ansluten utgång.

**Grafen öppnas som den lämnades**
- Byt till *haircut*, växla till hoppkolumner, välj *Spårade belopp*, zooma och panorera. Stäng ärendet (`/close`) och
  öppna det igen (`/open`): metoden, läget, beloppen och det som syns på skärmen är som när du lämnade det. Grafen
  passas inte in på nytt; F gör det fortfarande.
- Öppna ett annat ärende: det har sitt eget läge (FIFO, fri yta och inpassad graf om du aldrig ändrat något där).
- `/verify` är grönt efter bytet av metod, och journalen har en rad *Spårningsmetod: haircut v1 i stället för FIFO v1*.
  Zoom, läge och belopp ger ingen rad i journalen.

**Det som hölls tillbaka**
- Expandera en adress med många tx (en börs): högst 50 tx kommer in, och överst står *Delvis expanderad: … Adressen
  har N tx; de nyaste 50 togs med och resten hölls tillbaka.* (eller *fler än 50* utan källans sammanfattning).
- Expandera en batchutbetalning (en tx med många outputs som spenderats vidare): högst 50 spenderande tx kommer in, de
  som tar emot mest först, och överst står hur många som hölls tillbaka.
- Grafen svarar hela tiden medan den läses och spåras; en stor graf hackar inte när metoden byts.

**Kontrollen mot kedjan** (kräver utgång)
- Expandera en tx. I ärendet ligger nu också tx:ens bytes (`bitcoin.tx-hex`); `/verify` är grönt.
- Inget syns ännu i gränssnittet om att tx:en är kontrollerad (väntar på skiss), men ett svar som inte stämmer med
  bytesen ska aldrig synas i grafen.

**Etikettbiblioteket**
- Sätt (när det går) en etikett i ärende A, stäng A, öppna ärende B med samma adress i grafen: etiketten kommer in i B
  som *från ett annat ärende*, och journalen i B har en rad om det.
- Lås Chainsaw (Ctrl+L) medan ett ärende är öppet och lås upp: grafen visas som förut, utan fel.

## Utgångarna (etapp 17, UT1a)

**Titelraden och statusraden**
- Utan ansluten utgång står det *Ingen utgång* med en grå prick och en liten pil i titelraden, och statusraden säger
  *Offline: ingen utgång ansluten. Det ärendet redan har visas, men inget nytt hämtas.* Med en ansluten utgång: grön
  prick och namnet, och statusraden *Uppslag går genom utgången …*.
- Hovring över utgången visar statusradens text. Fönstret går fortfarande att dra i titelraden runt knappen.

Rutan under utgången provas under *Utgångsrutan (UT2b)*.

## Utgångsrutan (UT2b)

**Rutan**
- Ett klick på utgången i titelraden fäller ut rutan (300 px, runda hörn, skugga) under den, med högerkanten mot
  knappens. Ett nytt klick, Esc eller ett klick någon annanstans under titelraden fäller in den. Med tangentbordet går
  fokus till den första strömknappen, eller till *Inställningar för anslutning* när det inte finns någon.
- Överst vägen som en stor bild: tre ringar på rad, *Datorn* (laptop), utgången i mitten (störst) och *Blockkedjor*
  (server), med etiketterna under. Sidoringarna är grå på upphöjd yta.
- VPN med en ansluten utgång: mittringen och globen är gröna med en mycket svag grön ton inuti, linjerna hela och
  gröna, och under mittringen står utgångens namn (till exempel *Stockholm*). Ingen rad *Ansluten* under.
- VPN utan ansluten utgång, också medan en ansluter: mittringen röd med röd glob, linjerna streckade och röda, och
  *Offline* under.
- Windows: mittringen visar en skärm i grönt, *Windows* under, linjerna hela och gröna, och ingen lista med utgångar.
- Med VPN: under en avdelare utgångarna i namnordning, rader på 48 px med en prick (grå, gul medan den ansluter, grön
  ansluten, röd vid fel), namnet och en rund strömknapp utan ram. Hovring över en röd rad visar orsaken (*Tunneln finns
  inte*, *Servern svarar inte* …). Strömknappen ansluter (*Anslut*) och är ifylld med *Koppla ner* när utgången är
  ansluten eller ansluter. Anslut en andra: den första kopplas ner. Gränssnittet fryser inte medan tunneln startar.
- Inga utgångar i valvet: en dämpad rad *Inga utgångar i valvet*.
- Rutan har ingen *Lägg till WireGuard-fil …* och inget steg med fil, namn och Spara.
- Längst ner under en avdelare *Inställningar för anslutning* med kugghjulet och *Ctrl+,* till höger; hovringen följer
  rutans runda hörn. Ett klick fäller in rutan och öppnar inställningarna på sidan Nätverk.

**Lägg till WireGuard-fil … (Inställningar › Nätverk, under VPN)**
- Öppnar Windows filväljare (*.conf*). Dialogen står kvar medan filväljaren är öppen. Avbryt ändrar inget.
- En riktig WireGuard-fil från VPN-tjänsten: filens namn och *Giltig* i grönt, namnet ifyllt ur filnamnet med fokus
  och markerat, *Krypteras i valvet*. Spara (eller Enter) lägger den i listan, inte vald, och en ansluten utgång står
  kvar. Avbryt eller Esc går tillbaka till listan.
- Prova filer som saknar något: utan `DNS =` (eller med ett namn i stället för en adress) står *Saknar DNS = med en
  IP-adress* i rött och Spara går inte att trycka; två `[Peer]` ger *Ska ha en [Peer]*. En bild eller en tom fil ger
  ett rött besked, ingen krasch.
- Ändra filen mellan valet och Spara så att den blir fel: Spara sparar inget och visar vad som saknas.
- Lås Chainsaw (Ctrl+L) under steget: efter upplåsning är dialogen stängd och filen glömd. En fil som
  väljs efter att Chainsaw låsts medan filväljaren var öppen används inte.
- Anslut den nya utgången: tunneln startar och vägen i utgångsrutan blir grön (kräver tunneln från etapp 13 och nät).

## Guiden (etapp 18, GD1b)

**Panelen bredvid grafen**
- Välj en nod och klicka *Varför?*: en panel (440 px) glider inte in utan står direkt till höger, över inspektören, med
  skugga mot grafen. Grafen mörkas inte och går att panorera, zooma och klicka i medan panelen är öppen.
- Överst en ruta med magenta kant till vänster: *Varför? · nodens namn* och alla förklaringar. En förklaring som hör till
  ett annat avsnitt har en länk under sig med avsnittets titel.
- Under rutan avsnittet: titeln, och den rubrik som svarar har en magenta linje till vänster och står överst i den
  synliga ytan. Prova: ett kluster öppnar *Common-input-ownership* vid *Idén* (när klustrets länkar kommit), en börs
  eller en adress med etikett *Etiketter* vid *En etikett är alltid en bedömning*, en mixer *Varför ett spår tar slut*
  vid *Riktiga slut*, en startpunkt utan annat *Startpunkter*.
- Välj en annan nod med panelen öppen: panelen följer med och visar den nya nodens svar. Pilen uppe till vänster går
  tillbaka till den förra. Pilen är tonad när det inte finns något att gå tillbaka till.
- Klicka på en länk i texten (magenta, understruken på hovring), till exempel *Adresstyper* i *Startpunkter*: avsnittet
  byts, och pilen går tillbaka.
- Längst ner *Läs också* med avsnitten texten länkar till.
- × stänger, och Esc stänger var tangentbordet än är (utom i kommandoraden och i Nytt ärende). Ett andra Esc i grafen
  väljer inget, som förut.
- Högerklicka på *FIFO* i grafens hörn: panelen öppnas vid *FIFO (först in, först ut)* med det spårade överst. Ett
  vanligt klick byter fortfarande metod. Efter byte till haircut: högerklick öppnar vid *Haircut (andel)*.
- *?* nere till höger visar bildspråket; länken *Guiden: Grafen och bildspråket* stänger bildspråket och öppnar
  panelen vid avsnittets början. Med panelen redan öppen blir det ett steg, och pilen går tillbaka.
- Byt vy i pillret: panelen stängs. Öppna eller stäng ett ärende: panelen stängs.
- Svarsrutan står kvar överst medan avsnittet under den rullar till den markerade rubriken; ett långt svar (ett
  kluster med många länkar) rullar för sig i rutan.
- Med panelen öppen flyttar grafens hörn (växeln, metoden, beloppen) åt vänster så att det syns bredvid panelen, och
  kommandoraden centreras i ytan bredvid. Prova också vid fönstrets minsta bredd (960 px).
- F1 med panelen öppen: som *Öppna i Guide*. Esc med utgångarnas ruta öppen stänger den rutan, inte Guiden.
- En länk i texten får en fokusmarkering med Tab.

**Texten**
- Fetstil i ljus text, kursiv, kod (adresser, `coinjoin v1`) i Cascadia Mono på en upphöjd yta, punktlistor med en
  liten prick, numrerade listor (*Kurser*), inskjutna listor (*CoinJoin*: Whirlpool, Wasabi, JoinMarket), citat med en
  linje till vänster (*UTXO-modellen*: summan av inputs …), kodblock (*Peel chain*, *Kurser*) som krymper i stället för
  att brytas om panelen är smal, och tabeller (*Adresstyper*, *Växeladress*) med kolumner som delar bredden.
- Inga tecken som `**`, `` ` `` eller `](` syns någonstans.

**Vyn Guide (F1)**
- F1 eller Guide i pillret: avsnitten i fyra grupper (Bitcoin, Klustring, Spårning, Etiketter) till vänster, det valda
  i markerad yta, och texten i läsbredd (högst 620 px) till höger. Kommandoraden täcker inte textens slut.
- *Öppna i Guide* i panelen: vyn Guide visas vid samma avsnitt och rubrik, med *Tillbaka till grafen* överst, som går
  tillbaka till grafen. Panelen är stängd.

## Startpunkten, namnen och inspektörens formulär (etapp 16, GR5 och IN1a)

Förberedelse: ett ärende med en Bitcoin-adress som startpunkt, expanderad några steg (kräver utgång, eller ett ärende
som redan har det), så att grafen har ett kluster och några okända adresser.

**Startpunkten och namnen (GR5)**
- Startpunkten har sin egen sort (rund grå om den är okänd, fyrkantig om den är ett kluster) och en liten ljus flagga
  uppe till vänster. Flaggan krockar aldrig med markeringarna uppe till höger (till exempel *Uttag funnet*). Pekaren
  över noden säger *… · Startpunkt*. Inspektören har *Startpunkt* med flaggan under rubriken. Legenden bakom *?* har
  flaggan först bland markeringarna.
- En nod utan namn visar adressen i ljusgrått, förkortad i mitten (`bc1qdlm…37yr`) bara så mycket som behövs. Zooma: på
  alla nivåer står början och de fyra sista tecknen kvar.
- En nod med namn (sätt en etikett med namn) visar namnet i vitt och adressen under i nedtonat grått, förkortad bara när
  den inte får plats. Båda raderna ryms i noden utan att klippas.

**Etikett (L)**
- Välj en okänd adress. Under fakta finns *Etikett L*, *Bryt upp U* (tonad, *Bara på ett kluster*) och *Annan kedja K*.
  Tryck L i grafen: formuläret fälls ut och markören står i *Namn*. Esc stänger det utan att något sparas; L igen eller
  knappen igen stänger också.
- Fyll i namn, välj en sort i ikonrutnätet (sortens namn står till höger om *Sort*), välj källa (*Öppen källa* ger fältet
  *Vilken källa*), skriv skäl och tryck Enter. Formuläret stängs, grafen ritas om och noden har sortens färg och form och
  namnet med adressen under. *Varför?* visar etiketten med ursprung och skäl (*Ur beställningen: …* för beställningen).
- *Sätt etikett* är tonad tills skälet är ifyllt (och källans namn vid öppen källa).
- Sätt etiketten *Offer* på startpunkten: den blir magenta och behåller flaggan.
- *Sprid inte*: sätt en etikett med *Sprid inte*, stäng ärendet och öppna ett annat med samma adress: etiketten kommer
  inte dit. Utan *Sprid inte* kommer den (*från ett annat ärende*).
- Utan operatörsprofil (om det går att prova): formuläret står kvar och säger *Det kunde inte sparas i ärendet:
  operatörsprofilen saknas …*.
- `/verify` är grönt efteråt, och journalen har en rad för etiketten.

**Bryt upp (U)**
- Välj ett kluster och tryck U: adresserna listas med heuristiken till höger (*CIO v1*; den första, som klustret lästes
  från, utan). Pekaren över en rad visar hela adressen och tx:en. Bocka för en eller flera: *n av m ut* och knappen *Bryt
  ut n* följer med. Alla kan inte bockas för (knappen tonas).
- Skriv skäl och tryck Enter: grafen ritas om, och de utbrutna adresserna är inte längre i klustret. *Varför?* på klustret
  säger att användaren har brutit upp sammanslagningar. Expandera igen: samma heuristik slår inte ihop dem på nytt.
- `/verify` är grönt, och journalen har en rad per uppbrytning.

**Annan kedja (K)**
- Välj en adress som tagit emot pengar (helst en swap) och tryck K: Ethereum är valt, Litecoin och Solana är tonade med
  *Inte byggt än*. Klistra in en Ethereum-tx (`0x` och 64 hex): en grön bock visas i fältet. Klistra in 64 hex utan
  `0x`: ingen bock (det kan vara båda kedjorna); välj TRON så kommer bocken och rubriken blir *TRON-tx*. Med TRON valt
  och en tx med `0x` byts kedjan till Ethereum av sig själv.
- Kom flera mynt in till adressen listas de (största först) och ett måste väljas.
- Skriv skäl och tryck Enter: en lila streckad linje går till en lila nod med kedjans namn under hörnet. Myntet erbjuds
  inte igen i formuläret.
- Fokus: efter Enter eller Esc går tangentbordet tillbaka till grafen, så att E, F, L, U och K fungerar direkt.
- Enter efter att ha klickat på en rad, en sort eller en källa sparar formuläret (klickar inte på raden igen).
- Sorten *Sanktionerad* finns inte i rutnätet (den kommer bara ur en importerad lista). Med sorten *Offer* är *Sprid
  inte* tonad: ett offer sprids aldrig.


## Inställningarna och Windows som utgång (etapp 19, ST1a)

**Dialogen**
- Ctrl+, och kugghjulet i pillret öppnar dialogen mitt i fönstret över den mörkade vyn, 1040 × 720 px (mindre i ett
  litet fönster), med menyn till vänster: *Den här datorn* (Nätverk, Låsning, Utseende, Länkar), *Arbetet* (Kurser),
  *Du* (Profil) och sist Om Chainsaw. Vyn bakom byter inte, och pillrets markerade ikon står kvar.
- Esc, × och ett klick på det mörka stänger den. Esc stänger först steget *Lägg till WireGuard-fil* eller formuläret
  för huvudnyckeln om något av dem är ute. Ctrl+K, Ctrl+1 … och Ctrl+L stänger den också (Ctrl+L låser).
- Med Nytt ärende öppet öppnas inte dialogen. Tab stannar inne i dialogen.
- Utseende och Länkar är tonade med *Inte byggt än* och går inte att ändra.

**Nätverk: Windows eller VPN**
- Två kort, *Windows* och *VPN*; det valda har magentafärgad ram. VPN är förvalt första gången.
- Under VPN står utgångarna (anslut och koppla ner med strömknappen) och *Lägg till WireGuard-fil …*, som bara finns
  här och inte i rutan i titelraden.
- Välj Windows: titelraden säger *Windows* med grön prick, statusraden *Uppslag går genom Windows*, och grafen säger inte
  *Offline*. En expansion i grafen hämtar genom datorns eget nät.
- Med Windows: kontrollera med Wireshark eller Resursövervakaren att uppslag går till källan, eller till proxyn om Windows
  har en (Inställningar → Nätverk → Proxy, också en PAC-fil), och bara med https. Inga kakor, inget Referer och inga
  hämtningar av AIA, spärrlistor eller rötter från Windows Update.
- Med Windows och en proxy som kräver inloggning: uppslaget misslyckas ärligt (Chainsaw skickar inte dina
  Windows-uppgifter till proxyn; se TODO).
- Med Windows och en proxy på datorn eller det lokala nätet (till exempel `10.x.x.x:8080` eller en PAC-fil som pekar
  dit): uppslagen går genom proxyn och lyckas. Tidigare misslyckades varje uppslag med *Uppslaget misslyckades på vägen
  genom utgången*, eftersom tunneln till proxyn togs för en källa som pekade in i det lokala nätet.
- Expandera `bc1qtm7ye0wm085hhv88japwkalmwkt3amec5z9c7a3unv05tfg6y2pstr3xfz` (en poolens adress med över 1 700 tx)
  med bara mempool påslagen: *Delvis expanderad: 50 nya tx* på ungefär 15 s, inte minuter. Varje tx är en coinbase med
  en OP_RETURN, som står som en nod utan adress: den kan inte expanderas (E gör inget), och den visas inte som *Inte
  expanderad ännu*. Med både mempool och Blockstream påslagna går det ungefär dubbelt så fort.
- Med Windows och en källas namn som pekar på datorn eller det lokala nätet (till exempel genom `hosts`): uppslaget
  vägras.
- Byt tillbaka till VPN medan en expansion pågår genom Windows: den stoppas, och utan ansluten utgång görs inga fler
  uppslag (titelraden *Ingen utgång*, statusraden *Offline*). Kontrollera med Wireshark att inget mer går ut på det
  vanliga nätkortet.
- Valet ligger kvar efter omstart (`%LOCALAPPDATA%\Chainsaw\settings.json`, `"ExitRoute": "Windows"`). Skriv ett okänt
  värde i filen för hand: Chainsaw startar med VPN.
- Rutan under utgången i titelraden i läget Windows: vägen dator → Windows → källor är grön, utgångarna är tonade och går
  inte att använda, och *Inställningar …* öppnar dialogen på Nätverk.
- Låsskärmen säger *Uppslag går genom Windows. Chainsaw slår inte upp något medan det är låst.* med Windows valt.
- En ansluten utgång står kvar när Windows väljs (den används inte) och används igen när VPN väljs.

**Låsning, Kurser, Profil och Om**
- *Lås efter* 5 min, 15 min, 30 min och 1 h: valet gäller direkt (räknat från senaste aktivitet), syns på låsskärmen och
  ligger kvar efter omstart.
- *När Windows låses* av: Win+L låser inte Chainsaw; viloläge och en frånkopplad session låser fortfarande.
- Kurser: valuta SEK/USD/EUR och upplösning Minut/Timme/Dag sparas och ligger kvar efter omstart.
- Profil: namn, titel och enhet läses när dialogen öppnas och sparas när ett fält lämnas eller med Enter. Ett tomt namn
  sparas inte och säger varför.
- *Byt …* fäller ut tre fält (nuvarande, ny, upprepa). Fel nuvarande nyckel, för kort ny nyckel och olika upprepning
  säger varför. Efter ett byte låser Ctrl+L och den nya nyckeln låser upp; den gamla gör det inte. Fälten är tomma när
  formuläret fälls in eller dialogen stängs.
- Om Chainsaw: version och commit, *Ingen inbyggd nyckel* (eller fingeravtrycket när en nyckel är inbyggd), och *Visa*
  fäller ut licenserna ur `THIRD_PARTY.md`.
- När `settings.json` inte går att skriva (till exempel skrivskyddad) säger dialogen det, och valet gäller tills
  Chainsaw stängs.

## Historik, ta tillbaka och ångra (etapp 21, AN1b)

Ett ärende med en startpunkt, ett kluster, en egen etikett, en uppbrytning och en länk till Ethereum (gör dem med L, U
och K först).

- Välj en nod: under rubriken står flikarna **Fakta** och **Historik · N**, med accentlinjen under den valda. En linje
  har inga flikar. L, U eller K när Historik är vald byter till Fakta och öppnar formuläret där.
- Historiken står i tidsordning på en tunn linje: *Först in* (block och på kedjan) tonad, *Klustrad* med streckad ram,
  etiketter med sortens ikon och färg, *Uppbruten · N ut* och *Länk till annan kedja* (lila, streckad). Tiden står kort
  till höger; hovra raden för lokal tid och UTC.
- *Ta tillbaka* på en egen etikett fäller ut **Skäl** (frivilligt), *Biblioteket får veta* och *Står kvar, överstruket*.
  Skälets fält har tangentbordet direkt; **Enter** sparar utan skäl, **Esc** avbryter (också när fokus står i grafen).
  Raden står kvar överstruken och tonad, och en ny rad *Tillbakatagen: …* med vem och skäl kommer efter. Etiketten
  försvinner ur grafen.
- Öppna ett annat ärende som fått etiketten ur biblioteket: kopian är tillbakatagen där (en rad i journalen som inte
  säger något om det första ärendet).
- En etikett från ett annat ärende, en sanktionsträff och *Klustrad* har ingen knapp.
- *Ångra* på en uppbrytning visar **Slås ihop igen** med adresserna (förkortade, hela på hovring), heuristiken på varje
  rad och i rubriken. *Ångra* går inte att trycka förrän listan är läst. Efter Enter slås adresserna ihop i grafen igen,
  och raden *Ångrad: uppbrytningen* kommer i historiken. Bryt ut två adresser med två olika länkar: de står som en rad
  och ångras tillsammans.
- Bryt upp två gånger så att två uppbrytningar håller isär samma adress, och ångra den ena: formuläret säger att inget
  slås ihop just nu.
- *Ta tillbaka* på en länk säger *Den streckade länken tonas bort ur grafen*; efter Enter slutar spåret vid swappen igen.
- Ett skäl längre än 1 000 tecken går inte att skriva. Utan operatörsprofil säger formuläret varför det inte sparades.
- Lås valvet medan ett ärende är öppet (eller gör biblioteket oläsbart) och ta tillbaka en etikett: den tas tillbaka i
  ärendet, och grafens rad säger att biblioteket följer med när ärendet öppnas eller stängs.
- `/verify` efter allt detta: hashkedjorna är hela.

## Källornas endpoints (etapp 24, KL1a)

**Sidan Källor**
- Ctrl+, → *Källor* (efter Nätverk): chipsen Bitcoin, Ethereum, TRON och Solana; de tre sista tonade med *Inte byggt
  än*, och ett klick på dem säger att kedjan får sina källor när den byggs.
- Bitcoin har mempool.space och Blockstream som förval, i läget *Snabbast*. Raderna har prick, namn, adress (Cascadia
  Mono), stapel och millisekunder, ★ på den som används, brytare och ×.
- *Prova alla nu*: båda provas genom utgången (kontrollera med Wireshark eller utgångens logg att uppslaget bara är
  `blocks/tip/height`, och att inget går ut på det vanliga nätkortet med VPN valt). *Provade hh:mm* visar tiden.
- Utan ansluten utgång (VPN valt): *Prova alla nu* säger *Inget provades: utgången tillåter inga uppslag nu*, och inget
  skickas. Låst Chainsaw: inget provas.
- Vid start: lås upp med en ansluten utgång (eller Windows) och kontrollera att endpointarna provas en gång; ansluter
  utgången först senare provas de då.
- *Prioriterad*: platsnummer och handtag syns, pilarna och Alt+↑/Alt+↓ på en rad med fokus flyttar endpointen och
  fokus följer med. Stäng av den första (brytaren) eller lägg in en som inte svarar först: expandera i grafen och se att
  nästa tas, och att den döda får röd prick.
- *Av*: listan tonas, raden *Inga uppslag för Bitcoin …* syns, grafens hörn säger *Av* (med förklaring på hovring), och
  en expansion visar bara det ärendet har, utan att något skickas.
- Lägg till en egen Esplora som ligger efter (till exempel en nod som synkar): gul markering *N block efter*, och den
  används inte i Snabbast.

**Lägg till endpoint …**
- `http://…` säger *Bara https.* och *Lägg till* är tonad. `https://user:pw@…`, `?` och `#` vägras också.
- *Prova* visar *Höjd 921 404 · 00000000…xxxx · NN ms* i grönt, eller varför inte. Inget läggs till av *Prova*.
- API-nyckel ur valvet: välj en nyckel (lagd i valvet), lägg till och prova. Kontrollera i `sources.json` att bara
  nyckelns namn står där, aldrig nyckeln, och att nyckeln går som `Authorization: Bearer` (inte i adressen).
- Esc fäller in formuläret före dialogen; Enter i adressen lägger till.

**Ärendet och filen**
- Efter en expansion: `/verify` är grön, och varje råsvar i ärendet har `endpoint_id` och `endpoint_name` (till exempel
  med en SQLite-läsare på arbetskopian i ett testärende).
- Valen ligger kvar efter omstart i `%LOCALAPPDATA%\Chainsaw\sources.json`. En trasig fil ger förvalen; en endpoint med
  `http://` som skrivits in för hand används aldrig.

## Kontroller, förslag och områden (etapp 22, KF1a och KF1b)

Gör ett ärende med en etikett *Mixer* på en adress som tar emot pengar, och expandera några aktörer efter den, så att
mixerns kandidater, en CoinJoin eller en peel chain finns i grafen.

**Listan (KF1a)**
- *Förslag · N* står i grafens hörn, och N är antalet kandidater som väntar. Ctrl+J och knappen fäller ut listan där
  inspektören står och fäller in den igen; krysset i listan stänger den.
- Grupperna *Att avgöra* (streckad ring), *Gjort av sig självt* (hel ring) och *Avgjort* (bara när något är avgjort).
  Längst ner står tangenterna och när kontrollerna kördes, på hur många tx.
- Den valda raden fälls ut, resten av grafen tonas ned, och bara radens område syns. J och K stegar, Enter godkänner
  (*Godkänn bästa* först), X avfärdar, ? öppnar Guiden med *Varför?*. Tangenterna fungerar när grafen har fokus, också
  direkt efter ett klick på *Förslag · N*. Med svenskt tangentbord ska ? (Shift och +) inte zooma medan listan är öppen.
- Klicka på en annan kandidat och tryck Enter: den godkänns. Skriv ett skäl i fältet och tryck Enter: skälet sparas
  (kontrollera i journalen). En avfärdad kandidat är överstruken och dess linje försvinner ur grafen.
- Stäng och öppna ärendet: besluten står kvar, och en avfärdad kandidat kommer inte tillbaka.
- På en kontroll visar Enter (*Visa i grafen*) dess aktörer och zoomar dit; X gör ingenting.
- Inspektören visar under *Här* vilka kontroller och förslag som gäller den valda aktören.

**Områdena (KF1b)**
- Svaga ytor bakom aktörerna: rött för mixer och CoinJoin, sand för peel chain, grått för plånböcker, prickat för tid.
  Ett förslag som väntar har streckad kant. Ingen aktör har två fyllningar; ett inre område syns bara som kant.
- Brickan på kanten visar ikonen; namnet syns på brickan när den väljs. Ett klick på en kontrolls bricka öppnar listan
  vid dess rad.
- Menyn bredvid *Förslag · N* (lagerikonen och antalet områden) visar sorterna med antal; en bock döljer och visar
  sortens områden.
- Linjerna från mixern till kandidaterna är streckade i accentfärgen med poängen på, svagare ju lägre poäng.
- Grafen ska vara lika snabb med områdena på; panorera och zooma i en stor graf.

**Egna ytor**
- Ctrl+klicka två aktörer (accentram), tryck Z: formuläret visar *2 valda · Ny yta*. Skriv namn, välj sort och färg,
  kryssa *Samma aktör?*, skriv en anteckning och tryck Enter. Ytan har hel kant och en liten person på brickan.
- Z utan valda aktörer tar den valda aktören; utan någon säger formuläret hur man väljer.
- Klicka på ytans bricka: namnet, pennan och korgen syns. Pennan byter namn, korgen tar bort ytan (med valfritt skäl).
  Journalen har en rad för varje.
- Esc stänger formuläret, sedan menyn, sedan listan, sedan valet av aktörer.

## Ethereum, första delen (etapp 23)

Grafen för Ethereum är inte byggd än; det som går att prova är källan, startpunkterna, Källor och Guiden.

- Ctrl+, → *Källor*: chipset **Ethereum är inte längre tonat**. Det har *Blockscout* (`https://eth.blockscout.com/api/`)
  i läget *Snabbast*. *Prova alla nu* frågar bara efter kedjans höjd (`?module=block&action=eth_block_number`) genom
  utgången; raden visar höjd och millisekunder. TRON och Solana är fortfarande tonade.
- *Lägg till endpoint …* för Ethereum: sorten *Blockscout*; *Prova* visar höjd och blockets hash (`v2/blocks/:höjd`).
  En egen Blockscout på det lokala nätet går att lägga till på samma sätt (bara https).
- Kommandoraden och Nytt ärende: en Ethereum-adress i blandade versaler med rätt kontrollsumma (EIP-55) känns igen som
  Ethereum; ändra en bokstavs versal/gemen och den känns inte igen. Svaret säger ärligt att grafen för Ethereum inte är
  byggd än. Samma för `chainsaw:ethereum/0x…`.
- Guiden (F1): gruppen *Ethereum* med *Kontomodellen*, *ERC-20 och tokens*, *Interna tx* och *Kan frysas*.
- Vid start provas nu också Ethereums endpoint (eth.blockscout.com) genom utgången, fast grafen för Ethereum inte är
  byggd än (beslutet att prova vid start gäller alla kedjor). Sätt Ethereum på *Av* i Källor om det inte ska ske.

## Endpointarna, andra delen (etapp 25)

**Takt och väntan**
- Expandera en adress med många tx i grafen: uppslagen går i takt (högst fyra i sekunden per endpoint, Chainsaws eget
  förval) och fördelas mellan mempool.space och Blockstream (se i ärendets råsvar att båda förekommer). Grafen och
  dialogerna ska kännas lika snabba som förut; inget fryser medan Chainsaw väntar.
- Får en källa svara `429` (lättast med en egen proxy eller en egen Esplora bakom en hastighetsgräns): raden i *Källor*
  får den gula markeringen *Väntar* med tiden på hovring, pricken blir inte röd, och de andra endpointarna används under
  tiden. Statusraden säger *Väntar på källan · Bitcoin* bara medan ett uppslag faktiskt väntar (mer än en sekund), och
  markeringen försvinner när tiden gått.
- En källa som är död (fel adress eller avstängd server): den lämnas tyst (uppslagen lyckas genom de andra, inget fel i
  grafen), får röd prick, och provas på sin plats igen efter en minut, sedan två, fyra … högst 30 minuter. Starta servern
  igen och se att den kommer tillbaka och får grön prick.

**Ett blocks marginal**
- En egen Esplora som ligger exakt ett block efter (till exempel direkt efter ett nytt block): ingen gul markering, och
  hovring på pricken säger *ett block efter räknas inte som efter*. Två block efter ger *2 block efter* som förut.

**Egen nod på det lokala nätet**
- *Lägg till endpoint …* med `https://192.168.x.x/…` (eller `https://esplora.lan/…`) utan *Lokal*: *En adress på datorn
  eller ett privat nät kräver att den märks Lokal.* och *Lägg till* tonad. Slå på *Lokal*: felet försvinner.
- *Lokal* på med en adress på internet som IP (till exempel `https://8.8.8.8/`): *En lokal endpoint ska ha en adress på
  datorn eller ett privat nät.*
- Med en egen Esplora på det lokala nätet (eget certifikat installerat bland Windows betrodda rötter): *Prova* svarar med
  höjd och hash **också utan ansluten utgång**, raden får märket *Lokal*, och uppslag till den syns inte i utgångens
  logg (kontrollera med Wireshark att de går direkt till noden och att inget annat går direkt ut på nätkortet med VPN
  valt).
- Ett namn märkt *Lokal* som pekar på internet (ändra `hosts` eller DNS): uppslaget vägras med *Adressen pekar inte på
  datorn eller ett privat nät …* och inget skickas.
- Efter en expansion genom den lokala noden: `/verify` grön, och råsvaren har `exit_route` = `local` och inget
  `exit_id`.
- `sources.json`: endpointen har `"Local": true` och `"IntervalMilliseconds": 0`. Ändra för hand en privat adress till
  `"Local": false`: den endpointen används inte efter omstart.

## Ethereum, andra delen: grafen och inspektören (etapp 23b, TK1a)

Kräver en ansluten utgång (eller Windows som utgång) och Ethereum i läget *Snabbast* eller *Prioriterad* i Källor.

**Startpunkter**
- Skriv en Ethereum-adress (`0x…`, gärna med rätt EIP-55-versaler) i kommandoraden med ett öppet ärende: den läggs till
  som startpunkt och syns som en rund nod med flaggan. Samma för en tx-hash (`0x` och 64 tecken), Nytt ärende och
  `chainsaw:ethereum/0x…`. TRON säger fortfarande *inte byggt än*.

**Expansion och linjerna**
- Välj Ethereum-noden och tryck E: historiken hämtas i bakgrunden (tx, interna tx, tokens). Linjerna har märket först i
  beloppet (**₮** USDT, **$** USDC, **Ξ** ether) och värdet i kronor litet under när kursen finns; en okänd token står
  med sin symbol. Utan kurs står inget värde (inte noll). USDT och ether mellan samma två adresser är två linjer.
- En adress med fler tx än gränsen: raden säger att resten hölls tillbaka, och noden står som delvis expanderad.
- En adress som bytte token själv (till exempel USDT in till Uniswap och ether tillbaka i samma tx): ett litet lila
  piller **₮ → Ξ** på linjen till poolen, också med *Inga belopp*. Ingen annan linje har pillret.
- Spårningen: accenten följer pengarna som kom in till startadressen, per tillgång, genom de adresser som är
  expanderade. Byt FIFO/haircut och se att de spårade beloppen ändras. Två startpunkter i varandras flöde ger den
  ärliga raden om att spårningen inte kunde räknas.
- Stäng av utgången och öppna ärendet igen: grafen, linjerna och värdena ritas ur ärendet utan uppslag.

**Inspektören**
- En Ethereum-nod visar *Kedja: Ethereum*, *Spårat in* per tillgång, och tabellen Token · In · Ut · Kvar med utgivaren.
  Har en expanderad adress USDT eller USDC kvar står snöflingan i nodens hörn, i tabellen och i en ruta som säger att
  frysning begärs hos utgivaren. En adress som inte är expanderad säger *Inte expanderad: saldot är okänt* och får
  ingen snöflinga.
- En linje visar Belopp med märket, Värde (eller *Ingen kurs*), Tid, Tx, Swap när den finns, och varje tx med block och
  blockhash (`0x…`). *Varför?* förklarar token, swap och kursen steg för steg.
- Etikett (L) på en Ethereum-nod: sparas och syns i grafen och i Historik; ta tillbaka fungerar. Bryt upp (U) finns
  inte (inga kluster på Ethereum). Annan kedja (K): välj en rörelse som kom in till adressen (till exempel en brygga),
  välj TRON och klistra in en tx; en lila streckad linje går till den andra kedjans nod, och länken syns i Historik.
- *Förslag · N* (Ctrl+J): statusraden säger att kontrollerna gäller UTXO och inte körs på Ethereum.

## Den tomma grafen (GR6c)

- **Första gången** (inga ärenden i mappen): mitt på grafen står *Nytt ärende · Ctrl+N* med en pulserande ring, under
  den två rader om vad ett ärende är, och ärendets gång som fem prickar (Ärende först, i accent). Ett klick öppnar
  panelen Nytt ärende.
- **Med ärenden:** *Nytt ärende* i mitten, de fyra senast sparade ärendena runt om (nummer och *ändrad i dag 14:02*,
  *ändrad i går …* eller datum) och *Alla ärenden … · /open* sist, med prickade linjer från mitten. Ett klick öppnar
  ärendet; *Alla ärenden …* skriver `/open ` i kommandoraden med markören sist, så att Tab fyller i. Ringen krymper
  i ett litet fönster (960 × 600) men alla noder syns och krockar inte med pillret.
- **Ett ärende utan startpunkt:** *Första startpunkten* med en streckad nod (flaggan, ett fält och *Enter*), sonar
  runt den, brickorna Bitcoin-adress, Ethereum-adress, Bitcoin-tx och Ethereum-tx, och under dem tre svaga steg
  (Expandera, Hitta uttaget, Bevaka) som förklarar sig på hovring.
- Skriv eller klistra in en adress: brickan för sorten lyser grönt. **Ctrl+V var som helst på grafen** lägger texten
  i fältet. Enter lägger till startpunkten och grafen visas; text som inte känns igen ger en röd rad.
- Raden under brickorna säger att inget hämtas förrän du expanderar, och genom vilken utgång. Utan utgång: grå prick,
  *Offline …*, och ringen och ramen blir grå; startpunkten går ändå att lägga till.
- Längst ner: *Så spårar man i Chainsaw* öppnar Guiden bredvid ytan (avsnittet med de fem stegen), och utgången står
  med sin prick.
- Med animeringar avstängda i Windows står ringarna stilla, svaga.
- [ ] Utan ärende växer tre tunna ringar i accentfärgen ut runt *Nytt ärende* från nodens rundade kant, med samma hörn, och tonar bort lugnt efter ungefär 2 cm. Ingen fast ram står kvar runt noden.
- [ ] Ringarna står mitt på noden, både i navet med de senaste ärendena och första gången utan ärenden, och skär inte in i raden under noden.
- [ ] *Nytt ärende* har ett svagt sken i accentfärgen; hovring och klick fungerar som förut, och ringarna tar aldrig musen.
- [ ] Ett ärende utan startpunkt: samma ringar växer ut från den streckade startpunktsnoden, och offline blir de grå.
- [ ] Med animeringar avstängda i Windows står en enda svag, stilla ring runt noden; slås de på igen startar pulsen utan omstart.
- [ ] Pulsen rör sig mjukt utan att hacka och stannar när man byter vy eller öppnar ett ärende.

## Bevakningen, första delen (etapp 26, BV1a och BV2a)

Kräver en ansluten utgång (eller Windows som utgång), ett ärende med en graf, och att endpointarna har provats vid
start (en endpoint måste vara *i fas* för att bevakningen ska fråga).

**Högerklick och formuläret (BV2a)**
- Högerklicka på en adress eller ett kluster i grafen: menyn har Etikett (L), Bryt upp (U, bara kluster), Annan kedja
  (K), **Bevaka … (B)**, Expandera (E) och Kopiera adress (Ctrl+C). En tx-nod har Bevaka tonad. Klick utanför eller Esc
  stänger menyn.
- *Bevaka …* (eller B på en vald nod) fäller ut formuläret under grafens hörn: riktning In/Ut/Båda, Mottagare och
  Avsändare som brickor, Tillgång (bara Ethereum: Alla, ETH, USDT, USDC), Minsta belopp och *Hur ofta* (Varje block,
  1, 5 eller 15 min). Håll Ctrl och
  klicka på noder: de läggs som brickor i den rad vars sikte är rosa (klicka *Alla, eller lägg till* för att byta rad).
  Noden själv vägras. Ctrl+klick utan öppet formulär väljer fortfarande aktörer för en egen yta (Z).
- Enter sparar, Esc avbryter. Noden får ett **rosa öga** i hörnet.

**I grafen**
- En bevakning med mottagare: en tunn **prickad** linje i en dov turkos ton från noden till varje mottagare som inga
  pengar gått till, med ett litet öga mitt på. Till en mottagare som redan har en linje: linjen får ett mjukt turkost
  sken i stället. Avsändare: linjerna går in till noden.
- Utan motparter: korta prickade strålar vid porten (höger för ut, vänster för in) med ett litet öga.
- Koppla ner utgången: ögonen blir ihåliga och grå, linjerna och strålarna grå. Anslut igen: färgerna kommer tillbaka.
- Vid en träff (vänta på ett nytt block med en bevakning på en aktiv adress, eller ett testärende): en rosa linje med
  beloppet till motparten om den finns i grafen, och **ringar som rör sig** runt mitten tills du väljer noden. Med
  *Visa animeringar i Windows* avstängt står ringarna stilla. Grafens rad säger *Bevakning slog till · …*.
- Pillrets öga får en rosa prick vid nya träffar; den försvinner när träffarna är sedda.

**Vyn Bevakning (BV1a, Ctrl+6)**
- En rad per bevakning: läge som prick (grön aktiv, gul väntar, grå pausad), namn, kedja, pilar för riktningen,
  motparterna som brickor, tillgången, *Senast kontrollerad* med block och en rosa prick för nya träffar.
- Huvudet säger ärligt *Bevakar medan Chainsaw är öppet* (hovra för förklaringen), eller *Väntar på utgången · inga
  uppslag görs* utan utgång.
- Den valda bevakningen till höger: adressen, fakta (också *Räknas från: efter block N*), träffarna med block, och
  knapparna Öppna i grafen, Ändra, Pausa/Fortsätt och papperskorgen (skäl valfritt, Enter tar tillbaka).
- *Ny bevakning …* (B, eller Nytt → Bevakning i pillret): skriv en Bitcoin- eller Ethereum-adress; kedjan känns igen.
- *Hur ofta* står bland den valda bevakningens fakta och kan ändras med Ändra.

**Historiken (AN1b)**
- Inspektörens Historik för en bevakad nod har *Bevakning · ut · 1 mottagare* med *Ta tillbaka*, ändringar och pauser,
  träffarna som fakta, och efter borttagning raden överstruken med *Tillbakatagen: bevakningen*.

**Att kontrollera**
- `/verify` är grönt efter att bevakningar lagts till, ändrats, pausats, tagits bort och slagit till.
- Lås Chainsaw (Ctrl+L) och vänta på ett block: inga uppslag görs (utgångens trafik står still); efter upplåsning
  fortsätter bevakningen.
- Ett stort Ethereum-ärende med bevakning: grafen ska inte kännas trög medan ringarna rör sig.
- [ ] Bevakning i ett ärende utan bevakningar: ett stort öga i en mjuk rund cirkel mitt på ytan med *Inga bevakningar än*, två dämpade rader under och knappen *Ny bevakning …*; ingen räknare, inget bakgrundsläge, ingen knapp uppe till höger och ingen högerpanel.
- [ ] Klicka på *Ny bevakning …* i det tomma läget (eller tryck B): formuläret öppnas och det tomma läget försvinner medan det är öppet.
- [ ] Bevakning utan öppet ärende: *Inget ärende öppet* med en förklarande rad och ingen knapp.
- [ ] Med en bevakning: listan, räknaren, bakgrundens läge som prick och text utan ram och *Ny bevakning … B* syns i huvudet, och högerpanelen visar den valda.
- [ ] Bakgrundens läge i huvudet: grön prick när den går, gul när den väntar på utgången, grå när den är av; hovra för förklaringen.
- [ ] Ta bort den sista bevakningen: högerpanelen fälls bort och det tomma läget kommer tillbaka.

## Bevakningen, andra delen: när Chainsaw är stängt (etapp 27)

Kräver Windows som utgång (*Inställningar → Nätverk*), ett ärende med en aktiv bevakning och att `Chainsaw.Watcher.exe`
ligger bredvid `Chainsaw.exe`.

**Inställningarna (ST1a, Låsning)**
- Tre nya rader med reglage: *Bevaka när Chainsaw är stängt*, *Avisering i Windows* och *Ljud*, alla på från början.
  Reglaget blir rosa och knappen går till höger när det är på. Meddelandet mitt på skärmen finns **inte** som rad (väntar
  på skiss).

**Processen**
- Med en aktiv bevakning: `Chainsaw.Watcher.exe` syns i Aktivitetshanteraren (låg prioritet), utan fönster och utan
  ikon i aktivitetsfältet. Den ska använda lite minne (kontrollera ungefär hur mycket, gärna under 60 MB) och nästan ingen
  CPU mellan kontrollerna.
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` har värdet `Chainsaw.Watcher` med sökvägen. Pausa eller ta
  bort alla bevakningar, eller slå av *Bevaka när Chainsaw är stängt*: värdet försvinner och processen avslutas inom
  några sekunder.
- Starta `Chainsaw.Watcher.exe` två gånger för hand: bara en process blir kvar. `Chainsaw.Watcher.exe --stop` stoppar den.
  Har en avisering just visats lever processen kvar upp till en minut (tills ikonen tas bort), utan att bevaka.
- Logga ut och in med en aktiv bevakning: processen startar av sig själv.
- Döda processen i Aktivitetshanteraren medan Chainsaw är öppet: Chainsaw startar den igen inom någon minut.

**Bevakningen när Chainsaw är stängt**
- Lägg en bevakning på en aktiv adress (eller en egen testadress du kan skicka till), stäng Chainsaw och vänta på en tx i
  ett nytt block. En avisering i Windows säger **bara** *En bevakning har slagit till.* och *Öppna Chainsaw och lås upp
  för att se den.*, och mynt-swooshet spelas. Ingen adress, inget belopp, inget ärende.
- `Chainsaw.Watcher.exe --test-alert` visar aviseringen och spelar ljudet en gång, utan någon träff (lyssna: låter det
  som ett mynt som flyger förbi och skramlar ner?).
- Starta Chainsaw: låsskärmen har raden *En bevakning har slagit till. Den visas efter upplåsning.* med en rosa prick.
- Lås upp och öppna ärendet: inom några sekunder står träffen i vyn Bevakning och i grafen (ringarna), journalen har
  *Bevakningen av … slog till i bakgrunden* och *Bakgrundens bevakning förd in: …*, och `/verify` säger att ärendet är
  helt. Raden på låsskärmen är borta nästa gång Chainsaw låses.
- Lås Chainsaw (Ctrl+L) med ärendet öppet och vänta på en träff: bakgrunden tar över medan det är låst.
- Medan ärendet är öppet frågar bakgrunden inte efter det ärendets adresser (bara Chainsaw gör det).

**Huvudet i vyn Bevakning (BV1a)**
- Windows som utgång och bakgrunden på: *Går i bakgrunden · genom Windows · i fas med …* (grön prick, hel ram).
- VPN som utgång: *Bakgrunden väntar på utgången · bevakar bara medan Chainsaw är öppet* (gul, streckad ram); hovra
  för förklaringen om valvet.
- Bakgrunden av: *Bakgrunden är av · bevakar bara medan Chainsaw är öppet* (grå, streckad ram).
- Ta bort `Chainsaw.Watcher.exe` och starta om: *Bakgrunden går inte · …* (gul).

**Filerna**
- `%LOCALAPPDATA%\Chainsaw\Bevakning\` har `plan.bin`, `progress.bin`, `status.json`, `queue\…` och, medan ett ärende
  är öppet, `held\…`. Öppna `plan.bin` och en fil i `queue` i en texteditor: inga adresser ska gå att läsa (DPAPI).
  Kopiera `plan.bin` till en annan Windows-användare: bakgrunden där bevakar ingenting.

## Meddelanden och polering (TO1a, 2026-11-07)

- [ ] Expandera en adress: en grön toast *N nya tx i grafen* glider in nere till höger och tonar ut efter några
      sekunder; strecket i nederkanten krymper, och hovring stoppar det.
- [ ] Koppla ner utgången och expandera: en toast säger varför. Ett fel ligger kvar tills det stängs med krysset, och
      *Försök igen* gör om expansionen.
- [ ] Sju toasts i rad: högst fem står, den äldsta tonar ut när en sjätte kommer.
- [ ] Lås med Ctrl+L: inga toasts ligger kvar över låsskärmen.
- [ ] Med animeringar avstängda i Windows dyker en toast upp utan glidning och försvinner utan att tona.
- [ ] Bildspråkets *?* nere till höger i grafen är tydligt, med ett skarpt frågetecken och en ring på 1,5 px.
- [ ] Inspektörens vänsterkant har ett litet grepp mitt på; vilar pekaren där en stund tonar en mörkgrå linje in längs
      hela kanten.
- [ ] Etikett, Bryt upp och Annan kedja går att spara utan skäl.
- [ ] *Inställningar → Källor → Prova alla nu* och en expansion fungerar (felet *The media type asked for is not
      valid* är borta).
- [ ] Välj en nod: en list med ikoner står ovanför den (Expandera i accent först), bara de som går att använda; för
      musen över en ikon för namnet och tangenten. Panorera, zooma och dra noden: listen följer med. Nära överkanten
      hamnar listen under noden.
- [ ] Tryck L (eller etikettikonen): formuläret fälls ut i en ruta till höger om noden med en pil mot den; nära
      högerkanten till vänster. Esc stänger, Enter sparar. Samma för U och K.
- [ ] Inspektören har inga knappar längre, bara *Varför?* som rund ikon i huvudet och kopiera vid adresserna.
- [ ] Grafens hörn uppe till höger är en rad ikoner: förslag (prick när något väntar), områden (prick när några ritas),
      fri yta och hoppkolumner (den valda markerad), metoden som text och beloppen på linjerna (nedtonad vid *Inga
      belopp*). Tipsen säger vad varje ikon gör.
- [ ] Inspektörens skugga syns inte ovanför titelradens underkant.
- [ ] Klicka på + i pillret: undermenyn växer ut ur knappen åt höger och tonar in, och raderna glider det sista
      stycket på plats. Med animeringar avstängda i Windows dyker den bara upp.
- [ ] Välj en nod och tryck B (eller ögat i listen): Bevaka glider in i en ruta bredvid noden med en pil mot den. Bara
      riktningen syns, som tre stora ikoner (In, Ut, Båda), och en mening under säger vad som händer.
- [ ] *+ Bara vissa motparter*, *+ Minsta belopp* och *+ Hur ofta* fäller ut sitt fält och blir ✓; meningen ändras med
      det som väljs. Ctrl+klick på noder i grafen lägger till motparter. Tillgången syns bara på Ethereum.
- [ ] Ändra en bevakning med motparter eller ett minsta belopp: de fälten är redan utfällda.
- [ ] Tomma textfält visar en svag platshållartext där en sådan finns.
- [ ] Utan öppet ärende har pillret bara Nytt, Graf, Guide och Inställningar; öppna ett ärende och de andra vyerna
      fälls in animerat, stäng det och de fälls bort (en vy som kräver ett ärende byter då till grafen).
- [ ] Tidslinje, Flöde, Exponering, Begäran och Etiketter är nedtonade, och tipset säger *Inte byggt än*. Terminal
      finns inte längre.

## Statusraden och kommandoraden som Spotlight (polering 2026-11-07)
- [ ] Statusraden längst ner är borta; utgången står bara i titelraden, och hovring på den säger vart uppslagen går.
- [ ] När en källa ber Chainsaw vänta syns en liten gul prick till vänster om utgången i titelraden, med texten på hovring; den försvinner när väntan är över.
- [ ] Kommandoraden syns inte förrän Ctrl+K eller Ctrl+F; då kommer den som Spotlight i övre delen, 640 px bred, med förstoringsglas och större text, över en mjuk mörkning, och tonar in med en liten rörelse (direkt med animeringar avstängda).
- [ ] Esc, ett klick utanför (också i titelraden) och ett kommando som lyckas döljer raden; tangentbordet går tillbaka dit det var.
- [ ] Ett kommando som misslyckas låter raden stå kvar med felet i rött under fältet.
- [ ] *Alla ärenden …* i den tomma grafen öppnar raden med "/open " och markören sist, och Tab fyller i ärendenumret.
- [ ] Ett svar som kommer när raden är dold (nytt ärende skapat, sanktionslista importerad) visas som en toast.
- [ ] Nytt → Startpunkt med ett ärende öppet tar fram raden med förklaringen; en chainsaw:-länk tar fram den med adressen ifylld.
- [ ] Med Guidens panel öppen står raden mitt i ytan bredvid panelen; Ctrl+K medan Nytt ärende är öppet gör ingenting.

## Ärendeväljaren (ÄV1a)
- [ ] Klicka på ärendet i titelraden (eller *Inget ärende öppet*): ärendeväljaren fälls ut under det med samma yta, kant och skugga som utgångarnas ruta och glider in mjukt (stilla med animeringar avstängda).
- [ ] Ctrl+O öppnar den med markören i *Sök ärendenummer*; Esc stänger den och tangentbordet går tillbaka till ärendet i titelraden.
- [ ] Ett klick utanför stänger den utan att klicket når det som ligger under; ett klick på ärendet igen fäller in den.
- [ ] Med ett ärende öppet står *Öppet nu* med *Inte kontrollerat sedan det öppnades*; *Verifiera* visar *Kontrollerat nu · inget ändrat* (eller felet i rött), och *Stäng* stänger ärendet medan rutan står kvar.
- [ ] Listan visar ärendena med det senast ändrade först, med nummer och *ändrad i dag 14:02*; det öppna har accentstreck och brickan *Öppet*.
- [ ] Sökfältet filtrerar på ärendenummer och räknaren följer med; utan träff står *Inget ärende har det numret.*
- [ ] Upp och Ned flyttar den valda raden; Enter öppnar ärendet och rutan stängs. Ett klick väljer, ett andra klick öppnar.
- [ ] *Nytt ärende … Ctrl+N* stänger rutan och öppnar panelen Nytt ärende.
- [ ] Ärendeväljaren, utgångarnas ruta och kommandoraden är aldrig öppna samtidigt, och ärendeväljaren stängs när Chainsaw låses.

## Ikonlisten vid noden och zoomen (polering 2026-11-07)
- [ ] Välj en nod och zooma ut: ikonlisten ovanför noden krymper med grafen ner till tre fjärdedelar av sin storlek, och står kvar centrerad över noden.
- [ ] Zooma ut ännu mer (under ungefär 45 %): listen försvinner; E, L, B och de andra tangenterna fungerar ändå, och listen kommer tillbaka när man zoomar in.

## Panorering i grafen (polering 2026-11-07)
- [ ] Håll höger musknapp och dra, var som helst i grafen och också över en nod: ytan följer med och pekaren blir ett fyrpilskors.
- [ ] Samma sak med mittknappen (hjulet nedtryckt).
- [ ] Släpp i farten: ytan glider vidare och saktar in mjukt; ett nytt tryck eller hjulet fångar den. Släpp stilla: den står kvar.
- [ ] Ett högerklick utan att dra öppnar nodens meny som förut, och ingen meny kommer efter en panorering.
- [ ] Vänsterknappen på tom yta panorerar inte längre; den drar noder och väljer.
- [ ] Med animeringar avstängda i Windows glider ytan inte efter släppet.
- [ ] Grafens läge (var man panorerat) finns kvar när ärendet öppnas igen.

## Grafens ikon (IK1)
- [ ] Grafens ikon i pillret är en förgrening: en rund aktör till vänster vars linje delar sig mot en rund aktör uppe till höger och en fyrkantig tjänst nere till höger, skarp i 20 px.

## Windows som utgång utan nät

- [ ] Välj *Windows* i Inställningar › Nätverk med nät: titelradens utgång säger *Windows* i grönt.
- [ ] Dra ur nätverkskabeln och stäng av Wi-Fi (eller slå på flygplansläge): inom någon sekund säger titelraden
      *Offline* i rött, och verktygstipset säger att datorn inte har något nät.
- [ ] Öppna utgångsrutan utan nät: mittringen och skärmikonen är röda, det står *Offline* under, och båda linjerna är
      röda och streckade.
- [ ] Expandera en ny adress utan nät: grafen säger offline ärligt, inget uppslag görs och ingen endpoint märks som
      trasig i Inställningar › Källor.
- [ ] Koppla in nätet igen: titelraden och utgångsrutan blir gröna och säger *Windows* utan att Chainsaw startas om, och
      källorna provas igen.
- [ ] Med *VPN* valt och en ansluten utgång ändrar en kort nätförlust inte titelraden till *Offline* av sig själv
      (tunnelns eget läge gäller).
- [ ] Ett nät som finns men inte når internet (till exempel ett Wi-Fi med inloggningssida) visas som *Offline* inom
      några sekunder, och inga uppslag görs. Logga in på sidan: inom cirka 10 s blir titelraden grön och säger *Windows*.
- [ ] I ett nät där ping (ICMP) är spärrat men https går genom datorns proxy visas *Windows* ändå grönt.

## Ledtrådsnoden och samlingsnoden (EX1, EX2b)
- [ ] Expandera en adress med många tx (till exempel en börs eller en pool med över 1 000): grafen får 50 tx, och vid
      adressen står en streckad rund nod `+1 709 tx` (eller `+ fler tx` när källan inte sa hur många) med en tunn
      streckad linje till adressen.
- [ ] Ett klick på `+N tx` hämtar nästa 50; siffran minskar och noden försvinner när allt är hämtat.
- [ ] Har expansionen fler än åtta nya motparter på en sida ritas de inte var för sig: en fyrkantig nod som en
      kortlek (*Avsändare* till vänster, *Mottagare* till höger) med antalet och summan, och en tjock linje med summan.
- [ ] En motpart med etikett (börs, sanktion, offer) eller som spårade pengar når står alltid kvar som egen nod.
- [ ] Ett klick på högen dimmar resten av grafen och fäller ut korten i en spiral, störst närmast; ikonen med klockan
      i listen längst ner byter till tidsbågen, tidigast överst. Korten överlappar aldrig, också med hundratals.
- [ ] Reglagen i listen gallrar på belopp (lägsta och högsta, logaritmiskt), *Damm* döljer belopp under 0,0001 BTC och
      räknaren säger till exempel `12 av 40`.
- [ ] Klick väljer ett kort, Shift-klick väljer fler; *Plocka ut* eller Enter plockar ut de valda, som blir egna noder.
- [ ] Dra ett kort ut på den dimmade grafen och släpp: det blir en egen nod där det släpptes, och de andra valda
      följer med under det. Resten ligger kvar i högen.
- [ ] Esc eller ett klick på den dimmade grafen stänger högen.
- [ ] Stäng ärendet och öppna det igen: de utplockade står kvar som egna noder, högen och `+N tx` finns kvar.
- [ ] Välj en utplockad nod: ikonlisten har *Lägg tillbaka i högen*, som lägger tillbaka den.
- [ ] Verifiera ärendet efteråt: inget har ändrats i det förseglade (högen är bara hur grafen ritas).

## Utgångar utan mottagare döljs (OP_RETURN)
- [ ] Expandera en adress som tillhör en pool (coinbase-tx med OP_RETURN på 0 BTC): noderna för OP_RETURN syns inte,
      och en toast säger till exempel *49 utgångar utan mottagare döljs.* med *Visa dem*. Toasten står kvar i 20 s
      (de vanliga 6 s).
- [ ] *Visa dem* visar dem i grafen, och en ny toast erbjuder *Dölj dem*. Valet finns kvar när ärendet öppnas igen.
- [ ] En OP_RETURN som bränner pengar (mer än 0 BTC) döljs aldrig.

## Grafen använder ytan
- [ ] Med många motparter i samma hopp (under gränsen för högen, eller efter att många plockats ut) står de i flera
      kolumner sida vid sida i ett ungefär kvadratiskt block, inte i en lång kolumn; i hoppkolumnerna är hoppets band
      lika brett som blocket.
- [ ] Beloppen på linjerna hamnar aldrig ovanpå varandra: ett belopp som skulle krocka visas när pekaren är över
      linjen eller när noden är vald.

## Testärendet (/testdata)
- [ ] Bygg och kör i Debug (`dotnet run --project src/Chainsaw.App`), lås upp och skriv `/testdata` i kommandoraden (Ctrl+K). Efter några sekunder står *Testärendet TEST-0001 skapades med påhittade data och är öppet*, och grafen visar det; ett andra `/testdata` ger TEST-0002. I ett Release-bygge finns kommandot inte, och `/help` visar det bara i Debug.
- [ ] Ärendeväljaren visar också TEST-0001-KOLLEGA, kollegans ärende där etiketten från ett annat ärende sattes.
- [ ] Grafen har startpunktens flagga (offrets betalning till bedragaren är startpunkten), och varje sort i bildspråket finns: offer, kluster, okänd, börs (Nordbörsen med *Uttag funnet*), utan KYC (Snabbväxlaren, streckad), nästlad, OTC, automat, kort, spel, handel, presentkort, darknet (Skuggtorget med sanktionsmärket), garanti (Garantitorget med märket *från ett annat ärende*), mixer, CoinJoin (Whirlpool-tx:en), sanktion, swap, delad tx (gruvbelöningens mynt och en annan ägare), mynt (gruvbelöningens P2PK och OP_RETURN) och annan kedja (lila, från Bytaren). *Kan frysas* saknas (kräver Ethereum, se TODO).
- [ ] *Varför?* på *Bedragarens plånbok* förklarar konsolideringen (common-input-ownership); klustret med växeladressen förklarar växeln med tre tecken.
- [ ] *Förslag · N* har *Troliga utgångar ur mixern* att avgöra (två kandidater), och områden för CoinJoin, peel chain och föreslagna plånböcker; den egna ytan *Uttagsvägar* ligger runt börserna.
- [ ] Bevakningen *Bedragarens andra plånbok* har ett öga i grafen och en träff som väntar (ett nytt offer betalar in) med animerade ringar; vyn Bevakning visar träffen. Inget av testärendet hamnar i bakgrundsprocessens plan.
- [ ] Expandera själv: *Navet* (fler än 50 tx, så resten hålls tillbaka, och många motparter), mixerns troliga utgång (går vidare till Sydbörsen), *Offret* (utbetalat från Nordbörsens heta plånbok), en av Whirlpool-utgångarna och bedragarens andra plånbok (det nya offrets betalning).
- [ ] Allt fungerar med VPN valt och ingen utgång ansluten: testärendet frågar aldrig nätet efter kedjedata. Ett riktigt ärende som öppnas samtidigt frågar som vanligt genom utgången.
- [ ] `/verify` på testärendet säger att inget har ändrats, och en tx i inspektören visar källan *testdata* och endpointen *testdata (påhittade data, bara Debug)*.
