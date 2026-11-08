# Börser och uttag

> **Som i vardagen:** en börs är som en bank med ett eget fack för varje kund. Den som vill sätta in pengar får ett eget
> inbetalningsnummer. Banken tömmer sedan alla fack i ett stort valv. Ser du pengar gå till ett visst inbetalningsnummer
> vet banken vems fack det är, även om pengarna sedan blandas i valvet.

Målet med en spårning är oftast inte att följa pengarna för alltid, utan att hitta **den punkt där de lämnar kedjan**.
Där finns ofta någon som har visat legitimation. Den punkten är nästan alltid en **börs**.

## Varför börsen är så viktig

En börs växlar kryptovaluta mot vanliga pengar och tvärtom. De flesta börser måste **känna sina kunder** (KYC, *know
your customer*): kontrollera identitet, ofta bankkonto och telefon. Kommer spårade pengar in på en börs kan börsen
därför ofta säga vem som tog emot dem, vilket bankkonto de togs ut till och från vilken IP-adress kontot användes.

## Insättningsadresser och hot wallet

![En börs: varje kund har en egen insättningsadress, och börsen tömmer dem till sin hot wallet](figur:s-borsen)

- Varje kund får en eller flera egna **insättningsadresser**. En insättning dit hör till en kund.
- Börsen **tömmer** insättningsadresserna till sin **hot wallet**, en stor plånbok som börsen själv använder. Där blandas
  alla kunders pengar.
- Därför är **insättningen** det viktiga: adressen och tx:en där pengarna kom in. Att följa pengarna vidare in i börsens
  egna flyttar ger inget, och Chainsaw gör det inte: spåret tar slut med *Börs, uttag funnet* (se
  [Varför ett spår tar slut](sparet-tar-slut.md)).

Mönstret syns också på kedjan: många adresser som var och en får pengar från olika avsändare och sedan töms till samma
adress är ett tecken på en börs.

## I grafen

Uttag är **blått**: börs, börs utan kundkontroll (streckad ram, eftersom det är osäkert att en begäran ger något),
nästlad tjänst, OTC och P2P, kryptoautomat och kryptokort. När spårade pengar når ett uttag får noden en **blå bock** i
hörnet. Om tjänster inuti andra tjänster, se [Nästlade tjänster](nastlade-tjanster.md).

Att en adress är en börs vet Chainsaw från en **etikett**, din egen, en kollegas genom biblioteket eller en öppen källa.
Den är alltid en bedömning (princip 2). Att känna igen börsens mönster av sig själv är inte byggt än.

## Nästa steg

När uttaget är hittat är nästa steg en begäran om uppgifter till börsen. Se [Begäran om uppgifter](begaran.md). Står
pengarna fortfarande kvar på en adress, bevaka den (se [Bevakning](bevakning.md)): rör de sig till en börs vet du vart
begäran ska gå, och när.
