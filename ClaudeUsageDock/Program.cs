using JPSoftworks.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

/// <summary>
/// Entry point. Command Palette extensions are out-of-process COM servers: CmdPal
/// activates this executable with a registration cookie in <c>args</c>, and the
/// host runner registers <see cref="PowerCommandExtension"/> as the COM class,
/// then blocks until CmdPal releases it (which fires the disposed signal below).
/// </summary>
internal static class Program
{
    // MTA rather than STA: WinRT COM servers are activated on the multithreaded
    // apartment, and CmdPal calls in from its own threads.
    [MTAThread]
    private static async Task Main(string[] args)
    {
        await ExtensionHostRunner.RunAsync(
            args,
            new ExtensionHostRunnerParameters
            {
                PublisherMoniker = "ClaudeUsageDock",
                ProductMoniker = "ClaudeUsageDock",
                ExtensionFactories = new()
                {
                    // The disposed signal is how the host knows it may exit: it is
                    // set in PowerCommandExtension.Dispose() when CmdPal lets go.
                    new DelegateExtensionFactory(disposedSignal => new PowerCommandExtension(disposedSignal)),
                },
            });
    }
}
