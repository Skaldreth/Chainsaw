# Peel chain

En **peel chain** är ett vanligt sätt att flytta ett stort belopp i små delar. Varje steg (hopp) är en tx med:

- **en input:** det som är kvar sedan förra hoppet;
- **en liten output** som skalas av, ofta till en börs eller en mottagare;
- **en stor output** med resten, till en ny adress, som nästa hopp spenderar.

```
100 BTC ─┬─ 1,5 BTC  (skalas av)
         └─ 98,5 BTC ─┬─ 1,6 BTC  (skalas av)
                      └─ 96,9 BTC ─┬─ ...
```

Den stora outputen är **växeln**: samma ägare som hoppets input. De små är **betalningar** till andra.

## Hur Chainsaw känner igen den

`peel-chain v1` räknar ett hopp när tx:en har exakt en input och två outputs, och den stora är minst fyra gånger den
lilla. När ärendet känner till **minst tre hopp i rad**, där den stora outputen i ett hopp är inputen i nästa, är det
en peel chain:

- den stora outputen slås ihop med hoppets input i varje hopp **inne i kedjan**, så att innehavarna blir ett kluster;
- i kedjans **ändar** blir det bara ett förslag. Det första hoppet kan vara ett offer som betalar nästan allt till
  bedragaren, och det sista kan skicka huvuddelen till en börs insättningsadress. Ingen av dem är kedjans växel, och
  att slå ihop dem skulle göra offret eller börsens alla kunder till en del av bedragarens plånbok;
- de avskalade outputarna slås **aldrig** ihop; de är mottagare och ofta det intressanta: där kan en börs ta emot.

Två hopp räcker inte. Kedjan känns igen först när fler hopp har hämtats, och då kör Chainsaw analysen igen och sparar
den nya slutsatsen bredvid den gamla. Slutsatsen säger vilka andra hopp (tx) den bygger på.

## I utredningen

Följ den stora outputen för att hålla jämna steg med pengarna, och titta på de avskalade för att hitta uttagen.
