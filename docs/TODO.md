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

- **Spårningen efter en tx som inte finns** [etapp]: linjerna och aktörerna efter den ritas streckade, men Tracing
  läser fortfarande det sparade svaret, så de spårade beloppen och *Spårat* räknar med den.
- **Ersatt av vilken tx** [etapp]: en tx som källan inte längre känner (`404`) visas som *Finns inte längre*; vilken
  tx som spenderar samma pengar i stället kräver uppslag på de spenderade utgångarna.
- **Ångra en godkänd plånbok** (PL1b, vald): Ctrl+Z och *Ångra* i KF2b-panelen bekräftas i grafen.
- **Spåret genom en swap** (SW1a, vald) [etapp]: värdet följer med, och valet per swap i pillret.
- **En DEX:s router i grafen** [skiss]: listan över kända kontrakt (omgång 2) har Uniswaps, 1inchs och 0x:s routrar,
  men de får ingen etikett, eftersom ingen sort i bildspråket passar: *Swap eller brygga* avslutar spåret som
  *Fortsätter på annan kedja*, och ett byte på samma kedja byter inte ägare. Namnet står i *Varför?* för swappen. En
  egen sort, eller en markering i hörnet, skissas.
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
2. **Ethereum klart:** spårningen genom en swap (när regeln är beslutad), Tornado Cash och kända DEX:ar och bryggor,
   OFAC:s ETH-adresser, kontoheuristikerna, kontrollerna och plånböckerna på Ethereum, egna ytor, Etherscan med nyckel
   och fynden från etapp 23 och 23b.
3. **Bitcoin-källan, bevakningen och bakgrunden:** fynden under *Bitcoin-källan*, *Bevakning* och *Bevakning i
   bakgrunden*, och bevakning i realtid (websocket).
4. **Klustring, förvirring och kontroller:** tjänster ur beteende, fler tecken för växeln, CoinJoin och peel chain,
   centrala mixers, damm och adressförgiftning, jämna belopp och tidsmönster, och fynden under *Kontroller*.
5. **Attribution och kurser:** EU:s sanktionslista, öppna källor (efter licens), etiketter på tx, och fynden under
   *Attribution* och *Kurser*.
6. **Säkerhet, ärenden och utgångar:** byggt 2026-11-08 (DEV-LOG, *Omgång 6*). Kvar, som behöver ritas först:
   - [skiss] **Vem har tillgång / byt filnyckel:** en knapp i fliken *Kuvert* som byter filnyckeln (krypterar om
     databasen och lindar om kuverten för valvet och organisationen; delningslösenordet och andra valv faller bort),
     så att tillgång kan dras tillbaka, med en bekräftelse som visar vilka kuvert som försvinner.
   - [skiss] **En proxy som kräver inloggning:** en rad under Inställningar › Nätverk som låter Chainsaw skicka
     Windows-användarens uppgifter, bara till en proxy som är inställd för hand (aldrig en som hittats med WPAD).
   - [skiss] **En ansluten utgång i läget Windows:** strömknappen kvar på den anslutna raden i utgångsrutan medan
     listan i övrigt är tonad, så att den kan kopplas ner utan att välja VPN.
   - [skiss] **Den egna nodens certifikat:** fästa en lokal endpoints certifikat (pinning) på sidan Källor.
   - [skiss] **Välja ett befintligt ärende för en länk** efter upplåsningen (*lägg i ärende …*), i stället för att en
     länk alltid öppnar Nytt ärende.
   - [skiss] **Ankarna och flera datorer:** hur `/verify` och journalen förklarar en rad *case.anchors-replaced*
     (äldre kopia, parallella ändringar på två datorer, ankare som rullats tillbaka).
7. **Vyerna och appen:** byggt 2026-11-08 (DEV-LOG, *Omgång 7*). Kvar, som behöver ritas först:
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
8. **TRON med USDT**, sedan **Litecoin**, **BSC**, **Solana** och den genomskinliga delen av **Zcash**.
9. **Egen nod och eget index**, hitta fortsättningen på en annan kedja själv, och mönsterigenkänning som analys
   (utan språkmodell, princip 5).

**Ur omgång 4 (klustring, förvirring och kontroller), väntar på en yta** [skiss]:
- **Dra tillbaka en version:** `IClusteringService.Withdraw` finns, men ingen knapp eller rad i gränssnittet; var
  den står (inspektörens *Varför?*, Guiden eller Inställningar) och hur bekräftelsen ser ut skissas.
- **Slutsatser som hålls tillbaka** (`ClusteringRun.HeldBack`): när en körning saknar en tx som en sparad slutsats
  bygger på står den gamla slutsatsen kvar; gränssnittet bör visa det (en obekräftad tx som ersatts kan aldrig
  skickas med igen).
- **Uppbrytning av ett par som inte hör ihop:** gränssnittet bör föreslå länken på vägen mellan dem
  (`ExplainConnection`) och varna när de inte är i samma kluster.
- **Damm och den liknande adressen tonas ned** i grafen (`dust v1` markerar dem med ett område men tonar inte ned).
- **Många hopp fälls ihop** till en länk i grafen (`pass-through v1` markerar raden med ett område).
- **En trolig tjänst blir en etikett:** `service-behaviour v1` är en kontroll; att godkänna den som en egen etikett
  (med namn) behöver en form.
- **Testärendet** (`/testdata`) visar inte de nya kontrollerna än: en uppdelning med delar som möts, damm, en
  adressförgiftning, ett svep till Sydbörsens hot wallet och en möjlig PayJoin behöver läggas in i berättelsen
  (`TestStoryChain`) med stegen som hämtar dem [direkt].

**Beslutat 2026-11-08** (står i CLAUDE.md): spårningen genom en swap (förvalet följer värdet, utredaren väljer per
swap; valet i pillret skissas [skiss]), VPN i bakgrunden (byggs i omgång 3) och metoderna för privacy coins
(omgång 9). **LIFO och poison** väntar på kryptospåraren (`docs/KRYPTOSPARAREN.md`, fråga 1).

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
