# Utgångar som aldrig kan spenderas

> **Som i vardagen:** en del tx bär med sig en lapp som inte är pengar, som en anteckning på baksidan av en
> postanvisning. Lappen är en del av tx:en, men det finns inga pengar på den och den kan aldrig lösas in. Den behöver
> inte ligga mitt på bordet.

En Bitcoin-tx kan ha en utgång med **OP_RETURN**: en plats för lite data, till exempel en tidsstämpel, ett protokolls
märkning eller en text. En sådan utgång har ingen mottagare och kan **aldrig spenderas**. Oftast är beloppet 0 BTC.

## I grafen

Utgångar med OP_RETURN och **0 BTC döljs**, eftersom de bara skulle vara brus (princip 7). Efter en expansion som gav nya
sådana säger en toast hur många som döljs, med knappen **Visa dem**. Toasten ligger kvar längre än andra, så att du
hinner läsa den. När de visas kan du dölja dem igen på samma sätt. Valet sparas med grafens läge i ärendet.

**En OP_RETURN med ett belopp döljs aldrig.** Då har någon bränt pengar: de är borta för alla. Det är ett faktum värt
att se, och spåret tar slut där med orsaken *Kan aldrig spenderas*. Se [Varför ett spår tar slut](sparet-tar-slut.md).

Inget tas bort ur ärendet; tx:en och dess utgångar ligger kvar precis som de hämtades (princip 1).
