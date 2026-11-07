# Att prova på Windows före 1.0

Det som är byggt men inte kan provas i molnmiljön (Linux), per område: vad man gör och vad som ska hända.

## Start

- `dotnet run --project src/Chainsaw.App`: fönstret öppnas mitt på skärmen, mörkt, med egen titelrad och utan fel.

## Skalet (etapp 1, SH1b)

- Titelraden: kättingloggan (två länkar i varandra, diagonalt) är skarp i 16 px och snitten syns, *Chainsaw*, *Inget ärende öppet* och *Ingen utgång* med grå prick.
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


## Bitcoin-källan (etapp 5, Chains)

Chains har inget gränssnitt än. Det provas när kommandoraden eller grafen kan slå upp en adress, och först med en
ansluten utgång.

- Ett uppslag mot mempool.space och ett mot blockstream.info (bas-URL i inställningarna) ger samma tx med samma
  belopp, avgift, blockhöjd och blockhash som webbplatsernas egna sidor.
- En adress med många tx bläddras sida för sida tills historiken tar slut, och antalet stämmer med webbplatsen.
- En obekräftad tx visas som obekräftad, och efter att den bekräftats visar nästa uppslag blocket.
- Utan utgång: ett ärende med hämtade tx öppnas och visar dem, och det som inte är hämtat säger *Ingen utgång är
  ansluten* utan att något går ut på nätet.
- `/verify` på ett ärende med hämtade svar är grönt, och svarens SHA-256 stämmer med `sha256sum` av samma svar hämtat
  med `curl` genom samma utgång (så länge svaret inte har ändrats).

## Klustringen (etapp 6, Clustering)

Klustringen har inget gränssnitt än. Det provas när grafen kan hämta tx och visa kluster, helst tillsammans med
kryptospåraren.

- En känd Whirlpool-mix, en Wasabi 2-runda och en JoinMarket-tx känns igen som CoinJoin, spåret tar slut där, och
  ingen av deras inputs slås ihop.
- En börs sweep slår ihop insättningsadresserna, men kunderna som betalade in hamnar inte i börsens kluster.
- En batchutbetalning från en börs gör ingen kund till en del av börsen.
- En känd peel chain känns igen när minst tre hopp är hämtade, och bara de stora outputarna slås ihop.
- *Varför?* på varje adress i ett kluster visar heuristik, version och tx, och tx:en går att slå upp i en annan
  blockutforskare.
- En uppbrytning står kvar efter att ärendet stängts och öppnats, och efter att samma tx hämtats och analyserats igen.
- Jämför några kluster med kryptospårarens verktyg: slår Chainsaw ihop för mycket eller för lite? Skriv ner varje
  skillnad, så att tröskelvärdena kan prövas (TODO, *Väntar på beslut*).
- `/verify` är grönt efter klustring och uppbrytningar.

## Spårningen (etapp 7, Tracing)

Spårningen har inget gränssnitt än. Det provas när grafen (GR4) är byggd, helst tillsammans med kryptospåraren och ett
ärende där svaret redan är känt.

- Startpunkten (en tx eller en adress) står i första kolumnen, och varje expansion lägger till ett hopp; inget annat
  växer i grafen.
- Att öppna ett ärende och visa grafen skickar ingenting ut på nätet (kontrollera utgångens logg). Ett mynt som låg
  kvar visas som *kan ha flyttats* tills noden expanderas igen.
- FIFO och haircut ger samma belopp som kryptospårarens verktyg på samma spår, eller så går skillnaden att förklara
  (avgifter, ordning av inputs, avrundning). Skriv ner varje skillnad.
- Summan av alla slut är exakt det spårade beloppet, på satoshin, med avgifterna som eget slut.
- Spåret slutar vid en känd CoinJoin med orsaken CoinJoin, och linjen in i mixen visar beloppet.
- En adress utan expansion visas som *Inte expanderad ännu*, aldrig som ett slut.
- En länk till en tx på Ethereum eller TRON visas som en lila nod och står kvar efter att ärendet stängts och
  öppnats; tas den tillbaka står den kvar förseglad, och `/verify` är grönt.
- Nodernas platser i fri yta och i hoppkolumner står kvar efter att ärendet stängts och öppnats.

