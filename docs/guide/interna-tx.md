# Interna tx

> **Som i vardagen:** Du skickar ett paket till ett ombud, som skickar det vidare till någon annan. Ditt kvitto
> säger bara att paketet lämnades hos ombudet. Vart det tog vägen sedan står i ombudets egna papper. En intern tx är
> den vidaresändningen: den syns inte på kvittot, utan först när man går igenom vad ombudet gjorde.

En **intern tx** är ether som ett **kontrakt** skickar medan en tx körs, till exempel när en växlingstjänst (DEX)
betalar ut ether efter ett byte, eller när en plånbok som är ett kontrakt skickar vidare. Den har inget eget hash och
står inte i tx:ens fält eller logg.

## Varför de är lätta att missa

![Tx:en (till vänster) skickar ether till ett kontrakt. Kontraktet skickar dem vidare medan tx:en körs: en intern tx, inom den streckade ramen, som syns först när tx:en körs om.](figur:interna-tx)

Tittar man bara på tx:ernas `value` ser det ut som om pengarna stannade i kontraktet. Det är först när tx:en körs om
och varje anrop med ether följs (en *trace*) som det syns vart de tog vägen. Källan har gjort det arbetet, och
Chainsaw läser dess lista över interna tx för adressen eller tx:en.

## Det Chainsaw räknar

- Bara anrop som **lyckades** och som flyttade ether räknas; ett anrop som misslyckades flyttade ingenting.
- Varje intern tx har sin plats i tx:ens trace, så att två i samma tx hålls isär.
- Säger källan att den **inte har läst alla** interna tx än (det händer för nya block), sägs det, och svaret hämtas
  igen senare i stället för att sparas som slutligt.

Se [Kontomodellen](kontomodellen.md) för hur interna tx spåras som vilken överföring som helst.