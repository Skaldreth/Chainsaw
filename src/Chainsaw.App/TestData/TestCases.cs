#if DEBUG
using System.Runtime.CompilerServices;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.TestData;

/// <summary>
/// Knows a test case: one that <c>/testdata</c> created, which says so in its sealed journal right after it is created.
/// The journal can only be added to, and only <c>/testdata</c> writes the line, so no real case can become a test case,
/// and the made-up source is never asked for one.
/// </summary>
public static class TestCases
{
    /// <summary>The journal kind that marks a test case.</summary>
    public const string JournalKind = "testdata.created";

    /// <summary>How far into the journal the mark is looked for; it is written right after the case is created.</summary>
    private const int MarkWithin = 20;

    /// <summary>What each open case was found to be, so that the journal is read once per open case.</summary>
    private static readonly ConditionalWeakTable<ICase, StrongBox<bool>> Known = [];

    /// <summary>Marks a newly created case as a test case, in its journal.</summary>
    /// <param name="case">The case <c>/testdata</c> just created.</param>
    public static void Mark(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        @case.AppendJournal(new JournalDraft(
            JournalKind,
            "Testärende med påhittade data, skapat med /testdata (bara Debug). Kedjans data kommer från den påhittade "
                + "källan testdata och aldrig från nätet.",
            $$"""{"story":"{{TestStory.Version}}"}"""));
        Known.AddOrUpdate(@case, new StrongBox<bool>(true));
    }

    /// <summary>Says whether a case is a test case.</summary>
    /// <param name="case">The case.</param>
    /// <returns><see langword="true"/> when <c>/testdata</c> created it.</returns>
    public static bool IsTestCase(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        if (Known.TryGetValue(@case, out var known))
        {
            return known.Value;
        }

        bool marked;
        try
        {
            marked = @case.ReadJournal(1, MarkWithin).Any(entry => entry.Kind == JournalKind);
        }
        catch (CaseException)
        {
            // A closed or locked case is asked for nothing anyway; the real source says so, and nothing is cached.
            return false;
        }

        Known.AddOrUpdate(@case, new StrongBox<bool>(marked));
        return marked;
    }
}
#endif
