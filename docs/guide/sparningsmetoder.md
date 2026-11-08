# Spårningsmetoder: FIFO och haircut

> **Som i vardagen:** Häll ett glas saft och ett glas vatten i samma kanna och fyll sedan tre nya glas. Hur mycket saft
> finns i varje glas? Det beror på hur man tänker. Rörs kannan om innehåller varje glas lika stor andel saft: det är
> **haircut**. Hälls vätskorna i lager utan att blandas, så att det som kom in först också hälls ut först, hamnar all
> saft i några av glasen och inget i de andra: det är **FIFO**. Kedjan säger inte vilket som hände, så det är ett val.

När pengar går in i en tx tillsammans med andra pengar säger kedjan **inte** vems satoshi som gick vart. En tx har
bara inputs och outputs; den har ingen koppling mellan en viss input och en viss output. Hur mycket av offrets pengar
som följer med genom en tx är därför en **regel**, inte ett faktum. Regeln kallas spårningsmetod.

Chainsaw visar alltid vilken metod och version ett spårat belopp bygger på, och vilka tx det räknades genom. Ett
spårat belopp är en **bedömning**. Byter du metod får du ett annat belopp ur samma kedja.

## FIFO (först in, först ut)

Tänk dig att tx:ens inputs läggs **efter varandra på en linje**, i den ordning de står i tx:en. Outputs klipps sedan
av från samma linje, också de i ordning. Det som blir kvar längst bort är avgiften till mineraren.

Exempel: inputs 0,3 BTC (någon annans) och 0,7 BTC (offrets), en output på 0,99 BTC och avgiften 0,01 BTC.

- På linjen ligger offrets pengar från 0,3 till 1,0.
- Outputen är 0 till 0,99, så den får offrets pengar från 0,3 till 0,99, alltså 0,69 BTC.
- Avgiften är 0,99 till 1,0, och den är helt offrets: 0,01 BTC går till mineraren.

![Överst ligger inputs på en linje: 0,3 BTC någon annans (grått) och 0,7 BTC offrets (accent). Underst klipps outputen och avgiften ur samma linje. Offrets del hamnar där den låg: 0,69 BTC i outputen och 0,01 BTC i avgiften längst till höger.](figur:sparning-fifo)

Pengarna **behåller sin plats** genom nästa tx, så pengar som kom in tillsammans går ut tillsammans. Därför ger FIFO
skarpa svar: en output har ofta antingen allt eller inget av det spårade.

## Haircut (andel)

Haircut sprider det spårade **i proportion** till beloppen. Är 60 % av det som går in i en tx offrets pengar, så är 60
% av varje output och 60 % av avgiften offrets.

Exempel: inputs 0,6 BTC (offrets) och 0,4 BTC, outputs 0,5 och 0,4 BTC och avgiften 0,1 BTC.

- 60 % av 0,5 = 0,3 BTC.
- 60 % av 0,4 = 0,24 BTC.
- 60 % av avgiften 0,1 = 0,06 BTC.

![Haircut som färg i vatten: offrets pengar (accent) och andras (grått) hälls ihop i tx:en och blandas. Varje output får samma andel, och avgiften (det streckade glaset) också.](figur:sparning-haircut)

Haircut tappar aldrig spåret, men pengarna **tunnas ut** över alla vägar de kan ha tagit. Den passar när man vill se
allt som kan vara berört, och visar sämre var huvuddelen tog vägen.

## Inget skapas och inget försvinner

Oavsett metod går summan alltid jämnt ut, på satoshin:

- Det spårade som går in i en tx är exakt det som går ut i outputs **plus avgiften**. Avgiften visas som ett eget slut,
  *Avgift till minerare*.
- Haircut räknar i hela satoshi. Varje del avrundas först nedåt, och de satoshi som blir över delas ut en i taget till
  de delar som hade störst rest. Därför blir summan aldrig en satoshi för mycket eller för lite.
- Allt som spårats slutar någonstans med en orsak (se [Varför ett spår tar slut](sparet-tar-slut.md)). Summan av sluten är alltid det man
  började spåra. Chainsaw kontrollerar det och vägrar visa ett spår som inte går ihop.

## Vilken ska jag välja?

![Samma tx med båda metoderna. Till vänster FIFO: offrets pengar följer med i sin helhet till den ena outputen, och den andra får inget. Till höger haircut: båda outputs får en del, och spåret tunnas ut.](figur:sparning-jamforelse)

- **FIFO** när du vill följa huvuddelen av pengarna, till exempel mot den börs där de togs ut.
- **Haircut** när du vill se allt som kan vara berört, till exempel inför en bevakning.

LIFO (sist in, först ut) och *poison* (allt som rör spårade pengar blir berört) finns inte än. Poison ger inga belopp
som går jämnt ut, så den behöver visas som *berört* i stället för ett belopp.
