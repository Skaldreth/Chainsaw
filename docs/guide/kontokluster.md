# Kluster på Ethereum

> **Som i vardagen:** en butikskedja ger varje kund ett eget kuvert att lägga pengarna i, och varje kväll töms alla
> kuvert i samma kassaskåp. Den som ser kuverten tömmas där förstår att de hör till samma butik, men inte vem som
> lade i pengarna.

På Ethereum finns inga inputs som slås ihop, som på Bitcoin ([common-input-ownership](common-input-ownership.md)).
Varje adress är ett konto. Klustringen läser i stället **hur pengarna rör sig mellan kontona**. Den räknar bara med
adresser vars hela historik är läst, och bara med tx som inte är borta.

## deposit-sweep v1

En börs ger varje kund en egen **insättningsadress**. Det som kommer in töms sedan, nästan helt och bara, till börsens
egen adress (*hot wallet*). Chainsaw letar efter adresser som:

- tog emot pengar från någon annan än mottagaren,
- inom tre dygn efter varje insättning skickade minst 95 % av det, och bara till **en** adress,
- och där minst två sådana adresser töms till samma adress.

Har mottagaren en **etikett som börs** slås adresserna ihop med den av sig själv, som ett kluster. Saknas etiketten blir
det bara ett **förslag**: offer som köper krypto och skickar allt till samma bedragare ser exakt likadana ut, och de är
inte samma ägare. Ett kontrakt (en pool, en router, en mixer eller en brygga) är aldrig mottagaren.

## gas-funding v1

Den som ska skicka en token behöver lite av kedjans eget mynt till gasen. Har flera adresser fått sina **allra första
pengar** som lite ETH från samma adress, och använt det till att skicka en token innan de skickade något annat, kan de
vara uppsatta av samma person. Det är svagt, eftersom en tjänst kan betala gas åt många, så det blir alltid ett
**förslag** som du godkänner eller avfärdar.

## I grafen

Ett kluster står först som **en fyrkantig nod**, *Trolig tjänst*, med antalet adresser. Ett klick fäller ut det: varje
adress blir en egen nod, med ett blått område bakom. Ett klick på områdets bricka fäller ihop det igen. Klustret är en
**bedömning**, aldrig ett faktum; inspektören säger varför, med heuristiken och versionen.
