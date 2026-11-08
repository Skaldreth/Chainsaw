# Huvudnyckeln och valvet

> **Som i vardagen:** huvudnyckeln är nyckeln till ditt bankfack. Banken har inte någon kopia, och därför kan ingen
> annan öppna facket, men tappar du nyckeln kan inte heller banken hjälpa dig. I facket ligger de andra nycklarna du
> behöver: till ärendena, till källornas konton och till VPN-utgångarna.

**Valvet** är Chainsaws förvaring för det som är hemligt men inte hör till ett enskilt ärende. Det ligger krypterat i
`%LOCALAPPDATA%\Chainsaw\` och öppnas med **huvudnyckeln** när du låser upp Chainsaw.

## Det som ligger i valvet

- **Nycklarna till dina ärenden:** varje ärendefil har ett kuvert som öppnas med en nyckel ur valvet (se
  [Ärendefilen och krypteringen](arendefilen.md)).
- **API-nycklar** till källor, till exempel Chainabuse. De ligger aldrig i ett ärende.
- **VPN-utgångarna:** WireGuard-filerna och vilken utgång som är vald.
- **Din operatörsprofil:** namn, titel och enhet, som står som *vem* på allt du gör i ett ärende.
- Nycklarna till **etikettbiblioteket** och till **ankarna** som skyddar ärendenas hashkedjor.

## Huvudnyckeln lagras aldrig

![Huvudnyckeln lindar upp datanyckeln, och datanyckeln öppnar valvet](figur:s-huvudnyckel)

Chainsaw sparar inte huvudnyckeln, inte ens som ett fingeravtryck. I stället:

1. Valvet krypteras med en slumpad **datanyckel** (AES-256-GCM).
2. Datanyckeln **lindas in** med en nyckel som räknas fram ur huvudnyckeln (PBKDF2-SHA512 med 600 000 varv). De många
   varven gör varje gissning långsam, så att den som stjäl filen inte kan pröva miljontals lösenord.
3. När du låser upp räknas nyckeln fram igen, datanyckeln lindas upp och ligger i minnet **bara medan Chainsaw är
   upplåst**. Vid låsning nollställs den.

Fel huvudnyckel och en fil som ändrats går inte att skilja åt: båda säger att huvudnyckeln är fel.

## Välja och byta huvudnyckel

- Huvudnyckeln väljs i förstagångsinställningen och ska ha **minst 12 tecken**. En mening du minns är bättre än ett
  kort krångligt ord.
- **Byt** under *Inställningar → Profil* (Ctrl+,): *Byt …* fäller ut tre fält (nuvarande, ny, upprepa). Bara
  inlindningen av datanyckeln görs om, så bytet går fort och inga ärenden behöver skrivas om.
- Huvudnyckeln blir aldrig text som sparas: den läses direkt ur fältet och töms när den använts.

## Om du glömmer huvudnyckeln

Då går valvet **inte** att öppna, av någon. Det skapas på nytt, och API-nycklar och utgångar får läggas in igen. Dina
**ärenden** är däremot inte förlorade: varje ärendefil har ett kuvert för organisationens återställningsnyckel, så att
administratören kan öppna dem. Se [Återställningsnyckeln](aterstallningsnyckeln.md).

Se också [Låsning](lasning.md).
