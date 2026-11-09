# Privacy coins: Monero, Zcash och andra

> **Som i vardagen:** Bitcoin är som ett glashus där alla kan se varje kuvert som byter hand, men inte vem som håller
> det. En privacy coin är ett hus med frostade fönster: man ser att det är folk där inne, men inte vem som ger vad till
> vem, eller hur mycket.

En **privacy coin** är en kryptovaluta som är byggd för att dölja det som Bitcoin visar öppet: vem som betalar, vem som
får och hur mycket.

![In i en privacy coin: spåret går att följa in i växlingen, och sedan tar det slut](figur:s-privacy)

## De vanligaste

- **Monero (XMR):** allt är dolt som standard. Varje betalning blandas med andra som lockbeten (*ring signatures*),
  mottagaren får en ny engångsadress (*stealth address*) och beloppet är krypterat.
- **Zcash (ZEC):** har två delar. Den genomskinliga delen (adresser som börjar med `t`) fungerar som Bitcoin och går att
  följa. Den **skyddade poolen** döljer allt. Det är när pengarna går in i poolen som spåret tar slut.
- **Litecoin MWEB:** ett tillägg till Litecoin där pengar kan flyttas in i en dold del och ut igen.

## Det som går att se

Det som går att följa är vägen **in**: bitcoin eller USDT som skickas till en swap-tjänst eller börs som byter till
Monero. Det ger två möjligheter:

- **Tjänsten som växlade** vet ofta vem kunden var, eller åtminstone vart Monero skickades. En begäran dit kan ge mer än
  kedjan.
- **Vägen ut** ur privacy coinen sker ofta på samma sätt: Monero växlas tillbaka till bitcoin eller USDT någonstans.
  Belopp och tid kan ibland peka ut kandidater, men det är alltid en bedömning.

Forskningen har hittat svagheter i äldre Monero-tx och i Zcash-tx som går in och ut ur poolen med samma belopp. Det är
avancerade metoder, och de ger sannolikheter, aldrig visshet.

## I Chainsaw

Pengar som går till en privacy coin avslutar spåret med orsaken **Privacy coin**, i rött. Spåret slutar aldrig tyst:
det står varför (se [Varför ett spår tar slut](sparet-tar-slut.md)).

Zcashs genomskinliga del och Litecoin spåras i grafen. Pengar in i Zcashs pooler eller Litecoins MWEB tar slut där,
med beloppet in och ut som fakta, och `shielded-pool v1` föreslår troliga vägar ut ur poolen ur belopp och tid, som
streckade kandidater (se [Ut ur en skyddad pool](skyddade-pooler.md) och [Zcash](zcash.md)). Monero är inte byggt än.
