# Egen nod: Bitcoin Core och electrs

> **Som i vardagen:** att fråga en källa på internet är som att ringa ett arkiv och lita på kopian de läser upp. En
> egen nod är att ha ett eget exemplar av hela arkivet på kontoret, som du själv har läst igenom från första sidan. Den
> kan du fråga utan att någon utanför huset hör frågan.

En **nod** är ett program som laddar ner hela blockkedjan och själv kontrollerar varje block och varje tx från det
första blocket. **Bitcoin Core** är den vanliga noden. Den litar inte på någon: ett block utan rätt arbete eller en tx
som spenderar pengar som inte finns tas aldrig emot.

## Vad Chainsaw frågar noden om

Bitcoin Core svarar på frågor om kedjan genom sitt **JSON-RPC**: ett anrop med ett namn och några värden, och ett svar
med `result`. Chainsaw frågar den egna noden om det som är kedjans egna värden:

- kedjans höjd (`getblockcount`) och det nyaste blockets hash (`getbestblockhash`),
- hashen för blocket på en höjd (`getblockhash`),
- ett blocks huvud, 80 bytes (`getblockheader`),
- en tx:s egna bytes (`getrawtransaction`).

Varje svar förseglas i ärendet som det kom, med anropet i svarets `id` (till exempel `getblockhash 840000`), så att det
råa svaret själv säger vad som frågades. Raden säger att det kom från den egna noden.

## Varför adresserna inte frågas där

Bitcoin Core håller inget **index över adresser**: den vet vilka mynt som finns, men inte vilka tx en adress har haft.
Det kräver ett index bredvid noden, till exempel **electrs**, som har samma Esplora-API som mempool.space. Lägg till
electrs som en egen endpoint märkt **Lokal**, så frågas adresserna där och lämnar aldrig det egna nätet. Utan ett eget
index frågas adresserna som förut genom utgången.

Bitcoin Core hittar en gammal tx bara med **`txindex=1`** i `bitcoin.conf`. Utan den säger noden det, Chainsaw säger det
ärligt och frågar de andra källorna; noden används ändå för det den kan svara på.

## Så lägger du till noden

1. Bitcoin Core svarar bara över vanlig http. Chainsaw frågar bara över https, så ställ en **TLS-proxy** framför noden
   på det egna nätet (stunnel, nginx eller Caddy), och lägg till dess adress, till exempel `https://bitcoind.lan/`.
2. Lägg användarnamnet och lösenordet (`rpcauth`, eller innehållet i nodens kakfil `__cookie__:…`) som en nyckel i
   valvet, skrivet `användare:lösenord`. Det skickas som HTTP Basic och hamnar aldrig i ärendet.
3. I *Inställningar → Källor → Bitcoin*: *+ Lägg till endpoint …*, välj **Bitcoin Core (egen nod)**, slå på **Lokal**
   och välj nyckeln. Bitcoin Core kan bara läggas till som lokal, så att lösenordet aldrig går genom utgången.
4. Fäst nodens certifikat med nålen på raden, om proxyn har ett eget certifikat.

## Bekräftad av den egna noden

När Chainsaw kontrollerar att en tx ligger i sitt block räknar den merkleroten själv från tx:en och källans bevis och
jämför med blockets huvud (se [Kontroll mot kedjan](kontroll-mot-kedjan.md)). Huvudet visar bara att blocket bär
*något* arbete. Finns en egen nod frågar Chainsaw den vilket block som ligger på höjden. Har noden samma block är tx:en
**bekräftad av den egna noden**: blocket ligger i den kedja noden själv har kontrollerat. Beviset får ha kommit från en
annan källa, eftersom det kontrolleras mot huvudet och inte tas på ord. Har noden ett annat block på höjden säger
Chainsaw det, och blocket vilar bara på en källas svar.

## Utan utgång

Den egna noden nås direkt på det egna nätet och aldrig genom utgången, så den **provas också när utgången är stängd**.
Dess höjd jämförs då inte med kedjan: en nod som ligger efter skulle se ut att vara i fas med sig själv. Den svarar på
vanliga uppslag, men bevakningen använder den inte förrän ett prov genom utgången har visat att den är i fas.
