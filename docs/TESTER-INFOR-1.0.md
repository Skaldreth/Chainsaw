# Att prova på Windows före 1.0

Det som är byggt men inte kan provas i molnmiljön (Linux), per område: vad man gör och vad som ska hända.

## Start

- `dotnet run --project src/Chainsaw.App`: fönstret öppnas mitt på skärmen, mörkt, med egen titelrad och utan fel.

## Skalet (etapp 1, SH1b)

- Titelraden: kättingloggan med ögat är skarp i 16 px, *Chainsaw*, *Inget ärende öppet* och *Ingen utgång* med grå prick.
- Fönstret går att dra i titelraden, dubbelklick maximerar, Windows fäster det mot kanterna, och kanterna ändrar storlek.
- Maximerat går fönstret inte utanför skärmen; minimera, maximera och stäng fungerar, stäng blir röd vid hovring.
- Ikonlisten: elva ikoner, skarpa i 20 px, Nytt överst med en linje under, Terminal, Guide och Inställningar längst ner.
- Hovring visar namnet och kortkommandot till höger om ikonen, och ikonen blir ljusare på grå bakgrund.
- Klick på en ikon visar vyn: huvudet får vyns namn, sidan vyns ikon, namnet och *Inte byggt än*, och ikonen markeras.
- Ctrl+N, Ctrl+1 till Ctrl+7, Ctrl+0, F1 och Ctrl+, öppnar rätt vy.
- Ctrl+K och Ctrl+F lägger markören i kommandoraden längst ner; ramen blir magenta.
- Text i kommandoraden och Enter: *Kommandon och sökning är inte byggda än* visas ovanför raden; Esc tömmer allt.
- Kommandoradens text är skarp (skuggan får inte göra den suddig).

## Valvet (etapp 2, Security)

Valvet har inget gränssnitt än; det provas när förstagångsinställningen och låsskärmen byggs.

- Valvet hamnar i `%LOCALAPPDATA%\Chainsaw\vault.json` och innehåller bara format, version, salt, iterationer och
  krypterade fält (Base64), inga namn eller nycklar i klartext.
- Att låsa upp tar under ungefär en sekund på en vanlig arbetsstation (PBKDF2-SHA512, 600 000 iterationer), och
  gränssnittet fryser inte under tiden.
- Ingen `vault.json.tmp` ligger kvar efter att valvet sparats.

## Ärendefilen (etapp 3, Cases)

Ärendena har inget gränssnitt än; det provas när *Nytt ärende* och öppna ärende byggs.

- Ett nytt ärende hamnar i `Dokument\Chainsaw\Ärenden\<ärendenummer>.cut`. Medan det är öppet ligger
  `<ärendenummer>.cut.work` (krypterad arbetskopia) och `<ärendenummer>.cut.lock` bredvid; båda försvinner när ärendet
  stängs.
- Filen börjar med `CHSWCASE` och ett kort JSON-huvud med ärendets id och kuverten (Base64). Inget annat i filen går att
  läsa: inte ärendenumret, operatörens namn eller ordet `SQLite`.
- Ett ärende går inte att öppna i två Chainsaw samtidigt (*Används redan*).
- SQLite3 Multiple Ciphers (`SQLite3MC.PCLRaw.bundle`) laddar sitt inbyggda bibliotek för win-x64 utan fel.
- En `.cut` som kopieras till en annan dator och öppnas med delningslösenordet går att läsa där.
- Antivirus eller OneDrive som låser `.cut` medan den sparas: sparningen misslyckas utan att filen förstörs, och
  arbetskopian tas tillbaka nästa gång ärendet öppnas.
