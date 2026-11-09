# Solana

> **Som i vardagen:** På Solana har varje person ett konto för kronor, men aktier ligger i separata depåer som var
> och en är knuten till en ägare. Pengarna i depån är ägarens, även om depån har ett eget nummer. Chainsaw räknar
> därför tokens hos **ägaren**, inte hos depån.

**Solana** är en kedja i **kontomodellen** (se [Kontomodellen](kontomodellen.md)). Den egna valutan heter **SOL** och
räknas i **lamports** (9 decimaler). Ett block kallas en **slot** och kommer ungefär var 400:e millisekund; en slot
kan stå tom (*hoppas över*), och då finns inget block i den.

## Adresser och signaturer

En Solana-adress är en publik nyckel på **32 byte i Base58** (32 till 44 tecken, utan `0`, `O`, `I` och `l`). Den har
**ingen kontrollsumma**: ett felskrivet tecken ger en annan giltig adress som ingen äger. Kopiera därför alltid
adressen, skriv den aldrig av. En tx heter efter sin första **signatur**, 64 byte i Base58 (upp till 88 tecken). Stora
och små bokstäver betyder olika saker i Base58, så Chainsaw sparar dem precis som de skrivs.

Skriv `solana:` eller `sol:` före ett värde, eller öppna Chainsaw med `chainsaw:solana/…`. En adress eller signatur
känns också igen utan kedjan, eftersom ingen annan kedjas värden har de längderna.

## Tokenkonton

En token (SPL) ligger inte på ägarens adress utan i ett **tokenkonto** med en egen adress, oftast ett *associated
token account*, ett per ägare och token. Tokenkontots saldo säger vem som **äger** det. Chainsaw räknar en
tokenrörelse mellan ägarna, så att pengarna följer personen och inte depån.

Det här påverkar historiken: en överföring till ett tokenkonto som redan finns nämner inte ägarens adress. Chainsaw
läser därför också ägarens tokenkonton för de tokens den känner (USDT och USDC) och deras historik. Ett tokenkonto som
har **stängts** listas inte längre; dess rörelser hittas då bara i tx:er som ägaren själv signerade.

## Vad som flyttades

En Solana-tx kan köra många **instruktioner** och **inre instruktioner** (program som anropar andra program). Chainsaw
läser de överföringar som System- och Token-programmen gör, i den ordning de kördes, också de inre: SOL som skickas,
tokens som flyttas, skapas (*mint*) eller förstörs (*burn*), och hyran som ett stängt tokenkonto lämnar ifrån sig. Det
är fakta, en överföring för varje instruktion, så en mellanhand mitt i en tx (A till M till E) syns som två steg.

**Saldona före och efter** (`preBalances`/`postBalances` och `preTokenBalances`/`postTokenBalances`) är kontrollen:
Chainsaw spelar upp instruktionerna på saldona före och jämför med saldona efter. Går det inte ihop har ett program
flyttat värde utan en instruktion som noden tolkar; då visas de överföringar som instruktionerna anger, det som fattas
gissas inte fram, och tx:en sägs, i grafen och i bevakningen.

Ett **tokenkonto** och dess lamports räknas som **ägarens**, så hyran som läggs i ett nytt tokenkonto är en överföring
till ägaren, och ett tillfälligt konto som skapas och stängs i samma tx blir ingen egen aktör. **Wrapped SOL** är SOL i
ett tokenkonto och räknas som SOL; därför syns en swap mot en pool som SOL från poolens ägare och token till samma
ägare.

## USDT och USDC på Solana

USDT och USDC känns igen på sina **mint-adresser**, aldrig på symbolen. Båda har en **freeze authority** i sitt
mint-konto (Tether respektive Circle), som Chainsaw har läst ur kedjan, och får därför snöflingan **Kan frysas** (se
[Kan frysas](kan-frysas.md)). Ett tokenkonto som redan är fryst sägs i saldot.

## Var Chainsaw hämtar

Källan är Solanas **JSON-RPC**, som varje Solana-nod svarar på. Förvalet är den publika noden
`api.mainnet-beta.solana.com`, som är öppen utan nyckel men begränsar takten (tio `getTransaction` per tio sekunder),
så Chainsaw frågar lite långsammare än en gång i sekunden. Lägg till en egen nod eller en köpt under *Källor* (se
[Källor](kallor.md)).

Allt frågas med *commitment* **finalized**: noden svarar bara med det som en övervägande del av insatsen har låst och
som inte kan rullas tillbaka. Därför hämtas en tx, ett block eller en äldre sida aldrig igen. Varje svar förseglas
under kedjans namn *solana-mainnet* med sloten, och blockets hash hämtas för varje tx som öppnas.

En sida i historiken kostar ett uppslag per tx på sidan, eftersom signaturlistan inte säger vad som flyttades. Sidorna
är därför små (25 tx).

## Vad Chainsaw gör med Solana

- Spåra i grafen: SOL och SPL-tokens, med spårningsmetoderna.
- Swappar känns igen på mönstret (den som signerade skickade en tillgång och fick en annan); namnet på en DEX som
  Jupiter eller Raydium visas inte än, eftersom deras program inte är belagda med en källa i Chainsaw. Priset prövas
  för SOL, USDT och USDC.
- OFAC:s Solana-adresser (*SOL*, och USDT eller USDC skrivna med en Solana-adress) känns igen som sanktionerade.
- Bevakning, också av bakgrunden. *Vid varje nytt block* betyder på Solana varje gång kedjans höjd läses (en gång i
  minuten), eftersom en slot kommer flera gånger i sekunden.
- Kurser för SOL från Bitstamps marknad *SOL/EUR*, som har historik från 2022.
