# Utan nät

> **Som i vardagen:** ett ärende i Chainsaw är som en pärm med kopior av allt du hämtat från arkivet. Du kan läsa och
> arbeta i pärmen på tåget, utan att arkivet är öppet. Det enda du inte kan göra är att beställa nya kopior.

Allt som Chainsaw har hämtat ligger i ärendet: tx, adressernas historik, kurserna och svaren från attributionskällor.
Därför går ett ärende att öppna och arbeta med **utan nät**. Det är princip 9.

![Utan nät: det som hämtats ligger i ärendet, och nya uppslag väntar på nätet](figur:s-offline)

## Det som går

- Öppna ärendet, läsa grafen, flytta noder och byta mellan fri yta och hoppkolumner.
- Fråga *Varför?* och läsa inspektören, historiken och journalen.
- Sätta, ta tillbaka och ångra etiketter, bryta upp kluster och knyta en fortsättning på annan kedja.
- Räkna om spårningen med en annan metod (FIFO eller haircut), eftersom den bara räknar på det som redan finns.
- Kontrollera ärendet med `/verify`.
- Läsa tidslinjen och det som hölls tillbaka, så långt det redan är hämtat.

## Det som väntar

Det som kräver ett nytt uppslag går inte, och Chainsaw säger det i stället för att gissa:

- **Expandera** en adress som inte är hämtad.
- **Bevakning:** inga kontroller görs, och grafens markeringar blir grå.
- **Nya kurser:** saknas kursen i ärendet står det *Offline*.
- **Förhandshämtning** i Nytt ärende: raderna säger *Väntar på utgången* och hämtas när den kommer.
- **Chainabuse:** knappen för att fråga om rapporter visas inte.

Grafen visar ordet **Offline** i hörnet, med förklaringen på hovring. Är källorna för en kedja avstängda i
inställningarna står **Av** i stället, eftersom det är ditt val och inte nätet som fattas. Se
[Källorna och deras endpoints](kallor.md).

## När räknas Chainsaw som offline?

Chainsaw gör bara uppslag genom den valda utgången (se [Utgången: Windows eller VPN](utgangen.md)):

- **Med VPN** är Chainsaw offline så länge ingen VPN-utgång är ansluten, också om datorn själv har nät.
- **Med Windows** är Chainsaw online så länge datorn når internet. Det prövas med en https-förfrågan genom datorns
  proxy, inte med ping, som ofta är spärrat i företagsnät. Ett wifi med inloggningssida räknas därför som offline.

## Varför det är byggt så

Det som har hämtats hämtas aldrig igen, om det inte kan ha ändrats (en obekräftad tx eller en adress som kan ha fått
nya tx). Ärendet är källan för allt som redan är känt, och nätet behövs bara för det nya. Det gör Chainsaw snabbt,
skonsamt mot källorna och ärligt om vad det vet.
