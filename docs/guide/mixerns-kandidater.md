# Mixerns troliga utgångar

En **mixer** tar emot pengar från många kunder och betalar ut från andra mynt, efter en avgift och ofta en fördröjning.
På kedjan finns därför ingen tx som knyter en insättning till en utbetalning: spåret tar slut vid mixern.

Men en utbetalning brukar ändå likna insättningen. `mixer-outputs v1` letar i grafen efter sådana och föreslår dem som
**kandidater**. En kandidat är alltid en bedömning, streckad, och du godkänner eller avfärdar den.

## Vad som prövas

För varje insättning till en adress som en etikett säger är en mixer prövas varje utbetalning i grafen:

- **efter** insättningens block och högst **7 dygn** senare;
- som inte är insättarens egna pengar (växeln eller något insättaren själv spenderar);
- som inte går till mixerns egna kända adresser;
- som är **mindre** än insättningen, men högst **10 %** mindre.

Bara det som redan finns i grafen prövas. Kedjan runt insättningen söks inte igenom (det står i backloggen), så
expandera de aktörer du misstänker först.

## Hur poängen räknas

Poängen går från 0 till 1 och väger tre saker:

- **beloppet** till hälften: avgiften mellan 0,5 och 5 % räknas fullt, mindre och mer räknas mindre, och 10 % räknas
  inte alls;
- **tiden** till tre tiondelar: direkt efter räknas fullt, och poängen sjunker till en femtedel vid sju dygn;
- **adresstypen** till en femtedel: samma typ som insättarens (till exempel P2WPKH) räknas fullt, en annan typ mindre.

Kandidater under 0,4 visas inte, och högst fem visas per insättning, den bästa först. I grafen går en streckad linje
från mixern till varje kandidat, med poängen på linjen och svagare ju lägre den är.

## Godkänna och avfärda

Varje kandidat avgörs för sig, med Enter och X i [listan](kontroller-och-forslag.md). Ett avfärdande gäller tills en ny
version av analysen kommer.

Att godkänna en kandidat gör den inte till en del av spåret: belopp spåras inte genom mixern. Den står kvar som din
bedömning, streckad, med vem och när.

## Tänk på

- Mixers med fasta avgifter och fasta belopp ger många kandidater med samma poäng. Då säger tiden och adresstypen mest.
- `mixer-outputs v1` jämför bara en utbetalning med en insättning. Uppdelning i flera utbetalningar prövas av
  [Delarna tillsammans](mixer-efter-mixer.md), och en utgång som går in i en mixer igen visas av
  [Mixer efter mixer](mixer-efter-mixer.md).
