namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>Why a sealed chain is broken.</summary>
public enum ChainBreakReason
{
    /// <summary>The record's content or hash has been changed, or a record before it has been.</summary>
    RecordChanged,

    /// <summary>A record is missing or has been inserted: the sequence numbers are not 1, 2, 3 and so on.</summary>
    SequenceGap,

    /// <summary>
    /// The chain's stored head does not match its last record: records at the end have been removed or added
    /// outside Chainsaw.
    /// </summary>
    HeadMismatch,

    /// <summary>The triggers that stop changes and removals are missing or have been altered.</summary>
    ProtectionMissing,

    /// <summary>The table is missing or its columns do not match the chain.</summary>
    TableChanged,
}
