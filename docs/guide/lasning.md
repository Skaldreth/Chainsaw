# Låsning

> **Som i vardagen:** att låsa Chainsaw är som att stänga bankfacket och lämna tillbaka nyckeln i receptionen när du
> går ifrån skrivbordet. Pärmarna ställs undan, och ingen som sätter sig vid datorn ser vad du arbetade med.

## När Chainsaw låser

- **Ctrl+L**, eller `/lock` i kommandoraden.
- **Efter en tid utan aktivitet:** 5, 15 (förval) eller 30 minuter eller en timme, under *Inställningar → Låsning*.
  Tiden räknas från senaste gången du använde tangentbord eller mus i Chainsaw.
- **När Windows låses**, om brytaren *När Windows låses* är på.
- **Alltid** när datorn går i viloläge eller sessionen kopplas från.

## Vad som händer

![Låsning: arbetet döljs, datanyckeln lämnar minnet och ärendet sparas och stängs](figur:s-lasning)

1. Arbetsytan döljs direkt och låsskärmen visas.
2. Kommandoraden, öppna formulär och toasterna töms.
3. Det öppna ärendet sparas och stängs i bakgrunden.
4. Valvets datanyckel nollställs i minnet.

En **ansluten utgång står kvar** när Chainsaw låses, men inga uppslag görs medan Chainsaw är låst. Det sparar en ny
anslutning när du kommer tillbaka. Låsskärmen säger det.

**Bevakningen** fortsätter i bakgrunden medan Chainsaw är låst eller stängt, men den vet bara det den behöver och
öppnar aldrig ärendet. Har en bevakning slagit till säger låsskärmen bara det, aldrig vad. Se [Bevakning](bevakning.md).

En länk som kommer medan Chainsaw är låst väntar tills du låst upp (se [Kommandoraden och länkar](kommandoraden.md)).

## Låsa upp

Skriv huvudnyckeln på låsskärmen och tryck Enter. Ärendet som var öppet öppnas inte av sig självt; välj det med Ctrl+O.
I hörnet står *Om Chainsaw* med version och commit, så att du kan se vilken version du kör innan du låst upp.

Varför: ett ärende innehåller uppgifter om brott och offer. Det ska inte ligga öppet på en skärm som ingen tittar på
(princip 4). Se också [Huvudnyckeln och valvet](huvudnyckeln.md).
