using RetailCorrector.Plugins;
using System.Runtime.Loader;

namespace OfdRu;

public class Plugin : SourcePlugin
{
    public override string Name { get; } = "OFD.ru";

    public override Task OnLoad(AssemblyLoadContext ctx)
    {
        throw new NotImplementedException();
    }

    public override Task OnUnload()
    {
        throw new NotImplementedException();
    }

    public override Task<IEnumerable<Receipt>> Parse(CancellationToken token)
    {
        throw new NotImplementedException();
    }
}