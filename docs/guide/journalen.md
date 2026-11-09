# Journalen, hashkedjan och /verify

> **Som i vardagen:** tänk dig en pärm med numrerade sidor, där varje ny sida får en stämpel som är räknad ur hela
> sidan före. Ändrar någon en gammal sida stämmer inte stämpeln på nästa sida längre, och inte på någon sida efter den.
> Du behöver inte lita på att ingen har bläddrat i pärmen: du kan räkna efter.

Allt Chainsaw hämtar och allt du beslutar sparas i ärendet med **vem**, **när** (lokal tid och UTC) och **varifrån**.
Det är princip 1, spårbarhet. Två saker håller ordning på det: journalen och hashkedjan.

## Journalen

Journalen är ärendets dagbok. Varje rad har tiden, operatören (namnet ur din profil), en kod och en kort text, till
exempel att ärendet skapades, att en etikett sattes eller togs tillbaka, att en bevakning slog till eller att en
arbetskopia togs tillbaka efter en krasch. Journalen kan bara **fyllas på**: en rad går aldrig att ändra eller ta bort.

Det som hämtas från kedjan, kurserna och svaren från attributionskällor sparas på samma sätt, som **råsvar**: exakt de
bytes som kom, med källa, endpoint, utgångens sort, tider och **SHA-256**.

## Hashkedjan

En **hash** är ett fingeravtryck av en text: SHA-256 räknar fram 64 tecken ur vilken mängd data som helst, och ändras
ett enda tecken i datan blir fingeravtrycket ett helt annat.

Varje förseglad rad får en hash som räknas ur **raden själv och hashen för raden före**. Så hänger raderna ihop som
länkar i en kedja: den sista hashen beror på allt som står före den.

![Varje sida bär stämpeln från den förra](figur:s-parmen)

Ändrar någon en rad i efterhand, till exempel med ett annat verktyg som kan läsa databasen, stämmer inte längre dess
hash, och kedjan bryts just där.

![En sida som ändrats i efterhand: stämpeln stämmer inte, och kedjan bryts där](figur:s-parmen-bruten)

Databasen vägrar dessutom själv att ändra eller ta bort förseglade rader. Chainsaw sparar också kedjornas sista hashar
**utanför ärendet**, i valvets ankare på datorn, så att en äldre kopia av filen eller en kedja som kortats av märks när
ärendet öppnas. Se [Ankaret och flera datorer](ankaret.md).

## Kontrollera: /verify

Skriv `/verify` i kommandoraden (Ctrl+K), eller välj **Verifiera** i ärendeväljaren (Ctrl+O). Chainsaw går då igenom:

- varje sida i databasen;
- ärendets uppgifter mot journalens första rad;
- att skydden mot ändringar finns kvar;
- varje kedja, post för post, och att den slutar där ankaret säger.

Svaret säger i klartext om allt stämmer, eller **vilken kedja som bryts vid vilken post och varför**.

## Varför det är byggt så

- **Varje steg ska gå att förklara** (princip 3). Frågar någon hur en slutsats kom till finns svaret i journalen och
  råsvaren, inte i minnet.
- **Det kostar lite att spara och mycket att återskapa.** En adress kan få nya tx, en källa kan försvinna; råsvaret i
  ärendet visar vad som gällde då.
- **Kedjan är fakta, allt annat bedömningar** (princip 2). Råsvaren är kedjans fakta med blockhöjd och blockhash, så att
  varje steg kan kontrolleras mot kedjan med vilket verktyg som helst. Se
  [Kontrollera mot kedjan](kontroll-mot-kedjan.md).

Det som bara gäller hur grafen ritas (platser, zoom, vad som ligger i en hög) sparas som vanlig data utan rad i
journalen: det är ett arrangemang, ingen slutsats.
