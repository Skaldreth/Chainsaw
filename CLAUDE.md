# CLAUDE.md – Chainsaw

Projektets specifikation och arkitekturval. Läses in automatiskt i varje session.
Läs också `docs/HANDOFF.md` (läget och nästa steg) innan du börjar arbeta. Övriga dokument beskrivs under *Dokumentation* sist.

## Produkt

Chainsaw är ett Windows-verktyg för **spårning av kryptovalutor** i en utredning, i samma anda som Chainalysis Reactor men
inriktat på det en utredare faktiskt behöver: att följa pengarna till **den som tar ut dem**, oftast en börs, och få
fram det som behövs för en begäran om uppgifter.

- **Spåra** transaktioner och adresser i en graf, med klustring av adresser till plånböcker.
- **Attribuera:** känna igen tjänster (börser, mixers, bryggor, sanktionerade adresser) ur öppna källor, egna etiketter och beteende.
- **Bevaka** adresser och kluster, och säga till när pengarna rör sig.
- **Ta fram underlag** för en begäran om uppgifter till en börs, med transaktioner, tider och belopp.

**Ett läroprojekt först** (användarens beslut 2026-11-06): användaren lär sig kryptospårning genom att följa bygget. Målet
på sikt är att verktyget kan användas i tjänsten, så principerna och säkerheten byggs in från början. Användaren sitter
bredvid en kryptospårare som ger synpunkter och provar systemet.

**Inriktningen är underrättelse mer än förundersökning** (användarens beslut 2026-11-06): det viktigaste är att identifiera
vem som står och tar ut pengarna. Bevisvärdet i domstol är inte huvudfrågan nu, men varje slutsats ska ändå kunna förklaras
(princip 3), eftersom det kostar lite att spara och mycket att återskapa.

**Kedjor:** Bitcoin först. Sedan Ethereum med ERC-20 och TRON med USDT (den största brottsvalutan i praktiken). Privacy
coins (Monero, Zcash, Litecoin MWEB) visas först som *Spåret tar slut här*, men modellen hålls öppen för forskningsmetoderna.

### Principer som inte får brytas

1. **Spårbarhet.** Allt Chainsaw hämtar sparas i ärendet med källa, tid (lokal och UTC), vem som hämtade och SHA-256 av det
   råa svaret. Journalen och de hämtade svaren kan bara fyllas på, och en hashkedja förseglar varje rad. `/verify` kontrollerar
   att inget har ändrats. Hämtad data visar alltid **blockhöjd och blockhash** för det den bygger på, så att varje steg i
   spåret går att kontrollera mot kedjan med vilket verktyg som helst.
2. **Kedjan är fakta, allt annat är bedömningar.** Det som står på kedjan (tx, inputs, outputs, belopp, blocktid) är fakta.
   Klustring, attribution, växeladresser och spårade belopp är **slutsatser** som alltid visar varför: vilken heuristik eller
   källa, vilken version, vilken tx. Inget som Chainsaw räknar ut presenteras som ett faktum.
3. **Förklarbart.** En användare ska alltid kunna fråga *varför?* om en sammanslagning, en etikett eller ett belopp och få ett
   svar i klartext, med länk till det det bygger på.
4. **Sekretess.** Ärendet är en krypterad fil. Inga ärendedata skickas till molnet. Uppslag mot externa källor går bara
   genom en vald utgång (VPN), aldrig direkt från datorns nät, och skickar bara det uppslaget kräver (adressen eller tx:en),
   aldrig ärendet.
5. **Inget LLM nu, men en öppen dörr.** Chainsaw bygger inte in någon språkmodell. Mönsterigenkänning (det användaren ser som
   troligast senare) ska kunna läggas till som ännu en analys med namn och version, på samma sätt som en heuristik, utan
   att något annat ändras. En eventuell modell måste vara lokal. AI som själv driver utredningen släpps inte in.
6. **Snabbt.** Gränssnittet får aldrig kännas trögt. All I/O och tung beräkning körs utanför UI-tråden, långa listor
   virtualiseras och grafen ritar bara det som syns.
7. **Så lite brus som möjligt.** Det visuella är det viktigaste. Komplexitet döljs smart men syns ändå, genom **färg, form,
   ikoner, linjer och små markeringar** i stället för text och förklaringar i grafen och dialogerna (användarens krav
   2026-11-07). Smakfullt och stilrent, men alltid med en funktion. Viktig information, som beloppet på en linje, får stå
   kvar; förklaringar och detaljer visas på hovring, när något väljs eller i Guiden.
   En graf som växer okontrollerat är ett fel, inte en funktion.
8. **Moduler äger inte utredningen.** En datakälla, en heuristik eller en kedja ska kunna bytas utan att ärenden, hämtad
   historik eller slutsatser försvinner.
9. **Offline fungerar.** Allt som har hämtats ligger i ärendet, så ett ärende kan öppnas och arbetas med utan nät. Det som
   kräver nya uppslag (expandera nya adresser, bevakning, nya kurser) visar ärligt att det inte går just nu.

### Utredningsmodell

Ärendets gång, organiserad efter hur pengar spåras:

**Startpunkt → Hämta → Klustra → Spåra → Attribuera → Uttag → Begäran → Bevaka → Redovisa**

- **Startpunkt:** en adress, en tx eller ett kluster. Den kommer från en beställning, skrivs in för hand eller öppnas från ett
  annat program med en länk (se *Öppna med en adress*).
- **Hämta:** adressens eller tx:ens data hämtas genom datalagret och sparas i ärendet (princip 1 och 9).
- **Klustra:** heuristikerna slår **automatiskt** ihop adresser till kluster (användarens beslut 2026-11-06). Varje
  sammanslagning sparar heuristik, version och den tx som den bygger på, och kan brytas upp av användaren. En uppbrytning
  sparas, så att samma heuristik inte slår ihop dem igen.
- **Spåra:** användaren expanderar grafen steg för steg. Hur mycket av ett belopp som följer med genom en tx räknas med en
  **vald spårningsmetod** (FIFO, LIFO, haircut eller poison), som alltid visas.
- **Attribuera:** etiketter från sanktionslistor, öppna källor, egna etiketter och beteende (till exempel en börs
  insättningsadresser som töms till samma hot wallet).
- **Uttag:** målet är att hitta den punkt där pengarna lämnar kedjan, oftast en börs, och vilka insättningar det gäller.
- **Begäran:** Chainsaw tar fram en begäran om uppgifter till börsen ur mallar, med tx, tider och belopp.
- **Bevaka:** adresser och kluster kan bevakas, så att användaren får veta när pengarna rör sig. Det är viktigt, särskilt
  inför ett beslag. **Bevakningen går också när Chainsaw är stängt** (användarens beslut 2026-11-07), i en egen liten
  bakgrundsprocess som startar med Windows:
  - Den vet bara det bevakningen kräver: adresserna och ett id för ärendet, krypterat för Windows-användaren (DPAPI), så att
    den kan arbeta utan huvudnyckeln. Ärendet och valvet öppnas aldrig av den.
  - Den går ut genom Chainsaws utgång och gör inga uppslag om utgången inte är ansluten (princip 4).
  - Det den ser sparas i en kö och förs in i ärendet, med journal och hash, först när ärendet öppnas.
  - Aviseringen säger bara att en bevakning har slagit till, inte adressen, beloppet eller ärendet. Detaljerna visas efter
    upplåsning.
- **Redovisa:** hur rapporten byggs bestäms när vi kommer dit (användarens beslut 2026-11-06).

**Mixers är vanliga** i utredningarna (användarens observation 2026-11-07) och är en egen sort i grafen. Spåret tar slut
vid en mixer, men Chainsaw kan föreslå **troliga utgångar** ur belopp (minus avgift), tid och adresstyp. De är kandidater,
alltid streckade och aldrig fakta (princip 2), och användaren godkänner eller avfärdar dem.

**Spåret tar slut** är ett eget, uttalat läge med orsak: privacy coin, mixer eller CoinJoin, brygga eller swap till annan
kedja, börs (uttag funnet), okänd tjänst. Ett slut är aldrig tyst.

**Cross-chain** (Claudes beslut 2026-11-06, användaren lät Claude avgöra): datamodellen har från början en kant *fortsätter
på annan kedja* mellan två tx på olika kedjor. I första versionen känns kända bryggor och swap-tjänster igen som slut på
spåret med *Fortsätter på annan kedja*, och användaren knyter fortsättningen för hand (tx:en på den andra kedjan). Att hitta
fortsättningen själv (belopp och tid hos swap-tjänster, bryggornas egna händelser) kommer senare.

### Datalagret

- **Externa källor först, egen nod senare** (användarens beslut 2026-11-06). Att gå direkt mot kedjan med egen nod och eget
  index är målet, men ryms inte nu: allt ska rymmas på en vanlig Windows-arbetsstation. Därför finns ett **lager** mellan
  Chainsaw och källorna, så att en källa kan bytas ut utan att något annat ändras (princip 8).
- **Ett kontrakt per sorts kedja**, inte per källa: `IBitcoinSource` svarar på adress → tx, tx → inputs och outputs, block och
  höjd. Källan för Bitcoin är först ett **Esplora-API** (samma API som mempool.space och Blockstream), eftersom samma API
  går att köra själv senare. Då blir steget till egen nod bara en annan adress.
- **Det som hämtats hämtas aldrig igen**, om det inte kan ha ändrats (en obekräftad tx eller en adress med nya tx). Varje
  råsvar sparas med hash (princip 1).
- **Mellanhänder bara när de ger något kedjan inte har:** kurser och attribution. Kurser hämtas från en källa som anger
  kursen vid en tidpunkt, och kursen sparas i ärendet med källa. Valutan att räkna i ställs in (SEK som förval).
- **Uppslag kräver inget godkännande** (användarens beslut 2026-11-06), men går alltid genom vald utgång (princip 4).
- Källornas API-nycklar ligger i valvet, aldrig i ärendet.

### Öppna med en adress

Chainsaw är en **egen app** och vet inget om Gawk (användarens beslut 2026-11-06). Andra program, till exempel Gawk, kan
öppna Chainsaw med en adress genom länken `chainsaw:<kedja>/<adress eller tx>` (registrerad för användaren) eller samma
värde som argument till `Chainsaw.exe`. Är Chainsaw redan igång tas länken emot där. Är Chainsaw låst väntar länken tills
den låses upp, och användaren väljer ärende.

## Arkitektur

- **.NET 10 (LTS), WPF**, CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting/DI, Microsoft.Data.Sqlite.Core med SQLite3
  Multiple Ciphers. Samma val som Gawk, men **koden kopieras inte** från Gawk (användarens beslut 2026-11-06). Det som tas
  över är designstilen, temat och gränssnittets komponenter (teckenfältet, scrubbern, tidslinjen, tavlan med kolumner och
  kort), skrivna om för Chainsaw.
- **Modulär monolit med separation of concerns.** Varje modul består av ett `*.Contracts`-projekt (publika gränssnitt och
  typer) och ett implementationsprojekt med `internal`-typer, och registreras med `services.AddXxxModule()`. Moduler beror
  bara på varandras Contracts. Ingen logik ligger i vyer eller knappar.
- Central pakethantering (`Directory.Packages.props`), `TreatWarningsAsErrors`, nullable på. Lösning: `Chainsaw.slnx`.
- **Allt dokumenteras** (användarens krav 2026-11-06): varje typ och metod, också `internal` och `private`, har
  XML-dokumentation som säger vad den gör och, där det inte är självklart, varför.
- **Varje förmåga är ett steg** med tydlig in- och utdata, som kan köras i bakgrunden och misslyckas för sig, och vars
  resultat vet varifrån det kommer (steg, version, data och tid). Det gör att heuristiker, analyser och senare en lokal
  modell kan läggas till på samma sätt.

```
src/
  Chainsaw.App/             WPF-skal: fönster, tema, vyer (MVVM), grafen, kommandorad, inställningar, guiden
    Navigation/             Vyerna och deras kortkommandon i en tabell, utan WPF (testas i tests/Chainsaw.App.Tests)
    ViewModels/             Vymodellerna, utan WPF (testas i tests/Chainsaw.App.Tests)
    Controls/               Egna kontroller och det som behöver WPF: GridIcon, kortkommandon, omvandlare
    Themes/                 Colors, Icons (24-rutnätet), Logo (kättingen), Controls
  Chainsaw.SharedKernel/    Klocka, operatör, kommandon, resultat, kedjor och belopp (satoshi, wei) som egna typer
  Modules/
    Cases/                  Ärenden (en krypterad .cut-fil per ärende), journal, kommandologg, hashkedja
    Security/               Huvudnyckel, valv (API-nycklar), operatörsprofil
    Network/                Utgångar (VPN) och `IExitHttp`: alla Chainsaws förfrågningar går genom en utgång
    Chains/                 Kontrakten per sorts kedja och källorna (först Esplora för Bitcoin); cache i ärendet
    Clustering/             Heuristikerna (namn och version), kluster, sammanslagningar och uppbrytningar
    Attribution/            Etiketter med källa: sanktionslistor, öppna källor, egna, beteende
    Tracing/                Grafen som modell, spårningsmetoder, slut på spåret, kanter till andra kedjor
    Pricing/                Kurser vid en tidpunkt, med källa, sparade i ärendet
    Monitoring/             Bevakning av adresser och kluster
    Requests/               Begäran om uppgifter till börser, mallar
tests/                      xUnit, ett testprojekt per modul
assets/logo/                Loggan som vektor (chainsaw-logo.svg, källan), PNG i 256 px, programikonen chainsaw.ico och
                            kättingens skarv (chainsaw-chain-joint.svg) för låsskärmen
```

### Lagring

- **En fil per ärende:** `<ärendenummer>.cut`, krypterad och lösenordsskyddad på samma sätt som Gawks ärenden (huvudnyckeln
  låser upp). Allt ligger i filen: hämtade svar, kluster, etiketter, grafens läge, bevakningar och journal.
  Varje ärende har en **egen slumpad filnyckel** i nyckelkuvert i filens okrypterade huvud: ett för operatörens valv och
  ett valfritt **delningskuvert** med ett ärendelösenord som kan lämnas till en kollega (användarens beslut
  2026-11-07), och ett kuvert för **organisationens återställningsnyckel** (se *Säkerhet*). Ärendena ligger i
  `%LOCALAPPDATA%\Chainsaw\Ärenden`, inte i Dokument, som ofta synkas till molnet (användarens beslut 2026-11-07). Medan ärendet är öppet arbetar SQLite i en krypterad arbetskopia
  bredvid filen, som skrivs tillbaka vid sparning och tas tillbaka efter en krasch.
- **Appdata** i `%LOCALAPPDATA%\Chainsaw\`: valv, inställningar, delade listor (sanktionslistor, öppna etikettkällor) och
  etikettbiblioteket.
- **Etiketter smittar** (användarens beslut 2026-11-07). En etikett hör hemma i ärendet där den sattes, men det finns också
  ett **samlat etikettbibliotek** på datorn, krypterat i valvet, som sammanställer etiketterna ur alla ärenden man öppnat:
  - När ett ärende öppnas läses dess etiketter in i biblioteket, också när det är en kollegas fil.
  - Biblioteket smittar tillbaka: adresser i ärendet som har en etikett i biblioteket får den i ärendet, så att kollegans
    kunskap följer med nästa gång filen lämnas vidare.
  - En etikett behåller alltid sitt ursprung (vem, när, varför och från vilket ärende den kom) och visas som *från ett annat
    ärende* när den inte sattes här. Den blir en bedömning i det nya ärendet, aldrig ett faktum (princip 2).
  - Samma etikett som kommer in flera gånger slås ihop, och flera oberoende ursprung visas som bekräftelse.
- **Före 1.0 får schemat ändras fritt.** Migreringar får slås ihop och gamla testärenden behöver inte fungera.

### Säkerhet

- **Huvudnyckeln lagras aldrig.** Samma modell som Gawk: en slumpad datanyckel (AES-256-GCM) lindad med en nyckel härledd
  ur huvudnyckeln (PBKDF2-SHA512). Datanyckeln finns i minnet bara medan appen är upplåst.
- **Organisationens återställningsnyckel** (användarens beslut 2026-11-07): varje ärendefil får ett kuvert inlindat med
  organisationens publika nyckel, som är inbyggd i Chainsaw. Den privata nyckeln har bara administratören, och den finns
  aldrig i repot. Glöms huvudnyckeln går valvet inte att återställa (det skapas på nytt), men ärendena kan öppnas av
  administratören.
- **Låsning** automatiskt efter inaktivitet, när Windows låses och med Ctrl+L. En ansluten utgång står kvar när valvet
  låses, eftersom inga uppslag görs medan Chainsaw är låst (användarens beslut 2026-11-07).
- **Bara https ut** (användarens beslut 2026-11-07): `IExitHttp` vägrar `http`, så att utgången aldrig ser innehållet.
- **Utgångar:** Chainsaw har ett **eget exemplar** av hjälpprogrammet för WireGuard och egna utgångar, och är inte beroende
  av att Gawk är igång (användarens beslut 2026-11-07). Samma sorts tunnel som Gawk, men Chainsaws egen.

## Gränssnitt

- **Samma stil som Gawk, egen identitet** (användarens beslut 2026-11-06): mörkt tema i neutrala gråtoner, tangentbordsfokus,
  en flytande kommandorad för power users, svenska i gränssnittet. Loggan är **en kätting** (användarens beslut 2026-11-07, skiss
  ID5): **Chainsaw är blockkedjan som såg**, en ordlek. **Ikonen är två länkar som hakar i varandra, diagonalt, fyllda i
  accentfärgen**, spårad exakt som vektor ur användarens förlaga (så ritas en kedja i Chainsaw)
  (användarens beslut 2026-11-07, ersätter ID5 variant B med ögat). Bitcoin-myntet (ID1) och sågkedjan (ID3) är avvisade. Låsskärmen och
  förstagångsinställningen har teckenfältet i ASCII-stil, som i Gawk, och på låsskärmen **löper kättingen uppifrån och ner
  i vänsterdelen**, skriven i tecken, ganska stor men inte överdriven, och **står still** (användarens beslut
  2026-11-07); bara teckenfältet bakom lever (skiss ID7). Kättingen har **loggans form**: skarven ur loggan upprepad,
  med 40 enheter rak länk mellan skarvarna (`assets/logo/chainsaw-chain-joint.svg`, användarens beslut 2026-11-07).
- **Färger:** Gawks gråskala (bakgrund `#191919`, paneler `#1F1F1F`, upphöjt `#262626`, markerat `#333333`, text `#EFEFEF` /
  `#B4B4B4` / `#8F8F8F`, lyckat `#8CC98F`, fel `#EC8C8C`) med en **egen accentfärg: Magenta `#E070B0`** (användarens beslut 2026-11-07, skiss ID6). Accenten betyder alltid offrens
  pengar och det valda, och krockar inte med grafens färger för börs, risk, annan kedja och tjänst.
- **Typsnitt:** Segoe UI Variable, Cascadia Mono bara för hashar, txid och adresser i inspektören samt terminalen.
- **Grafen är hjärtat.** Det är där man arbetar mest. Den bygger på idéerna från Gawks Byggare (en duk med noder, portar och
  kopplingar), anpassad för spårning. Scrubbern, tidslinjen och kolumner med kort tas över i formen.
  Efter GR1 (användarens beslut 2026-11-07): noderna ritas **tunt som i GR1b** (en rund eller fyrkantig ram med sortens färg,
  namn och en svag rad under), **hoppen i tidsordning** från GR1a är bra struktur, men grafen måste tåla **många linjer åt
  alla håll**, som kryptoanalytikerna arbetar i Reactor. Hur de två förenas skissas i GR2a (fri duk som kan ordnas i hopp)
  och GR2b (hoppen som ryggrad, aktörerna fria i höjdled, pengar som vänder som bågar). **Grafen är en fri yta med en växel
  till hoppkolumner** (användarens beslut 2026-11-07, skiss GR4): aktörerna läggs först ut ungefär i hoppens ordning och står
  sedan där användaren drar dem, åt alla håll. Växeln (två ikoner i grafens huvud) sorterar allt i kolumner per hopp, där
  aktörerna dras upp och ner och pengar som vänder ritas som bågar. Båda lägena kommer ihåg sina platser. GR4 är godkänd
  (användarens beslut 2026-11-07) och är förlagan när grafen byggs.
- **Bildspråket** (skiss GR3a, användarens beslut 2026-11-07: godkänt): formen säger vad det är (rund = aktör vi inte vet något om, fyrkantig = tjänst eller
  kluster), färgen vad det betyder (accent = offrens pengar, blått = börs, rött = spåret tar slut eller risk, lila = annan
  kedja), en ikon per sort (spelsajt och casino är **två spelkort** som hålls i
  en hand, handel och betalning en butik), små markeringar i hörnet (bevakad, etikett från ett annat ärende, uttag funnet, sanktionerad),
  linjens tjocklek är beloppet och **streckat betyder alltid osäkert**. **Viktig information får stå kvar** (användarens
  beslut 2026-11-07): beloppet står alltid på linjerna, och användaren kan gallra till bara de spårade eller inga när grafen
  blir stor. Bildspråket finns bakom ett *?* i grafens hörn.
- **Sorterna efter vad som händer med pengarna** (skiss GR3c, användarens beslut 2026-11-07, införda i GR4): **blått är uttag**
  (börs, börs utan kundkontroll med streckad ram, nästlad tjänst, OTC och P2P, kryptoautomat, kryptokort), **gult är där
  pengarna spenderas** (spel som två spelkort, handel, presentkort), **rött är risk och slut på spåret** (darknet, garantimarknad,
  mixer, CoinJoin, sanktionerad). Markeringen **Kan frysas** (en snöflinga) sitter på adresser med USDT eller USDC, eftersom
  utgivaren kan frysa dem på begäran.
- **Skalet är SH1b** (användarens beslut 2026-11-07, byggt): en smal **ikonlist** till vänster med vyernas ikoner och
  namnet på hovring, i ordningen Nytt · Graf, Tidslinje, Flöde, Exponering, Begäran, Bevakning, Etiketter · Terminal,
  Guide, Inställningar (`src/Chainsaw.App/Navigation/ViewTable.cs`, som också ger kortkommandona). **Sök och kommandon
  finns bara i den flytande raden längst ner** (Ctrl+K eller Ctrl+F), aldrig i huvudet eller som egen vy. Antal visas som
  en prick på ikonen, inte som en siffra.
- **Förstagångsinställningen är FS1a** (stegvis: Välkommen, Huvudnyckel, Utgång, Första ärendet, på låsskärmens fält) och
  **Nytt ärende är NC1a** (en panel från höger som mörkar resten, som kan bli ärendets egenskaper senare). Användaren lät
  Claude välja (2026-11-07); valen följer Gawks form.
- **Vyer** (alla bekräftade 2026-11-06, ordning och utseende skissas): Graf, Tidslinje, Flöde (Sankey: pengar in och ut),
  Tx-inspektör, Adresskort och kluster, Exponering (vilka tjänster ett kluster har betalat till eller fått från), Bevakning,
  Etiketter, Terminal och Guide.
- **Guiden** (användarens önskemål 2026-11-06): förklaringar inne i programmet, som avsnitt man kan läsa, i stil med de
  förklarande anteckningarna till höger i skisserna. En heuristik, ett slut på spåret eller ett begrepp (UTXO, växeladress,
  CoinJoin, peel chain) har ett *?* som öppnar rätt avsnitt. Texterna ligger i repot och skrivs medan funktionerna byggs.
- **Visa aldrig påhittade data** eller funktioner som inte finns. Saknas något, skriv det ärligt ("Inte byggt än", "Offline").
- **Kortkommandon** är viktiga och visas i menyer och verktygstips.
- **Ikoner** ritas på ett rutnät på 24 × 24 med runda ändar, i samma stil som Lucide, och visas med `GridIcon`, som håller
  linjen 1,5 px på skärmen i alla storlekar (sätt aldrig en egen linjebredd). 20 px i listen, 16 px i knappar, minst 12 px.

## Arbetssätt

Samma arbetssätt som Gawk (användarens beslut 2026-11-06):

- **Språk:** svara användaren på **svenska**. Kod, identifierare och kommentarer skrivs på **engelska**.
- **Kodkvalitet:** professionell C#. En regel uttrycks en gång, inga nästlade ternärer, rader under ungefär 120 tecken,
  metoder som gör en sak, inga magiska strängar.
- **UI skissas alltid först** som en ny tavla på Chainsaws canvas (https://claude.ai/artifact/LVMLEqgJteM68jjc7vuq1h), och
  implementeras först när användaren sagt ok. Skisser
  visas i **två varianter sida vid sida** när formen inte är given. Undantaget är felrättningar som inte ändrar utseendet.
- **Förklara medan vi bygger.** Användaren lär sig genom att följa flödet: när något nytt om kedjorna dyker upp (en
  heuristik, ett format) förklaras det kort, och texten blir ett avsnitt i Guiden.
- **Beslut skrivs in direkt** i rätt dokument (CLAUDE.md, HANDOFF, TODO), committas och pushas så fort det är taget.
- **Inget tappas bort:** allt som diskuteras men inte byggs nu står i `docs/TODO.md`.
- **Processen efter storleken:** rena gränssnittsändringar byggs direkt och granskas av sessionen själv mot skissen. Ändringar
  som skriver i ärendet eller rör principerna byggs som en etapp med oberoende granskning av en granskande agent.
  Sessionen bygger själv och lämnar aldrig bygget till en annan session, **utom i en nattkörning**.
- **Nattkörning** (användarens beslut 2026-11-07, som i Gawk): när användaren sover byggs en etapp i taget, **var och en i
  en ny session** så att den får en färsk kontext. Huvudsessionen orkestrerar: startar etappen, väntar på rapporten,
  kontrollerar bygget och testerna och startar nästa. Hur en etapp byggs står i `docs/HANDOFF.md` under *Så byggs
  etapperna*. Gränssnitt som inte är skissat och godkänt byggs aldrig i en nattkörning; det skissas i stället och väntar.
- **Korta meddelanden:** användaren skriver ofta kort medan arbetet pågår. Ta hand om dem i ordning och säg kort vad som görs.
- **README nämner aldrig Polisen**, varken myndigheten eller ord som pekar dit. Skriv *utredning* och *beställning*.
- **Testning:** molnmiljön är Linux och kan inte köra WPF (bygget fungerar med `EnableWindowsTargeting`). Användaren provar
  på Windows. Det som ska provas där skrivs i `docs/TESTER-INFOR-1.0.md`.
- **Heuristiker testas mot ett facit:** syntetiska tx-mönster (peel chain, CoinJoin, konsolidering, börsens sweep) som
  testdata, så att varje heuristik har tester som visar vad den slår ihop och vad den låter bli.
- **.NET i molnmiljön:** `apt-get update && apt-get install -y dotnet-sdk-10.0`. Bygg och testa med `dotnet build` och `dotnet test` i roten.

## Dokumentation

Varje uppgift står på ett ställe. Historiken finns i git.

| Fil | Innehåll | Läses |
| --- | --- | --- |
| `CLAUDE.md` | Specifikation, arkitektur, gränssnitt och arbetssätt | Alltid (automatiskt) |
| `docs/HANDOFF.md` | Läget nu och nästa steg, kort | I början av varje session |
| `docs/TODO.md` | Backloggen: önskemål, öppna frågor och idéer som inte är byggda | När nästa uppgift väljs |
| `docs/DEV-LOG.md` | En post per byggd etapp: egna beslut, avsteg och det som återstår | När en etapp byggs eller granskas |
| `docs/TESTER-INFOR-1.0.md` | Det som ska provas på Windows före 1.0 | När det fylls på eller när användaren testar |

- **HANDOFF hålls kort.** Det som är klart tas bort. Ett beslut som ska gälla framåt flyttas hit.
- **En punkt i TODO som byggs** tas bort därifrån.
- **Skisserna** finns på canvasen. Dokumenten nämner bara skissens namn där det behövs.
