# Soptunnan

> **Som i vardagen:** soptunnan i Chainsaw är som papperskorgen på datorn, fast ingen tömmer den. Det du slänger
> försvinner från skrivbordet men ligger kvar i korgen, och du kan alltid ta upp det igen. Slänger du en mapp följer allt
> i den med.

En graf som växer okontrollerat är svår att läsa (princip 7). Med soptunnan rensar du bort det som inte hör till
utredningen: en återvändsgränd, en motpart som visade sig vara brus. Att slänga är **bara ett filter**: inget tas bort
ur ärendet (princip 1). Det slängda döljs i grafen och kan alltid läggas tillbaka.

## Slänga

- **Dra** en nod mot soptunnan nere till vänster. När du kommer nära växer den till en rund yta att släppa på, med en
  ringvåg när du är framme. Allt som går att dra går att slänga.
- **Delete** slänger den valda noden, de valda korten i en utfälld hög eller aktörerna du valt för en yta.
- **Ctrl+Z** ångrar den senaste slängningen.

![Att slänga: soptunnan växer till en rund yta, och det som bara hänger på det slängda följer med, streckat, medan resten tonas ned](figur:s-soptunnan)

## Det som följer med

Slänger du något mitt i grafen följer det med som **bara** nås genom det, åt båda hållen: motparter som inte har någon
annan väg till resten av grafen, högar och ledtrådar vid det slängda. Startpunkterna är ankare och stannar.

Följer något med öppnas en liten **varning** vid soptunnan, *Det här följer med*, med det som följer med som ett träd
under det du slänger. Medan varningen är öppen blir det som följer med streckat i grafen och resten tonas ned. Enter
slänger, Esc avbryter.

En linje till något i soptunnan blir en **kort streckad stump** vid noden som står kvar, så att du ser att det finns
mer.

## Bevakat slängs inte i tysthet

Ligger något som följer med under **bevakning**, frågar varningen alltid först. Varje bevakad rad har en brytare
**Avsluta**, som är på från början:

- På: bevakningen avslutas, med en egen förseglad post och skälet *Slängdes i soptunnan*, innan noden går i soptunnan.
- Av: bevakningen fortsätter, och soptunnans vy visar *bevakas* på raden.

Se [Bevakning](bevakning.md).

## Vyn och Lägg tillbaka

**Klicka på soptunnan** för att se allt som slängts, som ett träd per slängning: det du slängde överst och det som följde
med under, i flera led.

![Soptunnans vy: ett träd per slängning, med Lägg tillbaka på varje gren](figur:s-soptunnan-vyn)

- **Lägg tillbaka** på en rad lägger tillbaka den grenen, med det som ligger under.
- **Lägg tillbaka allt** tömmer soptunnan.

Det som läggs tillbaka står där det stod, eftersom platserna aldrig rörs. Varje slängning och varje återläggning
förseglas med vem och när och en rad i journalen. Soptunnan använder inte rött, som betyder risk.

## I startvyn

Också ärenden kan slängas. Lyft ett ärende på startsidan, så växer soptunnan nere till vänster; släpp det där. Ärendet
**döljs bara på startsidan på den här datorn**; filen ligger kvar. I vyn finns delen *Ärenden från startsidan* med
*Lägg tillbaka allt*. Se [Öppna, byta och stänga ärenden](arendevaljaren.md).

Det som inte är byggt än: att slänga kommentarer och egna ytor, och att dra en hel hög i startvyn till soptunnan.
