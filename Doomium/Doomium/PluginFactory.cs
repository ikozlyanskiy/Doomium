#nullable enable
using System.Runtime.InteropServices;
using DXP;

[assembly: ComVisible(true)]

namespace CSharpPlugin;

public interface IPluginFactory
{
    object InvokePluginFactory(IClient client);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PluginFactory : IPluginFactory
{
    public object InvokePluginFactory(IClient client)
    {
        Doomium.DoomiumTrace.Write("PluginFactory entered");
        try
        {
            var module = new Doomium.DoomiumServerModule();
            Doomium.DoomiumTrace.Write("PluginFactory returned server module");
            return module;
        }
        catch (Exception ex)
        {
            Doomium.DoomiumTrace.Write("PluginFactory failed: " + ex);
            throw;
        }
    }
}
