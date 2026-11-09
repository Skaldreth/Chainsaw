# Damm och adressförgiftning

> **Som i vardagen:** någon stoppar en enkrona i hundra brevlådor för att se vilka som plockar upp den tillsammans med
> sina andra mynt. Eller en bedragare som skickar ett vykort från en avsändare som nästan heter som din bank, i hopp om
> att du svarar till fel adress nästa gång.

Två knep med **mycket små belopp** förvirrar spårningen, och båda känns igen av kontrollen `dust v1`.

## Damm

**Damm** är belopp så små att de knappt är värda något, högst 1 000 satoshi här. Den som skickar damm till många
adresser hoppas att mottagarna spenderar det tillsammans med sina andra mynt. Då slår
[common-input-ownership](common-input-ownership.md) ihop adresserna, och avsändaren får veta vilka som hör ihop.

Kontrollen markerar en tx som skickar damm till **minst tio adresser** som *Damm till N adresser*. Följ inte dammet,
och se ett damm-mynt som spenderas tillsammans med andra som ett svagare tecken på samma ägare.

Är alla belopp **330 eller 546 satoshi** heter den *Damm eller inskriptioner*: det är de vanliga beloppen för
inskriptioner (*ordinals*), som skickas ut på samma sätt utan att vara damm.

## Adressförgiftning

En bedragare skapar en adress som **liknar** en adress offret har använt, i början och i slutet, och skickar ett litet
belopp till eller från offret. Adressen hamnar då i offrets historik. Nästa gång offret kopierar en adress ur historiken
kan pengarna gå till bedragaren i stället.

Kontrollen jämför adresserna i grafen: en adress som har ett litet belopp (högst 10 000 satoshi) och som delar sina
**fyra första tecken** (efter `bc1q`, `bc1p`, `1` eller `3`) och **fyra sista** med en annan adress i grafen markeras som
*Trolig adressförgiftning*. *Varför?* säger vilken adress den liknar.

## I utredningen

- **Spåret ska inte följa damm eller den liknande adressen.** De är inte en del av offrets pengar.
- **Jämför alltid hela adressen**, aldrig bara början och slutet, också när du skriver en begäran.
- Har offret faktiskt betalat till den liknande adressen är det bedragarens adress, och då är den startpunkten.

Det är bedömningar: två adresser kan likna varandra av en slump, och ett litet belopp kan vara en riktig betalning. Att
tona ned dammet och den liknande adressen i grafen är **inte byggt än**; det skissas först.
