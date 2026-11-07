# Att göra

Backloggen: det som inte är byggt, sorterat efter vad det väntar på. **Ta bort en punkt när den är byggd.** Här står allt
som diskuterats, så att inget tappas bort medan vi bygger MVP:n.

## Väntar på beslut

- **Vad av ett annat ärende som följer med en etikett** (CLAUDE.md, *Etiketter smittar*): ärendenumret, utredarens
  anteckning och varför etiketten sattes kan vara sekretess i det andra ärendet. Förslag: ärendenumret och anteckningen
  följer med till biblioteket men inte vidare in i ett annat ärende; där står bara vem, när och etiketten. Kan också
  behöva gå att stänga av per ärende eller per etikett (*Sprid inte*).
- **Hur bevakningen säger till** (den går också när Chainsaw är stängt, CLAUDE.md *Bevaka*): bara en avisering i Windows,
  eller också ljud, e-post eller telefon? Hur ofta källan frågas (var femte minut, varje block) och om en viktig bevakning
  ska kunna frågas oftare.
- **Rapporten:** hur redovisningen byggs (som i Gawk med bilagor, eller enklare underlag för underrättelse). Bestäms när vi
  kommer dit.
- **Spårningsmetod som förval:** FIFO, LIFO, haircut eller poison. Fråga kryptospåraren vilken som används i praktiken.

## Väntar på skiss

- Identiteten: loggan, accentfärgen, låsskärmen och förstagångsinställningen med teckenfältet.
- Grafen (hjärtat): noder för adress, tx och kluster, expandering, slut på spåret, spårat belopp på kanterna, brus-kontroll.
- Skalet: sidopanelen och vyernas ordning.
- Vyerna: Tidslinje, Flöde (Sankey), Tx-inspektör, Adresskort och kluster, Exponering, Bevakning, Etiketter, Terminal, Guide.
- Begäran om uppgifter till börser: formuläret och mallarna.

## Senare (efter MVP:n)

### Kedjor och data
- **Ethereum och ERC-20:** native ETH, interna tx (traces) och tokenöverföringar (Transfer-eventet). Kräver en källa med
  adresshistorik och traces.
- **TRON och USDT (TRC-20).**
- **Fler kedjor** efter behov: Litecoin, BSC, Solana.
- **Privacy coins:** först *Spåret tar slut här*. Senare forskningsmetoderna (till exempel tidsanalys och kända svagheter i
  äldre Monero-tx, Zcash-tx som går in och ut ur den skyddade poolen med samma belopp).
- **Cross-chain automatiskt:** hitta fortsättningen på den andra kedjan själv (belopp och tid hos swap-tjänster, bryggornas
  egna händelser). Kanten finns i modellen från början.
- **Direkt mot kedjan:** egen nod och eget index (Bitcoin Core med `txindex`, Fulcrum eller electrs eller ett eget index;
  Reth eller Erigon för Ethereum; java-tron för TRON). Kräver en server. Esplora går att köra själv, så Bitcoin blir bara en
  annan adress i datalagret.
- **Bevakning i realtid:** websocket där källan har det (mempool.space har bevakning av adresser), senare ZMQ från egen nod.

### Klustring och heuristiker
- **Common-input-ownership:** alla inputs i en tx har samma ägare, utom i CoinJoin.
- **Växeladress:** typ av adress, runda belopp, ny adress, optimal växel, samma skripttyp som inputs.
- **Känna igen mönster:** peel chain, CoinJoin (Wasabi, Whirlpool, JoinMarket), konsolidering, börsens sweep till hot wallet.
- **Tjänster ur beteende:** en börs insättningsadresser, utbetalningar i batch.
- **Mönsterigenkänning** (kanske en lokal modell senare): läggs till som ännu en analys med namn och version.

### Attribution
- **Sanktionslistor:** OFAC SDN (har kryptoadresser), EU:s lista. Hämtas som filer och sparas med hash och datum.
- **Öppna källor:** till exempel ransomwhe.re (öppen datamängd med ransomware-adresser), WalletExplorer (äldre BTC-kluster),
  Chainabuse (kräver nyckel). Varje källa granskas för licens och kvalitet innan den byggs in.
- **Egna etiketter** med vem, när och varför.

### Kurser
- Kursen vid tx:ens tid i vald valuta (SEK som förval), med källa, sparad i ärendet. Riksbankens kurser för SEK mot USD och
  EUR kan vara ett bra underlag; kryptokursen från en källa med historik per minut eller timme. Välj källor och skriv in dem.

### Begäran om uppgifter
- **Mallar** för en begäran till börser, på svenska och engelska, med tx, tider, belopp och insättningsadresser.
- **Börsernas kontaktvägar** för brottsbekämpande myndigheter (portaler och adresser). Varje uppgift ska verifieras och ha en
  källa och ett datum; inga påhittade kontaktvägar.

### Övrigt
- **Länken `chainsaw:`** registrerad för användaren, ett exemplar av Chainsaw som tar emot länkar, och vänta på upplåsning.
- **Guiden:** avsnitten skrivs medan funktionerna byggs (UTXO, adresstyper, växeladress, CoinJoin, peel chain, varje
  heuristik, spårningsmetoderna, varför ett spår tar slut).
- **Testdata:** `/testdata` med påhittade ärenden och syntetiska tx-mönster, bara i Debug.
