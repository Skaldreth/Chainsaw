using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// What every open case needs from outside: keys, the operator, the clock, the options and the schema.
/// </summary>
/// <param name="Vault">The vault, for its state.</param>
/// <param name="PurposeKeys">The vault's keys per purpose, for this vault's envelopes.</param>
/// <param name="Profiles">The operator profile, for the journal.</param>
/// <param name="Secrets">
/// The vault's named secrets, for the generation of this computer's anchors; without them (a host that has no vault
/// secrets) the anchors are checked as before, without the generation.
/// </param>
/// <param name="Clock">The clock, for the journal.</param>
/// <param name="Options">The options.</param>
/// <param name="Catalog">The modules' schemas and sealed chains.</param>
internal sealed record CaseServices(
    IVault Vault,
    IPurposeKeys PurposeKeys,
    IOperatorProfileStore Profiles,
    IVaultSecrets? Secrets,
    IClock Clock,
    CasesOptions Options,
    SchemaCatalog Catalog);
