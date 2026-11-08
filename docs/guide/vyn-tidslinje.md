# Vyn Tidslinje

> **Som i vardagen:** tänk dig en tidtabell för tåg, med ett spår per station. Varje tåg som går är ett streck från en
> station till nästa, vid den tid det gick. Tidslinjen är samma sak för pengarna: en bana per aktör, och varje tx är
> ett streck mellan två banor.

Vyn **Tidslinje** (Ctrl+2) visar grafens tx i tidsordning. Grafen säger *vart* pengarna gick; tidslinjen säger *när*,
och i vilken takt. Pengar som går vidare inom en timme efter ett bedrägeri säger något annat än pengar som ligger
still i ett halvår.

## Det du ser

- **En bana per aktör**, i den ordning pengarna mest rinner nedåt: först de som skickade, sedan hopp för hopp.
  Banan är prickad, och fast medan aktören är aktiv.
- **En tx är ett lodrätt streck** från avsändarens bana till mottagarens, med en prick där pengarna lämnar och en ring
  där de kommer fram. Strecket är **lika tjockt som beloppet** och har färgen för vad pengarna blev: accent för offrens
  pengar, blått för uttag, gult där de spenderas, rött för risk och slut, lila för en annan kedja och grått för pengar
  som spåret inte följer. **Streckat är osäkert**, som i grafen.
- **Ärendet** är den sista banan: dina egna etiketter, besluten om förslag och bevakningens träffar, som små rutor med
  en prickad tråd upp till aktören de gäller. En bevakningsträff har dessutom ögat med ringar på aktörens bana.
- **Listen längst ner** visar hela ärendet i sin riktiga takt, ett streck per händelse, med en ruta runt det som syns
  ovanför.

## Dygn och timmar

**Dygn** (Ctrl+−) visar hela ärendet, ett dygn i taget. Långa tomma sträckor, till exempel veckorna mellan spåret och
arbetet i ärendet, klipps bort och märks med ett sicksackband och hur många dygn det är.

Tx som ligger så tätt att de skulle hamna på samma ställe **samlas med en siffra**. Ett klick på siffran zoomar till
**timmar** (Ctrl++) runt dem. I timmarna säger brickorna `‹ 3` och `4 ›` hur många händelser som ligger utanför, och
ett klick tar dig tillbaka till dygnen.

Brickorna i huvudet döljer en sort i taget, så att till exempel bara uttag och risk syns. ← och → stegar mellan
händelserna.

## Inspektören

Till höger står den valda händelsen. **Fakta ur kedjan** står för sig: blocktiden (lokal och UTC), blocket, blockhash
och txid, så att varje steg går att kontrollera mot kedjan med vilket verktyg som helst. Det som är en **bedömning**,
färgen efter spårningsmetoden, ett förslag eller en bevakning, står i en streckad ruta med *Varför?*.

Under står **På banan**: allt som hände på aktörens bana, med belopp in (+) och ut (−). *Visa i grafen* (G) öppnar
grafen med tx:en och aktörerna markerade.

Tidslinjen läser bara det som redan finns i ärendet och skriver inget. Dygnen räknas i Windows tidszon.
