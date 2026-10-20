/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using Nini.Config;
using OpenSim.Region.PhysicsModules.SharedBase;

namespace OpenSim.Region.Framework.Scenes
{
    /// <summary>
    /// [Confluence] A region whose configured physics engine never loaded used to reach "Startup complete" and then
    /// throw a NullReferenceException in Scene.Update on its first heartbeat (found 2026-09-27 through a Jolt build gap,
    /// but any cause does it: a misspelt [Startup] physics, an engine whose native library failed to load). The region
    /// server checks this once the region's modules are loaded and refuses to start the region with a message that says
    /// what was asked for and where to look.
    /// </summary>
    public static class PhysicsEngineStartupCheck
    {
        /// <summary>The names the in-tree engines answer to in [Startup] physics (matched exactly, case and all).</summary>
        public static readonly string[] KnownEngines = { "ubODE", "BulletSim", "basicphysics", "POS", "Jolt" };

        /// <summary>
        /// Null when <paramref name="physics"/> is loaded; otherwise the error to log, naming the configured engine.
        /// </summary>
        public static string Problem(IConfigSource config, PhysicsScene physics, string regionName)
        {
            if (physics is not null)
                return null;

            string configured = config?.Configs["Startup"]?.GetString("physics", string.Empty) ?? string.Empty;

            string asked;
            if (configured.Length == 0)
                asked = "[Startup] physics is not set";
            else
            {
                asked = $"[Startup] physics = \"{configured}\"";
                string close = System.Array.Find(KnownEngines, e => string.Equals(e, configured, System.StringComparison.OrdinalIgnoreCase));
                if (close is not null && close != configured)
                    asked += $" (the engine is called \"{close}\"; the name is matched exactly)";
                else if (close is null)
                    asked += " (not one of " + string.Join(", ", KnownEngines) + ")";
                else
                    asked += " (the engine did not load: see its own error earlier in this log)";
            }

            return $"[PHYSICS]: No physics engine is loaded for region {regionName}: {asked}. " +
                   "Refusing to start the region; it would fail on its first frame.";
        }
    }
}
