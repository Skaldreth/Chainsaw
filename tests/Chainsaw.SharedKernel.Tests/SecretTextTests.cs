using System.Text;
using Chainsaw.SharedKernel.Text;

namespace Chainsaw.SharedKernel.Tests;

public sealed class SecretTextTests
{
    [Fact]
    public void A_decomposed_secret_becomes_the_composed_one()
    {
        const string Composed = "åäö är grå";
        var decomposed = Composed.Normalize(NormalizationForm.FormD);

        var normalized = SecretText.Normalize(decomposed);

        Assert.Equal(Composed, new string(normalized));
        Assert.False(SecretText.IsNormalized(decomposed));
        Assert.True(SecretText.IsNormalized(Composed));
    }

    [Fact]
    public void Clear_overwrites_every_character()
    {
        var secret = SecretText.Normalize("hemligt");

        SecretText.Clear(secret);

        Assert.All(secret, character => Assert.Equal('\0', character));
    }

    [Fact]
    public void A_lone_surrogate_becomes_the_replacement_character_and_is_not_refused()
    {
        const string Lone = "nyckel\uD800mitt i\uDC00";

        var normalized = new string(SecretText.Normalize(Lone));

        Assert.Equal("nyckel\uFFFDmitt i\uFFFD", normalized);
        Assert.False(SecretText.IsNormalized(Lone));
        Assert.Equal("a\uD83D\uDE00", new string(SecretText.Normalize("a\uD83D\uDE00")));
    }

    [Fact]
    public void An_empty_secret_stays_empty()
    {
        Assert.Empty(SecretText.Normalize(string.Empty));
    }
}
