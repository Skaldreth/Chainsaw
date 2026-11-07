# Hur Chainsaw kontrollerar svaren mot kedjan

Chainsaw hämtar kedjans uppgifter från en källa (först ett Esplora-API som mempool.space). Källan är en mellanhand,
men mycket av det den svarar går att kontrollera med kedjans egen kryptografi, utan att lita på den. Det gör att ett
steg i spåret vilar på kedjan och inte bara på källan.

## Txid är en hash av tx:ens egna bytes

En tx skickas i nätet som en följd av bytes: version, inputs (vilken output varje input spenderar), outputs (belopp
och skript) och locktime. **Txid är dubbel SHA-256 av de bytesen**, skriven baklänges som Bitcoin visar hashar.
SegWit-tx har dessutom ett **vittne** (signaturerna) som inte ingår i txid.

När Chainsaw expanderar en tx hämtar den också tx:ens bytes (`/tx/:txid/hex`), räknar fram txid själv och jämför med
källans beskrivning: version, varje inputs spenderade output och sekvens, varje outputs belopp och skript och
locktime. Stämmer allt är tx:en **kontrollerad mot sina bytes**, och det gäller sedan varje gång den läses ur ärendet.
Stämmer något inte används inte källans beskrivning alls; båda svaren ligger kvar i ärendet.

Det som bytesen inte innehåller, beloppen och adresserna på de outputs som spenderas och vilket block tx:en ligger i,
vilar fortfarande på källan (beloppen kontrolleras när den föregående tx:en hämtas).

## Blockets huvud

Ett block identifieras av hashen av sitt **huvud**, 80 bytes: version, föregående blocks hash, merkleroten (en hash
över blockets tx), tid, målet (`bits`) och ett nonce. Chainsaw hämtar huvudet (`/block/:hash/header`), räknar fram
hashen och kontrollerar att den är högst det mål som `bits` anger, alltså att blocket bär det arbete det påstår. Sedan
jämförs källans beskrivning av blocket med huvudet. Om målet är nätets rätta svårighet på den höjden kontrolleras inte.

## Omorganisation av kedjan

Ibland hittar två gruvarbetare ett block samtidigt, och kedjan grenar sig en kort stund. Den gren som växer snabbast
vinner, och blocken på den andra **faller bort** (en omorganisation). En tx i ett bortfallet block hamnar oftast i
ett senare block, men kan också bli obekräftad igen.

Därför räknar Chainsaw djupet från **när svaret hämtades**: en tx som låg i ett grunt block när den hämtades hämtas om
när blocket har blivit djupt (sex bekräftelser), och först då litar Chainsaw på svaret för gott. Ligger tx:en då i ett
annat block, eller är obekräftad, säger Chainsaw det: *blocket är inte längre en del av kedjan enligt källan*.

## Utgångar som aldrig kan spenderas

En output med ett **OP_RETURN**-skript bär data (till exempel ett meddelande eller en tidsstämpel) och kan aldrig
spenderas. Chainsaw väntar därför inte på att den ska spenderas när den avgör om svaret om vilka utgångar som är
spenderade kan ha ändrats.

## Adressens sammanfattning

Källan kan säga hur många tx en adress har och hur mycket den har tagit emot och spenderat (`/address/:adress`). Det
är källans siffror, inte kedjans, och de ändras hela tiden. Chainsaw använder dem för att säga ärligt hur mycket som
hölls tillbaka när en adress har fler tx än en expansion tar med.
