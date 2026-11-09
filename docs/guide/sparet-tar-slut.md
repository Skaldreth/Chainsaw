# Varför ett spår tar slut

> **Som i vardagen:** Tänk på paketspårning. Ett paket kan vara *levererat*, *på väg*, *skickat utomlands med en annan
> fraktfirma* eller *lämnat på en terminal där alla paket sorteras om*. Spårningen säger alltid var paketet är och
> varför spåret slutar där. Chainsaw gör likadant med pengarna: varje del slutar någonstans, med en orsak.

Ett spår tar aldrig slut tyst. Varje spårad satoshi hamnar i **exakt ett slut**, och slutet har alltid en orsak. Lägg
ihop alla slut, så får du det du började spåra.

![Offrets pengar från startpunkten (flaggan) delar sig och slutar på olika sätt: i en börs med uttag funnet (blå med bock), i en mixer (röd), på en annan kedja (lila, streckat), hos en aktör som inte är expanderad än (blek) och som avgift till minerare (den tunnaste).](figur:slut-orsaker)

## Inte slut än

Tre orsaker betyder att pengarna **går att följa vidare**:

- **Inte expanderad ännu.** Nästa steg finns inte i grafen. Antingen vet ärendet inte vart myntet tog vägen, eller så
  vet det vilken tx som spenderar myntet men har inte hämtat den. Expandera noden, så går spåret vidare. Utan nät
  (offline) går det inte just nu, och Chainsaw säger det i stället för att gissa.
- **Ligger kvar (ospenderad).** Myntet var ospenderat när ärendet senast frågade. Pengarna står alltså kvar på
  adressen, vilket är viktigt inför ett beslag. Har frågan inte kunnat ställas om nyligen står det att pengarna kan ha
  flyttats sedan.
- **Uppgifterna går inte ihop.** Källan säger att en viss tx spenderar myntet, men tx:en själv säger något annat.
  Chainsaw gissar inte vilken som har rätt och stannar här. Att expandera igen, eller en annan källa, kan reda ut det.
- **Tx:en kanske inte finns.** Bara en endpoint har sagt att den inte känner tx:en, eller källans svar om mynten går
  inte ihop med det. Spåret stannar tills det är bekräftat; se [En tx som inte finns längre](ersatt-tx.md).

På Ethereum finns en fjärde: **Följer med genom en swap.** Pengarna byttes mot en annan tillgång, och det spårade
värdet fortsätter i den nya (se [Spåret genom en swap](swappar.md)). Har du valt att värdet inte följer med är det i
stället ett riktigt slut, **Slutar i swappen**, med en liten person som visar att det är ditt val.

Ett slut som bygger på en obekräftad tx, eller på ett svar som inte kunde frågas om, säger att det **kan ha ändrats**.

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
- **Tjänst.** Pengarna gick in i en känd sorts tjänst där de spenderas eller samlas: spel, handel, presentkort, en
  darknet-marknad eller en garantimarknad. Etiketten säger vilken sort.
- **Okänd tjänst.** Pengarna gick in i en tjänst som flyttar dem internt på sätt som kedjan inte visar.
- **Avgift till minerare.** Den del av avgiften som var spårade pengar.
- **Kan aldrig spenderas.** En OP_RETURN-utgång med ett belopp: pengarna är borta för alla.
- **Tx:en finns inte längre.** Pengarna spenderades in i en tx som källan inte längre känner till, och det som kom
  efter den räknas inte. Rutan vid linjen säger vilken tx som spenderar samma mynt i stället, och du kan välja att
  följa den. Se [En tx som inte finns längre](ersatt-tx.md).

## Var kommer orsaken ifrån?

Slutet är en **bedömning**, inte ett faktum, och säger alltid vad det bygger på:

- CoinJoin kommer från klustringens analys (`coinjoin v1`) av just den tx:en.
- Börs (och andra uttag), tjänst, mixer och brygga kommer från etiketter: egna etiketter, sanktionslistor, öppna
  källor och etikettbiblioteket. Se [Etiketter och varifrån de kommer](etiketter.md). En etikett på ett offer, en sanktionerad ägare
  eller något okänt avslutar inte spåret: pengarna går att följa vidare.
- *Inte expanderad ännu*, *Ligger kvar*, *Uppgifterna går inte ihop*, *Avgift* och *Kan aldrig spenderas* avgör
  spåret självt ur kedjan.
- *Tx:en finns inte längre* kommer från analysen `gone-tx v2`, som läser källans förseglade svar om tx:en (också
  en tx som aldrig låg i ett block, efter regeln `mempool-gone v1`).
