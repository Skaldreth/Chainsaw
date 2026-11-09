# Chainsaw – överlämning

Senast uppdaterad: 2026-11-09.

Läget och nästa steg, kort. Specifikation, arkitektur och arbetssätt står i `CLAUDE.md`, vägen till 1.0 i
`docs/TODO.md`, det som kommer efter i `docs/BACKLOG.md`,
och vad varje etapp byggde i `docs/DEV-LOG.md`.

## Läget

- **Etapperna 1–27 är byggda**, och allt som valdes på canvasens sida *Mot 1.0* (2026-11-08): Begäran, vyerna
  Etiketter, Exponering, Rapport, Tidslinje och Flöde, grafens IN4a, TL2a och GR9a, kurserna via Riksbanken,
  inställningarna, ärendets kuvert och sökningen, och sedan SW1a, DX1b, PL1b, KV1a, HB1a, BU1a, DM1a, HP1a, PX1a,
  UT4a, KL3b, TJ1b, VT1a, LN1a och AK1a.
- **2026-11-09:**
  - Kedjorna TRON, Litecoin, BSC, Solana och Zcashs genomskinliga del, och den egna noden (Bitcoin Core och electrs).
  - Analyserna `pattern v1` och `cross-chain v1`, Checks omgång 5 och `round-fiat v1`.
  - En borta tx på Ethereum och ur mempoolen (`mempool-gone v1`), HB1a av sig själv, och besluten 12–19.
  - Testärendet med omgång 5:s mönster och Litecoin, Zcash, BSC och Solana.
- Varje etapp som skriver i ärendet är granskad oberoende; de mindre fynden står i `docs/BACKLOG.md`.
- **Senaste körningen** (2026-11-09): bygget har 0 varningar, och alla 16 testprojekt är gröna. Några tester i App och
  Pricing är instabila i tid (BACKLOG).
- **Windows:** bygget och testsviten är gröna där (2026-11-08); appen är inte provad för hand.
- **Inte byggt:** Monero, resten av Ethereum (kontoheuristikerna, kontrollerna, Etherscan med nyckel); båda väntar på
  användarens klartecken.

## Nästa steg

**Arbeta efter `docs/TODO.md`, *Vägen till 1.0*,** i den ordningen. Svar på frågorna under *Väntar på dig* skrivs in
där de hör hemma (CLAUDE.md eller TODO) och stryks ur listan.

1. **Provet på Windows:** användaren provar, och det som inte fungerar rättas först.
2. **Skisserna som väntar på val:** 14 skisser (OM1, LK1, RS1, SP1, YT1, UT5, BV5, KB1, CJ1, TE1, MS1, LS1, FR1, AV1)
   på canvasens sida *Mot 1.0*, en per [skiss]-punkt i TODO.
3. **Resterna** märkta [direkt] i TODO, och Monero och Ethereum när användaren säger till.
4. **Sparsamt med agenter** (användarens beslut 2026-11-09): inga nya agenter utan att användaren ber om det,
   granskning bara för det som skriver i ärendet, högst ett par omgångar, och M-fynd till BACKLOG.

## Agenter och canvas

- **Agenter:**
  - De arbetar i worktrees under `.claude/worktrees/`, på grenar som heter `worktree-agent-*`.
  - Starta dem inifrån `/home/user/Chainsaw`, annars går det inte att skapa worktreen. De committar men pushar inte.
  - Huvudsessionen slår ihop med `git merge`, löser dokumentkonflikter genom att behålla båda sidor, bygger, kör de
    berörda testprojekten och pushar.
  - Det som skriver i ärendet granskas av en oberoende agent.
- **Canvasen:** https://claude.ai/artifact/LVMLEqgJteM68jjc7vuq1h.
  - Läs in den levande `project/canvas.json` med Artifact `read` innan något läggs ut, och slå ihop.
  - Den lokala mappen `.claude/sketches/` är inte i git och kan vara gammal; canvasen är källan.
  - Provrendering: lägg `artifact-type/dc-runtime.js` som `support.js` bredvid tavlorna och servera mappen över http.

## Så byggs etapperna

1. Läs `CLAUDE.md`, denna fil och `docs/DEV-LOG.md`. Installera .NET med `apt-get update && apt-get install -y
   dotnet-sdk-10.0`. Hämta grenen `claude/busy-cray-wj01k4` och arbeta på den.
2. Bygg bara etappens innehåll. Modulär monolit: publika typer i `*.Contracts`, `internal` i implementationen, moduler
   beror bara på varandras Contracts. Varje typ och metod har XML-dokumentation (också `internal` och `private`). Koden
   och kommentarerna på engelska, professionell C#, rader under ungefär 120 tecken.
3. Skriv tester för allt som byggs (xUnit, i modulens testprojekt). Heuristiker testas mot ett facit av syntetiska tx.
   Inga riktiga adresser eller ärenden i testerna; nätet används aldrig i testerna.
4. `dotnet build Chainsaw.slnx` utan varningar och `dotnet test Chainsaw.slnx` gröna.
5. **Oberoende granskning:** starta en granskande agent (Agent-verktyget) som läser diffen mot principerna i CLAUDE.md
   och letar fel, och märker fynden C (kritiskt), I (viktigt) eller M (mindre). Rätta alla C och I; M rättas eller skrivs
   i `docs/BACKLOG.md`. Granska igen tills inga C eller I återstår.
6. Skriv en post i `docs/DEV-LOG.md` (egna beslut, avsteg, det som återstår), lägg etappen kort under *Läget* ovan,
   lägg öppna punkter i `docs/TODO.md` och det som ska provas på Windows i
   `docs/TESTER-INFOR-1.0.md`.
7. Committa med ett beskrivande meddelande och pusha till `claude/busy-cray-wj01k4`.
8. Skicka en rapport till huvudsessionen med `send_message` (session `session_013CLefr2kGog3dCFdfqN1yx`): vad som byggts,
   antal tester, granskningens resultat och commit. Stötte etappen på ett beslut som bara användaren kan ta: välj det
   säkraste, skriv det i TODO under *Väntar på dig* och säg det i rapporten.
