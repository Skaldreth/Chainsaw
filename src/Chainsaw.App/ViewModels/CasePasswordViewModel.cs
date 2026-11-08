using System.Collections.ObjectModel;
using Chainsaw.Modules.Cases.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// One of the small envelope cards in the dialog for a colleague's file (sketch ÄR1b): the key that opens it, the
/// one the password opens lit, and the envelope that is added for this vault dashed until it is there.
/// </summary>
/// <param name="kind">The envelope's kind.</param>
/// <param name="isLit">Whether it is the one opened now, the case password's, in the accent.</param>
/// <param name="isPending">Whether it is this vault's envelope that will be added, dashed.</param>
/// <param name="isAdded">Whether it is this vault's envelope that was just added, in green.</param>
public sealed class EnvelopeChip(
    CaseEnvelopeKind kind, bool isLit = false, bool isPending = false, bool isAdded = false)
{
    /// <summary>Gets the envelope's kind, which picks its icon.</summary>
    public CaseEnvelopeKind Kind { get; } = kind;

    /// <summary>Gets the label under the icon.</summary>
    public string Label { get; } = EnvelopeWords.Label(kind);

    /// <summary>Gets what the envelope is, on hovering it.</summary>
    public string Tip { get; } = isPending ? "Läggs till när filen öppnas" : EnvelopeWords.Tip(kind);

    /// <summary>Gets whether it is the envelope the password opens, lit in the accent.</summary>
    public bool IsLit { get; } = isLit;

    /// <summary>Gets whether it is this vault's envelope that will be added, dashed.</summary>
    public bool IsPending { get; } = isPending;

    /// <summary>Gets whether it is this vault's envelope that was just added, in green.</summary>
    public bool IsAdded { get; } = isAdded;
}

/// <summary>
/// The dialog that opens a colleague's file with its case password (sketch ÄR1b): the file, its envelopes as small
/// cards, the password field, <em>Lägg till ett kuvert för mig</em>, and afterwards that the envelope is there. The
/// password is read from the field only when Öppna is pressed and cleared at once; it is never a property. It knows
/// nothing of WPF, so that it is tested.
/// </summary>
public sealed partial class CasePasswordViewModel : ObservableObject
{
    /// <summary>What is said once the case is open and this vault has its envelope.</summary>
    public const string DoneText = "Ditt kuvert ligger i filen. Nästa gång räcker huvudnyckeln.";

    /// <summary>What is said once the case is open without an envelope for this vault.</summary>
    public const string OpenedWithoutEnvelopeText =
        "Ärendet är öppet. Nästa gång behövs lösenordet igen, eftersom du inte har något eget kuvert.";

    /// <summary>The cases, which open the file.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>The file offered now, or <see langword="null"/>.</summary>
    private CaseFileOffer? _offer;

    /// <summary>The file's envelopes as they were read, or none when the file could not be read.</summary>
    private IReadOnlyList<CaseEnvelope> _envelopes = [];

    /// <summary>Creates the dialog, closed.</summary>
    /// <param name="workflow">The cases.</param>
    public CasePasswordViewModel(CaseWorkflow workflow)
    {
        _workflow = workflow;
    }

    /// <summary>Gets the envelopes as small cards.</summary>
    public ObservableCollection<EnvelopeChip> Envelopes { get; } = [];

    /// <summary>Gets whether the dialog is shown.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets the case number, the file's name.</summary>
    [ObservableProperty]
    public partial string CaseNumber { get; private set; } = string.Empty;

    /// <summary>Gets where the file lies and its size, for example <c>Från en annan plats · 4,2 MB</c>.</summary>
    [ObservableProperty]
    public partial string FileText { get; private set; } = string.Empty;

    /// <summary>Gets or sets whether this vault gets an envelope of its own; on from the start.</summary>
    [ObservableProperty]
    public partial bool AddOwnEnvelope { get; set; } = true;

    /// <summary>Gets whether the last password did not open the file.</summary>
    [ObservableProperty]
    public partial bool IsWrong { get; private set; }

    /// <summary>Gets what went wrong otherwise, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether the file is being opened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets whether nothing runs, so that Öppna can be pressed.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>Gets whether the case is open, so that the dialog says so and offers <em>Till grafen</em>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    public partial bool IsDone { get; private set; }

    /// <summary>Gets what is said once the case is open.</summary>
    [ObservableProperty]
    public partial string DoneMessage { get; private set; } = DoneText;

    /// <summary>Gets whether the dialog asks for the password.</summary>
    public bool IsAsking => !IsDone;

    /// <summary>Gets whether <see cref="ErrorText"/> has something to say.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Shows the dialog for a file and reads its envelopes off the UI thread.</summary>
    /// <param name="offer">The file.</param>
    /// <returns>A task that completes when the envelopes are shown.</returns>
    public async Task ShowAsync(CaseFileOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        _offer = offer;
        _envelopes = [];
        CaseNumber = offer.CaseNumber;
        FileText = offer.InFolder
            ? $"I mappen Ärenden · {EnvelopeWords.Size(offer.Size)}"
            : $"Från en annan plats · {EnvelopeWords.Size(offer.Size)} · kopieras till Ärenden";
        IsWrong = false;
        ErrorText = string.Empty;
        IsDone = false;
        IsBusy = false;
        AddOwnEnvelope = true;
        ShowEnvelopes();
        IsOpen = true;
        var envelopes = await _workflow.DescribeEnvelopesAsync(offer.Path);
        if (ReferenceEquals(_offer, offer))
        {
            _envelopes = envelopes ?? [];
            ShowEnvelopes();
        }
    }

    /// <summary>Turns <em>Lägg till ett kuvert för mig</em> on or off.</summary>
    [RelayCommand]
    public void ToggleAdd()
    {
        if (!IsDone)
        {
            AddOwnEnvelope = !AddOwnEnvelope;
        }
    }

    /// <summary>
    /// Öppna (Enter): takes the password from the field, opens the file with it and clears it. A wrong password says
    /// so and changes nothing.
    /// </summary>
    /// <param name="password">The password field.</param>
    /// <returns>A task that completes when the case is open, or why not is said.</returns>
    public async Task OpenAsync(ISecretEntry password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var secret = password.Take();
        try
        {
            if (_offer is not { } offer || IsBusy || IsDone)
            {
                return;
            }

            if (secret.Length == 0)
            {
                IsWrong = true;
                return;
            }

            IsBusy = true;
            IsWrong = false;
            ErrorText = string.Empty;
            var result = await _workflow.OpenWithPasswordAsync(offer, secret, AddOwnEnvelope);
            IsBusy = false;
            ShowResult(result);
        }
        finally
        {
            Secret.Clear(secret);
        }
    }

    /// <summary>Closes the dialog (Avbryt, Esc, or <em>Till grafen</em> once the case is open).</summary>
    [RelayCommand]
    public void Close()
    {
        if (IsBusy)
        {
            return;
        }

        IsOpen = false;
        _offer = null;
    }

    /// <summary>Redraws the envelope cards when the choice to add an envelope changes.</summary>
    /// <param name="value">The choice.</param>
    partial void OnAddOwnEnvelopeChanged(bool value) => ShowEnvelopes();

    /// <summary>Shows how the opening went: the case open with or without an envelope, or why not.</summary>
    /// <param name="result">How it went.</param>
    private void ShowResult(PasswordOpening result)
    {
        if (result.IsOpen)
        {
            DoneMessage = result.EnvelopeAdded ? DoneText : OpenedWithoutEnvelopeText;
            ErrorText = result.Reply.IsError ? result.Reply.Text : string.Empty;
            if (result.EnvelopeAdded)
            {
                FileText = FileText.Replace("kopieras till", "kopierad till", StringComparison.Ordinal);
            }

            IsDone = true;
            ShowEnvelopes(added: result.EnvelopeAdded);
            return;
        }

        IsWrong = result.IsWrongPassword;
        ErrorText = result.IsWrongPassword ? string.Empty : result.Reply.Text;
    }

    /// <summary>
    /// Lays out the small cards: the file's envelopes, the case password's lit while it is asked for, and this
    /// vault's dashed while it is to be added, green once it is.
    /// </summary>
    /// <param name="added">Whether this vault's envelope was just added.</param>
    private void ShowEnvelopes(bool added = false)
    {
        Envelopes.Clear();
        foreach (var envelope in _envelopes)
        {
            var isLit = !IsDone && envelope.Kind == CaseEnvelopeKind.SharingPassword;
            Envelopes.Add(new EnvelopeChip(envelope.Kind, isLit));
        }

        var hasOwn = _envelopes.Any(envelope => envelope.Kind == CaseEnvelopeKind.Yours);
        if (added && !hasOwn)
        {
            Envelopes.Add(new EnvelopeChip(CaseEnvelopeKind.Yours, isAdded: true));
        }
        else if (!IsDone && AddOwnEnvelope && !hasOwn)
        {
            Envelopes.Add(new EnvelopeChip(CaseEnvelopeKind.Yours, isPending: true));
        }
    }
}
