# Öppna, byta och stänga ärenden

> **Som i vardagen:** Chainsaw har ett skrivbord med plats för en pärm i taget. De andra står i hyllan, och du kan
> lägga dem i högar som du själv namnger, som *Bedrägerier hösten* eller *Väntar på svar*. Att byta ärende är att
> ställa tillbaka den ena pärmen och ta fram den andra.

Ett ärende i taget är öppet. Numret och titeln står i titelraden, eller *Inget ärende* när inget är öppet.

## Ärendeväljaren

Klicka på ärendet i titelraden, eller tryck **Ctrl+O**. En ruta fälls ut under titelraden med:

- **Öppet nu:** ärendet som är öppet, med **Verifiera** (samma kontroll som `/verify`, se
  [Journalen och hashkedjan](journalen.md)) och **Stäng**.
- **Sök ärendenummer:** markören står där direkt. Skriv en del av numret.
- **De senaste ärendena**, med nummer och när de senast ändrades. Mer visas inte, eftersom resten av ärendet ligger
  krypterat i filen och inte går att läsa utan att öppna den.
- **Nytt ärende … Ctrl+N** längst ner.

Esc stänger rutan. Ett annat ärende som öppnas gör att det som var öppet först sparas och stängs.

## Stänga

**Ctrl+W** sparar och stänger ärendet och visar startvyn. Samma sak gör *Stäng* i ärendeväljaren och `/close` i
kommandoraden. Låser du Chainsaw sparas och stängs det öppna ärendet också (se [Låsning](lasning.md)).

## Startvyn och högarna

Utan öppet ärende står *Nytt ärende* mitt på ytan, med de senaste ärendena som noder runt omkring. Ett klick öppnar ett
ärende.

- **Gör en hög** genom att dra ett ärende och släppa det på ett annat. Högen får ett namn direkt, i högen själv.
- **Fler ärenden** släpps på en hög som finns.
- **Ett klick på en hög** gör samma sak som samlingsnoden i grafen: ytan skickas ut i en stöt, högen hamnar i mitten och
  korten fälls ut runt den medan resten dimmas. Dra ut ett kort för att lägga det fritt på startsidan igen; Shift-klick
  väljer flera.

Högarna sparas **på datorn**, aldrig i ärendena, så en kollega som får en fil ser inte dina högar. Ett ärende som inte
längre finns i mappen lämnar sin hög av sig själv.

Ett ärende som du lyfter kan släppas i **soptunnan** nere till vänster. Det döljs då bara på startsidan på den här
datorn; filen ligger kvar. Se [Soptunnan](soptunnan.md).

## Var ärendena ligger

Ärendena ligger i `%LOCALAPPDATA%\Chainsaw\Ärenden`, en fil per ärende. Inte i Dokument, eftersom den mappen ofta
synkas till molnet (princip 4). Vad filen innehåller står i [Ärendefilen och krypteringen](arendefilen.md).
