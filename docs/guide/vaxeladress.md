# Växeladress

En output spenderas alltid helt (se *UTXO-modellen*). Den som betalar 0,3 BTC från en output på 1 BTC får därför
tillbaka resten som **växel**, oftast till en ny adress i samma plånbok. Att hitta växeln är att hitta var avsändarens
pengar fortsätter.

Kedjan säger aldrig vilken output som är växeln. Chainsaw **väger tecken** (`change-address v1`):

| Tecken | Varför det pekar på växel |
| --- | --- |
| Samma skripttyp som inputs | En plånbok använder oftast en sorts adress, mottagaren kanske en annan. |
| De andra beloppen är jämna | Man betalar 0,05 BTC, inte 0,04873211; resten blir udda. |
| Ny adress när de andra är kända | Plånböcker skapar en ny adress för växeln; mottagaren har ofta använts förut. |
| Mindre än varje input | Hade outputen varit betalningen hade den minsta inputen inte behövts (*optimal växel*). |
| Tillbaka till en inputs adress | Plånboken skickar växeln till samma adress som betalade. |

Ett tecken räknas bara när det **skiljer** outputarna åt: är båda beloppen udda säger det inget.

## När slås växeln ihop?

Chainsaw visar hur många tecken som stämmer, till exempel *3 av 5 tecken, 0 mot*. Växeln slås ihop med avsändaren
**bara när tecknen räcker**: minst tre tecken, inget som pekar på en annan output, och tx:en har bara två outputs.
Annars är det ett **förslag** som syns men inte slår ihop något.

En **batchutbetalning** (en börs betalar många kunder i samma tx) slås aldrig ihop automatiskt: med många outputs blir
tecknen svagare, och varje fel skulle göra en kund till en del av börsen.

Pekar två analyser ut olika outputs som växel i samma tx (till exempel växeln och peel chain) slås ingen av dem ihop.

*Ny adress* betyder *inte sedd tidigare i ärendet*: Chainsaw vet bara om de tx som har hämtats, och slutsatsen säger i
vilken tx den andra adressen sågs.
