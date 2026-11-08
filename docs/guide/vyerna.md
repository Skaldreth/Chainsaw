# Vyerna och pillret

> **Som i vardagen:** Chainsaw är som ett skrivbord med några lådor. Pillret till vänster är lådornas handtag: ett
> handtag per låda, och den du har öppen lyser. Lådor som inte är byggda än står kvar, men gråa, så att du vet att de
> kommer.

## Pillret

Vid fönstrets vänsterkant står ett **flytande piller** med en rund knapp per vy. Namnet syns när du håller pekaren över
en knapp, och kortkommandot står i tipset. Den valda vyn är markerad; sidan har ingen egen rubrik, eftersom pillret
redan säger var du är.

![Pillret vid vänsterkanten: en rund knapp per vy, den valda i accenten och de som inte är byggda svaga](figur:s-pillret)

| Vy | Tangent | Läge |
| --- | --- | --- |
| Nytt | Ctrl+N | En meny som fälls ut ur knappen: Startpunkt (S), Bevakning (B), Importera sanktionslista … (I) och Ärende (Ctrl+N). Etikett och Begäran är tonade |
| Graf | Ctrl+1 | Byggd. Där spåret följs, se [Grafen och bildspråket](grafen.md) |
| Tidslinje | Ctrl+2 | Inte byggd än. Tiden finns redan under grafen, se [Tidslinjen under grafen](tidslinjen.md) |
| Flöde | Ctrl+3 | Inte byggd än. Pengar in och ut som ett flöde |
| Exponering | Ctrl+4 | Inte byggd än. Vilka tjänster ett kluster har betalat till eller fått från |
| Begäran | Ctrl+5 | Inte byggd än, se [Begäran om uppgifter](begaran.md) |
| Bevakning | Ctrl+6 | Byggd, se [Bevakning](bevakning.md) |
| Etiketter | Ctrl+7 | Inte byggd än, se [Etikettbiblioteket](etikettbiblioteket.md) |
| Guide | F1 | Byggd. Avsnitten du läser nu |
| Inställningar | Ctrl+, | Byggd. En dialog mitt i fönstret |

**Pillret följer läget.** Utan öppet ärende fälls de vyer som kräver ett ärende bort. Vyer som inte är byggda är
nedtonade och säger det.

## Inställningarna

**Ctrl+,** eller kugghjulet öppnar en dialog över en mörkad bakgrund, med sidorna:

- **Nätverk:** Windows eller VPN, och VPN-utgångarna. Se [Utgången: Windows eller VPN](utgangen.md).
- **Källor:** endpointarna per kedja och Chainabuse. Se [Källorna och deras endpoints](kallor.md).
- **Låsning:** tiden, Windows låsning och bevakningen i bakgrunden. Se [Låsning](lasning.md).
- **Kurser:** valutan (SEK, USD eller EUR) och periodens längd. Se [Kurser och omräkning](kurser.md).
- **Profil:** namn, titel och enhet, och att byta huvudnyckel. Se [Huvudnyckeln och valvet](huvudnyckeln.md).
- **Om Chainsaw:** version, commit, återställningsnyckelns fingeravtryck och licenserna.

*Utseende* och *Länkar* är inte byggda än. Ändringar gäller direkt. Esc stänger.

## Det som alltid finns

- **Titelraden:** ärendet (ett klick eller Ctrl+O öppnar ärendeväljaren, se [Öppna, byta och stänga ärenden](arendevaljaren.md))
  och utgången (ett klick visar vägen).
- **Kommandoraden** fälls fram med Ctrl+K, se [Kommandoraden och länkar](kommandoraden.md).
- **Toasterna** nere till höger: fel och varningar ligger kvar tills du stänger dem, lyckat och information tonar ut
  efter några sekunder (hovra för att hålla kvar dem). Högst fem åt gången, och de töms när Chainsaw låses.
- **Guidens panel** från höger, öppnad med *Varför?* eller *?*, med *Öppna i Guide* till hela vyn.
