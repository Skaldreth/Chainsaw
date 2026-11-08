# Ärendefilen och krypteringen

> **Som i vardagen:** ärendefilen är en låst pärm med ett kuvertfack på utsidan. Pärmen öppnas med en enda nyckel, men
> den nyckeln ligger aldrig löst: en kopia ligger i ett förseglat kuvert för dig, och kanske ett för en kollega och ett
> för organisationens administratör. Vem som helst kan se att det finns kuvert, men bara den som har rätt nyckel till
> sitt kuvert kommer åt pärmens nyckel.

Varje ärende är **en fil**, `<ärendenummer>.cut`. Allt ligger i den: det som hämtats om kedjan, kurserna, kluster,
etiketter, grafens läge, bevakningar och journalen. Inget om ärendet ligger någon annanstans, och inget skickas till
molnet (princip 4).

![Ärendefilen: ett öppet huvud med nyckelkuverten och det krypterade ärendet bakom](figur:s-arendefilen)

## Huvudet och kuverten

Filen börjar med ett litet **huvud** som inte är krypterat. Där står ärendets id och **nyckelkuverten**, men inget om
vad ärendet handlar om, inte ens numret eller vems det är.

- **Filnyckeln.** Varje ärende har en egen slumpad nyckel som krypterar resten av filen. Den står aldrig i klartext.
- **Ditt kuvert.** Filnyckeln inlindad med en nyckel ur ditt valv. Därför öppnas dina ärenden utan att du skriver
  något mer än huvudnyckeln, när du låser upp Chainsaw (se [Huvudnyckeln och valvet](huvudnyckeln.md)).
- **Delningskuvertet**, om du slog på *Kan öppnas av en kollega* när ärendet skapades: filnyckeln inlindad med ett
  **ärendelösenord** som du lämnar till kollegan på annan väg än filen. Lösenordet ska ha minst 12 tecken.
- **Organisationens kuvert:** filnyckeln inlindad med organisationens publika återställningsnyckel, så att
  administratören kan öppna ärendet om huvudnyckeln glöms. Se [Återställningsnyckeln](aterstallningsnyckeln.md).

Kuvert av en sort som Chainsaw inte känner till lämnas orörda, så att en nyare version aldrig förlorar något.

Att öppna en kollegas fil med ärendelösenordet i Chainsaw är **inte byggt än**; delningskuvertet läggs redan i filen.

## Arbetskopian

Medan ärendet är öppet arbetar Chainsaw i en krypterad **arbetskopia** bredvid filen (`.cut.work`). Den skrivs tillbaka
till ärendefilen när du sparar och stänger, genom en tillfällig fil som flyttas på plats, så att ett avbrott aldrig
lämnar en halv fil. Kraschar datorn tas arbetskopian tillbaka nästa gång ärendet öppnas, med en rad i journalen. Hör
den inte till filen som den ser ut nu lämnas båda orörda, så att inget skrivs över.

## Varför det är byggt så

- **Ett ärende ska kunna lämnas vidare** som en enda fil, och ändå vara oläsligt för den som inte ska läsa det.
- **En nyckel per ärende** gör att ett lösenord till ett ärende inte öppnar något annat.
- **Huvudet säger så lite som möjligt**, eftersom det måste kunna läsas innan något är upplåst.
- **Allt som hämtats ligger i filen**, så att ärendet fungerar utan nät (se [Utan nät](offline.md)) och så att varje
  slutsats går att kontrollera i efterhand (se [Journalen och hashkedjan](journalen.md)).
