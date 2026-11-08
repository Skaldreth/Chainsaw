# Peel chain

> **Som i vardagen:** Tänk på en lök som skalas ett lager i taget, eller en salami som skivas: varje gång går en tunn
> skiva iväg, och det mesta ligger kvar och skivas igen. Den som vill flytta en stor summa utan att det syns skickar
> den inte på en gång, utan skalar av lite åt gången, ofta till en börs, och skickar resten vidare till en ny adress.

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

![En trappa: den tjocka linjen går ner steg för steg och blir lite tunnare för varje hopp, när en tunn skiva skalas av uppåt. Skivorna går ofta till en börs (blå) eller till okända mottagare.](figur:peel-trappa)

## Hur Chainsaw känner igen den

`peel-chain v1` räknar ett hopp när tx:en har exakt en input och två outputs, och den stora är minst fyra gånger den
lilla. När ärendet känner till **minst tre hopp i rad**, där den stora outputen i ett hopp är inputen i nästa, är det
en peel chain:

- den stora outputen i varje hopp **föreslås** som hoppets [växel](vaxeladress.md), men slås inte ihop automatiskt i version 1. Formen
  räcker inte: många vanliga betalningar ser likadana ut, så ett offers egna betalningar kan få betalningen till
  bedragaren att se ut som ett hopp mitt i en kedja, och det sista hoppet kan skicka huvuddelen till en börs. Du
  godkänner förslagen när du har sett kedjan (godkännandet byggs med gränssnittet);
- de avskalade outputarna slås **aldrig** ihop; de är mottagare och ofta det intressanta: där kan en börs ta emot.

Två hopp räcker inte. Kedjan känns igen först när fler hopp har hämtats, och då kör Chainsaw analysen igen och sparar
den nya slutsatsen bredvid den gamla. Slutsatsen säger vilka andra hopp (tx) den bygger på.

## I utredningen

Följ den stora outputen för att hålla jämna steg med pengarna, och titta på de avskalade för att hitta uttagen.
