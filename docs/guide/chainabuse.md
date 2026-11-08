# Chainabuse

> **Som i vardagen:** Chainabuse är som en anslagstavla där folk sätter upp varningar: *jag blev lurad av den här
> adressen*. Det är värdefullt att läsa, men det är vad någon har sagt, inte vad någon har bevisat.

**Chainabuse** är en öppen tjänst där allmänheten och partner rapporterar adresser som använts vid bedrägerier,
utpressning och andra brott. Den drivs av TRM Labs, som samarbetar med brottsbekämpande myndigheter. Chainabuse är
Chainsaws första **underleverantör** av etiketter.

## Slå på

1. Skaffa en API-nyckel från Chainabuse.
2. Öppna *Inställningar → Källor* (Ctrl+,) och välj **Chainabuse** under *Etiketter*.
3. Slå på brytaren, skriv nyckeln och tryck Enter (eller *Spara*). Nyckeln läggs i **valvet** och visas aldrig igen i
   klartext. *Ta bort* tar bort den.

Raden säger *Av*, *På · ingen nyckel* (gul prick) eller *På* (grön prick).

## Fråga om en adress

Välj en nod med en adress och tryck **R**, eller *Fråga om rapporter* (skölden) i ikonlisten. Knappen visas bara när
Chainabuse är på och Chainsaw är online, och inte på ett helt kluster.

![Rapporter från Chainabuse: frågat för en adress genom utgången, sparat som en röd markering med källa](figur:s-chainabuse)

- Bara **adressen** skickas, genom utgången, aldrig ärendet.
- Svaret förseglas i ärendet med tider, vem som frågade och SHA-256, också när det blev fel.
- Finns rapporter blir de en etikett **Rapporterad** per sorts brott, i rött som risk, med Chainabuse som källa. Noden får
  en röd markering, och etiketten står i nodens historik.
- Svaret kommer som en toast.

En vanlig nyckel ger bara ett fåtal anrop i månaden. Därför frågar Chainsaw bara när du ber om det.

## Läs rapporterna rätt

En rapport är en **bedömning med källa** (princip 2). Den kan vara fel, den kan gälla en adress som en börs delar mellan
många kunder, och den som rapporterade kan ha blandat ihop adresser. *Rapporterad* avslutar därför aldrig spåret. Den
säger att någon har pekat ut adressen, och ger dig ett skäl att titta närmare.

En rapport kan tas tillbaka som en egen etikett, och tillbakatagandet följer med till biblioteket.

Att fråga automatiskt, och Chainabuse för TRON, är **inte byggt än**.
