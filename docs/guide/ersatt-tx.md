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

## En obekräftad tx som aldrig låg i ett block

En tx som bara har legat i **mempool** (kön av tx som väntar på ett block) kan försvinna utan att någon kedja
organiseras om: avsändaren ersätter den med RBF, en dubbelspendering hinner före, eller noderna kastar den för att
avgiften var för låg. En publik källa har dessutom många noder bakom en adress, och en helt ny tx har inte alltid
hunnit fram till alla. Chainsaw räknar därför en sådan tx som borta först när regeln `mempool-gone v1` håller:

- två svar i rad efter det senaste som hade tx:en säger att källan inte känner till den, från olika endpoints eller
  med ett nytt block emellan, och från endpoints som hade kommit minst lika långt som ärendet när det sist såg tx:en;
- det nyaste av dem kom **minst en timme** efter att ärendet sist såg tx:en (sex Bitcoin-block, hundratals på
  Ethereum), så att en tx som var giltig och betalade sin avgift normalt hade hunnit in i ett block;
- och ärendet hade då sett **minst ett nytt block**, så att kedjan har gått vidare utan den.

När regeln håller första gången **förseglas** det, med en rad i journalen som säger vilka svar det bygger på. Ingen tx
räknas som borta ur mempool utan en sådan rad. Kommer ett nytt svar som har tx:en igen, i mempool eller i ett block,
förseglas det också, med en egen rad i journalen, och tx:en räknas som förut. Inget av de gamla svaren eller raderna
tas bort. Märket i grafen är detsamma, **Finns inte längre**, men första raden i rutan säger *mempool* i stället för
ett block, och *Varför?* säger att tx:en aldrig låg i ett block. Resten är som för en tx som föll ur ett block: samma
krav på mer än en källa för att spåret ska sluta, och samma *Ersatt av*.

## På Ethereum och andra kontokedjor

På en kontokedja (Ethereum, BSC, TRON) finns inga mynt: varje adress har ett saldo, och en tx som inte finns i kedjan
har inte ändrat något saldo. Chainsaw läser därför en borta tx på samma sätt som på Bitcoin, med samma krav på mer än
en källa, men räknar den så här:

- **Bekräftat borta:** tx:ens rörelser och avgift tas bort ur varje adress huvudbok i spåret, exponeringen, Flöde,
  begäran och rapporten. Pengarna ligger alltså kvar hos avsändaren, så som kedjan säger, och spårningsmetoden (FIFO
  eller haircut) fördelar dem på adressens andra utbetalningar, till exempel den tx som skickades i stället. Det som
  ligger kvar säger i *Varför?* att en borta tx inte räknas, med tx:en som underlag. Ett spår som börjar i en borta tx
  slutar genast, hos avsändaren, med *Tx:en finns inte längre*. En swap i en borta tx är ingen swap.
- **Inte bekräftat** (en enda värd har sagt det medan ärendet har svar från fler): tx:en står kvar i huvudboken, men
  det spårade som går in i den **stannar** där med *Tx:en kanske inte finns; uppgifterna går inte ihop*, och
  exponeringen räknar det som *Uppgifterna går inte ihop*.

Linjerna står kvar i grafen, gråa och streckade som förut, och alla svar ligger kvar förseglade.

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
inte, och tx:ens andra slutsatser dras bara om de väger lika utan den, så att ingen annan slutsats blir starkare för
att en som höll emot lades åt sidan. Annars, och när samma heuristik redan har en slutsats om tx:en, görs ingenting
om: det som står kvar står kvar, och när tx:en kommer tillbaka görs allt om. Journalen säger vilka slutsatser som
inte drogs, när det ändras. Drar du tillbaka en version av en heuristik medan tx:en är borta görs inte heller det som
byggde på den om.

Bara en tx som det är **bekräftat** att den är borta lämnas utanför, efter samma regel som avgör var spåret slutar. När
spåret bara stannar, för att en enda värd sagt det medan ärendet har svar från fler eller för att svaren inte går ihop,
körs klustringen med tx:en som förut. Kommer tx:en tillbaka i ett block körs den med igen, och slutsatserna som hölls
tillbaka räknas som vanligt. Att följa ersättaren ändrar inte detta: den borta tx:en är fortfarande borta, och
ersättaren klustras som vilken tx som helst.
