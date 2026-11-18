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

// [Confluence] From OpenSim-Tranquillity #402 (cfb213d044), moved into the Phlox test project.

using OpenMetaverse.StructuredData;
using OpenSim.Tests.Common;

using Xunit;
using OpenSim.Region.Framework.Scenes;

using OpenMetaverse;
using OpenSim.Framework;
using System.Collections.Generic;
namespace InWorldz.Phlox.Tests
{
    /// <summary>
    /// GltfMaterialOverride: the per-face glTF override store both script engines write through. The stored data must
    /// be the compact LLSD the viewer reads (LLGLTFMaterial::applyOverrideLLSD), since LLClientView sends it as is.
    /// </summary>
    public class GltfMaterialOverrideTests : OpenSimTestCase
    {
        private static readonly UUID Material = new("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly UUID OtherMaterial = new("aaaaaaaa-0000-0000-0000-000000000002");
        private static readonly UUID Normal = new("bbbbbbbb-0000-0000-0000-000000000001");
        private static readonly UUID Emissive = new("bbbbbbbb-0000-0000-0000-000000000002");

        private static SceneObjectPart NewPart()
        {
            TestScene scene = new SceneHelpers().SetupScene();
            return SceneHelpers.AddSceneObject(scene).RootPart;
        }

        private static OSDMap Stored(SceneObjectPart part, int face)
        {
            foreach (var ovr in part.Shape.RenderMaterials.overrides)
            {
                if (ovr.te_index == face)
                    return (OSDMap)OSDParser.DeserializeLLSDNotation(ovr.data);
            }
            return null;
        }

        [Fact]
        public void TheStoredDataUsesTheViewersCompactKeys()
        {
            TestHelpers.InMethod();
            GltfMaterialOverride ovr = new();
            ovr.SetTexture(GltfMaterialOverride.SLOT_NORMAL, Normal);
            ovr.SetScale(GltfMaterialOverride.SLOT_BASE_COLOR, new Vector2(2, 3));
            ovr.SetOffset(GltfMaterialOverride.SLOT_BASE_COLOR, new Vector2(0.25f, -0.5f));
            ovr.SetRotation(GltfMaterialOverride.SLOT_BASE_COLOR, 1.5f);
            ovr.BaseColor = new Vector3(1, 0, 0);
            ovr.BaseAlpha = 0.5f;
            ovr.EmissiveColor = new Vector3(0, 1, 0);
            ovr.MetallicFactor = 0.25f;
            ovr.RoughnessFactor = 0.75f;
            ovr.AlphaMode = GltfMaterialOverride.ALPHA_MODE_MASK;
            ovr.AlphaCutoff = 0.3f;
            ovr.DoubleSided = true;

            var map = (OSDMap)OSDParser.DeserializeLLSDNotation(ovr.ToData());

            var tex = (OSDArray)map["tex"];
            Assert.Equal(2, tex.Count);
            Assert.Equal(OSDType.Unknown, tex[0].Type);
            Assert.Equal(Normal, tex[1].AsUUID());

            var ti = (OSDMap)((OSDArray)map["ti"])[0];
            Assert.Equal(2, ((OSDArray)ti["s"])[0].AsReal());
            Assert.Equal(3, ((OSDArray)ti["s"])[1].AsReal());
            Assert.Equal(0.25, ((OSDArray)ti["o"])[0].AsReal());
            Assert.Equal(-0.5, ((OSDArray)ti["o"])[1].AsReal());
            Assert.Equal(1.5, ti["r"].AsReal());

            Assert.Equal(new double[] { 1, 0, 0, 0.5 }, ((OSDArray)map["bc"]).Select(o => o.AsReal()));
            Assert.Equal(new double[] { 0, 1, 0 }, ((OSDArray)map["ec"]).Select(o => o.AsReal()));
            Assert.Equal(OSDType.Real, map["mf"].Type);
            Assert.Equal(0.25, map["mf"].AsReal());
            Assert.Equal(0.75, map["rf"].AsReal());
            Assert.Equal(OSDType.Integer, map["am"].Type);
            Assert.Equal(2, map["am"].AsInteger());
            Assert.Equal(0.3, map["ac"].AsReal(), 1e-6);
            Assert.Equal(OSDType.Boolean, map["ds"].Type);
            Assert.True(map["ds"].AsBoolean());
            Assert.Equivalent(new[] { "tex", "ti", "bc", "ec", "mf", "rf", "am", "ac", "ds" }, map.Keys);
        }

        [Fact]
        public void DataRoundTrips()
        {
            TestHelpers.InMethod();
            GltfMaterialOverride ovr = new();
            ovr.SetTexture(GltfMaterialOverride.SLOT_EMISSIVE, Emissive);
            ovr.SetRotation(GltfMaterialOverride.SLOT_NORMAL, 0.5f);
            ovr.RoughnessFactor = 0.2f;
            ovr.DoubleSided = false;

            GltfMaterialOverride back = GltfMaterialOverride.FromData(ovr.ToData());

            Assert.Equal(Emissive, back.GetTexture(GltfMaterialOverride.SLOT_EMISSIVE));
            Assert.Null(back.GetTexture(GltfMaterialOverride.SLOT_BASE_COLOR));
            Assert.Equal(0.5f, back.GetRotation(GltfMaterialOverride.SLOT_NORMAL));
            Assert.Null(back.GetScale(GltfMaterialOverride.SLOT_NORMAL));
            Assert.Equal(0.2, (double)back.RoughnessFactor, 1e-6);
            Assert.Null(back.MetallicFactor);
            Assert.False(back.DoubleSided);
            Assert.Null(back.BaseColor);
            Assert.Equal(ovr.ToData(), back.ToData());
        }

        [Fact]
        public void AnAlphaAloneOverridesTheBaseColorAsWhite()
        {
            TestHelpers.InMethod();
            // SL llSetLinkGLTFOverrides: OVERRIDE_GLTF_BASE_COLOR_FACTOR and OVERRIDE_GLTF_BASE_ALPHA are coupled
            GltfMaterialOverride ovr = new() { BaseAlpha = 0.4f };

            var map = (OSDMap)OSDParser.DeserializeLLSDNotation(ovr.ToData());

            Assert.Equal(new double[] { 1, 1, 1, 0.4 }, ((OSDArray)map["bc"]).Select(o => o.AsReal()));
        }

        [Fact]
        public void NothingOverriddenStoresNothing()
        {
            TestHelpers.InMethod();
            Assert.Null(new GltfMaterialOverride().ToData());
        }

        [Fact]
        public void DataWrittenByTheMaterialsModuleIsRead()
        {
            TestHelpers.InMethod();
            // as MaterialsModule.AddMaterialOverride stores a viewer edit
            string data = "{'bc':[r0.5,r0.25,r1,r1],'mf':r0,'ti':[{'o':[r0.1,r0.2],'r':r0.7,'s':[r4,r5]}]," +
                          "'tex':[u" + Normal + ",!,!,u" + Emissive + "]}";

            GltfMaterialOverride ovr = GltfMaterialOverride.FromData(data);

            Assert.Equal(new Vector3(0.5f, 0.25f, 1f), ovr.BaseColor);
            Assert.Equal(1f, ovr.BaseAlpha);
            Assert.Equal(0f, ovr.MetallicFactor);
            Assert.Equal(new Vector2(0.1f, 0.2f), ovr.GetOffset(0));
            Assert.Equal(new Vector2(4, 5), ovr.GetScale(0));
            Assert.Equal(0.7, (double)ovr.GetRotation(0), 1e-6);
            Assert.Equal(Normal, ovr.GetTexture(0));
            Assert.Null(ovr.GetTexture(1));
            Assert.Equal(Emissive, ovr.GetTexture(3));
        }

        [Fact]
        public void DataWrittenByEarlierPhloxBuildsIsRepaired()
        {
            TestHelpers.InMethod();
            var legacy = new OSDMap
            {
                ["tex"] = OSD.FromString(Normal.ToString()),
                ["rep"] = new OSDArray { 2.0, 2.0 },
                ["off"] = new OSDArray { 0.0, 0.5 },
                ["rot"] = 0.25,
                ["etex"] = OSD.FromString(Emissive.ToString()),
                ["bc"] = new OSDArray { 1.0, 0.0, 0.0, 1.0 },
                ["am"] = 1,
            };

            GltfMaterialOverride ovr = GltfMaterialOverride.FromData(OSDParser.SerializeLLSDNotation(legacy));
            var map = (OSDMap)OSDParser.DeserializeLLSDNotation(ovr.ToData());

            Assert.Equal(Normal, ovr.GetTexture(GltfMaterialOverride.SLOT_BASE_COLOR));
            Assert.Equal(new Vector2(2, 2), ovr.GetScale(GltfMaterialOverride.SLOT_BASE_COLOR));
            Assert.Equal(new Vector2(0, 0.5f), ovr.GetOffset(GltfMaterialOverride.SLOT_BASE_COLOR));
            Assert.Equal(0.25f, ovr.GetRotation(GltfMaterialOverride.SLOT_BASE_COLOR));
            Assert.Equal(Emissive, ovr.GetTexture(GltfMaterialOverride.SLOT_EMISSIVE));
            Assert.Equal(1, ovr.AlphaMode);
            Assert.Equivalent(new[] { "tex", "ti", "bc", "am" }, map.Keys);
            Assert.IsType<OSDArray>(map["tex"]);
        }

        [Fact]
        public void UnreadableDataReadsAsNoOverride()
        {
            TestHelpers.InMethod();
            Assert.True(GltfMaterialOverride.FromData("{not llsd").IsEmpty);
            Assert.True(GltfMaterialOverride.FromData(null).IsEmpty);
        }

        [Fact]
        public void EditingAFaceWithAMaterialStoresTheOverride()
        {
            TestHelpers.InMethod();
            SceneObjectPart part = NewPart();
            Assert.True(GltfMaterialOverride.SetRenderMaterial(part, 2, Material));

            Assert.True(GltfMaterialOverride.Edit(part, 2, o => { o.MetallicFactor = 0.5f; o.AlphaMode = 1; }));

            OSDMap map = Stored(part, 2);
            Assert.Equal(0.5, map["mf"].AsReal());
            Assert.Equal(1, map["am"].AsInteger());
            Assert.Equal(0.5f, GltfMaterialOverride.Get(part.Shape, 2).MetallicFactor);
        }

        [Fact]
        public void EditingTheSameValueAgainChangesNothing()
        {
            TestHelpers.InMethod();
            SceneObjectPart part = NewPart();
            GltfMaterialOverride.SetRenderMaterial(part, 0, Material);
            GltfMaterialOverride.Edit(part, 0, o => o.RoughnessFactor = 0.1f);

            Assert.False(GltfMaterialOverride.Edit(part, 0, o => o.RoughnessFactor = 0.1f));
        }

        [Fact]
        public void EditsMergeWithWhatIsAlreadyThere()
        {
            TestHelpers.InMethod();
            SceneObjectPart part = NewPart();
            GltfMaterialOverride.SetRenderMaterial(part, 0, Material);
            GltfMaterialOverride.Edit(part, 0, o => o.BaseColor = new Vector3(1, 0, 0));

            GltfMaterialOverride.Edit(part, 0, o => o.SetTexture(GltfMaterialOverride.SLOT_NORMAL, Normal));

            GltfMaterialOverride ovr = GltfMaterialOverride.Get(part.Shape, 0);
            Assert.Equal(new Vector3(1, 0, 0), ovr.BaseColor);
            Assert.Equal(Normal, ovr.GetTexture(GltfMaterialOverride.SLOT_NORMAL));
        }

        [Fact]
        public void ClearingTheLastOverrideRemovesTheEntryButKeepsRenderMaterials()
        {
            TestHelpers.InMethod();
            SceneObjectPart part = NewPart();
            GltfMaterialOverride.SetRenderMaterial(part, 0, Material);
            GltfMaterialOverride.Edit(part, 0, o => o.MetallicFactor = 0.5f);

            Assert.True(GltfMaterialOverride.Edit(part, 0, o => o.MetallicFactor = null));

            Assert.NotNull(part.Shape.RenderMaterials);
            Assert.Null(part.Shape.RenderMaterials.overrides);
        }

        [Fact]
        public void OnAFaceWithNoMaterialOnlyTransformsAreKept()
        {
            TestHelpers.InMethod();
            // SL GLTF Overrides: setting an override while PRIM_RENDER_MATERIAL is NULL_KEY clears most overrides,
            // texture transforms excepted
            SceneObjectPart part = NewPart();

            Assert.True(GltfMaterialOverride.Edit(part, 1, o =>
            {
                o.MetallicFactor = 0.5f;
                o.SetTexture(GltfMaterialOverride.SLOT_BASE_COLOR, Normal);
                o.SetScale(GltfMaterialOverride.SLOT_BASE_COLOR, new Vector2(3, 3));
            }));

            OSDMap map = Stored(part, 1);
            Assert.Equivalent(new[] { "ti" }, map.Keys);
        }

        [Fact]
        public void AllSidesEditsEveryFace()
        {
            TestHelpers.InMethod();
            SceneObjectPart part = NewPart();
            int sides = part.GetNumberOfSides();
            GltfMaterialOverride.SetRenderMaterial(part, -1, Material);

            GltfMaterialOverride.Edit(part, -1, o => o.DoubleSided = true);

            Assert.Equal(sides, System.Linq.Enumerable.Count(part.Shape.RenderMaterials.overrides));
            for (int f = 0; f < sides; f++)
                Assert.True(GltfMaterialOverride.Get(part.Shape, f).DoubleSided);
        }

        [Fact]
        public void AFaceThatDoesNotExistChangesNothing()
        {
            TestHelpers.InMethod();
            SceneObjectPart part = NewPart();
            int sides = part.GetNumberOfSides();

            Assert.False(GltfMaterialOverride.SetRenderMaterial(part, sides, Material));
            Assert.False(GltfMaterialOverride.Edit(part, sides, o => o.DoubleSided = true));
            Assert.Null(part.Shape.RenderMaterials);
        }

        [Fact]
        public void ANewMaterialClearsOverridesButKeepsTransforms()
        {
            TestHelpers.InMethod();
            // SL llSetRenderMaterial "will clear most PRIM_GLTF_* properties on the face", except "repeats, offsets,
            // and rotation_in_radians"
            SceneObjectPart part = NewPart();
            GltfMaterialOverride.SetRenderMaterial(part, 0, Material);
            GltfMaterialOverride.Edit(part, 0, o =>
            {
                o.BaseColor = new Vector3(0, 0, 1);
                o.SetTexture(GltfMaterialOverride.SLOT_NORMAL, Normal);
                o.SetOffset(GltfMaterialOverride.SLOT_NORMAL, new Vector2(0.5f, 0.5f));
            });

            Assert.True(GltfMaterialOverride.SetRenderMaterial(part, 0, OtherMaterial));

            Assert.Equal(OtherMaterial, GltfMaterialOverride.GetRenderMaterial(part.Shape, 0));
            GltfMaterialOverride ovr = GltfMaterialOverride.Get(part.Shape, 0);
            Assert.Null(ovr.BaseColor);
            Assert.Null(ovr.GetTexture(GltfMaterialOverride.SLOT_NORMAL));
            Assert.Equal(new Vector2(0.5f, 0.5f), ovr.GetOffset(GltfMaterialOverride.SLOT_NORMAL));
        }

        [Fact]
        public void NoMaterialRemovesTheFacesEntry()
        {
            TestHelpers.InMethod();
            SceneObjectPart part = NewPart();
            GltfMaterialOverride.SetRenderMaterial(part, 0, Material);
            GltfMaterialOverride.SetRenderMaterial(part, 3, Material);

            Assert.True(GltfMaterialOverride.SetRenderMaterial(part, 0, UUID.Zero));

            Assert.Equal(UUID.Zero, GltfMaterialOverride.GetRenderMaterial(part.Shape, 0));
            Assert.Equal(Material, GltfMaterialOverride.GetRenderMaterial(part.Shape, 3));
            Assert.Equal(1, System.Linq.Enumerable.Count(part.Shape.RenderMaterials.entries));
        }

        [Fact]
        public void SettingTheSameMaterialAgainChangesNothing()
        {
            TestHelpers.InMethod();
            SceneObjectPart part = NewPart();
            GltfMaterialOverride.SetRenderMaterial(part, -1, Material);

            Assert.False(GltfMaterialOverride.SetRenderMaterial(part, -1, Material));
        }
    }
}
