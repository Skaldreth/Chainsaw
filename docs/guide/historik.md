# Ta tillbaka och ångra

> **Som i vardagen:** i ett protokoll suddar man aldrig. Har man skrivit fel drar man ett streck över raden och skriver
> den rätta under, med datum och signatur. Den som läser efteråt ser både vad man först trodde och när man ändrade sig.

Ingenting i ett ärende tas bort. När du ändrar dig om något du själv har gjort sparas ändringen som en **ny förseglad
post**, och det gamla står kvar **överstruket**. Det är princip 1: journalen kan bara fyllas på.

## Historiken

Välj en nod och öppna fliken **Historik** i inspektören. Där står nodens händelser i tidsordning på en tunn linje:

- **Först in:** blocket där adressen först fick pengar, ett faktum ur kedjan.
- **Klustrad:** vilken heuristik som slog ihop adressen med andra. Det automatiska har en streckad ram.
- **Etiketter**, **uppbrytningar**, **länkar till annan kedja** och **bevakningar**, med vem och när.

![Nodens historik: det egna kan tas tillbaka, och det gamla står kvar överstruket](figur:s-historik)

## Det du kan ta tillbaka eller ångra

Det du själv har sparat har en knapp under raden:

- **Ta tillbaka** en etikett. Den tas också tillbaka i etikettbiblioteket, så att den inte smittar vidare till andra
  ärenden (se [Etikettbiblioteket](etikettbiblioteket.md)). Vem som helst kan ta tillbaka en egen etikett i ärendet,
  också en kollegas.
- **Ta tillbaka** en länk till en annan kedja.
- **Ångra** en uppbrytning av ett kluster. Då kan klustringen slå ihop adresserna igen, och därför visar Chainsaw
  först **vilka adresser som slås ihop och med vilken heuristik**. Knappen går att trycka först när listan har visats.

**Skälet är frivilligt.** Enter sparar, Esc avbryter. Tillbakatagandet blir en egen rad med vem och när, och den gamla
raden står kvar överstruken och tonad.

Det Chainsaw har gjort av sig självt, till exempel en sammanslagning från en heuristik, tas inte tillbaka här. En
sammanslagning du inte tror på bryter du upp, se [Bryta upp ett kluster](bryta-upp-kluster.md).

## Varför det är byggt så

En utredning ändrar sig hela tiden, och det är bra. Men den som läser ärendet efteråt ska kunna se **vad man trodde,
när, och varför man ändrade sig**. Därför ersätts ingenting, och `/verify` kan visa att inget har ändrats i efterhand
(se [Journalen och hashkedjan](journalen.md)).
