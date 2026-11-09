# Zcash

> **Som i vardagen:** Zcash är en bank med två avdelningar. I den ena syns varje överföring på kontoutdraget, som
> i Bitcoin. Den andra är ett bankfack: man ser att någon går in med ett kuvert eller kommer ut med ett, men inte vad
> som händer därinne eller vems facket är.

**Zcash** har två delar. Den **genomskinliga delen** fungerar som Bitcoin, i **UTXO-modellen**: pengarna är mynt som
spenderas hela, och en tx har inputs och outputs (se [UTXO-modellen](utxo-modellen.md)). Den **skyddade delen**, de
skyddade poolerna, döljer avsändare, mottagare och belopp. Valutan heter **ZEC** och räknas i **zatoshi**, 10⁻⁸ ZEC.
Ett nytt block kommer ungefär var 75:e sekund.

## Adresserna

- **`t1…`** är en genomskinlig P2PKH-adress och **`t3…`** en genomskinlig P2SH-adress: Bitcoins `1…` och `3…` med två
  versionsbyte i stället för ett. Dem kan Chainsaw följa.
- **`zs1…`** (Sapling), **`u1…`** (en *unified address*, som kan rymma flera mottagare, ofta Orchard) och **`zc…`**
  (Sprout, den äldsta poolen) är **skyddade** adresser. De syns aldrig på kedjan; skrivs en sådan som startpunkt säger
  Chainsaw att spåret tar slut där.

## De skyddade poolerna

Zcash har tre skyddade pooler: **Sprout** (2016, stängd för nya pengar), **Sapling** (2018) och **Orchard** (2022). En
tx kan flytta pengar mellan den genomskinliga delen och en pool. På kedjan syns bara **hur mycket** som gick in eller
ut, i tx:ens *value balance*:

- **t→z, in i poolen:** de genomskinliga inputs betalar mer än de genomskinliga outputs och avgiften; resten gick in i
  poolen. Där **tar spåret slut**, med orsaken *Privacy coin*. Tx:en får en egen nod i grafen med slutet, och den
  genomskinliga växeln, om det finns någon, följs vidare.
- **z→t, ut ur poolen:** outputs får pengar som inte kom från några genomskinliga inputs. Tx:ens nod säger att pengarna
  kom ur poolen, och de kan inte följas bakåt därifrån.

Beloppen in och ut är **fakta** på kedjan; vem som äger vad i poolen syns inte. Chainsaw kan **föreslå** vägar ut
som har samma belopp minus avgift kort efter (se [Ut ur en skyddad pool](skyddade-pooler.md)).

## Var Chainsaw hämtar

Källan är **Blockbook**, Trezors öppna indexerare, hos Zelcore (`blockbook.zec.zelcore.io`), som svarar utan nyckel
(se [Källor](kallor.md)). En tx är två svar som båda förseglas i ärendet under kedjans eget namn *zcash-mainnet*:
Blockbooks, med vad varje input spenderar, och nodens eget (zcashd), med poolernas *value balance*. De måste stämma
med varandra, annars används inget av dem. Vem som spenderade en output läses ur adressens historik från tx:ens block.

Zcashs nyare tx-id är ett träd av BLAKE2b-hashar över tx:ens delar (ZIP 244), inte en dubbel SHA-256 av dess byte som
i Bitcoin, så Chainsaw kontrollerar inte en Zcash-tx mot dess egna byte och märker den aldrig så.

## Vad Chainsaw gör med Zcash

- En genomskinlig adress eller tx är en startpunkt som spåras i grafen, med FIFO eller haircut, i ZEC.
- Klustringen kör **common-input-ownership** och **växeladressen**; CoinJoin-mönstren, PayJoin och peel chain känner
  bara Bitcoins mönster och körs inte. En tx med flera lika stora utgångar slås därför inte ihop av sig själv utan
  föreslås, eftersom den kan vara en sammanslagning av främlingar.
- I *Nytt ärende* hämtas saldo, antal tx, aktivitet och blockhöjd i förväg.
- OFAC:s Zcash-adresser (*ZEC*) känns igen som sanktionerade.
- En adress kan **bevakas**, och en träff räknas i ZEC.
- Kurserna kommer från Bitstamps marknad *ZEC/EUR*, som bara har historik från mars 2026 och handlas tunt; saknas en
  kurs sägs det.
