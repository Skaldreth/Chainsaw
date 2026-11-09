# Kan frysas

> **Som i vardagen:** Ett presentkort kan spärras av butiken som gav ut det, var kortet än ligger. På samma sätt kan
> utgivaren av USDT och USDC spärra en adress i sin bokföring, så att det som ligger där inte går att flytta, oavsett
> vilken plånbok eller börs adressen hör till.

**USDT** (Tether) och **USDC** (Circle) är tokens vars utgivare kan **frysa** dem: kontraktet har en spärrlista, och
en adress på listan kan inte flytta sina tokens. Utgivarna fryser på begäran, ofta snabbt, och det gäller oavsett
vilken plånbok eller börs adressen hör till.

## Snöflingan

![USDT har kommit till adressen i mitten och en del ligger kvar, så den får en snöflinga. Utgivaren (uppe till höger) kan spärra det som ligger kvar; den prickade linjen visar vem som kan det, inte att något är fryst.](figur:kan-frysas)

En adress där USDT eller USDC **ligger kvar** får en liten **snöflinga** i hörnet. Inspektören listar saldot per token
och säger vem som är utgivare. Har adressen skickat vidare allt finns det inget att frysa, och då står ingen
snöflinga.

Saldot räknas bara för en adress vars historik är **expanderad**. För en adress som bara syns för att pengar gick
dit vet ärendet inte vad den har skickat vidare, så raden säger *Inte expanderad: saldot är okänt* och ingen snöflinga
sätts, eftersom den då skulle vila på en gissning. Ligger något kvar visar inspektören en ruta med vem som kan frysa
det.

## Vad snöflingan inte säger

- Att något **är** fryst. Den säger bara att utgivaren **kan** frysa det som ligger kvar.
- Att saldot är exakt. Det som ligger kvar räknas ur de överföringar ärendet har hämtat; har adressen tagit emot eller
  skickat mer än det Chainsaw har läst kan saldot vara ett annat. Kontrollera alltid saldot mot kedjan innan en
  begäran.

## Var frysning begärs

Frysning begärs hos **utgivaren** (Tether för USDT, Circle för USDC), inte hos börsen. Ligger pengarna på en börs
insättningsadress är det börsen som har kunden, och då kan en begäran om uppgifter gå till börsen samtidigt.

Snöflingan sätts bara efter **kontraktet**, aldrig efter namnet: en token som kallar sig USDT men har ett annat
kontrakt kan inte frysas av Tether. Se [ERC-20 och tokens](erc-20.md).
På **Solana** heter kontraktet en **mint**, och frysningen sker hos ett **tokenkonto** (se [Solana](solana.md)): mint-
kontona för USDT och USDC anger en *freeze authority*, och därför får de snöflingan också där. Ett tokenkonto som
redan är fryst sägs i saldot.
