# Så spårar man i Chainsaw

> **Som i vardagen:** Att spåra kryptovalutor är som att följa fotspår i snö. Spåren syns för alla och går inte att
> sudda ut, men de säger inte vem som gick. Man följer dem steg för steg tills de går in genom en dörr där någon vet
> vem som kom in, oftast en börs, som har sina kunders namn.

Kryptospårning i en utredning handlar oftast om en sak: att följa pengarna till den punkt där någon tar ut dem,
oftast en börs. Där finns någon som kan svara på vem det var. Chainsaw följer ärendets gång i fem steg.

![Spåret börjar i startpunkten (flaggan), följer offrets pengar (accent) hopp för hopp förbi andras pengar (grått) och slutar helst i en börs (blå) med uttag funnet (bocken), dit en begäran om uppgifter kan gå.](figur:sa-sparar-man-flode)

## 1. Ett ärende

Ett ärende är en krypterad fil på den här datorn, en per utredning (**Ctrl+N**). Allt som hämtas sparas i den, med
källa, tid och hash, så att varje steg går att kontrollera i efterhand. Inget skickas till molnet, och uppslag går bara
genom den utgång du har valt.

## 2. Startpunkten

En startpunkt är en adress eller en tx där spåret börjar. Den kan lika gärna vara den misstänktes plånbok som offrets
betalning. Klistra in den var som helst på den tomma grafen (**Ctrl+V**), så känner Chainsaw igen sorten på formen.
Se [Startpunkter](startpunkter.md).

## 3. Expandera

Välj en aktör och tryck **E**. Då hämtas dess tx genom utgången och motparterna ritas. Spårningsmetoden räknar hur
mycket av beloppet som följer med genom varje tx, och beloppen står på linjerna. Se
[Spårningsmetoder](sparningsmetoder.md).

## 4. Uttaget

Följ pengarna tills de når en blå nod: en börs eller ett annat uttag. Där lämnar pengarna kedjan, och dit går en
begäran om uppgifter. Ett spår kan också ta slut tidigare, till exempel i en mixer; se
[Varför ett spår tar slut](sparet-tar-slut.md).

## 5. Bevaka

Välj en aktör och tryck **B**, eller högerklicka och välj *Bevaka …*, så säger Chainsaw till när pengarna rör sig
vidare. Se [Bevakning](bevakning.md).
