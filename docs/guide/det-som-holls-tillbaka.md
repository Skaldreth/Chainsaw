# Det som hölls tillbaka, högen och valvyn

> **Som i vardagen:** en adress med tusentals tx är som en brevlåda som har fått tusentals brev. Du tömmer inte allt på
> skrivbordet. Du lägger högen bredvid dig, sorterar fram de brev som kan ha med saken att göra, och lägger bara dem
> framför dig. Resten ligger kvar i högen, orörda men inte borta.

En börs, en spelsajt eller en betaltjänst kan ha tusentals tx på en adress. Ritas alla blir grafen oläslig, och en graf
som växer okontrollerat är ett fel, inte en funktion (princip 7). Därför håller Chainsaw tillbaka det som inte får plats,
men säger alltid att det finns.

![En expansion med många motparter: ledtråden +N tx (streckad) och högen med en tjock linje och summan](figur:s-hogen)

## Ledtråden +N tx

En expansion hämtar högst 50 tx. Finns det fler står en **streckad rund nod** vid adressen, till exempel `+243 tx`, med
en tunn streckad linje utan belopp. Streckad betyder här *inte hämtat än*. Ledtråden finns kvar när ärendet öppnas igen.

## Högen

Har en expanderad aktör **fler än åtta nya motparter** på en sida, läggs de i en **hög**: en fyrkantig nod som ser ut som
en kortlek, med antalet och summan och en tjock linje. Pengar ut och pengar in blir var sin hög.

Aldrig i en hög: startpunkter, expanderade aktörer, aktörer där spåret tar slut (börs, mixer, sanktionerad), länkar till
annan kedja, motparter med en etikett, och de som spårade pengar når. Offrets pengar ska alltid synas.

**Ett klick på högen** fäller ut korten i en ring runt den, medan resten av grafen dimmas. Klick väljer ett kort,
Shift-klick fler. Dra ut ett kort på den dimmade ytan, så blir det en egen nod där du släpper det, med de andra valda
under. *Lägg tillbaka i högen* i nodens ikonlist lägger tillbaka en utplockad nod. Ett nytt klick på högen fäller ihop
den.

## Valvyn

**Ett klick på ledtråden** läser adressens historik genom källan (*Läser historiken …*; Esc avbryter, och det som redan
hämtats ligger kvar i ärendet) och öppnar **valvyn**: grafen skickas ut i en cirkulär stöt, och tx:erna läggs som
prickar runt adressen.

![Valvyn: högen i mitten och tx:erna utfällda runt den över en nedtonad graf](figur:s-valvyn)

**Två lägen:**

- **Spiral:** tx:erna på en spiral ut från adressen, störst eller nyast innerst.
- **Tidsbåge:** vinkeln är tiden och avståndet beloppet.

**Filtren** rensar bort bruset:

- **Belopp** med två reglage och fördelningen ritad bakom, så att du ser var de flesta ligger.
- **In, ut eller båda.**
- **Tidsfönster** runt den spårade ankomsten: ±48 timmar som förval, ±7 dygn eller hela tiden. Dra fönstret längs tiden.
- **Motpartens sort** som brickor.
- **Dölj damm:** belopp under 0,0001 BTC, ofta adressförgiftning (någon skickar småbelopp från en adress som liknar en du
  använt, i hopp om att du ska kopiera fel adress).
- **Bara de som inte finns i grafen**, och **en prick per motpart** med antalet tx.

En räknare säger hur många som visas, till exempel *42 av 243*.

**Det som är av extra intresse pulserar:** en motpart med en negativ etikett, eller ett belopp nära det som spårades
in. Hörnmärken visar en motpart som redan finns i grafen och en etikett från ett annat ärende.

**Välja:** klick, Shift-klick för ett intervall, en ring som du drar runt prickarna (L), *De tio största* eller *Alla
som pulserar*. **Plocka ut** (Enter) lägger de valda i grafen. Esc stänger utan att ändra något.

## Det som är sant och det som är bedömt

Belopp ur en tx där flera betalade in tillsammans (en CoinJoin eller en utbetalning från en börs) räknas som adressens
andel. Andelen är en uträkning, inte ett faktum, och ritas därför **streckad** och sägs vara osäker (princip 2).

Det du plockar ut och vad som ligger i högen sparas i ärendet som grafens läge, inte som en slutsats.

Se också [Börser och uttag](borser.md) och [Uppdelning i många små belopp](uppdelning.md).
