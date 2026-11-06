# Chainsaw – överlämning

Senast uppdaterad: 2026-11-06.

Läget och nästa steg, kort. Specifikation, arkitektur och arbetssätt står i `CLAUDE.md`, backloggen i `docs/TODO.md`.

## Läget

Inget är byggt. Idén är brainstormad och de första besluten (2026-11-06) står i `CLAUDE.md`. Det som diskuterats men inte
bestämts eller byggts står i `docs/TODO.md`.

## Nästa steg

1. **Skapa Chainsaws canvas** för skisserna och skissa först:
   - **Identiteten:** loggan (Bitcoin-loggan med kedjesågens tänder), accentfärgen och låsskärmen med teckenfältet.
   - **Grafen** i två varianter: hur en adress, en tx och ett kluster ser ut, hur man expanderar, och hur *Spåret tar slut* visas.
   - **Skalet:** sidopanelen och vyernas ordning.
2. **Lösningens skelett** (utan UI): `Chainsaw.slnx`, `Directory.Build.props`, `Directory.Packages.props`, SharedKernel och
   modulerna med tomma Contracts, så att bygget och testerna går igenom från start.
3. **MVP** (överenskommen 2026-11-06): Bitcoin via Esplora genom en utgång, ett ärende, mata in en adress, expandera grafen
   för hand, automatisk klustring med förklaring, egna etiketter, export av ett enkelt underlag med hashkedja.
