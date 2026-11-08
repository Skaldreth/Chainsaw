using System.Reflection;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>A case that counts its write transactions and passes every call on to the real case.</summary>
public class WriteCountingCase : DispatchProxy
{
    private ICase? _inner;

    /// <summary>Gets how many write transactions were opened.</summary>
    public int Writes { get; private set; }

    /// <summary>Wraps a case.</summary>
    /// <param name="inner">The real case.</param>
    /// <returns>The counting case and its counter.</returns>
    public static (ICase Case, WriteCountingCase Counter) Wrap(ICase inner)
    {
        var proxy = Create<ICase, WriteCountingCase>();
        var counter = (WriteCountingCase)(object)proxy;
        counter._inner = inner;
        return (proxy, counter);
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name == nameof(ICase.Write))
        {
            Writes++;
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }
}
