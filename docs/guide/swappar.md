# Spåret genom en swap

> **Som i vardagen:** Den som växlar sedlar på ett växelkontor har fortfarande lika mycket pengar, bara i en annan
> valuta. Var hälften av sedlarna stulna är hälften av det som kom ut stulet också. Så tänker en utredare om en swap,
> och så räknar Chainsaw.

En **swap** är ett byte av en tillgång mot en annan i en och samma tx, oftast hos en DEX (en börs som är ett kontrakt,
till exempel Uniswap). Adressen skickar till exempel USDT till DEX:ens pool och får ether tillbaka. Pengarna byter
token men **inte ägare**, så spåret tar inte slut där. I grafen är swappen ett litet piller på linjen, **₮ → Ξ**.

## Värdet följer med

Kedjan säger bara att adressen skickade en tillgång och fick en annan. Att det ena betalade det andra är en
**bedömning**, och Chainsaw räknar den så här (analysen heter `swap-value v1`):

1. **In:** hur mycket som gick in i bytet, och hur mycket av det som var spårat enligt spårningsmetoden (FIFO eller
   haircut).
2. **Andel:** det spårade delat med allt som gick in, till exempel 62,2 %.
3. **Ut:** hur mycket som kom ut i den nya tillgången, och genom vilken DEX när Chainsaw känner kontraktet.
4. **Följer:** samma andel av det som kom ut räknas som spårat, avrundat nedåt så att inget spårat skapas.

Därefter spåras den nya tillgången som vanligt, och flera swappar i rad går att följa på samma sätt. Pengarna som gick
in i bytet räknas aldrig hos poolen: de har bytts, inte skickats vidare.

## I grafen

- Linjen **efter** swappen, och allt längre fram som vilar på den, är **streckad** i accent: beloppet bygger på en
  bedömning, som alltid när något är osäkert.
- **Hovra** över pillret, så visas stegen: in, andel, ut och vad som följer, med tx:en och analysens namn.
- *Varför?* på linjen och på det som kommer efter förklarar samma steg i klartext.

## Följer eller följer inte

Ibland vet du mer än kedjan: bytet kanske gjordes med andra pengar, eller hör inte till brottet. **Klicka** på pillret,
så fälls det ut på plats till två val:

- **Följer:** värdet följer med genom swappen. Det är förvalet.
- **Följer inte:** spåret slutar i swappen. Linjen efter blir grå och vanlig utan spårat belopp, ett litet streck över
  linjen säger att spåret slutar här, pilen i pillret blir ett stopp, och en liten **person** i hörnet visar att det är
  ditt val och inte förvalet. En börs längre fram förlorar *Uttag funnet* om inget spårat längre når dit.

**?** i pillret öppnar det här avsnittet.

Valet sparas i ärendet med vem och när, förseglat och journalfört. Det syns under **Historik** för adressen som bytte,
där det kan tas tillbaka som andra egna beslut; det gamla står kvar överstruket. Att välja **Följer** i pillret gör
samma sak.

## Exponering och rapport

[Exponering](exponering.md) följer samma regel: pengar som går in i en swap räknas vidare i den nya tillgången, värda
lika mycket i den första. Valet *Följer inte* stoppar pengarna där, som *Spåret tar slut*. Rapporten tar med en
slutsats för varje swap på spåren, med andelen och analysen, men aldrig ditt skäl.

## Vad regeln inte säger

- Att bytet faktiskt betalades med de spårade pengarna. Det är en bedömning, som FIFO och haircut.
- Något om byten **till en annan kedja**. En brygga eller swap-tjänst som byter kedja är ett slut på spåret, och
  fortsättningen knyts för hand, se [Bryggor och swappar till en annan kedja](bryggor-och-swappar.md).

## När är det en swap?

Hellre ingen swap än en felaktig: en felaktig swap flyttar de spårade pengarna från en verklig mottagare, medan en
missad bara låter spåret sluta ärligt vid poolen som *inte expanderad*. Chainsaw läser en tx som en swap bara när

- adressen fick tillbaka **en** tillgång som den inte också skickade (det som kommer tillbaka i samma tillgång, till
  exempel en återbetald avgift, är aldrig ett köp);
- det som kom tillbaka har ett värde Chainsaw känner (ether, WETH eller en token som känns igen på sitt kontrakt),
  alltid, också genom en känd DEX: vem som helst kan öppna en pool där för en egen token. Några enheter av en påhittad
  token tillbaka i samma tx, eller en egen pool med en påhittad token, gör aldrig en betalning till en swap. En token
  som Chainsaw inte känner på kontraktet står med symbolen och ett frågetecken, till exempel *USDC?*, eftersom vem som
  helst kan kalla sin token USDC;
- adressen skickade **en** tillgång, och **allt** av den gick till bytets sida: kontraktet tx:en anropade, den som
  skickade tillbaka den köpta tillgången, en känd DEX, eller en adress som i samma tx syns skicka den köpta tillgången
  (eller WETH) vidare dit;
- **priset är rimligt** (kontrollen `swap-price v1`): det som gick in och det som kom ut var värda ungefär lika mycket
  vid tx:ens tid, enligt kurserna ärendet redan har (Bitstamp för ether, USDT och USDC via Riksbanken). Skiljer de mer
  än en femtedel följer värdet inte med, eftersom vem som helst kan öppna en egen pool och byta till vilket pris som
  helst, till exempel 1 000 USDT mot en enda wei WETH. Pillret är då streckat i rött med ett stopp, och *Varför?*
  visar båda beloppen och värdena; pengarna går vidare till poolen som en vanlig betalning. Saknar ärendet en kurs
  följer värdet med som förval, och pillret är streckat i en lugn ton: *priset inte prövat*. Chainsaw hämtar då
  kurserna vid swappens tid i bakgrunden, för båda sidor och före linjernas kurser (WETH värderas som ether), och
  räknar om spåret när de kommit. Ändras spåret av prövningen säger ett meddelande det en gång, med swappen: *Spåret
  genom en swap räknades om efter kurserna*. Offline, eller när källan ber Chainsaw vänta, står priset kvar som inte
  prövat, och det står i *Varför?* och i begäran. Var det som kom ut värt mindre än det som gick in, men inom en
  femtedel, följer bara så stor del av det spårade med som det som kom ut var värt; resten **stannar i poolen** som
  avgifter och slippage, ett eget slut på spåret, så att det spårade beloppet inte ser helt ut när värde försvunnit. Var det som kom ut värt mer, följer bara den andel som det spårade var värt med, och resten
  är inte spårat: det spårade värdet växer aldrig genom en swap;
- ingen motpart är en känd brygga eller mixer, eller har en etikett som börs, mixer eller brygga: dit ska spåret nå.
  *Varför?* på linjen och noden säger då *Ingen swap* med etiketten och dess källa, och ett val *Följer inte* som
  redan finns står kvar i Historik med *ingen swap längre*.

Det gör att några riktiga byten inte syns som swappar:

- **Uniswap V2 genom routern**, när bara adressens historik är hämtad: historiken visar USDT till paret och ether från
  routern, men inte parets WETH till routern. Expandera tx:en (E på den) så att hela tx:en läses, så syns swappen.
- **En aggregator som tar en avgift** till en egen adress i samma tx: avgiften går utanför bytets sida, så tx:en läses
  inte som en swap.
- **En tx med flera tillgångar in eller ut**: det går inte att läsa vad som byttes mot vad, och då står inget piller.
