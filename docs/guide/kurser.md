# Kurser och omräkning

Kedjan säger hur många bitcoin som flyttades och när blocket bröts, men inget om vad de var värda. Värdet i kronor
kommer från **mellanhänder**, och därför är det två sorters uppgifter i Chainsaw:

- **Kursen** är en uppgift från en namngiven källa vid en tidpunkt. Den sparas i ärendet med källans råa svar, adressen
  som frågades, tiden och SHA-256, precis som det som hämtas om kedjan. En kurs som har sparats hämtas aldrig igen.
- **Omräkningen** är en uträkning som Chainsaw gör. Den visar alltid sina steg, så att vem som helst kan räkna om den
  med en miniräknare.

## Två källor

Ingen börs med lång historik handlar bitcoin mot kronor i någon större mängd. Därför räknar Chainsaw i två steg:

1. **Bitcoin i dollar** (eller euro) ur **Bitstamps** prishistorik. Bitstamp är en av de äldsta börserna och har öppen
   historik sedan 2011, per minut, timme och dag, utan API-nyckel.
2. **Dollar i kronor** ur **Riksbankens** valutakurser (serien `SEKUSDPMI`, eller `SEKEURPMI` för euro). Riksbanken ger
   en kurs per bankdag, öppet och utan avgift.

Räknar man i dollar eller euro behövs bara det första steget.

## Vilken kurs gäller?

En börs prishistorik är uppdelad i **perioder** (ljus): för varje minut, timme eller dag finns första, högsta, lägsta och
sista priset och hur mycket som handlades. Chainsaw tar **sista priset i den period där tidpunkten ligger**, och visar
alltid perioden, så att avståndet till tidpunkten syns.

- **Timme är förvalet.** En blocktid är bara ungefär rätt (den sätts av den som bryter blocket och kan avvika
  ett par timmar från den verkliga tiden), så en minutkurs ger en precision som inte finns.
- **En period som inte är slut** har ingen kurs än. Chainsaw frågar först fem minuter efter periodens slut (datorns
  klocka kan gå före, och börsen kan lägga till de sista affärerna lite sent), och sparar aldrig en pågående period
  som slutlig.
- **Volym noll** betyder att ingen handel skedde under perioden, så priset kommer inte från någon affär i den. Då står
  *Ingen handel under perioden* i stegen.

Riksbankens kurs gäller för en **bankdag** i svensk tid. För en helg eller helgdag används den senaste bankdagen före, och
det står i stegen. Dagens kurs sätts under dagen, så före dess står det att den inte är publicerad än (eller att dagen
kanske inte är en bankdag). Riksbanken tar bara emot några få uppslag i minuten utan nyckel, så Chainsaw frågar en gång
i taget, med tolv sekunders mellanrum, och ett svar täcker en hel månad. En dag räknas som passerad först sex timmar
efter midnatt, så att en kurs som publiceras sent inte missas.

## Så ser stegen ut

```
1 BTC = 30 123,45 USD: Bitstamp, BTC/USD, sista priset för 2023-11-14 22:00–23:00 (UTC).
1 USD = 10,513 SEK: Riksbanken, serien SEKUSDPMI, kursen för 2023-11-14.
1 BTC = 30 123,45 × 10,513 = 316 687,82985 SEK.
0,512 BTC × 316 687,82985 = 162 144,1688832 SEK, avrundat 162 144,17 SEK.
```

(Siffrorna är påhittade.) Uträkningen sker exakt, och bara det som visas avrundas, till två decimaler.

## Utan nät

Kurserna ligger i ärendet. Är ingen utgång ansluten svarar ärendet, och saknas kursen där står det **Offline**; inget
gissas. Uppslagen går, som allt annat, bara genom den valda utgången och innehåller bara marknaden och tiden, aldrig
något om ärendet.
