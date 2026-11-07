# Chainsaw – överlämning

Senast uppdaterad: 2026-11-06.

Läget och nästa steg, kort. Specifikation, arkitektur och arbetssätt står i `CLAUDE.md`, backloggen i `docs/TODO.md`.

## Läget

Inget är byggt. Idén är brainstormad och de första besluten (2026-11-06) står i `CLAUDE.md`. Det som diskuterats men inte
bestämts eller byggts står i `docs/TODO.md`.

## Nästa steg

1. **Skisserna väntar på användarens val** (canvas: https://claude.ai/artifact/LVMLEqgJteM68jjc7vuq1h):
   - **ID1 Identitet:** loggan A Sågklinga (förslag) eller B Kedja, och accentfärgen Stål `#4FBFA8` (förslag), Violett eller Bitcoin.
   - **ID2 Låsskärmen:** teckenfältet med loggan i tecken nere till vänster och en avisering om bevakning utan detaljer.
   - **GR1a Tidsflöde** (adresser i kolumner per hopp) eller **GR1b Aktörer** (kluster och tjänster, tx inom kluster dolda,
     tidslinje under), eller båda med en växel.
   - Därefter: skalet (sidopanelen och vyernas ordning) och resten av vyerna.
2. **Lösningens skelett** (utan UI): `Chainsaw.slnx`, `Directory.Build.props`, `Directory.Packages.props`, SharedKernel och
   modulerna med tomma Contracts, så att bygget och testerna går igenom från start.
3. **MVP** (överenskommen 2026-11-06): Bitcoin via Esplora genom en utgång, ett ärende, mata in en adress, expandera grafen
   för hand, automatisk klustring med förklaring, egna etiketter, export av ett enkelt underlag med hashkedja.
