# Mönster

> **Som i vardagen:** en kriminaltekniker som har sett tio inbrott med samma sorts brytmärken på samma sorts dörrar
> börjar ana att det är samma tjuv. Märkena bevisar ingenting var för sig, men formen som går igen säger något.

Den som tar pengar från många brottsoffer gör ofta **samma sak varje gång**: samma sorts insamlingsadress, samma väg
genom en mixer till samma börs, samma belopp. Chainsaw letar efter sådana former i ärendets graf och jämför dem med
**liknande fall i samma ärende**.

## Hur Chainsaw läser det

Analysen `pattern v1` läser bara **grafens form**: aktörerna (som klustringen räknar dem, och etiketternas mixers,
börser och tjänster), linjerna mellan dem med belopp, block och **mynten** de spenderar och betalar, och klustringens
slutsatser. Den gör inga uppslag och skickar inget någonstans. En tx med flera betalare (till exempel en CoinJoin) är
en egen aktör i mitten, eftersom ingen vet vems pengar som gick vart. Fyra regler, var och en en kontroll i panelen
*Förslag* under *Gjort av sig självt*, och alla läser bara **bland de hämtade tx:erna**:

- **Samma insamling:** minst **fem olika avsändare** betalar till en aktör som inte spenderar under tiden, och sedan
  sveps **90–105 %** av det insamlade vidare i en tx som spenderar deras mynt. En enda sådan aktör är vardag (en
  plånbok som slår ihop sina mynt), så kontrollen säger något först när **minst två aktörer** sveper till **samma
  aktör** som inget är känt om, till exempel samma insättningsadress. En börs eller tjänst med etikett som mottagare
  säger inget, eftersom den har många kunder, och en insättning som bara förs vidare från en annan adress i grafen
  (en börs insättningsadress som töms till sin hot wallet) räknas inte som en avsändare. Samma form har en handlare
  som tömmer sina kassor till sin kalla plånbok, och *Varför?* säger det.
- **Knutpunkter:** en aktör med minst tio motparter är ofta en tjänst utan etikett, och reglerna går inte igenom den.
  Offer, startpunkter och adresser som bara samlar in och tömmer till aktören räknas inte som motparter, så ett stort
  bedrägeri med många offer och många insamlingsadresser blir ingen knutpunkt. Det som räknas är aktörer med etikett
  och de aktören själv betalar: en börs betalar ut till många kunder, en bedragares insamling sveper till få.
- **Samma väg:** mynt från **skilda delar** av ärendet (olika aktörer, ingen tx gemensam) förs vidare på samma sätt
  till **samma adress hos en aktör med etikett** (samma insättningsadress, aldrig bara samma börs, som har många
  kunder): lika många steg, minst tre, där varje steg **spenderar mynt ur det förra**, bär
  minst halva beloppet och kommer inom 30 dygn. Vägen följs bara fram till den **första** aktören med etikett, aldrig
  igenom en mixer eller börs, eftersom det som kommer ut där är andra mynt. Aktörerna på vägen är sådana som inget är
  känt om och inga knutpunkter (aktörer med minst tio motparter, som ofta är tjänster utan etikett).
- **Rundgång:** mynt lämnar en aktör och **kommer tillbaka** till den genom minst två andra, högst fem steg inom 30
  dygn, där varje steg spenderar mynt ur det förra och bär **80–100 %** av det, och minst 80 % kommer tillbaka. Vägen
  går bara genom aktörer som inget är känt om och inga knutpunkter (en insättning på en börs och ett uttag är vardag),
  och minst en av dem gör mer än att föra pengarna vidare. Det är en avvägning: mynt som bara går runt mellan
  ägarens egna adresser är oftast vardag (en plånbok som flyttar sina mynt), så de räknas inte, men därmed missas
  också en rundgång där alla mellanhänder är tomma adresser som bara för pengarna vidare.
- **Likadana belopp i följd:** en aktör betalar **samma mottagare minst fyra gånger i rad**, i skilda tx inom **sju
  dygn**, belopp som skiljer högst **1 %** från det första. Det är formen när någon delar upp ett belopp för att hålla
  sig under en gräns (*strukturering*). **Jämna belopp** (högst två siffror, som 0,01, 0,25 eller 1 BTC) är vardag och
  räknas inte, och en följd är högst 50 betalningar. Gränsen räknas i kronor, som kedjan inte känner, så Chainsaw läser
  bara att beloppen är lika, och nämner en annan aktör bara när den betalar samma mottagare samma belopp. Betalningarna
  måste också **samlas**: mottagaren är en börs eller tjänst (en insättning), eller spenderar minst fyra av mynten
  tillsammans, så att en prenumeration som betalas varje dag inte räknas. Knutpunkter, börser och andra tjänster som
  betalare räknas inte.

*Varför?* visar exakt vilka aktörer och linjer som liknar varandra och hur, med tx:erna och tiden mellan dem.

## Det är en bedömning

- **Formen kan ha en vardaglig förklaring:** en lönekörning betalar lika belopp, och vänner betalar tillbaka.
- **Aktörerna är klustringens**, och sorterna etiketternas, som också är bedömningar.
- **Startpunkterna och offren räknas aldrig** som en del av ett mönsters affärer. Det skyddar offren, men om
  startpunkten är den misstänktes egen plånbok kan ett mönster som går genom den missas.
- **Det som inte är hämtat syns inte:** en följd eller en väg kan fortsätta utanför grafen.
- **En betalningsförmedlare** (en tjänst som tar emot betalningar åt många butiker och betalar ut till dem) har samma
  form som en insamling och som samma väg: många betalar in, och pengarna förs vidare i större belopp. Saknar den
  etikett kan den ge en träff; sätt en etikett på den, så räknas den som en tjänst.
- **En börs utan etikett** vars insättningsadresser töms till dess plånbok liknar en insamling. När mottagaren bara
  för vidare i större, samlade belopp är Chainsaw därför tyst, **utom** när en startpunkt eller ett offer finns bland
  avsändarna: en bedragares plånbok som tar ut allt i en tx har samma form. Då står träffen kvar, och *Varför?* säger
  att det också kan vara en börs utan etikett.
- **Mulor räknas bara i *Samma väg*:** en mottagare som bara för vidare aktörens egna mynt gör inte aktören till en
  knutpunkt när vägarna läses, så att en bedragare som betalar ut genom många mulor syns. I insamlingen räknas alla
  aktören betalar, eftersom en börs kunder som spenderar sina uttag vidare ser ut som mulor.
- **En träff kan komma och gå när grafen växer:** reglerna läser bara det som är hämtat. Expanderar du en insamlings
  plånbok kan det visa sig att den för vidare i stora belopp (och träffen försvinner, om inget offer är märkt), eller
  att den betalar ut till många (och blir en knutpunkt). Märk offren och startpunkterna, så står en träff som bygger
  på dem kvar.
- **Likadana belopp och samma insamling kan gälla samma pengar:** en bedragare som delar upp insamlade pengar i lika
  delar till en börs ger båda träffarna; de beskriver olika sidor av samma flöde.
- **En mixer utan etikett** är en spricka: Chainsaw vet inte att mynten byts där, så samma väg och rundgången kan följa
  mynten genom den som om de vore samma pengar, så länge den inte har så många motparter att den blir en knutpunkt.
  Sätt en etikett på en mixer så snart den känns igen; då slutar vägen där.
- **Mätt mot slumpen:** i 300 slumpade grafer med 1 500 betalningar var, där mynten följer varandra, gav reglerna
  inget larm, och de vardagliga formerna (jämna belopp, utbetalningar i batch, en börs hot wallet, CoinJoin, en tjänst
  med många utbetalningar, butiker som sveper till börser, insättning och uttag på en börs utan etikett) ger inget.

## En öppen dörr

Mönsterigenkänningen är byggd som **en analys bland de andra**, med namn och version. En lokal modell kan läggas till
senare på samma sätt, och läser då samma form av grafen, aldrig mer, och aldrig genom nätet. Chainsaw bygger inte in
någon språkmodell, och ingen analys får själv driva utredningen.
