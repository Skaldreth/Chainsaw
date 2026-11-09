using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The panel's row for <c>cross-chain</c> (KF2b): a candidate on another chain is shown with its own asset, and once
/// one is approved only <em>Avfärda resten</em> is offered, since a payment continues in one place.
/// </summary>
public sealed class CrossChainRowTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    private static ChainMovement Payout(char digit) => new(
        new ChainTransactionRef(Chain.Tron, new string(digit, 64)),
        null,
        new ChainAddress(Chain.Tron, "TPayoutAddressForTheRowTest" + digit),
        AssetAmount.Parse(Asset.Tronix, "100"),
        1,
        null,
        Start.AddMinutes(30));

    private static CheckCandidate Candidate(char digit) =>
        new($"Tron:{digit}", null, null, 0, null, 0.8, "Skäl.") { Arrival = Payout(digit) };

    private static StoredCheck Check(params CheckDecision[] decisions) => new(
        9,
        new HeuristicId(CheckAnalysisNames.CrossChain, 1),
        "Annan kedja",
        new CheckFinding(
            "Bitcoin:in:0", CheckKind.Suggestion, AreaSort.OtherChain, "Fortsättning på annan kedja", "2 kandidater",
            "cross-chain v1 … en bedömning.", [V], [], [new CheckBasis(Tx[0], Block(100))],
            [Candidate('a'), Candidate('b')])
        {
            Departure = new ChainMovement(
                ChainTransactionRef.Of(Tx[0]), null, V.ToChainAddress(), AssetAmount.Parse(Asset.Bitcoin, "0.01"),
                100, null, Start),
        },
        5,
        Start,
        decisions);

    [Fact]
    public void A_candidate_on_another_chain_shows_its_recipient_asset_and_time()
    {
        var row = new CheckRowViewModel(Check(), null, [], null, TimeZoneInfo.Utc);

        var first = row.Candidates[0];
        Assert.EndsWith("TRX", first.AmountText, StringComparison.Ordinal);
        Assert.Equal("30 min", first.TimeText);
        Assert.Contains("BTC in", row.Meta, StringComparison.Ordinal);
        Assert.True(row.CanApprove);
        Assert.Equal(string.Empty, row.Flow.FeeText);
    }

    [Fact]
    public void A_lines_why_names_the_approved_suggestion_and_leaves_out_an_empty_reason()
    {
        var link = new CrossChainLink(
            3, new Outpoint(Tx[0], 0), new ChainTransactionRef(Chain.Tron, new string('a', 64)), string.Empty, 1,
            Start)
        {
            Origin = new CrossChainOrigin("cross-chain v1", 12),
        };

        var why = InspectorContent.LinkWhy(link);

        Assert.Contains("ett godkänt förslag ur cross-chain v1 (beslut 12)", why, StringComparison.Ordinal);
        Assert.DoesNotContain(": .", why, StringComparison.Ordinal);
        Assert.Contains(": Samma belopp.", InspectorContent.LinkWhy(link with { Reason = "Samma belopp." }),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Once_a_candidate_is_approved_only_the_rest_can_be_dismissed()
    {
        var approved = new CheckDecision(
            1, new HeuristicId(CheckAnalysisNames.CrossChain, 1), "Bitcoin:in:0", "Tron:a", CheckVerdict.Approved,
            null, CheckSamples.Who, Start.AddHours(1), 6);

        var row = new CheckRowViewModel(Check(approved), null, [], null, TimeZoneInfo.Utc);

        Assert.True(row.CanDecide);
        Assert.False(row.CanApprove);
        Assert.Null(row.ApproveRequest());
        Assert.Equal("Avfärda resten", row.DismissText);
        Assert.Single(row.DismissRequests());
    }
}
