# Ut ur en skyddad pool

> **Som i vardagen:** någon går in i bankfacket med ett kuvert på 10 000 kronor och kommer ut en timme senare. Strax
> efter går någon ut ur samma bank med 9 990 kronor. Det bevisar ingenting, men det är värt att titta på.

En **skyddad pool** döljer allt som händer inne i den: Zcashs pooler (Sapling, Orchard, Sprout) och Litecoins **MWEB**.
På kedjan syns bara pengar som går **in** (en t→z-tx på Zcash, en *peg-in* på Litecoin) och pengar som kommer **ut**
(en z→t-tx, en *peg-out* i blockets integrationstx). Spåret tar slut vid ingången, med orsaken *Privacy coin*.

Forskningen om Zcash och MWEB har visat att många användare tar ut **samma belopp** som de satte in, minus avgiften,
**kort efter**. `shielded-pool v1` letar efter sådana vägar ut i grafen och föreslår dem som **kandidater**. En
kandidat är alltid en bedömning, streckad, och du godkänner eller avfärdar den, som mixerns troliga utgångar.

## Vad som prövas

För varje insättning i en pool prövas varje väg ut ur **samma kedjas** pool i grafen:

- **efter** insättningens block och högst **7 dygn** senare;
- som är lika stor som insättningen eller **högst 1 %** mindre (poolerna tar bara nätverkets avgift);
- aldrig en väg ut som är större än insättningen.

Bara det som redan finns i grafen prövas. Kedjan runt insättningen söks inte igenom, så expandera de tx med pengar ut
ur poolen som du misstänker först.

## Hur poängen räknas

Poängen går från 0 till 1 och väger två saker:

- **beloppet** till tre femtedelar: samma belopp räknas fullt, och poängen sjunker till noll vid 1 % mindre;
- **tiden** till två femtedelar: direkt efter räknas fullt, och poängen sjunker till en femtedel vid sju dygn.

Kandidater under 0,4 visas inte, och högst fem visas per insättning, den bästa först. Mönster i tid och belopp som
pekar på samma användare över flera insättningar (forskningens andra metod) är inte byggda än.
