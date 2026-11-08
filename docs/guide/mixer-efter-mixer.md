# Mixer efter mixer och delarna

En mixer gör det svårt att följa pengarna på två sätt till, utöver att den betalar ut från andra mynt: den betalar ut
**i flera delar och över tid**, och pengarna går ofta **från en mixer in i en mixer till**. Chainsaw har en analys för
var och en. Båda är bedömningar, aldrig fakta, och bygger på [mixerns troliga utgångar](mixerns-kandidater.md).

## Delarna tillsammans

En insättning på 0,15 BTC kan komma ut som 0,06, 0,05 och 0,037 BTC under ett par dygn, till olika adresser. Ingen av
delarna liknar insättningen, så `mixer-outputs` hittar dem inte. `mixer-parts v1` prövar i stället **kombinationer**:

- två till **fyra** utbetalningar i grafen, inom **7 dygn** efter insättningen;
- var och en minst **en tiondel** av insättningen, så att ingen del ensam kan vara en hel utgång;
- **högst en del per tx**, och aldrig två där den ena tx:en spenderar den andra (en betalning och dess växel är inte
  två delar);
- som **tillsammans** är insättningen minus en avgift på högst 10 %.

Poängen väger summan mot insättningen minus avgift (nästan hälften), tiden, hur **utspridda** delarna är (delar i olika
block väger mer än delar i samma block) och adresstypen. Den bästa kombinationen visas som ett förslag där varje del är en
kandidat, och du godkänner eller avfärdar delarna var för sig. I panelen räknas avgiften då mot **alla delar som inte
är avfärdade**, inte mot en del i taget. En **avfärdad** del prövas aldrig mer, så nästa bästa kombination visas, och
den avfärdade står kvar för sig. En kombination som håller de delar du **godkänt** prövas först; hör en godkänd del inte
till någon kombination som passar står den kvar med poäng 0, och texten säger att de inte kan stämma båda.

Antalet kombinationer växer snabbt, så sökningen är begränsad: bara de **20** utbetalningarna närmast efter
insättningen prövas. Texten säger hur många kombinationer som passade. **Många som passar betyder att en kan passa av
en slump**, särskilt när grafen har många utbetalningar med jämna belopp.

## Mixer efter mixer

Det är vanligt att lägga lager på lager: en utgång ur en mixer skickas in i en mixer till, samma igen eller en annan,
eller in i en CoinJoin. `mixer-chain v1` är en kontroll som ser när det händer:

- den följer utgångarna du har **godkänt** i mixerns förslag, och annars den **troligaste** (en hel utgång eller en
  del), men aldrig en du har **avfärdat**;
- när utgången spenderas in i en känd mixer blir det ett nytt steg, och den nya insättningen får **mixerns egna
  förslag** av sig själv, så att kedjan kan följas steg för steg;
- går den in i en CoinJoin som klustringen **känner igen** tar kedjan slut där; en *möjlig* CoinJoin är inget slut;
- två aktörer vars etiketter säger samma tjänst räknas som **samma mixer**;
- högst **10 steg** följs, och texten säger när kedjan går vidare efter det.

Texten räknar upp stegen och summerar **beloppet in**, **den sista utgången** och **de troliga avgifterna** i varje
mixer. Är nästa insättning inte samma belopp som utgången står det, eftersom något då kom till eller gick bort mellan
stegen.

## Tänk på

- Ett steg genom en **trolig** utgång är bara så säkert som utgången. Godkänn eller avfärda kandidaterna i mixerns
  förslag, så följer kedjan dina beslut i stället.
- Bara det som finns i grafen prövas. Expandera aktörerna efter mixern först.
- En mixer måste vara känd genom en etikett för att kedjan ska se den. En okänd mixer ser ut som en vanlig mottagare.
