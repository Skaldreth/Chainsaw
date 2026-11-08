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
- **Breda noder i resten av grafen** [direkt]: noden växer med namnet (GR5), men *Sortera grafen* (`TidyLayout`)
  räknar fortfarande med 132 px breda noder, och en ny kommentar vid en bred nod (`CommentGeometry.PlaceOfNew`)
  placeras efter samma bredd.

**Soptunnan och startvyn (SO1a, GR8a)**
- **Grafens samlingsnod trycks inte ut** [direkt]: stöten och centreringen är som startvyns, men ytan bakom trycks
  inte ut lite som i `FanOutLayer` (grafen ritas i en `DrawingContext`, så det kräver en egen skalning av det dimmade).

**Inspektören, Nytt ärende och Guiden**
- **Kortets baksida i dagens kurs** [direkt]: efter fråga 2 ovan.
- **Källors felrad till toastern** [direkt], som Bevakningens felrad redan är.
- **Bevakningens huvud** [direkt]: skissen säger *2 nya träffar sedan 09:10*, men vyn säger bara antalet. När
  användaren senast tittade behöver sparas som en tid med *sedd*.

- **Guidens panel** [direkt]:
  - **Länkar:** välj samma nod igen efter att ha följt en länk, så stannar panelen på länken. Klustrets svar, som
    kommer efter en stund, kan byta avsnitt medan man läser.
  - **Tangentbordet:** metodens avsnitt nås bara med högerklick.
  - **Tolkaren:** en avslutande `*` letas efter utan att kod hoppas över.
  - **Smalt fönster:** vid 960 px kapas kommandoraden bredvid den öppna panelen.

- **Återanslutningen i utgångsrutan** [direkt]: titelraden säger *Ansluter igen …* med nästa försök, men utgångsrutan
  (UT2b) visar bara *Offline* i rött medan tunneln ansluts igen.

**Testärendet** [direkt]: fyll på det med det som byggts sedan det byggdes om. Vad som saknas står i
`docs/BACKLOG.md` under *Testärendet (/testdata)*.

### 3. Godkänt men inte byggt

1. **Begäran, resterna efter BG1b** [direkt]:
   - **Markeringen i grafen:** skissen ger börsen en liten blå markering i hörnet när en begäran är exporterad. Den
     är inte ritad, eftersom grafen byggdes om samtidigt; listan i vyn Begäran har den gröna markeringen.
   - **Kontaktvägar:** katalogen är tom, eftersom ingen kontaktväg gick att kontrollera mot sin källa när modulen
     byggdes (`ExchangeContacts.Catalogue`). Lägg in verifierade vägar med källa och datum.
   - **Fler mallar** per börs, om kryptospåraren vill det.
2. **Chainabuse automatiskt** [etapp], när nyckeln tillåter.
   - Knappen R finns. En vanlig nyckel ger bara tio anrop i månaden, så uppslag på varje nod kräver partnernyckel.
3. **Säga till när sparningen inte går** [direkt]: ärendet sparar sig självt efter skrivningar (Spara ofta, byggt
   2026-11-08), och en sparning som misslyckas provas igen efter 1, 2, 4 och sedan högst 10 minuter utan att något sägs.
   - Arbetskopian har allt och tas tillbaka efter en krasch, så inget går förlorat, men `.cut` är då inte färsk.
   - Förslag: en varning i toastern (TO1a) när sparningen misslyckats några gånger i rad, och *Försök igen*. Kräver en
     händelse på `ICase` som appen lyssnar på.
4. **Godkänna en plånbok, resterna** [direkt]:
   - Ctrl+Z och *Ångra* i KF2b-panelen visar ingen bekräftelse av vad som slås ihop igen; det gör bara *Historik*.
     Behöver en liten skiss.
   - Ett förslag som ändrats sedan det förseglades ger det allmänna felet; det borde säga att kontrollerna behöver
     köras igen.

### 4. Valt 2026-11-08 och ska byggas

Skisserna på canvasens sida *Mot 1.0*; vad varje val innebär står i CLAUDE.md under *Valt för 1.0*.
- **Grafen** [direkt]: IN4a kortets baksida, TL2a tidslisten, GR9a bytt block och *Väntar på källan*.
- **Etiketter ET1a** [etapp], med sanktionsträffarna samlade.
- **Exponering EP1b** [etapp].
- **Rapport RP1b** [etapp].
- **Kurserna KU1a** [etapp]: kronorna via Riksbanken.
- **Inställningarna** [direkt]: BV4a meddelandet mitt på skärmen, ST2a återställningsnyckeln, FS2a steget Utgång
  (Windows förvalt) och UT3b utgångarna med kontrollen.
- **Ärendet ÄR1b** [etapp]: kollegans fil, egenskaperna med kuverten och konflikten med arbetskopian.
- **Sökningen, resterna efter KR1a** [direkt]: det slängda i soptunnan söks inte (raden kunde säga *I soptunnan* med
  *Lägg tillbaka*), och en etikett från ett annat ärende kan inte säga vilket, eftersom ursprunget saknar ärendenumret.

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
