using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The exits the user allows in the background (decided 2026-11-08): a copy of an allowed exit's WireGuard file under
/// the user's protection, outside the vault, followed by the selection and removed with the exit or the permission;
/// and the background's view of it, which connects only an allowed, selected exit.
/// </summary>
public sealed class BackgroundExitsTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "chainsaw-bg-exits-" + Guid.NewGuid().ToString("N"));

    private readonly NetworkHarness _harness = new();

    private readonly XorProtector _protector = new();

    private readonly BackgroundExitFile _file;

    private readonly ExitStore _store;

    public BackgroundExitsTests()
    {
        _file = new BackgroundExitFile(FilePath, _protector, () => _harness.Records);
        _store = new ExitStore(_harness.Records, _harness.Connection, _file);
    }

    private string FilePath => Path.Combine(_folder, NetworkOptions.BackgroundExitsFileName);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Nothing_is_kept_until_an_exit_is_allowed()
    {
        var exit = _store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(exit.Id);

        Assert.False(File.Exists(FilePath));
        Assert.False(_file.IsAllowed(exit.Id));
        Assert.Null(_file.Usable());
    }

    [Fact]
    public async Task An_allowed_selected_exit_is_usable_and_its_file_is_never_plain_text()
    {
        var exit = _store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(exit.Id);

        _file.Allow(exit.Id);

        Assert.True(_file.IsAllowed(exit.Id));
        Assert.Equal(exit.Id, _file.Usable());
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(FilePath));
        Assert.DoesNotContain("test-private-key-not-real", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_allowed_exit_that_is_not_selected_is_never_usable()
    {
        var allowed = _store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        var other = _store.Add("Exit B", "Bern", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(allowed.Id);
        _file.Allow(allowed.Id);

        await _store.SelectAsync(other.Id);

        Assert.True(_file.IsAllowed(allowed.Id));
        Assert.Null(_file.Usable());
    }

    [Fact]
    public async Task Removing_the_exit_or_the_permission_removes_the_copy()
    {
        var exit = _store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(exit.Id);
        _file.Allow(exit.Id);

        await _store.RemoveAsync(exit.Id);

        Assert.False(File.Exists(FilePath));
        Assert.False(_file.IsAllowed(exit.Id));

        var again = _store.Add("Exit B", "Bern", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(again.Id);
        _file.Allow(again.Id);
        _file.Forbid(again.Id);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task Turning_the_background_off_clears_every_copy()
    {
        var exit = _store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(exit.Id);
        _file.Allow(exit.Id);

        _file.Clear();

        Assert.False(File.Exists(FilePath));
        Assert.Null(_file.Usable());
    }

    [Fact]
    public void Allowing_an_unknown_exit_or_without_a_place_is_refused()
    {
        Assert.Throws<ArgumentException>(() => _file.Allow(Guid.NewGuid()));
        var nowhere = new BackgroundExitFile(null, _protector, () => _harness.Records);
        Assert.False(nowhere.IsAvailable);
        Assert.Throws<InvalidOperationException>(() => nowhere.Allow(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_foreign_or_broken_file_holds_nothing()
    {
        var exit = _store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(exit.Id);
        _file.Allow(exit.Id);
        var bytes = File.ReadAllBytes(FilePath);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(FilePath, bytes);

        Assert.Null(_file.Usable());
        Assert.False(_file.IsAllowed(exit.Id));
    }

    [Fact]
    public async Task The_background_connects_only_the_allowed_selected_exit_without_the_vault()
    {
        var exit = _store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        var other = _store.Add("Exit B", "Bern", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(exit.Id);
        _file.Allow(exit.Id);
        _harness.Secrets.Locked = true;

        var reader = new BackgroundExitFile(FilePath, _protector, null);
        var records = new ExitRecords(new BackgroundExitSecrets(reader));
        var connector = new FakeConnector();
        var connection = new ExitConnection(records, connector, new RecordingHandlerFactory());

        Assert.Equal([exit.Id], records.List().Select(each => each.Id));
        var status = await connection.ConnectAsync();

        Assert.Equal(ExitState.Connected, status.State);
        Assert.Equal(exit.Id, status.ExitId);
        Assert.Equal(NetworkHarness.Configuration, connector.Calls.Single().Configuration);
        Assert.Null(records.Find(other.Id));
        await connection.DisconnectAsync();
    }

    [Fact]
    public async Task The_background_has_no_exit_to_connect_when_the_selected_one_is_not_allowed()
    {
        var exit = _store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        var other = _store.Add("Exit B", "Bern", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(exit.Id);
        _file.Allow(exit.Id);
        await _store.SelectAsync(other.Id);

        var reader = new BackgroundExitFile(FilePath, _protector, null);
        var connection = new ExitConnection(
            new ExitRecords(new BackgroundExitSecrets(reader)), new FakeConnector(), new RecordingHandlerFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ConnectAsync());
        Assert.Throws<NotSupportedException>(() => new BackgroundExitSecrets(reader).Save("network/x", "y"));
    }

    [Fact]
    public async Task A_renamed_allowed_exit_is_copied_again()
    {
        var exit = _store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _store.SelectAsync(exit.Id);
        _file.Allow(exit.Id);

        await _store.DescribeAsync(exit.Id, "Exit Z", "Bern");

        var reader = new BackgroundExitFile(FilePath, _protector, null);
        var records = new ExitRecords(new BackgroundExitSecrets(reader));
        Assert.Equal("Exit Z", records.List().Single().Name);
    }

    /// <summary>DPAPI's stand-in: XOR with a key from the purpose and a check sum, so that a changed byte fails.</summary>
    private sealed class XorProtector : IUserDataProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> data, string purpose)
        {
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(purpose));
            var output = new byte[data.Length + 32];
            for (var index = 0; index < data.Length; index++)
            {
                output[index] = (byte)(data[index] ^ key[index % key.Length]);
            }

            SHA256.HashData([.. data, .. key]).CopyTo(output, data.Length);
            return output;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose)
        {
            if (protectedData.Length < 32)
            {
                throw new CryptographicException("Too short.");
            }

            var key = SHA256.HashData(Encoding.UTF8.GetBytes(purpose));
            var data = new byte[protectedData.Length - 32];
            for (var index = 0; index < data.Length; index++)
            {
                data[index] = (byte)(protectedData[index] ^ key[index % key.Length]);
            }

            if (!SHA256.HashData([.. data, .. key]).AsSpan().SequenceEqual(protectedData[data.Length..]))
            {
                throw new CryptographicException("Changed.");
            }

            return data;
        }
    }
}
