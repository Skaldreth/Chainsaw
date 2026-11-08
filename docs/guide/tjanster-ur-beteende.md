# Tjänster ur beteende

> **Som i vardagen:** du behöver inte se skylten för att förstå att det är en bank. Ser du hundratals människor lämna
> kuvert i var sitt fack, och varje kväll en värdetransport som tömmer alla facken på en gång, vet du vad det är för
> ställe, även om du inte vet vad det heter.

En börs eller en annan tjänst lämnar spår i **hur** den använder kedjan, även när ingen etikett säger vad den är.
Chainsaw känner igen två sådana mönster av sig själv, med kontrollen `service-behaviour v1`.

## Insättningsadresser som sveps

Varje kund hos en börs får en egen **insättningsadress**. Kunden betalar dit från sin egen plånbok, och börsen tömmer
sedan många insättningsadresser på en gång till sin **hot wallet** (ett *svep*). Kontrollen ser:

- **minst två svep** till samma adress (en tx som betalar en enda adress och tömmer minst två insättningsadresser),
- **minst tio insättningsadresser** i alla svepen, betalda av **minst tio olika avsändare** (ägare, som klustringen
  räknar dem),
- och betalningarna in **utspridda över minst ett dygn**.

En bedragare som samlar ihop några offers betalningar och sveper dem ser likadan ut i liten skala. Därför räknas
aldrig en adress som en **startpunkt** eller ett **offer** (en etikett) betalade, eller som är en av dem, som en kunds
insättning.

Då markeras hot walleten och insättningsadresserna med ett blått område och titeln *Trolig tjänst*. Är det en tjänst är
insättningsadresserna troligen kundernas hos den, och en [begäran](begaran.md) kan gälla dem.

## Utbetalningar i batch

En börs betalar ofta ut till många kunder i **samma tx**: en eller två adresser betalar minst åtta mottagare, med olika
belopp. Kontrollen markerar den som *Trolig utbetalning i batch*. Betalar den från samma adress som svepen går till
stärker det bilden.

## Det är bedömningar

Kontrollen säger bara *trolig tjänst*, aldrig vilken: det kan bara en etikett säga (se [Etiketter](etiketter.md)). En
plånbok som samlar ihop sina egna mynt kan likna ett svep, och en lönebetalning kan likna en batch. Vill du sätta namn på
tjänsten gör du det med en egen etikett, med skälet.

Om tjänster som har konto hos andra tjänster, se [Nästlade tjänster](nastlade-tjanster.md).
