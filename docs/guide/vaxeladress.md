# Växeladress

> **Som i vardagen:** Du ser ett kvitto där någon lämnat en femhundralapp och två belopp gått ut: 300 kronor jämnt och
> 187,50. Kvittot säger inte vilket som var växeln, men du gissar ändå: den jämna summan låter som ett pris, den udda som
> det som blev över. Chainsaw gissar på samma sätt, efter flera sådana tecken, och säger alltid hur många som stämde.

En output spenderas alltid helt (se [UTXO-modellen](utxo-modellen.md)). Den som betalar 0,3 BTC från en output på 1 BTC får därför
tillbaka resten som **växel**, oftast till en ny adress i samma plånbok. Att hitta växeln är att hitta var avsändarens
pengar fortsätter.

Kedjan säger aldrig vilken output som är växeln. Chainsaw **väger tecken** (`change-address v2`):

| Tecken | Varför det pekar på växel |
| --- | --- |
| Samma skripttyp som inputs | En plånbok använder oftast en sorts adress, mottagaren kanske en annan. |
| De andra beloppen är jämna | Man betalar 0,05 BTC, inte 0,04873211; resten blir udda. |
| Ny adress när de andra är kända | Plånböcker skapar en ny adress för växeln; mottagaren har ofta använts förut. |
| Mindre än varje input | Hade outputen varit betalningen hade den minsta inputen inte behövts (*optimal växel*). |
| Tillbaka till en inputs adress | Plånboken skickar växeln till samma adress som betalade. |
| Spenderas senare av samma sorts plånbok | Varje plånbok har ett *fingeravtryck*: tx-version, om tx:en låses till en blockhöjd eller en tid, och om den kan ersättas (RBF). Spenderas den ena outputen senare av en tx med samma fingeravtryck och den andra av en med ett annat, gick samma plånbok vidare med den första. |

Ett tecken räknas bara när det **skiljer** outputarna åt: är båda beloppen udda säger det inget.

![Inputen har en adresstyp (romben). Den ena outputen har en annan typ (pricken) och går till någon som använts förut, den andra har samma typ som inputen och är en ny adress (streckad). Tecknen pekar på den nedre som växel, och den streckade bågen tillbaka är slutsatsen att den hör till avsändaren.](figur:vaxel-tecken)

## När slås växeln ihop?

Chainsaw visar hur många tecken som stämmer, till exempel *3 av 6 tecken, 0 mot*. Växeln slås ihop med avsändaren
**bara när tecknen räcker**: minst tre tecken, inget som pekar på en annan output, och tx:en har bara två outputs.
Annars är det ett **förslag** som syns men inte slår ihop något.

En **batchutbetalning** (en börs betalar många kunder i samma tx) slås aldrig ihop automatiskt: med många outputs blir
tecknen svagare, och varje fel skulle göra en kund till en del av börsen.

Pekar två analyser ut olika outputs som växel i samma tx (till exempel växeln och peel chain) slås ingen av dem ihop.

Fingeravtrycket är ett svagt tecken ensamt, eftersom många plånböcker har samma; det räknas bara när det skiljer
outputarna åt, och bara när ärendet känner till tx:erna som spenderar dem.

*Ny adress* betyder *inte sedd tidigare i ärendet*: Chainsaw vet bara om de tx som har hämtats, och slutsatsen säger i
vilken tx den andra adressen sågs.
