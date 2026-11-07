namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>A case file in the cases folder, as listed without opening it.</summary>
/// <param name="CaseNumber">The case number, taken from the file name.</param>
/// <param name="Path">The full path of the <c>.cut</c> file.</param>
/// <param name="Size">The file size in bytes.</param>
/// <param name="LastWrittenUtc">When the file was last saved, in UTC.</param>
public sealed record CaseFileInfo(string CaseNumber, string Path, long Size, DateTimeOffset LastWrittenUtc);
