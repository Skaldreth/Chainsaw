# Common-input-ownership

Den äldsta och starkaste heuristiken för att slå ihop adresser till en plånbok.

## Idén

För att spendera en output måste man signera med nyckeln som hör till dess adress. När en tx har **flera inputs** från
olika adresser har någon alltså signerat för alla. Det enklaste skälet är att samma plånbok äger alla nycklarna, och
då har adresserna **samma ägare**.

> Tx:en spenderar outputs från A och B. Slutsats: A och B hör till samma plånbok.

Chainsaw slår ihop sådana adresser **automatiskt**, och varje sammanslagning sparar vilken heuristik och version
(`common-input-ownership v1`) och vilken tx den bygger på. Klicka på *varför?* för att se tx:en.

## När den inte håller

- **CoinJoin.** Flera personer bygger en tx tillsammans och signerar var och en sin egen input. Då är det just *inte*
  samma ägare. Därför körs CoinJoin-analysen först, och känner den igen en CoinJoin stoppas sammanslagningen för hela
  tx:en (se [CoinJoin](coinjoin.md)).
- **PayJoin.** Mottagaren lägger till en egen input i betalningen. Det syns inte på kedjan och är det kända hålet i
  heuristiken; ett kluster som plötsligt växer kraftigt genom en enda tx är värt att titta på.
- **Delade tjänster.** En börs äger sina kunders insättningsadresser. När börsen samlar ihop dem (en *sweep*) slås de
  ihop till börsens kluster, och det är rätt: det är börsens nycklar. Kunderna som betalade in hamnar däremot inte i
  börsens kluster, eftersom deras adresser aldrig är inputs i börsens tx.

## Om du inte håller med

Bryt upp länken. Uppbrytningen sparas i ärendet med ditt skäl, och samma heuristik slår aldrig ihop de två adresserna
igen. En annan heuristik får fortfarande föreslå det, och då syns förslaget, men inget slås ihop.
