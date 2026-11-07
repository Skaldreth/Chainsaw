# Kontomodellen

Ethereum (och TRON) har inga outputs som spenderas, som Bitcoin i [UTXO-modellen](utxo-modellen.md). En adress har ett
**konto** med ett **saldo** per tillgång: ett i kedjans eget mynt (ether, ETH) och ett i varje token den har tagit
emot. En tx flyttar belopp från ett saldo till ett annat.

## Vad kedjan säger, och vad den inte säger

Kedjan säger vem som skickade, till vem, hur mycket, i vilken tx och i vilket block. Det är fakta. Men pengar som
kommer in på ett konto **blandas med det som redan finns där**: kedjan säger aldrig vilka av kontots pengar som gick
ut i nästa tx. Därför är ett spårat belopp genom ett konto alltid en **bedömning** efter en spårningsmetod, precis som
genom en Bitcoin-tx.

## Tre sorters rörelser

En tx på Ethereum kan flytta pengar på tre sätt, och Chainsaw läser alla tre:

- **Ether som tx:en själv skickar** (dess `value`), från den som signerade tx:en.
- **[Interna tx](interna-tx.md)**: ether som ett kontrakt skickar medan tx:en körs.
- **[Tokenöverföringar](erc-20.md)** (ERC-20), som kontraktet skriver som händelser i tx:ens logg.

En tx som **misslyckas** flyttar ingenting, men den som signerade den betalar ändå avgiften. En tx som ännu inte
ligger i ett block är **obekräftad**: om den lyckas och vad den flyttar avgörs först när den körs.

Två sorters ether syns inte i de här tre: validatorernas uttag och det den som föreslår ett block får i dricks.
Chainsaw läser dem inte än, så ett saldo räknat ur överföringarna kan vara för lågt för en sådan adress.

## Avgiften

Avgiften (gas) betalas alltid i ether av den som signerade tx:en, också när det som flyttades var en token. Den är
tx:ens använda gas gånger priset per gas, så som kedjan tar betalt. Grundavgiften bränns, och dricksen går till den
som föreslog blocket. Ingen av delarna är en överföring som går att följa vidare, så i spårningen är avgiften ett eget
slut för pengarna, som avgiften till minerare i Bitcoin.

## Spårningsmetoderna för konton

Ett konto spåras per tillgång: ett saldo i ether och ett per token, aldrig blandade.

- **FIFO** ser saldot som en kö. Det som kom in först går ut först. Spårade pengar följer alltså med de första
  överföringarna efter att de kom in. Saldot som fanns före det Chainsaw har läst räknas som ospårat.
- **Haircut** låter varje överföring ut ta den spårade andelen av saldot just då. De spårade pengarna tunnas ut över
  allt som går ut efteråt.

Varje uträkning kontrolleras: det som kom in spårat ska vara exakt det som gick ut, till avgifter och det som ligger
kvar. Går det mer ut än den lästa historiken visar kom in, räknas resten som ospårat;
en metod hittar aldrig på pengar.

## I grafen

Varje Ethereum-adress är en egen aktör (rund), eftersom kontomodellens heuristiker för plånböcker inte är byggda än.
Att expandera en adress hämtar dess tre historiker (tx, interna tx och tokenöverföringar) sida för sida, de nyaste
först upp till gränsen; att expandera en tx hämtar alla rörelser i den. Linjerna summerar per par av aktörer och per
tillgång, så USDT och ether mellan samma två adresser är två linjer.

Spårningen börjar i det som kom in till en startadress, eller i en start-tx:s rörelser till andra än den som
signerade den. Pengarna förs vidare bara genom adresser vars historik är expanderad; når de en annan adress stannar de
där som *inte expanderad än*, och når de en börs eller en annan etikett som avslutar spåret stannar de där. Startpunkter
som ligger i varandras flöde spåras inte alls, hellre än att samma pengar räknas två gånger.

En adress vars historik bara är **delvis** läst (fler tx än gränsen på 50, eller ett svar som inte var komplett) för
inte pengarna vidare, eftersom det kan saknas rörelser mellan det lästa och det spårade; spåret stannar där och säger
varför, och saldot visas inte. Det är en gräns i Chainsaw just nu, inte i kedjan: för en mycket aktiv adress får
pengarna följas för hand från linjerna tills historiken kan läsas längre bakåt.

Kedjan säger inte i vilken ordning två tx i samma block kom, i den lästa historiken. Chainsaw ordnar dem då efter
hashen, vilket bara spelar roll för FIFO när en adress både tar emot och betalar i samma block.

Kontrollerna och förslagen (CoinJoin, peel chain, mixerns kandidater, plånböcker) gäller UTXO-modellen och körs inte på
Ethereum; listan säger det.

## Var svaren kommer ifrån

Ethereums svar hämtas från en källa med samma API som explorern **Blockscout**, som går att köra själv. Varje svar
förseglas i ärendet med endpoint, utgång och tid, och det som vilar på ett **slutgiltigt block** (64 block djupt,
ungefär 13 minuter) hämtas aldrig igen. Varje steg visar blockets höjd och hash, så att det kan kontrolleras mot
kedjan.