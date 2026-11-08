# Utgången: Windows eller VPN

> **Som i vardagen:** varje uppslag Chainsaw gör är som ett brev till ett arkiv. Utgången är postkontoret du skickar
> det från. Skickar du från ett postbox-kontor (VPN) ser arkivet bara postboxens adress, inte var du sitter. Skickar du
> från kontorets egen brevlåda (Windows) går brevet samma väg som all annan post från huset, med husets skydd.

Allt Chainsaw hämtar från internet går genom **en enda väg ut**, utgången. Det finns ingen annan väg: en modul som
vill fråga en källa måste gå genom den, och utan utgång görs inget uppslag alls (princip 4).

## Två val

Under *Inställningar → Nätverk* (Ctrl+,) väljer du ett av två kort:

- **VPN:** uppslagen går genom en av Chainsaws egna VPN-utgångar, en WireGuard-tunnel som bara Chainsaw använder.
  Källan ser utgångens adress, inte din. Är ingen utgång ansluten görs **inga uppslag**. Se
  [VPN-utgångar och WireGuard](vpn-utgangar.md).
- **Windows:** uppslagen går genom datorns eget nät, med dess proxy och skydd. Det är rätt val när datorn redan har ett
  externt skydd för hela trafiken, och då ska Windows få sköta utgången.

Titelraden säger alltid vilken utgång som gäller: utgångens namn, eller *Windows*, med en grön prick när uppslag går.

## Vägen

Ett klick på utgången i titelraden fäller ut en ruta med **vägen** som en bild: datorn, utgången i en stor ring och
blockkedjorna.

![Vägen ut: genom en ansluten utgång (grön och hel), eller stoppad när den inte är det (röd och streckad)](figur:s-vagen)

- **Hel och grön:** uppslag går.
- **Streckad och röd:** inga uppslag går, till exempel när VPN är valt men ingen utgång är ansluten, eller när datorn
  inte når internet med Windows valt.

Under bilden står VPN-utgångarna med en rund knapp för att ansluta eller koppla ner, och längst ner *Inställningar för
anslutning*.

## Det som skickas

Oavsett utgång:

- **Bara https.** En adress med `http` vägras, så att ingen på vägen kan läsa vad som frågas.
- **Bara det uppslaget kräver:** adressen eller tx:en, eller marknaden och tiden för en kurs. Aldrig ärendet, numret
  eller något annat.
- **Inga kakor, ingen referer, inga omdirigeringar** och en så allmän webbläsaridentitet som möjligt, så att en källa
  inte kan känna igen Chainsaw eller koppla ihop uppslag.
- **Certifikat hämtas aldrig vid sidan av.** Saknar en källa ett certifikat i kedjan misslyckas uppslaget hellre än att
  datorn hämtar det själv runt utgången.

Varje förseglat råsvar säger vilken sorts utgång det kom genom, VPN eller Windows (princip 1).

## Varför det är byggt så

En källa som ser vem som frågar om en adress kan förstå att adressen är intressant, och i värsta fall varna ägaren.
Därför ska uppslag inte gå att koppla till dig eller din organisation, och därför går allt genom ett ställe som du har
valt och kan se.
