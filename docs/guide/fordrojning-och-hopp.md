# Fördröjning och många hopp

> **Som i vardagen:** den som vill skaka av sig en förföljare byter tunnelbanelinje många gånger och sitter ibland kvar
> på en bänk en timme innan han går vidare. Ingen enskild resa är märklig. Det är bara den som orkar följa hela vägen,
> och vänta lika länge, som ser vart han tog vägen.

**Många hopp** (*layering*) är när pengarna skickas genom en lång rad adresser som bara tar emot och skickar vidare.
**Fördröjning** är när de ligger still länge mellan stegen, dagar eller månader. Båda gör spåret långt och tröttsamt, och
gör att sambandet mellan brottet och uttaget ser svagare ut.

![Fördröjning och många hopp: pengarna väntar dagar mellan stegen, så spåret sträcker sig långt i tiden](figur:s-fordrojning)

## Hur det ser ut

- **Adresser som bara passerar:** pengarna kommer in och går ut nästan i sin helhet, bara minus avgiften, ofta med en
  ny adress varje gång. Varje hopp ser ut som en enkel betalning.
- **Långa uppehåll:** pengarna ligger orörda, ibland tills uppmärksamheten har lagt sig.
- **Blandat med annat:** mellan hoppen kan pengarna gå genom en mixer, en swap eller en uppdelning.

En [peel chain](peel-chain.md) är ett närliggande mönster, där en liten del skalas av vid varje steg.

## Så arbetar du med det i Chainsaw

- **Hoppkolumnerna** visar hur många steg pengarna har gått, en kolumn per hopp (se [Röra sig i grafen](rora-sig-i-grafen.md)).
- **Tidslinjen** visar uppehållen: tunna dygnsband över grafen och ett streck per tx i listen. Välj en tid för att se
  hur långt pengarna hade kommit då (se [Tidslinjen under grafen](tidslinjen.md)).
- **Spårningsmetoden** räknar hur mycket av de spårade pengarna som följer med genom varje hopp, så att du ser att det
  är samma pengar hela vägen.
- **Ligger pengarna still**, bevaka adressen. Bevakningen säger till när de rör sig, också när Chainsaw är stängt (se
  [Bevakning](bevakning.md)).
- **Ett spår som inte är expanderat** är blekt och säger *Inte expanderad ännu*: det är aldrig slut av sig självt.

## Kontrollen Många hopp

Kontrollen `pass-through v2` känner igen **minst tre hopp i rad** där varje tx tar ett mynt och skickar nästan allt
(högst två procent bort, avgiften) vidare till en ny adress. Ett hopp får också betala en **liten avgift till en
tjänst** (högst tre procent), som en tjänst för många hopp tar för varje hopp: till en tjänst med etikett, eller till
samma avgiftsadress i flera hopp. En betalning till en **börs** är ett uttag, aldrig en avgift, och en rad som
klustringen läser som en [peel chain](peel-chain.md) står under peel chain, inte här. En peel chain som betalar små
belopp till olika mottagare räknas inte heller. Mynt under
10 000 satoshi är damm och räknas inte som hopp. Den lägger ett område runt hoppen och säger hur länge
pengarna var på väg. Låg de still **minst tre dygn** mellan två hopp säger titeln *med fördröjning*, och *Varför?* säger
det längsta uppehållet.

Att fälla ihop raden till en enda länk i grafen är **inte byggt än**; det skissas först.
