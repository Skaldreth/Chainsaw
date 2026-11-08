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

**Från valen 2026-11-08:**
- **Meddelandet mitt på skärmen** (BV4a) är av från början; i skissen var det på. *Nu:* av.
- **Operatörens namn, titel och enhet** står i rapportens huvud (RP1b), som i Begäran. *Nu:* de står där.

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

Ordningen: först provet på Windows, sedan resterna i det som redan är byggt. Allt som valdes på sidan *Mot 1.0* är
byggt; det som står här är rester. Inget nytt byggs förrän det som finns känns bra (användarens beslut 2026-11-07).

### 1. Provet på Windows

Inget sedan etapp 1 är provat på Windows. Användaren provar efter `docs/TESTER-INFOR-1.0.md` (rensas och ordnas
uppifrån och ner [pågår]), och det som inte fungerar rättas före allt annat nedan. Särskilt:
- **Kraschen vid start** (`NewCasePanel`) är rättad men inte bekräftad.
- **Expansionen gav *Inget nytt att hämta*** med mempool vald (rättat 2026-11-07, väntar på prov).
- **Allt som byggdes 2026-11-08** är osett: KF2b, NC2a, KN1a, SO1a, GR7a, TL1a/TL2a, IN4a, GR9a, BG1b, ET1a, EP1b,
  RP1b, KU1a, BV4a, ST2a, FS2a, UT3b, ÄR1b, KR1a, Spara ofta, plånböckerna och soptunnan för lappar och högar.

### 2. Rester som byggs nu [pågår]

**Grafen**
- **Breda noder:** *Sortera grafen* (`TidyLayout`) och en ny kommentar (`CommentGeometry.PlaceOfNew`) räknar med
  132 px breda noder.
- **Samlingsnoden i grafen** trycker inte ut ytan bakom som `FanOutLayer` gör i startvyn.
- **Linjerna efter en tx som inte finns** streckas inte, med aktörerna efter.
- **Bytt block på Ethereum:** bara Bitcoins svar jämförs (gäller det som hämtades medan blocket var grunt).
- **Återanslutningen i utgångsrutan** (UT2b) visar *Offline* i stället för *Ansluter igen …*.

**Vyerna och inställningarna**
- **Begäran:** börsens blå markering i grafen när en begäran är exporterad.
- **Säga till när sparningen inte går:** en varning i toastern med *Försök igen* när Spara ofta misslyckats några
  gånger i rad (en händelse på `ICase`).
- **Godkänna en plånbok:** ett förslag som ändrats sedan det förseglades ska säga att kontrollerna behöver köras igen.
- **Exponering (EP1b):** kronorna under beloppen, mixerns godkända utgångar som streckade förslag bortom mixern,
  *Tillbaka · Esc* i grafen och *Exponering …* i nodens meny; vägen ska stanna på samma ställen som grafen.
- **Inställningarna:** *Syns som* på utgångens tips i titelraden, och kontrollen av Windows som utgång.
- **Sökningen (KR1a):** det slängda i soptunnan söks, med *Lägg tillbaka*.
- **Det instabila testet** i App.Tests.

**Testärendet** (`/testdata`): det som byggts sedan det byggdes om, ett bytt block och påhittade kurser, så att det
aldrig skickar riktiga uppslag (listan i `docs/BACKLOG.md`, *Testärendet*).

### 3. Kvar efter omgången

- **Ersatt av vilken tx** [etapp]: en tx som källan inte längre känner (`404`) visas som *Finns inte längre*; vilken
  tx som spenderar samma pengar i stället kräver uppslag på de spenderade utgångarna.
- **Ångra en godkänd plånbok** [skiss]: Ctrl+Z och *Ångra* i KF2b-panelen visar ingen bekräftelse av vad som slås
  ihop igen; det gör bara *Historik*.
- **Sökningen:** en etikett från ett annat ärende kan inte säga vilket, eftersom ursprunget saknar ärendenumret
  [etapp].
- **Begäran:** kontaktvägar till börserna (katalogen är tom tills en väg kan kontrolleras mot sin källa, med källa
  och datum) och fler mallar per börs, om kryptospåraren vill det.
- **Chainabuse automatiskt** [etapp], när en partnernyckel finns: en vanlig nyckel ger tio anrop i månaden, så nu
  frågas den bara med R.

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
