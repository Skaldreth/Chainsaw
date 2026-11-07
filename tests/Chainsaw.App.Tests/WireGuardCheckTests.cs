using System.Text;
using Chainsaw.App.Exits;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.App.Tests;

public sealed class WireGuardCheckTests
{
    /// <summary>A made-up WireGuard file with every key the tunnel needs; the keys are not real keys.</summary>
    internal const string Valid = """
        [Interface]
        PrivateKey = cHJpdmF0ZS1rZXktZm9yLXRoZS10ZXN0cy1vbmx5AAA=
        Address = 10.64.0.2/32
        DNS = 10.64.0.1

        [Peer]
        PublicKey = cHVibGljLWtleS1mb3ItdGhlLXRlc3RzLW9ubHkAAAA=
        Endpoint = vpn.example.org:51820
        AllowedIPs = 0.0.0.0/0, ::/0
        """;

    [Fact]
    public void A_file_with_every_key_passes() =>
        Assert.Equal(WireGuardVerdict.Valid, WireGuardCheck.Check(Valid));

    [Theory]
    [InlineData("PrivateKey", WireGuardVerdict.NoPrivateKey)]
    [InlineData("Address", WireGuardVerdict.NoAddress)]
    [InlineData("DNS", WireGuardVerdict.NoDnsServer)]
    [InlineData("PublicKey", WireGuardVerdict.NoPublicKey)]
    [InlineData("Endpoint", WireGuardVerdict.NoEndpoint)]
    [InlineData("AllowedIPs", WireGuardVerdict.NoAllowedIps)]
    public void A_missing_key_is_named(string key, WireGuardVerdict verdict) =>
        Assert.Equal(verdict, WireGuardCheck.Check(Without(key)));

    [Fact]
    public void A_key_in_the_wrong_section_is_unknown()
    {
        var text = Without("Endpoint").Replace("DNS = 10.64.0.1", "DNS = 10.64.0.1\nEndpoint = vpn.example.org:1");

        Assert.Equal(WireGuardVerdict.UnknownKey, WireGuardCheck.Check(text));
    }

    [Theory]
    [InlineData("[Interface", WireGuardVerdict.BrokenHeader)]
    [InlineData("[", WireGuardVerdict.BrokenHeader)]
    [InlineData("[Foo]", WireGuardVerdict.UnknownSection)]
    [InlineData("Address", WireGuardVerdict.LineWithoutEquals)]
    [InlineData("= 10.64.0.9", WireGuardVerdict.UnknownKey)]
    [InlineData("Colour = blue", WireGuardVerdict.UnknownKey)]
    [InlineData("MTU = 1280\nMTU = 1420", WireGuardVerdict.RepeatedKey)]
    public void A_line_the_helper_refuses_is_refused(string line, WireGuardVerdict verdict) =>
        Assert.Equal(verdict, WireGuardCheck.Check(Valid.Replace("DNS = 10.64.0.1", "DNS = 10.64.0.1\n" + line)));

    [Fact]
    public void A_key_before_the_first_section_is_refused() =>
        Assert.Equal(WireGuardVerdict.KeyBeforeSection, WireGuardCheck.Check("MTU = 1280\n" + Valid));

    [Theory]
    [InlineData("Address = 10.64.0.3/32")]
    [InlineData("DNS = 10.64.0.2")]
    [InlineData("PostUp = echo")]
    [InlineData("PostUp = echo\nPostUp = echo")]
    public void Keys_the_helper_takes_again_or_ignores_pass(string line) =>
        Assert.Equal(
            WireGuardVerdict.Valid,
            WireGuardCheck.Check(Valid.Replace("DNS = 10.64.0.1", "DNS = 10.64.0.1\n" + line)));

    [Fact]
    public void The_same_key_in_two_sections_is_not_repeated() =>
        Assert.Equal(
            WireGuardVerdict.Valid,
            WireGuardCheck.Check(Valid.Replace("[Peer]", "[Peer]\nPersistentKeepalive = 25")
                .Replace("DNS = 10.64.0.1", "DNS = 10.64.0.1\nMTU = 1280")));

    [Fact]
    public void A_file_with_line_breaks_of_only_carriage_returns_is_refused_as_the_helper_refuses_it() =>
        Assert.Equal(WireGuardVerdict.BrokenHeader, WireGuardCheck.Check(Valid.ReplaceLineEndings("\r")));

    [Fact]
    public void Keys_are_read_without_regard_to_case() =>
        Assert.Equal(WireGuardVerdict.Valid, WireGuardCheck.Check(Valid.Replace("PrivateKey", "privatekey")));

    [Fact]
    public void Two_peers_are_refused() =>
        Assert.Equal(
            WireGuardVerdict.NotOnePeer,
            WireGuardCheck.Check(Valid + "\n[Peer]\nPublicKey = eA==\nEndpoint = b.example.org:1\nAllowedIPs = ::/0"));

    [Fact]
    public void A_file_without_an_interface_is_refused() =>
        Assert.Equal(
            WireGuardVerdict.NotOneInterface,
            WireGuardCheck.Check(Valid.AsSpan(Valid.IndexOf("[Peer]", StringComparison.Ordinal))));

    [Fact]
    public void Two_interfaces_are_refused() =>
        Assert.Equal(WireGuardVerdict.NotOneInterface, WireGuardCheck.Check(Valid + "\n[Interface]\nMTU = 1280"));

    [Theory]
    [InlineData("DNS = vpn.example.org")]
    [InlineData("DNS = 10")]
    [InlineData("# DNS = 10.64.0.1")]
    [InlineData("DNS =")]
    [InlineData("DNS = [::1]")]
    public void A_dns_line_without_a_plain_address_names_no_server(string line) =>
        Assert.Equal(WireGuardVerdict.NoDnsServer, WireGuardCheck.Check(Valid.Replace("DNS = 10.64.0.1", line)));

    [Theory]
    [InlineData("DNS = search.example.org, 10.64.0.1")]
    [InlineData("DNS = fd00::1")]
    [InlineData("DNS = 10.64.0.1 # the exit's own")]
    public void A_dns_line_with_an_address_names_a_server(string line) =>
        Assert.Equal(WireGuardVerdict.Valid, WireGuardCheck.Check(Valid.Replace("DNS = 10.64.0.1", line)));

    [Fact]
    public void Windows_line_ends_are_read() =>
        Assert.Equal(WireGuardVerdict.Valid, WireGuardCheck.Check(Valid.ReplaceLineEndings("\r\n")));

    [Fact]
    public void A_line_longer_than_the_helper_reads_is_too_large() =>
        Assert.Equal(WireGuardVerdict.TooLarge, WireGuardCheck.Check(Valid + "\n# " + new string('é', 33_000)));

    [Fact]
    public void An_empty_file_is_empty() =>
        Assert.Equal(WireGuardVerdict.Empty, WireGuardCheck.Check(" \n "));

    [Fact]
    public void A_file_longer_than_a_configuration_may_be_is_too_large() =>
        Assert.Equal(
            WireGuardVerdict.TooLarge,
            WireGuardCheck.Check(Valid + new string('#', ExitRules.MaximumConfigurationLength)));

    [Fact]
    public void Every_verdict_has_its_own_words()
    {
        var texts = Enum.GetValues<WireGuardVerdict>().Select(WireGuardCheck.TextFor).ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
        Assert.Equal("Giltig", WireGuardCheck.TextFor(WireGuardVerdict.Valid));
    }

    [Fact]
    public void A_file_is_read_without_its_byte_order_mark()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Valid, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var text = WireGuardFile.Read(path);

            Assert.NotNull(text);
            Assert.Equal(Valid, new string(text));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_file_too_large_is_not_read()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.OpenWrite(path))
            {
                stream.SetLength(WireGuardFile.MaximumBytes + 1);
            }

            Assert.Null(WireGuardFile.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(@"C:\VPN\se-got-02.conf", "se-got-02")]
    [InlineData(@"C:\VPN\ .conf", ExitNames.Fallback)]
    public void An_exit_is_first_named_after_its_file(string path, string name) =>
        Assert.Equal(name, ExitNames.FromFile(path.Replace('\\', Path.DirectorySeparatorChar)));

    [Fact]
    public void A_long_file_name_is_cut_to_the_limit() =>
        Assert.Equal(
            ExitRules.MaximumNameLength,
            ExitNames.FromFile(new string('a', ExitRules.MaximumNameLength + 20) + ".conf").Length);

    [Fact]
    public void Every_failure_has_its_own_words()
    {
        var texts = Enum.GetValues<ExitFailure>().Select(ExitNames.FailureText).ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    /// <summary>Gives the valid file without the line of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The file.</returns>
    private static string Without(string key) =>
        string.Join('\n', Valid.Split('\n').Where(line => !line.StartsWith(key + " ", StringComparison.Ordinal)));
}
