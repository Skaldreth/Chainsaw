# UTXO-modellen

> **Som i vardagen:** Bitcoin fungerar som sedlar i en plånbok, inte som ett bankkonto. Sedlarna går inte att riva
> sönder. Ska du betala 300 kronor med en femhundralapp lämnar du hela sedeln och får 200 tillbaka i växel. En
> bitcoin-tx gör samma sak: den lämnar hela "sedlar" och skapar nya, en till mottagaren och en med växeln tillbaka.

Bitcoin har inga konton med saldon. Kedjan består av **transaktioner** (tx), och varje tx gör två saker:

- **Inputs** spenderar tidigare **outputs**. En input pekar ut en output med tx:ens id (txid) och outputens nummer
  (`vout`), till exempel `a1a1…:1`.
- **Outputs** låser nya belopp till ett skript, oftast en adress.

En output som ännu inte har spenderats kallas **UTXO** (unspent transaction output). Det en adress "har" är summan av
dess UTXO:er; saldot finns inte på kedjan utan räknas fram.

## En output spenderas alltid helt

En input tar hela outputen. Vill avsändaren betala 0,3 BTC från en output på 1 BTC skapas två outputs: 0,3 BTC till
mottagaren och resten som **[växel](vaxeladress.md)** tillbaka till avsändaren, ofta till en ny adress. Därför har de flesta tx minst
två outputs, och därför är det en slutsats, aldrig ett faktum, vilken output som är växeln.

![En output på 1 BTC (den tjocka linjen) spenderas helt: 0,3 BTC går till mottagaren till höger, och resten kommer tillbaka som växel till en ny adress i samma plånbok. Växeln och plånbokens ram är streckade, eftersom de är slutsatser.](figur:utxo-vaxel)

## Avgiften står inte i någon output

Avgiften till den som bryter blocket är skillnaden mellan inputs och outputs:

> summan av inputs = summan av outputs + avgiften

![Inputs (överst) och outputs (underst) är lika långa. Den lilla streckade biten längst till höger är avgiften: den står inte i någon output, den är det som blir över.](figur:utxo-avgift)

Chainsaw kontrollerar att det går jämnt ut för varje tx som hämtas. Ett svar där det inte stämmer används inte (det
sparas ändå i ärendet).

## Coinbase

Den första tx:en i varje block är **coinbase**. Den har en enda input som inte spenderar något, och dess outputs är
blockbelöningen och blockets samlade avgifter. Här skapas nya bitcoin.

## Bekräftad och obekräftad

En tx som väntar i **mempoolen** är obekräftad och kan fortfarande bytas ut (till exempel med högre avgift) eller
försvinna. När den ligger i ett block är den bekräftad, och varje nytt block ovanpå ger en **bekräftelse** till.
Chainsaw hämtar en tx med minst sex bekräftelser bara en gång; en obekräftad tx eller en adress kan ändras och hämtas om.

![Till vänster mempoolen, där tx:en (accent) väntar bland andra. När den hamnar i ett block är den bekräftad, och varje block som läggs ovanpå ger en bekräftelse till (prickarna). Ju fler, desto säkrare: som ett kvitto som arkiveras under allt fler pärmar.](figur:utxo-bekraftelser)

Allt som bygger på en bekräftad tx visar **blockhöjd och blockhash**, så att varje steg kan kontrolleras mot kedjan
med vilket verktyg som helst.

## Att följa pengarna

- **Bakåt:** en inputs txid och `vout` visar varifrån pengarna kom.
- **Framåt:** kedjan själv säger inte vem som spenderar en output. Chainsaw frågar källan (`/outspends`) och visar
  den tx och input som spenderar den, när det finns en.
