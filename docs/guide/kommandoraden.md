# Kommandoraden och länkar

> **Som i vardagen:** kommandoraden är som sökfältet i telefonen som du drar fram och som försvinner när du är klar.
> Den finns inte i vägen när du inte behöver den, men är alltid ett tangenttryck bort.

## Kommandoraden

Tryck **Ctrl+K** (eller **Ctrl+F**). En rad fälls fram högt upp i fönstret över en mjuk mörkning, med förslag under.
Esc fäller bort den. Chainsaw har ingen egen sökvy och ingen statusrad: sökning och kommandon finns bara här.

![Kommandoraden fälls fram med Ctrl+K, med förslag under](figur:s-kommandoraden)

Det du skriver är antingen ett **kommando** som börjar med `/`, eller en **startpunkt**:

| Skriv | Det händer |
| --- | --- |
| `/new 2026-0117` | Nytt ärende, med numret ifyllt (samma som Ctrl+N) |
| `/open 2026-0117` | Öppnar ärendet; det som var öppet sparas och stängs först |
| `/close` | Sparar och stänger ärendet (samma som Ctrl+W) |
| `/verify` | Kontrollerar ärendet, se [Journalen och hashkedjan](journalen.md) |
| `/lock` | Låser Chainsaw (samma som Ctrl+L) |
| `/help` | Visar kommandona |
| en adress eller tx | Läggs som startpunkt i det öppna ärendet, eller fyller i Nytt ärende när inget är öppet |

**Tab** fyller i: `/op` blir `/open`, och efter `/open` föreslås det senast sparade ärendet. Ett svar visas under raden,
eller som en toast nere till höger när raden är dold.

Hemligheter skrivs aldrig i kommandoraden: huvudnyckeln och ärendelösenord har egna fält som aldrig sparas som text.

## Öppna Chainsaw med en länk

Andra program kan skicka en adress eller en tx till Chainsaw med en länk:

```
chainsaw:bitcoin/bc1q…
```

Formen är `chainsaw:<kedja>/<adress eller tx>`. Samma värde kan ges som argument till `Chainsaw.exe`.

- **Chainsaw är redan igång:** länken tas emot där. Det finns bara ett Chainsaw per Windows-användare.
- **Ett ärende är öppet:** värdet läggs i kommandoraden med *Från en länk: Enter lägger den som startpunkt i …*.
  Ingenting händer förrän du trycker Enter.
- **Inget ärende är öppet:** Nytt ärende fylls i med värdet.
- **Chainsaw är låst:** länken väntar tyst tills du låst upp. Bara den senaste länken väntar.

En länk skriver aldrig något i ett ärende av sig själv. Ett annat program ska inte kunna lägga startpunkter i din
utredning utan att du har sett dem.

Att registrera länken i Windows (sidan *Länkar* i inställningarna) är **inte byggt än**. Tills dess fungerar värdet som
argument till `Chainsaw.exe`.

Se också [Kortkommandon](kortkommandon.md).
