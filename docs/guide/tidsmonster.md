# Tidsmönster

> **Som i vardagen:** en granne du aldrig har sett går ändå att lära känna lite: lampan tänds klockan sju, och efter
> midnatt är det alltid mörkt. Du vet inte vem det är, men du anar när han sover.

Människor skickar sina tx när de är vakna. Ser man **vilka timmar på dygnet** en ägare spenderar, och vilka timmar den
aldrig gör det, kan man ana när den sover, och därmed i vilken **tidszon** den bor.

## Hur Chainsaw läser det

Kontrollen `active-hours v2` tar varje ägare som [klustringen](common-input-ownership.md) räknar den, och tittar på två
serier för sig: de bekräftade tx där ägaren **spenderar**, och de där ägaren **tar emot** från andra. En serie med
**minst tjugo** tx prövas, och mönstret är den längsta tystnaden, **minst sex timmar i rad** utan någon tx.

- **Svensk tid med sommartid.** Timmarna läses i svensk tid (Europe/Stockholm). En vana som följer klockan flyttar sig
  en timme i UTC när klockan ställs om; i svensk tid står den still. Titeln säger till exempel *Aktiv kl. 09–23 svensk
  tid*. Chainsaw har ingen inställning för tidszon, och samma tidszon på alla datorer gör att samma ärende ger samma
  fynd.
- **Hur troligt slumpen är** räknas exakt: chansen att lika många tx vid slumpade timmar lämnar en lika lång tystnad.
- **Många serier prövas samtidigt**, och bland hundra ägare får någon ett mönster av slumpen. Därför behålls bara de
  serier som Benjamini–Hochbergs metod behåller med högst **en procent** falska fynd, och *Varför?* säger hur många
  serier som prövades.

För den som **spenderar** räknas en trolig tidszon som att den tysta perioden är natten, med mitten runt halv fyra på
morgonen. För den som **tar emot** säger tiderna i stället när de som betalar är aktiva, till exempel offer på samma
ort, eller när ägaren tar ut pengar till sig själv från en tjänst; titeln är då *Tar emot kl. …*.

## Det är en svag bedömning

- **Tiden är blockets**, inte när tx:en skickades. Ett block kommer minuter, ibland timmar, efter att tx:en sändes.
- **En tjänst eller ett skript** kan skicka åt ägaren, när som helst.
- **Klustret kan ha fler ägare**, och då blandas deras timmar.

Använd mönstret som stöd tillsammans med annat, till exempel för att se om två kluster kan vara samma person. Det är
aldrig ett faktum.
