# Organisationens återställningsnyckel

> **Som i vardagen:** tänk dig ett hänglås som vem som helst kan knäppa igen, men som bara en person har nyckel till.
> Chainsaw har en låda med öppna hänglås från din organisation. Varje gång ett ärende skapas knäpps ett av dem runt en
> kopia av ärendets nyckel. Chainsaw kan låsa, men aldrig öppna. Bara administratören kan.

Glömmer du huvudnyckeln går valvet inte att öppna, av någon (se [Huvudnyckeln och valvet](huvudnyckeln.md)). Utan en
reserv skulle då också alla dina ärenden vara förlorade. **Återställningsnyckeln** är den reserven.

![Chainsaw har bara det öppna hänglåset; nyckeln har administratören](figur:s-aterstallning)

## Så fungerar det

- Organisationen har ett **nyckelpar**: en publik del (hänglåset) och en privat del (nyckeln).
- Den **publika** delen är inbyggd i Chainsaw. Med den lindar Chainsaw in ärendets filnyckel i ett eget kuvert i varje
  ärendefil (se [Ärendefilen och krypteringen](arendefilen.md)).
- Den **privata** delen har bara administratören, skyddad med ett eget lösenord. Den finns aldrig i Chainsaw, aldrig
  på din dator och aldrig i koden.
- Administratören öppnar ett ärende med ett eget verktyg, och journalen får en rad om att det öppnades med
  återställningsnyckeln, med administratörens namn.

Kuvertet läggs till när ett ärende skapas, och när ett äldre ärende som saknar det öppnas. En nyckel som har röjts kan
**återkallas**; dess kuvert tas då bort ur varje ärende som öppnas.

## Se vilken nyckel som gäller

Under *Inställningar → Om Chainsaw* står återställningsnyckelns **fingeravtryck**, eller *Ingen inbyggd nyckel*.
Fingeravtrycket kan jämföras med det administratören har.

Finns ingen nyckel inbyggd skapas ärendena utan kuvert, och ärendets första rad i journalen säger det. Då finns ingen
reserv.

## Varför det är byggt så

- **Ingen ska behöva lita på att du aldrig glömmer något.** En utredning kan vara viktigare än en enskild persons minne.
- **Ingen enskild dator** ska kunna öppna alla ärenden. Chainsaw kan bara lägga till kuvert, aldrig öppna dem.
- **Det syns** när administratören har öppnat ett ärende, eftersom journalen bara kan fyllas på.

Konstruktionen, för den som vill veta: ECIES på kurvan P-384, med HKDF-SHA384 och AES-256-GCM, och ärendets id bundet
till kuvertet. Hur nyckeln skapas och förvaras står i administratörens instruktion, inte i Guiden.
