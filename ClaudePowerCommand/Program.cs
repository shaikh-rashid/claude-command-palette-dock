using JPSoftworks.CommandPalette.Extensions.Toolkit;

namespace ClaudePowerCommand;

internal static class Program
{
    [MTAThread]
    private static async Task Main(string[] args)
    {
        await ExtensionHostRunner.RunAsync(
            args,
            new ExtensionHostRunnerParameters
            {
                PublisherMoniker = "ClaudePowerCommand",
                ProductMoniker = "ClaudePowerCommand",
                ExtensionFactories = new()
                {
                    new DelegateExtensionFactory(disposedSignal => new PowerCommandExtension(disposedSignal)),
                },
            });
    }
}
