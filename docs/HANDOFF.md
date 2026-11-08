# Chainsaw – överlämning

Senast uppdaterad: 2026-11-08.

Läget och nästa steg, kort. Specifikation, arkitektur och arbetssätt står i `CLAUDE.md`, vägen till 1.0 i `docs/TODO.md`, det som kommer efter i `docs/BACKLOG.md`,
och vad varje etapp byggde i `docs/DEV-LOG.md`.

## Läget

- **Etapperna 1–27 är byggda**:
  - Alla moduler, skalet SH1b och grafen med inspektören, kontrollerna och förslagen (KF2b) och bevakningen, också
    i bakgrunden.
  - Ethereum med ERC-20, källornas endpoints, Chainabuse som underleverantör och Guiden.
- **Byggt 2026-11-08 och ihopslaget** på grenen `claude/busy-cray-wj01k4`:
  - **Agenternas delar:** NC2a, GR7a, TL1a, KN1a, SO1a, BV1a, TO1a och mixeranalyserna (*Delarna tillsammans*,
    *Mixer efter mixer*).
  - **Grafen:** *Sortera grafen*, mjuk zoom, inspektörens glidning och ×, och rutmarkering med grupper som dras.
  - **Guiden:** centrerad, med 33 nya avsnitt och Esc tillbaka.
- **Senaste körningen:** bygget har 0 varningar, och testerna är gröna (App 1 025, Tracing 205, Checks 91,
  SharedKernel 65, TestData 19).
- **Inget är provat på Windows.**

## Nästa steg

**Arbeta efter `docs/TODO.md`, *Vägen till 1.0*,** i den ordningen. Svar på frågorna under *Väntar på dig* skrivs in
där de hör hemma (CLAUDE.md eller TODO) och stryks ur listan.

1. **Provet på Windows:** användaren provar, och det som inte fungerar rättas först.
2. **Resterna i det byggda:** de flesta är märkta [direkt] och kan byggas parallellt av agenter i worktrees.
3. **Godkänt men inte byggt:** först Begäran (BG1b), som en etapp med granskning.
4. **Skissas först:** vyerna och rapporten, efter användarens svar om vad 1.0 omfattar.

## Agenter och canvas

- **Agenter:**
  - De arbetar i worktrees under `.claude/worktrees/`, på grenar som heter `worktree-agent-*`.
  - Starta dem inifrån `/home/user/Chainsaw`, annars går det inte att skapa worktreen. De committar men pushar inte.
  - Huvudsessionen slår ihop med `git merge`, löser dokumentkonflikter genom att behålla båda sidor, bygger, kör de
    berörda testprojekten och pushar.
  - Det som skriver i ärendet granskas av en oberoende agent.
- **Canvasen:** https://claude.ai/artifact/LVMLEqgJteM68jjc7vuq1h.
  - Läs in den levande `project/canvas.json` med Artifact `read` innan något läggs ut, och slå ihop.
  - Skisserna och det gemensamma uppdraget ligger i `.claude/sketches/`, som inte är i git och hämtas om ur canvasen.
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
6. Skriv en post i `docs/DEV-LOG.md` (egna beslut, avsteg, det som återstår), lägg etappen under *Läget* ovan,
   lägg öppna punkter i `docs/TODO.md` och det som ska provas på Windows i
   `docs/TESTER-INFOR-1.0.md`.
7. Committa med ett beskrivande meddelande och pusha till `claude/busy-cray-wj01k4`.
8. Skicka en rapport till huvudsessionen med `send_message` (session `session_013CLefr2kGog3dCFdfqN1yx`): vad som byggts,
   antal tester, granskningens resultat och commit. Stötte etappen på ett beslut som bara användaren kan ta: välj det
   säkraste, skriv det i TODO under *Väntar på dig* och säg det i rapporten.
