# Uppdelning i många små belopp

> **Som i vardagen:** den som vill sätta in en stor summa kontanter utan att banken frågar delar upp den på många små
> insättningar, på olika kontor och olika dagar. Varje insättning ser oskyldig ut. Det är först när man lägger ihop dem
> som summan syns.

Uppdelning (på engelska ofta *smurfing* eller *structuring*, i grafen *fan-out* och *fan-in*) är när ett stort belopp
delas i **många små** som går olika vägar, för att senare **samlas igen**, ofta på en börs. Syftet är att varje del ska
se ut som en vanlig betalning, hamna under en gräns för kontroll, eller göra grafen så stor att utredaren ger upp.

![Uppdelning i många små belopp, som senare möts igen vid ett uttag](figur:s-uppdelning)

## Hur det ser ut

- **En tx med många utgångar** av ungefär lika stora belopp, ofta jämna i kronor eller dollar.
- **Många adresser som var och en bara tar emot och skickar vidare**, ofta inom kort tid.
- **Delarna möts igen**: flera av dem hamnar på samma börs, eller slås ihop i en tx med många inputs (som då kan
  klustras, se [Common-input-ownership](common-input-ownership.md)).
- Ibland blir delarna **mindre för varje steg**, som en [peel chain](peel-chain.md).

## Så arbetar du med det i Chainsaw

- **Spåra summan, inte varje del.** Spårningsmetoden räknar hur mycket av de spårade pengarna som följer med i varje del,
  så att du ser vilka delar som bär offrets pengar och hur mycket (se [Spårningsmetoder](sparningsmetoder.md)). Linjens
  tjocklek är beloppet.
- **Högen håller grafen läsbar.** Är delarna fler än åtta läggs de i en hög vid adressen, med summan på en tjock linje.
  Plocka ut de delar som går vidare (se [Det som hölls tillbaka, högen och valvyn](det-som-holls-tillbaka.md)).
- **Valvyns filter** hjälper: ett smalt beloppsintervall runt delarnas storlek och ett tidsfönster runt ankomsten tar
  fram just delarna.
- **Leta efter mötesplatsen.** Delar som når samma börs är det viktiga. Varje insättning hör till en begäran (se
  [Börser och uttag](borser.md)).
- **Etikettera** mellanstegen med ett eget namn när du förstått mönstret, så att det syns i grafen och i nästa ärende.

## Kontrollen Uppdelning

Kontrollen `split-merge v2` gör det av sig själv, som en av [kontrollerna](kontroller-och-forslag.md):

- **Uppdelning:** en tx från en eller två adresser med minst **fem utgångar av ungefär samma storlek** (högst tio
  procent skillnad). Växeln, som är större, räknas inte. Området läggs runt delarna.
- **Uppdelning över flera tx:** samma ägare betalar minst **fem olika mottagare** delar av ungefär samma storlek i
  **minst två tx inom ett dygn**, och delarna är det mesta ägaren betalade under tiden. Då ser ingen enskild tx ut som
  en uppdelning, men tillsammans gör de det. En tjänst som betalar många kunder olika belopp räknas inte, och inte
  heller en tjänst med etikett.
- **Delarna samlas igen:** en senare tx som spenderar minst två av delarna, direkt eller efter högst **tolv**
  mellanled som bara tar ett mynt och skickar det vidare. *Varför?* säger hur många av delarna som möts och genom
  vilka hopp, så att du kan följa summan därifrån.

Det är bedömningar: en utbetalning med lika belopp till många kan se ut som en uppdelning, och att delar möts kan också
betyda att de når samma tjänst.
