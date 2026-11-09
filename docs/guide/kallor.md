# Källorna och deras endpoints

> **Som i vardagen:** att fråga kedjan är som att ringa ett av flera arkiv som har samma handlingar. Du ringer det som
> svarar snabbast. Säger ett arkiv *ring tillbaka om en stund* gör du det, och under tiden frågar du ett annat. Du
> ringer aldrig så ofta att de tröttnar på dig.

Chainsaw har ingen egen kopia av blockkedjorna. Den frågar **källor**: öppna tjänster som läser kedjan och svarar på
frågor som *vilka tx har den här adressen?*. För Bitcoin är källan ett **Esplora-API**, samma API som mempool.space och
Blockstream har. För Ethereum är det Blockscout. En källas adress kallas **endpoint**.

## Sidan Källor

Öppna *Inställningar → Källor* (Ctrl+,). Till vänster står kedjorna; kedjor som inte är byggda säger det. För varje
kedja väljer du ett av tre lägen:

- **Snabbast** (förval). Chainsaw provar alla påslagna endpoints när den startar, genom utgången och med ett uppslag
  som inte säger något om ärendet (kedjans höjd), och använder den som svarar snabbast och rätt.
- **Prioriterad.** Endpointarna används i din ordning. Dra i greppet, eller använd Alt+↑ och Alt+↓, för att flytta en.
  Nästa tas när den första inte svarar.
- **Av.** Inga uppslag görs för kedjan. Grafen säger *Av*, och bara det ärendet redan har visas.

![Flera endpoints för samma kedja: den snabbaste som svarar rätt används, och en som är tyst lämnas](figur:s-endpoints)

*+ Lägg till endpoint …* tar en https-adress, och *Prova* frågar den direkt. *Prova alla nu* gör om provet för alla.
Varje endpoint kan slås av och på och tas bort.

## Snygg reserv

En endpoint som inte svarar, svarar fel eller ber Chainsaw vänta **lämnas tyst** för de andra. Den får en paus, en
minut som fördubblas för varje misslyckande i rad, upp till en halvtimme, och kommer tillbaka när den svarar igen.
Raden säger *Svarar inte* eller *Väntar*.

## Takten

Chainsaw frågar aldrig för fort:

- Varje endpoint på internet frågas **högst fyra gånger i sekunden**. Det är Chainsaws eget försiktiga val, inte
  källornas egna gränser.
- Säger källan **429** (för många frågor), eller ber den om väntan på annat sätt, väntar Chainsaw så länge den säger och
  frågar en annan under tiden.
- Uppslagen sprids över endpointarna och ställer sig i kö i stället för att gå alla på en gång.
- Kan ett uppslag inte få en tur inom två minuter svarar ärendet med det det har, och säger varför.

## Ett block efter

En endpoint kan ligga ett block efter de andra, eftersom nya block sprids efterhand. För vanliga uppslag räknas det som
i fas. **Bevakningen** är strängare och använder bara en endpoint som svarade med den högsta höjden, annars kunde den
missa det nyaste blocket. Se [Bevakning](bevakning.md).

## Egen nod på det lokala nätet

Kör du en egen Esplora på det egna nätet (till exempel `https://esplora.lan`) kan du lägga till den och märka den som
**lokal**. Bara en endpoint som du själv märkt som lokal får ha en adress på datorn eller ett privat nät, och det gäller
fortfarande bara https. Uppslagen till den lämnar aldrig det egna nätet; allt annat går som förut genom utgången.
En egen **Bitcoin Core** kan också läggas till, bara som lokal; se [Egen nod](egen-nod.md).

## Varför det är byggt så

- **En källa ska kunna bytas** utan att något annat ändras (princip 8). Varje förseglat råsvar säger vilken endpoint det
  kom från, så att ett gammalt svar alltid går att spåra.
- **En källa som stänger dig ute** för att du frågat för fort är värre än ett långsamt svar.
- **Steget till egen nod** är bara en annan adress, eftersom samma API går att köra själv.

Se också [Kontrollera mot kedjan](kontroll-mot-kedjan.md), om hur Chainsaw kontrollerar källornas svar.
