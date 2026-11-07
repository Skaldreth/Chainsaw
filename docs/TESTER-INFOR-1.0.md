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

- Ett nytt ärende hamnar i `%LOCALAPPDATA%\Chainsaw\Ärenden\<ärendenummer>.cut`. Medan det är öppet ligger
  `<ärendenummer>.cut.work` (krypterad arbetskopia), `.cut.work.base` och `.cut.lock` bredvid; alla försvinner när
  ärendet stängs.
- Ankarna hamnar i `%LOCALAPPDATA%\Chainsaw\Ankare\`, en fil per ärende med ett namn som inte visar ärendet.
- Filen börjar med `CHSWCASE` och ett kort JSON-huvud med ärendets id och kuverten (Base64). Inget annat i filen går att
  läsa: inte ärendenumret, operatörens namn eller ordet `SQLite`.
- Ett ärende går inte att öppna i två Chainsaw samtidigt (*Används redan*).
- SQLite3 Multiple Ciphers (`SQLite3MC.PCLRaw.bundle`) laddar sitt inbyggda bibliotek för win-x64 utan fel.
- En `.cut` som kopieras till en annan dator och öppnas med delningslösenordet går att läsa där.
- Låsning av valvet (Ctrl+L, Windows låses) medan ett ärende är öppet: låsskärmen kommer direkt, och ärendet sparas
  och stängs i bakgrunden.
- Antivirus som låser `.cut` medan den sparas: sparningen misslyckas utan att filen förstörs, och
  arbetskopian tas tillbaka nästa gång ärendet öppnas.

## Utgångarna (etapp 4, Network)

Utgångarna har inget gränssnitt än, och tunneln (Chainsaws eget exemplar av hjälpprogrammet för WireGuard) är inte
byggd. Det provas när tunneln och inställningarna för utgångar byggs.

- `vault.json` innehåller inget om utgångarna i klartext: inte namnet, platsen, id:t eller WireGuard-filens text.
- Att ansluta en utgång utan tunnel ger *misslyckades* (tunneln finns inte), och ett uppslag ger *Ingen utgång är
  ansluten* utan att något går ut på nätet (kontrollera med Resursövervakaren eller Wireshark att Chainsaw inte öppnar
  någon anslutning).
- När tunneln finns: med en ansluten utgång går uppslaget bara till tunnelns SOCKS5-port på `127.0.0.1`, och datorn
  gör ingen DNS-fråga om källans namn (Wireshark på det vanliga nätkortet visar ingenting från Chainsaw).
- En proxy i Windows inställningar eller i `HTTPS_PROXY` påverkar inte Chainsaws uppslag.
- Certifikatkedjor byggs utan nedladdningar och bara mot de rötter som redan finns i Windows certifikatarkiv. Kontrollera
  med Wireshark att uppslag mot en källa vars rot saknas lokalt inte får Windows att hämta roten från Windows Update
  (`ctldl.windowsupdate.com`) och inte hämtar AIA eller spärrlistor; uppslaget ska i stället misslyckas.
- En utgång vars WireGuard-fil saknar `DNS =` vägras när den läggs till.
- Rötterna på Windows: på en ny användarprofil (få rötter installerade) går uppslag mot vanliga källor att göra? Syns
  rötterna under *Tredjeparts rotcertifikatutfärdare* (AuthRoot) i det Chainsaw läser? Följs Windows egna
  begränsningar för en rot (spärrade certifikat i *Ej betrodda certifikat*, begränsade användningsområden,
  *distrust after*-datum)? Chainsaw tar bort rötter som ligger i *Ej betrodda certifikat*, men inte de andra.

