# Områden i grafen

> **Som i vardagen:** Som när man stryker under med överstrykningspenna på en karta: stadsdelar, ett område man ska
> hålla ögonen på, en egen anteckning. Kartan är densamma, men färgen bakom visar vad som hör ihop.

![Områden ligger svagt bakom aktörerna. Streckad kant är ett förslag (plånboken till vänster), hel kant en kontroll (mixern i mitten), och en yta med en liten person på kanten är din egen bedömning (till höger).](figur:omraden)

Ett **område** är en svag yta bakom aktörer som hör ihop. Det visar var något händer utan att skriva det i grafen:
färgen säger vad, kanten hur säker bedömningen är, och en liten bricka med en ikon sitter på kanten. Namnet visas när
du håller pekaren över brickan eller väljer den.

## Sorterna

Menyn *Områden i grafen* i grafens hörn väljer vilka sorter som ritas:

- **Förvirring**, rött för mixer och CoinJoin och sand för lager på lager (peel chain), ur [kontrollerna och
  förslagen](kontroller-och-forslag.md);
- **Plånböcker**, grått, där klustringen föreslår att adresser har samma ägare men inte slog ihop dem; ett sådant
  förslag kan [godkännas](kontroller-och-forslag.md);
- **Tjänster**, blått, där samma tjänsts etikett sitter på flera aktörer;
- **Annan kedja**, lila, där pengarna fortsätter på en annan kedja genom en länk du gjort;
- **Tid**, prickat utan fyllning, runt aktörer som pengarna passerade inom två timmar;
- **Egna**, de ytor du ritat själv.

## Kanten

- **Streckad** kant: ett förslag, också när det är avgjort, eftersom det är en bedömning.
- **Hel** kant: en kontroll eller en egen yta.
- **Prickad** kant: tid.

En aktör har **högst en fyllning**. Ett område vars aktörer redan ligger i ett annat ritas bara som kant, så att
ytorna aldrig staplas på varandra.

När listan med kontroller är öppen visas bara den valda radens område, och resten av grafen tonas ned.

## Egna ytor

Du kan rita en egen yta runt aktörer som du bedömer hör ihop:

1. välj aktörerna med **Ctrl+klick** (eller välj en aktör);
2. tryck **Z**;
3. skriv ett namn och välj sort och färg, kryssa i *Samma aktör?* om du tror att det är samma person, och skriv en
   anteckning om du vill.

En egen yta har en hel kant och en liten person på brickan: den är **din bedömning**. Den sparas i ärendet som en
etikett, förseglad med vem, när och anteckningen. Klicka på brickan för att se namnet; pennan byter namn och korgen tar
bort ytan, med ett skäl om du vill. Det gamla står kvar förseglat.

Ytan följer adresserna, inte aktörerna: slås två adresser ihop till ett kluster, eller bryts de upp, ritas ytan runt
de aktörer som har dem då.
