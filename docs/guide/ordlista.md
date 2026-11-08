# Ordlista

> **Som i vardagen:** varje yrke har sina ord, och de flesta är enklare än de låter. Här står de som kryptospårare
> använder, med en kort förklaring och en länk till avsnittet som berättar mer.

## A–F

- **Adress:** ett nummer som kan ta emot pengar på en kedja, ungefär som ett kontonummer, men vem som helst kan skapa
  hur många som helst gratis. Se [Adresstyper](adresstyper.md).
- **Avgift:** det den som skickar betalar till den som lägger tx:en i ett block. Spårade pengar som går till avgift är ett
  eget slut på spåret.
- **Block:** en sida i kedjans register, med många tx. Ett nytt block kommer ungefär var tionde minut på Bitcoin.
- **Blockhash:** blockets fingeravtryck. Med blockhöjden kan vem som helst slå upp exakt samma block.
- **Blockhöjd:** blockets nummer i ordningen, räknat från det första.
- **Brygga:** ett system som flyttar värde mellan två kedjor. Se [Bryggor och swappar](bryggor-och-swappar.md).
- **Börs:** en tjänst som växlar kryptovaluta mot vanliga pengar, oftast med kundkontroll. Se [Börser och uttag](borser.md).
- **Chainabuse:** en öppen tjänst med rapporter om adresser som använts vid brott. Se [Chainabuse](chainabuse.md).
- **CoinJoin:** en tx där många betalar in tillsammans, så att det inte går att se vems pengar som är vems. Se
  [CoinJoin](coinjoin.md).
- **Damm:** mycket små belopp, ofta skickade för att lura eller förvirra (adressförgiftning).
- **Endpoint:** en källas adress på internet, som Chainsaw frågar om kedjan. Se [Källorna](kallor.md).
- **ERC-20:** standarden för tokens på Ethereum, till exempel USDT och USDC. Se [ERC-20 och tokens](erc-20.md).
- **Etikett:** en bedömning av vad en adress är, med källa. Se [Etiketter](etiketter.md).
- **Etikettbiblioteket:** etiketterna ur alla dina ärenden, på din dator. Se [Etikettbiblioteket](etikettbiblioteket.md).
- **Expandera:** hämta en nods tx och lägga nästa steg i grafen (E).
- **FIFO:** *först in, först ut*, ett sätt att räkna vilka pengar som följer med. Se [Spårningsmetoder](sparningsmetoder.md).

## H–K

- **Haircut:** ett sätt att räkna spårade pengar där varje utgång får sin andel. Se [Spårningsmetoder](sparningsmetoder.md).
- **Hash, SHA-256:** ett fingeravtryck av data; ändras ett tecken blir det ett helt annat. Se
  [Journalen och hashkedjan](journalen.md).
- **Hashkedja:** rader där varje rad bär fingeravtrycket av raden före, så att en ändring i efterhand syns.
- **Heuristik:** en tumregel, till exempel att adresser som betalar tillsammans har samma ägare. Alltid en bedömning.
- **Hopp:** ett steg i spåret, från en aktör till nästa.
- **Hot wallet:** börsens egen plånbok, dit kundernas insättningar samlas.
- **Huvudnyckel:** lösenordet som låser upp valvet. Lagras aldrig. Se [Huvudnyckeln och valvet](huvudnyckeln.md).
- **Insättningsadress:** en adress som en börs ger en enda kund. Den viktigaste punkten i en begäran.
- **Kluster:** adresser som bedöms ha samma ägare, en plånbok. Se [Bryta upp ett kluster](bryta-upp-kluster.md).
- **Kontomodellen:** hur Ethereum och TRON håller pengar, som saldon på konton. Se [Kontomodellen](kontomodellen.md).
- **KYC:** *know your customer*, att en tjänst kontrollerar sina kunders identitet.

## M–S

- **Mempool:** väntrummet för tx som har skickats men inte hamnat i ett block än.
- **Mixer:** en tjänst som blandar kundernas pengar och betalar ut andra. Se
  [Mixerns troliga utgångar](mixerns-kandidater.md).
- **Nästlad tjänst:** en tjänst som har konto hos en större börs. Se [Nästlade tjänster](nastlade-tjanster.md).
- **Obekräftad tx:** en tx som ännu inte ligger i ett block och därför kan ändras eller försvinna.
- **OP_RETURN:** en utgång med data i stället för pengar. Se [Utgångar som aldrig kan spenderas](dolda-utgangar.md).
- **Peel chain:** en kedja av tx där en liten del skalas av i varje steg. Se [Peel chain](peel-chain.md).
- **Privacy coin:** en valuta som döljer vem, till vem och hur mycket. Se [Privacy coins](privacy-coins.md).
- **Råsvar:** exakt det en källa svarade, sparat i ärendet med SHA-256.
- **Satoshi:** den minsta delen av en bitcoin, en hundramiljondel.
- **Stablecoin:** en token som ska vara värd en dollar, till exempel USDT. Se [USDT på TRON](usdt-pa-tron.md).
- **Startpunkt:** där spåret börjar, en adress eller en tx. Se [Nytt ärende och startpunkterna](nytt-arende.md).
- **Swap:** ett byte från en tillgång till en annan, på samma kedja eller mellan kedjor.

## T–Ö

- **Token:** en tillgång som lever i ett kontrakt på en kedja, till exempel USDT på Ethereum.
- **Tx:** en transaktion, en överföring på kedjan. Varje tx har ett id, *txid*.
- **Utgång (nätverk):** vägen Chainsaws uppslag går ut på internet, Windows eller VPN. Se
  [Utgången: Windows eller VPN](utgangen.md).
- **UTXO:** ett ospenderat mynt på Bitcoin, som spenderas helt eller inte alls. Se [UTXO-modellen](utxo-modellen.md).
- **Valv:** Chainsaws krypterade förvaring av nycklar och utgångar. Se [Huvudnyckeln och valvet](huvudnyckeln.md).
- **Växeladress:** adressen dit det som blir över i en betalning går tillbaka. Se [Växeladress](vaxeladress.md).
- **Återställningsnyckel:** organisationens reserv för att öppna ärenden. Se
  [Återställningsnyckeln](aterstallningsnyckeln.md).
- **Ärendelösenord:** ett lösenord som låter en kollega öppna ett ärende. Se [Ärendefilen och krypteringen](arendefilen.md).
