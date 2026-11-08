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

`peel-chain v2` räknar ett hopp när tx:en har två outputs. Den output som går vidare är den som nästa hopp spenderar,
när ärendet känner till det; annars den stora, när den är minst fyra gånger den lilla. När ärendet känner till **minst
tre hopp i rad**, där den output som går vidare i ett hopp är en input i nästa, är det en peel chain:

- **Hopp som fylls på:** ett hopp får ha fler inputs än myntet från hoppet före. Det är vanligt att kedjan fylls på
  med nya pengar på vägen, och slutsatsen säger hur många inputs som kom till.
- **Växeln behöver inte vara den största:** går den mindre outputen vidare till nästa hopp är det den som följs, och
  *Varför?* säger det.

- den output som går vidare i varje hopp **föreslås** som hoppets [växel](vaxeladress.md), men slås inte ihop automatiskt. Formen
  räcker inte: många vanliga betalningar ser likadana ut, så ett offers egna betalningar kan få betalningen till
  bedragaren att se ut som ett hopp mitt i en kedja, och det sista hoppet kan skicka huvuddelen till en börs. Du
  godkänner förslagen när du har sett kedjan (godkännandet byggs med gränssnittet);
- de avskalade outputarna slås **aldrig** ihop; de är mottagare och ofta det intressanta: där kan en börs ta emot.

Två hopp räcker inte. Kedjan känns igen först när fler hopp har hämtats, och då kör Chainsaw analysen igen och sparar
den nya slutsatsen bredvid den gamla. Slutsatsen säger vilka andra hopp (tx) den bygger på.

## I utredningen

Följ den output som går vidare för att hålla jämna steg med pengarna, och titta på de avskalade för att hitta uttagen.
Slutar kedjan vid en börs (den sista växeln går till en adress som en etikett säger är en börs) säger kontrollen
*Peel chain till börs*: där tas pengarna troligen ut, och insättningen hör till en begäran.
