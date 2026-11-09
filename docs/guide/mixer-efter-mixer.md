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
eller in i en CoinJoin. `mixer-chain v2` är en kontroll som ser när det händer:

- den följer **alla** utgångar du har **godkänt** i mixerns förslag som går vidare, och annars den **troligaste** (en
  hel utgång eller en del), men aldrig en du har **avfärdat**;
- är den troligaste en **del** följs alla delar i samma kombination som går vidare. Delar som spenderas **tillsammans**
  i en tx är en väg, med summan av dem som stegets belopp;
- går två utgångar in i **olika mixrar** delar sig kedjan, och texten säger *kedjan delar sig i 2 vägar* och vilket
  steg varje gren kommer från (*Steg 3 (efter steg 1)*). Titeln säger hur många grenar;
- går den in i en **trolig mixer utan etikett** (se nedan) blir det ett steg där också, genom den utbetalning som bäst
  liknar insättningen minus en vanlig avgift;
- när utgången spenderas in i en känd mixer blir det ett nytt steg, och den nya insättningen får **mixerns egna
  förslag** av sig själv, så att kedjan kan följas steg för steg;
- går den in i en CoinJoin som klustringen **känner igen** tar kedjan slut där; en *möjlig* CoinJoin är inget slut;
- två aktörer vars etiketter säger samma tjänst räknas som **samma mixer**;
- högst **10 steg** följs, över alla grenar, och texten säger när kedjan går vidare efter det.

Texten räknar upp stegen och summerar **beloppet in**, **den sista utgången** och **de troliga avgifterna** i varje
mixer. Är nästa insättning inte samma belopp som utgången står det, eftersom något då kom till eller gick bort mellan
stegen.

## Tänk på

- Ett steg genom en **trolig** utgång är bara så säkert som utgången. Godkänn eller avfärda kandidaterna i mixerns
  förslag, så följer kedjan dina beslut i stället.
- Bara det som finns i grafen prövas. Expandera aktörerna efter mixern först.
- En mixer utan etikett ses bara när den beter sig som en mixer (nedan). Annars ser den ut som en vanlig mottagare.

## En trolig mixer utan etikett

En central mixer har ingen etikett förrän någon har satt en. Kontrollen `mixer-behaviour v1` känner igen den ur hur
den beter sig:

- minst **åtta olika avsändare** betalar in till ägaren, som [klustringen](common-input-ownership.md) räknar den, och
  ägaren betalar **minst åtta olika mottagare**;
- minst **fem** av insättningarna, och minst två av tre, **kommer ut igen** inom en vecka, till någon som inte satte
  in, som insättningen minus **0,5–5 procent**;
- utbetalningen görs med **andra mynt** än insättningens egna. Det är kärnan i en mixer: den betalar ur en gemensam
  pott, så att ingen tx knyter ihop in och ut.

En börs tar också emot från många och betalar många, men dess uttag är inte, insättning för insättning, samma belopp
minus en procentavgift till en annan person en stund senare. Kontrollen lägger ett rött område runt mixern och säger i
*Varför?* hur många insättningar som kom ut igen och med vilken avgift. Håller du med, **sätt etiketten Mixer** med L:
då får varje insättning mixerns egna förslag, och kedjan följer dina beslut i stället för den troligaste
utbetalningen. Det är en bedömning: en växlare som betalar tillbaka nästan samma belopp kan se likadan ut.
