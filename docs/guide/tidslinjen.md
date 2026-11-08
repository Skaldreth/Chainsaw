# Tidslinjen under grafen

> **Som i vardagen:** tidslinjen är som att spola tillbaka en övervakningsfilm. Du stoppar vid en tidpunkt och ser bara
> det som hade hänt då; det som kom senare bleknar, som om det ännu inte fanns.

Grafen visar vart pengarna gick. Tidslinjen visar **när**. Det spelar roll i en utredning: pengar som går vidare inom
en timme efter ett bedrägeri säger något annat än pengar som ligger still i ett halvår.

![Tidslinjen: prickade dygnsband, en list med ett streck per tx, och en vald tid där det som kom senare bleknar](figur:s-tidslinjen)

## Det du ser

- **Dygnsband:** tunna prickade linjer över hela grafen, mellan dygnen. De tonar in och ut i ändarna och böjer sig runt
  aktörer som du har dragit ur sitt dygn. I hoppkolumnerna är de i stort sett raka.
- **Listen** längst ner har **ett streck per tx** i grafen, i tidsordning. Där strecken ligger tätt hände mycket på kort
  tid.

En aktörs tid är när pengarna först kom dit i grafen, eller för en startpunkt som bara skickar, dess första utgående tx.
Dygnen räknas i Windows tidszon.

## Välja en tid

- **Klicka i listen.** Nära ett streck väljs tx:ens tid, annars tiden under pekaren. En hel linje dras genom grafen vid
  tiden, med en liten prick i listen.
- **Det som kom senare bleknar**, aktörer och linjer.
- **Till nu** (eller Esc) går tillbaka till grafen som den är nu. Knappen syns bara när en tid är vald.

Obekräftade tx, som ännu inte ligger i ett block, räknas som *nu* och bleknar alltid vid en vald tid.

## Tänk på

- **Blocktiden är ungefärlig.** Den sätts av den som bryter blocket och kan avvika något från den verkliga tiden, så en
  ordning inom några minuter ska läsas med försiktighet.
- **Tidslinjen visar grafen, inte hela kedjan.** Det som inte är expanderat finns inte i listen.

Det som inte är byggt än: band i timmar när man zoomar in, och listens slut vid det senaste blocket. Vyn *Tidslinje* i
pillret är en annan, egen vy som inte heller är byggd än.

Se också [Fördröjning och många hopp](fordrojning-och-hopp.md).
