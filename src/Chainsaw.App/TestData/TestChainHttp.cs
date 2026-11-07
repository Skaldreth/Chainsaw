#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Chains.TestData;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// Answers Esplora's requests from the made-up chain, in this process: the test case's source is asked through this
/// instead of through the exit, so nothing about the test case ever leaves the computer. A request for anything else
/// than the made-up source's address is refused, never sent.
/// </summary>
/// <param name="chain">The made-up chain.</param>
/// <param name="clock">The clock, for the response's times.</param>
public sealed class TestChainHttp(TestChain chain, IClock clock) : IExitHttp
{
    /// <summary>Esplora's page size for an address's confirmed transactions.</summary>
    public const int PageSize = 25;

    /// <summary>The media type of a JSON answer.</summary>
    private const string Json = "application/json";

    /// <summary>The media type of a text answer (a height, a hash, hex).</summary>
    private const string Text = "text/plain";

    /// <summary>The part of a path that asks for an address.</summary>
    private const string AddressPart = "address/";

    /// <summary>The part of a path that asks for a transaction.</summary>
    private const string TransactionPart = "tx/";

    /// <summary>The part of a path that asks for a block.</summary>
    private const string BlockPart = "block/";

    /// <summary>The part of a path that asks for the hash at a height.</summary>
    private const string HeightPart = "block-height/";

    /// <summary>How many requests have been answered.</summary>
    private int _requests;

    /// <summary>Gets how many requests have been answered, so that a test can see that the source was asked.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <inheritdoc/>
    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var root = TestDataBitcoin.Address.AbsoluteUri;
        if (!request.Uri.AbsoluteUri.StartsWith(root, StringComparison.Ordinal))
        {
            throw new ExitException(ExitError.RequestFailed);
        }

        Interlocked.Increment(ref _requests);
        var now = clock.UtcNow;
        var (status, type, body) = Answer(request.Uri.AbsoluteUri[root.Length..]);
        return Task.FromResult(new ExitResponse(
            request.Uri, Guid.Empty, status, type, body, now, now, ExitRoute.Windows) { IsLocal = true });
    }

    /// <summary>Answers a path under the source's address.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The status, the media type and the body.</returns>
    private (int Status, string Type, byte[] Body) Answer(string path)
    {
        var answer = path switch
        {
            "blocks/tip/height" => Plain(chain.Tip.ToString(CultureInfo.InvariantCulture)),
            _ when path.StartsWith(HeightPart, StringComparison.Ordinal) => HashAt(path[HeightPart.Length..]),
            _ when path.StartsWith(BlockPart, StringComparison.Ordinal) => BlockAnswer(path[BlockPart.Length..]),
            _ when path.StartsWith(TransactionPart, StringComparison.Ordinal) =>
                TransactionAnswer(path[TransactionPart.Length..]),
            _ when path.StartsWith(AddressPart, StringComparison.Ordinal) =>
                AddressAnswer(Uri.UnescapeDataString(path[AddressPart.Length..])),
            _ => null,
        };
        return answer ?? (404, Text, Encoding.UTF8.GetBytes("Not found"));
    }

    /// <summary>Answers <c>block-height/:height</c>.</summary>
    /// <param name="height">The height as written.</param>
    /// <returns>The answer, or <see langword="null"/> when there is no such block.</returns>
    private (int, string, byte[])? HashAt(string height) =>
        long.TryParse(height, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && chain.BlockAt(number) is { } block
            ? Plain(block.Hash)
            : null;

    /// <summary>Answers <c>block/:hash</c> and <c>block/:hash/header</c>.</summary>
    /// <param name="rest">The path after <c>block/</c>.</param>
    /// <returns>The answer, or <see langword="null"/> when there is no such block.</returns>
    private (int, string, byte[])? BlockAnswer(string rest)
    {
        var parts = rest.Split('/');
        if (chain.Block(parts[0]) is not { } block)
        {
            return null;
        }

        return parts.Length switch
        {
            1 => Write(writer => EsploraJson.Block(writer, block)),
            2 when parts[1] == "header" => Plain(Convert.ToHexStringLower(block.Header)),
            _ => null,
        };
    }

    /// <summary>Answers <c>tx/:txid</c>, <c>tx/:txid/hex</c> and <c>tx/:txid/outspends</c>.</summary>
    /// <param name="rest">The path after <c>tx/</c>.</param>
    /// <returns>The answer, or <see langword="null"/> when there is no such transaction.</returns>
    private (int, string, byte[])? TransactionAnswer(string rest)
    {
        var parts = rest.Split('/');
        if (chain.Find(parts[0]) is not { } transaction)
        {
            return null;
        }

        return parts.Length switch
        {
            1 => Write(writer => EsploraJson.Transaction(writer, chain, transaction)),
            2 when parts[1] == "hex" => Plain(Convert.ToHexStringLower(transaction.Bytes)),
            2 when parts[1] == "outspends" => Write(writer => EsploraJson.Spends(writer, chain, transaction)),
            _ => null,
        };
    }

    /// <summary>
    /// Answers <c>address/:address</c>, <c>address/:address/txs</c> and <c>address/:address/txs/chain/:last</c>: the
    /// first page is the newest <see cref="PageSize"/>, and a later page the next after the last one seen.
    /// </summary>
    /// <param name="rest">The path after <c>address/</c>.</param>
    /// <returns>The answer, or <see langword="null"/> for a path Esplora does not have.</returns>
    private (int, string, byte[])? AddressAnswer(string rest)
    {
        var parts = rest.Split('/');
        if (!Modules.Chains.Contracts.BitcoinAddress.TryParse(parts[0], out var address))
        {
            return null;
        }

        var script = ScriptOf(address.Value);
        var history = script is null ? [] : chain.History(script);
        switch (parts.Length)
        {
            case 1:
                return Write(writer => EsploraJson.Summary(writer, chain, address.Value, script, history));
            case 2 when parts[1] == "txs":
                return Write(writer => EsploraJson.List(writer, chain, history.Take(PageSize)));
            case 4 when parts[1] == "txs" && parts[2] == "chain":
                var after = history.ToList().FindIndex(transaction => transaction.Txid == parts[3]);
                var page = after < 0 ? [] : history.Skip(after + 1).Take(PageSize);
                return Write(writer => EsploraJson.List(writer, chain, page));
            default:
                return null;
        }
    }

    /// <summary>Finds an address's script among the made-up outputs.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The script, or <see langword="null"/> when the chain has never paid it.</returns>
    private string? ScriptOf(string address) => chain.Transactions
        .SelectMany(transaction => transaction.Outputs)
        .FirstOrDefault(output => output.To.Address?.Value == address)?.To.Script;

    /// <summary>Gives a text answer.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The answer.</returns>
    private static (int, string, byte[]) Plain(string text) => (200, Text, Encoding.ASCII.GetBytes(text));

    /// <summary>Gives a JSON answer written by a writer.</summary>
    /// <param name="write">Writes the JSON.</param>
    /// <returns>The answer.</returns>
    private static (int, string, byte[]) Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return (200, Json, stream.ToArray());
    }
}
#endif
