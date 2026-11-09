# Organisationens återställningsnyckel

För administratören. Så skapas, förvaras och används nyckeln som öppnar ett ärende när operatören har glömt sin
huvudnyckel.

## Vad den gör

Varje ärendefil (`.cut`) har en egen slumpad filnyckel. Filnyckeln ligger inlindad i *kuvert* i filens okrypterade
huvud: ett för operatörens valv, ett valfritt för ett delningslösenord och **ett för organisationens återställningsnyckel**.

Återställningsnyckeln är ett nyckelpar på kurvan P-384. Den **publika** nyckeln är inbyggd i Chainsaw, och med den kan
Chainsaw lägga till kuvertet utan att kunna öppna det. Bara den **privata** nyckeln öppnar kuvertet, och den har bara
administratören.

- Ett nytt ärende får kuvertet direkt.
- Ett äldre ärende utan kuvert får det nästa gång det öppnas, med en rad i journalen.
- Byts nyckeln får varje ärende ett nytt kuvert när det öppnas, och kuvertet för den gamla nyckeln ligger kvar. Ärenden
  som inte har öppnats sedan bytet öppnas bara med den gamla privata nyckeln, så spara den.
- Kan den gamla nyckeln ha röjts: **återkalla** den (se nedan). Då tas dess kuvert bort ur varje ärende som öppnas.
- Är ingen nyckel inbyggd säger Chainsaw det: journalens första rad visar att ärendet skapades utan återställningsnyckel.

Glömmer operatören sin huvudnyckel går **valvet** inte att återställa; det skapas på nytt. **Ärendena** kan däremot
öppnas av administratören och ges en ny väg in.

## Skapa nyckelparet

Gör det en gång, på en dator du litar på, och skriv nyckeln till ett flyttbart medium som inte ligger i något git-repo:

```
dotnet run --project tools/Chainsaw.Recovery -- keygen E:\Chainsaw-nyckel
```

Verktyget frågar efter ett lösenord två gånger (minst 16 tecken) och skriver två filer:

| Fil | Innehåll |
| --- | --- |
| `chainsaw-recovery.private.pem` | Den privata nyckeln, krypterad med lösenordet (PKCS #8, PBKDF2-HMAC-SHA512 med 600 000 iterationer och AES-256) |
| `chainsaw-recovery.public.pem` | Den publika nyckeln |

Verktyget skriver aldrig över en fil som finns, och vägrar att skriva i en mapp som ligger i ett git-repo. Det visar
nyckelns **fingeravtryck** (SHA-256 av den publika nyckeln, sexton grupper om fyra tecken). Skriv upp fingeravtrycket.

## Bygga in den publika nyckeln

1. Ersätt innehållet i `src/Modules/Cases/Chainsaw.Modules.Cases/organization-recovery-key.pem` med den publika
   nyckelns fil (blocket `-----BEGIN PUBLIC KEY-----` till `-----END PUBLIC KEY-----`; text runt blocket får stå kvar).
2. Bygg Chainsaw. En fil som inte innehåller en giltig P-384-nyckel stoppar programmet när det startar, så att inga
   ärenden skapas med fel nyckel.
3. Kontrollera att Chainsaw visar samma fingeravtryck:

   ```
   dotnet run --project tools/Chainsaw.Recovery -- fingerprint
   ```

## Förvara den privata nyckeln

- **Aldrig i repot**, aldrig i molnet och aldrig på operatörernas datorer. `.gitignore` utesluter `*.private.pem`, och
  ett test i lösningen letar efter privata nycklar i repot, men det är en sista spärr, inte en förvaringsplats.
- Förvara den privata nyckeln och lösenordet **åtskilda**, till exempel nyckeln på två USB-minnen i olika kassaskåp
  och lösenordet i ett förseglat kuvert hos en annan person.
- Byt och återkalla nyckeln om den kan ha röjts: skapa ett nytt par, bygg in den nya publika nyckeln och skriv en rad
  `REVOKED <den gamla nyckelns fingeravtryck>` i `organization-recovery-key.pem`, helst med verktyget, som skriver
  raden ur den gamla publika nyckeln så att inget skrivs fel:
  `dotnet run --project tools/Chainsaw.Recovery -- revoke E:\Chainsaw-nyckel\gammal.public.pem --to
  src\Modules\Cases\Chainsaw.Modules.Cases\organization-recovery-key.pem` (utan `--to` skrivs raden bara ut). För hand
  (fingeravtrycket med stora eller små
  bokstäver, med eller utan mellanslag). En rad som nämner ordet `REVOKED` får bara innehålla ordet och ett
  fingeravtryck (mellanslag, bindestreck, kolon, `*`, `•` och backticks går bra, men inte numrerade listor eller `#`),
  annars startar inte Chainsaw, så att en återkallelse aldrig tyst blir utan verkan. Skriv alltså inga kommentarer på
  raden, inte ordet i andra kommentarer, och inget fingeravtryck på en rad utan ordet. Ett fingeravtryck skrivs på en
  rad, inte delat på två. Varje ärende som öppnas förlorar då kuvertet för den gamla nyckeln, med en rad i journalen.
  Kuvert för nycklar som bara är okända tas inte bort, så att en äldre version inte kan ta bort en nyare nyckel,
  utom när huvudet är fullt och kuvertet för den inbyggda nyckeln annars inte får plats (se nedan).
- Kopior av ärendefiler som redan har lämnats ut har kvar kuvertet för den återkallade nyckeln, och en äldre version
  av Chainsaw som inte vet om återkallelsen kan lägga tillbaka det. Det stängs först när ärendets filnyckel kan bytas
  (finns inte än).
- Klistra aldrig in den privata nyckeln i `organization-recovery-key.pem`: Chainsaw vägrar då att starta.

## Kontrollera ett ärende

```
dotnet run --project tools/Chainsaw.Recovery -- check X:\Ärenden\A1.cut
dotnet run --project tools/Chainsaw.Recovery -- check X:\Ärenden\A1.cut --public E:\Chainsaw-nyckel\chainsaw-recovery.public.pem
```

Svaret är *Ja* (slutkod 0) eller *Nej* (slutkod 1) och en lista över de nycklar ärendet har kuvert för. Kontrollen
läser bara filens huvud och kräver ingen nyckel, så ett skadat kuvert med rätt fingeravtryck, eller ett som någon har
skrivit för att linda en annan nyckel, svarar också *Ja*; svaret påminner om det. Den som har den privata nyckeln kan
prova den på riktigt, utan att något i filen ändras:

```
dotnet run --project tools/Chainsaw.Recovery -- check X:\Ärenden\A1.cut --key E:\Chainsaw-nyckel\chainsaw-recovery.private.pem
```

Då öppnas kuverten med nyckeln, och ett kuvert räknas bara när sigillet över hela filen håller under den nyckel det
ger: *Ja … och filen är hel*, *Nej* när inget kuvert öppnas, och ett fel när filen är skadad eller kuvertet lindar en
annan nyckel. Ett sådant kuvert före ett riktigt hindrar inte att det riktiga öppnas. Chainsaw ser själv när kuvertet
för den inbyggda nyckeln saknas eller inte håller (se *Konstruktionen*) och skriver det på nytt när ärendet öppnas, med
en rad i journalen.

## Öppna ett ärende åt en operatör

Stäng ärendet i Chainsaw först. Verktyget öppnar ärendet med den privata nyckeln, skriver en rad i ärendets journal
(*Ärendet öppnades med organisationens återställningsnyckel av administratören …*, med nyckelns fingeravtryck) och ger
ärendet en ny väg in. Välj ett av två sätt:

**Ett delningslösenord** (operatören behöver inte vara på plats):

```
dotnet run --project tools/Chainsaw.Recovery -- open X:\Ärenden\A1.cut --key E:\Chainsaw-nyckel\chainsaw-recovery.private.pem --admin "Förnamn Efternamn" --share
```

Verktyget frågar efter nyckelns lösenord och ett nytt delningslösenord. Operatören öppnar sedan ärendet med
delningslösenordet i Chainsaw, lägger till sitt nya valv och tar bort delningslösenordet.

**Operatörens nya valv** (operatören skriver sin nya huvudnyckel på sin egen dator):

```
dotnet run --project tools/Chainsaw.Recovery -- open X:\Ärenden\A1.cut --key E:\Chainsaw-nyckel\chainsaw-recovery.private.pem --admin "Förnamn Efternamn" --vault %LOCALAPPDATA%\Chainsaw
```

Verktyget frågar efter nyckelns lösenord och operatörens huvudnyckel, och lägger till ett kuvert för operatörens valv.

Lösenord och huvudnycklar frågas alltid efter och tas aldrig från kommandoraden, så att de inte hamnar i historiken.
Verktyget skriver aldrig ut dem eller den privata nyckeln.

## Konstruktionen

Kuvertet är ECIES på P-384: ett tillfälligt nyckelpar per kuvert, ECDH mot den publika nyckeln ger en
gemensam hemlighet, HKDF-SHA384 gör den till en AES-nyckel, och AES-256-GCM krypterar filnyckeln. Ärendets id, nyckelns
fingeravtryck och den tillfälliga publika nyckeln är bundna till kuvertet, så ett kuvert som flyttas till ett annat
ärende eller ändras öppnar ingenting.

**Kuvertets format** (`kind` `recovery`, `alg` `ecies-p384-hkdf-sha384-aes256gcm`) har fälten `fingerprint` (SHA-256
av den publika nyckeln i DER-form, hex), `ephemeral` (den tillfälliga publika nyckeln som DER `SubjectPublicKeyInfo`),
`nonce` (12 byte), `key` (den krypterade filnyckeln, 32 byte) och `tag` (16 byte), alla binära i Base64. Kontexten
`chainsaw/case/envelope/v1`, slaget, algoritmen, ärendets id, fingeravtrycket och `ephemeral` som lagrad är både
HKDF-info och associerad data för AES-GCM. Verktyget öppnar kuvertet med den privata nyckeln och `ephemeral` som den
står, och behöver inte veta hur den tillfälliga nyckeln togs fram.

**Den tillfälliga nyckeln härleds ur filnyckeln** (2026-11-09), så att Chainsaw kan kontrollera kuvertet utan den
privata nyckeln. Den privata skalären *d* är HKDF-SHA384 med filnyckeln som indata och
`chainsaw/case/recovery-ephemeral/v2`, ärendets id, fingeravtrycket och en räknare som info, 48 byte lästa som ett tal;
ligger det inte i 1..n−1 (n är P-384:s ordning) dras ett nytt med nästa räknare. Varje tal som tas är lika sannolikt,
så *d* är likformig på 1..n−1, och ett andra drag händer i praktiken aldrig (n skiljer sig från 2^384 med mindre än
2^−190 av talen). Nonce härleds på samma sätt med `chainsaw/case/recovery-nonce/v2`. Samma ärende, filnyckel och
återställningsnyckel ger alltså alltid samma kuvert, byte för byte, och allt annat (ett annat ärende, en ny filnyckel
efter *Byt filnyckel*, en gren, en annan nyckel) ett orelaterat: AES-nyckeln används bara för just den filnyckeln, så
ingen nonce återanvänds för ett annat meddelande. Den som saknar filnyckeln lär sig ingenting om *d*; den som har
filnyckeln kan redan öppna ärendet.

**Chainsaw kontrollerar kuvertet varje gång ärendet öppnas**: kuvertet för den inbyggda nyckeln görs om ur filnyckeln och
jämförs byte för byte (i konstant tid) med det lagrade. Saknas det, eller håller det inte (skadat, eller skrivet av
någon för att linda en annan nyckel, vilket vem som helst kan eftersom nyckeln är publik), skrivs det rätta, och
journalen säger att kuvertet *saknades* eller *höll inte och skrevs på nytt*. Fler kopior av samma kuvert blir ett.
Huvudet sparas bara ur minnet, och inget efter öppningen ändrar kuvertet, så kontrollen vid öppningen gäller varje
sparning. Är huvudet fullt (64 kuvert) tas först kuvert som inget läser bort (av okänt slag eller format, och
kopior av delningskuvertet eller av det egna valvets kuvert), sedan kuvert för andra återställningsnycklar och sist
valvkuvert som inte är det egna valvets, bakifrån, och journalen säger hur många. Det egna valvets första kuvert och
delningskuvertet (det första av sitt slag, det som öppnas) tas aldrig bort, så kopior av dem kan inte hålla
kuvertet ute; räcker det ändå inte säger journalen att kuvertet saknas.
