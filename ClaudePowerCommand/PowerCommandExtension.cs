using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace ClaudePowerCommand;

[Guid("3d846f79-95c3-4052-b244-2fa775e4601d")]
public sealed partial class PowerCommandExtension(ManualResetEvent extensionDisposedSignal) : IExtension, IDisposable
{
    private readonly PowerCommandProvider _commandProvider = new();

    public object? GetProvider(ProviderType providerType)
    {
        return providerType == ProviderType.Commands ? _commandProvider : null;
    }

    public void Dispose()
    {
        extensionDisposedSignal.Set();
    }
}
