# ERC-20 och tokens

> **Som i vardagen:** En token är som presentkort i en butikskedja. Kortens värde finns inte på kortet utan i butikens
> egen bokföring, där varje kort har en rad med sitt saldo. Kontraktet är butikens bokförare: när ett belopp flyttas
> skriver den om två rader. Och precis som ett falskt presentkort kan bära rätt logga känns en riktig token igen på
> vem som bokför, inte på namnet.

En **token** på Ethereum är ett kontrakt som håller ett eget saldo för varje adress. USDT, USDC och tusentals andra är
tokens. Standarden heter **ERC-20**.

## En token känns igen på sitt kontrakt

Vem som helst kan skapa en token och kalla den USDT. Chainsaw känner därför **aldrig** igen en token på namnet eller
symbolen, bara på **kontraktets adress**. Riktig USDT på Ethereum är kontraktet `0xdAC1…1ec7`; en token med samma
symbol men ett annat kontrakt visas med sin egen adress och får aldrig USDT:s märke eller utgivare.

På **TRON** heter samma standard **TRC-20**, och allt i det här avsnittet gäller där också. Riktig USDT på TRON är
kontraktet `TR7NHq…gjLj6t`. Se [TRON](tron.md).

## Händelsen Transfer

![Kontraktet (rutan) håller en rad per adress. En överföring flyttar ett belopp från en rad till en annan: det streckade är det som lämnade den översta raden, och accenten det som kom till den nedersta.](figur:erc20-kontrakt)

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

Värdet i kronor räknas för varje överföring vid dess blocks tid, ur kursen för just den tillgången (ether, eller
USDT och USDC igenkända på kontraktet). Saknar ärendet kursen för någon överföring på linjen står **– kr**, hellre
än ett påhittat värde; de saknade kurserna hämtas i bakgrunden. En stablecoin räknas aldrig som en dollar av sig själv.

## Swap

När adressen som signerade en tx skickar **en** tillgång och får **en annan** tillbaka i samma tx (till exempel USDT in
till en DEX och ether tillbaka) har den bytt token. Det ritas som ett litet piller på linjen, **₮ → Ξ**, och är inget
slut på spåret: pengarna bytte token men inte ägare. Pillret sätts bara när tx:en själv visar bytet; skickar eller får
adressen flera tillgångar går det inte att läsa vad som byttes mot vad, och då står inget piller.

Spårningen räknar än så länge varje tillgång för sig, så de spårade USDT:na blir inte spårad ether i bytet. Det står i
*Varför?* på linjen.
