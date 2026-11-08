# Fortsättningen på en annan kedja

> **Som i vardagen:** pengarna växlas på ett växelkontor vid gränsen. Kvittot på den ena sidan säger bara att kronor
> lämnades in. För att följa pengarna vidare behöver du det andra kvittot, euro som betalades ut, och det är du som
> lägger de två kvittona bredvid varandra och säger att de hör ihop.

Pengar byter ofta kedja: bitcoin blir USDT på TRON, eller ETH på Ethereum. Det sker genom en **brygga** eller en
**swap-tjänst**. På den första kedjan syns bara att pengarna gick in i tjänsten; på den andra kedjan syns bara att
någon fick pengar ur den. Ingen av kedjorna säger att det är samma pengar. Se också
[Bryggor och swappar](bryggor-och-swappar.md).

![En brygga eller swap: spåret lämnar Bitcoin, och fortsättningen på den andra kedjan knyts för hand](figur:s-brygga)

## I grafen

En känd brygga eller swap-tjänst (en etikett av sorten swap eller brygga) avslutar spåret med orsaken *Fortsätter på
annan kedja*, i lila. Spåret är inte slut, men Chainsaw vet inte själv var det fortsätter.

## Knyta fortsättningen

1. Välj aktören där pengarna lämnade kedjan och tryck **K** (eller *Annan kedja* i ikonlisten).
2. Välj kedjan och klistra in tx:en på den andra kedjan. Den känns igen direkt; en Ethereum-tx börjar med `0x`.
3. Kom flera mynt in till aktören väljer du vilket som gick vidare.
4. Enter sparar.

Fortsättningen ritas som en **lila streckad** linje till tx:en på den andra kedjan, med kedjans namn under hörnet.
Streckad, eftersom länken är **din bedömning** och inte något kedjan säger. Länken förseglas i ärendet med vem och när,
och kan tas tillbaka i nodens historik (se [Ta tillbaka och ångra](historik.md)).

## Hur du hittar fortsättningen

Fortsättningen hittas oftast genom **belopp och tid**: ungefär samma värde (minus tjänstens avgift och växelkursen)
som kom ut på den andra kedjan strax efter. Många swap-tjänster visar dessutom båda sidorna i sina egna sökverktyg, och
en begäran till tjänsten kan ge svaret.

Att Chainsaw själv hittar fortsättningen ur belopp och tid, eller ur bryggornas egna händelser, är **inte byggt än**.
Spårning vidare på TRON är inte heller byggd än; Ethereum går att följa.
