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

## Förslag från Chainsaw (cross-chain v1)

Chainsaw letar också själv, med analysen **cross-chain v1**. För varje betalning in till en brygga eller swap som inte
är knuten än väger den betalningar på de andra kedjorna Chainsaw har (Bitcoin, Ethereum, TRON, BSC och Litecoin) och
visar de troliga som **kandidater** under *Förslag* (Ctrl+J), en i taget, som mixerns troliga utgångar:

- **Avsändarens memo.** Vissa swap-tjänster låter den som betalar skriva vart pengarna ska, direkt på kedjan. På
  Bitcoin står det i en utgång som aldrig kan spenderas (`OP_RETURN`), i THORChains form, till exempel `=:ETH.ETH:0x…`:
  byt till ether och betala till adressen. Det är **avsändarens instruktion** till tjänsten, inte tjänstens kvitto på
  en utbetalning: utbetalningen syns bara på den andra kedjan, och THORChain kan betala tillbaka till avsändaren i
  stället. Chainsaw slår upp mottagaren på den andra kedjan (också ether som en router betalat ut inne i en tx). En
  betalning till den **från en brygga eller swap** kommer först. Betalade någon annan, eller saknas kursen, står den
  kvar med lägre poäng och *beloppet är inte prövat*. Ett memo som pekar på samma kedja, eller på ett namn (THORName)
  i stället för en adress, sägs i *Varför?*.
- **Värdet efter en avgift.** Tillgångarna skiljer sig (bitcoin in, USDT ut), så Chainsaw jämför **värdet** med
  ärendets kurser vid de två blocktiderna. Det som kom ut får vara upp till 20 % mindre (tjänstens avgift och
  kursskillnad), eller några procent mer, eftersom kurserna läses vid två tider. Passar värdet inte är betalningen ingen
  kandidat.
- **Tiden.** Inom 72 timmar efter, och full poäng inom två timmar.
- **Adressen.** Ethereum, BSC och TRON skriver samma 20 byte på olika sätt, så samma nyckel kan ha en adress på alla
  tre. En utbetalning till betalarens egen nyckel på den andra kedjan väger mer, och så gör en utbetalning från en
  brygga eller swap. En betalning utan något sådant står alltid under dem, hur väl värdet och tiden än passar.
- **Det som inte vägs:** betalningar som själva går in i en brygga eller swap, och betalningar till eller från ett
  offer. En startpunkt vägs som andra adresser, eftersom den lika gärna kan vara den misstänktes plånbok, och skälet
  säger att mottagaren är en startpunkt.
- **Kontrakt har ingen nyckel.** Samma adress på två kedjor räknas bara som samma nyckel när ingen av dem är ett känt
  kontrakt (en brygga, ett kontrakt på Chainsaws lista, eller en adress som betalat ut ether inne i en tx).
- **Bryggor utanför grafen.** Att betalaren är en brygga eller swap läses också ur etiketter (*Swap eller brygga*) och
  ur Chainsaws lista över kända kontrakt, inte bara ur grafens noder.
- **Poängen är ordningen.** Memots mottagare som en brygga betalade får alltid över 0,8, alla andra högst 0,8, och en
  betalning utan tecken i adressen högst 0,6.

Varje kandidat har sitt skäl: beloppet, värdet mot det som gick in, tiden och vad som talar för adressen. *Varför?*
säger vilka kurser och vilken uppgift förslaget bygger på, med blockhöjd och hash.

En betalning fortsätter på ett ställe, så när en kandidat är godkänd finns bara *Avfärda resten* kvar.
**Godkänner** du en kandidat knyts fortsättningen precis som när du gör det för hand, med ditt frivilliga skäl, och
länken förseglas också med analysens namn och version och ditt beslut. **Ångrar** du beslutet, eller tar tillbaka
länken i historiken, tas båda tillbaka tillsammans, och förslaget väntar igen. Inget knyts av sig självt: det är alltid
ditt beslut, och länken är streckad som allt annat som är en bedömning.

### Vad Chainsaw skickar

Uppslaget på den andra kedjan går bara genom utgången och skickar bara **mottagarens adress**, aldrig ärendet. Kurserna
hämtas som alla andra kurser. Är utgången inte ansluten, kedjan avstängd under Källor eller källan borta, står det i
panelen, och förslaget bygger då bara på det som redan finns i ärendet. Bara mottagarens senaste sida läses, och
Chainsaw frågar inte om samma mottagare igen inom en kvart, hur ofta grafen än visas.

