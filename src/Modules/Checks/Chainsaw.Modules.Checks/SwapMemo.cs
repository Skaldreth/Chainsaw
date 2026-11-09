using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// A swap's memo in THORChain's form (<c>SWAP:ETH.ETH:0x…</c>, also <c>=</c> or <c>s</c> for <c>SWAP</c>), which a
/// payment into the swap writes on the chain itself, in Bitcoin's <c>OP_RETURN</c>: the asset to swap to and the
/// address it is paid to on the other chain. It is the swap's own event, read from the chain (principle 2); that the
/// payout really went there is still to be found on the other chain. The grammar is THORChain's own
/// (<see cref="Grammar"/>); other services that write the same form are read the same way.
/// </summary>
/// <param name="Text">The memo as written.</param>
/// <param name="ChainCode">The other chain's code in the memo, for example <c>ETH</c>.</param>
/// <param name="Asset">The asset after the chain's code, for example <c>ETH</c> or <c>USDT-0X…</c>.</param>
/// <param name="Destination">The recipient as written.</param>
internal sealed record SwapMemo(string Text, string ChainCode, string Asset, string Destination)
{
    /// <summary>Where the grammar is described: THORChain's own documentation of its memos.</summary>
    public const string Grammar = "https://dev.thorchain.org/concepts/memos.html";

    /// <summary>The opcode that starts a script that carries data and can never be spent.</summary>
    private const byte OpReturn = 0x6a;

    /// <summary>The opcode that pushes the length in the next byte.</summary>
    private const byte PushData1 = 0x4c;

    /// <summary>The opcode that pushes the length in the next two bytes.</summary>
    private const byte PushData2 = 0x4d;

    /// <summary>The words the memo's first part may have for a swap.</summary>
    private static readonly string[] SwapWords = ["SWAP", "S", "="];

    /// <summary>
    /// The short asset names in THORChain's documentation that this reads: <c>b</c> for bitcoin and <c>e</c> for
    /// ether. A name not listed here is read as the long form or not at all.
    /// </summary>
    private static readonly Dictionary<string, (string Chain, string Asset)> ShortNames = new(StringComparer.Ordinal)
    {
        ["b"] = ("BTC", "BTC"),
        ["e"] = ("ETH", "ETH"),
    };

    /// <summary>The chains' codes in the memos, as Chainsaw names the chains.</summary>
    private static readonly Dictionary<string, Chain> Chains = new(StringComparer.Ordinal)
    {
        ["BTC"] = SharedKernel.Chain.Bitcoin,
        ["ETH"] = SharedKernel.Chain.Ethereum,
        ["TRON"] = SharedKernel.Chain.Tron,
    };

    /// <summary>
    /// Gets the other chain as Chainsaw names it, or <see langword="null"/> when Chainsaw does not have the chain.
    /// </summary>
    public Chain? Chain => Chains.TryGetValue(ChainCode, out var chain) ? chain : null;

    /// <summary>
    /// Gets the recipient as an address on the other chain, or <see langword="null"/> when Chainsaw does not have the
    /// chain or the recipient is not a valid address there.
    /// </summary>
    public ChainAddress? Address => Chain is { } chain ? AddressOn(chain, Destination) : null;

    /// <summary>Reads the memo in a Bitcoin output's script, when it is an <c>OP_RETURN</c> with one.</summary>
    /// <param name="scriptHex">The script in hex.</param>
    /// <returns>The memo, or <see langword="null"/>.</returns>
    public static SwapMemo? FromScript(string? scriptHex)
    {
        if (string.IsNullOrEmpty(scriptHex) || scriptHex.Length % 2 != 0
            || !scriptHex.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        var script = Convert.FromHexString(scriptHex);
        return Pushed(script) is { } data && data.All(b => b is >= 0x20 and < 0x7f)
            ? Parse(System.Text.Encoding.ASCII.GetString(data))
            : null;
    }

    /// <summary>Reads a memo's text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The memo, or <see langword="null"/> when it is not a swap with a recipient.</returns>
    public static SwapMemo? Parse(string? text)
    {
        var parts = text?.Trim().Split(':');
        if (parts is null || parts.Length < 3
            || !SwapWords.Contains(parts[0], StringComparer.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parts[2]))
        {
            return null;
        }

        // A recipient may be followed by a refund address after a slash.
        var destination = parts[2].Split('/')[0].Trim();
        if (ShortNames.TryGetValue(parts[1], out var named))
        {
            return new SwapMemo(text!.Trim(), named.Chain, named.Asset, destination);
        }

        // Only a dot names an asset on its own chain; a tilde or a slash names one held on THORChain itself.
        var dot = parts[1].IndexOf('.', StringComparison.Ordinal);
        return dot <= 0 || dot == parts[1].Length - 1
            ? null
            : new SwapMemo(
                text!.Trim(), parts[1][..dot].ToUpperInvariant(), parts[1][(dot + 1)..], destination);
    }

    /// <summary>Reads the data an <c>OP_RETURN</c> script pushes.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The data, or <see langword="null"/> when the script is not one push after <c>OP_RETURN</c>.</returns>
    private static byte[]? Pushed(byte[] script)
    {
        if (script.Length < 2 || script[0] != OpReturn)
        {
            return null;
        }

        var (length, start) = script[1] switch
        {
            < PushData1 => (script[1], 2),
            PushData1 when script.Length > 2 => (script[2], 3),
            PushData2 when script.Length > 3 => (script[2] | (script[3] << 8), 4),
            _ => (-1, 0),
        };
        return length > 0 && start + length == script.Length ? script[start..] : null;
    }

    /// <summary>Reads a recipient as an address on a chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The recipient as written.</param>
    /// <returns>The address, or <see langword="null"/> when it is not one.</returns>
    private static ChainAddress? AddressOn(Chain chain, string text)
    {
        if (chain == SharedKernel.Chain.Bitcoin)
        {
            return BitcoinAddress.TryParse(text, out var bitcoin) ? bitcoin.ToChainAddress() : null;
        }

        return EthereumAddress.TryParse(chain, text, out var account) ? account.ToChainAddress() : null;
    }

    /// <summary>Writes the memo as the reason quotes it.</summary>
    /// <returns>For example <c>memot SWAP:ETH.ETH:0x… (THORChains form)</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"memot {Text} (THORChains form)");
}
