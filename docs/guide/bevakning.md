# Bevakning

En **bevakning** säger till när pengarna rör sig från eller till en adress eller ett kluster. Det är viktigt inför ett
beslag: ser du att pengarna går till en börs vet du var en begäran om uppgifter ska gå, och när.

## Att lägga till en bevakning

- **I grafen:** högerklicka på en nod och välj *Bevaka …*, eller välj noden och tryck **B**.
- **I vyn Bevakning** (ögat i pillret, **Ctrl+6**): *Ny bevakning …* (**B**) tar en adress som inte finns i grafen.

Formuläret har:

- **Riktning:** *In*, *Ut* eller *Båda*.
- **Mottagare** (för pengar ut) och **avsändare** (för pengar in) som små brickor. En tom lista betyder **alla**. Med
  mottagare slår bevakningen bara till när pengarna går till **någon av** dem (1:n); med avsändare bara när de kommer
  **från någon av** dem (n:1). Skriv en adress och tryck *Lägg till*, eller håll **Ctrl** och klicka på noder i grafen.
- **Tillgång** på Ethereum: alla, ETH, USDT eller USDC. På Bitcoin finns bara BTC.
- **Minsta belopp**, om du vill: mindre rörelser räknas inte. På Ethereum krävs att en tillgång är vald.

Bevakningen förseglas i ärendet med vem och när, och varje ändring, paus och borttagning blir en ny förseglad post. Inget
tas bort: en borttagen bevakning står kvar i nodens *Historik*, överstruken, med sina träffar.

## Vad som räknas

- **Ut** är pengar som lämnar noden. Växel tillbaka till noden (eller till ett annat av klustrets adresser) räknas inte.
- **In** är pengar som kommer till noden från någon annan. En tx där noden också betalar är nodens egen växel, inte
  pengar in.
- En bevakning börjar räkna **efter blocket där den startade**: det som hände före räknas aldrig som en träff.
- En **träff** är en ny tx i ett block, som matchar. Den förseglas med tx, block och blockhash och de råsvar den bygger
  på, och journalen får en rad. Obekräftade tx väntar tills de ligger i ett block.

En träff är ett faktum från kedjan som bevakningen plockat ut. Vad den betyder är din bedömning.

## Hur den frågar

- Bara **medan Chainsaw är öppet och upplåst**. Att bevaka när Chainsaw är stängt är inte byggt än, och vyns huvud säger
  ärligt *Bevakar medan Chainsaw är öppet*.
- Varje nytt block som förval: kedjans höjd frågas högst en gång i minuten, och adresserna läses när ett nytt block har
  kommit. *Hur ofta* ställs in under **Inställningar → Låsning**.
- Bara **genom utgången**. Utan ansluten utgång, när VPN är valt, eller med kedjans källor *Av*, frågas ingenting, och
  grafens markeringar blir grå.
- Bara mot en endpoint **i fas** med kedjan: en som vid senaste provet svarade med den högsta höjden. En endpoint ett
  block efter duger för vanliga uppslag, men inte för bevakningen, som annars kunde missa det nyaste blocket.
- Ett kluster läses som klustringen säger att det ser ut **vid varje kontroll**, så en adress som kommer med senare
  bevakas också (högst 50 adresser per kontroll).

## I grafen

- En bevakad nod har ett **öga i hörnet**.
- En bevakning med motparter ritar en tunn **prickad vaktlinje** i bevakningens egen dova ton till varje vald motpart i
  grafen, med ett litet öga mitt på. Prickat, inte streckat: streckat betyder alltid osäkert.
- En linje som redan går i den bevakade riktningen får ett **mjukt sken**.
- Utan motparter står korta prickade **strålar** vid porten: till höger för ut, till vänster för in.
- Vid en **träff** blir vaktlinjen en riktig linje med beloppet, och **ringar** rör sig runt den tills du har sett
  träffen (välj noden i grafen eller bevakningen i vyn). Har Windows animeringar avstängda står ringarna stilla.
- Pillrets öga får en **prick** när en bevakning har nya träffar.
