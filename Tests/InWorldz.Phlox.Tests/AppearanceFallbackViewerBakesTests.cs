using System.Reflection;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.CoreModules.Avatar.AvatarFactory;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] The temporary default appearance fallback (AvatarFactoryModule) on a viewer that bakes for itself.
/// UFPGC 2026-11-22: two Aurora residents each showed the default avatar to the other. The fallback ran although their
/// saved bakes were already default, so it could ask the viewer for nothing; then it put those saved bakes back over the
/// bakes the viewer had sent in the meantime. Now the fallback does not start when no rebake can be asked for, and bakes
/// the viewer sends while it runs end it and stay.
/// </summary>
// No process-wide state: each test has its own scene, so the class runs in parallel.
public class AppearanceFallbackViewerBakesTests
{
    private static readonly MethodInfo Process = typeof(AvatarFactoryModule).GetMethod(
        "ProcessTemporaryAppearanceFallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static (AvatarFactoryModule Factory, ScenePresence Sp) Setup()
    {
        var scene = new SceneHelpers().SetupScene();
        var factory = new AvatarFactoryModule();
        SceneHelpers.SetupSceneModules(scene, factory);
        var sp = SceneHelpers.AddScenePresence(scene, UUID.Random());
        return (factory, sp);
    }

    private static void SetBakes(Primitive.TextureEntry te, UUID id)
    {
        foreach (byte idx in AvatarAppearance.BAKE_INDICES)
            te.CreateFace(idx).TextureID = id;
    }

    private static Primitive.TextureEntry ViewerBakes(UUID id)
    {
        var te = new Primitive.TextureEntry(AppearanceManager.DEFAULT_AVATAR_TEXTURE);
        SetBakes(te, id);
        return te;
    }

    // Run the fallback's timer step as if its next action were due.
    private static void Tick(AvatarFactoryModule factory) => Process.Invoke(factory, new object[] { long.MaxValue });

    // no face is the default texture, as the viewer reads it
    private static UUID BakeOf(ScenePresence sp) =>
        sp.Appearance.Texture.FaceTextures[AvatarAppearance.BAKE_INDICES[0]]?.TextureID
            ?? sp.Appearance.Texture.DefaultTexture.TextureID;

    [Fact]
    public void SavedBakesThatAreAlreadyDefaultStartNoFallback()
    {
        var (factory, sp) = Setup();
        SetBakes(sp.Appearance.Texture, AppearanceManager.DEFAULT_AVATAR_TEXTURE);

        Assert.False(factory.ApplyTemporaryDefaultAppearanceFallback(sp));

        // the viewer's bakes then go on as sent
        UUID fresh = UUID.Random();
        factory.SetAppearance(sp, ViewerBakes(fresh), null, null);
        Assert.Equal(fresh, BakeOf(sp));
    }

    [Fact]
    public void SavedBakesMissingFromTheRegionStillStartTheFallback()
    {
        var (factory, sp) = Setup();
        SetBakes(sp.Appearance.Texture, UUID.Random());

        Assert.True(factory.ApplyTemporaryDefaultAppearanceFallback(sp));
    }

    [Fact]
    public void ViewerBakesBeforeTheTemporaryLookEndTheFallbackAndStay()
    {
        var (factory, sp) = Setup();
        UUID saved = UUID.Random();
        SetBakes(sp.Appearance.Texture, saved);
        Assert.True(factory.ApplyTemporaryDefaultAppearanceFallback(sp));

        UUID fresh = UUID.Random();
        factory.SetAppearance(sp, ViewerBakes(fresh), null, null);

        // the timer steps that would show the defaults, then put the saved outfit back, do nothing now
        Tick(factory);
        Tick(factory);
        Tick(factory);
        Assert.Equal(fresh, BakeOf(sp));
    }

    [Fact]
    public void ViewerBakesDuringTheTemporaryLookEndTheFallbackKeepTheOutfitAndStay()
    {
        var (factory, sp) = Setup();
        UUID saved = UUID.Random();
        SetBakes(sp.Appearance.Texture, saved);
        var shirt = new AvatarWearable();
        shirt.Add(UUID.Random(), UUID.Random());
        sp.Appearance.SetWearable(AvatarWearable.SHIRT, shirt);
        UUID shirtItem = sp.Appearance.Wearables[AvatarWearable.SHIRT][0].ItemID;
        Assert.True(factory.ApplyTemporaryDefaultAppearanceFallback(sp));

        Tick(factory);   // shows the temporary defaults
        Assert.Equal(AppearanceManager.DEFAULT_AVATAR_TEXTURE, BakeOf(sp));
        Assert.NotEqual(shirtItem, sp.Appearance.Wearables[AvatarWearable.SHIRT].Count > 0
            ? sp.Appearance.Wearables[AvatarWearable.SHIRT][0].ItemID : UUID.Zero);

        UUID fresh = UUID.Random();
        factory.SetAppearance(sp, ViewerBakes(fresh), null, null);
        Assert.Equal(fresh, BakeOf(sp));
        Assert.Equal(shirtItem, sp.Appearance.Wearables[AvatarWearable.SHIRT][0].ItemID);

        Tick(factory);   // would have put the saved outfit and its bakes back
        Tick(factory);
        Assert.Equal(fresh, BakeOf(sp));
    }
}
