/*
 * Confluence-only file, not part of the Tranquillity Phlox sources.
 *
 * Tranquillity finds region modules by scanning for INonSharedRegionModule implementers; Confluence finds them
 * through Mono.Addins. This registers Phlox with Mono.Addins without editing PhloxEngine.cs. The engine still decides
 * for itself whether it runs ([InWorldz.Phlox] Enabled) and which scripts it takes ([Startup] DefaultScriptEngine
 * and a script's first-line engine header); see doc/PhloxSetup.md.
 */

using Mono.Addins;

[assembly: Addin("Phlox.ScriptEngine", OpenSim.VersionInfo.AssemblyVersionNumber)]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.AssemblyVersionNumber)]

namespace Phlox.ScriptEngine
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "PhloxEngine")]
    public class ConfluencePhloxEngine : PhloxEngine
    {
    }
}
