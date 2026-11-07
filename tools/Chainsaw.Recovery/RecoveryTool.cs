using System.Security.Cryptography;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Recovery;

/// <summary>
/// The administrator's tool for the organisation's recovery key: makes the key pair, shows fingerprints, checks that a
/// case has an envelope for a key, and opens a case with the private key to give it a new way in.
/// </summary>
/// <remarks>
/// Nothing secret is ever written to the output: passwords and master keys are asked for without echo and cleared
/// after use, the private key only exists encrypted on disk and in memory while a command runs, and failures are
/// reported by their reason, never with an exception's details.
/// </remarks>
internal static class RecoveryTool
{
    /// <summary>The option with the private key's file.</summary>
    private const string KeyOption = "key";

    /// <summary>The option with the administrator's name.</summary>
    private const string AdministratorOption = "admin";

    /// <summary>The option with the administrator's unit, for the journal.</summary>
    private const string OrganizationOption = "org";

    /// <summary>The option with the operator's vault folder.</summary>
    private const string VaultOption = "vault";

    /// <summary>The switch for a new sharing password.</summary>
    private const string ShareSwitch = "share";

    /// <summary>The option with a public key's file.</summary>
    private const string PublicOption = "public";

    /// <summary>The administrator's title in the journal.</summary>
    private const string AdministratorTitle = "Administratör";

    /// <summary>The command that makes a key pair.</summary>
    private const string KeygenCommand = "keygen";

    /// <summary>The command that shows a fingerprint.</summary>
    private const string FingerprintCommand = "fingerprint";

    /// <summary>The command that checks a case.</summary>
    private const string CheckCommand = "check";

    /// <summary>The command that opens a case.</summary>
    private const string OpenCommand = "open";

    /// <summary>The switches, which take no value.</summary>
    private static readonly HashSet<string> Switches = [ShareSwitch];

    /// <summary>Runs the tool.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="console">The console.</param>
    /// <returns>The exit code (see <see cref="ExitCodes"/>).</returns>
    public static int Run(IReadOnlyList<string> args, IToolConsole console)
    {
        var line = CommandLine.Parse(args, Switches);
        try
        {
            return line?.Command switch
            {
                KeygenCommand => Keygen(line, console),
                FingerprintCommand => Fingerprint(line, console),
                CheckCommand => Check(line, console),
                OpenCommand => Open(line, console),
                _ => Usage(console),
            };
        }
        catch (CaseException exception)
        {
            console.Error($"Fel: {Describe(exception.Error)}");
            return ExitCodes.Failed;
        }
        catch (VaultException exception)
        {
            console.Error($"Fel i valvet: {exception.Error}");
            return ExitCodes.Failed;
        }
        catch (CryptographicException)
        {
            console.Error("Fel: nyckeln gick inte att läsa (fel lösenord, eller inte en P-384-nyckel).");
            return ExitCodes.Failed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            console.Error($"Fel: {exception.Message}");
            return ExitCodes.Failed;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Never a stack trace or an exception's details: only that the command failed.
            console.Error($"Fel: kommandot misslyckades ({exception.GetType().Name}).");
            return ExitCodes.Failed;
        }
    }

    /// <summary>
    /// <c>keygen &lt;folder&gt;</c>: makes a new key pair in a folder outside every git repository, the private key
    /// protected by a password typed twice. Nothing is overwritten.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <param name="console">The console.</param>
    /// <returns>The exit code.</returns>
    private static int Keygen(CommandLine line, IToolConsole console)
    {
        if (line.Positional.Count != 1 || !line.OnlyOptions())
        {
            return Usage(console);
        }

        var directory = Path.GetFullPath(line.Positional[0]);
        if (RepositoryGuard.FindRepositoryRoot(directory) is { } repository)
        {
            console.Error($"Vägrar: {directory} ligger i git-repot {repository}. Den privata nyckeln får aldrig "
                + "hamna i ett repo. Välj en mapp utanför, helst på ett flyttbart medium.");
            return ExitCodes.Failed;
        }

        var existing = new[] { KeyFiles.PrivateFileName, KeyFiles.PublicFileName }
            .Select(name => Path.Combine(directory, name))
            .Where(File.Exists)
            .ToList();
        if (existing.Count > 0)
        {
            console.Error($"Vägrar: {string.Join(", ", existing)} finns redan och skrivs aldrig över.");
            return ExitCodes.Failed;
        }

        var password = AskNewSecret(
            console, "Lösenord för den privata nyckeln", KeyFiles.MinimumPasswordLength);
        if (password is null)
        {
            return ExitCodes.Failed;
        }

        try
        {
            Directory.CreateDirectory(directory);
            using var key = KeyFiles.NewKey();
            var (privatePath, publicPath) = KeyFiles.Write(directory, key, password);

            // Read back, so that a key that cannot be opened with the password is found now and not in a crisis.
            using var check = KeyFiles.ReadPrivate(privatePath, password);
            var publicKey = RecoveryPublicKey.FromPrivateKey(check);
            console.Out($"Privat nyckel: {privatePath}");
            console.Out($"Publik nyckel: {publicPath}");
            console.Out($"Fingeravtryck: {publicKey.DisplayFingerprint}");
            console.Out("Kopiera den publika nyckeln till src/Modules/Cases/Chainsaw.Modules.Cases/"
                + "organization-recovery-key.pem och bygg Chainsaw.");
            console.Out("Förvara den privata nyckeln och lösenordet åtskilda, se docs/ATERSTALLNINGSNYCKEL.md.");
            return ExitCodes.Success;
        }
        finally
        {
            Array.Clear(password);
        }
    }

    /// <summary>
    /// <c>fingerprint [public-key-file]</c>: shows a public key's fingerprint, by default the key built into this
    /// Chainsaw.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <param name="console">The console.</param>
    /// <returns>The exit code.</returns>
    private static int Fingerprint(CommandLine line, IToolConsole console)
    {
        if (line.Positional.Count > 1 || !line.OnlyOptions())
        {
            return Usage(console);
        }

        var key = line.Positional.Count == 1 ? KeyFiles.ReadPublic(line.Positional[0]) : BuiltInKey();
        if (key is null)
        {
            console.Error(line.Positional.Count == 1
                ? "Fel: filen har ingen publik nyckel (ange den publika nyckelns fil, aldrig den privata)."
                : "Ingen återställningsnyckel är inbyggd i den här versionen av Chainsaw.");
            return ExitCodes.Failed;
        }

        console.Out(key.DisplayFingerprint);
        return ExitCodes.Success;
    }

    /// <summary>
    /// <c>check &lt;case.cut&gt; [--public &lt;public-key-file&gt;]</c>: says whether the case has an envelope for a
    /// key, by default the one built into this Chainsaw, and lists the keys it has envelopes for.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <param name="console">The console.</param>
    /// <returns><see cref="ExitCodes.Success"/> when it has, <see cref="ExitCodes.NotCovered"/> when not.</returns>
    private static int Check(CommandLine line, IToolConsole console)
    {
        if (line.Positional.Count != 1 || !line.OnlyOptions(PublicOption))
        {
            return Usage(console);
        }

        var publicFile = line.Option(PublicOption);
        var key = publicFile is null ? BuiltInKey() : KeyFiles.ReadPublic(publicFile);
        if (key is null)
        {
            console.Error(publicFile is null
                ? "Fel: ingen återställningsnyckel är inbyggd; ange den publika nyckeln med --public."
                : "Fel: filen har ingen publik nyckel.");
            return ExitCodes.Failed;
        }

        using var session = CaseSession.WithoutVault();
        var fingerprints = session.Store.ReadRecoveryFingerprints(line.Positional[0]);
        foreach (var fingerprint in fingerprints)
        {
            console.Out($"Kuvert för {RecoveryPublicKey.FormatFingerprint(fingerprint)}");
        }

        if (fingerprints.Contains(key.Fingerprint, StringComparer.Ordinal))
        {
            console.Out($"Ja: ärendet har ett kuvert för {key.DisplayFingerprint}.");
            return ExitCodes.Success;
        }

        console.Out($"Nej: ärendet har inget kuvert för {key.DisplayFingerprint}.");
        return ExitCodes.NotCovered;
    }

    /// <summary>
    /// <c>open &lt;case.cut&gt; --key &lt;private-key-file&gt; --admin &lt;name&gt; (--share | --vault
    /// &lt;folder&gt;)</c>: opens the case with the private key and gives it a sharing password, or an envelope for
    /// the operator's (new) vault, with entries in the case's journal.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <param name="console">The console.</param>
    /// <returns>The exit code.</returns>
    private static int Open(CommandLine line, IToolConsole console)
    {
        var keyFile = line.Option(KeyOption);
        var administrator = line.Option(AdministratorOption)?.Trim();
        var vault = line.Option(VaultOption);
        if (line.Positional.Count != 1 || keyFile is null || string.IsNullOrEmpty(administrator)
            || administrator.Any(char.IsControl) || (line.Option(OrganizationOption)?.Any(char.IsControl) ?? false)
            || line.Has(ShareSwitch) == (vault is not null) || (vault is not null && line.Has(OrganizationOption))
            || !line.OnlyOptions(KeyOption, AdministratorOption, OrganizationOption, VaultOption, ShareSwitch))
        {
            return Usage(console);
        }

        var casePath = Path.GetFullPath(line.Positional[0]);
        using var privateKey = ReadPrivateKey(console, keyFile);
        var profile = new OperatorProfile(administrator, AdministratorTitle, line.Option(OrganizationOption) ?? "");
        return vault is null
            ? OpenAndShare(console, casePath, privateKey, profile)
            : OpenForOperator(console, casePath, privateKey, administrator, vault);
    }

    /// <summary>Opens the case as the administrator and sets a new sharing password.</summary>
    /// <param name="console">The console.</param>
    /// <param name="casePath">The case file.</param>
    /// <param name="privateKey">The private recovery key.</param>
    /// <param name="administrator">The administrator's profile.</param>
    /// <returns>The exit code.</returns>
    private static int OpenAndShare(
        IToolConsole console, string casePath, ECDiffieHellman privateKey, OperatorProfile administrator)
    {
        var password = AskNewSecret(console, "Nytt delningslösenord", SharingPasswordRules.MinimumLength);
        if (password is null)
        {
            return ExitCodes.Failed;
        }

        try
        {
            // The session (and its temporary vault) is gone before success is reported, so that a failure to clean
            // up is never reported after the case was changed.
            using (var session = CaseSession.ForAdministrator(administrator))
            using (var opened = session.Store.OpenWithRecoveryKey(casePath, privateKey, administrator.Name))
            {
                opened.SetSharingPassword(password);
                opened.Close();
            }

            console.Out($"Ärendet {casePath} har ett nytt delningslösenord. Operatören öppnar det med lösenordet, "
                + "lägger till sitt nya valv och tar bort lösenordet.");
            return ExitCodes.Success;
        }
        finally
        {
            Array.Clear(password);
        }
    }

    /// <summary>
    /// Opens the case in the operator's vault, which the operator unlocks with the master key, and adds an envelope
    /// for that vault.
    /// </summary>
    /// <param name="console">The console.</param>
    /// <param name="casePath">The case file.</param>
    /// <param name="privateKey">The private recovery key.</param>
    /// <param name="administrator">The administrator's name, for the journal.</param>
    /// <param name="vault">The operator's vault folder.</param>
    /// <returns>The exit code.</returns>
    private static int OpenForOperator(
        IToolConsole console, string casePath, ECDiffieHellman privateKey, string administrator, string vault)
    {
        var masterKey = console.ReadSecret("Operatörens huvudnyckel");
        try
        {
            using (var session = CaseSession.ForOperator(vault, masterKey))
            {
                Array.Clear(masterKey);
                using var opened = session.Store.OpenWithRecoveryKey(casePath, privateKey, administrator);
                opened.AddOperatorAccess();
                opened.Close();
            }

            console.Out($"Ärendet {casePath} öppnas nu med operatörens valv.");
            return ExitCodes.Success;
        }
        finally
        {
            Array.Clear(masterKey);
        }
    }

    /// <summary>Asks for the private key's password and decrypts the key.</summary>
    /// <param name="console">The console.</param>
    /// <param name="path">The private key's file.</param>
    /// <returns>The key; the caller disposes it.</returns>
    /// <exception cref="CryptographicException">The password is wrong or the file holds no such key.</exception>
    private static ECDiffieHellman ReadPrivateKey(IToolConsole console, string path)
    {
        var password = console.ReadSecret("Lösenord för den privata nyckeln");
        try
        {
            return KeyFiles.ReadPrivate(path, password);
        }
        finally
        {
            Array.Clear(password);
        }
    }

    /// <summary>Asks for a new secret twice and checks its length.</summary>
    /// <param name="console">The console.</param>
    /// <param name="prompt">What is asked for.</param>
    /// <param name="minimumLength">The shortest accepted secret.</param>
    /// <returns>The secret, which the caller clears, or <see langword="null"/> when it was refused.</returns>
    private static char[]? AskNewSecret(IToolConsole console, string prompt, int minimumLength)
    {
        var first = console.ReadSecret(prompt);
        var second = console.ReadSecret(prompt + " igen");
        try
        {
            if (first.Length < minimumLength)
            {
                console.Error($"Fel: minst {minimumLength} tecken.");
            }
            else if (!first.AsSpan().SequenceEqual(second))
            {
                console.Error("Fel: de två skiljer sig åt.");
            }
            else
            {
                return first;
            }

            Array.Clear(first);
            return null;
        }
        finally
        {
            Array.Clear(second);
        }
    }

    /// <summary>Reads the public key built into the Cases module of this build.</summary>
    /// <returns>The key, or <see langword="null"/> when none is configured.</returns>
    private static RecoveryPublicKey? BuiltInKey() => new Modules.Cases.CasesOptions().RecoveryKey;

    /// <summary>Says why a case operation failed, in Swedish.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>The text.</returns>
    private static string Describe(CaseError error) => error switch
    {
        CaseError.NotFound => "ärendefilen finns inte.",
        CaseError.NoAccess => "ärendet har inget kuvert som den här privata nyckeln öppnar.",
        CaseError.Corrupt => "ärendefilen är skadad eller ändrad utanför Chainsaw.",
        CaseError.UnsupportedVersion => "ärendefilen är från en nyare version av Chainsaw.",
        CaseError.InUse => "ärendet är öppet i Chainsaw; stäng det först.",
        CaseError.Conflict => "en arbetskopia från ett avbrutet pass hör till en annan fil; titta på den först.",
        CaseError.NoOperatorProfile => "operatörens valv har ingen profil.",
        CaseError.PasswordTooShort => $"delningslösenordet måste ha minst {SharingPasswordRules.MinimumLength} tecken.",
        _ => error.ToString(),
    };

    /// <summary>Shows how the tool is used.</summary>
    /// <param name="console">The console.</param>
    /// <returns><see cref="ExitCodes.Usage"/>.</returns>
    private static int Usage(IToolConsole console)
    {
        console.Error("Chainsaw.Recovery: organisationens återställningsnyckel");
        console.Error($"  {KeygenCommand} <mapp>                          "
            + "skapa ett nyckelpar (utanför alla git-repon)");
        console.Error($"  {FingerprintCommand} [publik.pem]               "
            + "visa fingeravtrycket (förval: den inbyggda nyckeln)");
        console.Error($"  {CheckCommand} <ärende.cut> [--public <fil>]    har ärendet ett kuvert för nyckeln?");
        console.Error($"  {OpenCommand} <ärende.cut> --key <privat.pem> --admin <namn> [--org <enhet>] --share");
        console.Error("                                         öppna och sätt ett nytt delningslösenord");
        console.Error($"  {OpenCommand} <ärende.cut> --key <privat.pem> --admin <namn> --vault <valvets mapp>");
        console.Error("                                         öppna och lägg till ett kuvert för operatörens valv");
        console.Error("Lösenord och huvudnycklar frågas efter och tas aldrig från kommandoraden.");
        return ExitCodes.Usage;
    }
}
