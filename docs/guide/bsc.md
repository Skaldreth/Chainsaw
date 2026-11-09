# BSC (BNB Smart Chain)

> **Som i vardagen:** BSC är en bank som har byggt sina system som Ethereums och till och med använder samma
> kontonummer. Ett kontonummer på BSC ser ut precis som ett på Ethereum, men är ett konto i en annan bank.

**BSC** (BNB Smart Chain) är en kedja i **kontomodellen** som kör Ethereums virtuella maskin (se
[Kontomodellen](kontomodellen.md)). Den egna valutan heter **BNB** och räknas i wei (18 decimaler), som ether. Tokens
följer **BEP-20**, samma standard som ERC-20 (se [ERC-20 och tokens](erc-20.md)). Blocken kommer på mindre än en
sekund.

## Adresserna

En BSC-adress skrivs som en Ethereum-adress, `0x` och 40 hexadecimala tecken, med samma kontrollsumma (EIP-55). Därför
kan Chainsaw inte se på adressen vilken kedja den hör till: utan kedjan räknas `0x…` som Ethereum. Skriv **`bsc:0x…`**
i en startpunkt eller i kommandoraden, eller öppna Chainsaw med länken `chainsaw:bsc/0x…`. En nod på BSC i grafen
bär sin kedja, så det som görs på den (bevaka, etikett, annan kedja) gäller BSC.

Samma 20 byte på Ethereum och på BSC är **två olika konton** i Chainsaw: en etikett, en sanktion eller ett spår på
den ena kedjan gäller aldrig den andra, också när det är samma nyckel som styr båda.

## USDT och USDC på BSC

USDT och USDC på BSC känns igen på sina **kontrakt**, aldrig på symbolen. De ges inte ut av Tether och Circle själva
utan av **Binance** som bryggade tokens (*Binance-Peg*), med 18 decimaler. Därför har de **inte** snöflingan *Kan
frysas*: Chainsaw känner inte till något sätt för utgivaren att frysa dem (se [Kan frysas](kan-frysas.md)). Kursen
tas från Bitstamps marknad för den token de står för, och det står i kursens källa.

## Var Chainsaw hämtar

Källan är ett API av **Blockscouts** sort, som för Ethereum (se [Källor](kallor.md)). Det finns ingen öppen
Blockscout för BSC, och BscScan kräver en betald nyckel, så **BSC har ingen källa från början**: lägg till en egen
Blockscout för BSC, eller en köpt, under *Källor*, med nyckeln i valvet. Utan källa görs inga uppslag, och det sägs
ärligt. Allt som hämtas förseglas med blockhöjd och blockhash under kedjans namn *bsc-mainnet*, och det som ligger 50
block djupt hämtas aldrig igen.

## Vad Chainsaw gör med BSC

- Spåra i grafen som på Ethereum: BNB, interna tx och BEP-20-tokens, med spårningsmetoderna.
- Swappar känns igen på mönstret (den som signerade skickade en tillgång och fick en annan); namnet på en DEX som
  PancakeSwap visas inte än, eftersom dess kontrakt inte är belagda med en källa i Chainsaw.
- OFAC:s BSC-adresser (*BSC*) känns igen som sanktionerade. OFAC skriver USDT och USDC med en adress men utan kedja;
  en sådan `0x`-adress räknas som Ethereums.
- Bevakning, också av bakgrunden, och kurser för BNB från Bitstamps marknad *BNB/EUR*.
