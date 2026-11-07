# CoinJoin

En CoinJoin är en tx som **flera personer bygger tillsammans** för att sudda ut spåret. Var och en lägger in en eller
flera inputs och får tillbaka en output med **exakt samma belopp** som de andra. Eftersom alla utgångar ser likadana ut
går det inte att läsa ut vem som fick vilken.

## Vad Chainsaw tittar efter

CoinJoin-analysen (`coinjoin v1`) tittar bara på tx:en själv, utan etiketter:

- **Lika stora outputs** till olika adresser (damm under 5 000 satoshi räknas inte).
- **Inputs från olika håll:** flera olika adresser och flera olika tidigare tx.
- **Kända mönster:**
  - *Whirlpool* (Samourai med efterföljare): exakt fem inputs och fem outputs med en av poolernas belopp (0,001,
    0,01, 0,05 eller 0,5 BTC), alla P2WPKH.
  - *Wasabi*: många deltagare (minst 20 inputs och outputs), bara SegWit-outputs, och minst hälften av utgångarna i
    grupper med samma belopp.
  - *JoinMarket*: minst tre lika stora outputs med ett udda belopp och högst en växel per deltagare.
- **Lika stora jämna belopp** från flera inputs utan känt mönster kan vara en CoinJoin, men lika gärna en börs som
  betalar ut flera uttag på 0,01 BTC, eller någon som delar upp pengar i lika delar. Det blir bara en *möjlig*
  CoinJoin.

## Vad det betyder i utredningen

- **Känd CoinJoin** (Whirlpool, Wasabi eller JoinMarket): spåret tar slut här med orsaken *CoinJoin*, och **inget** i tx:en slås ihop. Common-input-
  ownership skulle annars ha slagit ihop främlingar till en plånbok.
- **Möjlig CoinJoin** (två lika stora outputs, eller lika belopp utan känt mönster): spåret går vidare, men inget i tx:en slås ihop automatiskt; det
  blir förslag som du kan pröva.

Reglerna lutar åt att hellre se en CoinJoin för mycket än en för lite. En missad CoinJoin ger ett falskt stort kluster;
en tx som felaktigt tas för en CoinJoin lämnar bara några adresser ihopslagna för lite.

Att hitta troliga utgångar ur en CoinJoin (belopp, tid, adresstyp) är inte byggt än.
