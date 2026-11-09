# Mönster

> **Som i vardagen:** en kriminaltekniker som har sett tio inbrott med samma sorts brytmärken på samma sorts dörrar
> börjar ana att det är samma tjuv. Märkena bevisar ingenting var för sig, men formen som går igen säger något.

Den som tar pengar från många brottsoffer gör ofta **samma sak varje gång**: samma sorts insamlingsadress, samma väg
genom en mixer till samma börs, samma belopp. Chainsaw letar efter sådana former i ärendets graf och jämför dem med
**liknande fall i samma ärende**.

## Hur Chainsaw läser det

Analysen `pattern v1` läser bara **grafens form**: aktörerna (som klustringen räknar dem, och etiketternas mixers,
börser och tjänster), linjerna mellan dem med belopp och block, och klustringens slutsatser. Den gör inga uppslag och
skickar inget någonstans. Fyra regler, var och en en kontroll i panelen *Förslag* under *Gjort av sig självt*:

- **Samma insamling:** minst **fem olika avsändare** betalar till en aktör som inte spenderar under tiden, och sedan
  sveps **90–105 %** av det insamlade vidare i en tx. En enda sådan aktör är vardag (en plånbok som slår ihop sina
  mynt), så kontrollen säger något först när **minst två delar** av ärendet gör så och sveper till **samma ställe**:
  samma aktör, eller två börser, två mixers.
- **Samma väg:** pengar från **skilda delar** av ärendet (olika aktörer, ingen tx gemensam) går genom samma sorter i
  samma ordning, till exempel *okänd → mixer → börs*. Vägen är högst fyra steg, passerar minst två aktörer med
  etikett och varje steg bär minst halva det förra beloppet. Pengar som samlas och flyttas tillsammans räknas som en
  väg, inte två.
- **Rundgång:** pengar lämnar en aktör och **kommer tillbaka** till den genom minst två andra, högst fem steg, utan att
  passera en börs eller tjänst (en insättning och ett uttag är vardag). Varje steg bär **80–100 %** av det förra, och
  minst 80 % kommer tillbaka.
- **Likadana belopp i följd:** en aktör betalar **minst fyra gånger i rad**, i skilda tx inom **sju dygn**, belopp som
  skiljer högst **1 %** från det första. Det är formen när någon delar upp ett belopp för att hålla sig under en gräns
  (*strukturering*). Gränsen räknas i kronor, som kedjan inte känner, så Chainsaw läser bara att beloppen är lika, och
  säger när någon annan aktör i ärendet delar upp i samma belopp. Börser och andra tjänster räknas inte.

*Varför?* visar exakt vilka aktörer och linjer som liknar varandra och hur, med tx:erna och tiden mellan dem.

## Det är en bedömning

- **Formen kan ha en vardaglig förklaring:** en lönekörning betalar lika belopp, och vänner betalar tillbaka.
- **Aktörerna är klustringens**, och sorterna etiketternas, som också är bedömningar.
- **Mätt mot slumpen:** i 300 slumpade grafer med 1 500 vanliga betalningar var gav reglerna inget larm.

## En öppen dörr

Mönsterigenkänningen är byggd som **en analys bland de andra**, med namn och version. En lokal modell kan läggas till
senare på samma sätt, och läser då samma form av grafen, aldrig mer, och aldrig genom nätet. Chainsaw bygger inte in
någon språkmodell, och ingen analys får själv driva utredningen.
