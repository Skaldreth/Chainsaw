# Kan frysas

**USDT** (Tether) och **USDC** (Circle) är tokens vars utgivare kan **frysa** dem: kontraktet har en spärrlista, och en
adress på listan kan inte flytta sina tokens. Utgivarna fryser på begäran, ofta snabbt, och det gäller oavsett vilken
plånbok eller börs adressen hör till.

## Snöflingan

En adress där USDT eller USDC **ligger kvar** får en liten **snöflinga** i hörnet. Inspektören listar saldot per token
och säger vem som är utgivare. Har adressen skickat vidare allt finns det inget att frysa, och då står ingen snöflinga.

> Grafen för Ethereum är inte byggd än; snöflingan och saldot per token syns där när den är det.

## Vad snöflingan inte säger

- Att något **är** fryst. Den säger bara att utgivaren **kan** frysa det som ligger kvar.
- Att saldot är exakt. Det som ligger kvar räknas ur de överföringar ärendet har hämtat; har adressen tagit emot eller
  skickat mer än det Chainsaw har läst kan saldot vara ett annat. Kontrollera alltid saldot mot kedjan innan en begäran.

## Var frysning begärs

Frysning begärs hos **utgivaren** (Tether för USDT, Circle för USDC), inte hos börsen. Ligger pengarna på en börs
insättningsadress är det börsen som har kunden, och då kan en begäran om uppgifter gå till börsen samtidigt.

Snöflingan sätts bara efter **kontraktet**, aldrig efter namnet: en token som kallar sig USDT men har ett annat kontrakt
kan inte frysas av Tether. Se [ERC-20 och tokens](erc-20.md).
