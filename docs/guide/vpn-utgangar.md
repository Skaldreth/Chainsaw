# VPN-utgångar och WireGuard

> **Som i vardagen:** en VPN-utgång är en postbox i en annan stad. Breven du skickar därifrån bär postboxens adress, och
> svaren kommer tillbaka dit och vidare till dig i ett förseglat kuvert som ingen på vägen kan öppna.

En **VPN-utgång** är en krypterad tunnel från datorn till en server någon annanstans. Chainsaw använder
**WireGuard**, ett enkelt och modernt sätt att bygga sådana tunnlar. Tunneln tillhör bara Chainsaw: resten av datorn
går ut som vanligt, och Chainsaw är inte beroende av något annat program.

## Lägga till en utgång

Utgången kommer som en **WireGuard-fil** (`.conf`) från den som driver VPN-tjänsten.

1. Öppna *Inställningar → Nätverk* (Ctrl+,) och välj kortet **VPN**.
2. Välj *Lägg till WireGuard-fil …* och filen.
3. Chainsaw kontrollerar filen och säger *Giltig* eller vad som är fel. Filen måste ha en rad `DNS =`, så att namn kan
   slås upp inne i tunneln och aldrig på datorns eget nät.
4. *Spara* lägger utgången **krypterad i valvet**. Filens innehåll står aldrig i klartext på disken.

Förstagångsinställningen har samma steg, och det går att hoppa över och göra senare.

## Ansluta

Klicka på utgången i titelraden, eller öppna Nätverk i inställningarna, och tryck på den runda knappen vid utgången.
Pricken är grå (inte ansluten), gul (*Ansluter …*), grön (*Ansluten*) eller röd med orsaken. Bara en utgång är ansluten
åt gången; att ansluta en annan kopplar ner den förra.

## Så fungerar tunneln

Chainsaw har ett eget litet hjälpprogram för tunneln, en process per tunnel:

- Det startas bara om dess fingeravtryck (SHA-256) är det Chainsaw byggdes med, så att ett utbytt program aldrig körs.
- Det får sin konfiguration direkt ur valvet, utan att den skrivs till disk.
- Det tar bara emot trafik från Chainsaw på datorn själv (`127.0.0.1`), med ett slumpat användarnamn och lösenord per
  tunnel.
- Namn slås upp inne i tunneln.
- Det stoppar när Chainsaw stänger.

Det som inte är byggt än: att kontrollera vilken adress utgången syns som, och att ta bort eller byta namn på en
utgång i rutan.

Se [Utgången: Windows eller VPN](utgangen.md) för vad som skickas och varför.
