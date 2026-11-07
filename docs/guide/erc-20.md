# ERC-20 och tokens

En **token** på Ethereum är ett kontrakt som håller ett eget saldo för varje adress. USDT, USDC och tusentals andra är
tokens. Standarden heter **ERC-20**.

## En token känns igen på sitt kontrakt

Vem som helst kan skapa en token och kalla den USDT. Chainsaw känner därför **aldrig** igen en token på namnet eller
symbolen, bara på **kontraktets adress**. Riktig USDT på Ethereum är kontraktet `0xdAC1…1ec7`; en token med samma
symbol men ett annat kontrakt visas med sin egen adress och får aldrig USDT:s märke eller utgivare.

## Händelsen Transfer

När en token flyttas skriver kontraktet en händelse i tx:ens logg: `Transfer(från, till, belopp)`. Händelsen är
**kontraktets eget besked** om sin bokföring. För USDT och USDC är det utgivarens kontrakt; ett okänt kontrakt kan
skriva vad det vill, så en överföring i en okänd token är kontraktets uppgift, inte ett bevis. Chainsaw läser
händelsen ur loggen och kontrollerar formen: tre ämnen (händelsens namn, från och till) och beloppet som ett enda tal.
En händelse med fyra ämnen är en NFT (ERC-721), där talet är vilken sak det gäller och inte ett belopp, och den räknas
inte.

En överföring på **noll** räknas inte heller. Vem som helst kan få ett tokenkontrakt att skriva en sådan från vilken
adress som helst, till en adress som liknar en riktig mottagare (**adressförgiftning**), i hopp om att någon kopierar
fel adress ur historiken. Den flyttar ingenting.

## Decimaler

Ett tokenbelopp står på kedjan i tokenens minsta enhet. Hur många decimaler det är bestämmer kontraktet: USDT och USDC
har 6, ether och de flesta andra 18. 52 000 USDT står alltså som 52 000 000 000. Chainsaw känner USDT:s och USDC:s
decimaler; för andra tokens hämtas de från källan och sparas i ärendet. Går de inte att få räknas överföringen inte,
och tx:en säger att den är ofullständig.

## I grafen

Linjerna ser ut som för Bitcoin: accenten är offrens pengar och tjockleken beloppet. Tokenen står som ett litet märke
först i beloppet, **₮** för USDT, **$** för USDC och **Ξ** för ether, med värdet i kronor litet under när det finns en
kurs. En token utan känt märke står med sin symbol. Se också [Kan frysas](kan-frysas.md).

> Grafen för Ethereum är inte byggd än; snöflingan och saldot per token syns där när den är det.
