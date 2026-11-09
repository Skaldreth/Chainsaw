# Nästlade tjänster

> **Som i vardagen:** en liten växlare på torget har inget eget bankkontor. Han har ett vanligt konto på en storbank och
> sköter sina kunders pengar genom det. Frågar du storbanken vems kontot är får du växlarens namn, inte namnet på den
> kund du letar efter.

En **nästlad tjänst** är en tjänst som inte har en egen plats på kedjan, utan använder ett eller flera konton hos en
större börs. Det kan vara en liten växlare, en OTC-handlare, en betaltjänst eller en börs utan kundkontroll. Pengarna
går in på den stora börsens insättningsadresser, och den stora börsen ser bara tjänsten som en kund.

![En nästlad tjänst: en mindre tjänst med egna kunder inuti en större börs konton](figur:s-nastlad)

## Varför det försvårar

- **Spåret ser ut att vara klart.** Pengarna når en känd börs, uttaget är funnet, men kunden hos börsen är tjänsten och
  inte den som tog ut pengarna.
- **Kundkontrollen kan saknas** i den nästlade tjänsten, även när den stora börsen har en.
- **Pengarna blandas två gånger:** först i tjänsten, sedan i börsen.

## Hur det ser ut

- Samma insättningsadress på en börs tar emot pengar från **väldigt många olika avsändare**, mycket fler än en vanlig
  privatperson.
- Uttagen från börsen går till adresser som i sin tur **betalar ut till många**.
- Öppna källor eller kollegors etiketter pekar ut tjänsten.

## I Chainsaw

Sorten **Nästlad tjänst** finns bland uttagen och är **blå**, som de andra ställena där pengarna lämnar kedjan. Sätt
etiketten med **L** när du vet eller tror att en insättningsadress tillhör en sådan tjänst, och skriv i skälet varför.
Spåret tar slut där med *Börs, uttag funnet*, men etiketten säger att en begäran till den stora börsen ger tjänsten, och
att nästa steg är en begäran till tjänsten själv.

## Förslaget Nästlad tjänst

Förslaget `nested-service v1` känner igen mönstret av sig själv, när en börs har en etikett:

- en ägare utan etikett, som [klustringen](common-input-ownership.md) räknar den, tar emot betalningar från **minst
  åtta olika avsändare** och betalar **minst åtta olika mottagare**, över minst ett dygn;
- den betalar **in till börsen minst två gånger** och får **uttag tillbaka minst två gånger**;
- **minst hälften** av det kunderna betalade går vidare in på börsen, och minst hälften av det som betalas ut kom
  därifrån: tjänsten sköter sin verksamhet genom kontot.

Startpunkter och offer räknas aldrig som tjänstens kunder. Förslaget är streckat, med ett blått område, och väntar på
ditt beslut: godkänn eller avfärda. Ett godkännande är din bedömning, inte ett faktum; sätt gärna etiketten **Nästlad
tjänst** med L när du är säker.

## Vad det betyder för begäran

En begäran till den stora börsen är fortfarande värd att göra: den ger vem tjänsten är, vilka konton den har och hur
pengarna flyttades. Men be också om **tjänstens egna uppgifter om insättningen**, eller rikta en ny begäran till
tjänsten. Se [Börser och uttag](borser.md) och [Begäran om uppgifter](begaran.md).
