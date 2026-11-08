# Nytt ärende och startpunkterna

> **Som i vardagen:** att lägga upp ett ärende är som att lägga en ny pärm på skrivbordet och skriva numret på ryggen.
> Startpunkterna är de första lapparna du lägger i den: kontonumret i beställningen, kvittot från offret. Chainsaw
> slår redan medan du skriver upp vad som står på kontot i dag, så att du ser att lappen stämmer innan pärmen stängs.

Ett **ärende** är en utredning i Chainsaw: en krypterad fil med allt som har hämtats, alla slutsatser och hela
journalen. En **startpunkt** är där spåret börjar, en adress eller en tx. Vad en startpunkt är och vilka former som
känns igen står i [Startpunkter](startpunkter.md).

## Så gör du

Tryck **Ctrl+N**, eller välj *Nytt ärende* mitt på den tomma grafen eller i pillrets meny *Nytt*. En panel fälls ut från
höger och resten av fönstret mörkas.

1. **Ärendenummer.** Det enda som krävs. En grön bock säger att numret går att använda, och under fältet står vad filen
   kommer att heta (`2026-0117.cut`). Finns numret redan säger panelen det direkt.
2. **Titel**, om du vill. Den står efter numret i titelraden.
3. **Startpunkter.** Skriv eller klistra in en adress eller en tx på raden *+ Lägg till adress eller tx*. Raden blir
   en startpunkt när du slutar skriva en stund, eller med Enter. **Ctrl+V** med flera rader lägger in alla på en gång,
   högst 50.
4. **Kan öppnas av en kollega**, om ärendet ska lämnas vidare. Se [Ärendefilen och krypteringen](arendefilen.md).
5. **Skapa ärende** (Ctrl+Enter). Knappen är nedtonad tills det finns ett nummer.

![Panelen Nytt ärende: numret överst och en rad per startpunkt med det som hämtats och dess läge](figur:s-nytt-arende)

## Det som hämtas i förväg

Varje rad visar vad kedjan säger om startpunkten redan innan ärendet finns: för en adress saldot, antalet tx och första
och senaste aktivitet, för en tx beloppet och hur många in och ut. Höjden till höger är kedjans höjd när det hämtades.
Sanktionsbrickan kommer från den sanktionslista som är importerad på datorn, och inget skickas för den.

Raden säger ärligt sitt läge:

- **Hämtar** medan svaret är på väg (tre streck som pulserar).
- **Klar** med uppgifterna.
- **Väntar på utgången** när ingen utgång är ansluten. Raden hämtas av sig själv när utgången kommer.
- **Inte igenkänd** när texten inte är en adress eller tx som Chainsaw känner igen. *Skapa ärende* säger vilken rad som
  ska bort.

Två startpunkter hämtas åt gången, och ett halvskrivet värde skickas aldrig: en adress känns igen först när hela formen
och kontrollsumman stämmer.

## Varför det är byggt så

- **Inget hämtat går förlorat** (princip 1). Ärendet finns inte medan panelen är öppen, så det som hämtas läggs i ett
  ärende i minnet, förseglat precis som i ett riktigt ärende. När du trycker *Skapa ärende* förs varje råsvar över till
  den nya filen med sin SHA-256 och en rad i journalen. Avbryter du (Esc) skrivs ingenting till disk.
- **Bara det uppslaget kräver skickas** (princip 4): adressen eller tx:en, genom den valda utgången. Vid rubriken står
  vilken utgång som används.
- **Startpunkten är neutral.** Den behåller sin sort och får bara en liten flagga. Den kan lika gärna vara den
  misstänktes plånbok som offrets; offret är en etikett du sätter själv. Se [Grafen och bildspråket](grafen.md).

Ett ärende kan också få en startpunkt senare: skriv adressen i kommandoraden (Ctrl+K) och tryck Enter, eller klistra in
den på den tomma grafen. Se [Kommandoraden och länkar](kommandoraden.md).
