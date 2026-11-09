# Jämna belopp i fiat

> **Som i vardagen:** Den som swishar betalar 500 kronor, inte 487,32. Ser du två belopp på ett kvitto, och det ena är
> jämnt i kronor medan det andra är udda, gissar du att det jämna var priset och det udda det som blev över.

Den som köper något med bitcoin tänker oftast i sin egen valuta: priset är **5 000 kronor** eller **200 euro**, och
plånboken räknar ut hur många bitcoin det blir just då. Beloppet blir då udda i bitcoin men **jämnt i kronor eller
euro** vid tx:ens tid. Resten går tillbaka som [växel](vaxeladress.md), och den är udda i alla valutor.

## Hur Chainsaw läser det

Kontrollen `round-fiat v1` värderar varje output i en bekräftad tx med minst två outputs till **kursen vid blocket**,
i kronor, euro och dollar, och frågar om beloppet ligger nära ett jämnt tal:

- **Jämnt** är en siffra gånger en tiopotens (100, 2 000, 50 000), och 1,5 och 2,5 gånger en tiopotens (1 500,
  25 000), som man betalar lika ofta. Beloppet får ligga högst **en halv procent** från det jämna talet, ungefär så
  mycket som priset rör sig inom timmen som kursen gäller för.
- **Små belopp** vägs inte: under 100 kronor eller 10 euro och dollar räknas inget som en jämn betalning.
- Fyndet kräver att **precis en output** inte är jämn i någon valuta och att **alla andra** är jämna i minst en. Då är
  de jämna troliga betalningar och den udda trolig växel. Är fler än en udda, eller alla jämna, säger kontrollen inget.

*Varför?* visar varje jämn output med beloppet, det jämna talet och hur långt ifrån det ligger, och om
[växeladressens](vaxeladress.md) tecken pekar ut samma output eller en annan. Det är en **kontroll** som gör sitt av
sig själv: den slår aldrig ihop något, och området i grafen ligger runt avsändaren och den troliga växeln.

## Bara kurser som redan finns i ärendet

Kontrollen hämtar aldrig själv. Den läser de [kurser](kurser.md) som ärendet redan har, till exempel för att grafen
har visat kronor under beloppen, och **gissar aldrig** en kurs som saknas:

- En tx vars blocktid inte har någon kurs i ärendet vägs inte alls.
- Saknas en valuta (oftast dollar, när man räknar i kronor) säger *Varför?* det, och den valutan vägs inte.
- Kursen för timmen används i första hand, sedan minuten och dagen, så att samma ärende alltid ger samma svar.

Varje kurs som användes står i *Varför?* med källan, perioden eller dagen och **SHA-256** av det sparade svaret, och
sparas med fyndet, så att värderingen går att räkna om med en miniräknare.

## Det är ett svagt tecken

- **Kursen är källans, inte köparens.** En börs eller en växlare tar sin egen kurs och sin avgift, som kan skilja några
  procent från Bitstamps vid blocket. Ett belopp som var jämnt hos köparen kan då missas.
- **Slumpen.** Ungefär vart tjugonde belopp ligger inom en halv procent från ett jämnt tal av en slump, i varje
  valuta. Med kronor och euro får därför ungefär **var sjätte** tx med två slumpade belopp fyndet ändå.
- **Blockets tid** är inte när tx:en skickades; priset kan ha hunnit röra sig.

Använd det som stöd tillsammans med växeladressens tecken, aldrig ensamt. Det är en bedömning, inte ett faktum.
