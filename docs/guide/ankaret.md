# Ankaret och flera datorer

> **Som i vardagen:** tänk dig att du skriver upp sidnumret och stämpeln på den sista sidan i pärmen i din egen
> anteckningsbok varje gång du ställer tillbaka den. Nästa gång du tar fram pärmen jämför du. Står det samma är det
> din pärm som du lämnade den. Står det något annat har någon gett dig en annan pärm, eller skrivit i den någon
> annanstans, även om varje sida i den för sig är hel.

## Vad ett ankare är

Hashkedjan i ärendet visar att **inget i filen har ändrats i efterhand** (se [Journalen](journalen.md)). Men en kedja
kan vara hel och ändå vara **en annan** än den du arbetade i: en äldre kopia av filen är också hel, den slutar bara
tidigare.

Därför sparar datorn ett **ankare** för varje ärende: den senaste raden den har sett i varje kedja och dess hash. Ankaret
ligger på datorn, förseglat med valvets nyckel, och inte i ärendefilen, så att den som byter ut filen inte kan byta
ankaret samtidigt.

När ärendet öppnas jämförs filen med ankaret. Stämmer de inte står det i journalen som en rad med koden
`case.anchors-replaced`, med ankaret som det var och filens topp som den är, och ankaret flyttas till filens rad från
och med nu. Det tysta alternativet, att låta ankaret stå kvar, skulle dölja nästa gång något händer.

## Det som lades till på en annan dator

Har en kollega arbetat i ärendet och lämnat tillbaka filen, finns rader efter ankaret som den här datorn aldrig såg
skrivas. Kedjan är hel, men en kollega med filnyckeln kan skriva vad som helst i sina rader, också ditt namn och en
annan tid. Därför har sådana rader en liten nedtonad **dator** i Historik och Tidslinjen, med *Tillagt på en annan
dator* på hovring. Det den här datorn själv skriver märks aldrig. Markeringen sparas med ankaret, så den finns kvar
efter nästa sparning. Första gången ärendet öppnas på en dator märks ingenting, eftersom datorn ännu inte har sett
något att gå i god för, och när ankaret byts (se nedan) börjar markeringarna om.

## Verifiera säger två saker

**Verifiera** i ärendeväljaren (Ctrl+O) och `/verify` svarar först med det viktiga: om **hashkedjan är hel**, hur många
rader den har och dess topp. Ett bytt ankare är **inget fel i filen**. Det står under, som en lugn rad i gult, med när
ankaret byttes och den troligaste orsaken. *Varför?* öppnar förklaringen här, med kedjan som prickar: filens topp i
grönt och ankaret i gult, med rad och hash för båda.

## Vad som kan ha hänt

Chainsaw ser bara filen och ankaret, inte vad som hände däremellan. Orsaken är därför alltid en **bedömning**, aldrig
ett faktum (princip 2), och visas streckad som *Troligast*, med det andra som kan ha hänt under.

### En äldre kopia öppnades

Ankaret står längre fram än filen slutar: datorn har sett rader som inte finns i filen. Filen är troligen en äldre kopia,
till exempel från en säkerhetskopia, ett mejl eller en sticka. Har du en nyare fil, öppna den i stället; raderna efter
filens topp finns inte i den här.

### Ärendet ändrades på två datorer

Filen har en rad på samma plats som ankaret, men med en annan hash: filen har gått åt ett håll och den här datorn åt ett
annat. Båda grenarna är hela var för sig. Den andra finns kvar i den kopia där den gjordes; Chainsaw slår inte ihop dem.
Det kan också vara en äldre kopia som sedan ändrats.

### Ankarfilen saknades eller rullades tillbaka

Datorn hade inget ankare för ärendet, fast ditt valv har ett kuvert i det: ankarfilen var borttagen, ny eller
tillbakalagd från en säkerhetskopia. Det fanns inget att jämföra med, så inget är fel i filen. Från och med nu står
ankaret på filens rad, och nästa öppning jämförs mot det. Det kan också vara första gången ärendet öppnas på datorn.
Är ankarfilen äldre än den valvet skrev senast har den lagts tillbaka från en kopia, och då gäller samma sak.

### Ankaret gick inte att läsa

Ankarfilen var skadad eller ändrad, så den gick inte att lita på. Den ersätts, och filen jämförs mot det nya ankaret
nästa gång.

## Varför det är byggt så

- **Hashkedjan bevisar att filen är orörd, ankaret att det är rätt fil.** Båda behövs: en äldre kopia har en hel kedja.
- **Ett bytt ankare journalförs alltid**, med före och efter, så att det går att förklara långt senare (princip 3).
- **Chainsaw gissar inte tyst.** Orsaken är en bedömning med det andra som kan ha hänt, eftersom bara du vet vilka
  kopior som finns och var ärendet har varit öppet.
