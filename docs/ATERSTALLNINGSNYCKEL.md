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
- Byt och återkalla nyckeln om den kan ha röjts: skapa ett nytt par, bygg in den nya publika nyckeln och skriv en
  rad `REVOKED <den gamla nyckelns fingeravtryck>` i `organization-recovery-key.pem` (fingeravtrycket med stora
  eller små bokstäver, med eller utan mellanslag). En rad som börjar med ordet men inte har ett giltigt fingeravtryck
  stoppar Chainsaw, så att en återkallelse aldrig tyst blir utan verkan. Varje ärende som öppnas förlorar då kuvertet för den gamla nyckeln, med en rad i journalen.
  Kuvert för nycklar som bara är okända tas aldrig bort, så att en äldre version inte kan ta bort en nyare nyckel.
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
läser bara filens huvud och kräver ingen nyckel.

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

Kuvertet är ECIES på P-384: för varje kuvert skapas ett nytt tillfälligt nyckelpar, ECDH mot den publika nyckeln ger en
gemensam hemlighet, HKDF-SHA384 gör den till en AES-nyckel, och AES-256-GCM krypterar filnyckeln. Ärendets id, nyckelns
fingeravtryck och den tillfälliga publika nyckeln är bundna till kuvertet, så ett kuvert som flyttas till ett annat
ärende eller ändras öppnar ingenting.
