using Chainsaw.Modules.Security.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Security.Tests;

/// <summary>The protection for the Windows user (DPAPI), without the vault, for the background watching.</summary>
public sealed class UserDataProtectionTests
{
    [Fact]
    public void The_protection_is_added_alone_without_the_vault()
    {
        var services = new ServiceCollection().AddUserDataProtection();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DpapiProtector>(provider.GetRequiredService<IUserDataProtector>());
        Assert.Null(provider.GetService<IVault>());
    }

    [Fact]
    public void A_protector_already_given_is_kept()
    {
        var mine = new DpapiProtector();
        var services = new ServiceCollection().AddSingleton<IUserDataProtector>(mine).AddUserDataProtection();
        using var provider = services.BuildServiceProvider();

        Assert.Same(mine, provider.GetRequiredService<IUserDataProtector>());
    }

    [Fact]
    public void Dpapi_round_trips_on_windows_and_is_refused_elsewhere()
    {
        var protector = new DpapiProtector();
        byte[] data = [.. "adresser och ärendets id"u8];
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() => protector.Protect(data, "test"));
            Assert.Throws<PlatformNotSupportedException>(() => protector.Unprotect(data, "test"));
            return;
        }

        var protectedData = protector.Protect(data, "test");

        Assert.NotEqual(data, protectedData);
        Assert.Equal(data, protector.Unprotect(protectedData, "test"));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => protector.Unprotect(protectedData, "another"));
    }

    [Fact]
    public void A_purpose_is_required()
    {
        Assert.ThrowsAny<ArgumentException>(() => new DpapiProtector().Protect([1], " "));
    }
}
