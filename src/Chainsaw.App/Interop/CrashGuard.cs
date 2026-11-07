using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Chainsaw.App.Interop;

/// <summary>
/// Catches what no part of Chainsaw caught, so that one fault never closes the program and loses what is on screen:
/// the fault is said in a box, and its sort and stack trace (never its message, which may hold an address or other case
/// data, principle 4) are written to <c>%LOCALAPPDATA%\Chainsaw\crash.log</c> for the developer.
/// </summary>
public static class CrashGuard
{
    /// <summary>The log's file name.</summary>
    public const string FileName = "crash.log";

    /// <summary>The largest the log grows before it starts over.</summary>
    private const long MaximumLogSize = 1024 * 1024;

    /// <summary>What the box says, before the fault's own message.</summary>
    private const string BoxText =
        "Något gick fel, men Chainsaw fortsätter. Detaljerna (utan ärendets data) står i {0}.\n\n{1}";

    /// <summary>Guards the application: the UI thread, other threads and tasks that nobody waited for.</summary>
    /// <param name="application">The application.</param>
    public static void Guard(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.DispatcherUnhandledException += OnUiFault;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Write(exception);
            }
        };
    }

    /// <summary>Gives the line written for a fault: when, its sort and its stack trace, inner faults too.</summary>
    /// <param name="exception">The fault.</param>
    /// <param name="at">When it happened.</param>
    /// <returns>The text, without any fault's message.</returns>
    public static string Describe(Exception exception, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = new StringBuilder();
        text.Append(at.ToString("O", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        for (var fault = exception; fault is not null; fault = fault.InnerException)
        {
            text.Append(fault.GetType().FullName).AppendLine();
            text.Append(fault.StackTrace).AppendLine();
        }

        return text.AppendLine().ToString();
    }

    /// <summary>Says a fault on the UI thread in a box and keeps Chainsaw running.</summary>
    /// <param name="sender">The dispatcher.</param>
    /// <param name="e">The fault.</param>
    private static void OnUiFault(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var path = Write(e.Exception);
        e.Handled = true;
        MessageBox.Show(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, BoxText, path, e.Exception.Message),
            "Chainsaw", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Writes a fault to the log; a log that cannot be written is left.</summary>
    /// <param name="exception">The fault.</param>
    /// <returns>The log's path.</returns>
    private static string Write(Exception exception)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw");
        var path = Path.Combine(folder, FileName);
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(path) && new FileInfo(path).Length > MaximumLogSize)
            {
                File.Delete(path);
            }

            File.AppendAllText(path, Describe(exception, DateTimeOffset.Now));
        }
        catch (Exception fault) when (fault is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the box still says what went wrong.
        }

        return path;
    }
}
