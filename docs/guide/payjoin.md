# PayJoin

> **Som i vardagen:** du betalar en vara med en hundralapp, och säljaren lägger samtidigt en egen femtiolapp i samma
> kuvert innan det går iväg. Den som bara ser kuvertet tror att båda sedlarna var dina.

En **PayJoin** (också *P2EP*) är en betalning där **mottagaren lägger till ett eget mynt** som input i tx:en. Det ser
ut som en vanlig betalning, men inputs har **två ägare**. Det är gjort för att lura
[common-input-ownership](common-input-ownership.md), som antar att alla inputs i en tx har samma ägare.

## Det syns inte på kedjan

En PayJoin går inte att se säkert. Den kan bara **anas**. Kontrollen `payjoin-hint v1` letar efter tx där
common-input-ownership slog ihop inputs, men där:

- **en input behövdes inte:** utan den minsta inputen räcker de andra ändå till den större utbetalningen och avgiften.
  En vanlig plånbok lägger sällan till en input den inte behöver, men i en PayJoin gör mottagaren det;
- och, som ett extra tecken, **inputs är av olika adresstyp**, som när två olika plånböcker bidrar.

Då markeras tx:en som *Möjlig PayJoin* med ett grått område.

## I utredningen

Klustringen är **osäkrare** här. Visar det sig vara en PayJoin, bryt upp sammanslagningen (se
[Bryta upp ett kluster](bryta-upp-kluster.md)), så att mottagarens mynt inte räknas som avsändarens. Det kan också vara en
plånbok som samlar ihop mynt samtidigt som den betalar; det är en bedömning, aldrig ett faktum.
