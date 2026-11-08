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

En känd brygga eller swap (en etikett) avslutar spåret med *Fortsätter på annan kedja*, i **lila**. När du har hittat
fortsättningen knyter du den för hand med **K**, se [Fortsättningen på en annan kedja](annan-kedja.md). Länken är
streckad, eftersom den är din bedömning. Ett byte av token på samma kedja står som ett litet piller på linjen (₮ → Ξ).

Att Chainsaw själv föreslår fortsättningen ur belopp och tid är **inte byggt än**. Att spåra vidare på TRON är inte
heller byggt än.
