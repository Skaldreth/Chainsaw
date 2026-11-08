# Bryta upp ett kluster

> **Som i vardagen:** en kollega har sorterat kvitton i högar efter vem hon tror har betalat dem, för att samma
> kreditkort syns på flera. Du ser att två kvitton i en hög inte kan vara samma persons: kortet var lånat. Du lägger dem
> i var sin hög och sätter en lapp på dem: *inte samma, sorterade isär av mig*. Nästa gång någon sorterar läser de lappen.

Ett **kluster** är adresser som Chainsaw bedömer har samma ägare, en plånbok. Klustringen sker **automatiskt** med
heuristiker, till exempel [common-input-ownership](common-input-ownership.md) (adresser som betalar tillsammans i
samma tx) och [växeladressen](vaxeladress.md). Varje sammanslagning sparas med heuristiken, versionen och den tx den
bygger på.

En heuristik är en tumregel, och tumregler kan ha fel. En CoinJoin som inte känns igen, eller en tjänst som betalar för
flera kunder i samma tx, kan slå ihop adresser som inte hör ihop. Då bryter du upp klustret.

![Ett kluster bryts upp: delarna hålls isär, och samma heuristik slår inte ihop dem igen](figur:s-bryt-upp)

## Så gör du

1. Välj klustret och tryck **U** (eller *Bryt upp* i ikonlisten).
2. Bocka för de adresser som ska ut. Varje rad visar vilken heuristik som slog ihop den.
3. Skriv ett skäl om du vill; det är frivilligt.
4. Enter sparar, Esc avbryter.

Chainsaw bryter varje sammanslagning mellan en adress som ska ut och en som stannar. Uppbrytningen förseglas i ärendet
med en rad i journalen, och grafen ritas om.

## Uppbrytningen håller

Samma heuristik slår **inte** ihop adresserna igen, inte ens när nya tx hämtas. En annan heuristik som skulle göra det
visas bara som ett förslag. Etiketter på klustret följer med den del som adressen är kvar i.

Ångrar du dig, ångra uppbrytningen i nodens historik. Chainsaw visar först vilka adresser som slås ihop igen och med
vilken heuristik. Se [Ta tillbaka och ångra](historik.md).

## Varför det är byggt så

Klustringen är en **bedömning**, aldrig ett faktum (princip 2). Du ska alltid kunna se varför två adresser slogs ihop,
och kunna säga emot, utan att din invändning glöms bort nästa gång Chainsaw räknar.
