# En tx som inte finns längre

> **Som i vardagen:** Tänk på en check som skrivs ut två gånger på samma konto. Banken löser bara in den ena, och den
> andra blir aldrig pengar. Den som följer den ogiltiga checken hamnar hos någon som aldrig fick något. Chainsaw
> stannar därför vid en tx som kedjan inte längre har, och visar vilken tx som löste in pengarna i stället.

Ibland säger källan att den inte känner till en tx som ärendet har sett, ibland till och med i ett block. Det händer
när kedjan har organiserats om (se [Hur Chainsaw kontrollerar svaren mot kedjan](kontroll-mot-kedjan.md)) och en
annan tx har hunnit spendera samma mynt, eller när en obekräftad tx har ersatts eller fallit ur mempool. I grafen får
linjen ett rött märke, **Finns inte längre**, och beloppet stryks över.

## Varför spåret slutar där

En tx som inte finns i kedjan har aldrig flyttat några pengar. Allt som bygger på den (mottagarna och det de gjorde
sedan) är därför osäkert, och Chainsaw räknar inte spårade belopp genom den. Spåret slutar vid tx:en med orsaken
**Tx:en finns inte längre**, och *Varför?* säger vilket block ärendet såg den i och vilket svar som säger att den är
borta. Linjerna och aktörerna efter den står kvar i grafen, streckade och svaga, och ingenting tas bort ur ärendet:
både det gamla och det nya svaret ligger kvar, förseglade.

Att tx:en är borta är en **bedömning** av vad källan svarar, inte ett faktum. Chainsaw kräver därför två svar i rad,
från olika endpoints eller med ett nytt block emellan, från endpoints som hade kommit minst lika långt som blocket.
Spåret **slutar** vid tx:en först när det är bekräftat av mer än en källa: endpoints på två olika värdar har sagt det,
eller en annan värd än den som sa det svarar att en annan tx spenderar mynten eller att de är fria igen (Chainsaw frågar
då en annan värd om mynten, när ärendet har fler). Har ärendet bara hört från en enda värd räcker dess ord, men slutet
säger då i *Varför?* och i rutan att det vilar på en enda källa, och vilken. Annars, och när källan samtidigt säger att
tx:en fortfarande spenderar mynten, **stannar** spåret där med *Tx:en kanske inte finns; uppgifterna går inte ihop*.
Det är inget slut: Chainsaw gissar inte åt något håll, och nästa svar kan reda ut det.

## Ersatt av

Ett mynt kan bara spenderas en gång. Är tx:en borta frågar Chainsaw, genom utgången, vem som spenderar mynten den
spenderade: en fråga per tx som mynten kom ur (`/tx/:txid/outspends`), högst tjugo åt gången; rutan säger hur många
som är frågade, och resten frågas vid nästa uppslag. Bara de tx:ernas id skickas,
aldrig ärendet, och varje svar förseglas. En ersättare som ännu inte ligger i ett block frågas om igen, eftersom den i
sin tur kan ha ersatts. Svaren blir rader i märkets ruta, med ersättarens block eller *obekräftad*:

- **Ersatt av …** när en annan tx spenderar mynten. Det kallas **RBF** (replace-by-fee) när avsändaren skickar om
  samma betalning med högre avgift, ofta till samma mottagare, och en **dubbelspendering** när mynten går till någon
  annan. Spenderas mynten av flera olika tx får var och en sin rad.
- **Föll bort utan ersättare** när inget spenderar mynten. Pengarna ligger kvar där de kom ifrån.

## Följ ersättaren

Att ersättaren för *samma pengar* vidare är en bedömning, så spåret följer den inte av sig självt. **Följ ersättaren**
i rutan hämtar tx:en till grafen och låter spåret gå vidare genom den. Länken syns bara när källan kan frågas, och
Chainsaw kontrollerar att ersättarens egna inputs spenderar just de mynten med samma belopp innan valet sparas. Valet
förseglas med vem och när och står i journalen, och beloppen efter ersättaren ritas **streckade**, som allt som
bygger på en bedömning. *Ta tillbaka* i rutan (eller *Ångra* i meddelandet) gör att spåret slutar vid den borttagna
tx:en igen; valet står kvar förseglat.

## Slutsatser som bygger på den

Klustringen räknar inte med en tx som inte finns längre. När grafen expanderas nästa gång körs heuristikerna utan den,
och en slutsats som bygger på den (tx:ens egna sammanslagningar, eller en växeladress som bedömdes ny för att adressen
först sågs i den) **hålls tillbaka**. Klustret får då en tyst markering i hörnet, en streckad gul ring med pausmärke,
och hovringen säger vilken tx slutsatsen byggde på och vad som ersatte den.

Ingenting tas bort och ingenting slutar räknas av sig självt: slutsatsen står kvar, förseglad, tills du väljer i
*Historik*. **Låt stå** behåller den, och nästa körning respekterar det. **Släpp slutsatsen** gör att den inte räknas
längre (också dess fynd), men den står kvar förseglad. Nästa körning drar nya slutsatser om tx:en bara när de inte
bygger på den borta tx:en; annars väntar den tills tx:en är tillbaka.
Båda valen förseglas med vem och när, och kan tas tillbaka.

Klustringen drar inte heller några **nya** slutsatser av att tx:en saknas. En adress som först syntes i den räknas
fortfarande som sedd, eftersom den visades för världen där. En ny slutsats som skulle bygga på den borta tx:en dras
inte; tx:ens andra slutsatser dras ändå och vägs som om den heuristiken inte hade någon åsikt. Har den heuristiken
redan en slutsats om tx:en görs ingenting om, eftersom en tx:s slutsatser vägs ihop: det som står kvar står kvar, och
när tx:en kommer tillbaka görs allt om. Journalen säger vilka slutsatser som inte drogs, när det ändras. Drar du
tillbaka en version av en heuristik medan tx:en är borta görs inte heller det som byggde på den om.

Bara en tx som det är **bekräftat** att den är borta lämnas utanför, efter samma regel som avgör var spåret slutar. När
spåret bara stannar, för att en enda värd sagt det medan ärendet har svar från fler eller för att svaren inte går ihop,
körs klustringen med tx:en som förut. Kommer tx:en tillbaka i ett block körs den med igen, och slutsatserna som hölls
tillbaka räknas som vanligt. Att följa ersättaren ändrar inte detta: den borta tx:en är fortfarande borta, och
ersättaren klustras som vilken tx som helst.
