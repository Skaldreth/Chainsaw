# Begäran om uppgifter

> **Som i vardagen:** att begära uppgifter från en börs är som att fråga en bank vem som äger ett konto. Banken svarar
> bara om frågan är exakt: vilket konto, vilken insättning, vilken dag och vilket belopp. En vag fråga får ett vagt svar,
> eller inget alls.

När spåret har nått en börs är nästa steg att fråga börsen vem kunden är. Det kallas en **begäran om uppgifter**.

![Från spåret till en begäran: insättningen på börsen, med tx, tid och belopp, i ett dokument](figur:s-begaran)

## Det en begäran behöver

För varje insättning:

- **Adressen** där pengarna kom in, hela, aldrig förkortad.
- **Tx:en** (txid) och **kedjan**.
- **Tiden** för blocket, i UTC och i lokal tid.
- **Beloppet** i kryptovaluta, och gärna värdet i kronor vid tiden med kursens källa (se [Kurser](kurser.md)).
- **Blockhöjden**, så att börsen kan kontrollera mot kedjan.

Och sedan frågorna: vem kontot tillhör (namn, födelsedatum, adress), vilka identitetshandlingar som visats, vilka
bankkonton och kort som är kopplade, inloggningar med IP-adress och tid, och vart pengarna togs ut.

## Det en begäran inte ska innehålla

En börs är en tredje part. Den ska få veta det den behöver för att svara, inte mer:

- aldrig **offrets namn** eller andra uppgifter om offret;
- aldrig dina anteckningar eller bedömningar om andra aktörer i ärendet;
- aldrig sådant som kan varna kunden, om börsen skulle berätta för den.

## I Chainsaw

Vyn **Begäran** (Ctrl+5) är **inte byggd än**. Den ska lista alla uttag som spårade pengar har nått i ärendet, visa
begäran som en förhandsvisning och exportera den som en Markdown-fil, med bara det begäran behöver. Exporten ska
journalföras med SHA-256 och aldrig skriva över en tidigare fil.

Tills dess hittar du uppgifterna i grafen: välj linjen in till börsen, så står tx:erna med block i inspektören, och
*Kopiera* tar hela adressen. Se [Inspektören, ikonlisten och Varför?](inspektoren.md).

Varför det spelar roll att uppgifterna kommer ur ärendet: varje uppgift i begäran ska gå att kontrollera mot kedjan och
mot det råsvar den kom ifrån (princip 1), så att ett fel i en adress eller en tid aldrig skickar börsen till fel kund.
