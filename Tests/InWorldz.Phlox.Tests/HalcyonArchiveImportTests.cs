using System.Text.RegularExpressions;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Scenes.Serialization;
using OpenSim.Region.OptionalModules.Materials;
using OpenSim.Tests.Common;
using ProtoBuf;
using Xunit;
using Xunit.Abstractions;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] Loading objects from an InWorldz/Halcyon archive (2026-10-16). Halcyon wrote a prim's shape with
/// XmlSerializer and left ExtraParams out, so its projector is in ProjectionEntry / ProjectionTextureUUID /
/// ProjectionFOV / ProjectionFocus / ProjectionAmbiance; its materials are inline in RenderMaterialsBytes (protobuf-net,
/// a map of material id to material, the ids being the faces' MaterialIDs); and its physics shape type is
/// PreferredPhysicsShape. The objects here are Confluence's own XML with those elements put into the shape as Halcyon
/// wrote them, and the materials are encoded by protobuf-net itself from a copy of Halcyon's contract
/// (OpenSim/Framework/RenderMaterials.cs in HalcyonGrid/halcyon), so the decoder is checked against the real encoding.
/// Not a Tranquillity test. Set HALCYON_OAR_OBJECTS to an unpacked archive's objects folder to also read real ones.
/// </summary>
public class HalcyonArchiveImportTests
{
    private readonly ITestOutputHelper _out;
    public HalcyonArchiveImportTests(ITestOutputHelper o) => _out = o;

    [ProtoContract]
    public struct HalcyonRenderMaterial
    {
        [ProtoMember(1)] public Guid NormalID;
        [ProtoMember(2)] public float NormalOffsetX;
        [ProtoMember(3)] public float NormalOffsetY;
        [ProtoMember(4)] public float NormalRepeatX;
        [ProtoMember(5)] public float NormalRepeatY;
        [ProtoMember(6)] public float NormalRotation;
        [ProtoMember(7)] public Guid SpecularID;
        [ProtoMember(8)] public float SpecularOffsetX;
        [ProtoMember(9)] public float SpecularOffsetY;
        [ProtoMember(10)] public float SpecularRepeatX;
        [ProtoMember(11)] public float SpecularRepeatY;
        [ProtoMember(12)] public float SpecularRotation;
        [ProtoMember(13)] public byte SpecularLightColorR;
        [ProtoMember(14)] public byte SpecularLightColorG;
        [ProtoMember(15)] public byte SpecularLightColorB;
        [ProtoMember(16)] public byte SpecularLightColorA;
        [ProtoMember(17)] public byte SpecularLightExponent;
        [ProtoMember(18)] public byte EnvironmentIntensity;
        [ProtoMember(19)] public byte DiffuseAlphaMode;
        [ProtoMember(20)] public byte AlphaMaskCutoff;
    }

    [ProtoContract]
    public class HalcyonRenderMaterials
    {
        [ProtoMember(1)] public Dictionary<Guid, HalcyonRenderMaterial> Materials = new();
    }

    private static readonly UUID NormalMap = new("10e4e75e-3948-4953-abf4-ca06a4cecf63");
    private static readonly UUID SpecularMap = new("4cd3d440-d35e-4ca8-835a-af5930350c07");
    private static readonly UUID ProjectorTexture = new("3852da50-0cd5-478a-acfd-acdcc4e4cdaa");

    private static HalcyonRenderMaterial Shiny() => new()
    {
        NormalID = NormalMap.Guid, NormalRepeatX = 2f, NormalRepeatY = 1f, NormalRotation = 0.5f, NormalOffsetX = 0.25f,
        SpecularID = SpecularMap.Guid, SpecularRepeatX = 1f, SpecularRepeatY = 1f,
        SpecularLightColorR = 255, SpecularLightColorG = 200, SpecularLightColorB = 100, SpecularLightColorA = 255,
        SpecularLightExponent = 51, EnvironmentIntensity = 5, DiffuseAlphaMode = 2, AlphaMaskCutoff = 10,
    };

    private static string Base64(HalcyonRenderMaterials mats)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, mats);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>A one-prim object as Confluence writes it, the face given <paramref name="faceMaterial"/>, with
    /// <paramref name="halcyonShape"/> put into its shape as Halcyon wrote it.</summary>
    private static string HalcyonObject(UUID faceMaterial, string halcyonShape)
    {
        SceneObjectGroup sog = SceneHelpers.CreateSceneObject(1, UUID.Random(), "halcyon-", 0x10);
        var te = new Primitive.TextureEntry(UUID.Parse("89556747-24cb-43ed-920b-47caed15465f"));
        te.CreateFace(0).MaterialID = faceMaterial;
        sog.RootPart.Shape.TextureEntry = te.GetBytes(9);
        string xml = SceneObjectSerializer.ToOriginalXmlFormat(sog);
        // Halcyon had no ExtraParams element; drop Confluence's so the shape is read only from the Halcyon elements.
        xml = Regex.Replace(xml, "<ExtraParams>[^<]*</ExtraParams>", string.Empty);
        // nor the part's PhysicsShapeType (Halcyon kept it on the shape, as PreferredPhysicsShape)
        xml = Regex.Replace(xml, "<PhysicsShapeType>[^<]*</PhysicsShapeType>", string.Empty);
        return xml.Replace("</Shape>", halcyonShape + "</Shape>");
    }

    [Fact]
    public void AProjectorIsReadFromHalcyonsFiveElements()
    {
        string xml = HalcyonObject(UUID.Zero,
            "<ProjectionEntry>true</ProjectionEntry><ProjectionTextureUUID><Guid>" + ProjectorTexture +
            "</Guid></ProjectionTextureUUID><ProjectionFOV>2</ProjectionFOV><ProjectionFocus>1.5</ProjectionFocus>" +
            "<ProjectionAmbiance>0.75</ProjectionAmbiance>");

        PrimitiveBaseShape shape = SceneObjectSerializer.FromOriginalXmlFormat(xml).RootPart.Shape;

        Assert.True(shape.ProjectionEntry);
        Assert.Equal(ProjectorTexture, shape.ProjectionTextureUUID);
        Assert.Equal(2f, shape.ProjectionFOV);
        Assert.Equal(1.5f, shape.ProjectionFocus);
        Assert.Equal(0.75f, shape.ProjectionAmbiance);
        // and it reaches the viewer: the projection block is in the shape's ExtraParams
        Assert.Contains((byte)0x40, shape.ExtraParams);
    }

    [Theory]
    [InlineData("Prim", 0)]
    [InlineData("None", 1)]
    [InlineData("ConvexHull", 2)]
    public void ThePhysicsShapeTypeComesFromPreferredPhysicsShape(string halcyon, byte expected)
    {
        string xml = HalcyonObject(UUID.Zero, "<PreferredPhysicsShape>" + halcyon + "</PreferredPhysicsShape>");

        Assert.Equal(expected, SceneObjectSerializer.FromOriginalXmlFormat(xml).RootPart.PhysicsShapeType);
    }

    [Fact]
    public void TheMaterialAFaceUsesIsConvertedUnderItsIdAndAnUnusedOneIsNot()
    {
        UUID used = UUID.Random(), unused = UUID.Random();
        var mats = new HalcyonRenderMaterials();
        mats.Materials[used.Guid] = Shiny();
        mats.Materials[unused.Guid] = new HalcyonRenderMaterial { NormalID = NormalMap.Guid, NormalRepeatX = 1, NormalRepeatY = 1 };
        string xml = HalcyonObject(used, "<RenderMaterialsBytes>" + Base64(mats) + "</RenderMaterialsBytes>");

        SceneObjectPart part = SceneObjectSerializer.FromOriginalXmlFormat(xml).RootPart;

        Assert.True(part.DynAttrs.TryGetStore("OpenSim", "Materials", out OSDMap store));
        OSDArray list = (OSDArray)store["Materials"];
        _out.WriteLine(OSDParser.SerializeJsonString(list));
        OSDMap entry = Assert.Single(list.Cast<OSDMap>());
        Assert.Equal(used, entry["ID"].AsUUID());

        var fm = new FaceMaterial((OSDMap)entry["Material"]);
        Assert.Equal(NormalMap, fm.NormalMapID);
        Assert.Equal(SpecularMap, fm.SpecularMapID);
        Assert.Equal(2f, fm.NormalRepeatX, 3);
        Assert.Equal(1f, fm.NormalRepeatY, 3);
        Assert.Equal(0.5f, fm.NormalRotation, 3);
        Assert.Equal(0.25f, fm.NormalOffsetX, 3);
        Assert.Equal(0f, fm.NormalOffsetY, 3);                     // left out by protobuf-net as zero
        Assert.Equal((255, 200, 100, 255), (fm.SpecularLightColorR, fm.SpecularLightColorG, fm.SpecularLightColorB, fm.SpecularLightColorA));
        Assert.Equal(51, fm.SpecularLightExponent);
        Assert.Equal(5, fm.EnvironmentIntensity);
        Assert.Equal(2, fm.DiffuseAlphaMode);
        Assert.Equal(10, fm.AlphaMaskCutoff);
    }

    [Fact]
    public void AnObjectWithNoHalcyonElementsIsReadAsBefore()
    {
        SceneObjectGroup sog = SceneHelpers.CreateSceneObject(1, UUID.Random(), "plain-", 0x10);
        SceneObjectPart part = SceneObjectSerializer.FromOriginalXmlFormat(SceneObjectSerializer.ToOriginalXmlFormat(sog)).RootPart;

        Assert.False(part.Shape.ProjectionEntry);
        Assert.Equal(0, part.PhysicsShapeType);
        Assert.False(part.DynAttrs?.ContainsStore("OpenSim", "Materials") ?? false);
    }

    [Fact]
    public void InARegionTheMaterialBecomesAnAssetAndTheFaceKeepsIt()
    {
        var scene = new SceneHelpers().SetupScene();
        var config = new IniConfigSource();
        config.AddConfig("Materials").Set("enable_materials", "true");
        SceneHelpers.SetupSceneModules(scene, config, new MaterialsModule());

        UUID used = UUID.Random();
        var mats = new HalcyonRenderMaterials();
        mats.Materials[used.Guid] = Shiny();
        SceneObjectGroup sog = SceneObjectSerializer.FromOriginalXmlFormat(
            HalcyonObject(used, "<RenderMaterialsBytes>" + Base64(mats) + "</RenderMaterialsBytes>"));
        scene.AddNewSceneObject(sog, false);
        scene.EventManager.TriggerOnBackup(scene.SimulationDataService, true);

        var te = new Primitive.TextureEntry(sog.RootPart.Shape.TextureEntry, 0, sog.RootPart.Shape.TextureEntry.Length);
        Assert.Equal(used, te.GetFace(0).MaterialID);                // not cleared as a missing asset
        AssetBase asset = scene.AssetService.Get(used.ToString());
        Assert.NotNull(asset);                                         // saved, so it is there after a restart
        var fm = new FaceMaterial((OSDMap)OSDParser.DeserializeLLSDXml(asset.Data));
        Assert.Equal(NormalMap, fm.NormalMapID);
        Assert.Equal(51, fm.SpecularLightExponent);
        Assert.False(sog.RootPart.DynAttrs.ContainsStore("OpenSim", "Materials"));   // the inline copy is gone
    }

    /// <summary>Real archives, when HALCYON_OAR_OBJECTS points at an unpacked one's objects folder; otherwise nothing.</summary>
    [Fact]
    public void RealHalcyonArchiveObjectsLoadWithTheirProjectorsMaterialsAndPhysicsShapes()
    {
        string dir = Environment.GetEnvironmentVariable("HALCYON_OAR_OBJECTS");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return;

        int objects = 0, parts = 0, projectors = 0, withMaterials = 0, materials = 0, noPhysics = 0, unread = 0;
        int facesUsingMaterials = 0, facesMatched = 0, facesWithNoMaterialsOnThePart = 0;
        foreach (string file in Directory.GetFiles(dir, "*.xml"))
        {
            SceneObjectGroup sog = SceneObjectSerializer.FromOriginalXmlFormat(File.ReadAllText(file));
            if (sog == null) { unread++; continue; }
            objects++;
            foreach (SceneObjectPart part in sog.Parts)
            {
                parts++;
                if (part.Shape.ProjectionEntry) projectors++;
                if (part.PhysicsShapeType == 1) noPhysics++;
                var converted = new HashSet<UUID>();
                if (part.DynAttrs != null && part.DynAttrs.TryGetStore("OpenSim", "Materials", out OSDMap store))
                {
                    withMaterials++;
                    foreach (OSDMap m in ((OSDArray)store["Materials"]).Cast<OSDMap>()) { materials++; converted.Add(m["ID"].AsUUID()); }
                }
                var te = part.Shape.Textures;
                foreach (var face in te.FaceTextures.Append(te.DefaultTexture))
                {
                    if (face == null || face.MaterialID.IsZero()) continue;
                    facesUsingMaterials++;
                    if (converted.Contains(face.MaterialID)) facesMatched++;
                    else if (converted.Count == 0) facesWithNoMaterialsOnThePart++;
                }
            }
        }
        _out.WriteLine($"objects {objects} (unread {unread}), parts {parts}, projectors {projectors}, physics shape None {noPhysics}, " +
                       $"parts with materials {withMaterials}, materials {materials}, faces with a material {facesUsingMaterials}, of which converted {facesMatched}, " +
                       $"on a part that carries no materials {facesWithNoMaterialsOnThePart}");
        Assert.Equal(0, unread);
    }
}
