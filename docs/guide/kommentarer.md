# Kommentarer i grafen

> **Som i vardagen:** en kommentar är en gul lapp som du sätter på kartan med en tråd till platsen den gäller: *kolla
> den här med börsen*, *samma belopp som i förra ärendet*. Lappen säger inget om pengarna; den är din tanke, och den
> sitter kvar där du satte den.

En **kommentarsnod** är en nod som bara är en kommentar: en lapp på ytan i en svag varm ton, med ett vikt hörn. Den är
inte en aktör och påverkar inte spårningen.

![En kommentar: en lapp på ytan med vikt hörn, knuten till sin nod med en tunn prickad tråd, och handtaget i nedre högra hörnet](figur:s-kommentar)

## Skapa en kommentar

- **N** skapar en lapp. Är en nod vald knyts lappen till noden.
- **Dubbelklick** på tom yta skapar en lapp där.
- **Kommentar** i nodens ikonlist, eller raden i högerklicksmenyn, skapar en lapp knuten till noden.

En ny lapp är ett utkast. Den sparas först när du lämnar den med text; en tom lapp försvinner utan spår.

## Skriva

Du skriver **direkt i lappen**, i Markdown: två stjärnor runt ett ord gör det fett och en stjärna kursivt, ett
bindestreck först på raden gör en lista och `#` en rubrik. Medan du skriver syns tecknen dämpade, och när du lämnar
lappen (Esc eller ett klick utanför) ritas den färdig. Foten säger *Markdown · Esc lämnar*. Medan du skriver tar
grafen inga tangenter, så N eller E skriver bara bokstaven.

**Enter** i en lista fortsätter listan (nästa nummer i en numrerad lista), och Enter på en tom punkt avslutar den.
**Tab** drar in en punkt och **Shift+Tab** drar ut den. Mushjulet zoomar grafen också över en lapp som du skriver i.

En länk `chainsaw:bitcoin/<adress>` i lappen väljer och visar noden när du klickar på den; inget slås upp. Andra
webbadresser ritas grå och följs aldrig (princip 4).

## Tråden och handtaget

- En **tunn prickad tråd** knyter lappen till noden den gäller. Dra **pricken** vid trådens ände till en annan nod för
  att knyta om lappen, eller till tom yta för att lösa den. Tråden hittar noden igen också när ett kluster växer.
- Lappen **växer med texten**. **Handtaget** i nedre högra hörnet, som syns vid hovring eller när lappen är vald, drar
  den till en egen storlek.
- Lappen flyttas med vänsterknappen som en nod; platsen och storleken sparas med grafens läge.

## Förseglad med vem och när

Varje kommentar sparas i ärendet med **vem** som skrev den och **när**, och varje ändring blir en ny förseglad post: ny
text, ny tråd eller borttagen. Inget skrivs över. **Delete** tar bort en vald lapp ur grafen, men texten står kvar i
ärendet, och `/verify` visar att ingen kommentar har ändrats i efterhand (se [Journalen och hashkedjan](journalen.md)).

En kommentar är ditt arbete, inte en slutsats. Skriv inget i den som inte ska stå i ärendet; den följer med filen.

Det som inte är byggt än: att lappens historik syns i inspektörens *Historik*, att en borttagen lapp kan läggas
tillbaka, och att slänga lappen i [soptunnan](soptunnan.md).
