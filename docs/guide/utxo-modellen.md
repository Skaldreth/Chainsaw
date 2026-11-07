# UTXO-modellen

Bitcoin har inga konton med saldon. Kedjan består av **transaktioner** (tx), och varje tx gör två saker:

- **Inputs** spenderar tidigare **outputs**. En input pekar ut en output med tx:ens id (txid) och outputens nummer
  (`vout`), till exempel `a1a1…:1`.
- **Outputs** låser nya belopp till ett skript, oftast en adress.

En output som ännu inte har spenderats kallas **UTXO** (unspent transaction output). Det en adress "har" är summan av
dess UTXO:er; saldot finns inte på kedjan utan räknas fram.

## En output spenderas alltid helt

En input tar hela outputen. Vill avsändaren betala 0,3 BTC från en output på 1 BTC skapas två outputs: 0,3 BTC till
mottagaren och resten som **växel** tillbaka till avsändaren, ofta till en ny adress. Därför har de flesta tx minst
två outputs, och därför är det en slutsats, aldrig ett faktum, vilken output som är växeln.

## Avgiften står inte i någon output

Avgiften till den som bryter blocket är skillnaden mellan inputs och outputs:

> summan av inputs = summan av outputs + avgiften

Chainsaw kontrollerar att det går jämnt ut för varje tx som hämtas. Ett svar där det inte stämmer används inte (det
sparas ändå i ärendet).

## Coinbase

Den första tx:en i varje block är **coinbase**. Den har en enda input som inte spenderar något, och dess outputs är
blockbelöningen och blockets samlade avgifter. Här skapas nya bitcoin.

## Bekräftad och obekräftad

En tx som väntar i **mempoolen** är obekräftad och kan fortfarande bytas ut (till exempel med högre avgift) eller
försvinna. När den ligger i ett block är den bekräftad, och varje nytt block ovanpå ger en **bekräftelse** till.
Chainsaw hämtar en tx med minst sex bekräftelser bara en gång; en obekräftad tx eller en adress kan ändras och hämtas om.

Allt som bygger på en bekräftad tx visar **blockhöjd och blockhash**, så att varje steg kan kontrolleras mot kedjan
med vilket verktyg som helst.

## Att följa pengarna

- **Bakåt:** en inputs txid och `vout` visar varifrån pengarna kom.
- **Framåt:** kedjan själv säger inte vem som spenderar en output. Chainsaw frågar källan (`/outspends`) och visar
  den tx och input som spenderar den, när det finns en.
