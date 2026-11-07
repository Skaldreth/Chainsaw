# Chainsaw – överlämning

Senast uppdaterad: 2026-11-06.

Läget och nästa steg, kort. Specifikation, arkitektur och arbetssätt står i `CLAUDE.md`, backloggen i `docs/TODO.md`.

## Läget

**Lösningens skelett är byggt** (2026-11-07): `Chainsaw.slnx` med SharedKernel (`IClock`, `Chain`, `Satoshi`), de tio
modulerna (Contracts och implementation, var och en med `AddXxxModule()`), ett testprojekt per modul och `Chainsaw.App`
som bygger värden med alla moduler och visar ett tomt fönster. `dotnet build` utan varningar, 26 tester gröna. Inget
gränssnitt är byggt; skalet väntar på skissen.

## Nästa steg

1. **Skalet väntar på val** (canvas: https://claude.ai/artifact/LVMLEqgJteM68jjc7vuq1h): **SH1a** (sidopanel med ikon
   och ord, som Gawk) eller **SH1b** (smal ikonlist, sök och kommandon i huvudet och som flytande rad). Vyernas ordning
   i båda: Nytt, Sök · Graf, Tidslinje, Flöde, Exponering, Begäran, Bevakning, Etiketter · Terminal, Guide, Inställningar.
   Bestämt sedan tidigare: loggan ID5 B, accenten Magenta `#E070B0`, låsskärmen ID7, bildspråket GR3a och GR3c, grafen GR4.
2. **Temat och skalet byggs** när SH1 är vald: färger, typsnitt, ikonerna, titelraden, sidopanelen och låsskärmen.
3. **MVP** (överenskommen 2026-11-06): Bitcoin via Esplora genom en utgång, ett ärende, mata in en adress, expandera grafen
   för hand, automatisk klustring med förklaring, egna etiketter, export av ett enkelt underlag med hashkedja.
