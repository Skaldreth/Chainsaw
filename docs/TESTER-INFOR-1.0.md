# Att prova på Windows före 1.0

Det som är byggt men inte kan provas i molnmiljön (Linux), per område: vad man gör och vad som ska hända.

Filen är ordnad efter område, i den ordning det är naturligt att gå igenom Chainsaw: start och låsning, skalet,
ärenden, utgångar, inställningar, grafen, förslagen, etiketter, bevakning, begäran och rapport, Guiden och sist
testärendet. Mycket av grafen går att prova i testärendet (`/testdata` i ett Debug-bygge) utan nät.

## Start, låsskärmen och förstagångsinställningen

### Start

- `dotnet run --project src/Chainsaw.App`: fönstret öppnas mitt på skärmen, mörkt, med egen titelrad och utan fel.
- Utan valv (`%LOCALAPPDATA%\Chainsaw\vault.json` saknas) öppnas förstagångsinställningen; med valv öppnas låsskärmen.
  Skalet (ikonlisten, kommandoraden) syns aldrig förrän valvet är upplåst.

### Låsskärmen och förstagångsinställningen (etapp 10, ID8c och FS1a)

Jämför med skisserna ID8c och FS1a på canvasen, gärna i 1440 × 900 och i ett mindre fönster.

**Kättingen som blir DNA (ID8c)**
- Kättingen kommer in på snedden uppifrån i accentfärgen, med loggans skarv och en rak bit med hål mellan skarvarna, och
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
- Utgång: se *Förstagångens steg Utgång* under *Inställningar* (FS2a).
- Första ärendet: ett ärendenummer skapar `%LOCALAPPDATA%\Chainsaw\Ärenden\<nummer>.cut` och titelraden visar numret;
  ett nummer med `/` vägras; *Hoppa över* går till skalet utan ärende. Steget har *Titel* och *Startpunkt*, med
  raden om kedjan under startpunkten.

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

### Valvet (etapp 2, Security)

- Valvet hamnar i `%LOCALAPPDATA%\Chainsaw\vault.json` och innehåller bara format, version, salt, iterationer och
  krypterade fält (Base64), inga namn eller nycklar i klartext.
- Att låsa upp tar under ungefär en sekund på en vanlig arbetsstation (PBKDF2-SHA512, 600 000 iterationer), och
  gränssnittet fryser inte under tiden.
- Ingen `vault.json.tmp` ligger kvar efter att valvet sparats.

### Återställningsnyckeln och verktyget (etapp 9)

Verktyget `tools/Chainsaw.Recovery` körs på Windows (fingeravtrycket i appen provas under *Inställningar*). Se
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

## Skalet, pillret och kommandoraden

### Skalet (etapp 1, SH1b)

- Programikonen (kättingen) syns i Aktivitetsfältet, i Alt+Tab och på Chainsaw.exe i Utforskaren, skarp i alla storlekar.
- Titelraden: kättingloggan (två länkar i varandra, diagonalt) är skarp i 16 px och mellanrummen mellan länkarna syns, *Chainsaw*, *Inget ärende öppet* och *Ingen utgång* med grå prick.
- Fönstret går att dra i titelraden, dubbelklick maximerar, Windows fäster det mot kanterna, och kanterna ändrar storlek.
- Maximerat går fönstret inte utanför skärmen; minimera, maximera och stäng fungerar, stäng blir röd vid hovring.
- Pillrets ikoner är skarpa i 20 px: Nytt överst, sedan Graf, Tidslinje, Flöde, Exponering, Begäran, Bevakning,
  Etiketter och Rapport, och Guide och Inställningar längst ner. Hovring visar namnet och kortkommandot.
- Ctrl+N, Ctrl+1 till Ctrl+8, F1 och Ctrl+, öppnar rätt vy; en vy som inte är byggd säger det.
- Kommandoradens text är skarp (skuggan får inte göra den suddig).

### Runda hörn och pillret (2026-11-07)

- Fönstret har runda hörn och en tunn grå kant i Windows 11, som Gawk. Maximerat är hörnen raka.
- Vyernas ikoner ligger i ett flytande piller vid vänsterkanten, centrerat i höjd, med skugga. Knapparna är runda,
  den valda vyn har grå bakgrund, namnet och kortkommandot visas på hovring, och pricken sitter i knappens hörn.
- Sidan har ingen rubrik under titelraden; vyn börjar direkt under den, och pillret får plats i fönstrets minsta höjd (600 px).

### Undermenyer i pillret (etapp 11b, PN1)

**Formen**
- Klicka på *Nytt*: knappens runda form växer till en flik förbi pillrets högra kant och går över i menyn, som hänger
  från flikens överkant. Under fliken möts de i ett rundat inre hörn. Flik och meny är en form med samma yta, en tunn
  kant runt hela och en skugga under, och kanten är skarp (inte suddig) i 100 % och 150 % skalning.
- Fliken ligger exakt över knappen (ikonen hoppar inte), och pillrets knapp syns inte igenom.
- Raderna: Startpunkt S, Etikett E, Bevakning B, Begäran R, linje, Importera sanktionslista … I, linje, Ärende Ctrl+N.
  Kortkommandona står till höger i grått.

**Tonat och ärligt**
- Utan ärende: Etikett, Bevakning och Begäran är tonade (grå text, vanlig pil, ingen markering på hovring),
  eftersom de kräver ett ärende. Startpunkt, Importera sanktionslista och Ärende går att välja.
- Ett klick på en tonad rad gör inget och menyn står kvar.

**Tangentbordet**
- Tab till *Nytt* och Enter (eller Space) fäller ut menyn; Startpunkt är markerad. ↓ och ↑ hoppar över linjer och
  tonade rader och går runt; Home och End går till första och sista. Enter väljer.
- Medan menyn är öppen väljer S, E, B, R och I direkt (E, B och R bara med ett ärende öppet). Ctrl+N öppnar Nytt ärende och stänger menyn.
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

### Grafens ikon (IK1)

- [ ] Grafens ikon i pillret är en förgrening: en rund aktör till vänster vars linje delar sig mot en rund aktör uppe till höger och en fyrkantig tjänst nere till höger, skarp i 20 px.

### Kommandoraden som Spotlight (polering 2026-11-07)

- [ ] Statusraden längst ner är borta; utgången står bara i titelraden, och hovring på den säger vart uppslagen går.
- [ ] När en källa ber Chainsaw vänta syns en liten gul prick till vänster om utgången i titelraden, med texten på hovring; den försvinner när väntan är över.
- [ ] Kommandoraden syns inte förrän Ctrl+K eller Ctrl+F; då kommer den som Spotlight nära överkanten, 640 px bred, med förstoringsglas och större text, utan mörkning (KR1a), och tonar in med en liten rörelse (direkt med animeringar avstängda).
- [ ] Esc, ett klick utanför (också i titelraden) och ett kommando som lyckas döljer raden; tangentbordet går tillbaka dit det var.
- [ ] Ett kommando som misslyckas låter raden stå kvar med felet i rött under fältet.
- [ ] *Alla ärenden …* i den tomma grafen öppnar raden med "/open " och markören sist, och Tab fyller i ärendenumret.
- [ ] Ett svar som kommer när raden är dold (nytt ärende skapat, sanktionslista importerad) visas som en toast.
- [ ] Nytt → Startpunkt med ett ärende öppet tar fram raden med förklaringen; en chainsaw:-länk tar fram den med adressen ifylld.
- [ ] Med Guidens panel öppen står raden mitt i ytan bredvid panelen; Ctrl+K medan Nytt ärende är öppet gör ingenting.

### Sökningen (KR1a)

- [ ] Ctrl+K eller Ctrl+F och *kra* (med en etikett Kraken i ärendet): *Bästa träff* överst, stor, med rutan i nodens form och färg, *Etikett* litet bredvid och en rad om var den står och källorna; under den grupperna Etiketter, Adresser, Tx, Kluster och Guide i den ordningen, det skrivna i fetstil.
- [ ] Den valda raden visas direkt i grafen: grafen glider så att noden hamnar under rutan, med en ring i accent och en ring som pulserar utåt, och resten tonas ner. Med animeringar avstängda hoppar grafen och ringen står still.
- [ ] Pil upp och ner byter rad, och grafen följer med; *Visa i grafen* med Enter-tecknet står bara vid den valda raden.
- [ ] Enter (eller ett andra klick på den valda raden) stänger rutan och lämnar noden vald i grafen; Esc stänger utan att välja något, och ringen försvinner.
- [ ] *bc1q*: gruppen Adresser visar fyra och *4 av N · Tab visar alla*; Tab fäller ut gruppen (högst 100), och Tab igen går till nästa grupp.
- [ ] En tx som är hämtad men inte står på någon linje säger *Hämtad, inte i grafen* och *Inte i grafen* i stället för att flytta grafen.
- [ ] *utxo* (eller */guide utxo*): Guidens panel fälls ut från höger vid avsnittet, raden säger *Öppna i Guiden* och foten *Enter öppnar Guiden*; byter man till en rad i ärendet fälls panelen in igen. Enter lämnar panelen öppen.
- [ ] *utxo* säger *Inget i ärendet heter utxo. Guiden svarar på begrepp.*
- [ ] En adress som inte finns i ärendet: brickan *₿ Bitcoin* i fältet och raden *Lägg till som startpunkt* med *Inget hämtas förrän du expanderar den, och då bara adressen, genom <utgången>*; offline säger den när det kan hämtas. Enter eller ett klick lägger till startpunkten.
- [ ] Utan öppet ärende söks bara Guiden, och raden säger det.
- [ ] Ett stort ärende (tusentals adresser): raden hänger aldrig medan man skriver.
- [ ] */help* listar */guide*, och */gu* med Tab fyller i kommandot.

### Meddelanden (TO1a) och polering 2026-11-07

- [ ] Expandera en adress: en grön toast *N nya tx i grafen* glider in nere till höger och tonar ut efter några
      sekunder; strecket i nederkanten krymper, och hovring stoppar det.
- [ ] Koppla ner utgången och expandera: en toast säger varför. Ett fel ligger kvar tills det stängs med krysset, och
      *Försök igen* gör om expansionen.
- [ ] Sju toasts i rad: högst fem står, den äldsta tonar ut när en sjätte kommer.
- [ ] Lås med Ctrl+L: inga toasts ligger kvar över låsskärmen.
- [ ] Med animeringar avstängda i Windows dyker en toast upp utan glidning och försvinner utan att tona.
- [ ] Efter skissen (TO1a, 2026-11-08): varje toast har ett färgat streck i nederkanten i sortens färg. För lyckat
      och information krymper det med tiden (hovring stoppar det, och det fortsätter där det stod), för fel och
      varningar står det stilla över hela bredden. Ikonen står på en rund tonad skiva i sortens färg, åtgärden
      (*Försök igen*, *Visa dem*) är i accentfärgen, och krysset är dämpat men ljusnar vid hovring.
- [ ] Toasterna står 76 px upp från nederkanten, så att bildspråkets *?* nere till höger i grafen går att klicka på
      medan en toast visas.
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
- [ ] Tomma textfält visar en svag platshållartext där en sådan finns.
- [ ] Utan öppet ärende har pillret bara Nytt, Guide och Inställningar; öppna ett ärende och de andra vyerna
      fälls in animerat, stäng det och de fälls bort (en vy som kräver ett ärende byter då till grafen).
- [ ] Tidslinje och Flöde är nedtonade, och tipset säger *Inte byggt än* (de kommer efter 1.0). Terminal finns inte.

### Polering 2026-11-08

- Öppna ett ärende utan att välja något i grafen: inspektören till höger syns inte. Välj en aktör eller linje: den fälls
  fram. Ctrl+J (Förslag) fäller också fram den. Esc eller klick på tom yta fäller bort den igen.
- Tom startpunkt: ingen puls runt rutan och ingen prickad ring kring flaggan. *Nytt ärende* i startvyn har ingen puls, inte heller
  första gången.
- Utan ärende: Graf-ikonen syns inte i pillret, och ingen dubbel skiljelinje. Startvyns fot säger inte *Utgång: …*,
  bara *Offline* när inga uppslag går.
- Osäkra (streckade) linjer: korta, fina streck oavsett hur tjock linjen är, inte grova korvar.
- Guiden bredvid arbetet (F1 eller *?*): ingen skugga över titelraden. Längst ner tonar texten ut mot panelens botten så
  länge det finns mer att läsa; vid slutet försvinner toningen.
- Ctrl+W med ett öppet ärende: ärendet sparas och stängs och startvyn visas. Utan ärende händer inget. *Stäng* i
  ärendeväljaren säger Ctrl+W i sitt tips.

## Ärenden

### Ärendefilen (etapp 3, Cases)

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
- **Spara ofta:** gör något som skriver i ärendet (en etikett, en expansion) och vänta fem sekunder: `.cut` får en ny
  ändringstid, och en kopia av filen som tas medan ärendet är öppet går att öppna med det nya i (till exempel med
  delningslösenordet på en annan dator). Under arbetet syns ingen paus eller fördröjning i gränssnittet.
- **Spara ofta medan man arbetar hela tiden:** expandera och ändra utan paus i mer än en minut: `.cut` får ändå en ny
  ändringstid ungefär en gång i minuten.
- **Spara ofta och låsning:** lås med Ctrl+L några sekunder efter en ändring: låsskärmen kommer direkt, ärendet sparas
  och stängs i bakgrunden, och ingen `.cut.tmp` ligger kvar i mappen.
- **Spara ofta och en krasch:** avsluta Chainsaw i Aktivitetshanteraren efter en ändring, både före och efter att fem
  sekunder gått: nästa gång tas arbetskopian tillbaka (*Arbetskopian från ett avbrutet pass togs tillbaka*), aldrig
  *Konflikt*.

### Ärenden i appen och kommandona (etapp 11)

**Nytt ärende**
- Ctrl+N, *Nytt → Ärende* i pillret och `/new` öppnar panelen från höger; resten av fönstret mörkas. Esc, Avbryt, × och
  ett klick på det mörka stänger den, och nästa gång är fälten tomma.
- `/new 2026-0117` fyller i ärendenumret. Ctrl+Enter skapar.
- Ett ärende med titel och startpunkter: titelraden visar *nummer · titel*, grafen visas, och en toast säger att
  ärendet skapades. Journalen har *Titeln sattes till …* och en rad per startpunkt, och en
  TRON-startpunkt står som *Startpunkt på TRON noterad, men TRON är inte byggt än* (`app.start-point-noted`).
- *Kan öppnas av en kollega*: reglaget visar två lösenordsfält. Kortare än 12 tecken eller två olika ger ett fel och
  båda fälten töms. Ett delat ärende kan öppnas av en kollega med lösenordet (se *Ärendets kuvert*).
- Slå av reglaget, stäng panelen eller lås med Ctrl+L: lösenordsfälten är tomma när de visas igen.
- Ärendelösenordet går inte att hitta i en minnesdump efter att ärendet skapats (som för huvudnyckeln).
- Ett ärendenummer som finns ger *Det finns redan ett ärende med det numret.* och fälten står kvar.
- En tx-rad visar kedjans höjd vid blockikonen; tipset säger *Hämtat vid block N* med **kedjans hash vid den
  höjden** under, och *I block M* med tx:ens blockhash. Utan kedjans höjd syns ingen höjd alls (aldrig tx:ens block i
  dess ställe). Jämför hashen med mempool.space eller Etherscan för samma höjd.
- Ethereum: en Ethereum-adress visar saldot i ETH (avkortat till fyra decimaler med ≈ när det har fler), USDT och
  USDC om adressen har några, antal tx och aktivitet. Andra tokens visas inte i raden. Med Blockscout som svarar *Too
  many requests* ska raden vänta och försöka på nytt, inte säga att källan är trasig.
- Sanktion: med en importerad SDN-lista har en Ethereum-adress på listan (ETH, eller USDT/USDC på Ethereum)
  brickan *Sanktionerad*; tipset säger *(listad som USDT)* för en token. Skapa ärendet: adressen får etiketten
  Sanktionerad med listan som ursprung.
- Förhandshämtat används: skapa ett ärende med en Bitcoin-tx som startpunkt (djupt i kedjan) och expandera den i grafen direkt:
  tx:en hämtas inte igen (bara dess bytes och vilka som spenderar den). Journalraden säger *… används som svar där de
  inte kan ha ändrats*.
- Klistra in löptext med en SHA-256 i (*Filen har SHA-256 … enligt anteckningen*): ingen rad läggs till och inget
  hämtas; felraden säger att raden inte hämtades. En lista med kommatecken (`adress, adress`) ger en rad var.
- Skapa ärendet medan en rad hämtar: ärendet skapas först när hämtningen är klar, och journalen räknar alla råsvar.
- Lås med Ctrl+L medan ärendet skapas: ärendet får ändå de förhandshämtade råsvaren och raden i journalen.
- Utseende: inga linjer mellan raderna, hovring ger en ljusare grund och visar ×; en ny rad glider in uppifrån;
  medan en rad hämtar glider en ljusning över tre streck och snurran går runt på en svag ring (stilla med Windows
  animeringar avstängda). Med × fokuserad (Tab) tar Del bort raden.
- Hovra *Lägg till adress eller tx*: plusrutan blir grå. Utan startpunkter står brickorna ₿ Bitcoin, Ξ Ethereum och
  T TRON. Offline: utgångens bricka har röd text och en svagt röd kant.
- Reglaget *Kan öppnas av en kollega* glider över och fälten fälls in; *Upprepa* visar *Samma igen* tills man skriver,
  och igen när fältet töms. *Skapa ärende* är tydligt nedtonad utan nummer. Skuggan faller åt vänster över det mörka.

**Kommandoraden**
- `/help` listar kommandona. `/hjälp` ger *Okänt kommando*. Fel står i rött under fältet; ett lyckat kommando tömmer
  och döljer raden, ett misslyckat lämnar texten kvar.
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
  byggt än*. Annan text söks (se *Sökningen*).
- Stäng Chainsaw med ett ärende öppet (krysset): `.cut` är sparad och arbetskopian (`.work`) är borta.

**Länken `chainsaw:`**
- Prova med argumentet `Chainsaw.exe chainsaw:bitcoin/<adress>`, eller med länken registrerad (se *Länken chainsaw:*
  nedan).
- Chainsaw stängt: Chainsaw startar på låsskärmen och visar inget om länken. Efter upplåsning öppnas Nytt ärende med
  startpunkten ifylld; inget ärende skapas förrän du klickar.
- Chainsaw igång och upplåst med ett ärende öppet: kör kommandot igen. Inget nytt fönster öppnas; Chainsaws fönster kommer
  fram och adressen står i kommandoraden med *Från en länk: Enter lägger den som startpunkt i …*. Inget läggs till
  förrän du trycker Enter.
- Chainsaw låst: kör kommandot. Låsskärmen visar inget om länken; efter upplåsning visas den som ovan.
- Under förstagångsinställningen: länken väntar tills inställningen är klar.
- `Chainsaw.exe chainsaw:bitcoin/hej`, `chainsaw:monero/…` och `chainsaw:bitcoin/abc?x=1` gör ingenting.
- Två Windows-användare på samma dator (eller en annan användare med `runas`): den andras Chainsaw får inte länkarna.

### Nytt ärende (NC2a)

- [ ] Chainsaw startar utan felrutan *Provide value on 'System.Windows.StaticResourceExtension' … Line 541* (rättat
      2026-11-08), och fältens svaga platshållare syns i titeln och i raden *Lägg till*.
- [ ] Ctrl+N: panelen från höger är 520 px bred, resten mörkas. Rubriken *Nytt ärende*, *Ctrl+N* och × (Esc). Markören står
      i det stora fältet *Ärendenummer* med *t.ex. 2026-0117* svagt; ramen är i accenten medan fältet har tangentbordet.
- [ ] Skriv `2026-0117`: en grön bock i fältet och under det hänglåset och *Krypterad fil 2026-0117.cut*. Ett nummer som redan finns
      ger ingen bock utan *Ett ärende med numret finns redan* i rött. *Skapa ärende* är nedtonad tills ett nummer finns.
- [ ] *Titel, valfri* är en tyst rad med bara en linje under, som blir accent med tangentbordet.
- [ ] Utan startpunkter: listan har bara raden *+ Lägg till adress eller tx · Ctrl+V klistrar in flera*, och under den
      brickorna ₿ Bitcoin, Ξ Ethereum, TRON och *Bara adressen eller tx:en skickas.* Vid rubriken *Startpunkter* står
      *via Windows* (skärmen) eller *via <utgången>* (skölden); hovra för vad som skickas.
- [ ] Skriv en Bitcoin-adress i raden: när du slutar skriva (knappt en sekund) blir den en rad, och fältet töms. Enter gör
      det direkt. Raden skimrar (tre pulserande streck och en snurra till höger) och fylls sedan med saldo i BTC, antal
      tx och *mars 2024 → 29 okt* (eller *senast 29 okt* när adressen har fler än de nyaste tx:erna), och kuben med
      blockhöjden till höger. Hovra kuben: *Hämtat vid block …*, *Senaste tx i block …* och blockets hash.
- [ ] Jämför saldot och antalet tx med mempool.space för samma adress. Inget visas som källan inte gav.
- [ ] En Ethereum-adress visar antal tx (med `+` när det finns äldre) och aktiviteten, inget saldo (källan ger inget). En
      Bitcoin-tx visar beloppet ut, antal in och ut och dagen; en Ethereum-tx ether, antal överföringar och dagen.
- [ ] En TRON-adress får TRON-märket och *TRON hämtas inte, inte byggt än*. Skriv `hej` och Enter: en rad med streckad
      ruta, *?* och *Känns inte igen som adress eller tx*, och × syns hela tiden. *Skapa ärende* säger då vilken rad.
- [ ] Kopiera fyra adresser på var sin rad och tryck Ctrl+V i raden (eller var som helst i panelen utom i ett fält): fyra
      rader. Samma adress två gånger blir en rad. Text som inte känns igen sägs i en rad längst ner.
- [ ] Hovra en rad: den ljusnar och × kommer fram; × tar bort raden.
- [ ] En adress på den importerade sanktionslistan får den röda brickan *Sanktionerad*; hovra för listan, datumet och
      namnet. Efter *Skapa ärende* har adressen etiketten Sanktionerad i grafen.
- [ ] Offline (VPN vald och inte ansluten, eller Windows utan nät): brickan säger *Offline* i rött, raderna klockan och *Väntar på
      utgången*, och under listan en gång *Offline: hämtas när utgången är ansluten. Ärendet kan skapas ändå.* Anslut:
      raderna hämtas utan att något görs. Kontrollera i utgångens logg att inget gick ut medan den var nere.
- [ ] Skapa ärendet efter att raderna hämtats: journalen har *N råsvar som hämtades i Nytt ärende innan ärendet fanns
      förseglades i ärendet* (`app.prefetch-sealed`) och `/verify` säger att inget har ändrats.
- [ ] Avbryt efter hämtningar: ingen fil skapas i Ärenden och inget ligger kvar i `%TEMP%` eller `%LOCALAPPDATA%`.
- [ ] *Kan öppnas av en kollega*: ikonen med två personer, reglaget till höger; påslaget fäller ut *Ärendelösenord* och
      *Upprepa* (38 px, rundade) indragna under texten.
- [ ] Med Windows animeringar avstängda står skimret och snurran stilla.

### Ärendeväljaren (ÄV1a)

- [ ] Klicka på ärendet i titelraden (eller *Inget ärende öppet*): ärendeväljaren fälls ut under det med samma yta, kant och skugga som utgångarnas ruta och glider in mjukt (stilla med animeringar avstängda).
- [ ] Ctrl+O öppnar den med markören i *Sök ärendenummer*; Esc stänger den och tangentbordet går tillbaka till ärendet i titelraden.
- [ ] Ett klick utanför stänger den utan att klicket når det som ligger under; ett klick på ärendet igen fäller in den.
- [ ] Med ett ärende öppet står *Öppet nu* med *Inte kontrollerat sedan det öppnades*; *Verifiera* visar *Kontrollerat nu · inget ändrat* (eller felet i rött), och *Stäng* stänger ärendet medan rutan står kvar.
- [ ] Listan visar ärendena med det senast ändrade först, med nummer och *ändrad i dag 14:02*; det öppna har accentstreck och brickan *Öppet*.
- [ ] Sökfältet filtrerar på ärendenummer och räknaren följer med; utan träff står *Inget ärende har det numret.*
- [ ] Upp och Ned flyttar den valda raden; Enter öppnar ärendet och rutan stängs. Ett klick väljer, ett andra klick öppnar.
- [ ] *Nytt ärende … Ctrl+N* stänger rutan och öppnar panelen Nytt ärende.
- [ ] Ärendeväljaren, utgångarnas ruta och kommandoraden är aldrig öppna samtidigt, och ärendeväljaren stängs när Chainsaw låses.

### Ärendets kuvert (ÄR1b)

- [ ] Lägg en kollegas ärendefil (skapad i ett annat valv, med ärendelösenord) i mappen Ärenden och öppna den i ärendeväljaren: rutan *Öppna med ärendelösenordet* kommer i mitten med kuverten som små kort (*Ett annat valv*, *Ärendelösenord* som lyser i accent, *Organisationen* och ett streckat *Du*), och ärendeväljaren fälls in.
- [ ] *Öppna en kollegas fil …* längst ner i ärendeväljaren öppnar Windows filruta (bara `.cut`); en fil utanför mappen visas som *Från en annan plats · 4,2 MB · kopieras till Ärenden*.
- [ ] Fel lösenord: rött *Lösenordet stämmer inte. Inget har ändrats i filen.*, ingen kopia i Ärenden och det öppna ärendet står kvar. Markören står kvar i fältet.
- [ ] Rätt lösenord med *Lägg till ett kuvert för mig* ikryssad: filen kopieras till Ärenden, *Du* blir grönt och *Ditt kuvert ligger i filen. Nästa gång räcker huvudnyckeln.*; Enter eller *Till grafen* stänger. Stäng ärendet och öppna det igen: inget lösenord frågas.
- [ ] Utan krysset: ärendet öppnas och rutan säger att lösenordet behövs igen nästa gång.
- [ ] Ctrl+I, eller *Egenskaper* i ärendeväljaren, öppnar ärendets egenskaper på fliken *Kuvert*: ett kort per kuvert med flik som ett kuvert, *Du* i grönt på ditt, låset och hela fingeravtrycket i grupper (Cascadia Mono) på organisationens, streckat *Ingen delning* eller *Inget eget kuvert* när de saknas.
- [ ] *Skapa lösenord …* och *Byt …* fäller ut två fält i kortet; olika lösenord ger *Lösenorden är inte lika.*, för kort ger felet om längden; Enter eller *Spara* sparar, Esc eller *Avbryt* fäller in.
- [ ] *Ta bort* på lösenordet i en kollegas fil utan eget kuvert vägras med en förklaring; efter *Lägg till mitt kuvert* går det.
- [ ] Titeln överst ändras och sparas när fältet lämnas eller med Enter; titelraden följer med. Fliken *Information* visar filen och ärendets id.
- [ ] Varje ändring av kuverten står i journalen (`/verify` är grön efteråt).
- [ ] Krasch med en arbetskopia som inte hör till filen (öppna ärendet, döda Chainsaw i Aktivitetshanteraren, lägg tillbaka en äldre kopia av `.cut`): när ärendet öppnas kommer *<nummer> gick åt två håll* med de två grenarna, arbetskopian vald och *Spara som <nummer>-B*.
- [ ] *Filen* + *Öppna filen*: arbetskopian ligger orörd i `Ärenden\Återställda\<nummer> <tid>Z\`, och journalen säger var.
- [ ] *Arbetskopian* + *Spara som …*: ett nytt ärende `<nummer>-B.cut` öppnas med journalen fram till kraschen och en rad om vilket ärende det kom ur och vilka kuvert som inte följde med; ursprungsfilen är orörd och dess arbetskopia ligger i `Återställda`. Titelraden, ärendeväljaren och en exporterad begäran säger `-B`. Öppna de två ärendena om vartannat några gånger: `/verify` är grön för båda.
- [ ] *Öppna filen* på en skadad `.cut`: felet sägs i rutan, arbetskopian ligger kvar bredvid filen och rutan kommer tillbaka nästa gång ärendet öppnas.
- [ ] Låses Chainsaw medan en av rutorna är öppen stängs den.

### Ärenden i högar (GR8a)

- [ ] Utan öppet ärende: dra ett ärende runt *Nytt ärende* och släpp det på ett annat. Medan det dras följer en lite
      vriden bild av kortet pekaren, och ärendet eller högen under som tar emot det växer lite. De två blir en hög:
      en fyrkantig nod med två kort bakom sig, ikonen med lagren, antalet och *senast ändrad …* under namnet.
- [ ] Namnet skrivs direkt i den nya högen (*Namnge högen* i fältet). Enter eller ett klick utanför behåller det,
      Esc lämnar högen utan namn, och en hög utan namn heter *Namnlös*. Högst 40 tecken.
- [ ] Dra ett tredje ärende till högen: antalet ökar. Släpps ett ärende utanför något händer inget, och Esc under
      dragningen lägger tillbaka det.
- [ ] Ett klick på ett ärende (utan att dra) öppnar det som förut.
- [ ] Ett klick på högen: två ringar går ut från högen, ytan trycks ut lite och dimmas, högen glider till mitten och
      korten fjädrar ut runt den i samma ring som grafens samlingsnod (en ellips, upp till tolv kort, de första
      uppifrån), med tunna linjer till dem. Korten ser ut som grafens: dokumentet i en streckad fyrkant, numret och
      *ändrad …*. I ett litet fönster dras ringen in så att korten får plats.
- [ ] Med animeringar avstängda i Windows (Inställningar › Hjälpmedel › Visuella effekter) står allt direkt på sin
      plats, utan ringar eller rörelse.
- [ ] Shift-klick väljer kort (terrakottaram), raden längst ner säger *2 valda · dra ut dem tillsammans*. Dra ett valt
      kort ut på det dimmade: de valda följer med under det, högen fälls ihop och de ligger lösa runt *Nytt ärende*,
      också när de är äldre än de fyra senaste. Enter tar ut de valda på samma sätt.
- [ ] Ett kort som släpps på högen i mitten glider tillbaka till sin plats.
- [ ] Ett klick på ett kort öppnar ärendet. Esc, ett klick på högen i mitten eller på det dimmade fäller ihop högen.
- [ ] Pennan i den utfällda högen byter namn på den.
- [ ] Dra en hög och släpp den på en annan hög: den andra växer lite medan den tar emot, och de blir en hög med den
      dragnas ärenden överst och namnet från högen man släppte på (eller den dragnas, om den saknade namn). Släpp en
      hög på ett löst ärende: ärendet hamnar i högen.
- [ ] Tab når högarna som ärendena, med en terrakottaram runt den som har tangentbordet. Enter eller blanksteg fäller
      ut den, F2 skriver namnet.
- [ ] En hög som töms försvinner.
- [ ] Starta om Chainsaw: högarna står kvar med namn (`%LOCALAPPDATA%\Chainsaw\piles.json`). Ta bort en ärendefil
      ur mappen: den försvinner ur sin hög när Chainsaw listar ärendena igen.

### Länken chainsaw: (Inställningar › Länkar)

- [ ] Slå på *Öppna chainsaw:-länkar*. I `regedit` finns `HKEY_CURRENT_USER\Software\Classes\chainsaw` med
      `URL Protocol` och `shell\open\command` = `"…\Chainsaw.exe" "%1"`. Inget skrivs under `HKEY_LOCAL_MACHINE`.
- [ ] Skriv `chainsaw:bitcoin/<en adress>` i Kör (Win+R) eller i webbläsarens adressfält: Chainsaw öppnas (eller tar
      emot länken om det redan är igång) och ber om ärende.
- [ ] Slå av: nyckeln är borta, och länken öppnar inget.
- [ ] Peka länken på en annan kopia av Chainsaw (kör den andra och slå på där): raden säger att länken öppnar en annan
      Chainsaw, och strömbrytaren är av tills du slår på den igen.

## Utgångar och nät

### Utgångarna (etapp 4, Network)

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

### WireGuard-tunneln (etapp 13)

Hjälpprogrammet `chainsaw-tunnel.exe` ligger bredvid `Chainsaw.exe` (och licenserna i `licenses\chainsaw-tunnel\`).
Programmet kan också provas för sig i PowerShell med en riktig WireGuard-fil från utgångens leverantör (filen ligger
redan på disken; Chainsaw själv skriver den aldrig dit):

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
- Anslut i utgångsrutan, gör ett uppslag, och kontrollera att `chainsaw-tunnel.exe` försvinner ur
  Aktivitetshanteraren när utgången kopplas ner, när Chainsaw stängs och när Chainsaw avslutas i Aktivitetshanteraren.

### Utgången i titelraden

- Utan ansluten utgång står det *Ingen utgång* (eller *Offline*) med en grå prick och en liten pil i titelraden. Med
  en ansluten utgång: grön prick och namnet. Hovring säger vart uppslagen går. Fönstret går fortfarande att dra i
  titelraden runt knappen.

### Utgångsrutan (UT2b)

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

### Windows som utgång utan nät

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

### Offline när Windows saknar nät

- [ ] Välj *Windows* och dra ur nätet. Tomma grafens fot säger *Offline: datorn når inte internet*, startpunkten säger
      att inget hämtas förrän datorn når internet igen, och verktygstipset nämner ingen utgång.
- [ ] Grafens hörn *Offline* och en expansion som inte gick säger *datorn når inte internet*, inte *ingen utgång*.
- [ ] Nytt ärende: taggen *Offline* har samma verktygstips, och en startpunkt som väntar säger *Väntar på nätet*.
- [ ] Bevakning: huvudet säger *Väntar på nätet · inga uppslag görs* och raderna *Väntar på nätet*; bevakningen frågar
      inget förrän nätet är tillbaka.
- [ ] Med *VPN* utan ansluten utgång står det som förut: *ingen utgång ansluten* och *Väntar på utgången*.

### Återanslutning av tunneln

- [ ] Anslut en VPN-utgång, och låt datorn sova en stund. När den vaknar säger titelraden *Ansluter igen …*, och
      verktygstipset säger att tunneln föll och när nästa försök görs. Inom några sekunder efter att nätet är tillbaka
      är utgången ansluten igen utan att du gör något.
- [ ] Döda processen `chainsaw-tunnel.exe` i Aktivitetshanteraren: utgången ansluts igen efter ett par sekunder.
- [ ] Utan nät väntar Chainsaw allt längre mellan försöken (2 s, 5 s, 15 s, 30 s och sedan en gång i minuten), och
      försöker direkt när nätet kommer tillbaka.
- [ ] Koppla från utgången för hand medan Chainsaw väntar på nästa försök: inget nytt försök görs.
- [ ] En bevakning som väntade på utgången går igen av sig själv när tunneln är tillbaka.

## Inställningar och källor

### Inställningarna och Windows som utgång (etapp 19, ST1a)

**Dialogen**
- Ctrl+, och kugghjulet i pillret öppnar dialogen mitt i fönstret över den mörkade vyn, 1040 × 720 px (mindre i ett
  litet fönster), med menyn till vänster: *Den här datorn* (Nätverk, Låsning, Utseende, Länkar), *Arbetet* (Kurser),
  *Du* (Profil) och sist Om Chainsaw. Vyn bakom byter inte, och pillrets markerade ikon står kvar.
- Esc, × och ett klick på det mörka stänger den. Esc stänger först steget *Lägg till WireGuard-fil* eller formuläret
  för huvudnyckeln om något av dem är ute. Ctrl+K, Ctrl+1 … och Ctrl+L stänger den också (Ctrl+L låser).
- Med Nytt ärende öppet öppnas inte dialogen. Tab stannar inne i dialogen.
- Länkar har strömbrytaren för `chainsaw:` (se *Länken chainsaw:*).

**Nätverk: Windows eller VPN**
- Två kort, *Windows* och *VPN*; det valda har terrakottafärgad ram. Windows är förvalt i förstagångsinställningen.
- Under VPN står utgångarna som en tabell (se *Inställningarna mot 1.0*) och *Lägg till WireGuard-fil …*, som bara
  finns här och inte i rutan i titelraden.
- Välj Windows: titelraden säger *Windows* med grön prick, och grafen säger inte *Offline*. En expansion i grafen hämtar genom datorns eget nät.
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
  uppslag (titelraden *Ingen utgång*). Kontrollera med Wireshark att inget mer går ut på det
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

### Inställningarna mot 1.0 (BV4a, ST2a, FS2a och UT3b)

- **Låsning › När en bevakning slår till:** tre rader med ikon (Avisering i Windows, Ljud, Meddelande mitt på
  skärmen). Brickan blir terrakotta när sättet är på. *Lyssna* spelar mynt-swooshet, *Prova* visar kortet mitt på
  skärmen. Slå av alla tre: en gul rad säger att en träff bara syns i Bevakning.
- **Kortet mitt på skärmen:** ett litet kort med ögat och tre ringar som går ut (stilla med animeringar avstängda),
  ingenting mörkas. Det ligger överst över alla fönster, går att dra, Esc stänger och Enter tar åtgärden. *Prova*:
  *Visa* öppnar Bevakning.
- **Kortet från bakgrunden:** slå på *Meddelande mitt på skärmen*, stäng Chainsaw och kör `Chainsaw.Watcher.exe
  --test-alert`. Kortet visar *Chainsaw · Bevakning* uppe till vänster och *Öppna Chainsaw*. Med Chainsaw igång (öppet
  eller låst) står *Visa Chainsaw*, och Enter tar fram fönstret. Bara ett kort åt gången.
- **Om Chainsaw (inställningarna):** fingeravtrycket i fyra rader om fyra grupper, *Kopiera* blir *Kopierat* med en
  bock, och klistrat är det gruppat. Utan inbyggd nyckel: *Ingen nyckel* och en gul streckad varning. Lägg en rad
  `REVOKED <fingeravtryck>` i nyckelfilen: den står överstruken under *Återkallade*, hela på hovring.
- **Om Chainsaw från låsskärmen:** *Om Chainsaw · commit* i hörnet öppnar samma sida utan menyn, med raden *Visas före
  upplåsningen …*. Esc, × och ett klick på det mörka stänger den, och tangentbordet går tillbaka till fältet.
- **Förstagångens steg Utgång:** Windows är förvalt, med vägen grön (Datorn, Windows, Blockkedjor) och *Fortsätt*.
  Utan nät: rött och streckat, *Fortsätt offline*. VPN: det streckade fältet för en WireGuard-fil (släpp en fil på det
  eller klicka), sedan raden med filen, × och strömknappen; ansluten blir vägen grön med utgångens namn. Efter
  *Fortsätt* står samma val i Inställningar › Nätverk.
- **Nätverk › utgångarna som tabell:** kolumnerna Utgång, Plats och Syns som. Ett klick fäller ut raden med namn,
  land (listan) och stad, som sparas när fältet lämnas eller med Enter, och rutan *Kontroll*. *Kontrollera nu* går
  bara på en ansluten utgång och visar adressen och landet; fel land blir gult. *Ta bort utgången …* frågar först,
  och Esc ångrar.
- **Kontrollera vid anslutning:** på från början. Anslut en utgång vars land är satt till något annat än där den
  faktiskt går ut: toastern varnar en gång. Slå av: ingen fråga när den ansluts.

### Sidan Källor (KL2b) och Chainabuse

- [ ] Ctrl+, → *Källor*: en mittkolumn med Bitcoin, Ethereum, TRON och Solana (prick, namn och under det läget och
      antalet påslagna, till exempel *Snabbast · 2 av 2*; Solana säger *Inte byggt än*). Den valda raden ligger på den
      upphöjda ytan, utan färgad list i kanten. Sidan rullar inte med rubriken; mittkolumnen går hela vägen ner.
- [ ] Till höger: kedjans namn, lägets namn och tre ikoner (blixt, numrerad lista, strömbrytare). Hovra en ikon: namnet
      i fetstil och vad läget gör. Den valda ikonen står i accenten (Av i kräm).
- [ ] *ANVÄNDS*: den som används är ett upphöjt kort med prick, namn, ★, svarstiden stort (*146 ms*), adressen i
      Cascadia Mono och *Höjd 921 404* (hashen bara efter *Prova* i formuläret, inte efter provet vid start). Ingen
      stapel under kortet. Hovra kortet: brytaren och × kommer fram.
- [ ] *RESERVER efter svarstid* i Snabbast: tysta rader med prick, namn, hus för egen nod, *↓ N block efter* i gult,
      *Väntar* med klocka, *Svarar inte* i rött, och svarstiden till höger, utan staplar. Hovra en rad: adressen, brytaren
      och × kommer fram, och raden blir tydlig. Tab till brytaren visar den också.
- [ ] Stäng av en endpoint: den hamnar under *Avstängda · 1*, som är ihopfälld. Ett klick fäller ut den: streckad ram,
      tom prick, nyckelikon om den har en API-nyckel, brytaren för att slå på igen och × på hovring.
- [ ] *Prioriterad*: *RESERVER i din ordning, dra i greppet*, grepp och nummer (1, 2, 3 bland de påslagna) på kortet
      och raderna. Dra en reserv i greppet upp över kortet: den blir den som används. Alt+↑/↓ med fokus på greppet
      flyttar och fokus följer med, också förbi avstängda.
- [ ] *Av*: en streckad ruta säger *Inga uppslag för Bitcoin …*, inget kort, rubriken *ENDPOINTS används inte medan
      läget är Av*, raderna tonade. *Prova alla nu* är tonad.
- [ ] Foten: *+ Lägg till endpoint …* med streckad ram (formuläret fälls ut ovanför foten, fokus i adressen; Esc fäller
      in), *Provade hh:mm* och *Prova alla nu* med pilarna i en ring.
- [ ] *Etiketter* under kedjorna: *Chainabuse*, *Av* under namnet och tom prick. Ett klick visar namnet, raden
      *Rapporter om bedrägerier, från allmänheten och partner (TRM Labs)*, brytaren (av), *API-NYCKEL* med ett maskerat
      fält och *Spara*, och noten *En vanlig nyckel ger 10 anrop i månaden; uppslag görs när du ber om det.* Ingen knapp
      för uppslag.
- [ ] Slå på Chainabuse: raden säger *På · ingen nyckel* med gul prick. Skriv en nyckel och tryck Enter (eller *Spara*):
      fältet töms och ersätts av *•••••••••••• Sparad i valvet* med *Ta bort*; raden säger *På* med grön prick. Nyckeln
      visas aldrig igen i klartext. Kontrollera i Valvet att nyckeln ligger under tjänsten *Chainabuse*.
- [ ] *Ta bort*: nyckeln försvinner ur valvet och fältet kommer tillbaka.
- [ ] Starta om Chainsaw: Chainabuse är fortfarande på (`"AttributionProviders": [ "chainabuse" ]` i
      `%LOCALAPPDATA%\Chainsaw\settings.json`). Stäng av: listan blir tom.
- Chainabuse med en knapp: slå på Chainabuse under Inställningar › Källor › Etiketter och spara en nyckel. Välj en nod
  med en adress (Bitcoin eller Ethereum): ikonen *Fråga om rapporter* (skölden) står i ikonlisten, och R gör samma sak.
  Ett kluster, offline eller med Chainabuse av: ikonen syns inte. Svaret kommer som en toast; finns rapporter får noden
  en röd markering *Rapporterad* och etiketten står i Historik med Chainabuse som källa.
- Klicka på en ledtråd `+N tx` vid en adress med lång historik och tryck Esc medan *Läser historiken …* står: läsningen
  stoppas, grafen kommer tillbaka och en toast säger att det som redan hämtats ligger kvar i ärendet. Inget fel visas.

### Källornas endpoints (etapp 24, beteendet)

Sidans form provas under *Sidan Källor (KL2b)*.

**Provet och lägena**
- Bitcoin har mempool.space och Blockstream som förval, i läget *Snabbast*.
- *Prova alla nu*: båda provas genom utgången (kontrollera med Wireshark eller utgångens logg att uppslaget bara är
  `blocks/tip/height`, och att inget går ut på det vanliga nätkortet med VPN valt). *Provade hh:mm* visar tiden.
- Utan ansluten utgång (VPN valt): *Prova alla nu* säger *Inget provades: utgången tillåter inga uppslag nu*, och inget
  skickas. Låst Chainsaw: inget provas.
- Vid start: lås upp med en ansluten utgång (eller Windows) och kontrollera att endpointarna provas en gång; ansluter
  utgången först senare provas de då.
- *Prioriterad*: stäng av den första (brytaren) eller lägg in en som inte svarar först: expandera i grafen och se att
  nästa tas, och att den döda får röd prick.
- *Av*: grafens hörn säger *Av* (med förklaring på hovring), och en expansion visar bara det ärendet har, utan att
  något skickas.
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

### Takt, väntan och egen nod (etapp 25)

**Takt och väntan**
- Expandera en adress med många tx i grafen: uppslagen går i takt (högst fyra i sekunden per endpoint, Chainsaws eget
  förval) och fördelas mellan mempool.space och Blockstream (se i ärendets råsvar att båda förekommer). Grafen och
  dialogerna ska kännas lika snabba som förut; inget fryser medan Chainsaw väntar.
- Får en källa svara `429` (lättast med en egen proxy eller en egen Esplora bakom en hastighetsgräns): raden i *Källor*
  får den gula markeringen *Väntar* med tiden på hovring, pricken blir inte röd, och de andra endpointarna används under
  tiden. Titelraden har den gula pricken (och grafens huvud brickan *Väntar på källan*) bara medan ett uppslag
  faktiskt väntar, och markeringen försvinner när tiden gått.
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

### Bitcoin-källan (etapp 5, Chains)

Kräver en ansluten utgång (eller Windows som utgång).

- Ett uppslag mot mempool.space och ett mot blockstream.info (bas-URL i inställningarna) ger samma tx med samma
  belopp, avgift, blockhöjd och blockhash som webbplatsernas egna sidor.
- En adress med många tx bläddras sida för sida tills historiken tar slut, och antalet stämmer med webbplatsen.
- En obekräftad tx visas som obekräftad, och efter att den bekräftats visar nästa uppslag blocket.
- Utan utgång: ett ärende med hämtade tx öppnas och visar dem, och det som inte är hämtat säger *Ingen utgång är
  ansluten* utan att något går ut på nätet.
- `/verify` på ett ärende med hämtade svar är grönt, och svarens SHA-256 stämmer med `sha256sum` av samma svar hämtat
  med `curl` genom samma utgång (så länge svaret inte har ändrats).

### Kurserna (etapp 14, Pricing, och KU1a)

Kronan följer Riksbanken: Bitstamps kurs i euro räknas om med Riksbankens dagskurs för EUR/SEK. Provas med en
ansluten utgång.

- Kursen för en känd tx stämmer med Bitstamps egen graf för samma timme i euro (sista priset, `BTC/EUR`) och med
  Riksbankens kurs för euro samma dag på riksbank.se.
- **Kronorna under beloppet (KU1a):** expandera en Bitcoin-adress; linjerna får kronorna litet under beloppet, som
  Ethereums. Ett klick på beloppet väljer linjen och öppnar *Varför?* i Guidens panel: *Beloppet* (fakta, grön bock,
  tx och utgång, block med hash, blocktid i UTC och lokal tid), *Kursen BTC → EUR* (Bitstamp, timmen, *Timmens
  stängning*, hämtad och genom vilken utgång, *Svaret* med hashen i grupper), *Växelkursen EUR → SEK* (Riksbanken,
  dagen, serien, hämtad, hashen) och uträkningen. Kopiera vid tx, block och svaret lägger hela värdet i urklipp.
- Under panelens steg öppnas avsnittet *Kurser och omräkning* vid *Så ser stegen ut*.
- **Kurs saknas:** koppla ner utgången och öppna ett ärende vars linjer saknar kurs; under beloppet står **– kr** med
  ett överstruket nät, och hovring säger vilken kurs som saknas (*… är inte hämtad · Offline*). En obekräftad tx ger
  en klocka. Ett klick på **– kr** väljer bara linjen.
- **I bakgrunden:** en expansion visas direkt, också när många kurser saknas; linjerna står med **– kr** och en klocka
  och fylls i medan kurserna kommer (Riksbanken svarar högst var tolfte sekund). Anslut utgången igen efter offline:
  de saknade kurserna hämtas utan att något annat görs. Ett ärende med fler än 30 tider får kurser för alla.
- **Inställningar › Kurser:** Valuta (SEK, EUR, USD), Upplösning (Minut, Timme, Dygn), Källa för kryptokurser
  (Bitstamp), Källa för växelkursen (Riksbanken) och *Sparas i ärendet* med antalet kurser i det öppna ärendet (på
  hovring hur många svar från varje källa); utan ärende *Inget ärende öppet*.
- En tx på en lördag får fredagens kurs från Riksbanken, och stegen säger det.
- Samma kurs hämtas inte två gånger: två uppslag för samma timme ger bara ett nytt svar i ärendet.
- Utan utgång: en kurs som redan är hämtad visas ur ärendet, och en ny säger *Offline* utan att något går ut.
- Många tx på en gång (en graf med tx över flera månader) ger inga fel 429 från Riksbanken.

### Källor: fel som toast

- [ ] Gör en ändring under Inställningar › Källor som inte går att spara (till exempel med filen skrivskyddad): felet
      kommer som en röd toast nere till höger, en gång, och står inte längre som en röd rad på sidan.

## Grafen

### Den tomma grafen (GR6c)

- **Första gången** (inga ärenden i mappen): mitt på grafen står *Nytt ärende · Ctrl+N* utan puls, under
  den två rader om vad ett ärende är, och ärendets gång som fem prickar (Ärende först, i accent). Ett klick öppnar
  panelen Nytt ärende.
- **Med ärenden:** *Nytt ärende* i mitten, de fyra senast sparade ärendena runt om (nummer och *ändrad i dag 14:02*,
  *ändrad i går …* eller datum) och *Alla ärenden … · /open* sist, med prickade linjer från mitten. Ett klick öppnar
  ärendet; *Alla ärenden …* skriver `/open ` i kommandoraden med markören sist, så att Tab fyller i. Ringen krymper
  i ett litet fönster (960 × 600) men alla noder syns och krockar inte med pillret.
- **Ett ärende utan startpunkt:** *Första startpunkten* med en streckad nod (flaggan, ett fält och *Enter*),
  brickorna Bitcoin-adress, Ethereum-adress, Bitcoin-tx och Ethereum-tx, och under dem tre svaga steg
  (Expandera, Hitta uttaget, Bevaka) som förklarar sig på hovring.
- Skriv eller klistra in en adress: brickan för sorten lyser grönt. **Ctrl+V var som helst på grafen** lägger texten
  i fältet. Enter lägger till startpunkten och grafen visas; text som inte känns igen ger en röd rad.
- Raden under brickorna säger att inget hämtas förrän du expanderar, och genom vilken utgång. Utan utgång: grå prick,
  *Offline …*, och ramen blir grå; startpunkten går ändå att lägga till.
- Längst ner: *Så spårar man i Chainsaw* öppnar Guiden bredvid ytan (avsnittet med de fem stegen), och bara offline
  står *Offline* med sin prick bredvid.

### Grafen (etapp 12, GR4, GR3a och GR3c)

Förberedelse: ett ärende med en Bitcoin-adress som startpunkt och en ansluten utgång (utan tunnel: prova det som går
offline och se att det sägs ärligt). Grafen är förstavyn.

**Läsning**
- Den tomma grafen provas under *Den tomma grafen (GR6c)*. Lägg till en adress som startpunkt: grafen visar
  den som en rund grå nod (*Okänd*) med en ljus flagga uppe till vänster, blek eftersom den inte är expanderad.
- Inget hackar medan grafen läses eller expanderas: pillret, kommandoraden och fönstret svarar hela tiden.

**Expandera**
- Klicka på startpunkten: ramen blir terrakotta, resten bleknar, och inspektören fälls fram (se *Inspektören*).
  E (eller Expandera i ikonlisten vid noden) hämtar ett steg: nya noder och linjer kommer, och en toast säger *n nya
  tx i grafen*.
- Utan utgång: *Offline* står i grafens hörn; en expansion av något ärendet inte har säger varför i en toast.
  Ingenting påhittat visas.
- Överst till vänster står *Spårat x BTC från n mynt · FIFO v1* när startpunktens mynt finns i grafen.

**Bildspråket**
- Noderna är tunna, 132 × 40, med en ikon i en rund (okänd, offer, CoinJoin, mynt) eller fyrkantig (tjänst, kluster)
  ram i sortens färg, namnet (förkortad adress, eller etikettens namn) och antalet adresser för ett kluster. Ett
  kluster har en andra ram bakom sig. Ikonerna är skarpa i alla zoomnivåer (linjen 1,5 px).
- Sätt etiketter (när det går, eller i ett ärende med etiketter): en börs blir blå och fyrkantig, en mixer röd med
  streckad ram, spel gult med två spelkort. Börs med spårade pengar får en blå bock i hörnet (*Uttag funnet*), en
  sanktionerad adress med annan etikett en röd markering, en etikett från ett annat ärende en grå etikettmarkering.
- Linjerna: spårade pengar i terrakotta och tjockare ju större belopp, andras pengar tunna och grå, en obekräftad tx
  streckad. Beloppet står mitt på linjen, *0,1570 av 0,2400* när bara en del är spårad.
- Beloppsikonen i grafens hörn växlar till *Spårade belopp* (bara de spårade linjerna har belopp) och *Inga belopp*. Håll pekaren
  över en linje: beloppet visas ändå. Välj en nod: bara linjerna runt den har belopp.
- *?* nere till höger öppnar bildspråket (GR7a), en ruta på 600 px ovanför knappen: en rad per färg (*Offrens pengar*,
  *Uttag*, *Spenderas*, *Risk, slut*, *Annan kedja*, *Okänt*) med en prick och sorterna som små noder i rätt form och
  färg (Utan KYC, Mixer, CoinJoin, Swap och Annan kedja streckade; Offer, Okänd, CoinJoin och Mynt runda), under en
  tunn linje *Form*, *Markering* och *Linje · tjock är mycket* i tre kolumner, och längst ner regeln *Formen säger vad
  det är, färgen vad det betyder.* med *Guiden F1* till höger. Inga sorter radbryts och inga namn klipps (*Etikett från
  ett annat ärende* får plats). Markeringarna ser ut som i grafens hörn, de streckade linjerna har samma streck som på
  ytan. Hovring över *?* visar grafens tangenter.
- Hovra en färg, en sort, en form, en markering eller en linje: den får en mjuk yta, och raden längst ner byter till
  dess namn och förklaring i ljus text; när pekaren lämnar kommer regeln tillbaka. Stäng och öppna rutan: regeln står
  där igen. *Guiden F1* blir ljus vid hovring, har pekhand och öppnar Guiden vid *Grafen och bildspråket*.

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
- Växelns två ikoner och *Guiden F1* i bildspråket är klickbara direkt när vyn visas (inte
  tonade).
- Håll pekaren över en nod: ett verktygstips med sort, markeringar och om den är expanderad; flytta till en annan
  nod: tipset byter innehåll.
- Expandera så att en ny nod kommer i en kolumn som redan har noder på den fria ytan: den hamnar under dem, aldrig ovanpå.
- Två noder i samma kolumn med en linje mellan sig: linjen går som en liten båge ut till höger, inte genom noderna.
- Expandera ett kluster med många adresser: högst tio uppslag och högst 100 nya tx per tryck, och svaret säger att
  gränsen nåddes.

### Datalagret och spårningen (etapp 15)

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

### Spårningen (etapp 7, Tracing)

Helst tillsammans med kryptospåraren och ett ärende där svaret redan är känt.

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

### Klustringen (etapp 6, Clustering)

Helst tillsammans med kryptospåraren.

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

### Startpunkten, namnen och formulären vid noden (etapp 16, GR5 och IN1b)

Förberedelse: ett ärende med en Bitcoin-adress som startpunkt, expanderad några steg (kräver utgång, eller ett ärende
som redan har det), så att grafen har ett kluster och några okända adresser.

**Startpunkten och namnen (GR5)**
- Startpunkten har sin egen sort (rund grå om den är okänd, fyrkantig om den är ett kluster) och en liten ljus flagga
  uppe till vänster. Flaggan krockar aldrig med markeringarna uppe till höger (till exempel *Uttag funnet*). Pekaren
  över noden säger *… · Startpunkt*. Inspektören säger *… · startpunkt* under rubriken. Legenden bakom *?* har
  flaggan först bland markeringarna.
- En nod utan namn visar adressen i ljusgrått, förkortad i mitten (`bc1qdlm…37yr`) bara så mycket som behövs. Zooma: på
  alla nivåer står början och de fyra sista tecknen kvar.
- En nod med namn (sätt en etikett med namn) visar namnet i vitt och adressen under i nedtonat grått, förkortad bara när
  den inte får plats. Båda raderna ryms i noden utan att klippas.

**Etikett (L)**
- Välj en okänd adress och tryck L (eller etikettikonen i listen vid noden): formuläret fälls ut i en ruta bredvid
  noden och markören står i *Namn*. Esc stänger det utan att något sparas; L igen stänger också. Bryt upp finns bara
  på ett kluster.
- Fyll i namn, välj en sort i ikonrutnätet (sortens namn står till höger om *Sort*), välj källa (*Öppen källa* ger fältet
  *Vilken källa*), skriv gärna ett skäl och tryck Enter. Formuläret stängs, grafen ritas om och noden har sortens färg och form och
  namnet med adressen under. *Varför?* visar etiketten med ursprung och skäl (*Ur beställningen: …* för beställningen).
- Skälet är frivilligt; vid öppen källa krävs källans namn.
- Sätt etiketten *Offer* på startpunkten: den blir terrakotta och behåller flaggan.
- *Sprid inte*: sätt en etikett med *Sprid inte*, stäng ärendet och öppna ett annat med samma adress: etiketten kommer
  inte dit. Utan *Sprid inte* kommer den (*från ett annat ärende*).
- Utan operatörsprofil (om det går att prova): formuläret står kvar och säger *Det kunde inte sparas i ärendet:
  operatörsprofilen saknas …*.
- `/verify` är grönt efteråt, och journalen har en rad för etiketten.

**Bryt upp (U)**
- Välj ett kluster och tryck U: adresserna listas med heuristiken till höger (*CIO v1*; den första, som klustret lästes
  från, utan). Pekaren över en rad visar hela adressen och tx:en. Bocka för en eller flera: *n av m ut* och knappen *Bryt
  ut n* följer med. Alla kan inte bockas för (knappen tonas).
- Tryck Enter (skälet är frivilligt): grafen ritas om, och de utbrutna adresserna är inte längre i klustret. *Varför?* på klustret
  säger att användaren har brutit upp sammanslagningar. Expandera igen: samma heuristik slår inte ihop dem på nytt.
- `/verify` är grönt, och journalen har en rad per uppbrytning.

**Annan kedja (K)**
- Välj en adress som tagit emot pengar (helst en swap) och tryck K: Ethereum är valt, Litecoin och Solana är tonade med
  *Inte byggt än*. Klistra in en Ethereum-tx (`0x` och 64 hex): en grön bock visas i fältet. Klistra in 64 hex utan
  `0x`: ingen bock (det kan vara båda kedjorna); välj TRON så kommer bocken och rubriken blir *TRON-tx*. Med TRON valt
  och en tx med `0x` byts kedjan till Ethereum av sig själv.
- Kom flera mynt in till adressen listas de (största först) och ett måste väljas.
- Tryck Enter (skälet är frivilligt): en lila streckad linje går till en lila nod med kedjans namn under hörnet. Myntet erbjuds
  inte igen i formuläret.
- Fokus: efter Enter eller Esc går tangentbordet tillbaka till grafen, så att E, F, L, U och K fungerar direkt.
- Enter efter att ha klickat på en rad, en sort eller en källa sparar formuläret (klickar inte på raden igen).
- Sorten *Sanktionerad* finns inte i rutnätet (den kommer bara ur en importerad lista). Med sorten *Offer* är *Sprid
  inte* tonad: ett offer sprids aldrig.

### Inspektören (IN2b, GR5, TK1a och AN1b, 2026-11-08)

- [ ] Välj startpunkten utan namn: inspektören (420 px från början) glider in från höger och har sortens ikon i en rund ram (38 px), adressen förkortad i
      grått som rubrik och *Okänd · Bitcoin · startpunkt* under. En nod med namn har namnet i krämvitt. Ett kluster
      säger *… · 6 adresser*. *Varför?* är en rund knapp (32 px) i hörnet, med hovring, och bredvid den ett × (*Stäng ·
      Esc*) som avmarkerar noden eller linjen och stänger inspektören, som Esc och ett klick på tom yta.
- [ ] Flikarna *Fakta* och *Historik* står på en egen linje med accenten under den valda.
- [ ] *Fakta* i två kolumner: *In*, *Ut*, *Spårat in*, *Tx*, *Hopp*, *Först in* och *Senast* (*block 974 990*, med
      mellanslag mellan tusentalen). Ingen *Sort* och ingen *Kedja* bland fakta längre.
- [ ] Adressen står i full längd i en mörk ruta i Cascadia Mono, med kopiera (30 px, rundad) till höger. Kopiera lägger
      adressen i urklipp. En linje: varje txid i rutan med block och blockhash under i mindre text; fler än 12 tx ger
      *och N till* sist.
- [ ] En Ethereum-adress: *Saldo per token* med märket i en rund bricka, namnet med snöflingan när det kan frysas,
      *Utgivare: Tether* under, och till höger det som ligger kvar (*20,0000 USDT*) med SEK under (*≈ 210 kr*). Hovra
      SEK: kursen och hur det räknades. Inget kvar: beloppet är nedtonat. Utan kurs i ärendet står ingen SEK.
- [ ] Rutan om frysning är kall blå med ljus text. *Begäran om frysning …* visas inte (den går inte att göra än).
- [ ] *Historik*: runda markörer (18 px) på en tunn linje, tiden till höger i svag text, och *Ta tillbaka* eller
      *Ångra* som ett litet piller under raden. Ett klick fäller ut skälet under; pillret får accentens ram så länge.
      Det tillbakatagna står kvar överstruket och nedtonat.

### Historik, ta tillbaka och ångra (etapp 21, AN1b)

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

### Grafens rester (GR4, GR5, GR6c, TK1a, KF1b och KF2b, 2026-11-08)

- [ ] Ge en nod ett långt namn (Etikett, L): noden blir bredare så att namnet syns helt, högst ungefär 214 px, och
      adressen under står kvar förkortad i mitten. En nod utan namn är lika bred som förut.
- [ ] I hoppkolumnerna flyttas kolumnen efter en bred nod åt höger, så att beloppen fortfarande får plats mellan
      kolumnerna; linjerna går ut från den breda nodens högra kant och markeringarna sitter i dess hörn.
- [ ] Testärendet i hoppkolumner: ett hopp med fler än åtta aktörer sprids över två kolumner, och linjen till den andra
      kolumnen (CoinJoin) böjer av mellan raderna i stället för att gå rakt genom en nod i den första.
- [ ] Ett belopp i USDT eller ETH: tokenen står som en liten rund bricka (₮, Ξ) först i beloppet, i krämvitt på en
      mörk skiva, och beloppet behåller linjens färg.
- [ ] En linje med en swap: ett piller mitt på linjen med två brickor och en pil (₮ → Ξ), som linjen går in i och ut ur;
      beloppet står bredvid pillret och aldrig på det, och pillret står kvar när beloppen gallras bort.
- [ ] En graf med aktörer på två kedjor (till exempel efter *Annan kedja*): en svag rubrik i versaler (BITCOIN,
      ETHEREUM) står ovanför varje kedjas del av ytan. En graf på en kedja har inga rubriker.
- [ ] En graf med dolda OP_RETURN-utgångar: grafens hörnrad har ett överstruket öga med en prick, verktygstipset säger
      hur många som döljs. Ett klick visar dem (ögat öppnas och lyser), ett klick till döljer dem igen. Stäng och öppna
      ärendet: markeringen finns kvar, också när toasten är borta.
- [ ] Nytt ärende med en startpunkt, innan något hämtats: bredvid startpunkten står guidenoden *Expandera* med streckad
      accentram och två ringar som pulserar, en prickad linje från startpunkten och raden *Hämtar startpunktens tx och
      ritar motparterna.* under. Efter den står *Hitta uttaget* svagt med en blå bank. Hovra båda: förklaringen visas.
- [ ] Samma sak offline: raden under *Expandera* säger att expansionen inte kan hämta något förrän en utgång är ansluten.
- [ ] Klicka på *Expandera* (eller tryck E med startpunkten vald): startpunkten väljs och expanderas, och guiden
      försvinner när de första linjerna kommer. Med animeringar avstängda står ringarna still.
- [ ] Dubbelklicka på en nod: den expanderas (som E). Dubbelklick på en hög eller en ledtråd gör inget nytt, och
      dubbelklick på tom yta ger fortfarande en kommentar.
- [ ] Högerklicka på tom yta: en meny med *Kommentar … N*, *Passa in F*, *Zooma in +* och *Zooma ut −*. Varje rad gör
      det den säger; kommentaren hamnar där man högerklickade. Högerklick på en nod ger nodens meny som förut.
- [ ] Välj två aktörer (Ctrl+klick eller ruta) och tryck Z: formuläret för en egen yta står bredvid aktörerna, till höger
      om dem (eller till vänster när det inte får plats), och följer med när grafen panoreras och zoomas.
- [ ] Byt namn på eller ta bort en egen yta från dess bricka: formuläret står bredvid ytans aktörer.
- [ ] En mixers förslag med kandidater: numret (1, 2, 3) står på kandidatens nod uppe till vänster, ifyllt i accenten
      för den valda, inte mitt på linjen. En godkänd kandidat får en grön bock uppe till höger, efter nodens andra
      markeringar, och linjen förblir streckad.
- [ ] Öppna Etikett (L) på en nod, klicka i Guidens panel och tryck Esc: formuläret stängs. Esc en gång till stänger
      Guidens panel. Samma sak med Bevaka (B) och formuläret för en egen yta (Z).

### Ikonlisten vid noden och zoomen (polering 2026-11-07)

- [ ] Välj en nod och zooma ut: ikonlisten ovanför noden krymper med grafen ner till tre fjärdedelar av sin storlek, och står kvar centrerad över noden.
- [ ] Zooma ut ännu mer (under ungefär 45 %): listen försvinner; E, L, B och de andra tangenterna fungerar ändå, och listen kommer tillbaka när man zoomar in.

### Panorering i grafen (polering 2026-11-07)

- [ ] Håll höger musknapp och dra, var som helst i grafen och också över en nod: ytan följer med och pekaren blir ett fyrpilskors.
- [ ] Samma sak med mittknappen (hjulet nedtryckt).
- [ ] Släpp i farten: ytan glider vidare och saktar in mjukt; ett nytt tryck eller hjulet fångar den. Släpp stilla: den står kvar.
- [ ] Ett högerklick utan att dra öppnar nodens meny som förut, och ingen meny kommer efter en panorering.
- [ ] Vänsterknappen på tom yta panorerar inte längre; den drar noder och väljer.
- [ ] Med animeringar avstängda i Windows glider ytan inte efter släppet.
- [ ] Grafens läge (var man panorerat) finns kvar när ärendet öppnas igen.

### Mjuk zoom i grafen (2026-11-08, som i Gawk)

- [ ] Ett hack på hjulet: grafen glider mjukt in eller ut på ungefär en kvarts sekund, och punkten under pekaren står helt still hela vägen.
- [ ] Snurra snabbt flera hack: zoomen samlas till ett enda mjukt svep, utan hack eller ryck tillbaka. Byt riktning mitt i: den vänder mjukt.
- [ ] Pekplattan (två fingrar upp och ner): zoomen följer fingrarna i små steg, lika mjukt, och blir inte ryckig.
- [ ] Zooma ut och in till gränserna (20 % och 300 %): zoomen stannar mjukt vid gränsen och fortsätter inte; fler hack där gör ingenting.
- [ ] + och − zoomar mjukt runt mitten av ytan på samma sätt. F passar in direkt och avbryter en zoom som pågår.
- [ ] Släpp en panorering i farten och vrid hjulet medan ytan glider: glidningen stannar och zoomen tar över, utan att ytan hoppar.
- [ ] Tryck med vänster-, höger- eller mittknappen mitt i en zoom: zoomen stannar där den är, utan hopp.
- [ ] Ikonlisten och formulären vid den valda noden, kommentarslapparna och tidsbanden följer med i varje bildruta under zoomen.
- [ ] Med animeringar avstängda i Windows: varje hack zoomar direkt ett steg, utan glidning.
- [ ] Zooma och öppna ärendet igen: zoomnivån finns kvar (den sparas när zoomen landat, inte under den).

### Välja flera med en ruta (2026-11-08, efter användarens beskrivning)

- [ ] Tryck vänsterknappen på tom yta i grafen och dra: en tunn ruta i accentfärgen (terrakotta) med svag fyllning
      följer pekaren, åt vilket håll man än drar. Aktörer som rutan rör får accentramen direkt medan man drar, och
      släpper ramen när rutan dras tillbaka.
- [ ] Utan tangent ersätter rutan det som var valt. Med Shift läggs det till. Med Ctrl vänds det: valda blir ovalda,
      ovalda blir valda, som i Utforskaren.
- [ ] Esc medan rutan dras: rutan försvinner och valet blir som före rutan; att släppa knappen efteråt gör inget.
- [ ] Ett klick på tom yta (utan att dra) släpper valet, också det som gjorts med Ctrl+klick. Ett vanligt klick på en
      nod väljer bara den.
- [ ] Dra en av de valda aktörerna: alla valda följer med, och kommentarer som är knutna till dem följer med på den
      fria ytan. I hoppkolumnerna går de bara upp och ner i sina kolumner. Stäng och öppna ärendet: alla står kvar.
- [ ] Dra en aktör som inte är vald: bara den flyttas.
- [ ] Delete med flera valda: alla åker i soptunnan (varningen vid soptunnan visar dem om något hänger under). Dra de
      valda till soptunnan: samma sak, och de står kvar där de var om man avbryter.
- [ ] Z med flera valda: formuläret för en egen yta räknar dem.
- [ ] Dra rutan mot grafens kant: ytan rullar och rutan växer vidare utanför det som syntes.
- [ ] Inget av detta händer i valvyn eller i en utfälld hög; där fungerar ringen och korten som förut. Att dra en
      kommentar, dess tråd, en hög eller en nod fungerar som förut.

### Sortera grafen (2026-11-08, efter användarens beskrivning)

- [ ] På den fria ytan, i ett ärende med minst två aktörer, står en ikon i grafens hörn uppe till höger, efter
      växeln Fri yta · Hoppkolumner (spridda prickar, en pil och prickarna på rad). Hovring: *Sortera grafen …*.
      I hoppkolumnerna, utan ärende och med bara en aktör syns den inte.
- [ ] Dra isär grafen så att den blir stökig och klicka på ikonen: en liten ruta under hörnet frågar *Sortera om
      grafen? N aktörer flyttas från där du har lagt dem.* med *Avbryt · Esc* och *Sortera · Enter*. Inget har flyttats.
- [ ] Esc eller *Avbryt*: rutan stängs och allt står kvar. Enter eller *Sortera*: aktörerna flyttas till sina nya
      platser, grafen anpassas till skärmen och meddelandet *Grafen sorterad* med *Ångra* står en stund nere till höger.
- [ ] Den sorterade grafen är luftig, inte kompakt: pengarna går från vänster till höger i hoppens ordning, varje hopp
      i en kolumn, kolumnerna centrerade kring samma mittlinje, mottagare i höjd med sina avsändare så att linjerna
      korsar varandra så lite som möjligt, och beloppen på linjerna får plats mellan noderna. Ett hopp med många
      motparter sprids ut i flera kolumner bredvid varandra, aldrig en lång kolumn.
- [ ] En hög och en ledtrådsnod hamnar vid sin aktör. En kommentar som är knuten till en aktör följer med och står kvar
      lika långt från den; en lös kommentar står kvar där den var.
- [ ] *Ångra* i meddelandet: varje aktör och kommentar står exakt där den stod före sorteringen. Ctrl+Z ångrar inte
      sorteringen (den hör till soptunnan och förslagen).
- [ ] Stäng och öppna ärendet: den sorterade (eller ångrade) grafen står kvar. En graf som redan är sorterad: ett klick
      säger *Grafen är redan sorterad.* utan att fråga.

### Grafen använder ytan

- [ ] Med många motparter i samma hopp (under gränsen för högen, eller efter att många plockats ut) står de i flera
      kolumner sida vid sida i ett ungefär kvadratiskt block, inte i en lång kolumn; i hoppkolumnerna är hoppets band
      lika brett som blocket.
- [ ] Beloppen på linjerna hamnar aldrig ovanpå varandra: ett belopp som skulle krocka visas när pekaren är över
      linjen eller när noden är vald.

### Luft i grafen och områden bara när de väljs (TODO 34, 2026-11-08)

Förberedelse: `/testdata` i ett Debug-bygge och öppna `TEST-0001` (gör om testärendet, eftersom ett äldre kan ha sparat
platser och en vy).

- [ ] Grafen öppnas läsbar (zoom ungefär 75 % eller mer), med startpunkten uppe till vänster; resten nås genom att
      panorera. F passar in igen på samma sätt. En liten graf står mitt på ytan i naturlig storlek.
- [ ] Inga noder ligger på varandra, varken på den fria ytan eller i hoppkolumnerna. Mellan kolumnerna är det luft nog
      för de längsta beloppen (*0,689997 av 1,0000*), och raderna i en kolumn har gott om plats.
- [ ] Inget belopp ligger på ett annat belopp eller på en nod. Ett belopp som inte får plats mitt på linjen står en bit
      längre fram eller bak på den, eller precis ovanför eller under linjen.
- [ ] Pengar fram och tillbaka mellan två noder (växeltx:en `3d0c…` och `bc1q…vd0j`) ritas som två linjer bredvid
      varandra, var och en med sitt belopp, aldrig ovanpå varandra.
- [ ] Expandera navet (`bc1q…k298`): högarna och `+N tx` får plats, och beloppen krockar fortfarande inte.
- [ ] Dra en nod mitt i en solfjäder av linjer: beloppen flyttar sig undan medan du drar, utan att det hackar.
- [ ] Inga områden ritas när ärendet öppnas. Menyn bredvid *Förslag · N* visar sorterna utan bock och med antal; en bock
      visar sortens områden, och bocken bort döljer dem igen. Valet står kvar när ett annat ärende öppnas.
- [ ] Gå igenom förslagen (Ctrl+J): det visade förslagets område syns bakom dess aktörer, men inga andra.
- [ ] Rita en egen yta (välj aktörer, Z): den syns direkt, och sorten den sparades som har fått bock i menyn.

### Utgångar utan mottagare döljs (OP_RETURN)

- [ ] Expandera en adress som tillhör en pool (coinbase-tx med OP_RETURN på 0 BTC): noderna för OP_RETURN syns inte,
      och en toast säger till exempel *49 utgångar utan mottagare döljs.* med *Visa dem*. Toasten står kvar i 20 s
      (de vanliga 6 s).
- [ ] *Visa dem* visar dem i grafen, och en ny toast erbjuder *Dölj dem*. Valet finns kvar när ärendet öppnas igen.
- [ ] *Visa dem* och *Dölj dem* i ett stort ärende (tusentals noder): grafen hackar inte när de visas eller döljs, och
      ett ärende som sparat *visas* öppnar direkt med dem utan att grafen ritas två gånger.
- [ ] En OP_RETURN som bränner pengar (mer än 0 BTC) döljs aldrig.

### Ledtrådsnoden, valvyn och högen (EX1, EX2, EX2b, omgjort 2026-11-08)

- [ ] Expandera en adress med många tx (över 50): vid adressen står en streckad pill `+ 243 tx` med en tunn streckad
      linje från adressen och under pillen, litet, *äldre än 2026-03-02* (den äldsta tx som togs med).
- [ ] Ett klick på pillen: två ringar går ut från den, grafens noder flyger ut åt alla håll (de närmaste först) och
      adressen glider till mitten. Medan historiken läses står bara adressen kvar med *Läser historiken …*.
- [ ] Valvyn: tx:erna som prickar i en spiral runt adressen (nyast innerst), färgen efter motpartens sort (blått uttag,
      grått okänt, rött risk, gult spel), större prick för större belopp, fylld för ut och ihålig för in. Filterpanelen
      till vänster, raden *bc1q…x0wl · 243 tx som hölls tillbaka · visas 42 av 243* överst, knapparna uppe till höger
      och listen längst ner tonar in när prickarna har kommit ut. Grafens hörn och *?* syns inte under tiden.
- [ ] Förval: ±48 h runt den spårade ankomsten (streckad accentlinje i tidsreglaget) och *Dölj damm* på. Utan spårade
      pengar till adressen: *Allt*. Räknaren säger *42 av 243*.
- [ ] Beloppsreglaget har fördelningen bakom sig; dra ändarna och texten blir till exempel *0,01 – allt BTC*. *In*,
      *Ut*, *Alla*, tidsfönstret (dra bandet längs tiden, eller ±48 h, ±7 d, Allt), motpartens grupper, *Bara de som
      inte finns i grafen* och *Gruppera per motpart* (en prick per motpart med antalet i) gallrar som de säger.
      *Återställ* tar tillbaka förvalen. Den sista gruppen går inte att släcka.
- [ ] En motpart med röd etikett (mixer, darknet, sanktion) pulserar med röd sonar; en utbetalning strax under det
      spårade beloppet (97–100 %) pulserar i accenten. En motpart som redan är en nod har en liten ihålig ring i
      hörnet, en etikett från ett annat ärende en liten romb. Hovra en prick: en ruta säger *Ut till … · belopp*, txid
      och tid i UTC och varför den är markerad. Med animeringar avstängda i Windows står sonaren still.
- [ ] *Tidsbåge*: prickarna i en båge över adressen, vinkeln är tiden och avståndet beloppet, med streckade bågar för
      0,01, 0,1 och 1 BTC, dagar (eller månader) i ytterkanten och den spårade ankomsten som en streckad accentstråle.
      *Tid* och *Belopp* (bara i spiralen) byter ordning; spiralen säger då månader eller *störst*, *< 1 BTC* …
- [ ] Klick väljer en prick (ring i accenten), ett nytt klick släpper den; Shift-klick väljer alla mellan den förra och
      denna. *Lasso · L*: dra en ring runt prickar. *Största 10*, *Välj pulserande* (bara när något pulserar) och
      *Rensa* (bara när något är valt). Listen säger *12 valda · 4,97 BTC · 12 motparter* och *Plocka ut 12 · Enter*
      blir terrakotta.
- [ ] *Avbryt · Esc* eller Esc: grafen kommer tillbaka (noderna landar, de närmaste först). Enter eller *Plocka ut*:
      grafen kommer tillbaka och de valda tx:erna hämtas in; är de fler än åtta nya motparter på en sida hamnar de i en
      hög. Pillen säger nu *231 tx* och under den *12 plockade · äldst 2025-08-04*.
- [ ] Utan nät och utan sparad historik: ett fel säger *Det som hölls tillbaka kunde inte läsas.* med *Försök igen*,
      och grafen kommer tillbaka. Ett ärende som läst historiken förut öppnar valvyn också offline.
- [ ] En tx-expansion med fler mottagare än gränsen har också en pill; ett klick där hämtar nästa del (ingen valvy).
- [ ] Ett kluster med två expanderade adresser som båda hölls tillbaka: pillen räknar båda, och valvyn visar tx från
      båda (samma antal som pillen när inget annat i grafen tar dem). En utplockad tx hamnar vid rätt adress. Har
      klustret också en tx-expansion som hölls tillbaka står en egen pill för den.
- [ ] En tx där adressen betalar tillsammans med andra (CoinJoin): pricken har streckad ring och svag fyllning, och
      tipset säger *Flera betalade in: adressens andel, osäkert*; beloppet är adressens insats minus växeln, inte
      allas mynt. En tx med flera mottagare säger *Ut till 3 mottagare*, och *Nära det spårade* säger *skillnad* och
      tx:ens avgift.
- [ ] En obekräftad tx syns i fönstret ±48 h när det når nuet och alltid under *Allt*, och tipset säger *obekräftad*.
- [ ] En konsolidering till sig själv syns som *Till sig själv · 0,49 BTC*, inte som damm.
- [ ] Stäng ärendet medan valvyn läser: inget fel visas.
- [ ] Högen ritas som en kortlek (två kort bakom), *12 mottagare* och *4,97 BTC · 12 tx*, med en grå linje som är
      tjockare ju mer den bär och säger *4,97 BTC · 12 tx*.
- [ ] Ett klick på högen dimmar resten av grafen och korten flyger ut i en ring runt den (tolv i den inre, resten i
      yttre ringar, aldrig på varandra), med en liten studs. Varje kort har sortens ikon, namnet och *0,038 BTC · 1 tx*;
      ett kort med röd etikett har en röd prick i hörnet. Ett nytt klick på högen, Esc eller ett klick på det dimmade
      fäller ihop den.
- [ ] Klick väljer ett kort, Shift-klick fler. Dra ett kort ut på det dimmade och släpp: det blir en egen nod där, de
      andra valda under det, och högen säger *11 mottagare*. Släpps kortet på högen ligger det kvar. Enter plockar ut de
      valda på sina vanliga platser.
- [ ] Högerklick och hjulet gör inget medan högen är utfälld eller valvyn är öppen.
- [ ] Stäng ärendet och öppna det igen: de utplockade står kvar, högen och pillen med *12 plockade* finns kvar.
- [ ] Välj en utplockad nod i ett kluster: *Lägg tillbaka i högen* lägger tillbaka den, också om klustrets
      representant har bytts sedan den plockades ut.
- [ ] Verifiera ärendet efteråt: verifieringen går igenom (historiken som lästes är förseglad som andra svar).
- [ ] Bevaka en adress som ligger i en hög (eller välj den som motpart i en bevakning): den lämnar högen och står som
      egen nod med ögat, vaktlinjen och, vid en träff, ringarna; högen säger en mottagare färre. Ta bort bevakningen:
      adressen går tillbaka i högen.
- [ ] Fäll ut en hög med 50 kort och öppna valvyn med hundratals prickar: utfällningen och prickarna rör sig mjukt,
      också med många belopp på linjerna i grafen bakom.
- [ ] Flytta en hög eller en pill i ett kluster och expandera så att klustret växer (en annan adress kan bli
      representant): högen och pillen står kvar där de flyttades. Ett klick på pillens tunna linje öppnar valvyn som
      ett klick på pillen.

### Kortet, markeringarna och tidslisten (IN4a, GR9a och TL2a, 2026-11-08)

- [ ] Alt + klick på en Bitcoin-adress med pengar kvar: noden vänds runt sin lodräta axel (0,6 s) och baksidan växer
      till 260 px med en rad: en ring med ₿, beloppet med de sista fyra decimalerna dämpade och kronorna under. Foten
      säger *Bitstamp · i dag 14:00* med en grön prick, och blockhöjden med en kub till höger.
- [ ] Vänd en till nod: båda står vända samtidigt och ligger över noderna runt omkring. Alt + klick igen, eller pilen
      uppe till höger på baksidan (syns vid hovring), vänder tillbaka.
- [ ] En Ethereum-adress med ETH och USDT: två rader, ₮-raden med snöflingan *Kan frysas*.
- [ ] En tom adress: raden *Tom · sedan 29 sep 10:20* med en streckad ring och ingen kurs i foten.
- [ ] Offline (VPN vald men inte ansluten): foten har en gul klocka och *Offline · kurs 5 nov 09:14*, och kronorna är
      streckade under. Utan kurs i ärendet: *Offline · ingen kurs i ärendet* och *– kr*.
- [ ] Hovra en rad, foten och höjden: verktygstipset visar hur kronorna räknats, kursen i sin helhet och vilket block
      saldot gäller. Med animeringar avstängda vänds kortet direkt.
- [ ] En tx som bytt block (testärendets sista skalningshopp, eller ett ärende med en tx hämtad i ett grunt block):
      ett gult runt märke i beloppets övre hörn. Hovra eller klicka: rutan *Bytte block* med förut (överstruket) och nu,
      höjd, hash och när svaret kom. *?* öppnar Guiden på *Omorganisation av kedjan*.
- [ ] Alla Bitcoin-källor ber Chainsaw vänta (till exempel många expansioner i följd mot publika källor): en bricka
      med ₿ i en ring som töms och sekunderna nedräknade står i grafens huvud; hovra för källorna och hur länge.
      Noden som expanderas får ett timglas i nedre högra hörnet som andas. Ber bara en källa om väntan syns inget.
- [ ] Öppna det som hölls tillbaka (+N tx), välj ±48 h och stäng: tidslisten blir en lins där fönstret tar drygt
      halva listen med dygn och timmar, de utelämnade tx:erna som korta grå streck och ankomsten som en streckad
      accentlinje. Brickan *Valvyn · ±48 h* över linsen byter till ±7 d, ±6 h och sedan hela ärendet.
- [ ] En adress med en obekräftad tx: tillgångens rad har en liten gul klocka; hovra raden för det som väntar i mempool.
- [ ] Med utgången ansluten men kurskällan nere: foten säger *Ingen ny kurs · kurs …*, inte *Offline*.
- [ ] Listen slutar prickad vid senaste blocket, en kub med höjden under; hovra kuben för höjden och tiden.
- [ ] Zooma in i grafen tills ett dygn är brett: prickade timlinjer vid 06, 12 och 18 med timmen överst.

### Tidslinjen under grafen (TL1a)

Förberedelse: ett ärende med en graf som har bekräftade tx över flera dygn (`/testdata` i Debug, eller expandera
några steg).

- Mellan dygnen står tunna prickade linjer över hela grafen, som tonar in upptill och ut nertill. Överst i varje band
  står dagen (*3 mars*). Dra en aktör till ett annat dygns band på den fria ytan: linjen böjer sig runt den i stället
  för att flytta den. I hoppkolumnerna står linjerna i stort sett raka.
- Zooma ut i ett ärende med många dygn: dygnen slås ihop till band med flera dagar (*3 mars – 5 mars*) i stället för
  en vägg av linjer. Ett dagnamn som inte får plats i sitt band visas inte.
- Längst ner står en tunn list som tonar in och ut i ändarna, hel fram till den sista tx:en och prickad efter, med ett
  streck per tx och dagarna under. Utan en enda bekräftad tx visas varken listen eller banden.
- Hovra över ett streck: det blir ljust och högre, ett kort ovanför säger tiden och vad som flyttades
  (*bc1q…x0wl → Börs A · 0,6 BTC*), och en svag prickad linje visar var tiden går genom grafen.
- Klicka på ett streck (eller mellan två): en hel ljus linje står vid tiden med tiden i ett piller överst, och det som
  kom senare (aktörer, linjer, belopp, dagnamn och streck) bleknar. Huvudet säger *Grafen vid 5 mars 12:00 · det som
  kom senare är blekt*. I listen står en prick med en sonar som vidgas och tonar ut.
- *Till nu* bredvid listen (bara när en tid är vald) och Esc går tillbaka till grafen som den är nu.
- Stäng av animeringar i Windows (Hjälpmedel › Visuella effekter): sonaren står still som en svag ring.
- Panorera och zooma en stor graf med en vald tid: inget hackar. Öppna urvalsvyn för det som hölls tillbaka: listen
  döljs så länge den är öppen.

### Kommentarsnoden (KN1a)

- [ ] Klicka på en lapp: inspektören öppnas med lappens första rad som rubrik, *Kommentar* under, utan *Varför?*, och
      fakta om vem som skrev den, när, vem som ändrade den sist och vilken nod tråden går till. En vald nod släpps.
- [ ] Fliken *Historik* på lappen: *Kommentar skriven*, *Texten ändrad* (med början av den nya texten), *Tråd till …*,
      *Tråden lossad*, *Slängd i soptunnan* och *Lagd tillbaka ur soptunnan*, i tidsordning med vem och tid, utan
      knappar. Skriv i lappen och lämna den: rubriken och historiken följer med.
- [ ] Välj en nod medan en lapp är vald: inspektören visar noden och lappen släpps. Krysset eller Esc stänger.
- [ ] Välj en nod och tryck **N** (eller ikonen *Kommentar* i listen över noden, eller *Kommentar …* i högerklicket):
      en lapp öppnas uppe till höger om noden med en tunn prickad tråd till den och en prick vid noden. Tomma lappen är
      en streckad kontur med *Skriv … # **fet** - lista*, och markören står i den.
- [ ] Dubbelklick på tom yta (eller N utan vald nod, vid pekaren) ger en lapp utan tråd där man klickade.
- [ ] Skriv `## Rubrik`, `**fet**`, `*kursiv*`, `- punkt`, `` `kod` `` och `[tx](chainsaw:bitcoin/<adress>)`: tecknen
      är dämpade medan man skriver, rubriken större och fet, fet och kursiv syns direkt, koden i mono på mörkare grund.
      Lappen växer med texten. Foten säger *Markdown · Esc lämnar*.
- [ ] Esc, Ctrl+Enter eller ett klick utanför lämnar lappen: den ritas utan tecken (rubrik, fet, kursiv, punkter med
      •, kod och länken med prickad understrykning). En lapp som lämnas tom försvinner och inget sparas.
- [ ] Journalen har *Kommentar skriven: Rubrik*; `/verify` är grön. Ändra texten och lämna: *Kommentaren ändrades*.
      Samma text igen ger ingen ny rad. Töm en lapp och lämna den: *Kommentaren togs bort* (texten finns kvar i den
      förseglade historiken).
- [ ] Hovra en lapp eller välj den: ramen ljusnar när den är vald, foten säger vem och när (*Namn · 14:32 8 nov*) och
      handtaget syns i nedre högra hörnet. Dra handtaget: lappen blir 200–620 bred och minst 110–420 hög; texten bryts
      om och lappen blir aldrig lägre än texten. Dra lappen i ramen: den flyttas, och tråden följer.
- [ ] Panorera med höger- eller mittknappen över en lapp och zooma med hjulet: lapparna följer grafen. Högerklick i
      en lapp som skrivs i ger *Klipp ut, Kopiera, Klistra in, Markera allt*.
- [ ] Dra pricken vid nodens ände av tråden till en annan nod: tråden knyts dit (*Kommentarens tråd knöts till en
      annan nod* i journalen). Släpp den på tom yta: tråden tas bort.
- [ ] Välj en lapp (klick, sedan Esc så att den inte skrivs i) och tryck **Delete**: lappen tas bort med en journalrad.
- [ ] Klicka på länken `chainsaw:bitcoin/…` i en lapp: noden väljs och visas. En länk till något som inte finns i
      grafen säger *Det länken pekar på finns inte i grafen än.* En vanlig webbadress ritas grå och följs aldrig.
- [ ] Stäng och öppna ärendet: lapparna står kvar med text, plats, storlek och tråd. Ett kluster som byter
      representant behåller tråden.
- [ ] Medan en lapp skrivs i gör E, L, B, U, K, F, + och − ingenting i grafen; bokstäverna hamnar i lappen.
- [ ] `/testdata`: en lapp *Uttag via Nordbörsen?* står ovanför grafen med tråd till offret och en länk till
      Nordbörsens insättning.

**Skrivandet och tråden (2026-11-08)**
- [ ] Skriv `- ett` och Enter: nästa rad börjar med `- `. Enter på en tom punkt avslutar listan; på en tom inskjuten
      punkt flyttas den ut en nivå. `1. ett` och Enter ger `2. `, `> citat` och Enter ger `> `. I ett kodblock (efter
      ```` ``` ````) är Enter en vanlig ny rad. Ctrl+Z ångrar fortsättningen i ett steg.
- [ ] Tab på en punkt drar in den två steg; Shift+Tab drar ut den. Markera flera rader och tryck Tab: alla dras in.
      Tab på en vanlig rad skriver mellanslag, och Shift+Tab lämnar aldrig lappen.
- [ ] Radavståndet medan man skriver är detsamma som när lappen är ritad: lämna lappen och öppna den igen, så står
      raderna på samma höjd och texten hoppar inte. Kod och rubriker följer med.
- [ ] Rulla hjulet över en lapp som man skriver i: grafen zoomar som över en ritad lapp, och lappen rullar inte.
- [ ] Skriv en lapp på en adress som sedan hamnar i en hög (EX2b): tråden går till högen. Plocka ut adressen ur högen:
      tråden går till noden igen.
- [ ] En lapp på en adress på Ethereum behåller sin tråd när ärendet stängs och öppnas igen.

### Soptunnan (SO1a)

- Dra en nod mot soptunnan nere till vänster i grafen: ikonen växer till en rund yta, och nära blir den ljus med en
  ringvåg och *Släpp*. Släpp en ände (till exempel damm): den döljs direkt, och toasten säger *Slängde 1 aktör* med
  *Ångra · Ctrl+Z*, som lägger tillbaka den där den stod.
- Släpp en nod mitt i grafen: rutan *Det här följer med* öppnas vid soptunnan med trädet, det som följer med får en
  streckad ram och resten tonas ned. Esc stänger utan att något händer; Enter slänger alla.
- En linje till något i soptunnan blir en kort streckad stump med *i soptunnan* vid porten. Inget rött.
- Dra en nod mot soptunnan: den krymper mot pekaren ju närmare den kommer (till ungefär hälften vid soptunnan) och
  blir lite blekare när ett släpp slänger den. Dra flera valda: alla krymper tillsammans.
- Släng en mottagare som en bevakning har, och låt bevakningen slå till mot den: vid den bevakade nodens port står en
  kort hel linje i terrakotta med beloppet och ringar som rör sig tills träffen är sedd, och *i soptunnan* i änden,
  i stället för den streckade stumpen. Grå när utgången inte är ansluten.
- Välj en nod och tryck Delete: samma sak som att dra den. I en utfälld hög: Shift-klicka kort och tryck Delete.
- Släng något som är bevakat (eller har en bevakning längre ner): raden har ett öga och *Avsluta*. Med *Avsluta* på
  avslutas bevakningen (syns i vyn Bevakning); med den av fortsätter den, och soptunnans vy säger *bevakas*.
- Klicka på soptunnan: vyn visar en slängning per rad (när, vem, hur många) med det slängda överst och resten indraget
  under. *Lägg tillbaka* på en rad tar med det som ligger under; *Lägg tillbaka allt* tar hela slängningen. Esc eller
  krysset går tillbaka till grafen.
- Stäng och öppna ärendet: det slängda är fortfarande dolt. `/verify` säger att allt är orört.
- Startvyn utan ärende: lyft ett ärende, dra det mot soptunnan och släpp. Det försvinner från startsidan, toasten har
  *Ångra*, och filen ligger kvar i `%LOCALAPPDATA%\Chainsaw\Ärenden`. Soptunnans vy listar det under *Ärenden från
  startsidan* med *Lägg tillbaka*. Starta om Chainsaw: det är fortfarande dolt.
- Med animeringar avstängda i Windows står ringvågen stilla.
- Släng ett ärende på startsidan, ta bort filen och skapa ett nytt ärende med samma nummer: det nya står på
  startsidan och inte i soptunnans vy. Ett slängt ärende vars fil skrivs igen (öppnas och sparas) kommer också
  tillbaka.
- Släng en nod och byt ärende: Ctrl+Z och toastens *Ångra* lägger inget tillbaka i det nya ärendet, utan säger att
  Ångra gäller ett annat ärende.
- Släng startpunkten: en varning *Startpunkten ligger i soptunnan* med *Lägg tillbaka*. Släng allt: grafen säger att
  allt ligger i soptunnan.
- Släng en nod med en kommentarslapp: lappen försvinner med noden och kommer tillbaka med den.
- Tryck Delete två gånger snabbt på samma nod: den slängs en gång.

**Kommentarer, egna ytor och högar i soptunnan (2026-11-08)**

- [ ] Klicka på en kommentarslapp och tryck Delete: lappen och dess prickade tråd försvinner, toasten säger *Slängde 1
      kommentar* med *Ångra · Ctrl+Z*, och Ctrl+Z lägger tillbaka den där den stod med samma text.
- [ ] Dra en lapp mot soptunnan: soptunnan växer som för en nod, och ett släpp på den slänger lappen. Släppt utanför
      står lappen där den släpptes.
- [ ] Töm all text i en lapp och klicka utanför: lappen slängs (inte borttagen), och *Lägg tillbaka* i soptunnans vy ger
      tillbaka den gamla texten.
- [ ] Soptunnans vy visar lappen med lappens ikon i en fyrkant och dess första rad som namn, och raden räknar *1
      kommentar*, inte *1 aktör*. Huvudets *dolda i grafen* räknar med lappen.
- [ ] Välj en egen yta (klicka på dess bricka) och tryck Delete: ytan försvinner men aktörerna står kvar; toasten säger
      *Slängde 1 yta*, och *Lägg tillbaka* i soptunnans vy (ytans ikon) ger tillbaka den. Krysset på brickan tar
      fortfarande bort ytan med skäl, som förut.
- [ ] Stäng och öppna ärendet: slängda lappar och ytor är fortfarande dolda. `/verify` säger att allt är orört.
- [ ] Startvyn: lyft en hel hög och dra den mot soptunnan; soptunnan växer, och ett släpp slänger hela högen. Toasten
      säger *Högen … ligger i soptunnan* med *Ångra*, som ger tillbaka högen hel med namn och ordning.
- [ ] Fäll ut en hög och dra ett kort mot soptunnan (den syns över det dimmade): kortet slängs och högen fälls ihop med
      ett ärende mindre. Shift-klicka flera kort och tryck Delete: alla slängs. *Ångra* lägger tillbaka dem i högen.
- [ ] Soptunnans vy under *Ärenden från startsidan*: en slängd hög står som en rad med högens ikon, namn och antal, och
      ärendena hängda under med ├ och └. *Lägg tillbaka högen* ger tillbaka hela, *Lägg tillbaka* på ett ärende bara
      det, i högen.
- [ ] Starta om Chainsaw: en slängd hög är fortfarande dold och kommer tillbaka hel (`%LOCALAPPDATA%\Chainsaw\trash.json`).

### Ethereum: källan och startpunkterna (etapp 23)

- Ctrl+, → *Källor*: chipset **Ethereum är inte längre tonat**. Det har *Blockscout* (`https://eth.blockscout.com/api/`)
  i läget *Snabbast*. *Prova alla nu* frågar bara efter kedjans höjd (`?module=block&action=eth_block_number`) genom
  utgången; raden visar höjd och millisekunder. TRON och Solana är fortfarande tonade.
- *Lägg till endpoint …* för Ethereum: sorten *Blockscout*; *Prova* visar höjd och blockets hash (`v2/blocks/:höjd`).
  En egen Blockscout på det lokala nätet går att lägga till på samma sätt (bara https).
- Kommandoraden och Nytt ärende: en Ethereum-adress i blandade versaler med rätt kontrollsumma (EIP-55) känns igen som
  Ethereum; ändra en bokstavs versal/gemen och den känns inte igen. Samma för `chainsaw:ethereum/0x…`.
- Guiden (F1): gruppen *Ethereum* med *Kontomodellen*, *ERC-20 och tokens*, *Interna tx* och *Kan frysas*.
- Vid start provas också Ethereums endpoint (eth.blockscout.com) genom utgången (beslutet att prova vid start gäller
  alla kedjor). Sätt Ethereum på *Av* i Källor om det inte ska ske.

### Ethereum: grafen och inspektören (etapp 23b, TK1a)

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
- En Ethereum-nod säger *… · Ethereum* under rubriken, *Spårat in* per tillgång, och *Saldo per token* med utgivaren.
  Har en expanderad adress USDT eller USDC kvar står snöflingan i nodens hörn, i tabellen och i en ruta som säger att
  frysning begärs hos utgivaren. En adress som inte är expanderad säger *Inte expanderad: saldot är okänt* och får
  ingen snöflinga.
- En linje visar Belopp med märket, Värde (eller *Ingen kurs*), Tid, Tx, Swap när den finns, och varje tx med block och
  blockhash (`0x…`). *Varför?* förklarar token, swap och kursen steg för steg.
- Etikett (L) på en Ethereum-nod: sparas och syns i grafen och i Historik; ta tillbaka fungerar. Bryt upp (U) finns
  inte (inga kluster på Ethereum). Annan kedja (K): välj en rörelse som kom in till adressen (till exempel en brygga),
  välj TRON och klistra in en tx; en lila streckad linje går till den andra kedjans nod, och länken syns i Historik.
- *Förslag · N* (Ctrl+J): panelen säger att kontrollerna gäller UTXO och inte körs på Ethereum.

### Grafens rester mot 1.0 (breda noder, högen, bytt block och utgångsrutan, 2026-11-08)
- [ ] Ge några aktörer långa namn (etikett *Misstänktes plånbok* och liknande) och välj *Sortera grafen*: kolumnen
      med en bred nod flyttar nästa kolumn åt höger, så att beloppen fortfarande har luft mellan noderna och ingen nod
      ligger över en annan.
- [ ] Välj en bred nod och tryck N: den nya kommentaren står uppe till höger om nodens högra kant, inte över noden.
- [ ] Klicka på en hög i grafen: ytan bakom trycks ut lite från högen (som i startvyn) medan ringarna går ut och
      högen glider till mitten, och dras ihop igen när högen fälls ihop. Högen själv står still och skarp ovanpå.
      Med animeringar avstängda är ytan utskjuten direkt.
- [ ] En tx som källan inte längre känner (*Finns inte längre*, svårt att framkalla): linjen blir grå och streckad,
      aktörerna som bara pengarna genom den når får streckad ram och tonas ner, och linjerna ut från dem streckas.
      En aktör som också får pengar från en linje som står kvar ritas som vanligt.
- [ ] En Ethereum-tx hämtad i ett grunt block (yngre än 64 block) som sedan omorganiserats: samma gula märke
      *Bytte block* i beloppets hörn som på Bitcoin, också när den ligger kvar på samma höjd i ett annat block.
      Ett ensamt *finns inte* från en endpoint som ligger efter ger inget märke.
- [ ] Låt en VPN-tunnel falla (stäng av nätet en stund): utgångsrutan säger *Ansluter igen …* under mittringen och
      *Nästa försök kl. 14:02:31* litet under, samma tid som titelradens tips; *Försöker nu* medan ett försök pågår.
      Ringen och linjerna är fortfarande röda och streckade, eftersom inga uppslag går. Ansluten igen: utgångens namn.

## Kontroller och förslag

### Kontroller, förslag och områden (KF2b och KF1b)

Gör ett ärende med en etikett *Mixer* på en adress som tar emot pengar, och expandera några aktörer efter den, så att
mixerns kandidater, en CoinJoin eller en peel chain finns i grafen.

**Ett förslag i taget (KF2b)**
- I grafens hörn står *Förslag* med en accentbricka med antalet förslag som väntar (inte kandidater); brickan syns bara
  när något väntar. Ctrl+J och knappen fäller ut panelen där inspektören står, minst 400 px bred, och fäller in den.
- Översikten: *Gå igenom N att avgöra* i accent överst, sedan *ATT AVGÖRA* (streckade rundade rutor med ikonen) och
  *GJORT AV SIG SJÄLVT* (hela rutor). När kontrollerna kördes står i tipset på *Förslag*.
  Ett klick på en kontroll zoomar grafen till dess aktörer och avgör ingenting.
- Enter (eller knappen) börjar gå igenom: huvudet visar *1 av N* med pilarna och en tunn förloppslist (grönt avgjort,
  accent nu, svagt kvar). Förslaget visas stort: ikon i streckad ruta, titeln, raden under (aktören, *BTC in*, datum),
  bilden av hur pengarna flyter (bandens bredd är poängen, den valda kandidaten i accent), brickorna *Belopp − avgift*,
  *Tid*, *Adresstyp* med förklaring på hovring, tabellen (rang, adress, belopp, tid efter insättningen, poäng som stapel
  och tal), *Varför?* och *Mixerutgångar · v1*.
- Ett klick på en annan kandidat i tabellen väljer den: raden lyfts, bilden och knappen följer (*Godkänn 2*), och i
  grafen fylls dess nummer på linjen i accent.
- Beslutslisten står still längst ner: Avfärda X, Senare J, Godkänn N Enter, och *+ Skäl, frivilligt* som fäller ut ett
  fält (Enter i fältet godkänner). Skälet står i journalen.
- Godkänn: nästa förslag visas, förloppslisten blir grön för det avgjorda, och en toast nere till höger säger *Trolig
  utgång godkänd* med adress och belopp och *Ångra · Ctrl+Z*. Kandidatens linje i grafen är fortfarande streckad, med
  en grön bock i mitten (en bedömning, princip 2).
- Ctrl+Z (eller *Ångra* i toasten) tar tillbaka beslutet: journalen får en ny rad *Beslut om förslag ångrat*, det gamla
  står kvar, och förslaget visas igen. `/verify` är hel efteråt.
- Ctrl+Z ångrar ett beslut bara medan panelen är öppen, och alltid med en toast *Beslutet ångrat*. Med panelen
  stängd gör Ctrl+Z ingenting med förslagen. En gammal toasts *Ångra* efter ett nyare beslut om samma förslag, eller
  efter att ett annat ärende öppnats (Ctrl+O), skriver ingenting.
- Godkänn, och tryck Enter igen direkt medan grafen expanderar: samma förslag avgörs aldrig två gånger.
- Panelens *in*-belopp är insättningens utgång, och avgiften står som *trolig avgift*.
- Avfärda (X) avfärdar hela förslaget: dess område och linjer försvinner ur grafen.
- Flera godkända: godkänn kandidat 1; förslaget står kvar med kandidat 2 vald (*Godkänn 2*) och knappen heter
  *Avfärda resten*. Godkänn 2 också, eller tryck X: de som väntar avfärdas och kön går vidare. Båda godkända har en
  grön bock på sin streckade linje.
- Senare (J) lägger förslaget sist i kön och visar nästa; inget skrivs i ärendet eller journalen. K och pilen åt vänster
  går tillbaka, pilen åt höger framåt.
- *Gjort av sig självt · N* är en hopfälld rad ovanför beslutslisten; ett klick visar översikten.
- När inget väntar: *Allt avgjort* med en ring i delar (gröna godkända, svaga avfärdade), raden *1 godkänd · 1 avfärdad
  · inget nytt sedan 14:40*, och loggen *AVGJORT* (Godkänd i grönt, Avfärdad i grått, *Ångra* på hovring) och *GJORT
  AV SIG SJÄLVT* (*Markerad*). *Ångra* i loggen öppnar förslaget igen.
- Den visade förslagets aktörer syns tydligt och resten av grafen tonas ned, med bara dess område bakom; i översikten
  och i *Allt avgjort* tonas inget ned.
- Stäng och öppna ärendet: besluten står kvar; ett godkänt förslag kommer inte tillbaka i samma version.
- Inspektören visar under *Här* vilka kontroller och förslag som gäller den valda aktören.

**Delarna tillsammans och mixer efter mixer (2026-11-08)**
- Ett ärende där mixern betalar ut en insättning i två till fyra delar (var och en minst en tiondel, tillsammans
  insättningen minus några procent, inom en vecka): förslaget *Delarna tillsammans* med brickorna *Delarna
  tillsammans*, *Tid*, *Spridda block*, *Adresstyp*, en kandidat per del (*Del 1 av 3 …* i *Varför?*) och *trolig
  avgift* mot alla delar, med antalet delar. Avfärda en del: avgiften räknas om mot de andra.
- En utgång ur mixern som skickas in i en mixer igen: kontrollen *Mixer efter mixer, N steg* under *Gjort av sig
  självt*, rött mixerområde, och *Varför?* som räknar upp stegen med belopp, avgifter och om det är samma mixer igen
  eller en annan. Den nya insättningen har ett eget förslag *Troliga utgångar ur mixern*.
- Godkänn en annan kandidat i mixerns förslag som också går in i en mixer: kedjan följer den godkända och säger
  *godkänd* i stället för *trolig*. `/verify` är hel efteråt.
- *Varför?* på båda öppnar Guiden vid *Mixer efter mixer och delarna*.

**Områdena (KF1b)**
- Svaga ytor bakom aktörerna: rött för mixer och CoinJoin, sand för peel chain, grått för plånböcker, prickat för tid.
  Ett förslag som väntar har streckad kant. Ingen aktör har två fyllningar; ett inre område syns bara som kant.
- Brickan på kanten visar ikonen; namnet syns på brickan när den väljs. Ett klick på ett förslags bricka öppnar
  panelen vid förslaget; en kontrolls bricka visar bara namnet.
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
- Esc stänger formuläret, sedan menyn, sedan panelen, sedan valet av aktörer.

### Godkänna en plånbok (2026-11-08, KF2b)

- Öppna ett ärende med en tx som kan vara en CoinJoin (två inputs, två lika stora utgångar). *Förslag · N* har ett
  förslag *Samma plånbok troligt* utan kandidater, med brickorna *Samma ägare troligt* och *Din bedömning*.
- Tryck Enter (*Godkänn*), gärna med ett skäl: toasten säger *Förslaget godkänt*, grafen läses om och adresserna står
  som ett kluster. Inspektörens *Varför?* på klustret säger `user v1` och *Användaren godkände förslaget från …*, och
  *Historik* har raden *Sammanslagen av användaren* (streckad) med vem och antal adresser. Journalen har två rader:
  beslutet och sammanslagningen.
- Ctrl+Z (eller *Ångra* i loggen): klustret delas igen, förslaget väntar igen, och historiken har *Ångrad:
  sammanslagningen*.
- Godkänn igen och ångra i stället från *Historik* (*Ångra* på raden, valfritt skäl): samma resultat, och förslaget
  väntar igen i listan.
- *Bryt upp* (U) klustret: adressen går ut, uppbrytningen står i historiken som *Uppbruten · 1 ut* (med *Egen*), och den
  kan ångras så att klustret är helt igen.
- Avfärda ett plånboksförslag (X): inget slås ihop och området försvinner.
- Efter godkännandet heter raden *Godkänd plånbok* under *Avgjort*, och *Varför?* säger *Du godkände …*. Bryt upp
  klustret: raden heter *Godkänd plånbok, uppbruten*.
- Bryt ut en adress ur plånboken (U) och försök sedan godkänna ett förslag som för ihop dem igen: panelen säger att
  uppbrytningen måste ångras först, och inget sparas.
- Hämta en tx som för en ny adress till en godkänd plånbok: ett nytt förslag med bara den nya biten väntar.
- *Ångra* på *Sammanslagen av användaren* i *Historik* visar *Slås ihop igen* innan något sparas.

## Etiketter

### Etiketterna (etapp 8, Attribution)

- Importera OFAC:s riktiga SDN-lista (`sdn.xml`, sparad från OFAC:s webbplats): importen går på några sekunder,
  antalet adresser per valuta stämmer ungefär med listan, och filen ligger orörd i
  `%LOCALAPPDATA%\Chainsaw\Sanktionslistor` med sin SHA-256 i namnet. Jämför hashen med `Get-FileHash`.
- En känd sanktionerad adress (ur listan) som expanderas i ett ärende märks *Sanktionerad*, och *varför?* visar listans
  datum, hash, postens uid och program.
- En egen etikett *Börs* på en adress gör att spåret slutar där med *Börs, uttag funnet*, och *varför?* säger att det är
  en bedömning med vem, när och varför.
- Två ärenden: en etikett i det första syns i det andra som *från ett annat ärende* när samma adress finns där, med
  ärendenummer, anteckning, skäl och vem som satte den (*Allt följer med*). En etikett märkt *Sprid inte* och en
  etikett på ett offer syns inte.
- En kollegas ärendefil (öppnad med delningslösenordet) ger sina etiketter till biblioteket, och de syns sedan i egna
  ärenden med samma adresser.
- `%LOCALAPPDATA%\Chainsaw\Etiketter\bibliotek.bin` går inte att läsa i en texteditor (krypterad).
- `/verify` är grönt efter etiketter, tillbakatagningar och etiketter från andra ärenden.

### Vyn Etiketter (ET1a)


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
- En Ethereum-nod säger *… · Ethereum* under rubriken, *Spårat in* per tillgång, och *Saldo per token* med utgivaren.
  Har en expanderad adress USDT eller USDC kvar står snöflingan i nodens hörn, i tabellen och i en ruta som säger att
  frysning begärs hos utgivaren. En adress som inte är expanderad säger *Inte expanderad: saldot är okänt* och får
  ingen snöflinga.
- En linje visar Belopp med märket, Värde (eller *Ingen kurs*), Tid, Tx, Swap när den finns, och varje tx med block och
  blockhash (`0x…`). *Varför?* förklarar token, swap och kursen steg för steg.
- Etikett (L) på en Ethereum-nod: sparas och syns i grafen och i Historik; ta tillbaka fungerar. Bryt upp (U) finns
  inte (inga kluster på Ethereum). Annan kedja (K): välj en rörelse som kom in till adressen (till exempel en brygga),
  välj TRON och klistra in en tx; en lila streckad linje går till den andra kedjans nod, och länken syns i Historik.
- *Förslag · N* (Ctrl+J): statusraden säger att kontrollerna gäller UTXO och inte körs på Ethereum.

## Ethereum klart, omgång 2 (kända kontrakt, plats i blocket, egna ytor, start-tx)

Kräver en ansluten utgång och Ethereum i Källor, som ovan.

- **Tornado Cash:** expandera en adress som har satt in i en Tornado Cash-pool (till exempel 1 ETH). Poolens nod blir
  röd med mixerns ikon och etiketten *Tornado Cash 1 ETH*, och spåret tar slut där. *Varför?* på noden säger
  *Chainsaws kända kontrakt v1 2026-11-08* och sidan adressen lästes från. Journalen har en rad *… adresser är kända
  kontrakt*. Ta tillbaka etiketten i Historik och läs om grafen: den kommer inte tillbaka.
- **Tidigare sanktionerad:** samma pool har en liten, nedtonad röd markering i hörnet (inte den ljusröda för
  sanktionerad). Hovra: *Tidigare sanktionerad*. Inspektören har raden *Sanktion: Tidigare sanktionerad, OFAC SDN
  2022-08-08 – 2025-03-21*, och *Varför?* länkar OFAC:s två meddelanden. Bildspråkets *?* har raden. I Etikett (L)
  går sorten inte att välja. I testärendet (`/testdata`) har mixern markeringen.
- **Brygga:** en adress som skickat ether till Arbitrums *Delayed Inbox* eller OP Mainnets *L1 Standard Bridge*:
  noden blir lila (*Swap eller brygga*) och spåret säger *Fortsätter på annan kedja*.
- **Swap genom Uniswap:** en adress som bytt USDT mot ether genom Uniswaps router: *Varför?* på linjen med pillret
  **₮ → Ξ** säger *genom Uniswap …* med kontraktets adress och källan. En swap genom en okänd pool nämner ingen DEX.
  Routern själv får ingen etikett och ingen färg.
- **Plats i blocket:** en adress som tog emot och betalade vidare i samma block: FIFO räknar det som kom in före det
  som gick ut, i blockets ordning (inte efter tx:ens hash).
- **Egen yta (Z) med Ethereum-aktörer:** välj två Ethereum-noder (och gärna en Bitcoin-nod), tryck Z och spara: ytan
  ritas runt alla valda, också efter att ärendet öppnats igen.
- **Start-tx som inte finns i ärendet:** lägg till en Ethereum- eller Bitcoin-tx som startpunkt med kommandoraden (eller
  i Nytt ärende) med utgången ansluten: den hämtas av sig själv, och grafen ritas om med noden. Utan utgång säger en
  varning i toastern att den inte kunde hämtas och att Chainsaw försöker igen; anslut utgången (eller stäng och öppna
  ärendet) och se att den hämtas och ritas utan att du gör något. Tryck E på noden medan den hämtas: den hämtas inte
  två gånger. Lås Chainsaw medan den hämtas: inget varnar, och den hämtas när ärendet öppnas igen.

## Den tomma grafen (GR6c)

- **Första gången** (inga ärenden i mappen): mitt på grafen står *Nytt ärende · Ctrl+N* utan puls, under
  den två rader om vad ett ärende är, och ärendets gång som fem prickar (Ärende först, i accent). Ett klick öppnar
  panelen Nytt ärende.
- **Med ärenden:** *Nytt ärende* i mitten, de fyra senast sparade ärendena runt om (nummer och *ändrad i dag 14:02*,
  *ändrad i går …* eller datum) och *Alla ärenden … · /open* sist, med prickade linjer från mitten. Ett klick öppnar
  ärendet; *Alla ärenden …* skriver `/open ` i kommandoraden med markören sist, så att Tab fyller i. Ringen krymper
  i ett litet fönster (960 × 600) men alla noder syns och krockar inte med pillret.
- **Ett ärende utan startpunkt:** *Första startpunkten* med en streckad nod (flaggan, ett fält och *Enter*),
  brickorna Bitcoin-adress, Ethereum-adress, Bitcoin-tx och Ethereum-tx, och under dem tre svaga steg
  (Expandera, Hitta uttaget, Bevaka) som förklarar sig på hovring.
- Skriv eller klistra in en adress: brickan för sorten lyser grönt. **Ctrl+V var som helst på grafen** lägger texten
  i fältet. Enter lägger till startpunkten och grafen visas; text som inte känns igen ger en röd rad.
- Raden under brickorna säger att inget hämtas förrän du expanderar, och genom vilken utgång. Utan utgång: grå prick,
  *Offline …*, och ramen blir grå; startpunkten går ändå att lägga till.
- Längst ner: *Så spårar man i Chainsaw* öppnar Guiden bredvid ytan (avsnittet med de fem stegen), och bara offline
  står *Offline* med sin prick bredvid.

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
- [ ] Med en bevakning: listan, räknaren, bakgrundens läge i en rundad ram och *Ny bevakning … B* syns i huvudet, och högerpanelen visar den valda.
- [ ] Bakgrundens läge i huvudet (BV1a, omgjort 2026-11-08): en rundad ram med prick och text. Hel grå ram och grön prick när den går, streckad gul ram med gul text när den väntar på utgången, streckad grå ram med grå text när den är av; hovra för förklaringen.
- [ ] Varje rad har nodens egen ikon och färg som i grafen (en börs blå och fyrkantig, en okänd adress rund med ett frågetecken, ett kluster fyrkantigt med plånboken); en bevakning på en adress som inte finns i grafen visas som okänd eller kluster. Motparternas brickor har en liten pil för riktningen (ut till en mottagare, in från en avsändare).
- [ ] Högerpanelen: nodens ikon i en ram på 30 px bredvid namnet, läget i lägets färg (grönt, gult eller grått), och knapparna längst ner där *Öppna i grafen* är ljusare än de andra.
- [ ] Ett fel när bevakningen skriver eller läses (till exempel ett låst ärende) kommer som en röd toast nere till höger, inte som en röd rad i vyn.
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
- [ ] Efter skissen (TO1a, 2026-11-08): varje toast har ett färgat streck i nederkanten i sortens färg. För lyckat
      och information krymper det med tiden (hovring stoppar det, och det fortsätter där det stod), för fel och
      varningar står det stilla över hela bredden. Ikonen står på en rund tonad skiva i sortens färg, åtgärden
      (*Försök igen*, *Visa dem*) är i accentfärgen, och krysset är dämpat men ljusnar vid hovring.
- [ ] Toasterna står 76 px upp från nederkanten, så att bildspråkets *?* nere till höger i grafen går att klicka på
      medan en toast visas.
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
- [ ] Utan öppet ärende har pillret bara Nytt, Guide och Inställningar; öppna ett ärende och de andra vyerna
      fälls in animerat, stäng det och de fälls bort (en vy som kräver ett ärende byter då till grafen).
- [ ] Tidslinje, Flöde, Exponering, Begäran och Etiketter är nedtonade, och tipset säger *Inte byggt än*. Terminal
      finns inte längre.

## Statusraden och kommandoraden som Spotlight (polering 2026-11-07)
- [ ] Statusraden längst ner är borta; utgången står bara i titelraden, och hovring på den säger vart uppslagen går.
- [ ] När en källa ber Chainsaw vänta syns en liten gul prick till vänster om utgången i titelraden, med texten på hovring; den försvinner när väntan är över.
- [ ] Kommandoraden syns inte förrän Ctrl+K eller Ctrl+F; då kommer den som Spotlight nära överkanten, 640 px bred, med förstoringsglas och större text, utan mörkning (KR1a), och tonar in med en liten rörelse (direkt med animeringar avstängda).
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

## Ärendets kuvert (ÄR1b)
- [ ] Lägg en kollegas ärendefil (skapad i ett annat valv, med ärendelösenord) i mappen Ärenden och öppna den i ärendeväljaren: rutan *Öppna med ärendelösenordet* kommer i mitten med kuverten som små kort (*Ett annat valv*, *Ärendelösenord* som lyser i accent, *Organisationen* och ett streckat *Du*), och ärendeväljaren fälls in.
- [ ] *Öppna en kollegas fil …* längst ner i ärendeväljaren öppnar Windows filruta (bara `.cut`); en fil utanför mappen visas som *Från en annan plats · 4,2 MB · kopieras till Ärenden*.
- [ ] Fel lösenord: rött *Lösenordet stämmer inte. Inget har ändrats i filen.*, ingen kopia i Ärenden och det öppna ärendet står kvar. Markören står kvar i fältet.
- [ ] Rätt lösenord med *Lägg till ett kuvert för mig* ikryssad: filen kopieras till Ärenden, *Du* blir grönt och *Ditt kuvert ligger i filen. Nästa gång räcker huvudnyckeln.*; Enter eller *Till grafen* stänger. Stäng ärendet och öppna det igen: inget lösenord frågas.
- [ ] Utan krysset: ärendet öppnas och rutan säger att lösenordet behövs igen nästa gång.
- [ ] Ctrl+I, eller *Egenskaper* i ärendeväljaren, öppnar ärendets egenskaper på fliken *Kuvert*: ett kort per kuvert med flik som ett kuvert, *Du* i grönt på ditt, låset och hela fingeravtrycket i grupper (Cascadia Mono) på organisationens, streckat *Ingen delning* eller *Inget eget kuvert* när de saknas.
- [ ] *Skapa lösenord …* och *Byt …* fäller ut två fält i kortet; olika lösenord ger *Lösenorden är inte lika.*, för kort ger felet om längden; Enter eller *Spara* sparar, Esc eller *Avbryt* fäller in.
- [ ] *Ta bort* på lösenordet i en kollegas fil utan eget kuvert vägras med en förklaring; efter *Lägg till mitt kuvert* går det.
- [ ] Titeln överst ändras och sparas när fältet lämnas eller med Enter; titelraden följer med. Fliken *Information* visar filen och ärendets id.
- [ ] Varje ändring av kuverten står i journalen (`/verify` är grön efteråt).
- [ ] Krasch med en arbetskopia som inte hör till filen (öppna ärendet, döda Chainsaw i Aktivitetshanteraren, lägg tillbaka en äldre kopia av `.cut`): när ärendet öppnas kommer *<nummer> gick åt två håll* med de två grenarna, arbetskopian vald och *Spara som <nummer>-B*.
- [ ] *Filen* + *Öppna filen*: arbetskopian ligger orörd i `Ärenden\Återställda\<nummer> <tid>Z\`, och journalen säger var.
- [ ] *Arbetskopian* + *Spara som …*: ett nytt ärende `<nummer>-B.cut` öppnas med journalen fram till kraschen och en rad om vilket ärende det kom ur och vilka kuvert som inte följde med; ursprungsfilen är orörd och dess arbetskopia ligger i `Återställda`. Titelraden, ärendeväljaren och en exporterad begäran säger `-B`. Öppna de två ärendena om vartannat några gånger: `/verify` är grön för båda.
- [ ] *Öppna filen* på en skadad `.cut`: felet sägs i rutan, arbetskopian ligger kvar bredvid filen och rutan kommer tillbaka nästa gång ärendet öppnas.
- [ ] Låses Chainsaw medan en av rutorna är öppen stängs den.

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

## Mjuk zoom i grafen (2026-11-08, som i Gawk)
- [ ] Ett hack på hjulet: grafen glider mjukt in eller ut på ungefär en kvarts sekund, och punkten under pekaren står helt still hela vägen.
- [ ] Snurra snabbt flera hack: zoomen samlas till ett enda mjukt svep, utan hack eller ryck tillbaka. Byt riktning mitt i: den vänder mjukt.
- [ ] Pekplattan (två fingrar upp och ner): zoomen följer fingrarna i små steg, lika mjukt, och blir inte ryckig.
- [ ] Zooma ut och in till gränserna (20 % och 300 %): zoomen stannar mjukt vid gränsen och fortsätter inte; fler hack där gör ingenting.
- [ ] + och − zoomar mjukt runt mitten av ytan på samma sätt. F passar in direkt och avbryter en zoom som pågår.
- [ ] Släpp en panorering i farten och vrid hjulet medan ytan glider: glidningen stannar och zoomen tar över, utan att ytan hoppar.
- [ ] Tryck med vänster-, höger- eller mittknappen mitt i en zoom: zoomen stannar där den är, utan hopp.
- [ ] Ikonlisten och formulären vid den valda noden, kommentarslapparna och tidsbanden följer med i varje bildruta under zoomen.
- [ ] Med animeringar avstängda i Windows: varje hack zoomar direkt ett steg, utan glidning.
- [ ] Zooma och öppna ärendet igen: zoomnivån finns kvar (den sparas när zoomen landat, inte under den).

## Grafens rester (GR4, GR5, GR6c, TK1a, KF1b och KF2b, 2026-11-08)
- [ ] Ge en nod ett långt namn (Etikett, L): noden blir bredare så att namnet syns helt, högst ungefär 214 px, och
      adressen under står kvar förkortad i mitten. En nod utan namn är lika bred som förut.
- [ ] I hoppkolumnerna flyttas kolumnen efter en bred nod åt höger, så att beloppen fortfarande får plats mellan
      kolumnerna; linjerna går ut från den breda nodens högra kant och markeringarna sitter i dess hörn.
- [ ] Testärendet i hoppkolumner: ett hopp med fler än åtta aktörer sprids över två kolumner, och linjen till den andra
      kolumnen (CoinJoin) böjer av mellan raderna i stället för att gå rakt genom en nod i den första.
- [ ] Ett belopp i USDT eller ETH: tokenen står som en liten rund bricka (₮, Ξ) först i beloppet, i krämvitt på en
      mörk skiva, och beloppet behåller linjens färg.
- [ ] En linje med en swap: ett piller mitt på linjen med två brickor och en pil (₮ → Ξ), som linjen går in i och ut ur;
      beloppet står bredvid pillret och aldrig på det, och pillret står kvar när beloppen gallras bort.
- [ ] En graf med aktörer på två kedjor (till exempel efter *Annan kedja*): en svag rubrik i versaler (BITCOIN,
      ETHEREUM) står ovanför varje kedjas del av ytan. En graf på en kedja har inga rubriker.
- [ ] En graf med dolda OP_RETURN-utgångar: grafens hörnrad har ett överstruket öga med en prick, verktygstipset säger
      hur många som döljs. Ett klick visar dem (ögat öppnas och lyser), ett klick till döljer dem igen. Stäng och öppna
      ärendet: markeringen finns kvar, också när toasten är borta.
- [ ] Nytt ärende med en startpunkt, innan något hämtats: bredvid startpunkten står guidenoden *Expandera* med streckad
      accentram och två ringar som pulserar, en prickad linje från startpunkten och raden *Hämtar startpunktens tx och
      ritar motparterna.* under. Efter den står *Hitta uttaget* svagt med en blå bank. Hovra båda: förklaringen visas.
- [ ] Samma sak offline: raden under *Expandera* säger att expansionen inte kan hämta något förrän en utgång är ansluten.
- [ ] Klicka på *Expandera* (eller tryck E med startpunkten vald): startpunkten väljs och expanderas, och guiden
      försvinner när de första linjerna kommer. Med animeringar avstängda står ringarna still.
- [ ] Dubbelklicka på en nod: den expanderas (som E). Dubbelklick på en hög eller en ledtråd gör inget nytt, och
      dubbelklick på tom yta ger fortfarande en kommentar.
- [ ] Högerklicka på tom yta: en meny med *Kommentar … N*, *Passa in F*, *Zooma in +* och *Zooma ut −*. Varje rad gör
      det den säger; kommentaren hamnar där man högerklickade. Högerklick på en nod ger nodens meny som förut.
- [ ] Välj två aktörer (Ctrl+klick eller ruta) och tryck Z: formuläret för en egen yta står bredvid aktörerna, till höger
      om dem (eller till vänster när det inte får plats), och följer med när grafen panoreras och zoomas.
- [ ] Byt namn på eller ta bort en egen yta från dess bricka: formuläret står bredvid ytans aktörer.
- [ ] En mixers förslag med kandidater: numret (1, 2, 3) står på kandidatens nod uppe till vänster, ifyllt i accenten
      för den valda, inte mitt på linjen. En godkänd kandidat får en grön bock uppe till höger, efter nodens andra
      markeringar, och linjen förblir streckad.
- [ ] Öppna Etikett (L) på en nod, klicka i Guidens panel och tryck Esc: formuläret stängs. Esc en gång till stänger
      Guidens panel. Samma sak med Bevaka (B) och formuläret för en egen yta (Z).

## Kortet, markeringarna och tidslisten (IN4a, GR9a och TL2a, 2026-11-08)
- [ ] Alt + klick på en Bitcoin-adress med pengar kvar: noden vänds runt sin lodräta axel (0,6 s) och baksidan växer
      till 260 px med en rad: en ring med ₿, beloppet med de sista fyra decimalerna dämpade och kronorna under. Foten
      säger *Bitstamp · i dag 14:00* med en grön prick, och blockhöjden med en kub till höger.
- [ ] Vänd en till nod: båda står vända samtidigt och ligger över noderna runt omkring. Alt + klick igen, eller pilen
      uppe till höger på baksidan (syns vid hovring), vänder tillbaka.
- [ ] En Ethereum-adress med ETH och USDT: två rader, ₮-raden med snöflingan *Kan frysas*.
- [ ] En tom adress: raden *Tom · sedan 29 sep 10:20* med en streckad ring och ingen kurs i foten.
- [ ] Offline (VPN vald men inte ansluten): foten har en gul klocka och *Offline · kurs 5 nov 09:14*, och kronorna är
      streckade under. Utan kurs i ärendet: *Offline · ingen kurs i ärendet* och *– kr*.
- [ ] Hovra en rad, foten och höjden: verktygstipset visar hur kronorna räknats, kursen i sin helhet och vilket block
      saldot gäller. Med animeringar avstängda vänds kortet direkt.
- [ ] En tx som bytt block (svårt att framkalla; ett ärende med en tx hämtad i ett grunt block som sedan omorganiserats):
      ett gult runt märke i beloppets övre hörn. Hovra eller klicka: rutan *Bytte block* med förut (överstruket) och nu,
      höjd, hash och när svaret kom. *?* öppnar Guiden på *Omorganisation av kedjan*.
- [ ] Alla Bitcoin-källor ber Chainsaw vänta (till exempel många expansioner i följd mot publika källor): en bricka
      med ₿ i en ring som töms och sekunderna nedräknade står i grafens huvud; hovra för källorna och hur länge.
      Noden som expanderas får ett timglas i nedre högra hörnet som andas. Ber bara en källa om väntan syns inget.
- [ ] Öppna det som hölls tillbaka (+N tx), välj ±48 h och stäng: tidslisten blir en lins där fönstret tar drygt
      halva listen med dygn och timmar, de utelämnade tx:erna som korta grå streck och ankomsten som en streckad
      accentlinje. Brickan *Valvyn · ±48 h* över linsen byter till ±7 d, ±6 h och sedan hela ärendet.
- [ ] En adress med en obekräftad tx: tillgångens rad har en liten gul klocka; hovra raden för det som väntar i mempool.
- [ ] Med utgången ansluten men kurskällan nere: foten säger *Ingen ny kurs · kurs …*, inte *Offline*.
- [ ] Listen slutar prickad vid senaste blocket, en kub med höjden under; hovra kuben för höjden och tiden.
- [ ] Zooma in i grafen tills ett dygn är brett: prickade timlinjer vid 06, 12 och 18 med timmen överst.

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

## Ledtrådsnoden, valvyn och högen (EX1, EX2, EX2b, omgjort 2026-11-08)
- [ ] Expandera en adress med många tx (över 50): vid adressen står en streckad pill `+ 243 tx` med en tunn streckad
      linje från adressen och under pillen, litet, *äldre än 2026-03-02* (den äldsta tx som togs med).
- [ ] Ett klick på pillen: två ringar går ut från den, grafens noder flyger ut åt alla håll (de närmaste först) och
      adressen glider till mitten. Medan historiken läses står bara adressen kvar med *Läser historiken …*.
- [ ] Valvyn: tx:erna som prickar i en spiral runt adressen (nyast innerst), färgen efter motpartens sort (blått uttag,
      grått okänt, rött risk, gult spel), större prick för större belopp, fylld för ut och ihålig för in. Filterpanelen
      till vänster, raden *bc1q…x0wl · 243 tx som hölls tillbaka · visas 42 av 243* överst, knapparna uppe till höger
      och listen längst ner tonar in när prickarna har kommit ut. Grafens hörn och *?* syns inte under tiden.
- [ ] Förval: ±48 h runt den spårade ankomsten (streckad accentlinje i tidsreglaget) och *Dölj damm* på. Utan spårade
      pengar till adressen: *Allt*. Räknaren säger *42 av 243*.
- [ ] Beloppsreglaget har fördelningen bakom sig; dra ändarna och texten blir till exempel *0,01 – allt BTC*. *In*,
      *Ut*, *Alla*, tidsfönstret (dra bandet längs tiden, eller ±48 h, ±7 d, Allt), motpartens grupper, *Bara de som
      inte finns i grafen* och *Gruppera per motpart* (en prick per motpart med antalet i) gallrar som de säger.
      *Återställ* tar tillbaka förvalen. Den sista gruppen går inte att släcka.
- [ ] En motpart med röd etikett (mixer, darknet, sanktion) pulserar med röd sonar; en utbetalning strax under det
      spårade beloppet (97–100 %) pulserar i accenten. En motpart som redan är en nod har en liten ihålig ring i
      hörnet, en etikett från ett annat ärende en liten romb. Hovra en prick: en ruta säger *Ut till … · belopp*, txid
      och tid i UTC och varför den är markerad. Med animeringar avstängda i Windows står sonaren still.
- [ ] *Tidsbåge*: prickarna i en båge över adressen, vinkeln är tiden och avståndet beloppet, med streckade bågar för
      0,01, 0,1 och 1 BTC, dagar (eller månader) i ytterkanten och den spårade ankomsten som en streckad accentstråle.
      *Tid* och *Belopp* (bara i spiralen) byter ordning; spiralen säger då månader eller *störst*, *< 1 BTC* …
- [ ] Klick väljer en prick (ring i accenten), ett nytt klick släpper den; Shift-klick väljer alla mellan den förra och
      denna. *Lasso · L*: dra en ring runt prickar. *Största 10*, *Välj pulserande* (bara när något pulserar) och
      *Rensa* (bara när något är valt). Listen säger *12 valda · 4,97 BTC · 12 motparter* och *Plocka ut 12 · Enter*
      blir terrakotta.
- [ ] *Avbryt · Esc* eller Esc: grafen kommer tillbaka (noderna landar, de närmaste först). Enter eller *Plocka ut*:
      grafen kommer tillbaka och de valda tx:erna hämtas in; är de fler än åtta nya motparter på en sida hamnar de i en
      hög. Pillen säger nu *231 tx* och under den *12 plockade · äldst 2025-08-04*.
- [ ] Utan nät och utan sparad historik: ett fel säger *Det som hölls tillbaka kunde inte läsas.* med *Försök igen*,
      och grafen kommer tillbaka. Ett ärende som läst historiken förut öppnar valvyn också offline.
- [ ] En tx-expansion med fler mottagare än gränsen har också en pill; ett klick där hämtar nästa del (ingen valvy).
- [ ] Ett kluster med två expanderade adresser som båda hölls tillbaka: pillen räknar båda, och valvyn visar tx från
      båda (samma antal som pillen när inget annat i grafen tar dem). En utplockad tx hamnar vid rätt adress. Har
      klustret också en tx-expansion som hölls tillbaka står en egen pill för den.
- [ ] En tx där adressen betalar tillsammans med andra (CoinJoin): pricken har streckad ring och svag fyllning, och
      tipset säger *Flera betalade in: adressens andel, osäkert*; beloppet är adressens insats minus växeln, inte
      allas mynt. En tx med flera mottagare säger *Ut till 3 mottagare*, och *Nära det spårade* säger *skillnad* och
      tx:ens avgift.
- [ ] En obekräftad tx syns i fönstret ±48 h när det når nuet och alltid under *Allt*, och tipset säger *obekräftad*.
- [ ] En konsolidering till sig själv syns som *Till sig själv · 0,49 BTC*, inte som damm.
- [ ] Stäng ärendet medan valvyn läser: inget fel visas.
- [ ] Högen ritas som en kortlek (två kort bakom), *12 mottagare* och *4,97 BTC · 12 tx*, med en grå linje som är
      tjockare ju mer den bär och säger *4,97 BTC · 12 tx*.
- [ ] Ett klick på högen dimmar resten av grafen och korten flyger ut i en ring runt den (tolv i den inre, resten i
      yttre ringar, aldrig på varandra), med en liten studs. Varje kort har sortens ikon, namnet och *0,038 BTC · 1 tx*;
      ett kort med röd etikett har en röd prick i hörnet. Ett nytt klick på högen, Esc eller ett klick på det dimmade
      fäller ihop den.
- [ ] Klick väljer ett kort, Shift-klick fler. Dra ett kort ut på det dimmade och släpp: det blir en egen nod där, de
      andra valda under det, och högen säger *11 mottagare*. Släpps kortet på högen ligger det kvar. Enter plockar ut de
      valda på sina vanliga platser.
- [ ] Högerklick och hjulet gör inget medan högen är utfälld eller valvyn är öppen.
- [ ] Stäng ärendet och öppna det igen: de utplockade står kvar, högen och pillen med *12 plockade* finns kvar.
- [ ] Välj en utplockad nod i ett kluster: *Lägg tillbaka i högen* lägger tillbaka den, också om klustrets
      representant har bytts sedan den plockades ut.
- [ ] Verifiera ärendet efteråt: verifieringen går igenom (historiken som lästes är förseglad som andra svar).
- [ ] Bevaka en adress som ligger i en hög (eller välj den som motpart i en bevakning): den lämnar högen och står som
      egen nod med ögat, vaktlinjen och, vid en träff, ringarna; högen säger en mottagare färre. Ta bort bevakningen:
      adressen går tillbaka i högen.
- [ ] Fäll ut en hög med 50 kort och öppna valvyn med hundratals prickar: utfällningen och prickarna rör sig mjukt,
      också med många belopp på linjerna i grafen bakom.
- [ ] Flytta en hög eller en pill i ett kluster och expandera så att klustret växer (en annan adress kan bli
      representant): högen och pillen står kvar där de flyttades. Ett klick på pillens tunna linje öppnar valvyn som
      ett klick på pillen.

## Utgångar utan mottagare döljs (OP_RETURN)
- [ ] Expandera en adress som tillhör en pool (coinbase-tx med OP_RETURN på 0 BTC): noderna för OP_RETURN syns inte,
      och en toast säger till exempel *49 utgångar utan mottagare döljs.* med *Visa dem*. Toasten står kvar i 20 s
      (de vanliga 6 s).
- [ ] *Visa dem* visar dem i grafen, och en ny toast erbjuder *Dölj dem*. Valet finns kvar när ärendet öppnas igen.
- [ ] *Visa dem* och *Dölj dem* i ett stort ärende (tusentals noder): grafen hackar inte när de visas eller döljs, och
      ett ärende som sparat *visas* öppnar direkt med dem utan att grafen ritas två gånger.
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

## Ärenden i högar (GR8a)
- [ ] Utan öppet ärende: dra ett ärende runt *Nytt ärende* och släpp det på ett annat. Medan det dras följer en lite
      vriden bild av kortet pekaren, och ärendet eller högen under som tar emot det växer lite. De två blir en hög:
      en fyrkantig nod med två kort bakom sig, ikonen med lagren, antalet och *senast ändrad …* under namnet.
- [ ] Namnet skrivs direkt i den nya högen (*Namnge högen* i fältet). Enter eller ett klick utanför behåller det,
      Esc lämnar högen utan namn, och en hög utan namn heter *Namnlös*. Högst 40 tecken.
- [ ] Dra ett tredje ärende till högen: antalet ökar. Släpps ett ärende utanför något händer inget, och Esc under
      dragningen lägger tillbaka det.
- [ ] Ett klick på ett ärende (utan att dra) öppnar det som förut.
- [ ] Ett klick på högen: två ringar går ut från högen, ytan trycks ut lite och dimmas, högen glider till mitten och
      korten fjädrar ut runt den i samma ring som grafens samlingsnod (en ellips, upp till tolv kort, de första
      uppifrån), med tunna linjer till dem. Korten ser ut som grafens: dokumentet i en streckad fyrkant, numret och
      *ändrad …*. I ett litet fönster dras ringen in så att korten får plats.
- [ ] Med animeringar avstängda i Windows (Inställningar › Hjälpmedel › Visuella effekter) står allt direkt på sin
      plats, utan ringar eller rörelse.
- [ ] Shift-klick väljer kort (terrakottaram), raden längst ner säger *2 valda · dra ut dem tillsammans*. Dra ett valt
      kort ut på det dimmade: de valda följer med under det, högen fälls ihop och de ligger lösa runt *Nytt ärende*,
      också när de är äldre än de fyra senaste. Enter tar ut de valda på samma sätt.
- [ ] Ett kort som släpps på högen i mitten glider tillbaka till sin plats.
- [ ] Ett klick på ett kort öppnar ärendet. Esc, ett klick på högen i mitten eller på det dimmade fäller ihop högen.
- [ ] Pennan i den utfällda högen byter namn på den.
- [ ] Dra en hög och släpp den på en annan hög: den andra växer lite medan den tar emot, och de blir en hög med den
      dragnas ärenden överst och namnet från högen man släppte på (eller den dragnas, om den saknade namn). Släpp en
      hög på ett löst ärende: ärendet hamnar i högen.
- [ ] Tab når högarna som ärendena, med en terrakottaram runt den som har tangentbordet. Enter eller blanksteg fäller
      ut den, F2 skriver namnet.
- [ ] En hög som töms försvinner.
- [ ] Starta om Chainsaw: högarna står kvar med namn (`%LOCALAPPDATA%\Chainsaw\piles.json`). Ta bort en ärendefil
      ur mappen: den försvinner ur sin hög när Chainsaw listar ärendena igen.

## Polering 2026-11-08

- Öppna ett ärende utan att välja något i grafen: inspektören till höger syns inte. Välj en aktör eller linje: den fälls
  fram. Ctrl+J (Förslag) fäller också fram den. Esc eller klick på tom yta fäller bort den igen.
- Tom startpunkt: ingen puls runt rutan och ingen prickad ring kring flaggan. *Nytt ärende* i startvyn har ingen puls, inte heller
  första gången.
- Utan ärende: Graf-ikonen syns inte i pillret, och ingen dubbel skiljelinje. Startvyns fot säger inte *Utgång: …*,
  bara *Offline* när inga uppslag går.
- Osäkra (streckade) linjer: korta, fina streck oavsett hur tjock linjen är, inte grova korvar.
- Guiden bredvid arbetet (F1 eller *?*): ingen skugga över titelraden. Längst ner tonar texten ut mot panelens botten så
  länge det finns mer att läsa; vid slutet försvinner toningen.
- Ctrl+W med ett öppet ärende: ärendet sparas och stängs och startvyn visas. Utan ärende händer inget. *Stäng* i
  ärendeväljaren säger Ctrl+W i sitt tips.

## Sidan Källor (KL2b) och Chainabuse

- [ ] Ctrl+, → *Källor*: en mittkolumn med Bitcoin, Ethereum, TRON och Solana (prick, namn och under det läget och
      antalet påslagna, till exempel *Snabbast · 2 av 2*; Solana säger *Inte byggt än*). Den valda raden ligger på den
      upphöjda ytan, utan färgad list i kanten. Sidan rullar inte med rubriken; mittkolumnen går hela vägen ner.
- [ ] Till höger: kedjans namn, lägets namn och tre ikoner (blixt, numrerad lista, strömbrytare). Hovra en ikon: namnet
      i fetstil och vad läget gör. Den valda ikonen står i accenten (Av i kräm).
- [ ] *ANVÄNDS*: den som används är ett upphöjt kort med prick, namn, ★, svarstiden stort (*146 ms*), adressen i
      Cascadia Mono och *Höjd 921 404* (hashen bara efter *Prova* i formuläret, inte efter provet vid start). Ingen
      stapel under kortet. Hovra kortet: brytaren och × kommer fram.
- [ ] *RESERVER efter svarstid* i Snabbast: tysta rader med prick, namn, hus för egen nod, *↓ N block efter* i gult,
      *Väntar* med klocka, *Svarar inte* i rött, och svarstiden till höger, utan staplar. Hovra en rad: adressen, brytaren
      och × kommer fram, och raden blir tydlig. Tab till brytaren visar den också.
- [ ] Stäng av en endpoint: den hamnar under *Avstängda · 1*, som är ihopfälld. Ett klick fäller ut den: streckad ram,
      tom prick, nyckelikon om den har en API-nyckel, brytaren för att slå på igen och × på hovring.
- [ ] *Prioriterad*: *RESERVER i din ordning, dra i greppet*, grepp och nummer (1, 2, 3 bland de påslagna) på kortet
      och raderna. Dra en reserv i greppet upp över kortet: den blir den som används. Alt+↑/↓ med fokus på greppet
      flyttar och fokus följer med, också förbi avstängda.
- [ ] *Av*: en streckad ruta säger *Inga uppslag för Bitcoin …*, inget kort, rubriken *ENDPOINTS används inte medan
      läget är Av*, raderna tonade. *Prova alla nu* är tonad.
- [ ] Foten: *+ Lägg till endpoint …* med streckad ram (formuläret fälls ut ovanför foten, fokus i adressen; Esc fäller
      in), *Provade hh:mm* och *Prova alla nu* med pilarna i en ring.
- [ ] *Etiketter* under kedjorna: *Chainabuse*, *Av* under namnet och tom prick. Ett klick visar namnet, raden
      *Rapporter om bedrägerier, från allmänheten och partner (TRM Labs)*, brytaren (av), *API-NYCKEL* med ett maskerat
      fält och *Spara*, och noten *En vanlig nyckel ger 10 anrop i månaden; uppslag görs när du ber om det.* Ingen knapp
      för uppslag.
- [ ] Slå på Chainabuse: raden säger *På · ingen nyckel* med gul prick. Skriv en nyckel och tryck Enter (eller *Spara*):
      fältet töms och ersätts av *•••••••••••• Sparad i valvet* med *Ta bort*; raden säger *På* med grön prick. Nyckeln
      visas aldrig igen i klartext. Kontrollera i Valvet att nyckeln ligger under tjänsten *Chainabuse*.
- [ ] *Ta bort*: nyckeln försvinner ur valvet och fältet kommer tillbaka.
- [ ] Starta om Chainsaw: Chainabuse är fortfarande på (`"AttributionProviders": [ "chainabuse" ]` i
      `%LOCALAPPDATA%\Chainsaw\settings.json`). Stäng av: listan blir tom.
- Chainabuse med en knapp: slå på Chainabuse under Inställningar › Källor › Etiketter och spara en nyckel. Välj en nod
  med en adress (Bitcoin eller Ethereum): ikonen *Fråga om rapporter* (skölden) står i ikonlisten, och R gör samma sak.
  Ett kluster, offline eller med Chainabuse av: ikonen syns inte. Svaret kommer som en toast; finns rapporter får noden
  en röd markering *Rapporterad* och etiketten står i Historik med Chainabuse som källa.
- Klicka på en ledtråd `+N tx` vid en adress med lång historik och tryck Esc medan *Läser historiken …* står: läsningen
  stoppas, grafen kommer tillbaka och en toast säger att det som redan hämtats ligger kvar i ärendet. Inget fel visas.

## Nytt ärende NC2a (2026-11-08)

- [ ] Chainsaw startar utan felrutan *Provide value on 'System.Windows.StaticResourceExtension' … Line 541* (rättat
      2026-11-08), och fältens svaga platshållare syns i titeln och i raden *Lägg till*.
- [ ] Ctrl+N: panelen från höger är 520 px bred, resten mörkas. Rubriken *Nytt ärende*, *Ctrl+N* och × (Esc). Markören står
      i det stora fältet *Ärendenummer* med *t.ex. 2026-0117* svagt; ramen är i accenten medan fältet har tangentbordet.
- [ ] Skriv `2026-0117`: en grön bock i fältet och under det hänglåset och *Krypterad fil 2026-0117.cut*. Ett nummer som redan finns
      ger ingen bock utan *Ett ärende med numret finns redan* i rött. *Skapa ärende* är nedtonad tills ett nummer finns.
- [ ] *Titel, valfri* är en tyst rad med bara en linje under, som blir accent med tangentbordet.
- [ ] Utan startpunkter: listan har bara raden *+ Lägg till adress eller tx · Ctrl+V klistrar in flera*, och under den
      brickorna ₿ Bitcoin, Ξ Ethereum, TRON och *Bara adressen eller tx:en skickas.* Vid rubriken *Startpunkter* står
      *via Windows* (skärmen) eller *via <utgången>* (skölden); hovra för vad som skickas.
- [ ] Skriv en Bitcoin-adress i raden: när du slutar skriva (knappt en sekund) blir den en rad, och fältet töms. Enter gör
      det direkt. Raden skimrar (tre pulserande streck och en snurra till höger) och fylls sedan med saldo i BTC, antal
      tx och *mars 2024 → 29 okt* (eller *senast 29 okt* när adressen har fler än de nyaste tx:erna), och kuben med
      blockhöjden till höger. Hovra kuben: *Hämtat vid block …*, *Senaste tx i block …* och blockets hash.
- [ ] Jämför saldot och antalet tx med mempool.space för samma adress. Inget visas som källan inte gav.
- [ ] En Ethereum-adress visar antal tx (med `+` när det finns äldre) och aktiviteten, inget saldo (källan ger inget). En
      Bitcoin-tx visar beloppet ut, antal in och ut och dagen; en Ethereum-tx ether, antal överföringar och dagen.
- [ ] En TRON-adress får TRON-märket och *TRON hämtas inte, inte byggt än*. Skriv `hej` och Enter: en rad med streckad
      ruta, *?* och *Känns inte igen som adress eller tx*, och × syns hela tiden. *Skapa ärende* säger då vilken rad.
- [ ] Kopiera fyra adresser på var sin rad och tryck Ctrl+V i raden (eller var som helst i panelen utom i ett fält): fyra
      rader. Samma adress två gånger blir en rad. Text som inte känns igen sägs i en rad längst ner.
- [ ] Hovra en rad: den ljusnar och × kommer fram; × tar bort raden.
- [ ] En adress på den importerade sanktionslistan får den röda brickan *Sanktionerad*; hovra för listan, datumet och
      namnet. Efter *Skapa ärende* har adressen etiketten Sanktionerad i grafen.
- [ ] Offline (VPN vald och inte ansluten, eller Windows utan nät): brickan säger *Offline* i rött, raderna klockan och *Väntar på
      utgången*, och under listan en gång *Offline: hämtas när utgången är ansluten. Ärendet kan skapas ändå.* Anslut:
      raderna hämtas utan att något görs. Kontrollera i utgångens logg att inget gick ut medan den var nere.
- [ ] Skapa ärendet efter att raderna hämtats: journalen har *N råsvar som hämtades i Nytt ärende innan ärendet fanns
      förseglades i ärendet* (`app.prefetch-sealed`) och `/verify` säger att inget har ändrats.
- [ ] Avbryt efter hämtningar: ingen fil skapas i Ärenden och inget ligger kvar i `%TEMP%` eller `%LOCALAPPDATA%`.
- [ ] *Kan öppnas av en kollega*: ikonen med två personer, reglaget till höger; påslaget fäller ut *Ärendelösenord* och
      *Upprepa* (38 px, rundade) indragna under texten.
- [ ] Med Windows animeringar avstängda står skimret och snurran stilla.

## Luft i grafen och områden bara när de väljs (TODO 34, 2026-11-08)

Förberedelse: `/testdata` i ett Debug-bygge och öppna `TEST-0001` (gör om testärendet, eftersom ett äldre kan ha sparat
platser och en vy).

- [ ] Grafen öppnas läsbar (zoom ungefär 75 % eller mer), med startpunkten uppe till vänster; resten nås genom att
      panorera. F passar in igen på samma sätt. En liten graf står mitt på ytan i naturlig storlek.
- [ ] Inga noder ligger på varandra, varken på den fria ytan eller i hoppkolumnerna. Mellan kolumnerna är det luft nog
      för de längsta beloppen (*0,689997 av 1,0000*), och raderna i en kolumn har gott om plats.
- [ ] Inget belopp ligger på ett annat belopp eller på en nod. Ett belopp som inte får plats mitt på linjen står en bit
      längre fram eller bak på den, eller precis ovanför eller under linjen.
- [ ] Pengar fram och tillbaka mellan två noder (växeltx:en `3d0c…` och `bc1q…vd0j`) ritas som två linjer bredvid
      varandra, var och en med sitt belopp, aldrig ovanpå varandra.
- [ ] Expandera navet (`bc1q…k298`): högarna och `+N tx` får plats, och beloppen krockar fortfarande inte.
- [ ] Dra en nod mitt i en solfjäder av linjer: beloppen flyttar sig undan medan du drar, utan att det hackar.
- [ ] Inga områden ritas när ärendet öppnas. Menyn bredvid *Förslag · N* visar sorterna utan bock och med antal; en bock
      visar sortens områden, och bocken bort döljer dem igen. Valet står kvar när ett annat ärende öppnas.
- [ ] Gå igenom förslagen (Ctrl+J): det visade förslagets område syns bakom dess aktörer, men inga andra.
- [ ] Rita en egen yta (välj aktörer, Z): den syns direkt, och sorten den sparades som har fått bock i menyn.

## Tidslinjen under grafen (TL1a)

Förberedelse: ett ärende med en graf som har bekräftade tx över flera dygn (`/testdata` i Debug, eller expandera
några steg).

- Mellan dygnen står tunna prickade linjer över hela grafen, som tonar in upptill och ut nertill. Överst i varje band
  står dagen (*3 mars*). Dra en aktör till ett annat dygns band på den fria ytan: linjen böjer sig runt den i stället
  för att flytta den. I hoppkolumnerna står linjerna i stort sett raka.
- Zooma ut i ett ärende med många dygn: dygnen slås ihop till band med flera dagar (*3 mars – 5 mars*) i stället för
  en vägg av linjer. Ett dagnamn som inte får plats i sitt band visas inte.
- Längst ner står en tunn list som tonar in och ut i ändarna, hel fram till den sista tx:en och prickad efter, med ett
  streck per tx och dagarna under. Utan en enda bekräftad tx visas varken listen eller banden.
- Hovra över ett streck: det blir ljust och högre, ett kort ovanför säger tiden och vad som flyttades
  (*bc1q…x0wl → Börs A · 0,6 BTC*), och en svag prickad linje visar var tiden går genom grafen.
- Klicka på ett streck (eller mellan två): en hel ljus linje står vid tiden med tiden i ett piller överst, och det som
  kom senare (aktörer, linjer, belopp, dagnamn och streck) bleknar. Huvudet säger *Grafen vid 5 mars 12:00 · det som
  kom senare är blekt*. I listen står en prick med en sonar som vidgas och tonar ut.
- *Till nu* bredvid listen (bara när en tid är vald) och Esc går tillbaka till grafen som den är nu.
- Stäng av animeringar i Windows (Hjälpmedel › Visuella effekter): sonaren står still som en svag ring.
- Panorera och zooma en stor graf med en vald tid: inget hackar. Öppna urvalsvyn för det som hölls tillbaka: listen
  döljs så länge den är öppen.

## Inspektören (IN2b, GR5, TK1a och AN1b, 2026-11-08)

- [ ] Välj startpunkten utan namn: inspektören (420 px från början) glider in från höger och har sortens ikon i en rund ram (38 px), adressen förkortad i
      grått som rubrik och *Okänd · Bitcoin · startpunkt* under. En nod med namn har namnet i krämvitt. Ett kluster
      säger *… · 6 adresser*. *Varför?* är en rund knapp (32 px) i hörnet, med hovring, och bredvid den ett × (*Stäng ·
      Esc*) som avmarkerar noden eller linjen och stänger inspektören, som Esc och ett klick på tom yta.
- [ ] Flikarna *Fakta* och *Historik* står på en egen linje med accenten under den valda.
- [ ] *Fakta* i två kolumner: *In*, *Ut*, *Spårat in*, *Tx*, *Hopp*, *Först in* och *Senast* (*block 974 990*, med
      mellanslag mellan tusentalen). Ingen *Sort* och ingen *Kedja* bland fakta längre.
- [ ] Adressen står i full längd i en mörk ruta i Cascadia Mono, med kopiera (30 px, rundad) till höger. Kopiera lägger
      adressen i urklipp. En linje: varje txid i rutan med block och blockhash under i mindre text; fler än 12 tx ger
      *och N till* sist.
- [ ] En Ethereum-adress: *Saldo per token* med märket i en rund bricka, namnet med snöflingan när det kan frysas,
      *Utgivare: Tether* under, och till höger det som ligger kvar (*20,0000 USDT*) med SEK under (*≈ 210 kr*). Hovra
      SEK: kursen och hur det räknades. Inget kvar: beloppet är nedtonat. Utan kurs i ärendet står ingen SEK.
- [ ] Rutan om frysning är kall blå med ljus text. *Begäran om frysning …* visas inte (den går inte att göra än).
- [ ] *Historik*: runda markörer (18 px) på en tunn linje, tiden till höger i svag text, och *Ta tillbaka* eller
      *Ångra* som ett litet piller under raden. Ett klick fäller ut skälet under; pillret får accentens ram så länge.
      Det tillbakatagna står kvar överstruket och nedtonat.

## Soptunnan (SO1a)

- Dra en nod mot soptunnan nere till vänster i grafen: ikonen växer till en rund yta, och nära blir den ljus med en
  ringvåg och *Släpp*. Släpp en ände (till exempel damm): den döljs direkt, och toasten säger *Slängde 1 aktör* med
  *Ångra · Ctrl+Z*, som lägger tillbaka den där den stod.
- Släpp en nod mitt i grafen: rutan *Det här följer med* öppnas vid soptunnan med trädet, det som följer med får en
  streckad ram och resten tonas ned. Esc stänger utan att något händer; Enter slänger alla.
- En linje till något i soptunnan blir en kort streckad stump med *i soptunnan* vid porten. Inget rött.
- Dra en nod mot soptunnan: den krymper mot pekaren ju närmare den kommer (till ungefär hälften vid soptunnan) och
  blir lite blekare när ett släpp slänger den. Dra flera valda: alla krymper tillsammans.
- Släng en mottagare som en bevakning har, och låt bevakningen slå till mot den: vid den bevakade nodens port står en
  kort hel linje i terrakotta med beloppet och ringar som rör sig tills träffen är sedd, och *i soptunnan* i änden,
  i stället för den streckade stumpen. Grå när utgången inte är ansluten.
- Välj en nod och tryck Delete: samma sak som att dra den. I en utfälld hög: Shift-klicka kort och tryck Delete.
- Släng något som är bevakat (eller har en bevakning längre ner): raden har ett öga och *Avsluta*. Med *Avsluta* på
  avslutas bevakningen (syns i vyn Bevakning); med den av fortsätter den, och soptunnans vy säger *bevakas*.
- Klicka på soptunnan: vyn visar en slängning per rad (när, vem, hur många) med det slängda överst och resten indraget
  under. *Lägg tillbaka* på en rad tar med det som ligger under; *Lägg tillbaka allt* tar hela slängningen. Esc eller
  krysset går tillbaka till grafen.
- Stäng och öppna ärendet: det slängda är fortfarande dolt. `/verify` säger att allt är orört.
- Startvyn utan ärende: lyft ett ärende, dra det mot soptunnan och släpp. Det försvinner från startsidan, toasten har
  *Ångra*, och filen ligger kvar i `%LOCALAPPDATA%\Chainsaw\Ärenden`. Soptunnans vy listar det under *Ärenden från
  startsidan* med *Lägg tillbaka*. Starta om Chainsaw: det är fortfarande dolt.
- Med animeringar avstängda i Windows står ringvågen stilla.
- Släng ett ärende på startsidan, ta bort filen och skapa ett nytt ärende med samma nummer: det nya står på
  startsidan och inte i soptunnans vy. Ett slängt ärende vars fil skrivs igen (öppnas och sparas) kommer också
  tillbaka.
- Släng en nod och byt ärende: Ctrl+Z och toastens *Ångra* lägger inget tillbaka i det nya ärendet, utan säger att
  Ångra gäller ett annat ärende.
- Släng startpunkten: en varning *Startpunkten ligger i soptunnan* med *Lägg tillbaka*. Släng allt: grafen säger att
  allt ligger i soptunnan.
- Släng en nod med en kommentarslapp: lappen försvinner med noden och kommer tillbaka med den.
- Tryck Delete två gånger snabbt på samma nod: den slängs en gång.

### Kommentarer, egna ytor och högar i soptunnan (2026-11-08)

- [ ] Klicka på en kommentarslapp och tryck Delete: lappen och dess prickade tråd försvinner, toasten säger *Slängde 1
      kommentar* med *Ångra · Ctrl+Z*, och Ctrl+Z lägger tillbaka den där den stod med samma text.
- [ ] Dra en lapp mot soptunnan: soptunnan växer som för en nod, och ett släpp på den slänger lappen. Släppt utanför
      står lappen där den släpptes.
- [ ] Töm all text i en lapp och klicka utanför: lappen slängs (inte borttagen), och *Lägg tillbaka* i soptunnans vy ger
      tillbaka den gamla texten.
- [ ] Soptunnans vy visar lappen med lappens ikon i en fyrkant och dess första rad som namn, och raden räknar *1
      kommentar*, inte *1 aktör*. Huvudets *dolda i grafen* räknar med lappen.
- [ ] Välj en egen yta (klicka på dess bricka) och tryck Delete: ytan försvinner men aktörerna står kvar; toasten säger
      *Slängde 1 yta*, och *Lägg tillbaka* i soptunnans vy (ytans ikon) ger tillbaka den. Krysset på brickan tar
      fortfarande bort ytan med skäl, som förut.
- [ ] Stäng och öppna ärendet: slängda lappar och ytor är fortfarande dolda. `/verify` säger att allt är orört.
- [ ] Startvyn: lyft en hel hög och dra den mot soptunnan; soptunnan växer, och ett släpp slänger hela högen. Toasten
      säger *Högen … ligger i soptunnan* med *Ångra*, som ger tillbaka högen hel med namn och ordning.
- [ ] Fäll ut en hög och dra ett kort mot soptunnan (den syns över det dimmade): kortet slängs och högen fälls ihop med
      ett ärende mindre. Shift-klicka flera kort och tryck Delete: alla slängs. *Ångra* lägger tillbaka dem i högen.
- [ ] Soptunnans vy under *Ärenden från startsidan*: en slängd hög står som en rad med högens ikon, namn och antal, och
      ärendena hängda under med ├ och └. *Lägg tillbaka högen* ger tillbaka hela, *Lägg tillbaka* på ett ärende bara
      det, i högen.
- [ ] Starta om Chainsaw: en slängd hög är fortfarande dold och kommer tillbaka hel (`%LOCALAPPDATA%\Chainsaw\trash.json`).

## Kommentarsnoden (KN1a)
- [ ] Klicka på en lapp: inspektören öppnas med lappens första rad som rubrik, *Kommentar* under, utan *Varför?*, och
      fakta om vem som skrev den, när, vem som ändrade den sist och vilken nod tråden går till. En vald nod släpps.
- [ ] Fliken *Historik* på lappen: *Kommentar skriven*, *Texten ändrad* (med början av den nya texten), *Tråd till …*,
      *Tråden lossad*, *Slängd i soptunnan* och *Lagd tillbaka ur soptunnan*, i tidsordning med vem och tid, utan
      knappar. Skriv i lappen och lämna den: rubriken och historiken följer med.
- [ ] Välj en nod medan en lapp är vald: inspektören visar noden och lappen släpps. Krysset eller Esc stänger.
- [ ] Välj en nod och tryck **N** (eller ikonen *Kommentar* i listen över noden, eller *Kommentar …* i högerklicket):
      en lapp öppnas uppe till höger om noden med en tunn prickad tråd till den och en prick vid noden. Tomma lappen är
      en streckad kontur med *Skriv … # **fet** - lista*, och markören står i den.
- [ ] Dubbelklick på tom yta (eller N utan vald nod, vid pekaren) ger en lapp utan tråd där man klickade.
- [ ] Skriv `## Rubrik`, `**fet**`, `*kursiv*`, `- punkt`, `` `kod` `` och `[tx](chainsaw:bitcoin/<adress>)`: tecknen
      är dämpade medan man skriver, rubriken större och fet, fet och kursiv syns direkt, koden i mono på mörkare grund.
      Lappen växer med texten. Foten säger *Markdown · Esc lämnar*.
- [ ] Esc, Ctrl+Enter eller ett klick utanför lämnar lappen: den ritas utan tecken (rubrik, fet, kursiv, punkter med
      •, kod och länken med prickad understrykning). En lapp som lämnas tom försvinner och inget sparas.
- [ ] Journalen har *Kommentar skriven: Rubrik*; `/verify` är grön. Ändra texten och lämna: *Kommentaren ändrades*.
      Samma text igen ger ingen ny rad. Töm en lapp och lämna den: *Kommentaren togs bort* (texten finns kvar i den
      förseglade historiken).
- [ ] Hovra en lapp eller välj den: ramen ljusnar när den är vald, foten säger vem och när (*Namn · 14:32 8 nov*) och
      handtaget syns i nedre högra hörnet. Dra handtaget: lappen blir 200–620 bred och minst 110–420 hög; texten bryts
      om och lappen blir aldrig lägre än texten. Dra lappen i ramen: den flyttas, och tråden följer.
- [ ] Panorera med höger- eller mittknappen över en lapp och zooma med hjulet: lapparna följer grafen. Högerklick i
      en lapp som skrivs i ger *Klipp ut, Kopiera, Klistra in, Markera allt*.
- [ ] Dra pricken vid nodens ände av tråden till en annan nod: tråden knyts dit (*Kommentarens tråd knöts till en
      annan nod* i journalen). Släpp den på tom yta: tråden tas bort.
- [ ] Välj en lapp (klick, sedan Esc så att den inte skrivs i) och tryck **Delete**: lappen tas bort med en journalrad.
- [ ] Klicka på länken `chainsaw:bitcoin/…` i en lapp: noden väljs och visas. En länk till något som inte finns i
      grafen säger *Det länken pekar på finns inte i grafen än.* En vanlig webbadress ritas grå och följs aldrig.
- [ ] Stäng och öppna ärendet: lapparna står kvar med text, plats, storlek och tråd. Ett kluster som byter
      representant behåller tråden.
- [ ] Medan en lapp skrivs i gör E, L, B, U, K, F, + och − ingenting i grafen; bokstäverna hamnar i lappen.
- [ ] `/testdata`: en lapp *Uttag via Nordbörsen?* står ovanför grafen med tråd till offret och en länk till
      Nordbörsens insättning.

**Skrivandet och tråden (2026-11-08)**
- [ ] Skriv `- ett` och Enter: nästa rad börjar med `- `. Enter på en tom punkt avslutar listan; på en tom inskjuten
      punkt flyttas den ut en nivå. `1. ett` och Enter ger `2. `, `> citat` och Enter ger `> `. I ett kodblock (efter
      ```` ``` ````) är Enter en vanlig ny rad. Ctrl+Z ångrar fortsättningen i ett steg.
- [ ] Tab på en punkt drar in den två steg; Shift+Tab drar ut den. Markera flera rader och tryck Tab: alla dras in.
      Tab på en vanlig rad skriver mellanslag, och Shift+Tab lämnar aldrig lappen.
- [ ] Radavståndet medan man skriver är detsamma som när lappen är ritad: lämna lappen och öppna den igen, så står
      raderna på samma höjd och texten hoppar inte. Kod och rubriker följer med.
- [ ] Rulla hjulet över en lapp som man skriver i: grafen zoomar som över en ritad lapp, och lappen rullar inte.
- [ ] Skriv en lapp på en adress som sedan hamnar i en hög (EX2b): tråden går till högen. Plocka ut adressen ur högen:
      tråden går till noden igen.
- [ ] En lapp på en adress på Ethereum behåller sin tråd när ärendet stängs och öppnas igen.

## Sortera grafen (2026-11-08, efter användarens beskrivning)
- [ ] På den fria ytan, i ett ärende med minst två aktörer, står en ikon i grafens hörn uppe till höger, efter
      växeln Fri yta · Hoppkolumner (spridda prickar, en pil och prickarna på rad). Hovring: *Sortera grafen …*.
      I hoppkolumnerna, utan ärende och med bara en aktör syns den inte.
- [ ] Dra isär grafen så att den blir stökig och klicka på ikonen: en liten ruta under hörnet frågar *Sortera om
      grafen? N aktörer flyttas från där du har lagt dem.* med *Avbryt · Esc* och *Sortera · Enter*. Inget har flyttats.
- [ ] Esc eller *Avbryt*: rutan stängs och allt står kvar. Enter eller *Sortera*: aktörerna flyttas till sina nya
      platser, grafen anpassas till skärmen och meddelandet *Grafen sorterad* med *Ångra* står en stund nere till höger.
- [ ] Den sorterade grafen är luftig, inte kompakt: pengarna går från vänster till höger i hoppens ordning, varje hopp
      i en kolumn, kolumnerna centrerade kring samma mittlinje, mottagare i höjd med sina avsändare så att linjerna
      korsar varandra så lite som möjligt, och beloppen på linjerna får plats mellan noderna. Ett hopp med många
      motparter sprids ut i flera kolumner bredvid varandra, aldrig en lång kolumn.
- [ ] En hög och en ledtrådsnod hamnar vid sin aktör. En kommentar som är knuten till en aktör följer med och står kvar
      lika långt från den; en lös kommentar står kvar där den var.
- [ ] *Ångra* i meddelandet: varje aktör och kommentar står exakt där den stod före sorteringen. Ctrl+Z ångrar inte
      sorteringen (den hör till soptunnan och förslagen).
- [ ] Stäng och öppna ärendet: den sorterade (eller ångrade) grafen står kvar. En graf som redan är sorterad: ett klick
      säger *Grafen är redan sorterad.* utan att fråga.

## Välja flera med en ruta (2026-11-08, efter användarens beskrivning)
- [ ] Tryck vänsterknappen på tom yta i grafen och dra: en tunn ruta i accentfärgen (terrakotta) med svag fyllning
      följer pekaren, åt vilket håll man än drar. Aktörer som rutan rör får accentramen direkt medan man drar, och
      släpper ramen när rutan dras tillbaka.
- [ ] Utan tangent ersätter rutan det som var valt. Med Shift läggs det till. Med Ctrl vänds det: valda blir ovalda,
      ovalda blir valda, som i Utforskaren.
- [ ] Esc medan rutan dras: rutan försvinner och valet blir som före rutan; att släppa knappen efteråt gör inget.
- [ ] Ett klick på tom yta (utan att dra) släpper valet, också det som gjorts med Ctrl+klick. Ett vanligt klick på en
      nod väljer bara den.
- [ ] Dra en av de valda aktörerna: alla valda följer med, och kommentarer som är knutna till dem följer med på den
      fria ytan. I hoppkolumnerna går de bara upp och ner i sina kolumner. Stäng och öppna ärendet: alla står kvar.
- [ ] Dra en aktör som inte är vald: bara den flyttas.
- [ ] Delete med flera valda: alla åker i soptunnan (varningen vid soptunnan visar dem om något hänger under). Dra de
      valda till soptunnan: samma sak, och de står kvar där de var om man avbryter.
- [ ] Z med flera valda: formuläret för en egen yta räknar dem.
- [ ] Dra rutan mot grafens kant: ytan rullar och rutan växer vidare utanför det som syntes.
- [ ] Inget av detta händer i valvyn eller i en utfälld hög; där fungerar ringen och korten som förut. Att dra en
      kommentar, dess tråd, en hög eller en nod fungerar som förut.

## Små rättelser mot 1.0 (2026-11-08)

**Källor**
- [ ] Gör en ändring under Inställningar › Källor som inte går att spara (till exempel med filen skrivskyddad): felet
      kommer som en röd toast nere till höger, en gång, och står inte längre som en röd rad på sidan.

**Bevakningens huvud (BV1a)**
- [ ] Välj en bevakning med nya träffar, vänta tills en ny träff kommer och titta på vyns huvud: det står till exempel
      *2 nya träffar sedan 09:10*, där tiden är när du senast tittade på träffarna. Ett ärende där inget har setts säger
      bara antalet.
- [ ] Stäng och öppna ärendet: tiden står kvar (den sparas i ärendet).

**Offline när Windows saknar nät**
- [ ] Välj *Windows* och dra ur nätet. Tomma grafens fot säger *Offline: datorn når inte internet*, startpunkten säger
      att inget hämtas förrän datorn når internet igen, och verktygstipset nämner ingen utgång.
- [ ] Grafens hörn *Offline* och en expansion som inte gick säger *datorn når inte internet*, inte *ingen utgång*.
- [ ] Nytt ärende: taggen *Offline* har samma verktygstips, och en startpunkt som väntar säger *Väntar på nätet*.
- [ ] Bevakning: huvudet säger *Väntar på nätet · inga uppslag görs* och raderna *Väntar på nätet*; bevakningen frågar
      inget förrän nätet är tillbaka.
- [ ] Med *VPN* utan ansluten utgång står det som förut: *ingen utgång ansluten* och *Väntar på utgången*.

**Länken `chainsaw:` (Inställningar › Länkar)**
- [ ] Slå på *Öppna chainsaw:-länkar*. I `regedit` finns `HKEY_CURRENT_USER\Software\Classes\chainsaw` med
      `URL Protocol` och `shell\open\command` = `"…\Chainsaw.exe" "%1"`. Inget skrivs under `HKEY_LOCAL_MACHINE`.
- [ ] Skriv `chainsaw:bitcoin/<en adress>` i Kör (Win+R) eller i webbläsarens adressfält: Chainsaw öppnas (eller tar
      emot länken om det redan är igång) och ber om ärende.
- [ ] Slå av: nyckeln är borta, och länken öppnar inget.
- [ ] Peka länken på en annan kopia av Chainsaw (kör den andra och slå på där): raden säger att länken öppnar en annan
      Chainsaw, och strömbrytaren är av tills du slår på den igen.

**Återanslutning av tunneln**
- [ ] Anslut en VPN-utgång, och låt datorn sova en stund. När den vaknar säger titelraden *Ansluter igen …*, och
      verktygstipset säger att tunneln föll och när nästa försök görs. Inom några sekunder efter att nätet är tillbaka
      är utgången ansluten igen utan att du gör något.
- [ ] Döda processen `chainsaw-tunnel.exe` i Aktivitetshanteraren: utgången ansluts igen efter ett par sekunder.
- [ ] Utan nät väntar Chainsaw allt längre mellan försöken (2 s, 5 s, 15 s, 30 s och sedan en gång i minuten), och
      försöker direkt när nätet kommer tillbaka.
- [ ] Koppla från utgången för hand medan Chainsaw väntar på nästa försök: inget nytt försök görs.
- [ ] En bevakning som väntade på utgången går igen av sig själv när tunneln är tillbaka.

## Begäran (BG1b, 2026-11-08)

- Öppna testärendet och spåra till en börs. Begäran i pillret (Ctrl+5) är inte längre nedtonad. Vänster: *Uttag som
  nåtts i ärendet · N*, en rad per uttag med den blå fyrkanten som i grafen (streckad för börs utan KYC), kedjan,
  antal insättningar och summan. Hovring ger en mjuk bakgrund och pekhand; den valda raden har ram.
- Utan spårade pengar till ett uttag står ikonen och en ärlig text mitt i vyn, inga påhittade rader.
- Höger: *Ta med* med en bricka per insättning (hel kant och accentprick för spårade pengar, streckad kant för andra
  insättningar till adressen), *Spåret dit* när spåret når hit, språkknappen *Svenska/English* och *Exportera .md …*.
  Ett klick på en bricka tar med eller lämnar insättningen, och förhandsvisningen ändras direkt. Utan insättningar
  står *Välj minst en insättning att ta med.* och exporten går inte.
- Förhandsvisningen: rubrik, *Ärende … upprättad … av* (din profil), *Det vi ber om*, insättningsadressen,
  insättningarna som tabell (tid lokal med offset, UTC, belopp, adress, tx, block, blockhash; tx och hashar i
  Cascadia Mono, förkortade bara när de inte får plats, hela på hovring) och *Spåret dit* i en mening. Inget
  offernamn, ingen ärendetitel och inga anteckningar får stå i den.
- *Exportera .md …* (eller Ctrl+S i vyn) öppnar Windows spara-dialog med förslaget
  `<ärendenummer> Begäran <börs> <datum>.md`. Välj en fil som redan finns: dialogen frågar **inte** om att skriva
  över, och filen blir `… (2).md`. Öppna filen i Anteckningar: samma innehåll som förhandsvisningen.
- Efter exporten: en grön toast med filnamn och SHA-256, raden får en grön dokumentikon (hovring: *Exporterad …*),
  och *Exporterade* nere till vänster visar filen, tiden och SHA-256 (hela på hovring). Kontrollera med
  `Get-FileHash -Algorithm SHA256` att hashen stämmer, och att journalen har en rad `requests.exported`; `/verify`
  är grön.
- Stäng och öppna ärendet: *Exporterade* står kvar.
- Under förhandsvisningen står att ingen verifierad kontaktväg finns för börsen (katalogen är tom med avsikt).
- Nytt → Begäran (R) öppnar vyn när ett ärende är öppet, och är nedtonad utan ärende.
- Högerklick på en börs som spårade pengar når: *Begäran …* i menyn öppnar vyn med börsen vald. Andra noder har
  inte raden.
- En Ethereum-adress med USDT eller USDC kvar: inspektörens snöflingeruta har *Begäran om frysning …*, som öppnar
  vyn med raden *Tether · …* (isblå fyrkant) och en begäran till utgivaren med det som ligger kvar.

## Sökningen i kommandoraden (KR1a)
- [ ] Ctrl+K eller Ctrl+F och *kra* (med en etikett Kraken i ärendet): *Bästa träff* överst, stor, med rutan i nodens form och färg, *Etikett* litet bredvid och en rad om var den står och källorna; under den grupperna Etiketter, Adresser, Tx, Kluster och Guide i den ordningen, det skrivna i fetstil.
- [ ] Den valda raden visas direkt i grafen: grafen glider så att noden hamnar under rutan, med en ring i accent och en ring som pulserar utåt, och resten tonas ner. Med animeringar avstängda hoppar grafen och ringen står still.
- [ ] Pil upp och ner byter rad, och grafen följer med; *Visa i grafen* med Enter-tecknet står bara vid den valda raden.
- [ ] Enter (eller ett andra klick på den valda raden) stänger rutan och lämnar noden vald i grafen; Esc stänger utan att välja något, och ringen försvinner.
- [ ] *bc1q*: gruppen Adresser visar fyra och *4 av N · Tab visar alla*; Tab fäller ut gruppen (högst 100), och Tab igen går till nästa grupp.
- [ ] En tx som är hämtad men inte står på någon linje säger *Hämtad, inte i grafen* och *Inte i grafen* i stället för att flytta grafen.
- [ ] *utxo* (eller */guide utxo*): Guidens panel fälls ut från höger vid avsnittet, raden säger *Öppna i Guiden* och foten *Enter öppnar Guiden*; byter man till en rad i ärendet fälls panelen in igen. Enter lämnar panelen öppen.
- [ ] *utxo* säger *Inget i ärendet heter utxo. Guiden svarar på begrepp.*
- [ ] En adress som inte finns i ärendet: brickan *₿ Bitcoin* i fältet och raden *Lägg till som startpunkt* med *Inget hämtas förrän du expanderar den, och då bara adressen, genom <utgången>*; offline säger den när det kan hämtas. Enter eller ett klick lägger till startpunkten.
- [ ] Utan öppet ärende söks bara Guiden, och raden säger det.
- [ ] Ett stort ärende (tusentals adresser): raden hänger aldrig medan man skriver.
- [ ] */help* listar */guide*, och */gu* med Tab fyller i kommandot.

## Inställningarna mot 1.0 (BV4a, ST2a, FS2a och UT3b)

- **Låsning › När en bevakning slår till:** tre rader med ikon (Avisering i Windows, Ljud, Meddelande mitt på
  skärmen). Brickan blir terrakotta när sättet är på. *Lyssna* spelar mynt-swooshet, *Prova* visar kortet mitt på
  skärmen. Slå av alla tre: en gul rad säger att en träff bara syns i Bevakning.
- **Kortet mitt på skärmen:** ett litet kort med ögat och tre ringar som går ut (stilla med animeringar avstängda),
  ingenting mörkas. Det ligger överst över alla fönster, går att dra, Esc stänger och Enter tar åtgärden. *Prova*:
  *Visa* öppnar Bevakning.
- **Kortet från bakgrunden:** slå på *Meddelande mitt på skärmen*, stäng Chainsaw och kör `Chainsaw.Watcher.exe
  --test-alert`. Kortet visar *Chainsaw · Bevakning* uppe till vänster och *Öppna Chainsaw*. Med Chainsaw igång (öppet
  eller låst) står *Visa Chainsaw*, och Enter tar fram fönstret. Bara ett kort åt gången.
- **Om Chainsaw (inställningarna):** fingeravtrycket i fyra rader om fyra grupper, *Kopiera* blir *Kopierat* med en
  bock, och klistrat är det gruppat. Utan inbyggd nyckel: *Ingen nyckel* och en gul streckad varning. Lägg en rad
  `REVOKED <fingeravtryck>` i nyckelfilen: den står överstruken under *Återkallade*, hela på hovring.
- **Om Chainsaw från låsskärmen:** *Om Chainsaw · commit* i hörnet öppnar samma sida utan menyn, med raden *Visas före
  upplåsningen …*. Esc, × och ett klick på det mörka stänger den, och tangentbordet går tillbaka till fältet.
- **Förstagångens steg Utgång:** Windows är förvalt, med vägen grön (Datorn, Windows, Blockkedjor) och *Fortsätt*.
  Utan nät: rött och streckat, *Fortsätt offline*. VPN: det streckade fältet för en WireGuard-fil (släpp en fil på det
  eller klicka), sedan raden med filen, × och strömknappen; ansluten blir vägen grön med utgångens namn. Efter
  *Fortsätt* står samma val i Inställningar › Nätverk.
- **Nätverk › utgångarna som tabell:** kolumnerna Utgång, Plats och Syns som. Ett klick fäller ut raden med namn,
  land (listan) och stad, som sparas när fältet lämnas eller med Enter, och rutan *Kontroll*. *Kontrollera nu* går
  bara på en ansluten utgång och visar adressen och landet; fel land blir gult. *Ta bort utgången …* frågar först,
  och Esc ångrar.
- **Kontrollera vid anslutning:** på från början. Anslut en utgång vars land är satt till något annat än där den
  faktiskt går ut: toastern varnar en gång. Slå av: ingen fråga när den ansluts.

## Etiketter (ET1a, 2026-11-08)
- [ ] Öppna testärendet (`/testdata`) och vyn Etiketter i pillret (Ctrl+7): ikonen är inte längre nedtonad. Till
      vänster står KÄLLOR (Sanktionslistor, Egna, Chainabuse, Beteende, Andra ärenden) med antal, och under dem
      BIBLIOTEKET med Hela biblioteket, Kom in, Gick ut och Sprids inte.
- [ ] En sanktionsträff på en nod i grafen står överst under SANKTIONSTRÄFFAR I GRAFEN i rött, med listans namn och
      datum till höger; övriga etiketter under ETIKETTER I ÄRENDET.
- [ ] Varje rad: sorten som en liten nod (rund eller fyrkantig, streckad för förslag), etiketten med nodens namn
      bredvid, adressen i Cascadia Mono, prickar för oberoende ursprung (gröna vid fler än ett), källans ikon (röd ram
      för sanktionslistan, streckad för beteende), märket *från ett annat ärende* och *Sprid inte*. Hovra en rad: ikonen
      *Visa i grafen* syns längst till höger.
- [ ] Klicka på en källa: dess etiketter försvinner och källan tonas ner; klicka igen så kommer de tillbaka.
- [ ] Skriv en del av en adress eller ett namn i filterfältet: listan krymper. Pilarna upp och ner flyttar valet.
- [ ] Den valda etiketten till höger: sorten stor, namnet, adressen med Kopiera, brickorna (källan, *Bedömning*
      streckad eller *Står på listan*), nodens rad i grafen och ursprungen med vem, när (lokal tid och UTC), ärende och
      varför. *Varför?* syns för beteende och Chainabuse och öppnar Guiden om etiketter, med väg tillbaka.
- [ ] *Visa i grafen* (knappen eller G) går till grafen med noden vald och i bild.
- [ ] *Sprid inte* på en egen etikett: strömbrytaren slås om, en toast säger att den stannar i ärendet, och raden får
      märket. Journalen har en rad *Etiketten sprids inte*. På en etikett på ett offer står brytaren på och går inte
      att ändra.
- [ ] *Ta tillbaka …*: ett fält för skälet fälls ut (Enter tar tillbaka, Esc avbryter). Raden blir överstruken och
      hamnar sist, och rutan till höger säger vem som tog tillbaka den, när och varför. Grafen läses om.
- [ ] Växeln *Biblioteket*: alla etiketter i biblioteket med ärendenumren som brickor; inga åtgärder vid foten. Med
      låst valv eller trasigt bibliotek står *Biblioteket kan inte läsas just nu* och antalen är streck.
- [ ] Släng en sanktionerad aktör i soptunnan, eller låt den hamna i en hög: den står ändå under SANKTIONSTRÄFFAR I
      GRAFEN, med en liten soptunna eller hög på raden.
- [ ] En etikett på samma aktör som ett offer: brytaren *Sprid inte* står på och går inte att ändra, och under den står
      *Nära ett offer i grafen: når aldrig biblioteket.*
- [ ] Skriv snabbt i filterfältet i ett stort ärende: listan hänger med utan att hacka.
- [ ] Nytt → Etikett (E) med en nod vald i grafen: grafen visas och etikettformuläret öppnas vid noden. Utan vald nod
      säger grafen *Välj noden i grafen som etiketten gäller, och tryck L.*

## Bevakning

### Bevakningen medan Chainsaw är öppet (etapp 26, BV1a, BV2a och BV3a)

Kräver en ansluten utgång (eller Windows som utgång), ett ärende med en graf, och att endpointarna har provats vid
start (en endpoint måste vara *i fas* för att bevakningen ska fråga).

**Högerklick och formuläret (BV3a)**
- Högerklicka på en adress eller ett kluster i grafen: menyn har Etikett (L), Bryt upp (U, bara kluster), Annan kedja
  (K), **Bevaka … (B)**, Expandera (E) och Kopiera adress (Ctrl+C). En tx-nod har Bevaka tonad. Klick utanför eller Esc
  stänger menyn.
- *Bevaka …* (eller B, eller ögat i listen vid noden): Bevaka glider in i en ruta bredvid noden med en pil mot den
  (BV3a). Bara riktningen syns, som tre stora ikoner (In, Ut, Båda), och en mening under säger vad som händer.
- *+ Bara vissa motparter*, *+ Minsta belopp* och *+ Hur ofta* fäller ut sitt fält och blir ✓; meningen ändras med
  det som väljs. Ctrl+klick på noder i grafen lägger till motparter; noden själv vägras. Tillgången syns bara på
  Ethereum. Ctrl+klick utan öppet formulär väljer fortfarande aktörer för en egen yta (Z).
- Ändra en bevakning med motparter eller ett minsta belopp: de fälten är redan utfällda.
- Enter sparar, Esc avbryter. Noden får ett **turkost öga** i hörnet.

**I grafen**
- En bevakning med mottagare: en tunn **prickad** linje i en dov turkos ton från noden till varje mottagare som inga
  pengar gått till, med ett litet öga mitt på. Till en mottagare som redan har en linje: linjen får ett mjukt turkost
  sken i stället. Avsändare: linjerna går in till noden.
- Utan motparter: korta prickade strålar vid porten (höger för ut, vänster för in) med ett litet öga.
- Koppla ner utgången: ögonen blir ihåliga och grå, linjerna och strålarna grå. Anslut igen: färgerna kommer tillbaka.
- Vid en träff (vänta på ett nytt block med en bevakning på en aktiv adress, eller testärendet): en turkos linje med
  beloppet till motparten om den finns i grafen, och **ringar som rör sig** runt mitten tills du väljer noden. Med
  *Visa animeringar i Windows* avstängt står ringarna stilla. Grafens rad säger *Bevakning slog till · …*.
- Pillrets öga får en prick vid nya träffar; den försvinner när träffarna är sedda.

**Vyn Bevakning (BV1a, Ctrl+6)**
- En rad per bevakning: läge som prick (grön aktiv, gul väntar, grå pausad), namn, kedja, pilar för riktningen,
  motparterna som brickor, tillgången, *Senast kontrollerad* med block och en prick för nya träffar.
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
- [ ] Med en bevakning: listan, räknaren, bakgrundens läge i en rundad ram och *Ny bevakning … B* syns i huvudet, och högerpanelen visar den valda.
- [ ] Bakgrundens läge i huvudet (BV1a, omgjort 2026-11-08): en rundad ram med prick och text. Hel grå ram och grön prick när den går, streckad gul ram med gul text när den väntar på utgången, streckad grå ram med grå text när den är av; hovra för förklaringen.
- [ ] Varje rad har nodens egen ikon och färg som i grafen (en börs blå och fyrkantig, en okänd adress rund med ett frågetecken, ett kluster fyrkantigt med plånboken); en bevakning på en adress som inte finns i grafen visas som okänd eller kluster. Motparternas brickor har en liten pil för riktningen (ut till en mottagare, in från en avsändare).
- [ ] Högerpanelen: nodens ikon i en ram på 30 px bredvid namnet, läget i lägets färg (grönt, gult eller grått), och knapparna längst ner där *Öppna i grafen* är ljusare än de andra.
- [ ] Ett fel när bevakningen skriver eller läses (till exempel ett låst ärende) kommer som en röd toast nere till höger, inte som en röd rad i vyn.
- [ ] Ta bort den sista bevakningen: högerpanelen fälls bort och det tomma läget kommer tillbaka.

### Bevakningen när Chainsaw är stängt (etapp 27)

Kräver Windows som utgång (*Inställningar → Nätverk*), ett ärende med en aktiv bevakning och att `Chainsaw.Watcher.exe`
ligger bredvid `Chainsaw.exe`.

**Inställningarna (Låsning)**
- *Bevaka när Chainsaw är stängt* är på från början; sätten att säga till provas under *Inställningarna mot 1.0*
  (BV4a).

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
- Starta Chainsaw: låsskärmen har raden *En bevakning har slagit till. Den visas efter upplåsning.* med en prick.
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

### Bevakningens huvud (BV1a)

- [ ] Välj en bevakning med nya träffar, vänta tills en ny träff kommer och titta på vyns huvud: det står till exempel
      *2 nya träffar sedan 09:10*, där tiden är när du senast tittade på träffarna. Ett ärende där inget har setts säger
      bara antalet.
- [ ] Stäng och öppna ärendet: tiden står kvar (den sparas i ärendet).

## Begäran, exponering och rapport

### Begäran (BG1b, 2026-11-08)

- Öppna testärendet och spåra till en börs. Begäran i pillret (Ctrl+5) är inte längre nedtonad. Vänster: *Uttag som
  nåtts i ärendet · N*, en rad per uttag med den blå fyrkanten som i grafen (streckad för börs utan KYC), kedjan,
  antal insättningar och summan. Hovring ger en mjuk bakgrund och pekhand; den valda raden har ram.
- Utan spårade pengar till ett uttag står ikonen och en ärlig text mitt i vyn, inga påhittade rader.
- Höger: *Ta med* med en bricka per insättning (hel kant och accentprick för spårade pengar, streckad kant för andra
  insättningar till adressen), *Spåret dit* när spåret når hit, språkknappen *Svenska/English* och *Exportera .md …*.
  Ett klick på en bricka tar med eller lämnar insättningen, och förhandsvisningen ändras direkt. Utan insättningar
  står *Välj minst en insättning att ta med.* och exporten går inte.
- Förhandsvisningen: rubrik, *Ärende … upprättad … av* (din profil), *Det vi ber om*, insättningsadressen,
  insättningarna som tabell (tid lokal med offset, UTC, belopp, adress, tx, block, blockhash; tx och hashar i
  Cascadia Mono, förkortade bara när de inte får plats, hela på hovring) och *Spåret dit* i en mening. Inget
  offernamn, ingen ärendetitel och inga anteckningar får stå i den.
- *Exportera .md …* (eller Ctrl+S i vyn) öppnar Windows spara-dialog med förslaget
  `<ärendenummer> Begäran <börs> <datum>.md`. Välj en fil som redan finns: dialogen frågar **inte** om att skriva
  över, och filen blir `… (2).md`. Öppna filen i Anteckningar: samma innehåll som förhandsvisningen.
- Efter exporten: en grön toast med filnamn och SHA-256, raden får en grön dokumentikon (hovring: *Exporterad …*),
  och *Exporterade* nere till vänster visar filen, tiden och SHA-256 (hela på hovring). Kontrollera med
  `Get-FileHash -Algorithm SHA256` att hashen stämmer, och att journalen har en rad `requests.exported`; `/verify`
  är grön.
- Stäng och öppna ärendet: *Exporterade* står kvar.
- Under förhandsvisningen står att ingen verifierad kontaktväg finns för börsen (katalogen är tom med avsikt).
- Nytt → Begäran (R) öppnar vyn när ett ärende är öppet, och är nedtonad utan ärende.
- Högerklick på en börs som spårade pengar når: *Begäran …* i menyn öppnar vyn med börsen vald. Andra noder har
  inte raden.
- En Ethereum-adress med USDT eller USDC kvar: inspektörens snöflingeruta har *Begäran om frysning …*, som öppnar
  vyn med raden *Tether · …* (isblå fyrkant) och en begäran till utgivaren med det som ligger kvar.

### Exponering (EP1b)

- Öppna `/testdata`, gå till **Exponering** (Ctrl+4): pillrets ikon är inte nedtonad. Klustret står i mitten med tre
  prickade ringar, utbetalt åt höger och inkommet åt vänster; tjänsterna har sortens form, färg och ikon, större ju
  större andel, och aktörer utan etikett är små punkter med namnet ovanför.
- Byt **Djup** 1, 2 och 3: ringarna tänds och släcks och andelarna ändras; med 1 syns bara det som gått direkt.
- Klicka på ämnet uppe till vänster: menyn *I grafen* listar kluster och adresser; välj ett annat.
- Till höger: *Betalat till* och *Fått från* med andelar per färg och *Okänt* med streckad prick; hovra *Okänt* och
  läs skälen. Klicka en tjänst i listan eller i ringarna: vägen lyser, resten tonas ner, och nere visas vägen som
  brickor, tx:erna och underlaget (metoden, `exposure v1`, klustringen, etiketten).
- *Varför?* öppnar Guidens panel bredvid med förklaringen och avsnittet *Exponering*; vyn flyttar in sidopanelen.
- *Visa vägen i grafen* (eller G): grafen visas med tjänsten vald, vägen tydlig och resten svag, och ett meddelande
  säger vägen och beloppet. Ett klick i grafen tar bort markeringen.
- Byt spårningsmetod i grafen (FIFO och haircut) och gå tillbaka: metoden i Exponeringens huvud följer med och
  andelarna ändras.
- På Bitcoin: välj en börs som grafen visar spårade pengar till; exponeringen ska ge börsen samma belopp som grafens
  linje dit. Hovra *Okänt*: en aktör som inte är expanderad sägs som *Aktören är inte expanderad*.
- Släng en börs i soptunnan och öppna Exponering: börsen räknas fortfarande som en tjänst.

### Rapporten (RP1b)

- Pillret har *Rapport* sist bland vyerna, med Ctrl+8, och bara med ett ärende öppet.
- Utan markering: vyn säger att aktörerna markeras i grafen med Ctrl+klick eller en ruta, och *Till grafen* går dit.
- Markera en startpunkt, ett kluster och en börs som spårade pengar når. Rapporten visar *Byggd ur markeringen* med en
  liten bild där de markerade lyser och resten är svagt, brickorna för aktörerna i sortens form och färg, och *Delar*
  med åtta strömbrytare (sju på från början, *Etiketter och källor* av).
- Förhandsvisningen har spåret som tabell med tid lokal och UTC, belopp, hela txid, blockhöjd och blockhash, och
  rubriken har brickan *Uttag funnet* i blått. Slutsatserna har *Varför:* under sig bakom en streckad linje.
- Slå av *Blockhöjd och blockhash*: kolumnerna försvinner direkt. Växla till *Markdown*: filens text i mono, rubriker
  i accent.
- Ett offer med namn i en etikett: namnet står aldrig i rapporten, varken i spåret, slutsatserna eller etiketterna.
- *Exportera .md …* (eller Ctrl+S): filen sparas, en toast säger att den exporterats och journalförts, och filen står
  under förhandsvisningen med SHA-256 i verktygstipset. Exportera igen till samma namn: den nya filen får `(2)`.
- Journalen har raden *Rapport exporterad som …* med SHA-256, och `/verify` är grön efteråt.
- *Kontroll* i filen visar en topphash över alla ärendets förseglade kedjor (och journalens rad) från precis före
  exporten. Ett ärende där `/verify` hittar fel får texten *kontrollen fann ändringar*, aldrig *inga ändringar*.
- Markera två offer som båda betalar till samma kluster och vidare till en börs: rapporten har ett spår med båda som
  start, och *Spårat* är summan av allt som når börsen. Pengar som går tillbaka till en markerad aktör står med.
- Belopp i SEK står bara när ärendet har en kurs för tiden när pengarna rörde sig, och då för den spårade delen.
- *Ändra i grafen* och klicket på den lilla bilden går till grafen; ändra markeringen och gå tillbaka med Ctrl+8:
  rapporten följer med.

## Guiden

### Guiden (etapp 18, GD1b)

**Fler avsnitt med bilder (2026-11-08)**
- [ ] F1: indexet har grupperna *Kom igång*, *Ärendet*, *Säkerhet och utgångar*, *Arbeta i grafen*, *Uttag och begäran*,
      *Förvirring* och *Ordlista*, och inget står under *Övrigt*.
- [ ] Öppna Guiden från pillret eller med F1 i vilken vy som helst: överst står *Tillbaka till <vyn>*, och Esc eller
      länken går tillbaka dit. Från panelen bredvid grafen går den tillbaka till grafen.
- [ ] Varje nytt avsnitt börjar med en ruta *Som i vardagen:* och har en eller två bilder (till exempel pärmen med
      stämplar i *Journalen, hashkedjan och /verify*). Bilderna är skarpa, i temats färger, lika breda och utan text i
      själva bilden; bildtexten står under. Ingen bild saknas eller visas som en tom ruta.

**Panelen bredvid grafen**
- Välj en nod och klicka *Varför?*: en panel (440 px) glider inte in utan står direkt till höger, över inspektören, med
  skugga mot grafen. Grafen mörkas inte och går att panorera, zooma och klicka i medan panelen är öppen.
- Överst en ruta med terrakotta kant till vänster: *Varför? · nodens namn* och alla förklaringar. En förklaring som hör till
  ett annat avsnitt har en länk under sig med avsnittets titel.
- Under rutan avsnittet: titeln, och den rubrik som svarar har en terrakotta linje till vänster och står överst i den
  synliga ytan. Prova: ett kluster öppnar *Common-input-ownership* vid *Idén* (när klustrets länkar kommit), en börs
  eller en adress med etikett *Etiketter* vid *En etikett är alltid en bedömning*, en mixer *Varför ett spår tar slut*
  vid *Riktiga slut*, en startpunkt utan annat *Startpunkter*.
- Välj en annan nod med panelen öppen: panelen följer med och visar den nya nodens svar. Pilen uppe till vänster går
  tillbaka till den förra. Pilen är tonad när det inte finns något att gå tillbaka till.
- Klicka på en länk i texten (terrakotta, understruken på hovring), till exempel *Adresstyper* i *Startpunkter*: avsnittet
  byts, och pilen går tillbaka.
- Längst ner *Läs också* med avsnitten texten länkar till.
- × stänger, och Esc stänger var tangentbordet än är (utom i kommandoraden och i Nytt ärende). Ett andra Esc i grafen
  väljer inget, som förut.
- Högerklicka på *FIFO* i grafens hörn: panelen öppnas vid *FIFO (först in, först ut)* med det spårade överst. Ett
  vanligt klick byter fortfarande metod. Efter byte till haircut: högerklick öppnar vid *Haircut (andel)*.
- *?* nere till höger visar bildspråket; *Guiden F1* längst ner i rutan stänger bildspråket och öppnar
  panelen vid avsnittets början. Med panelen redan öppen blir det ett steg, och pilen går tillbaka.
- Byt vy i pillret: panelen stängs. Öppna eller stäng ett ärende: panelen stängs.
- Svarsrutan står kvar överst medan avsnittet under den rullar till den markerade rubriken; ett långt svar (ett
  kluster med många länkar) rullar för sig i rutan.
- Med panelen öppen flyttar grafens hörn (växeln, metoden, beloppen) åt vänster så att det syns bredvid panelen, och
  kommandoraden centreras i ytan bredvid. Prova också vid fönstrets minsta bredd (960 px).
- F1 med panelen öppen: som *Öppna i Guide*. Esc med utgångarnas ruta öppen stänger den rutan, inte Guiden.
- En länk i texten får en fokusmarkering med Tab.

**Panelen: länkar, tangentbordet och smalt fönster (2026-11-08)**
- [ ] Välj en nod, öppna *Varför?* och följ en länk i texten. Klicka på samma nod igen: panelen visar nodens svar igen,
      och pilen går tillbaka till länken.
- [ ] Välj ett kluster med panelen öppen och följ en länk innan klustrets länkar kommit: panelen står kvar på länken.
      Gå tillbaka med pilen: svaret har klustrets länkar. Läs i klustrets avsnitt när länkarna kommer: avsnittet byts
      inte, bara svaret överst.
- [ ] Klicka i grafen så att den har tangentbordet och tryck Shift+F1: panelen öppnas vid metodens rubrik, som
      högerklick på *FIFO*. Metodens verktygstips säger *högerklicka eller Shift+F1 för Guiden*.
- [ ] Gör fönstret så smalt det går (960 px), öppna panelen och Ctrl+K: kommandoraden står hel i ytan bredvid
      panelen, med luft på båda sidor, och inget av den är kapat. I ett brett fönster är den 640 px som förut.

**Texten**
- Fetstil i ljus text, kursiv, kod (adresser, `coinjoin v1`) i Cascadia Mono på en upphöjd yta, punktlistor med en
  liten prick, numrerade listor (*Kurser*), inskjutna listor (*CoinJoin*: Whirlpool, Wasabi, JoinMarket), citat med en
  linje till vänster (*UTXO-modellen*: summan av inputs …), kodblock (*Peel chain*, *Kurser*) som krymper i stället för
  att brytas om panelen är smal, och tabeller (*Adresstyper*, *Växeladress*) med kolumner som delar bredden.
- Inga tecken som `**`, `` ` ``, `](` eller `![` syns någonstans.
- **Som i vardagen** (2026-11-08): varje avsnitt börjar med en ruta på upphöjd yta med en glödlampa och *Som i
  vardagen* i sandfärg, och analogin under i vanlig text. Inget i accentfärgen.
- **Figurerna** (2026-11-08): skarpa vektorbilder på en yta med tunn kant (25 stycken, till exempel tre i
  *UTXO-modellen* och tre i *Spårningsmetoder*), med bildtexten liten, nedtonad och vänsterställd under. Färgerna
  följer bildspråket: accent för offrets pengar, blått för börs, rött för mixer och CoinJoin, lila för annan kedja,
  streckat för det osäkra. Inga ikoner i figurerna är suddiga eller tjockare än i grafen.
- I den smala panelen krymper figurerna till panelens bredd i stället för att klippas eller rulla i sidled; i vyn är
  de högst 560 px och centrerade i textspalten.
- En figur som saknas (prova att skriva `![Prov](figur:finns-inte)` i en text i ett Debug-bygge) visar en tom
  streckad ram och bildtexten följd av *Bilden saknas*, och Chainsaw kraschar inte.

**Vyn Guide (F1)**
- F1 eller Guide i pillret: avsnitten i grupper till vänster, det valda i markerad yta, och textspalten (högst
  620 px) **centrerad** i ytan till höger om förteckningen, också i ett brett fönster; texten i spalten är
  vänsterställd. Spalten har samma bredd i ett kort avsnitt som i ett långt. Kommandoraden täcker inte textens slut.
- *Öppna i Guide* i panelen: vyn Guide visas vid samma avsnitt och rubrik, med *Tillbaka till grafen* överst, som går
  tillbaka till grafen. Panelen är stängd.

## Testärendet

### Testärendet (/testdata)

Allt i testärendet är påhittat: kedjan, kurserna och sanktionslistan besvaras på datorn, och inget går ut på nätet.
Gör om testärendet efter en ny version (berättelsen är version 2), eftersom ett äldre saknar det som lagts till.

**Att skapa det**
- [ ] Bygg och kör i Debug (`dotnet run --project src/Chainsaw.App`), lås upp och skriv `/testdata` i kommandoraden
      (Ctrl+K). Efter några sekunder står *Testärendet TEST-0001 skapades med påhittade data och är öppet*, och grafen
      visar det; ett andra `/testdata` ger TEST-0002. I ett Release-bygge finns kommandot inte, och `/help` visar det
      bara i Debug.
- [ ] Ärendeväljaren visar också TEST-0001-KOLLEGA, kollegans ärende där etiketterna från ett annat ärende sattes.
- [ ] Allt fungerar med VPN valt och ingen utgång ansluten: testärendet frågar aldrig nätet efter kedjedata eller
      kurser. Ett riktigt ärende som öppnas samtidigt frågar som vanligt genom utgången.
- [ ] `/verify` på testärendet säger att inget har ändrats, och en tx i inspektören visar källan *testdata* och
      endpointen *testdata (påhittade data, bara Debug)*.

**Grafen**
- [ ] Startpunktens flagga (offrets betalning till bedragaren är startpunkten), och varje sort i bildspråket: offer,
      kluster, okänd, börs (Nordbörsen med *Uttag funnet*, Sydbörsen), utan KYC (Snabbväxlaren, streckad), nästlad,
      OTC, automat, kort, spel, handel, presentkort, darknet (Skuggtorget med sanktionsmärket), garanti (Garantitorget
      med märket *från ett annat ärende*), mixer (Blandaren och Virveln), CoinJoin (Whirlpool-tx:en), sanktion, swap,
      delad tx (gruvbelöningens mynt och en annan ägare), mynt (gruvbelöningens P2PK och OP_RETURN) och annan kedja
      (lila, från Bytaren). *Kan frysas* saknas (kräver Ethereum, se BACKLOG).
- [ ] Kronorna står under beloppen utan att något hämtas: kurserna kommer från *Påhittade kurser (testdata)* och
      *Påhittade växelkurser (testdata)*. *Varför?* på ett belopp visar stegen med de påhittade källornas namn, aldrig
      Bitstamp eller Riksbanken.
- [ ] Sista hoppet i skalningskedjan (till Nordbörsens insättning) har det gula märket för ett bytt block: *Bytte block*
      visar förut och nu på samma höjd med två olika hashar.
- [ ] *Varför?* på *Bedragarens plånbok* förklarar konsolideringen (common-input-ownership); klustret med växeladressen
      förklarar växeln med tre tecken.
- [ ] Påfyllnaden är uppbruten ur det samlade klustret: Historik på den visar *Uppbruten · 1 ut* med *Ångra*.
- [ ] Kommentaren *Uttag via Nordbörsen?* står ovanför grafen med tråd till offret; dess Historik har *Kommentar
      skriven* och *Texten ändrad*.
- [ ] Den egna ytan *Uttagsvägar* ligger runt börserna.

**Förslag, etiketter och soptunnan**
- [ ] *Förslag · N*: *Troliga utgångar ur mixern* med den troligaste godkänd (grön bock på den streckade linjen) och
      resten att avgöra, *Delarna tillsammans* att avgöra, och under *Gjort av sig självt* *Mixer efter mixer, 2 steg*
      (Blandaren till Virveln), CoinJoin och peel chain. Plånboken med mixerns utgång och dess reserv står som *Godkänd
      plånbok*; skalningskedjans plånbok väntar.
- [ ] Vyn Etiketter har *Sanktionerad aktör (påhittad)* under SANKTIONSTRÄFFAR I GRAFEN, med listan *Påhittad
      sanktionslista (testdata)*; ingen riktig lista importeras. Garantitorget och Nordbörsen har etiketter från
      TEST-0001-KOLLEGA med ärendenumret. Testärendets egna etiketter är *Sprid inte*.
- [ ] Soptunnan har en slängd kommentar och en slängd egen yta (*Spelsajterna*); *Lägg tillbaka* ger tillbaka dem.

**Bevakning, begäran, exponering och rapport**
- [ ] Bevakningen *Bedragarens andra plånbok* har ett öga i grafen och en träff som väntar (ett nytt offer betalar in)
      med animerade ringar; vyn Bevakning visar träffen. Inget av testärendet hamnar i bakgrundsprocessens plan.
- [ ] Begäran listar Nordbörsen med insättningen och spåret dit; förhandsvisningen har txid, block och hash.
- [ ] Exponering på bedragarens kluster når tjänster på flera hopp (Djup 2 och 3).
- [ ] Rapport: markera offret, bedragaren och Nordbörsen; rapporten har spåret dit.

**Att expandera själv**
- [ ] *Navet* (fler än 50 tx, så resten hålls tillbaka, och många motparter), *Offret* (utbetalat från Nordbörsens heta
      plånbok), en av Whirlpool-utgångarna och bedragarens andra plånbok (det nya offrets betalning).

## Resterna efter valen (Begäran, sparningen, Exponering, utgången, sökningen, 2026-11-08)

- **Börsens markering:** exportera en begäran i vyn Begäran och gå till grafen: börsen får en liten blå markering med
  en sida (begäran) i hörnet, efter *Uttag funnet* och före ögat om den bevakas. Den står kvar när ärendet öppnas
  igen. Bildspråkets *?* har raden *Begäran exporterad*.
- **Sparningen som inte går:** lås ärendefilen för skrivning (öppna `.cut` i ett program som håller den, eller gör
  mappen skrivskyddad) och skriv något i ärendet. Efter tre misslyckade sparningar i rad (cirka 7 minuter) står en
  gul varning nere till höger, *Ärendet kunde inte sparas*, med *Försök igen*. Släpp låset och tryck *Försök igen*:
  *Ärendet är sparat igen*. Släpps låset utan att trycka tas varningen bort när nästa sparning går.
- **Ett ändrat förslag:** godkänn en plånbok i panelen Förslag efter att klustringen ändrats (bryt upp och ångra i en
  annan ordning, eller expandera så att förslaget ändras): panelen säger att förslaget har ändrats och att
  kontrollerna är körda igen, i stället för *Det kunde inte sparas i ärendet*.
- **Exponering, kronorna:** med kurser i ärendet står kronorna efter beloppet för den valda tjänsten (`0,2500 BTC ·
  ≈ 23 400 kr`) och i tipset på andelarna per färg. Utan kurs för en linje på vägen står inga kronor.
- **Exponering, vägen tillbaka:** *Visa vägen i grafen* (G) visar grafen med *‹ Exponering Esc* uppe till vänster;
  knappen och Esc går tillbaka till Exponering. Lämnas grafen på annat sätt försvinner knappen.
- **Exponering i nodens meny:** högerklicka ett kluster eller en adress som betalat eller fått: *Exponering …* öppnar
  vyn med noden i mitten. Noder utan pengar har ingen sådan rad.
- **Exponering och spåret:** knyt en adress mynt till en annan kedja (K): Exponering för den som betalade dit visar
  pengarna hos *Annan kedja*, inte vidare i bryggans egna tx. Pengar in i en CoinJoin som inte räknas som tjänst står
  under *Okänt* som *Spåret tar slut i en tx*.
- **Utgångens tips:** håll musen över utgången i titelraden med en ansluten VPN-utgång som har kontrollerats: tipset
  slutar med `syns som <adress>, <land>`. Med Windows som utgång kontrolleras datorns eget nät en gång när datorn är
  online (om *Kontrollera vid anslutning* är på) och tipset säger `Uppslag går genom Windows · syns som <adress>`.
  Låst: inget frågas.
- **Sökningen och soptunnan:** släng en adress och sök på den med Ctrl+K: raden säger *I soptunnan* och *Öppna
  soptunnan*; Enter öppnar soptunnan.
