# Chainsaw – överlämning

Senast uppdaterad: 2026-11-06.

Läget och nästa steg, kort. Specifikation, arkitektur och arbetssätt står i `CLAUDE.md`, backloggen i `docs/TODO.md`.

## Läget

- **Skelettet** (2026-11-07): `Chainsaw.slnx` med SharedKernel (`IClock`, `Chain`, `Satoshi`), de tio modulerna och ett
  testprojekt per modul.
- **Etapp 1, skalet SH1b och temat** (2026-11-07, DEV-LOG): titelraden, ikonlisten, tomma sidor per vy, kommandoraden
  längst ner och statusraden. 41 tester gröna. **Inte provat på Windows**, se `docs/TESTER-INFOR-1.0.md`.

## Nästa steg

1. **Prova skalet på Windows** efter `docs/TESTER-INFOR-1.0.md` och skicka skärmdumpar.
2. **Låsskärmen** (ID7: kättingen i tecken och teckenfältet) och förstagångsinställningen med huvudnyckeln.
3. **MVP** (överenskommen 2026-11-06): Bitcoin via Esplora genom en utgång, ett ärende, mata in en adress, expandera grafen
   för hand, automatisk klustring med förklaring, egna etiketter, export av ett enkelt underlag med hashkedja.
