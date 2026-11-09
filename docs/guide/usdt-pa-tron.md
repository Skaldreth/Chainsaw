# USDT på TRON

> **Som i vardagen:** USDT är som presentkort i dollar som ett företag har gett ut. Korten går att skicka till vem som
> helst, snabbt och nästan gratis, och de är alltid värda ungefär en dollar. Men företaget som gav ut dem kan spärra ett
> kort om någon ber det, och det har ett register över varje kort.

**USDT** (Tether) är en **stablecoin**: en token som ska vara värd exakt en amerikansk dollar, utgiven av företaget
Tether. Den finns på flera kedjor, och den största delen av den handel som rör brott sker på **TRON**.

## Varför den är den största brottsvalutan

- **Stabil.** En bedragare vill inte att bytet tappar en fjärdedel i värde på en vecka, som bitcoin kan göra.
- **Billig och snabb.** En överföring på TRON kostar några kronor och är klar på sekunder.
- **Överallt.** Nästan alla börser, OTC-handlare och växlare tar USDT på TRON, också de utan kundkontroll.
- **Ser ut som dollar.** Det gör den lätt att använda i bedrägerier som ser ut som investeringar, och lätt att växla
  till kontanter.

## Det som hjälper utredningen

![USDT på TRON: pengarna flyttas som en token, och utgivaren kan frysa det som ligger på en adress](figur:s-usdt)

- **Kontomodellen:** TRON fungerar som Ethereum, med konton och saldon i stället för mynt. Varje överföring av USDT
  syns som en händelse i tokenens kontrakt, med avsändare, mottagare och belopp. Se [Kontomodellen](kontomodellen.md)
  och [ERC-20](erc-20.md), som är samma idé.
- **Kan frysas.** Tether kan frysa USDT på en adress, på begäran. Ligger offrets pengar kvar på en adress kan en snabb
  begäran till Tether hindra att de flyttas. Det är en av få gånger kryptovaluta kan stoppas innan den försvinner. Se
  [Kan frysas](kan-frysas.md).
- **Börserna** som tar emot USDT har oftast kundkontroll, och kan svara på en begäran som andra börser (se
  [Börser och uttag](borser.md)).

## Swappar till och från USDT

Pengar byter ofta form längs vägen: bitcoin växlas till USDT på TRON, eller USDT på Ethereum flyttas till TRON genom en
brygga. Det är ett av de vanligaste sätten att försvåra en spårning. Se [Bryggor och swappar](bryggor-och-swappar.md).

## I Chainsaw

USDT går att spåra på **TRON** och på **Ethereum** (där också USDC): tokenen står som ett litet märke först i beloppet
på linjen (₮), med kronor litet under, och noden får snöflingan *Kan frysas* när USDT ligger kvar. USDT på TRON och
USDT på Ethereum är två olika kontrakt; Chainsaw känner igen båda på kontraktet, aldrig på namnet. Hur TRON fungerar
och var Chainsaw hämtar står i [TRON](tron.md).

