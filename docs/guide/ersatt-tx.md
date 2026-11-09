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
Spåret **slutar** vid tx:en först när det är bekräftat: endpoints på två olika värdar har sagt det (eller den enda som
är påslagen), eller svaren om mynten visar att en annan tx spenderar dem eller att de är fria igen. Annars, och när
källan samtidigt säger att tx:en fortfarande spenderar mynten, **stannar** spåret där med *Tx:en kanske inte finns;
uppgifterna går inte ihop*. Det är inget slut: Chainsaw gissar inte åt något håll, och nästa svar kan reda ut det.

## Ersatt av

Ett mynt kan bara spenderas en gång. Är tx:en borta frågar Chainsaw, genom utgången, vem som spenderar mynten den
spenderade: en fråga per tx som mynten kom ur (`/tx/:txid/outspends`), högst tjugo. Bara de tx:ernas id skickas,
aldrig ärendet, och varje svar förseglas. En ersättare som ännu inte ligger i ett block frågas om igen, eftersom den i
sin tur kan ha ersatts. Svaren blir rader i märkets ruta, med ersättarens block eller *obekräftad*:

- **Ersatt av …** när en annan tx spenderar mynten. Det kallas **RBF** (replace-by-fee) när avsändaren skickar om
  samma betalning med högre avgift, ofta till samma mottagare, och en **dubbelspendering** när mynten går till någon
  annan. Spenderas mynten av flera olika tx får var och en sin rad.
- **Föll bort utan ersättare** när inget spenderar mynten. Pengarna ligger kvar där de kom ifrån.

## Följ ersättaren

Att ersättaren för *samma pengar* vidare är en bedömning, så spåret följer den inte av sig självt. **Följ ersättaren**
i rutan hämtar tx:en till grafen och låter spåret gå vidare genom den. Länken syns bara när källan kan frågas;
Chainsaw kontrollerar att ersättarens egna inputs spenderar just de mynten med samma belopp innan valet sparas. Valet förseglas med vem och när och står i
journalen, och beloppen efter ersättaren ritas **streckade**, som allt som bygger på en bedömning. *Ta tillbaka* i
rutan (eller *Ångra* i meddelandet) gör att spåret slutar vid den borttagna tx:en igen; valet står kvar förseglat.
