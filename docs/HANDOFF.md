# Chainsaw – överlämning

Senast uppdaterad: 2026-11-06.

Läget och nästa steg, kort. Specifikation, arkitektur och arbetssätt står i `CLAUDE.md`, backloggen i `docs/TODO.md`.

## Läget

Inget är byggt. Idén är brainstormad och de första besluten (2026-11-06) står i `CLAUDE.md`. Det som diskuterats men inte
bestämts eller byggts står i `docs/TODO.md`.

## Nästa steg

1. **Identiteten och grafen är bestämda** (canvas: https://claude.ai/artifact/LVMLEqgJteM68jjc7vuq1h): loggan ID5 B
   (kättingen med ögat), accenten Magenta `#E070B0`, låsskärmen ID7 (kättingen står still), bildspråket GR3a och grafen
   GR4. Nästa skiss är **skalet**: sidopanelen, vyernas ordning och titelraden.
2. **Lösningens skelett** (utan UI): `Chainsaw.slnx`, `Directory.Build.props`, `Directory.Packages.props`, SharedKernel och
   modulerna med tomma Contracts, så att bygget och testerna går igenom från start.
3. **MVP** (överenskommen 2026-11-06): Bitcoin via Esplora genom en utgång, ett ärende, mata in en adress, expandera grafen
   för hand, automatisk klustring med förklaring, egna etiketter, export av ett enkelt underlag med hashkedja.
