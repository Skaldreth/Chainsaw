# Röra sig i grafen

> **Som i vardagen:** grafen är ett stort bord med lappar och snören. Du skjuter bordsskivan dit du vill titta, böjer dig
> närmare för detaljer och tar ett steg tillbaka för helheten. Lapparna ligger kvar där du lade dem.

## Panorera och zooma

- **Höger- eller mittknappen** drar ytan, också när pekaren står på en nod. Släpper du i farten glider ytan vidare och
  saktar in, som när man skjuter ett papper över ett bord.
- **Ett högerklick utan rörelse** öppnar nodens meny.
- **Vänsterknappen** väljer och drar noder. Ctrl+klick väljer flera.
- **Dra en ruta** med vänsterknappen från tom yta för att välja flera, som på skrivbordet i Windows: allt som rutan
  rör blir valt medan du drar. Shift lägger till, Ctrl vänder (valda blir ovalda), och Esc avbryter. Drar du en av de
  valda följer alla med, och Delete slänger dem. Ett klick på tom yta släpper valet.
- **Mushjulet**, **+** och **−** zoomar.
- **F** passar in hela grafen i fönstret.
- **Esc** stänger det som är öppet, i ordning: ett formulär, en vald tid på tidslinjen, och till sist valet.

## Två sätt att ordna grafen

I grafens huvud finns en växel med två ikoner:

- **Fri yta.** Aktörerna läggs först ut ungefär i hoppens ordning och står sedan där du drar dem, åt alla håll. Så kan
  du lägga det som hör ihop nära varandra.
- **Hoppkolumner.** Allt sorteras i kolumner, en per hopp från startpunkten. Du kan dra aktörer upp och ner i sin
  kolumn, och pengar som vänder tillbaka till ett tidigare hopp ritas som bågar.

![Samma graf på två sätt: fri yta, och hoppkolumner där pengar som vänder är en båge](figur:s-lagen)

Båda lägena kommer ihåg sina platser, och de sparas i ärendet. Ett hopp med många aktörer sprids över flera kolumner
sida vid sida i stället för en lång rad, så att grafen använder ytan.

## Beloppen på linjerna

Beloppet står alltid på linjerna, eftersom det är viktig information. Blir grafen stor kan du **gallra** i grafens huvud:
alla belopp, bara de spårade eller inga. Belopp som skulle hamna ovanpå varandra ritas inte; det under pekaren, nära det
valda och de spårade går först, och resten syns på hovring.

Spårningsmetoden (FIFO eller haircut) väljs också i huvudet och syns alltid. Se
[Spårningsmetoder](sparningsmetoder.md).

## Rensa och anteckna

Det som inte hör till utredningen slänger du i soptunnan nere till vänster (Delete, eller dra dit det). Inget tas
bort ur ärendet, och allt kan läggas tillbaka. Se [Soptunnan](soptunnan.md). Egna tankar skriver du på en lapp i
grafen med N, se [Kommentarer i grafen](kommentarer.md).

## Varför det är byggt så

Grafen är där man arbetar mest, och den får aldrig kännas trög (princip 6): den ritar bara det som syns, och allt tungt
räknas utanför det du ser. Hur noderna och linjerna ser ut står i [Grafen och bildspråket](grafen.md), och alla tangenter
i [Kortkommandon](kortkommandon.md).
