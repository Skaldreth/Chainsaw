# Litecoin

> **Som i vardagen:** Litecoin är samma sorts plånbok med sedlar som Bitcoin, tryckt av en annan centralbank. Sedlarna
> växlas på samma sätt, men en sedel i den ena valutan är aldrig en sedel i den andra. Litecoin har dessutom ett
> bakrum, MWEB, där det som händer inte syns utifrån.

**Litecoin** är en kedja i **UTXO-modellen**, som Bitcoin: pengarna är mynt som spenderas hela, och en tx har inputs
och outputs (se [UTXO-modellen](utxo-modellen.md)). Valutan heter **LTC** och räknas i **litoshi**, 10⁻⁸ LTC, som
satoshi. Ett nytt block kommer ungefär var 2,5 minut.

## Adresserna

Litecoin använder Bitcoins sorters skript men egna prefix:

- **`L…`** är P2PKH (Bitcoins `1…`).
- **`M…`** är P2SH (Bitcoins `3…`). Äldre plånböcker skrev P2SH med Bitcoins `3…`; samma skript har då två
  skrivsätt. Med kedjan utskriven (`litecoin:3…`) läser Chainsaw `3…` som Litecoins och visar alltid `M…`-formen, så
  att en adress aldrig blir två. Utan kedjan är en `3…`-adress Bitcoins.
- **`ltc1…`** är SegWit och Taproot (Bitcoins `bc1…`).

Samma hash på Bitcoin och på Litecoin är **två olika adresser**: en etikett, en sanktion eller en bevakning på den ena
kedjan gäller aldrig den andra.

## MWEB, den skyddade delen

**MWEB** (MimbleWimble Extension Blocks) är ett tillägg till Litecoin sedan 2022 där belopp och ägare inte syns. På
kedjan syns bara gränsen:

- **In i MWEB (peg-in):** en output till ett vittnesprogram av version 9 (`ltc1f…`). Chainsaw känner igen den ur
  skriptet, *MWEB peg-in*. Där tar spåret slut, med orsaken *skyddad pool*.
- **Blockets integrationstx (HogEx):** den sista tx:en i varje block tar emot blockets peg-ins och betalar ut det som
  lämnar MWEB (**peg-out**) till vanliga adresser. Dess egen adress är version 8 (`ltc1g…`), *MWEB HogAddr*.

Vad som hände inne i MWEB går inte att läsa. Chainsaw kan **föreslå** vilka peg-outs som kan höra till en peg-in, ur
belopp (minus avgift) och tid, alltid som streckade kandidater (se [Ut ur en skyddad pool](skyddade-pooler.md)).

## Var Chainsaw hämtar

Källan är **Esplora**, samma API som för Bitcoin, hos **litecoinspace.org** (se [Källor](kallor.md)). Allt som hämtas
förseglas i ärendet med blockhöjd och blockhash, under kedjans eget namn *litecoin-mainnet*, och det som ligger tolv
block djupt (en halvtimme) hämtas aldrig igen.

## Vad Chainsaw gör med Litecoin i dag

- En Litecoin-adress eller tx är en startpunkt som **spåras i grafen**, med FIFO eller haircut, i LTC; i *Nytt ärende*
  hämtas saldo, antal tx, aktivitet och blockhöjd i förväg.
- Klustringen kör **common-input-ownership** och **växeladressen**. CoinJoin-mönstren, PayJoin och peel chain känner
  bara Bitcoins mönster och körs inte på Litecoin; en tx med flera lika stora utgångar slås därför inte ihop av sig
  själv utan föreslås. Kontrollerna i grafen är Bitcoins, utom förslaget om vägar ut ur MWEB.
- OFAC:s Litecoin-adresser (*LTC*) känns igen som sanktionerade.
- En adress kan **bevakas**, också av bakgrunden, och en träff räknas i LTC.
- Kurserna kommer från Bitstamps marknad *LTC/EUR*.
