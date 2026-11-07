# Grafen och bildspråket

Grafen är där spåret följs. Varje **nod** är en aktör pengarna passerar: en adress, ett kluster av adresser med samma
ägare, en tx med flera ägare (till exempel en CoinJoin) eller en tx på en annan kedja. Varje **linje** är pengar som
gick från en aktör till en annan, summerade per par.

Grafen visar så lite text som möjligt. Det mesta syns i **form, färg, ikon och linje**, och resten visas när du håller
pekaren över något eller väljer det. Hela bildspråket finns också bakom *?* i grafens nedre hörn.

## Formen säger vad det är

- **Rund:** en aktör vi inte vet något om, till exempel en ensam adress utan etikett, eller offret.
- **Fyrkantig:** en tjänst eller ett kluster. Ett kluster har en skugga av en andra ram bakom sig, och antalet adresser
  i hörnet.

## Färgen säger vad som händer med pengarna

- **Magenta (accenten):** offrets pengar och startpunkten. Accenten betyder också det du har valt.
- **Blått: uttag.** Här lämnar pengarna kedjan och här kan man begära uppgifter: börs, börs utan kundkontroll (streckad
  ram, eftersom det är tveksamt att en begäran ger något), nästlad tjänst, OTC och P2P, kryptoautomat och kryptokort.
- **Gult: spenderat.** Spel (två spelkort i en hand), handel och presentkort.
- **Rött: risk och slut på spåret.** Darknet, garantimarknad, mixer, CoinJoin och sanktionerad (ett ljusare rött).
- **Lila: annan kedja.** En swap eller brygga, och tx:en på den andra kedjan med kedjans namn under hörnet.
- **Grått:** okänd adress, kluster utan etikett och tx med flera ägare.

Sorten kommer från **etiketterna** (avsnittet *Etiketter*). En etikett är en bedömning, aldrig ett faktum. Utan etikett
är startpunkten ett offer och resten okänt.

## Markeringar i hörnet

Små runda tecken i nodens övre hörn, aldrig text:

- **Uttag funnet** (blå bock): spårade pengar har nått ett uttag.
- **Etikett från ett annat ärende** (grå etikett): etiketten satt i ett annat ärende och kom hit genom biblioteket.
- **Sanktionerad** (röd): adressen står på en sanktionslista, när en annan etikett säger vad det är.

*Bevakad* och *Kan frysas* kommer när bevakningen och kedjorna med USDT finns.

En nod som **inte är expanderad** är blek: det som går vidare därifrån finns inte i grafen än.

## Linjerna

- **Tjockleken är beloppet:** ju mer av de spårade pengarna, desto tjockare. Linjer utan spårade pengar är tunna och
  grå: det är andras pengar i samma tx.
- **Streckat betyder alltid osäkert:** en obekräftad tx, ett svar som kan ha ändrats, eller (med långa streck och i
  lila) pengar som fortsätter på en annan kedja genom användarens egen länk.
- **Pengar som vänder** tillbaka till ett tidigare hopp ritas som en båge när grafen står i hoppkolumner.
- **Beloppet står på linjen.** Knappen i hörnet växlar mellan *Alla belopp*, *Spårade belopp* och *Inga belopp* när
  grafen blir stor. Står det *0,1570 av 0,2400* gick 0,2400 BTC på kedjan, och 0,1570 av dem räknas som offrets
  pengar enligt spårningsmetoden.

## Fri yta och hoppkolumner

Två ikoner i grafens hörn växlar mellan två lägen, och båda kommer ihåg var du ställt aktörerna:

- **Fri yta:** aktörerna läggs först ut i hoppens ordning, och står sedan där du drar dem, åt alla håll.
- **Hoppkolumner:** en kolumn per hopp från startpunkten (*Start*, *+1*, *+2*, och *−1* före den). Aktörerna dras bara
  upp och ner. Hoppen är grafens ordning, inte ett faktum om vem som äger vad.

Dra den tomma ytan för att flytta grafen, och använd hjulet för att zooma. **F** passar in hela grafen, **+** och **−**
zoomar, **Esc** väljer inget och **E** expanderar det valda.

## Spårningsmetoden

Knappen *FIFO* eller *haircut* i hörnet visar alltid vilken metod de spårade beloppen bygger på, och växlar metod.
Överst till vänster står hur mycket som spårades, från hur många mynt och med vilken metod och version. Spåret räknas
från de mynt som startpunkten tog emot (eller från en startpunkts-tx:s betalningar till andra än betalarna). Se
avsnittet *Spårningsmetoder*.

## Inspektören och Varför?

Välj en nod eller en linje, så visar inspektören till höger:

- **fakta:** belopp in och ut, spårat in, sort, hopp och tid (lokal tid och UTC);
- **adresserna och tx:erna i sin helhet**, med blockhöjd och blockhash, så att varje steg kan kontrolleras mot kedjan
  med vilket verktyg som helst;
- **Expandera**, som hämtar ett steg till genom utgången. Offline svarar ärendet med det det redan har, och Chainsaw
  säger ärligt vad som inte gick att hämta;
- **Varför?**, som förklarar varje slutsats: etiketten och dess ursprung, klustret och vilken heuristik (med version)
  som slog ihop vilka adresser ur vilken tx, varför spåret tar slut, och det spårade beloppet med metod och tx. Varje
  förklaring har en länk till rätt avsnitt i Guiden.
