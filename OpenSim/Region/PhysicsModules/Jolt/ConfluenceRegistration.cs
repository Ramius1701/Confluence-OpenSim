/*
 * Confluence-only file, not part of the Legion Jolt sources.
 *
 * Tranquillity finds region modules through IPluginRegistryProvider (the Legion PluginRegistration.cs, which is not
 * carried here); Confluence finds them through Mono.Addins. This registers the module with Mono.Addins and hands
 * every call to the Legion JoltModule, which does nothing unless [Startup] physics = Jolt.
 */

using System.Reflection;
using System.Runtime.InteropServices;
using Mono.Addins;
using Nini.Config;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

[assembly: AssemblyTitle("OpenSim.Region.PhysicsModules.Jolt")]
[assembly: AssemblyDescription("Jolt physics region module (from Legion's Tranquillity lane/jolt)")]
[assembly: AssemblyCompany("http://opensimulator.org")]
[assembly: AssemblyProduct("OpenSim")]
[assembly: AssemblyCopyright("OpenSimulator developers; Jolt module (c) Legion Builds, MPL-2.0")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion(OpenSim.VersionInfo.AssemblyVersionNumber)]

[assembly: Addin("OpenSim.Region.PhysicsModules.Jolt", OpenSim.VersionInfo.AssemblyVersionNumber)]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.AssemblyVersionNumber)]

namespace OpenSim.Region.PhysicsModules.Jolt
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "JoltPhysicsScene")]
    public sealed class ConfluenceJoltModule : INonSharedRegionModule
    {
        private readonly JoltModule m_module = new JoltModule();

        public string Name => m_module.Name;
        public System.Type ReplaceableInterface => m_module.ReplaceableInterface;
        public void Initialise(IConfigSource source) => m_module.Initialise(source);
        public void AddRegion(Scene scene) => m_module.AddRegion(scene);
        public void RegionLoaded(Scene scene) => m_module.RegionLoaded(scene);
        public void RemoveRegion(Scene scene) => m_module.RemoveRegion(scene);
        public void Close() => m_module.Close();
    }
}
