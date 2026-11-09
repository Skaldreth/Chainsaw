# Bryggor och swappar till en annan kedja

> **Som i vardagen:** att byta kedja är som att växla pengar vid en gräns och fortsätta resan i ett annat land med en
> annan valuta. Tullen i det första landet ser dig gå in på växelkontoret. Vad som händer på andra sidan syns bara i det
> andra landets register, och bara om någon vet vad den ska leta efter.

Varje blockkedja är ett eget register. Bitcoin vet ingenting om Ethereum, och Ethereum ingenting om TRON. Den som vill
försvåra en spårning byter därför gärna kedja, och ofta token, en eller flera gånger.

## Tre sätt att byta

- **Swap-tjänst:** en tjänst som tar emot en valuta och skickar ut en annan, ofta utan konto och utan kundkontroll. Du
  skickar bitcoin till en adress de ger dig, och får USDT på TRON till en adress du anger.
- **Brygga:** ett system som låser pengar på en kedja och ger ut motsvarande på en annan. Pengarna *flyttar* inte;
  bryggan håller det ena och ger ut det andra.
- **DEX** (decentraliserad börs): ett kontrakt på samma kedja som byter en token mot en annan, till exempel ETH mot
  USDT. Kedjan är densamma, men tillgången byts.

![En brygga eller swap: spåret lämnar Bitcoin, och fortsättningen på den andra kedjan knyts för hand](figur:s-brygga)

## Varför det försvårar

- **Ingen tx binder ihop sidorna.** Spåret på den första kedjan slutar i tjänsten; på den andra börjar ett nytt.
- **Tjänsten blandar.** Många kunder skickar in samtidigt, så det kan finnas flera kandidater på andra sidan.
- **Beloppet ändras** med växelkursen och avgiften, så det går inte att leta efter exakt samma siffra.
- **Tiden glider:** utbetalningen kan dröja från minuter till timmar.

## Så hittar man fortsättningen

- **Belopp och tid:** värdet som gick in, omräknat till den andra tillgången, minus en rimlig avgift, och en utbetalning
  strax efter. Kurserna i Chainsaw hjälper att räkna om (se [Kurser och omräkning](kurser.md)).
- **Tjänstens egna verktyg:** många swap-tjänster och bryggor visar båda sidorna av en växling om man har tx:en.
- **En begäran** till tjänsten, om den har ett företag bakom sig (se [Begäran om uppgifter](begaran.md)).

## I Chainsaw

En känd brygga eller swap (en etikett) avslutar spåret med *Fortsätter på annan kedja*, i **lila**. Chainsaw föreslår
själv troliga fortsättningar ur avsändarens memo, värde och tid (**cross-chain v1**), och du godkänner eller avfärdar dem;
du kan också knyta fortsättningen för hand med **K**, se [Fortsättningen på en annan kedja](annan-kedja.md). Länken är
streckad, eftersom den är din bedömning. Ett byte av token på samma kedja står som ett litet piller på linjen (₮ → Ξ),
se [Spåret genom en swap](swappar.md).

### Kända kontrakt på Ethereum

Chainsaw har en egen lista över **kända kontrakt** på Ethereum, var och en med sidan adressen lästes från och dagen:

- **Tornado Cash** (en mixer): poolerna har fasta belopp (0,1, 1, 10 och 100 ETH, och några i DAI, USDC, USDT och
  WBTC). Den som sätter in får ett kvitto, och kan ta ut till vilken adress som helst, så kedjan säger inte vem som tog
  ut. En adress som är en pool får etiketten *Mixer*, och spåret tar slut där i rött. Den får också en nedtonad
  markering i hörnet, **Tidigare sanktionerad**: OFAC satte Tornado Cash på SDN-listan 2022-08-08 och tog bort den
  2025-03-21. Datumen och OFAC:s egna meddelanden står i *Varför?*. Det är ingen sanktion som gäller nu; en sanktion
  kommer bara ur listan du själv har läst in.
- **Bryggor** till Arbitrum, OP Mainnet, Base och Polygon: får etiketten *Swap eller brygga*, och spåret tar slut i
  lila med *Fortsätter på annan kedja*.
- **DEX:ar och aggregatorer** (Uniswap, 1inch, 0x): får ingen etikett, eftersom ett byte där inte byter ägare. När en
  swap gick genom en av dem står namnet i *Varför?* för swappen.

Etiketten är en bedömning med listan som källa, inte ett faktum, och kan tas tillbaka som andra etiketter. Listans
namn och version står i etikettens ursprung, så att det går att se vilken lista den kom från.

Förslagen på fortsättningen ur memo, värde och tid står i [Fortsättningen på en annan kedja](annan-kedja.md). Bryggorna
till Arbitrum, OP Mainnet, Base och Polygon leder till kedjor Chainsaw inte har än, så där kan Chainsaw inte föreslå
något. Vidare på Ethereum och TRON går det att spåra, när fortsättningen är knuten (se [TRON](tron.md)).

