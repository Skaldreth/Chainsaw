# Kurser och omräkning

> **Som i vardagen:** Kedjan är som ett kvitto i en främmande valuta utan växelkurs. Hur mycket det var värt i kronor
> får man slå upp i efterhand, för rätt dag och timme, och på en resa växlar man ofta i två steg: först till euro,
> sedan till kronor. Chainsaw gör samma sak och visar varje steg, så att vem som helst kan räkna om det.

Kedjan säger hur många bitcoin som flyttades och när blocket bröts, men inget om vad de var värda. Värdet i kronor
kommer från **mellanhänder**, och därför är det två sorters uppgifter i Chainsaw:

- **Kursen** är en uppgift från en namngiven källa vid en tidpunkt. Den sparas i ärendet med källans råa svar, adressen
  som frågades, tiden och SHA-256, precis som det som hämtas om kedjan. En kurs som har sparats hämtas aldrig igen.
- **Omräkningen** är en uträkning som Chainsaw gör. Den visar alltid sina steg, så att vem som helst kan räkna om den
  med en miniräknare.

## Två källor

Bitstamp har ingen kronkurs, och ingen börs med lång historik handlar bitcoin mot kronor i någon större mängd. Därför
**följer kronan Riksbanken**, och Chainsaw räknar i två steg:

1. **Bitcoin i euro** ur **Bitstamps** prishistorik (`BTC/EUR`; för ether och USDT och USDC samma sak). Bitstamp är en
   av de äldsta börserna och har öppen historik sedan 2011, per minut, timme och dag, utan API-nyckel.
2. **Euro i kronor** ur **Riksbankens** dagskurser (serien `SEKEURPMI`, kronor för en euro). Riksbanken ger en kurs per
   bankdag, öppet och utan avgift, genom sitt öppna API för räntor och valutakurser (SWEA).

Båda svaren sparas i ärendet, var för sig, med källa, tid och SHA-256. Räknar man i euro eller dollar (*Inställningar
› Kurser*) behövs bara det första steget.

## Vilken kurs gäller?

En börs prishistorik är uppdelad i **perioder** (ljus): för varje minut, timme eller dag finns första, högsta, lägsta och
sista priset och hur mycket som handlades. Chainsaw tar **sista priset i den period där tidpunkten ligger**, och visar
alltid perioden, så att avståndet till tidpunkten syns.

![En prishistorik i perioder (ljus): gröna där priset steg, röda där det föll. Tidpunkten (den prickade linjen) ligger i en period (den streckade ramen), och Chainsaw tar det sista priset i den perioden (pricken).](figur:kurser-ljus)

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

Under beloppet på en linje i grafen står värdet i kronor, litet. Ett klick på beloppet öppnar *Varför?* här
bredvid grafen, med stegen för varje överföring på linjen:

- **Beloppet** på kedjan, som är fakta: tx:en och utgången, blockets höjd och hash, och blocktiden i UTC och lokal tid.
- **Kursen BTC → EUR** ur Bitstamp: perioden, att det är periodens sista pris, när och genom vilken utgång den
  hämtades, och SHA-256 av svaret.
- **Växelkursen EUR → SEK** ur Riksbanken: dagen, serien, när den hämtades och SHA-256 av svaret.
- **Uträkningen**, till exempel:

```
0,512 × 28 010,5 × 11,451 = 164 223,10 kr, avrundat
```

(Siffrorna är påhittade.) Uträkningen sker exakt, och bara det som visas avrundas. Med SHA-256 kan svaret i ärendet
kontrolleras: det är samma bytes som källan skickade.

En linje vars kurs inte finns i ärendet visar **– kr** med en liten markering: ett överstruket nät när ingen utgång är
ansluten, en klocka medan kursen hämtas eller när den inte är satt än hos källan (en obekräftad tx har ingen blocktid
än, och ett block vars tid ligger före datorns klocka har ingen kurs än). Hovra över den för att se vilken kurs som saknas.

## Utan nät

Kurserna ligger i ärendet. Är ingen utgång ansluten svarar ärendet, och saknas kursen där står det **– kr** och
**Offline**; inget gissas. Grafen visas alltid direkt med kurserna i ärendet, och de som saknas hämtas i bakgrunden
genom utgången och fylls i när de kommer (under tiden står **– kr** med en klocka).
Inställningarna (*Kurser*) säger hur många kurser ärendet har sparat.

Uppslagen går, som allt annat, bara genom den valda utgången och innehåller bara marknaden och tiden, aldrig
något om ärendet.
