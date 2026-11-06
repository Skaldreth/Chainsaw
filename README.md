# Chainsaw

Chainsaw är ett Windows-verktyg för spårning av kryptovalutor i en utredning. Det används för att

- **spåra** transaktioner och adresser i en graf, med klustring av adresser till plånböcker,
- **känna igen** tjänster som börser, mixers och bryggor, ur öppna källor, egna etiketter och beteende,
- **hitta uttaget**, den punkt där pengarna lämnar kedjan, och ta fram underlag för en begäran om uppgifter,
- och **bevaka** adresser, så att man får veta när pengarna rör sig.

> Chainsaw är i planeringsstadiet. Inget är byggt än.

## Principer

- **Spårbarhet.** Allt som hämtas sparas med källa, tid, blockhöjd och SHA-256, och en hashkedja förseglar journalen.
- **Kedjan är fakta, allt annat är bedömningar.** Kluster, etiketter och spårade belopp visar alltid varför.
- **Sekretess.** Ärendet är en krypterad fil. Uppslag går bara genom en vald VPN-utgång och skickar bara adressen eller tx:en.
- **Offline fungerar** med allt som redan har hämtats.

Den fullständiga specifikationen står i [`CLAUDE.md`](CLAUDE.md).
