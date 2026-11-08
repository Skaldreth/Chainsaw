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

## Det som inte är byggt än

En egen kontroll som av sig själv säger *här delas ett belopp i många likstora delar* eller *här samlas de igen*, och
föreslår att följa summan, är **inte byggd än**. Den kommer som en analys med namn och version, på samma sätt som de
andra [kontrollerna och förslagen](kontroller-och-forslag.md).
