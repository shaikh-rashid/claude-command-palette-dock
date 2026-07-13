using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace ClaudeUsageDock;

/// <summary>
/// The COM object Command Palette instantiates to talk to this extension. The GUID
/// must match the CreateInstance class id declared in Package.appxmanifest —
/// change one and the other must follow. CmdPal asks it for providers by type;
/// everything this extension offers (commands, dock bands, settings) hangs off the
/// single <see cref="PowerCommandProvider"/>.
/// </summary>
[Guid("3d846f79-95c3-4052-b244-2fa775e4601d")]
public sealed partial class PowerCommandExtension(ManualResetEvent extensionDisposedSignal) : IExtension, IDisposable
{
    private readonly PowerCommandProvider _commandProvider = new();

    /// <summary>Hands CmdPal the command provider; other provider types aren't offered.</summary>
    public object? GetProvider(ProviderType providerType)
    {
        return providerType == ProviderType.Commands ? _commandProvider : null;
    }

    /// <summary>
    /// Called when CmdPal releases the extension. Setting the signal lets the
    /// host process (parked in <see cref="Program.Main"/>) shut down.
    /// </summary>
    public void Dispose()
    {
        extensionDisposedSignal.Set();
    }
}
