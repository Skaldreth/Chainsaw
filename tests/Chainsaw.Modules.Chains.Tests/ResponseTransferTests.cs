using System.Security.Cryptography;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Raw responses moved from one case to another unchanged (the background watching brings its responses in this way):
/// every value kept, the SHA-256 checked, and the hash chain of the case they come into intact.
/// </summary>
public sealed class ResponseTransferTests : IDisposable
{
    private readonly SourceHarness _harness = new();

    public ResponseTransferTests() => _harness.AnswerAll();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_response_is_read_with_every_value_and_sealed_unchanged_in_another_case()
    {
        await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var read = _harness.Case.Read(reader => _harness.Transfer.Read(reader, 1))!;
        using var other = _harness.CreateCase("2026-0043");
        other.Write(transaction => _harness.Transfer.Seal(transaction, Fixtures.Seal(read, 0)));

        var origin = other.Write(transaction => _harness.Transfer.Seal(transaction, read));
        var again = other.Read(reader => _harness.Transfer.Read(reader, origin.Sequence))!;

        Assert.Equal(2, origin.Sequence);
        Assert.Equal(read with { Sequence = 2, Body = [] }, again with { Body = [] });
        Assert.Equal(read.Body, again.Body);
        Assert.Equal(Fixtures.Bytes("tx-confirmed.json"), again.Body);
        Assert.Equal(read.Sha256, origin.Sha256);
        Assert.Equal(read.EndpointId, origin.EndpointId);
        Assert.Equal(read.Url, origin.Url.AbsoluteUri);
        Assert.Equal(_harness.Profiles.Profile, origin.FetchedBy);
        Assert.True(_harness.Verify(other).IsIntact);
    }

    [Fact]
    public async Task A_response_whose_body_does_not_match_its_hash_is_refused()
    {
        await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var read = _harness.Case.Read(reader => _harness.Transfer.Read(reader, 1))!;
        using var other = _harness.CreateCase("2026-0044");

        Assert.Throws<ArgumentException>(() => other.Write(transaction =>
            _harness.Transfer.Seal(transaction, read with { Body = [1, 2, 3] })));
        Assert.Throws<ArgumentException>(() => other.Write(transaction =>
            _harness.Transfer.Seal(transaction, read with { FetchedBy = "not json" })));
        Assert.Throws<ArgumentException>(() => other.Write(transaction =>
            _harness.Transfer.Seal(transaction, read with { Sha256 = "zz" })));
        Assert.Null(other.Read(reader => _harness.Transfer.Read(reader, 1)));
    }

    [Fact]
    public async Task The_chain_s_height_without_a_subject_is_moved_too()
    {
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        var read = _harness.Case.Read(reader => _harness.Transfer.Read(reader, 1))!;
        using var other = _harness.CreateCase("2026-0045");

        var origin = other.Write(transaction => _harness.Transfer.Seal(transaction, read));

        Assert.Equal(1, origin.Sequence);
        Assert.True(_harness.Verify(other).IsIntact);
    }

    [Fact]
    public async Task An_index_with_a_height_the_chain_cannot_have_come_to_is_sealed_but_not_used()
    {
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        var read = _harness.Case.Read(reader => _harness.Transfer.Read(reader, 1))!;
        using var other = _harness.CreateCase("2026-0046");

        other.Write(transaction => _harness.Transfer.Seal(transaction, read, new SealedResponseIndex(4_800_000, [])));

        Assert.Null(other.Read(reader => _harness.Transfer.ReadIndex(reader, 1)));
        Assert.True(_harness.Verify(other).IsIntact);
    }

    [Fact]
    public void A_number_with_no_response_reads_as_none()
    {
        Assert.Null(_harness.Case.Read(reader => _harness.Transfer.Read(reader, 99)));
    }

    [Fact]
    public async Task A_read_response_carries_the_hash_of_its_body()
    {
        await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var read = _harness.Case.Read(reader => _harness.Transfer.Read(reader, 1))!;

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(read.Body)), read.Sha256);
    }
}
