using System.Diagnostics;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Monitoring.Contracts;
using Microsoft.Win32;

namespace Chainsaw.App.Interop;

/// <summary>
/// The background process next to <c>Chainsaw.exe</c> (<see cref="BackgroundNames.Executable"/>): whether it runs is
/// whether its named mutex exists; it is started without a window and asked to stop with its named event.
/// </summary>
public sealed class WindowsBackgroundProcess : IBackgroundProcess
{
    /// <summary>The program.</summary>
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, BackgroundNames.Executable);

    /// <inheritdoc/>
    public bool Exists => File.Exists(_path);

    /// <inheritdoc/>
    public string Command => $"\"{_path}\"";

    /// <inheritdoc/>
    public bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(BackgroundNames.Mutex, out var mutex))
        {
            return false;
        }

        mutex.Dispose();
        return true;
    }

    /// <inheritdoc/>
    public bool Start()
    {
        if (!Exists)
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(_path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (EventWaitHandle.TryOpenExisting(BackgroundNames.StopEvent, out var stop))
        {
            using (stop)
            {
                stop.Set();
            }
        }
    }
}

/// <summary>
/// Starts the background process at sign-in through the user's <c>Run</c> key (HKCU), never the machine's.
/// </summary>
public sealed class RunKeyAutostart : IAutostart
{
    /// <summary>The user's <c>Run</c> key.</summary>
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <inheritdoc/>
    public bool IsSet()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(BackgroundNames.RunValue) is string;
    }

    /// <inheritdoc/>
    public void Set(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (command is null)
        {
            key.DeleteValue(BackgroundNames.RunValue, throwOnMissingValue: false);
        }
        else if (key.GetValue(BackgroundNames.RunValue) as string != command)
        {
            key.SetValue(BackgroundNames.RunValue, command, RegistryValueKind.String);
        }
    }
}
