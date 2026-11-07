# Chainsaw – överlämning

Senast uppdaterad: 2026-11-06.

Läget och nästa steg, kort. Specifikation, arkitektur och arbetssätt står i `CLAUDE.md`, backloggen i `docs/TODO.md`.

## Läget

Inget är byggt. Idén är brainstormad och de första besluten (2026-11-06) står i `CLAUDE.md`. Det som diskuterats men inte
bestämts eller byggts står i `docs/TODO.md`.

## Nästa steg

1. **Skisserna väntar på användarens val** (canvas: https://claude.ai/artifact/LVMLEqgJteM68jjc7vuq1h):
   - **Identiteten:** loggan är en kätting (blockkedjan som *såg*) och accenten Magenta `#E070B0`. **ID5** väntar på
     val mellan A (två länkar) och B (en länk är ett brett öga). **ID7** visar låsskärmen med kättingen i vänsterdelen.
   - **Grafen:** bildspråket (GR3a) är godkänt, och grafen är en fri yta med växel till hoppkolumner. **GR4** visar dem
     tillsammans och väntar på ett sista ok innan grafen byggs.
   - Därefter: skalet (sidopanelen och vyernas ordning) och resten av vyerna.
2. **Lösningens skelett** (utan UI): `Chainsaw.slnx`, `Directory.Build.props`, `Directory.Packages.props`, SharedKernel och
   modulerna med tomma Contracts, så att bygget och testerna går igenom från start.
3. **MVP** (överenskommen 2026-11-06): Bitcoin via Esplora genom en utgång, ett ärende, mata in en adress, expandera grafen
   för hand, automatisk klustring med förklaring, egna etiketter, export av ett enkelt underlag med hashkedja.
