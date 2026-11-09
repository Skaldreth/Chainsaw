# Flödet

> **Som i vardagen:** tänk dig en flod som delar sig i grenar på väg mot havet. Vattnet från en källa blandas med
> annat, och i varje gren kan du fråga: hur mycket av det här vattnet kom från *vår* källa, och var hamnar det till
> sist? Flödet ritar offrens pengar på det sättet.

Vyn **Flöde** (Ctrl+3) är ett **Sankey-diagram**. Offren står till vänster, och varje kolumn till höger är ett hopp.
En stapel per aktör är lika hög som pengarna som går igenom den, och banden mellan staplarna är lika breda som
beloppet.

## Trådarna och färgerna

Varje band är delat i **trådar** som har färgen för var pengarna **till sist hamnade**: blått uttag, gult där de
spenderas, rött risk och slut, lila en annan kedja. Så ser du redan vid första hoppet hur mycket som kommer att nå en
börs. Det som **ligger kvar** hos en aktör utan tjänstens sort står som en kort stump i accenten, eftersom det
fortfarande kan frysas eller beslagtas.

Pengar som inte är offrens, men som blandas in på vägen, är **grå** trådar. Bortom något osäkert, till exempel en
godkänd trolig utgång ur en mixer, är trådarna **randiga och streckade**: vägen dit är en bedömning.

## Metoden räknar om delningen

När pengar från flera håll blandas hos en aktör går det inte att se på kedjan vilka pengar som lämnade först. Därför
väljs en **spårningsmetod** överst, streckad eftersom delningen är en bedömning (se
[Spårningsmetoder](sparningsmetoder.md)):

- **FIFO:** de pengar som kom först går först.
- **Haircut:** varje utbetalning bär sin andel av allt som kom in. Med haircut syns de grå trådarna i nästan varje
  band.

Flödet räknas av analysen **flow** med version. Den läser varje aktör som en **plånbok**: grafens överföringar i
tidsordning, och metoden säger hur mycket av varje del som följer med. Det är samma tanke som Ethereums konton, och
på Bitcoin kan det skilja sig något från spåret i grafen, som följer varje mynt. Avgifterna till gruvarbetarna står
inte som en egen tråd; de ligger kvar hos aktören som betalade dem.

## Välja och visa vägen

Ett klick på ett band eller en stapel tänder alla trådar som går där. Till höger står var de hamnade, de största
vägarna med aktörerna och tx:erna i varje steg, och det delningen bygger på. *Varför?* förklarar siffran, och *Visa
vägen i grafen* (G) öppnar grafen med vägen markerad.

Delar som är för tunna för att synas ritas inte, men räknas, och vyn säger hur många de är. Flödet skriver inget i
ärendet.
