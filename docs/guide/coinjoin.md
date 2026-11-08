# CoinJoin

> **Som i vardagen:** Fem personer lämnar in var sin sedelbunt i ett växelkontor och får tillbaka lika många helt nya
> hundralappar var. Efteråt går det inte att säga vems gamla sedlar som blev vems nya. Eller en garderob på en fest,
> där alla hänger likadana svarta rockar: när alla har gått hem vet ingen vem som tog vilken.

En CoinJoin är en tx som **flera personer bygger tillsammans** för att sudda ut spåret. Var och en lägger in en eller
flera inputs och får tillbaka en output med **exakt samma belopp** som de andra. Eftersom alla utgångar ser likadana ut
går det inte att läsa ut vem som fick vilken.

![Fem deltagare lägger in olika belopp (till vänster) och får tillbaka lika stora outputs (till höger). Offrets pengar (accent) går in, men vilken utgång som är deras går inte att läsa: alla är lika troliga, därför streckade.](figur:coinjoin-lika)

## Vad Chainsaw tittar efter

CoinJoin-analysen (`coinjoin v2`) tittar bara på tx:en själv, utan etiketter:

- **Lika stora outputs** till olika adresser (damm under 5 000 satoshi räknas inte).
- **Inputs från olika håll:** flera olika adresser och flera olika tidigare tx.
- **Kända mönster:**
  - *Whirlpool* (Samourai och efterföljaren Ashigaru): exakt fem inputs och fem outputs med en av poolernas belopp
    (0,001, 0,01, 0,025, 0,05, 0,25 eller 0,5 BTC), alla P2WPKH.
  - *Wasabi 2*: många deltagare (minst 20 inputs och outputs), bara SegWit-outputs, och minst hälften av utgångarna
    med Wasabi 2:s standardbelopp (potenser av 2 och 3, två gånger potenser av 3, och 1, 2 och 5 gånger potenser av
    10, från 5 000 satoshi), med minst tre olika sådana belopp, varav minst två utanför serien 1, 2 och 5 gånger en
    tiopotens. Beloppen behöver inte komma i lika stora grupper. Kravet på potenserna av 2 och 3 gör att en börs
    utbetalning av jämna belopp (0,01, 0,05, 0,1 BTC) aldrig tas för en Wasabi 2-runda.
  - *Wasabi* (den första versionen): lika stor, och minst hälften av utgångarna i grupper med samma belopp.
  - *JoinMarket*: minst tre lika stora outputs med ett udda belopp och högst en växel per deltagare.
- **Lika stora jämna belopp** från flera inputs utan känt mönster kan vara en CoinJoin, men lika gärna en börs som
  betalar ut flera uttag på 0,01 BTC, eller någon som delar upp pengar i lika delar. Det blir bara en *möjlig*
  CoinJoin.

## Vad det betyder i utredningen

- **Känd CoinJoin** (Whirlpool, Wasabi eller JoinMarket):
  spåret tar slut här med orsaken *CoinJoin*, och **inget** i tx:en slås ihop.
  Common-input-ownership skulle annars ha slagit ihop främlingar till en plånbok.
- **Möjlig CoinJoin** (två lika stora outputs, eller lika belopp utan känt mönster):
  spåret går vidare, men inget i tx:en slås ihop automatiskt; det
  blir förslag som du kan pröva.

Reglerna lutar åt att hellre se en CoinJoin för mycket än en för lite. En missad CoinJoin ger ett falskt stort kluster;
en tx som felaktigt tas för en CoinJoin lämnar bara några adresser ihopslagna för lite.

## Mixade mynt som slås ihop igen

En CoinJoin skyddar bara så länge mynten hålls isär. Spenderar någon **två eller fler utgångar ur CoinJoin** i samma
tx, eller en mixad utgång tillsammans med **växeln ur samma runda** (*toxic change*, som pekar på den som betalade in),
visar det att mynten troligen har samma ägare, och mixen döljer dem inte längre. Kontrollen `coinjoin-merge v1`
markerar en sådan tx med ett rött område och säger det i *Varför?*. Spåret kan gå vidare där. Det är en bedömning: en
tjänst som tar emot mynt från flera kunder kan också spendera dem tillsammans.

Att hitta troliga utgångar ur en CoinJoin (belopp, tid, adresstyp) är inte byggt än.
