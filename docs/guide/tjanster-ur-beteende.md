# Tjänster ur beteende

> **Som i vardagen:** du behöver inte se skylten för att förstå att det är en bank. Ser du hundratals människor lämna
> kuvert i var sitt fack, och varje kväll en värdetransport som tömmer alla facken på en gång, vet du vad det är för
> ställe, även om du inte vet vad det heter.

En börs eller en annan tjänst lämnar spår i **hur** den använder kedjan, även när ingen etikett säger vad den är.
Chainsaw känner igen två sådana mönster av sig själv, med kontrollen `service-behaviour v1`.

## Insättningsadresser som sveps

Varje kund hos en börs får en egen **insättningsadress**. Kunden betalar dit från sin egen plånbok, och börsen tömmer
sedan många insättningsadresser på en gång till sin **hot wallet** (ett *svep*). Kontrollen ser:

- en tx som betalar **en enda adress**,
- och som tömmer **minst tre adresser** som var och en fick en betalning från **en egen avsändare** i grafen.

Då markeras hot walleten och insättningsadresserna med ett blått område och titeln *Trolig tjänst*. Insättningsadresserna
är de viktiga: varje insättning är en kund hos tjänsten, och en [begäran](begaran.md) gäller just dem.

## Utbetalningar i batch

En börs betalar ofta ut till många kunder i **samma tx**: en eller två adresser betalar minst åtta mottagare, med olika
belopp. Kontrollen markerar den som *Trolig utbetalning i batch*. Betalar den från samma adress som svepen går till
stärker det bilden.

## Det är bedömningar

Kontrollen säger bara *trolig tjänst*, aldrig vilken: det kan bara en etikett säga (se [Etiketter](etiketter.md)). En
plånbok som samlar ihop sina egna mynt kan likna ett svep, och en lönebetalning kan likna en batch. Vill du sätta namn på
tjänsten gör du det med en egen etikett, med skälet.

Om tjänster som har konto hos andra tjänster, se [Nästlade tjänster](nastlade-tjanster.md).
