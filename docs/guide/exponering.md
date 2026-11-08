# Exponering

> **Som i vardagen:** tänk dig ett företags kontoutdrag. Exponering är att fråga *vilka sorters mottagare gick
> pengarna till?* Så och så mycket till banker, så mycket till ett kasino, resten till privatpersoner vi inte känner.
> Och åt andra hållet: *varifrån kom pengarna?*

Vyn **Exponering** (Ctrl+4) visar det för ett kluster eller en adress i grafen. Klustret står i mitten, och varje
ring runt det är ett hopp. Det som har **betalats ut** går åt höger, det som har **kommit in** åt vänster, så att båda
hållen syns på en gång.

## Vad bilden visar

- **Tjänsterna** är noder i bildspråket: blått är uttag, gult där pengarna spenderas, rött risk och slut, lila en
  annan kedja och accenten ett offer. En tjänst är större ju större andel den har.
- **Aktörer utan etikett** på vägen är små grå punkter. Pengarna följs vidare genom dem.
- **Linjerna** är pengarna, tjockare ju mer som passerar. Sista steget in i en tjänst får tjänstens färg.
- **Djupet** (1, 2 eller 3) väljer hur många ringar som följs. Med 1 syns bara det som gått direkt.

Till höger står andelarna per färg för båda hållen, tjänsterna som pengarna nått och den valda tjänstens väg: aktörerna
på vägen, tx:erna och det andelen bygger på. *Visa vägen i grafen* (G) öppnar grafen med vägen markerad.

## Hur andelarna räknas

Andelarna räknas av analysen **exposure** med version, och med grafens **spårningsmetod** (FIFO eller haircut, se
[Spårningsmetoder](sparningsmetoder.md)). Varje aktör läses som en plånbok: dess överföringar står i tidsordning, och
metoden säger hur mycket av det som kom in som följer med varje överföring ut, på samma sätt som i
[kontomodellen](kontomodellen.md).

- **Betalat till:** allt klustret betalat ut är pengarna. Går de till en tjänst räknas de där. Går de till en aktör
  utan etikett följs de vidare genom den aktörens plånbok, hopp för hopp.
- **Fått från:** allt klustret fått är pengarna. Hos en aktör på vägen frågar metoden vilka av aktörens inbetalningar
  som pengarna till klustret kom ifrån, och följer dem bakåt.

Vilka aktörer som är tjänster bestäms av etiketterna och sorterna i grafen, och de är också bedömningar.

## Okänt

Det som inte når en tjänst sägs ärligt som **Okänt**, med skälet när du håller pekaren över raden:

- *Nådde ingen tjänst inom djupet*: pengarna gick vidare till en aktör utan etikett längre bort än ringarna.
- *Ligger kvar hos en aktör på vägen*: enligt metoden har pengarna inte lämnat aktören.
- *Gick tillbaka till klustret*: pengarna kom tillbaka på vägen.
- *Kom före historiken i grafen*: aktören hade pengar innan det grafen har läst, och det säger inte varifrån.

Expandera grafen så kan Okänt bli mindre.

## En bedömning

Kedjan säger vilka överföringar som gjordes, inte vems pengar som gick vart. En andel är därför alltid en
**bedömning**: en annan metod ger en annan andel ur samma kedja. *Varför?* vid den valda tjänsten visar analysen och
metoden med version, vägen och tx:erna.
