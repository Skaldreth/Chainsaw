# Frågor till kryptospåraren

Det här är frågorna där Chainsaw behöver en erfaren kryptospårares svar. Under varje fråga står vad Chainsaw gör i dag,
så att det går att svara *ja, så* eller *nej, så här i stället*. Svaren skrivs in i `CLAUDE.md` eller `docs/TODO.md`,
och frågan tas bort härifrån.

## Spårningen

1. **Vilken spårningsmetod används i praktiken?** Chainsaw kan FIFO och haircut, och har plats för LIFO och poison.
   - Vilken ska vara förval?
   - Används LIFO? I så fall hur: inom en tx är ordningen redan FIFO:s, så LIFO gäller en adress saldo över tid.
   - Används poison? Den ger inga belopp som går jämnt ut, så Chainsaw skulle visa den som *berört* i stället för ett
     belopp.
2. **Vilka mynt börjar spåret i?** I dag spårar grafen från alla mynt som startadressen tagit emot, och från en
   startpunkts-tx:s betalningar till andra än betalarna. Vill man hellre välja själv, till exempel bara offrets
   betalningar eller ett visst belopp?
3. **Genom en växling** (till exempel USDT som byts till ETH i en DEX): ska spåret följa beloppet vidare i den nya
   tokenen, efter kursen i själva tx:en? I dag visar Chainsaw bytet som ett piller på linjen, men spårar varje token för
   sig.
4. **Adresser med lång historik:** en expansion hämtar högst 50 tx per adress. En adress med fler blir delvis läst, och
   spåret stannar där. Hur gör ni: hämtar ni allt, tar ett tidsfönster runt händelsen, eller stannar ni och bedömer?

## Klustringen

5. **Gränsvärdena för växeln och peel chain.** I dag:
   - **Växeln** slås ihop automatiskt när minst tre tecken talar för den och inget emot, i en tx med två outputs.
   - **En peel chain** kräver tre kända hopp, och att det som går vidare är minst fyra gånger det som skalas av.
   - **Ett jämnt belopp** är en multipel av 0,001 BTC.
   - **En möjlig CoinJoin** (två lika stora outputs, eller lika jämna belopp utan känt mönster) gör allt i tx:en till
     förslag i stället för sammanslagningar.

   Känns det rimligt?
6. **Vad skiljer en riktig peel chain** från någon som bara gör vanliga betalningar? Chainsaw föreslår i dag bara, och
   slår inte ihop. Är det samma skripttyp i varje hopp, en liknande andel som skalas av, eller kort tid mellan hoppen?
   Sedan 2026-11-08 följs också hopp som fylls på med fler inputs, och kedjor där den mindre outputen går vidare.
6b. **De nya kontrollernas gränser** (2026-11-08). Stämmer de med det ni ser?
   - **CoinJoin:** Whirlpools pooler 0,001, 0,01, 0,025, 0,05, 0,25 och 0,5 BTC (0,025 och 0,25 är Ashigarus, som
     bör kontrolleras mot en aktuell källa); Wasabi 2 när minst hälften av utgångarna har standardbelopp och minst
     två av beloppen är potenser av 2 eller 3.
   - **Tjänst ur beteende:** minst två svep till samma adress, minst tio insättningsadresser från minst tio olika
     avsändare, utspridda över minst ett dygn, och aldrig adresser som en startpunkt eller ett offer betalade; en batch
     är minst åtta mottagare från högst två adresser.
   - **Uppdelning:** minst fem delar med högst tio procents skillnad; delarna följs högst tre mellanled.
   - **Många hopp:** minst tre hopp i rad, högst två procent bort per hopp; fördröjning från tre dygn.
   - **Damm:** högst 1 000 satoshi till minst tio adresser; förgiftning när fyra tecken i början och slutet är lika och
     beloppet är högst 10 000 satoshi.
   - **Tidsmönster:** minst tjugo tx och sex tysta timmar i rad, och högst en procents chans att slumpen ger lika
     lång tystnad; natten antas ha sin mitt kl. 03.30.
   - **Växeln:** fingeravtrycket (version, låsning, RBF) är ett av sex tecken och räknas aldrig för att slå ihop.
   - **PayJoin:** sammanslagningarna blir förslag bara vid starka tecken (två eller tre inputs, mottagarens input av en
     egen adresstyp som en output har, den outputen större än inputen, och inputen onödig). Missar det PayJoins ni ser?
   - **Damm:** 330 och 546 satoshi sägs kunna vara inskriptioner.

## Mixers och förvirring

7. **Gränsvärdena för mixerns troliga utgångar.** I dag:
   - **Avgiften** får vara 0–10 %, och ger full poäng mellan 0,5 och 5 %.
   - **Tiden:** högst 7 dygn efter insättningen.
   - **Poängen** väger belopp 0,5, tid 0,3 och adresstyp 0,2.
   - **Kandidaterna:** de under 0,4 visas inte, och högst fem per insättning.
   - **Tidsytor** samlar pengar som rört sig inom två timmar.

   Hur ser det ut i era ärenden?
8. **Vilka mixers och tjänster ser ni oftast?** Det avgör vilka mönster som byggs först, till exempel Wasabi,
   Whirlpool, JoinMarket, centrala mixers, Tornado Cash, bryggor och swap-tjänster utan konto.
9. **Vilka förvirringstekniker möter ni mest?** Katalogen står i `docs/TODO.md` under *Förvirringstekniker*: uppdelning
   i många små belopp, genomgångshopp, byte av kedja och token, nästlade tjänster, damm och adressförgiftning med flera.

## Uttagen och börserna

10. **Vilka uttag är vanligast i de svenska ärendena?** Till exempel börs, börs utan kundkontroll, OTC och P2P,
    kryptoautomat och kryptokort. Kan några sorter slås ihop i grafen?

## Kurser

11. **Vilken kurs används i rapporterna?** Chainsaw tar sista priset i perioden, alltså ungefär dagens stängning. Brukar
    man använda stängningskursen, ett medelpris eller något annat?

## Bevakningen

12. **Hur ofta behöver en bevakning fråga källan?** Var femte minut, vid varje nytt block, eller oftare för en viktig
    bevakning inför ett beslag?
