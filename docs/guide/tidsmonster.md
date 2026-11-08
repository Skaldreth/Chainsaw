# Tidsmönster

> **Som i vardagen:** en granne du aldrig har sett går ändå att lära känna lite: lampan tänds klockan sju, och efter
> midnatt är det alltid mörkt. Du vet inte vem det är, men du anar när han sover.

Människor skickar sina tx när de är vakna. Ser man **vilka timmar på dygnet** en ägare spenderar, och vilka timmar den
aldrig gör det, kan man ana när den sover, och därmed i vilken **tidszon** den bor.

## Hur Chainsaw läser det

Kontrollen `active-hours v1` tar varje ägare som [klustringen](common-input-ownership.md) räknar den, och tittar på de
bekräftade tx där ägaren spenderar. Har ägaren **minst tjugo** sådana tx, och finns det **minst sex timmar i rad**
utan någon, prövar Chainsaw om tystnaden kan vara slumpen: av 2 000 slumpade dygn med lika många tx vid slumpade timmar
får högst **en procent** en lika lång tystnad. Först då markeras ägaren med titeln *Aktiv kl. 08–22 UTC* och en trolig
tidszon, och *Varför?* säger hur ofta slumpen ger lika lång tystnad. Tidszonen räknas som att den tysta
perioden är natten, med mitten runt halv fyra på morgonen i ägarens tid.

## Det är en svag bedömning

- **Tiden är blockets**, inte när tx:en skickades. Ett block kommer minuter, ibland timmar, efter att tx:en sändes.
- **En tjänst eller ett skript** kan skicka åt ägaren, när som helst.
- **Klustret kan ha fler ägare**, och då blandas deras timmar.

Använd mönstret som stöd tillsammans med annat, till exempel för att se om två kluster kan vara samma person. Det är
aldrig ett faktum.
