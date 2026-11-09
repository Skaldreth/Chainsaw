# TRON

> **Som i vardagen:** TRON är ett bankkonto i samma bank som Ethereum använder för sina system, men med en annan
> kontonummerserie och egna avgifter. Pengarna rör sig på samma sätt, och det är samma sorts kontoutdrag, men ett
> kontonummer i den ena serien är aldrig samma konto som i den andra.

**TRON** är en kedja i **kontomodellen**, som Ethereum: varje adress har ett saldo, och en tx flyttar pengar från ett
konto till ett annat (se [Kontomodellen](kontomodellen.md)). Den egna valutan heter **TRX**, och den viktigaste tokenen
är **USDT** (se [USDT på TRON](usdt-pa-tron.md)). Ett nytt block kommer var tredje sekund.

## Adresserna

En TRON-adress börjar med **T** och har 34 tecken, till exempel `TR7NHq…gjLj6t` (USDT:s kontrakt). Den är skriven i
**Base58Check**: de sista fyra byten är en kontrollsumma, så ett tecken som skrivits fel känns igen och adressen tas
inte emot. Samma adress kan också skrivas i hex, `41` och 40 tecken, som TRON:s eget API gör; Chainsaw läser båda och
visar alltid `T…`-formen.

Bakom formen är adressen 20 byte, precis som en Ethereum-adress. Samma 20 byte på Ethereum och på TRON är ändå **två
olika konton**, och Chainsaw håller dem alltid isär: en etikett, en sanktion eller ett spår på den ena kedjan gäller
aldrig den andra.

## Belopp och avgifter

- **TRX** räknas i **sun**: en TRX är en miljon sun (6 decimaler).
- **USDT** på TRON har också 6 decimaler, och är en **TRC-20**-token, samma standard som ERC-20 på Ethereum. En
  överföring syns som händelsen `Transfer(från, till, belopp)` i tx:ens logg (se [ERC-20 och tokens](erc-20.md)).
- **Avgiften** betalas i TRX för det tx:en använder av nätets **bandbredd** och **energi**. Den som har låst TRX får
  en del gratis, så avgiften kan vara noll. Chainsaw visar den TRX som faktiskt brändes, så som kedjan säger det.
- **TRC-10** är en äldre sorts token på TRON. Den räknas inte, och en sådan överföring sägs i stället för att visas.

## Var Chainsaw hämtar

Källan är **TronGrid**, TRON:s öppna API (se [Källor](kallor.md)). En tx och dess block kan varje TRON-nod svara på, så
en egen nod kan ta över dem senare; en adress historik är TronGrids eget index. Utan nyckel är TronGrid långsam, så
Chainsaw frågar högst en gång i sekunden och väntar när TronGrid ber om det. Allt som hämtas förseglas i ärendet med
blockhöjd och blockets id, och ett block anses slutgiltigt när 20 block har byggts på det (ungefär en minut). Det som
vilar på ett slutgiltigt block hämtas aldrig igen. Blockets id kontrolleras mot höjden, men att tx:en verkligen ligger
i blocket kontrolleras inte (då behövs hela blocket); det gäller också Ethereum.

Historiken bläddras **efter tid**, inte efter block. Listan över en adress USDT säger inte i vilket block en överföring
ligger, så Chainsaw hämtar också varje tx:s egen redovisning (i vilket block, och var i loggen händelsen står) och
förseglar den. Därför kan den första expansionen av en adress med många USDT-överföringar ta en stund.

## I Chainsaw

En TRON-adress eller TRON-tx är en startpunkt som Bitcoin och Ethereum: den hämtas i förväg i *Nytt ärende*, kan
expanderas i grafen, spåras med samma metoder som Ethereum och bevakas. USDT står som ₮ först i beloppet, TRX med sin
symbol, och en adress där USDT ligger kvar får snöflingan *Kan frysas* (se [Kan frysas](kan-frysas.md)).

En **token som Chainsaw inte känner** (bara USDT känns igen på TRON) visas i grafen ur adressens historik, men
bevakningen säger aldrig till för den: vem som helst kan skicka skräptokens till vilken adress som helst, och det är
vanligt på TRON. En bevakning av *alla* tillgångar på TRON gäller TRX och USDT.

**Bevakningen** frågar källan när ett nytt block har kommit, men kedjans höjd läses en gång i minuten, så *vid varje
nytt block* blir på TRON ungefär en gång i minuten: ofta nog för att hinna före ett uttag, utan att fråga 20 gånger i
minuten.
