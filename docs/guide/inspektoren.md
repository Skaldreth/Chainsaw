# Inspektören, ikonlisten och Varför?

> **Som i vardagen:** grafen är kartan, och inspektören är skylten du läser när du stannar vid en plats på den. Skylten
> ändrar inget på kartan; den säger bara vad som finns här och hur vi vet det. Frågar du *varför?* får du hänvisningen
> till källan.

## Ikonlisten vid noden

Välj en nod med ett klick. Ovanför den (eller under, när det är trångt) står en liten list med ikoner, **bara de som går
att använda på just den noden**:

| Ikon | Tangent | Gör |
| --- | --- | --- |
| Expandera | E | Hämtar nodens tx och lägger nästa steg i grafen |
| Etikett | L | Sätter en etikett, se [Etiketter](etiketter.md) |
| Bevaka | B | Lägger till en bevakning, se [Bevakning](bevakning.md) |
| Bryt upp | U | Bara på ett kluster, se [Bryta upp ett kluster](bryta-upp-kluster.md) |
| Annan kedja | K | Knyter fortsättningen, se [Fortsättningen på en annan kedja](annan-kedja.md) |
| Fråga om rapporter | R | Frågar Chainabuse, när den är påslagen, se [Chainabuse](chainabuse.md) |
| Kopiera adress | Ctrl+C | Kopierar adressen |

Etikett, Bryt upp och Annan kedja fälls ut i en liten ruta bredvid noden, med en pil mot den. **Skälet är alltid
frivilligt.** Esc stänger rutan. Ett högerklick utan rörelse öppnar nodens meny med samma val.

## Inspektören

Till höger står **inspektören** så länge något är valt. Den är ren läsning: inget ändras där.

![En vald nod och inspektören bredvid: huvudet med Varför?, fakta och adressen med kopiera](figur:s-inspektoren)

- **Huvudet:** sortens ikon i nodens form, namnet (grått när noden inte har något namn, och då heter den efter adressen)
  och en rad under med sorten, kedjan, antalet adresser i ett kluster och om noden är en startpunkt.
- **Fakta:** in och ut på grafens linjer, antalet tx och det senaste blocket. Siffrorna gäller det som finns i grafen,
  inte adressens hela historik.
- **Saldo per token** på Ethereum: det som ligger kvar per tillgång, med utgivaren och snöflingan *Kan frysas*, se
  [Kan frysas](kan-frysas.md).
- **Adresserna** i en mörk ruta, i fast bredd så att de går att jämföra tecken för tecken, var och en med kopiera.
- **Historik:** nodens händelser i tidsordning, se [Ta tillbaka och ångra](historik.md).

En vald **linje** visar beloppet, tx:erna med sina block och vilken kedja det gäller.

## Varför?

Den runda knappen **?** i huvudet öppnar Guiden i en panel bredvid grafen, med svaret för det valda överst: vilken
heuristik som slog ihop klustret och på vilken tx, var etiketten kom ifrån, varför spåret tar slut här. Under svaret
finns länkar till de andra avsnitten som gäller noden, och *Öppna i Guide* öppnar hela vyn (F1).

## Varför det är byggt så

- **Allt Chainsaw räknar ut är en bedömning** (princip 2), och en bedömning ska alltid kunna förklaras (princip 3).
  Därför har varje slutsats ett *Varför?*.
- **Så lite brus som möjligt** (princip 7). Åtgärderna står där du redan tittar, vid noden, och en knapp som inte går
  att använda visas inte alls.
