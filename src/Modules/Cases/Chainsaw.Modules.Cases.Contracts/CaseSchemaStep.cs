namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>One versioned change to a module's tables.</summary>
/// <param name="Version">The version the module's tables have after this step; at least 1.</param>
/// <param name="Description">What the step does, for the journal.</param>
/// <param name="Sql">
/// The SQL, one or more statements. It may create, change and drop the module's own tables and indexes; it may not
/// touch sealed tables after creating them, the Cases module's tables, attached databases or settings.
/// </param>
public sealed record CaseSchemaStep(int Version, string Description, string Sql);
