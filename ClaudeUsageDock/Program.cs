using JPSoftworks.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsageDock;

internal static class Program
{
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
                    new DelegateExtensionFactory(disposedSignal => new PowerCommandExtension(disposedSignal)),
                },
            });
    }
}
