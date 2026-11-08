# Etiketter och varifrån de kommer

> **Som i vardagen:** En etikett är som namnskylten på en brevlåda. Den säger vem som bor där, men vem som helst kan ha
> satt upp den, och den kan vara gammal. Därför står det alltid på etiketten vem som satte den, när och varför, och
> två skyltar från olika håll som säger samma sak väger tyngre än en.

En **etikett** säger vad en adress eller ett kluster är: en börs, en mixer, ett casino, ett offer. Etiketterna är det
som gör en graf av adresser till en berättelse om pengar, och det är oftast en etikett som säger att spåret har nått
en börs och att en begäran om uppgifter kan skickas.

## En etikett är alltid en bedömning

Kedjan säger vilka adresser som fick pengar, aldrig vem som äger dem. Allt som står på en etikett är därför en
**bedömning**, aldrig ett faktum, och etiketten säger alltid vad den bygger på. Frågar du *varför?* får du:

- **Källan:** en egen etikett, en sanktionslista, en öppen källa, en analys av beteende eller ett annat ärende.
- **Vem** som satte den (eller körde kontrollen) och **när**.
- **Varför**, i klartext.
- **Versionen:** för en sanktionslista vilket datum listan publicerades och dess SHA-256, för en analys dess version.

En etikett kan inte ändras. Har den blivit fel **tas den tillbaka** med en rad i journalen som säger varför, och den
står kvar i ärendet som tillbakatagen. Då går det alltid att läsa vad man trodde och när, och `/verify` visar att
ingen etikett har ändrats i efterhand. Att ta tillbaka en etikett går inte att göra i grafen än.

## Sätta en etikett i grafen

Välj en adress eller ett kluster och tryck **L** (eller *Etikett* i inspektören). Du anger namn, sort, källa och skäl,
och kan slå på *Sprid inte*. En etikett på ett kluster sätts på klustret, en på en adress på adressen. Källan
*Beställningen* och *Svar från börs* är din egen bedömning ur ett dokument, och står först i skälet; en *öppen källa*
får sitt namn och dagen du läste den som version. Etiketten *Offer* är det som ger en nod accentfärgen: startpunkten är aldrig
ett offer av sig själv. En etikett som redan kom från ett annat ärende och som du sätter igen blir ett eget ursprung,
och två oberoende ursprung visas som bekräftelse.

## Adress eller kluster

En etikett kan sitta på **en adress** eller på **klustret** som adressen hör till. En etikett på ett kluster gäller alla
adresser som klustringen slår ihop med den, också de som kommer till senare. Den vilar då på två bedömningar: etiketten
och klustringen. Bryter du upp klustret följer etiketten med den del som adressen är kvar i.

## Sorterna

Sorterna följer vad som händer med pengarna, och färgen i grafen säger samma sak:

- **Blått, uttag:** börs, börs utan kundkontroll, nästlad tjänst, OTC och P2P, kryptoautomat, kryptokort. Här tar
  spåret slut med *Börs, uttag funnet*.
- **Gult, pengarna spenderas:** spel, handel, presentkort. Spåret tar slut i en *Tjänst*.
- **Rött, risk:** darknet och garantimarknad (spåret tar slut i en *Tjänst*), mixer och CoinJoin (spåret tar slut där)
  och sanktionerad.
- **Lila, annan kedja:** swap eller brygga. Spåret *fortsätter på annan kedja*.
- **Offer** i accentfärgen och **okänd** utan färg. *Okänd* med ett namn är ett eget namn på aktören.

En **sanktionerad** adress avslutar inte spåret: det säger vem ägaren är, inte vart pengarna tar vägen. Detsamma gäller
offer och okänd.

## Sanktionslistan

Chainsaw läser OFAC:s SDN-lista, den amerikanska listan över sanktionerade personer och företag, som har ett fält för
kryptoadresser (*Digital Currency Address - XBT* för Bitcoin, och motsvarande för andra kedjor). Listan **hämtas inte**
av Chainsaw. Du sparar filen (XML) och importerar den, och Chainsaw:

- sparar filen exakt som den var, med dess **SHA-256** och **publiceringsdatum**, i `%LOCALAPPDATA%\Chainsaw\Sanktionslistor`;
- räknar adresserna per valuta och säger hur många som inte är giltiga (till exempel en Bitcoin-adress med fel
  kontrollsumma), eftersom de aldrig kan ge en träff;
- kontrollerar filen mot sin hash varje gång den läses in, så att en ändrad fil märks i stället för att litas på.

En träff blir en etikett **Sanktionerad** i ärendet, med namnet på posten i listan, dess uid och program, och listans
datum och hash som version. Samma träff märks bara en gång per lista. Kommer en nyare lista med samma adress blir det
ett nytt ursprung, så att båda listorna syns, men två versioner av samma lista räknas inte som en bekräftelse.

Ärendet sparar listans hash, datum och postens uid, inte hela listan. **Spara listfilen** så länge ärendet lever, så
att en träff alltid kan kontrolleras mot exakt den fil den kom från.

## Etiketter smittar

Det du har lärt dig i ett ärende ska inte behöva läras igen i nästa. Därför finns ett **etikettbibliotek** på datorn,
krypterat med en nyckel ur valvet:

![En etikett som sätts i ett ärende (till vänster) läses in i biblioteket på datorn (mitten) och följer med in i nästa ärende som öppnas (till höger), märkt från ett annat ärende.](figur:etiketter-smittar)

- När ett ärende öppnas läses dess etiketter in i biblioteket, också när det är en kollegas fil.
- Biblioteket ger tillbaka: adresser i ärendet som har en etikett i biblioteket får den, märkt **från ett annat ärende**.
  När filen lämnas vidare följer kunskapen med.
- Tas en etikett tillbaka i ärendet där den sattes **dras den tillbaka överallt**: den lämnar biblioteket, ges aldrig
  igen, och kopiorna i andra ärenden tas tillbaka där nästa gång de får etiketter, med en rad i journalen som inte säger
  något om det första ärendet. Har du tagit tillbaka en etikett som kom från biblioteket ges den inte igen.

**Sekretess.** Ärendenumret, anteckningen och varför etiketten sattes stannar i det ärendet och i ditt eget bibliotek.
In i ett annat ärende följer bara **vem** som satte etiketten, **när**, **vad** den säger och vilken sorts källa den
kom från. En etikett kan märkas **Sprid inte**; då lämnar den aldrig sitt ärende. En etikett på ett **offer** sprids
aldrig, hur den än är märkt, och inte heller någon annan etikett på offrets adresser eller kluster (till exempel
offrets eget konto på en börs), eftersom den kan avslöja vem som drabbats. En etikett som redan hade spridits innan
offret märktes ligger kvar i de andra ärendena; ska den bort därifrån tar du tillbaka den i ärendet där den sattes.

Etikettens **namn** följer med. Skriv därför aldrig ett ärendenummer eller en misstänkts namn i namnet; det hör hemma
i anteckningen, som stannar i ärendet.

## Flera ursprung är en bekräftelse

Samma etikett (samma adress, sort och namn) från flera håll slås ihop till en. Kommer den från **flera oberoende
ursprung**, till exempel din egen etikett och en kollegas, visas det som en bekräftelse. En person, en analys eller en
sanktionslista räknas bara en gång, hur många gånger, versioner eller ärenden den än kommer genom. Säger två etiketter olika saker om samma adress visas båda, den med
flest ursprung först.
