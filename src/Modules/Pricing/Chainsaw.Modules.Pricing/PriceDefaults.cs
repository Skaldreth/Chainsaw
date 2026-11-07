using Chainsaw.Modules.Pricing.Contracts;

namespace Chainsaw.Modules.Pricing;

/// <summary>The currency and resolution chosen in the settings, read by every request that names none.</summary>
/// <param name="options">The module's settings, which give the values at the start.</param>
internal sealed class PriceDefaults(PricingOptions options) : IPriceDefaults
{
    /// <summary>The currency and resolution, replaced together so that a request never sees half a change.</summary>
    private volatile Choice _choice = new(options.Currency, options.Resolution);

    /// <inheritdoc/>
    public FiatCurrency Currency => _choice.Currency;

    /// <inheritdoc/>
    public PriceResolution Resolution => _choice.Resolution;

    /// <summary>Gets both values at once, as one request uses them.</summary>
    public (FiatCurrency Currency, PriceResolution Resolution) Both
    {
        get
        {
            var choice = _choice;
            return (choice.Currency, choice.Resolution);
        }
    }

    /// <inheritdoc/>
    public void Choose(FiatCurrency currency, PriceResolution resolution)
    {
        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), currency, "Unknown currency.");
        }

        if (!Enum.IsDefined(resolution))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown resolution.");
        }

        _choice = new Choice(currency, resolution);
    }

    /// <summary>The two values chosen together.</summary>
    /// <param name="Currency">The currency.</param>
    /// <param name="Resolution">The resolution.</param>
    private sealed record Choice(FiatCurrency Currency, PriceResolution Resolution);
}
