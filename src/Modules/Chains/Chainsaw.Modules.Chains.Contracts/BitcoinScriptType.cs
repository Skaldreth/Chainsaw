namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The kind of locking script (scriptPubKey) on an output, which decides what is needed to spend it and how its
/// address is written. Chainsaw reads the kind from the script's bytes itself, not from the source's label.
/// </summary>
public enum BitcoinScriptType
{
    /// <summary>A script that matches no known pattern; it has no address.</summary>
    NonStandard,

    /// <summary>P2PK: pay to a public key written in the script (the oldest kind); it has no address of its own.</summary>
    PayToPublicKey,

    /// <summary>P2PKH: pay to the hash of a public key; addresses start with <c>1</c>.</summary>
    PayToPublicKeyHash,

    /// <summary>P2SH: pay to the hash of a script, often multisig or wrapped SegWit; addresses start with <c>3</c>.</summary>
    PayToScriptHash,

    /// <summary>P2WPKH: SegWit version 0, pay to a key hash; addresses start with <c>bc1q</c> and are 42 characters.</summary>
    PayToWitnessPublicKeyHash,

    /// <summary>P2WSH: SegWit version 0, pay to a script hash; addresses start with <c>bc1q</c> and are 62 characters.</summary>
    PayToWitnessScriptHash,

    /// <summary>P2TR: Taproot (SegWit version 1); addresses start with <c>bc1p</c>.</summary>
    PayToTaproot,

    /// <summary>A SegWit version that is not in use yet (2–16, or 1 with another length); it has a Bech32m address.</summary>
    WitnessUnknown,

    /// <summary>Bare multisig: the public keys are written in the script; it has no address.</summary>
    Multisig,

    /// <summary>OP_RETURN: data written on the chain; it can never be spent and has no address.</summary>
    OpReturn,
}

/// <summary>The short names of the script types, as they are written in the field.</summary>
public static class BitcoinScriptTypeNames
{
    /// <summary>Gives the short name of a script type.</summary>
    /// <param name="type">The script type.</param>
    /// <returns>For example <c>P2WPKH</c>.</returns>
    public static string ShortName(this BitcoinScriptType type) => type switch
    {
        BitcoinScriptType.PayToPublicKey => "P2PK",
        BitcoinScriptType.PayToPublicKeyHash => "P2PKH",
        BitcoinScriptType.PayToScriptHash => "P2SH",
        BitcoinScriptType.PayToWitnessPublicKeyHash => "P2WPKH",
        BitcoinScriptType.PayToWitnessScriptHash => "P2WSH",
        BitcoinScriptType.PayToTaproot => "P2TR",
        BitcoinScriptType.WitnessUnknown => "Witness",
        BitcoinScriptType.Multisig => "Multisig",
        BitcoinScriptType.OpReturn => "OP_RETURN",
        _ => "Nonstandard",
    };
}
