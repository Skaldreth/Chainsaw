# Kontroller och förslag

> **Som i vardagen:** Som stavningskontrollen i ett ordbehandlingsprogram. Den letar själv efter fel medan du skriver
> och stryker under det den hittar, men det är du som bestämmer om ordet ska ändras. Chainsaw letar på samma sätt efter
> knep för att dölja pengar, och lägger fram det som förslag som du godkänner eller avfärdar.

Den som vill gömma var pengarna tar vägen använder **förvirring**: mixers, CoinJoin, peel chains, många små belopp,
byte av kedja och många hopp. Chainsaw letar efter det av sig självt, med **analyser** som körs i bakgrunden på det som
finns i ärendet varje gång grafen läses.

- En **kontroll** har gjort sitt arbete själv, till exempel att en tx är en CoinJoin eller att några hopp är en peel
  chain. Den står under *Gjort av sig självt*.
- Ett **förslag** väntar på dig, till exempel en mixers troliga utgångar. Det står under *Att avgöra* tills du har
  godkänt eller avfärdat det, och sedan under *Avgjort*.

Knappen *Förslag · N* i grafens hörn (**Ctrl+J**) fäller ut listan där inspektören står. N är hur många beslut som
väntar.

## Allt är en bedömning

Varje kontroll och förslag är en **analys med namn och version**, som heuristikerna: `mixer-outputs v1`,
`coinjoin-area v1`, `peel-chain-area v1` och `wallet-proposals v1`. Det den hittar är en bedömning, aldrig ett faktum.
Därför:

- syns förslag **streckade**: ringen i listan, områdets kant och linjerna till kandidaterna;
- **förseglas** varje resultat i ärendet med analysen, versionen och de tx det bygger på, med blockhöjd och blockhash,
  så att varje steg kan kontrolleras mot kedjan med vilket verktyg som helst;
- säger en ny version av en analys sitt eget, bredvid den gamla, som står kvar.

Ett resultat som inte har ändrats sedan det förseglades förseglas inte igen. Journalen får en rad per körning som hittar
något nytt.

## Att gå igenom listan

- **J** och **K** stegar mellan raderna. Den valda raden fälls ut, tonar ned resten av grafen och visar sitt
  [område](omraden.md) svagt bakom aktörerna.
- **Enter** godkänner. Har förslaget kandidater godkänns den valda, först den bästa (*Godkänn bästa*); klicka på en
  annan för att välja den. På en kontroll visar Enter dess aktörer i grafen.
- **X** avfärdar den valda kandidaten, eller förslaget.
- **?** visar *Varför?*: analysens förklaring, varje kandidats skäl och när det förseglades, med länk hit.

Ett **skäl** kan skrivas i fältet under kandidaterna, men det behövs inte.

## Beslutet sparas

Ett godkännande eller ett avfärdande förseglas i ärendet med **vem, när** och skälet om du skrev ett, och en rad i
journalen. Det senaste beslutet om samma kandidat gäller; de tidigare står kvar.

Ett **avfärdat förslag kommer inte tillbaka** så länge analysen har samma version. Kommer en ny version frågar den igen,
eftersom den kan ha räknat på ett annat sätt.

Ett godkänt förslag är fortfarande en bedömning, din. Det står kvar streckat i grafen.

## Läs mer

- [Mixerns troliga utgångar](mixerns-kandidater.md)
- [Områden i grafen](omraden.md)
- [CoinJoin](coinjoin.md) och [Peel chain](peel-chain.md)
