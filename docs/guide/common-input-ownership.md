# Common-input-ownership

> **Som i vardagen:** Någon betalar i kassan med pengar ur tre olika bankfack. För att få ut dem har hen behövt
> nycklarna till alla tre facken. Det enklaste skälet är att facken är hens egna, och det är just den slutsatsen
> heuristiken drar: adresser som betalar tillsammans i samma tx har samma ägare.

Den äldsta och starkaste heuristiken för att slå ihop adresser till en plånbok.

## Idén

För att spendera en output måste man signera med nyckeln som hör till dess adress. När en tx har **flera inputs** från
olika adresser har någon alltså signerat för alla. Det enklaste skälet är att samma plånbok äger alla nycklarna, och
då har adresserna **samma ägare**.

> Tx:en spenderar outputs från A och B. Slutsats: A och B hör till samma plånbok.

![Tre adresser, var och en med sin nyckel, betalar i samma tx. Någon har haft alla tre nycklarna, så adresserna slås ihop till en plånbok. Ramen är streckad: sammanslagningen är en slutsats, inte ett faktum.](figur:cio-sammanslagning)

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

![Kunderna (till vänster) betalar in till var sin insättningsadress hos börsen. När börsen samlar ihop insättningarna till sin egen plånbok (blå) slås insättningsadresserna ihop med börsen, inom den blå streckade ramen. Kunderna står kvar utanför.](figur:cio-sweep)

## Om du inte håller med

Bryt upp länken. Uppbrytningen sparas i ärendet med ditt skäl, och samma heuristik slår aldrig ihop de två adresserna
igen. En annan heuristik får fortfarande föreslå det, och då syns förslaget, men inget slås ihop.
