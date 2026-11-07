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

## Låsskärmen och förstagångsinställningen (etapp 10, ID7a och FS1a)

Jämför med skisserna ID7a och FS1a på canvasen, gärna i 1440 × 900 och i ett mindre fönster.

**Teckenfältet och kättingen**
- Fältet rör sig långsamt (scenen Drift), är tyst runt formuläret och syns inte bakom texten.
- Kättingen står still i magenta från topp till botten i vänsterdelen, rak, med loggans skarv och en rak bit med hål
  mellan skarvarna; den är ungefär lika stor som i skissen och tar inte över sidan.
- Håll Ctrl och rör musen över fältet: en ljus lykta följer musen med ett kort spår; släpp Ctrl och den tonar bort.
  Ctrl+L under tiden låser inte om låsskärmen redan visas.
- Stäng av animeringar i Windows (Inställningar, Hjälpmedel, Visuella effekter, *Animeringseffekter* av): fältet står
  still. Lyktan fungerar fortfarande.
- Minimera fönstret och lås upp: Chainsaw använder ingen märkbar processor (Aktivitetshanteraren) medan fältet inte syns.
- Fältet är skarpt på en skärm med 150 % skalning, och när fönstret flyttas mellan skärmar med olika skalning.
- Fältet hackar inte och gränssnittet känns inte trögt medan det rör sig (skriv i fältet för huvudnyckeln).

**Låsskärmen**
- Titelraden har bara loggan, *Chainsaw* och fönsterknapparna; fönstret går att dra, maximera och stänga.
- Markören står i fältet för huvudnyckeln. Fel nyckel och Enter: *Fel huvudnyckel. Valvet öppnades inte.*, fältet töms
  och markören står kvar. Rätt nyckel: skalet visas efter ungefär en sekund, utan att fönstret fryser (knappen säger
  *Låser upp …* under tiden).
- Raderna under formuläret: utgången (*Utgången ansluts efter upplåsning …*) och *Låses automatiskt efter 15 minuter
  utan aktivitet.*
- *Om Chainsaw · <commit>* i hörnet, där commit är samma som `git log -1`; klick visar version, commit och
  teckenfältets ursprung.

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
  Enter: grafen läses om och visar startpunkten som en rund magenta nod (*Offer*), blek eftersom den inte är expanderad,
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
- *?* nere till vänster öppnar bildspråket: sorterna med ikon och färg, markeringarna och linjerna, och en länk till
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
- *Varför?* visar förklaringarna: för en linje att beloppet är summan av överföringar (fakta) och det spårade beloppet
  med metod, version och tx (en bedömning); för en nod etiketten och dess ursprung, klustrets heuristiker med version
  och tx, varför spåret tar slut. Länkarna *Guiden: …* visar vyn Guide (inte byggd än) och svaret ovanför
  kommandoraden säger var avsnittet finns.

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
  *Offline: ingen utgång ansluten, så inget nytt kan hämtas*. Med en ansluten utgång: grön prick och namnet, och
  statusraden *Uppslag går genom utgången …*.
- Hovring över utgången visar statusradens text. Fönstret går fortfarande att dra i titelraden runt knappen.

**Rutan**
- Ett klick på utgången fäller ut rutan under den, med högerkanten mot knappens. Knappen är upplyst medan rutan är ute.
  Ett nytt klick, Esc eller ett klick någon annanstans under titelraden fäller in den (klicket går inte vidare till
  grafen). Esc och utfällning med tangentbordet: fokus går in i rutan och tillbaka till knappen.
- Överst vägen: datorn, en linje, utgången, en linje, källorna. Grön och hel när en utgång är ansluten (med dess
  namn), röd och streckad med *Ingen* annars, också medan en utgång ansluter.
- Utgångarna i valvet i namnordning: grå prick (inte ansluten), gul *Ansluter …*, grön *Ansluten*, röd med orsaken
  (*Tunneln finns inte*, *Gick inte att ansluta*, *Förbindelsen förlorad*, *Filen godtas inte*, *Servern svarar inte*).
  Den valda utgången har ett magentafärgat streck till vänster. Inga utgångar: *Inga utgångar i valvet*.
- Strömknappen ansluter (verktygstips *Anslut*) och är upplyst med *Koppla ner* när utgången är ansluten eller
  ansluter. Anslut en andra utgång medan en är ansluten: den första kopplas ner, bara en är ansluten. Koppla ner medan
  en utgång ansluter: försöket avbryts. Gränssnittet ska inte frysa medan tunneln startar.
- Ingen rad om den offentliga adressen (skissens *Syns som …*) finns; den är inte byggd.

**Lägg till WireGuard-fil …**
- Öppnar Windows filväljare (*.conf*). Rutan står kvar medan filväljaren är öppen. Avbryt ändrar inget.
- En riktig WireGuard-fil från VPN-tjänsten: filens namn och *Giltig* i grönt, namnet ifyllt ur filnamnet med fokus
  och markerat, *Krypteras i valvet*. Spara (eller Enter) lägger den i listan, inte vald, och en ansluten utgång står
  kvar. Avbryt eller Esc går tillbaka till listan.
- Prova filer som saknar något: utan `DNS =` (eller med ett namn i stället för en adress) står *Saknar DNS = med en
  IP-adress* i rött och Spara går inte att trycka; två `[Peer]` ger *Ska ha en [Peer]*. En bild eller en tom fil ger
  ett rött besked, ingen krasch.
- Ändra filen mellan valet och Spara så att den blir fel: Spara sparar inget och visar vad som saknas.
- Lås Chainsaw (Ctrl+L) med rutan ute eller under steget: efter upplåsning är rutan stängd och filen glömd. En fil som
  väljs efter att Chainsaw låsts medan filväljaren var öppen används inte.
- Anslut den nya utgången: tunneln startar och vägen blir grön (kräver tunneln från etapp 13 och nät).
