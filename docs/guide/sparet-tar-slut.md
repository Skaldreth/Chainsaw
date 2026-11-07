# Varför ett spår tar slut

Ett spår tar aldrig slut tyst. Varje spårad satoshi hamnar i **exakt ett slut**, och slutet har alltid en orsak. Lägg
ihop alla slut, så får du det du började spåra.

## Inte slut än

Två orsaker betyder att pengarna **går att följa vidare**:

- **Inte expanderad ännu.** Nästa steg finns inte i grafen. Antingen vet ärendet inte vart myntet tog vägen, eller så
  vet det vilken tx som spenderar myntet men har inte hämtat den. Expandera noden, så går spåret vidare. Utan nät
  (offline) går det inte just nu, och Chainsaw säger det i stället för att gissa.
- **Ligger kvar (ospenderad).** Myntet var ospenderat när ärendet senast frågade. Pengarna står alltså kvar på
  adressen, vilket är viktigt inför ett beslag. Har frågan inte kunnat ställas om nyligen står det att pengarna kan ha
  flyttats sedan.

## Riktiga slut

- **Börs, uttag funnet.** Pengarna har nått en börs insättningsadress. Här finns oftast den som tar ut pengarna, och
  här kan man begära uppgifter. Chainsaw följer inte pengarna vidare in i börsens egna flyttar (till exempel när börsen
  samlar ihop insättningar), eftersom de då blandas med alla andra kunders pengar.
- **CoinJoin.** Ett känt CoinJoin-mönster (Whirlpool, Wasabi, JoinMarket). Många ägare betalar in tillsammans, så det
  går inte att läsa vems output som är vems. En *möjlig* CoinJoin avslutar inte spåret.
- **Mixer.** En tjänst som blandar kundernas pengar och betalar ut andra mynt. Chainsaw kan senare föreslå troliga
  utgångar ur belopp och tid, men de är alltid kandidater.
- **Fortsätter på annan kedja.** En brygga eller en swap-tjänst. Har du knutit tx:en på den andra kedjan för hand visas
  den som fortsättningen, med en lila kant. Annars står det bara att pengarna lämnade Bitcoin.
- **Privacy coin.** Pengarna gick till till exempel Monero, där de inte går att följa.
- **Okänd tjänst.** Pengarna gick in i en tjänst som flyttar dem internt på sätt som kedjan inte visar.
- **Avgift till minerare.** Den del av avgiften som var spårade pengar.
- **Kan aldrig spenderas.** En OP_RETURN-utgång med ett belopp: pengarna är borta för alla.

## Var kommer orsaken ifrån?

Slutet är en **bedömning**, inte ett faktum, och säger alltid vad det bygger på:

- CoinJoin kommer från klustringens analys (`coinjoin v1`) av just den tx:en.
- Börs, mixer, brygga och sanktionerade adresser kommer från etiketter: öppna källor, egna etiketter och
  etikettbiblioteket (byggs i Attribution).
- *Inte expanderad ännu*, *Ligger kvar*, *Avgift* och *Kan aldrig spenderas* avgör spåret självt ur kedjan.
