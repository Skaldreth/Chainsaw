# Att göra

Det som är kvar för att Chainsaw ska vara färdigt (1.0). **Ta bort en punkt när den är byggd.** Det som kommer efter
1.0 och de mindre fynden från granskningarna står i `docs/BACKLOG.md`.

Filen har tre delar:
1. **Väntar på dig:** frågor som bara användaren kan avgöra. Till dess gäller det som står efter *Nu*.
2. **Vägen till 1.0:** det som ska vara gjort för att Chainsaw ska vara färdigt, i den ordning det tas.
3. **Före tjänstebruk:** sådant som inte är kod men måste vara klart innan Chainsaw används i tjänsten.
4. **Efter 1.0 och kända svagheter:** nya förmågor och de mindre fynden från granskningarna, per område.

Varje punkt i *Vägen till 1.0* är märkt med hur den byggs (CLAUDE.md, *Processen efter storleken*):
- **[direkt]:** en rest eller rättelse inom en godkänd skiss, som byggs och granskas mot skissen av sessionen själv.
- **[skiss]:** ritas först på canvasen, och byggs när användaren sagt ok.
- **[etapp]:** skriver i ärendet eller rör principerna, och byggs med oberoende granskning.

## Väntar på dig

Frågorna till kryptospåraren står i `docs/KRYPTOSPARAREN.md`.

**Från bygget 2026-11-08**, så som det är byggt nu:
1. **Kandidaternas linjer** i mixerns förslag ritas alltid. Förut ritades de bara när områdena var på. *Nu:* alltid.
2. **SEK för det som ligger kvar** räknas till den senaste kursen ärendet har för tillgången, inte till dagens kurs
   som kortets baksida ska visa. *Nu:* den senaste kursen i ärendet. Dagens kurs kräver ett uppslag genom utgången.
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

**Äldre, där sessionen valt det säkraste:**
- **WireGuard i bakgrunden** (etapp 27): ska en utgångs WireGuard-fil kunna sparas under DPAPI utanför valvet, så
  att bakgrunden kan bevaka genom VPN när Chainsaw är stängt? *Nu:* nej, bakgrunden går bara genom Windows.
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
- **Spårning genom en swap** (Ethereum): hur stor del av ett byte som räknas som spårat är en bedömning som behöver
  en regel. *Nu:* varje tillgång spåras för sig (`docs/BACKLOG.md`, *Kedjor och data*).

**Behövs för att 1.0 ska kunna bli klar:**
- **Vad ingår i 1.0?** Vyerna Tidslinje, Flöde (Sankey), Exponering och Etiketter är bekräftade men inte skissade.
  *Förslag:* Etiketter och Exponering med i 1.0, eftersom de hör till Attribuera och Uttag. Tidslinje och Flöde
  efter 1.0, eftersom tidslinjen under grafen (TL1a) och linjernas tjocklek redan visar mycket av det.
- **Rapporten (Redovisa):** antingen som i Gawk med bilagor, eller ett enklare underlag för underrättelse.
  *Förslag:* ett enklare underlag först, en export till Markdown som Begäran, med spåret, slutsatserna med *Varför?*
  och hashkedjans topphash.

## Vägen till 1.0

Ordningen: först provet på Windows, sedan resterna i det som redan är byggt, sedan det som är godkänt men inte byggt,
och sist det som skissas. Inget nytt byggs förrän det som finns känns bra (användarens beslut 2026-11-07).

### 1. Provet på Windows

Inget sedan etapp 1 är provat på Windows. Användaren provar efter `docs/TESTER-INFOR-1.0.md`, och det som inte
fungerar rättas före allt annat nedan. Särskilt:
- **Kraschen vid start** (`NewCasePanel`) är rättad men inte bekräftad.
- **Expansionen gav *Inget nytt att hämta*** med mempool vald (rättat 2026-11-07, väntar på prov).
- **Det som byggdes 2026-11-08** och inte är sett av användaren:
  - KF2b, NC2a, KN1a, SO1a, GR7a, TL1a, inspektören och BV1a.
  - Guiden, *Sortera grafen*, den mjuka zoomen, inspektörens glidning och rutmarkeringen.
- **Inaktuella rader i TESTER-INFOR** [direkt]: läs igenom hela filen och stryk det som ersatts, till exempel
  NC1a, IN1a, KF1a, UT1a, sonarn, statusraden och Terminal.

### 2. Resterna i det som är byggt

**Grafen**
- **Linjer genom noder** [direkt]: när ett hopp med fler än åtta aktörer sprids över två kolumner går en linje till
  den andra kolumnen rakt genom en nod i den första (i testärendet linjen till CoinJoin). Linjerna kunde böjas runt
  noderna, eller aktörer med linjer vidare läggas i den närmaste kolumnen.
- **Granskningen mot GR4, GR5 och TK1a** [direkt]:
  - Noden växer inte med namnet och en lång adress.
  - Tokenmärket ska vara en liten rund bricka.
  - Swappen ska vara ett piller på linjen.
  - Kedjornas delar av ytan saknar rubriker.
- **Dolda OP_RETURN går inte att visa igen** [direkt] när toasten tonat ut eller ärendet öppnats på nytt. Lägg en
  markering i grafens hörnrad när några är dolda.
- **GR6c, tredje läget** [direkt]: när startpunkten lagts till ska den riktiga noden få en guidenod *Expandera*
  bredvid sig, med *Hitta uttaget* svagt efter. Det ritas i grafens duk.
  - Avsteg från skissen: Enter är inte grått offline. En startpunkt går att lägga till utan nät (princip 9), och
    inget hämtas förrän den expanderas, så raden säger det i stället.
- **Förmågor utan synlig väg** [direkt]:
  - Dubbelklick på en nod expanderar inte.
  - Passa in (F) och zoom finns bara i verktygstips.
  - Kontrollera att högerklick på tom yta ger något.
- **Formuläret för en egen yta** [direkt] står under grafens hörn, inte vid de valda aktörerna som i KF1b.
- **Numren på kandidaterna** [direkt] står mitt på linjen, inte vid kandidatens nod som i KF2b.
- **Esc med ett formulär öppet och fokus någon annanstans** [direkt]: står fokus i Guidens panel gör Esc ingenting,
  eftersom formuläret tar Esc före panelen. Esc ska då stänga formuläret.

**Soptunnan och startvyn (SO1a, GR8a)**
- **Det som ännu inte går att slänga** [etapp]:
  - Kommentarsnoder och egna ytor. Delete tar i dag bort en lapp förseglat direkt, utan soptunna.
  - En hel hög i startvyn, och ett ärende som ligger i en hög.
- **Startvyns högar** [direkt]:
  - En hög går inte att dra på en annan hög.
  - En hög tar inte fokus med Tab.
  - Startvyn fäller ut korten i en spiral i stället för grafens ring.
  - Grafens samlingsnod får inte samma stöt och centrering genom `FanOutLayer`.
- **Ett nytt ärende med samma nummer som ett slängt** döljs [direkt].
- **Noden krymper inte** när den närmar sig soptunnan, som i skissen [direkt].
- **En bevakningsträff till något i soptunnan** [direkt] visas bara som *i soptunnan · bevakad* på stumpen. Den
  borde visas som en linje med beloppet och ringarna.
- **Test saknas** [direkt] för när inga startpunkter finns kvar och grafen bara är cykler, så att allt följer med.

**Kommentarsnoden (KN1a)**
- **Skrivandet** [direkt]:
  - Enter i en lista fortsätter inte listan, och Tab drar inte in. Gawks `MarkdownEdits` kan tas över.
  - Radavståndet medan man skriver är tätare än de 1,55 som ritas.
  - Hjulet över en lapp som man skriver i zoomar inte grafen.
- **Tråden** [direkt]:
  - En lapp vars adress ligger i en hög får ingen tråd. Den borde gå till högen.
  - `CommentGeometry.Resolve` tolkar bara Bitcoin.
- **Historiken** [etapp]: lappens förseglade poster syns inte i inspektörens *Historik* (AN1b), och en borttagen lapp
  kan inte läggas tillbaka.

**Inspektören, Nytt ärende och Guiden**
- **Nytt ärende (NC2a)** [etapp]:
  - **Ethereums saldo** saknas. Källan ger inget saldo för en adress, så `IEthereumSource` behöver ett uppslag av
    saldot för ETH och tokens.
  - **Kedjans hash vid höjden:** raden visar den senaste tx:ens block, inte hashen för höjden. Esplora har
    `/blocks/tip/hash`, och Blockscout har motsvarande.
  - **Det förhandshämtade som cache:** grafens första expansion hämtar adressen igen. Ett eget steg i Chains kunde
    låta det som inte kan ha ändrats svara som cache.
  - **Sanktionslistans Ethereum-adresser** kontrolleras inte, så brickan visas bara för Bitcoin.
- **Kortets baksida i dagens kurs** [direkt]: efter fråga 2 ovan.
- **Guidens panel** [direkt]:
  - **Länkar:** välj samma nod igen efter att ha följt en länk, så stannar panelen på länken. Klustrets svar, som
    kommer efter en stund, kan byta avsnitt medan man läser.
  - **Tangentbordet:** metodens avsnitt nås bara med högerklick.
  - **Tolkaren:** en avslutande `*` letas efter utan att kod hoppas över.
  - **Smalt fönster:** vid 960 px kapas kommandoraden bredvid den öppna panelen.
- **Källors felrad till toastern** [direkt], som Bevakningens felrad redan är.
- **Bevakningens huvud** [direkt]: skissen säger *2 nya träffar sedan 09:10*, men vyn säger bara antalet. När
  användaren senast tittade behöver sparas som en tid med *sedd*.

**Testärendet** [direkt]: fyll på det med det som byggts sedan det byggdes om. Vad som saknas står i
`docs/BACKLOG.md` under *Testärendet (/testdata)*.

### 3. Godkänt men inte byggt

1. **Begäran, BG1b** [etapp]: kärnan i *Uttag → Begäran*, och det sista steget i det Chainsaw är till för.
   - **Vyn och exporten:**
     - Vyn listar alla uttag som spårade pengar nått.
     - Förhandsvisningen av Markdown, och brickor för insättningarna och spåret dit.
     - *Exportera .md …* och *Exporterade* med SHA-256. Exporten journalförs och skriver aldrig över.
   - **Modulen Requests:** mallar på svenska och engelska, med tx, tider, belopp och insättningsadresser.
   - **Börsernas kontaktvägar** för brottsbekämpande myndigheter, var och en med källa och datum. Inga påhittade
     kontaktvägar.
   - **När den finns:**
     - *Nytt → Begäran* och *Nytt → Etikett* kopplas i `SubmenuTable` (`docs/BACKLOG.md`, *Undermenyer i pillret*).
     - Inspektörens *Begäran om frysning …* (TK1a) visas.
2. **Innehav per adress** [skiss]: Alt + klick vänder noden som ett kort (IN4 skissas först).
   - **Baksidan** visar det som ligger kvar, i rätt tecken (₿, Ξ, ₮) och i inställd valuta till dagens kurs.
   - **Inspektören** visar redan allt som passerat, det som ligger kvar och det som gått ut, vid en blockhöjd.
3. **Grafens text när Windows är utan nät** [direkt]: godkänd 2026-11-08.
4. **Chainabuse automatiskt** [etapp], när nyckeln tillåter.
   - Knappen R finns. En vanlig nyckel ger bara tio anrop i månaden, så uppslag på varje nod kräver partnernyckel.
5. **Länken `chainsaw:` registreras** [direkt] för användaren, och tas bort under Länkar i inställningarna (i dag
   tonat *Inte byggt än*).
   - Registreringen: `HKCU\Software\Classes\chainsaw` med `URL Protocol` och `shell\open\command` till
     `Chainsaw.exe "%1"`.
   - Mottagandet är byggt sedan etapp 11.
6. **Spara ofta** [etapp]:
   - **Varför:** `.cut` skrivs bara vid sparning och stängning, och ett ärende som lämnas vidare ska vara färskt.
   - **Hur:** spara efter skrivningar när det är lugnt, till exempel efter några sekunder utan nya.
   - Arbetskopian tas redan tillbaka efter en krasch, så inget går förlorat i dag.
7. **Återanslutning av tunneln** [direkt]: när tunneln faller, till exempel efter att datorn sovit, ansluter Chainsaw
   inte igen av sig själv. Viktigt för bevakningen.
8. **Meddelandet mitt på skärmen** [skiss]: bevakningens tredje sätt att säga till.
   - Det är byggt bakom samma gränssnitt som de andra sätten (`ScreenMessage`), men syns inte i inställningarna.
   - Skissa raden i ST1a och själva meddelandet, och lägg raden under Låsning.
9. **Sanktionslistan mot grafens adresser och vyn Etiketter** [skiss]: `CheckSanctions` körs när grafen byggs, men
   en träff har ingen samlad vy.

### 4. Skissas först

- **Vyerna som ingår i 1.0**, efter frågan ovan.
- **Rapporten**, efter frågan ovan.
- **Kurserna i gränssnittet:**
  - SEK på Bitcoins linjer, som Ethereums redan har.
  - Stegen bakom *Varför?*.
  - Valet av valuta och upplösning i inställningarna.
- **Förstagångsinställningens steg Utgång:** i dag erbjuds bara en WireGuard-fil eller *Senare*, och VPN är förvalt.
  Steget behöver valet *Windows* eller *VPN* som de två korten i ST1a.
- **Utgångarna:**
  - Ta bort en utgång, byta dess namn och ange platsen (`ExitInfo.Place`).
  - Kontrollera utgången: fråga en källa vilken offentlig adress uppslagen kommer ifrån (*Syns som …*).
- **Ärendet:**
  - Öppna en kollegas fil med ärendelösenordet (`OpenWithPassword`) och lägga till ett eget kuvert.
  - Ärendets egenskaper: titel och delning.
  - Konflikten med arbetskopian: när arbetskopian inte hör till filen ska användaren få välja mellan att öppna filen
    och att spara arbetskopian som ett eget ärende.
- **Återställningsnyckeln i *Om Chainsaw*:** om en nyckel är inbyggd och dess fingeravtryck, och ärligt när den saknas.
- **Sökning i kommandoraden:** etiketter, adresser i ärendet och kluster. Också Guiden därifrån, till exempel
  `/guide utxo`.
- **Tidslisten:**
  - Den blir för smal för ±48 h i valvyn och behöver zoomas runt fönstret.
  - Tidsbanden behöver timmar när man zoomar in.
  - Listens prickade slut vid senaste blocket saknas.
- **Små markeringar i grafen:**
  - En tx som bytt block (omorganisation).
  - *Väntar på källan* i grafens hörn, som statusraden sa förut.

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
