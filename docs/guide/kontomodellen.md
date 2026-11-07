# Kontomodellen

Ethereum (och TRON) har inga outputs som spenderas, som Bitcoin i [UTXO-modellen](utxo-modellen.md). En adress har ett
**konto** med ett **saldo** per tillgång: ett i kedjans eget mynt (ether, ETH) och ett i varje token den har tagit emot.
En tx flyttar belopp från ett saldo till ett annat.

## Vad kedjan säger, och vad den inte säger

Kedjan säger vem som skickade, till vem, hur mycket, i vilken tx och i vilket block. Det är fakta. Men pengar som kommer
in på ett konto **blandas med det som redan finns där**: kedjan säger aldrig vilka av kontots pengar som gick ut i nästa
tx. Därför är ett spårat belopp genom ett konto alltid en **bedömning** efter en spårningsmetod, precis som genom en
Bitcoin-tx.

## Tre sorters rörelser

En tx på Ethereum kan flytta pengar på tre sätt, och Chainsaw läser alla tre:

- **Ether som tx:en själv skickar** (dess `value`), från den som signerade tx:en.
- **[Interna tx](interna-tx.md)**: ether som ett kontrakt skickar medan tx:en körs.
- **[Tokenöverföringar](erc-20.md)** (ERC-20), som kontraktet skriver som händelser i tx:ens logg.

En tx som **misslyckas** flyttar ingenting, men den som signerade den betalar ändå avgiften.

## Avgiften

Avgiften (gas) betalas alltid i ether av den som signerade tx:en, också när det som flyttades var en token. Den är tx:ens
använda gas gånger priset per gas, så som kedjan tar betalt. Ingen tar emot den som ett konto gör, så i spårningen är den
ett eget slut för pengarna, som avgiften till minerare i Bitcoin.

## Spårningsmetoderna för konton

Ett konto spåras per tillgång: ett saldo i ether och ett per token, aldrig blandade.

- **FIFO** ser saldot som en kö. Det som kom in först går ut först. Spårade pengar följer alltså med de första
  överföringarna efter att de kom in. Saldot som fanns före det Chainsaw har läst räknas som ospårat.
- **Haircut** låter varje överföring ut ta den spårade andelen av saldot just då. De spårade pengarna tunnas ut över
  allt som går ut efteråt.

Chainsaw kontrollerar varje uträkning: det som kom in spårat är exakt det som gick ut, till avgifter och det som ligger
kvar. Går det mer ut än den lästa historiken visar kom in, räknas resten som ospårat; en metod hittar aldrig på pengar.

## Var svaren kommer ifrån

Ethereums svar hämtas från en källa med samma API som explorern **Blockscout**, som går att köra själv. Varje svar
förseglas i ärendet med endpoint, utgång och tid, och det som vilar på ett **slutgiltigt block** (64 block djupt, ungefär
13 minuter) hämtas aldrig igen. Varje steg visar blockets höjd och hash, så att det kan kontrolleras mot kedjan.
