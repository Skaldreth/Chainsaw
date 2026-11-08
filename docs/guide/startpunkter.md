# Startpunkter: hur Chainsaw känner igen en adress eller en tx

> **Som i vardagen:** Ett telefonnummer, ett postnummer och ett personnummer går att skilja åt bara på hur de ser ut:
> antalet siffror, var strecket står, vad de börjar med. Chainsaw känner igen en adress eller en tx på samma sätt, på
> formen, innan den frågar någon.

En utredning börjar i en **startpunkt**: en adress (till exempel den offret betalade till) eller en transaktion (till
exempel offrets betalning). Chainsaw känner igen vad du har skrivit på formen, utan att fråga någon källa:

- **Bitcoin-adress:** `1…` och `3…` (Base58Check) eller `bc1…` (Bech32 och Bech32m). Formen har en kontrollsumma, så ett
  felskrivet tecken upptäcks nästan alltid. Se [Adresstyper](adresstyper.md).
- **Bitcoin-tx:** 64 hexadecimala tecken (0–9, a–f), tx:ens id (txid).
- **Ethereum:** `0x` och 40 hexadecimala tecken är en adress, `0x` och 64 en transaktion.
- **TRON:** en adress börjar med `T` och är Base58Check, som Bitcoins äldre adresser, men med versionsbyten `0x41`.
  En TRON-tx är 64 hexadecimala tecken, precis som en Bitcoin-tx. Därför går den inte att skilja från en Bitcoin-tx på
  formen; den känns igen som TRON bara när kedjan står utskriven, som i länken `chainsaw:tron/<tx>`.

Bitcoin och Ethereum går att spåra. En startpunkt på TRON visas tonad med *inte byggt än*, och skrivs den i ett nytt
ärende sparas den i journalen så att den inte tappas bort.

En startpunkt läggs till i ärendet med en rad i journalen. Grafen börjar där, och det som går vidare därifrån hämtas
först när startpunkten expanderas.

**Länken `chainsaw:`** låter ett annat program öppna Chainsaw med en startpunkt: `chainsaw:bitcoin/<adress eller tx>`
eller `chainsaw:ethereum/<adress eller tx>`.
Länken gör aldrig något själv. Är Chainsaw låst väntar den tills det låses upp; sedan fyller den i Nytt ärende, eller
står i kommandoraden tills du trycker Enter.
