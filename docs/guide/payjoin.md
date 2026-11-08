# PayJoin

> **Som i vardagen:** du betalar en vara med en hundralapp, och säljaren lägger samtidigt en egen femtiolapp i samma
> kuvert innan det går iväg. Den som bara ser kuvertet tror att båda sedlarna var dina.

En **PayJoin** (också *P2EP*) är en betalning där **mottagaren lägger till ett eget mynt** som input i tx:en. Det ser
ut som en vanlig betalning, men inputs har **två ägare**. Det är gjort för att lura
[common-input-ownership](common-input-ownership.md), som antar att alla inputs i en tx har samma ägare.

## Det syns inte på kedjan

En PayJoin går inte att se säkert. Den kan bara **anas**. Klustringens analys `payjoin v1` letar efter tx med minst
två inputs från olika adresser och två outputs, där:

- **en input behövdes inte:** utan den minsta inputen räcker de andra ändå till den större utbetalningen och avgiften.
  En vanlig plånbok lägger sällan till en input den inte behöver, men i en PayJoin gör mottagaren det;
- och, som ett extra tecken, **inputs är av olika adresstyp**, som när två olika plånböcker bidrar.

Då slås **inget** i tx:en ihop automatiskt: inputs blir ett **förslag** i stället för en sammanslagning, det säkraste
när de kan ha två ägare. Kontrollen `payjoin-hint v1` markerar tx:en som *Möjlig PayJoin* med ett grått område.

## I utredningen

Klustringen är **osäkrare** här, och därför bara ett förslag. Godkänn sammanslagningen bara när du vet att inputs har
samma ägare; det kan vara en plånbok som samlar ihop mynt samtidigt som den betalar. Det är en bedömning, aldrig ett
faktum. Om att bryta upp ett kluster, se [Bryta upp ett kluster](bryta-upp-kluster.md).
