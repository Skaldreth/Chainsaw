using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>The start page's piles of cases (sketch GR8a), without WPF.</summary>
public sealed class CasePileTests
{
    /// <summary>Keeps the piles in memory, counting the writes.</summary>
    private sealed class MemoryPiles : ICasePileStore
    {
        public IReadOnlyList<CasePile> Saved { get; set; } = [];

        public int Writes { get; private set; }

        public IReadOnlyList<CasePile> Load() => Saved;

        public bool Save(IReadOnlyList<CasePile> piles)
        {
            Saved = piles;
            Writes++;
            return true;
        }
    }

    /// <summary>The cases folder, the workflow and the start page, with no case open.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Workflow = new CaseWorkflow(Store, new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Start = new GraphStartViewModel(Session, Workflow, new InlineUi(), new TestClock(), TimeZoneInfo.Utc);
        }

        public TestCaseStore Store { get; } = new();

        public CaseSession Session { get; } = new();

        public CaseWorkflow Workflow { get; }

        public GraphStartViewModel Start { get; }

        public MemoryPiles Piles { get; } = new();

        /// <summary>Puts cases in the folder, the first the newest, an hour apart, and lists them.</summary>
        public async Task ListAsync(params string[] numbers)
        {
            for (var index = 0; index < numbers.Length; index++)
            {
                Store.AddFile(numbers[index], minutesAgo: 60 * (index + 1));
            }

            await Workflow.RefreshCasesAsync();
            await Start.UsePilesAsync(Piles);
        }

        public IEnumerable<string> Loose => Start.Recent.Select(item => item.CaseNumber);

        public CasePileItem OnlyPile => Assert.Single(Start.Ring.OfType<CasePileItem>());
    }

    [Fact]
    public async Task A_case_dropped_on_another_makes_a_pile_that_is_named_at_once()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124");

        Assert.True(rig.Start.Stack("2026-0129", "2026-0131"));

        var pile = rig.OnlyPile;
        Assert.Equal(2, pile.Count);
        Assert.True(pile.IsNaming);
        Assert.Equal(pile.Id, rig.Start.NamingPileId);
        Assert.Equal(["2026-0124"], rig.Loose);
        Assert.Equal("senast ändrad i dag 11:00", pile.ChangedText);
    }

    [Fact]
    public async Task The_name_is_kept_and_saved_on_this_computer()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129");
        rig.Start.Stack("2026-0129", "2026-0131");

        rig.Start.NamePile(rig.OnlyPile.Id, "  Väntar på börs ");
        await rig.Start.PilesSaved;

        Assert.Null(rig.Start.NamingPileId);
        Assert.Equal("Väntar på börs", rig.OnlyPile.ShownName);
        var saved = Assert.Single(rig.Piles.Saved);
        Assert.Equal("Väntar på börs", saved.Name);
        Assert.Equal(["2026-0129", "2026-0131"], saved.CaseNumbers);
    }

    [Fact]
    public async Task A_pile_without_a_name_is_called_namnlos_and_a_long_name_is_cut()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124", "2026-0122");
        rig.Start.Stack("2026-0129", "2026-0131");
        rig.Start.Stack("2026-0124", "2026-0122");
        var piles = rig.Start.Ring.OfType<CasePileItem>().ToList();

        rig.Start.NamePile(piles[0].Id, string.Empty);
        rig.Start.NamePile(piles[1].Id, new string('x', 60));

        var named = rig.Start.Ring.OfType<CasePileItem>().ToDictionary(pile => pile.Id);
        Assert.Equal(GraphStartViewModel.UnnamedPileText, named[piles[0].Id].ShownName);
        Assert.Equal(GraphStartViewModel.MaximumPileName, named[piles[1].Id].Name.Length);
    }

    [Fact]
    public async Task More_cases_are_dropped_on_a_pile_and_lie_on_top()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124");
        rig.Start.Stack("2026-0129", "2026-0131");

        Assert.True(rig.Start.AddToPile("2026-0124", rig.OnlyPile.Id));

        Assert.Equal(3, rig.OnlyPile.Count);
        Assert.Equal("2026-0124", rig.Start.Piles[0].CaseNumbers[0]);
        Assert.Empty(rig.Loose);
    }

    [Fact]
    public async Task A_pile_dropped_on_another_lies_on_top_of_it_under_its_name()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124", "2026-0122");
        rig.Start.Stack("2026-0129", "2026-0131");
        var first = rig.Start.Piles[0].Id;
        rig.Start.NamePile(first, "Börs A");
        rig.Start.Stack("2026-0122", "2026-0124");
        var second = rig.Start.Piles[1].Id;
        rig.Start.NamePile(second, string.Empty);
        var dragged = (CasePileItem)rig.Start.Ring.Single(item => item.Key == second);
        var target = (CasePileItem)rig.Start.Ring.Single(item => item.Key == first);

        Assert.True(rig.Start.CanDrop(dragged, target));
        Assert.True(rig.Start.DropOn(dragged, target));
        await rig.Start.PilesSaved;

        var pile = rig.OnlyPile;
        Assert.Equal(first, pile.Id);
        Assert.Equal("Börs A", pile.ShownName);
        Assert.Equal(4, pile.Count);
        Assert.Equal(["2026-0122", "2026-0124", "2026-0129", "2026-0131"], rig.Start.Piles[0].CaseNumbers);
        Assert.Equal([first], rig.Piles.Saved.Select(saved => saved.Id));
    }

    [Fact]
    public async Task A_merged_pile_takes_the_dragged_name_when_its_own_is_empty()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124", "2026-0122");
        rig.Start.Stack("2026-0129", "2026-0131");
        rig.Start.NamePile(rig.Start.Piles[0].Id, string.Empty);
        rig.Start.Stack("2026-0122", "2026-0124");
        rig.Start.NamePile(rig.Start.Piles[1].Id, "Mixern");

        Assert.True(rig.Start.MergePiles(rig.Start.Piles[1].Id, rig.Start.Piles[0].Id));

        Assert.Equal("Mixern", rig.OnlyPile.ShownName);
    }

    [Fact]
    public async Task A_pile_is_not_dropped_on_itself_and_one_being_named_is_left_without_a_name_field()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124", "2026-0122");
        rig.Start.Stack("2026-0129", "2026-0131");
        rig.Start.NamePile(rig.Start.Piles[0].Id, "Börs A");
        rig.Start.Stack("2026-0122", "2026-0124");
        var naming = rig.Start.NamingPileId!;

        Assert.False(rig.Start.CanMergePiles(naming, naming));
        Assert.False(rig.Start.CanMergePiles(naming, "missing"));
        Assert.True(rig.Start.MergePiles(naming, rig.Start.Piles[0].Id));
        Assert.Null(rig.Start.NamingPileId);
    }

    [Fact]
    public async Task A_pile_dropped_on_a_loose_case_takes_it_in_and_a_case_on_a_pile_goes_in_it()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124", "2026-0122");
        rig.Start.Stack("2026-0129", "2026-0131");
        rig.Start.NamePile(rig.Start.Piles[0].Id, "Börs A");
        var pile = rig.OnlyPile;
        var loose = rig.Start.Recent.Single(item => item.CaseNumber == "2026-0124");

        Assert.True(rig.Start.CanDrop(pile, loose));
        Assert.True(rig.Start.DropOn(pile, loose));
        Assert.Equal(3, rig.OnlyPile.Count);

        var last = rig.Start.Recent.Single(item => item.CaseNumber == "2026-0122");
        Assert.True(rig.Start.DropOn(last, rig.OnlyPile));
        Assert.Equal(4, rig.OnlyPile.Count);
        Assert.Empty(rig.Loose);
    }

    [Fact]
    public async Task Two_loose_cases_dropped_on_each_other_make_a_pile_through_the_ring_drop()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129");
        var lifted = rig.Start.Recent.Single(item => item.CaseNumber == "2026-0129");
        var target = rig.Start.Recent.Single(item => item.CaseNumber == "2026-0131");

        Assert.False(rig.Start.CanDrop(lifted, lifted));
        Assert.True(rig.Start.DropOn(lifted, target));

        Assert.Equal(2, rig.OnlyPile.Count);
    }

    [Fact]
    public async Task A_case_is_not_stacked_on_itself_or_out_of_a_pile()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124");
        rig.Start.Stack("2026-0129", "2026-0131");

        Assert.False(rig.Start.CanStack("2026-0124", "2026-0124"));
        Assert.False(rig.Start.CanStack("2026-0124", "2026-0131"));
        Assert.False(rig.Start.CanStack("2026-0131", "2026-0124"));
        Assert.False(rig.Start.CanAddToPile("2026-0131", rig.OnlyPile.Id));
        Assert.False(rig.Start.CanStack("2026-0124", "2026-9999"));
    }

    [Fact]
    public async Task The_ring_stands_newest_first_with_the_piles_among_the_cases()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124", "2026-0122");

        rig.Start.Stack("2026-0124", "2026-0122");

        Assert.Equal(["2026-0131", "2026-0129"], rig.Start.Ring.Take(2).Select(item => item.Key));
        Assert.IsType<CasePileItem>(rig.Start.Ring[2]);
    }

    [Fact]
    public async Task Piling_up_frees_the_ring_for_older_cases()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124", "2026-0122", "2026-0118");
        Assert.DoesNotContain("2026-0118", rig.Loose);

        rig.Start.Stack("2026-0129", "2026-0131");

        Assert.Equal(["2026-0124", "2026-0122", "2026-0118"], rig.Loose);
    }

    [Fact]
    public async Task Only_piles_is_not_the_first_time()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129");

        rig.Start.Stack("2026-0129", "2026-0131");

        Assert.Empty(rig.Start.Recent);
        Assert.False(rig.Start.IsFirstTime);
        Assert.True(rig.Start.HasRecent);
    }

    [Fact]
    public async Task An_open_pile_shows_its_cases_as_cards_and_shift_click_chooses()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124");
        rig.Start.Stack("2026-0129", "2026-0131");
        rig.Start.AddToPile("2026-0124", rig.OnlyPile.Id);

        rig.Start.OpenPileOf(rig.OnlyPile.Id);
        rig.Start.ToggleCard("2026-0131");
        rig.Start.ToggleCard("2026-0129");

        Assert.True(rig.Start.IsPileOpen);
        Assert.Equal(["2026-0124", "2026-0129", "2026-0131"], rig.Start.OpenCards.Select(card => card.CaseNumber));
        Assert.Equal("2 valda · dra ut dem tillsammans", rig.Start.FanHint);
        Assert.Equal(["2026-0131", "2026-0129"], rig.Start.CardsToDrag("2026-0131"));
        Assert.Equal(["2026-0124"], rig.Start.CardsToDrag("2026-0124"));
    }

    [Fact]
    public async Task Cards_taken_out_lie_loose_even_when_old_and_the_pile_folds()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124", "2026-0122", "2026-0118", "2026-0117");
        rig.Start.Stack("2026-0118", "2026-0117");
        rig.Start.OpenPileOf(rig.OnlyPile.Id);

        rig.Start.TakeOut(["2026-0117"]);

        Assert.False(rig.Start.IsPileOpen);
        Assert.Contains("2026-0117", rig.Loose);
        Assert.Equal(1, rig.OnlyPile.Count);
    }

    [Fact]
    public async Task A_pile_emptied_is_gone()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129");
        rig.Start.Stack("2026-0129", "2026-0131");
        rig.Start.OpenPileOf(rig.OnlyPile.Id);

        rig.Start.TakeOut(["2026-0129", "2026-0131"]);
        await rig.Start.PilesSaved;

        Assert.Empty(rig.Start.Ring.OfType<CasePileItem>());
        Assert.Empty(rig.Piles.Saved);
        Assert.Equal(["2026-0131", "2026-0129"], rig.Loose);
    }

    [Fact]
    public async Task Saved_piles_are_read_and_a_case_no_longer_there_leaves_its_pile()
    {
        var rig = new Rig();
        rig.Piles.Saved = [new CasePile("p1", "Avslutade", ["2026-0117", "2026-0001", "2026-0112"])];

        await rig.ListAsync("2026-0131", "2026-0117", "2026-0112");
        await rig.Start.PilesSaved;

        Assert.Equal("Avslutade", rig.OnlyPile.Name);
        Assert.Equal(2, rig.OnlyPile.Count);
        Assert.Equal(["2026-0117", "2026-0112"], Assert.Single(rig.Piles.Saved).CaseNumbers);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_read_forgets_nothing()
    {
        var rig = new Rig();
        rig.Piles.Saved = [new CasePile("p1", "Avslutade", ["2026-0117", "2026-0112"])];
        rig.Store.ListFailure = new IOException("Nätverksenheten svarar inte.");

        await rig.ListAsync("2026-0117", "2026-0112");

        Assert.Equal(0, rig.Piles.Writes);
        Assert.Equal(["2026-0117", "2026-0112"], rig.Start.Piles[0].CaseNumbers);
    }

    [Fact]
    public async Task A_case_is_kept_in_one_pile_when_the_file_has_it_in_two()
    {
        var rig = new Rig();
        rig.Piles.Saved =
        [
            new CasePile("p1", "A", ["2026-0117", "2026-0112"]),
            new CasePile("p2", "B", ["2026-0112"]),
        ];

        await rig.ListAsync("2026-0117", "2026-0112");

        Assert.Equal("p1", Assert.Single(rig.Start.Piles).Id);
    }

    [Fact]
    public async Task Opening_a_case_folds_the_pile()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129");
        rig.Start.Stack("2026-0129", "2026-0131");
        rig.Start.OpenPileOf(rig.OnlyPile.Id);

        rig.Start.Show(GraphStartMode.None);

        Assert.False(rig.Start.IsPileOpen);
        Assert.Empty(rig.Start.OpenCards);
    }

    [Fact]
    public void The_store_writes_and_reads_the_piles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "chainsaw-piles-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CasePileStore(folder);
            Assert.Empty(store.Load());

            Assert.True(store.Save([new CasePile("p1", "Väntar på börs", ["2026-0120", "2026-0108"])]));

            var read = Assert.Single(store.Load());
            Assert.Equal("p1", read.Id);
            Assert.Equal("Väntar på börs", read.Name);
            Assert.Equal(["2026-0120", "2026-0108"], read.CaseNumbers);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_damaged_file_gives_no_piles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "chainsaw-piles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, CasePileStore.FileName), "{ inte json");

            Assert.Empty(new CasePileStore(folder).Load());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
