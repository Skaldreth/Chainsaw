# Grafen och bildspråket

Grafen är där spåret följs. Varje **nod** är en aktör pengarna passerar: en adress, ett kluster av adresser med samma
ägare, en tx med flera ägare (till exempel en CoinJoin) eller en tx på en annan kedja. Varje **linje** är pengar som
gick från en aktör till en annan, summerade per par.

Grafen visar så lite text som möjligt. Det mesta syns i **form, färg, ikon och linje**, och resten visas när du håller
pekaren över något eller väljer det. Hela bildspråket finns också bakom *?* i grafens nedre hörn.

## Formen säger vad det är

- **Rund:** en aktör vi inte vet något om, till exempel en ensam adress utan etikett, eller offret (en etikett).
- **Fyrkantig:** en tjänst eller ett kluster. Ett kluster har en skugga av en andra ram bakom sig, och antalet adresser
  i hörnet.

## Färgen säger vad som händer med pengarna

- **Magenta (accenten):** offrets pengar och offret, som är en etikett du sätter. Accenten betyder också det du har
  valt.
- **Blått: uttag.** Här lämnar pengarna kedjan och här kan man begära uppgifter: börs, börs utan kundkontroll (streckad
  ram, eftersom det är tveksamt att en begäran ger något), nästlad tjänst, OTC och P2P, kryptoautomat och kryptokort.
- **Gult: spenderat.** Spel (två spelkort i en hand), handel och presentkort.
- **Rött: risk och slut på spåret.** Darknet, garantimarknad, mixer, CoinJoin och sanktionerad (ett ljusare rött).
- **Lila: annan kedja.** En swap eller brygga, och tx:en på den andra kedjan med kedjans namn under hörnet.
- **Grått:** okänd adress, kluster utan etikett och tx med flera ägare.

Sorten kommer från **etiketterna** (avsnittet [Etiketter](etiketter.md)). En etikett är en bedömning, aldrig ett faktum. Utan
är en adress okänd och ett kluster ett kluster, också startpunkten.

## Startpunkten är neutral

En startpunkt behåller sin sort: en okänd adress är rund och grå, ett kluster fyrkantigt. Bara en **liten flagga uppe
till vänster** säger att spåret började här, eftersom startpunkten lika gärna kan vara den misstänktes plånbok som
offrets. Är någon ett offer sätter du etiketten *Offer*, som gör noden magenta.

## Namnen

En nod utan namn heter efter sin adress, förkortad i mitten (`bc1qdlm…37yr`) bara när den inte får plats. En nod med
namn, från en etikett eller ett eget namn, visar namnet och adressen under i nedtonat grått. Hela adressen står alltid
i inspektören.

## Markeringar i hörnet

Små runda tecken i nodens övre hörn, aldrig text:

- **Startpunkt** (en ljus flagga, uppe till vänster): spåret började här. De andra markeringarna sitter uppe till höger.
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
zoomar, **Esc** väljer inget och **E** expanderar det valda. **L**, **U** och **K** öppnar inspektörens formulär
(nedan).

## Spårningsmetoden

Knappen *FIFO* eller *haircut* i hörnet visar alltid vilken metod de spårade beloppen bygger på, och växlar metod.
Överst till vänster står hur mycket som spårades, från hur många mynt och med vilken metod och version. Spåret räknas
från de mynt som startpunkten tog emot (eller från en startpunkts-tx:s betalningar till andra än betalarna). Se
avsnittet [Spårningsmetoder](sparningsmetoder.md).

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

## Etikett, Bryt upp och Annan kedja

Under fakta i inspektören finns tre knappar. Formuläret fälls ut där, grafen står orörd bredvid, **Esc** avbryter och
**Enter** sparar. Allt som sparas förseglas i ärendet med en rad i journalen, och grafen ritas om ur ärendet.

- **Etikett (L)** på en adress eller ett kluster: namn, sort (ikonerna ur bildspråket), källa (egen bedömning, öppen
  källa med namn, beställningen eller svar från börs), skäl och *Sprid inte*, som håller etiketten borta från
  biblioteket och andra ärenden. Sorten *Okänd* med ett namn är ett eget namn på aktören. Se avsnittet [Etiketter](etiketter.md).
- **Bryt upp (U)**, bara på ett kluster: bocka för adresserna som ska ut. Varje rad visar heuristiken som slog ihop
  den (till exempel *CIO v1*, common-input-ownership). Chainsaw bryter varje sammanslagning mellan en adress som ska ut
  och en som stannar, med samma heuristik och ditt skäl. Uppbrytningen **håller**: samma heuristik slår inte ihop dem
  igen, och en annan heuristik som skulle göra det visas bara som förslag.
- **Annan kedja (K)**: pengarna fortsätter på Ethereum eller TRON genom en brygga eller swap. Välj kedjan och klistra
  in tx:en, som känns igen direkt (en Ethereum-tx börjar med `0x`). Kom flera mynt in till aktören väljer du vilket som
  gick vidare. Länken ritas lila och streckad, eftersom den är din bedömning och inte något kedjan säger.
