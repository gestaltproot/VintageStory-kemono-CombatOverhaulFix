using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using EntityShapeRenderer = Vintagestory.GameContent.EntityShapeRenderer;
using EntityBehaviorPlayerInventory = Vintagestory.GameContent.EntityBehaviorPlayerInventory;
using EntityPlayerShapeRenderer = Vintagestory.GameContent.EntityPlayerShapeRenderer;
using RenderMode = Vintagestory.GameContent.RenderMode;

namespace kemono;

/// <summary>
/// Stores an allocated texture atlas location. Each texture render
/// target (e.g. #main, #hair, #emblem, etc.) needs an allocated
/// texture location.
/// </summary>
public class KemonoTextureAtlasAllocation
{
    // name of texture target
    public readonly string Code;
    // texture allocation width
    public readonly int Width;
    // texture allocation height
    public readonly int Height;
    // allocated texture id
    public readonly int TextureSubId;
    // allocated texture position in entity texture atlas
    public readonly TextureAtlasPosition TexPos;
    // cpu side pixel buffer for texture allocation
    // BGRA format because that is default VS textures
    public readonly int[] BgraPixels;
    
    // gpu loaded texture, allocate lazily when needed
    public LoadedTexture Texture = null;
    
    public KemonoTextureAtlasAllocation(string code, int width, int height, int textureSubId, TextureAtlasPosition texPos)
    {
        Code = code;
        Width = width;
        Height = height;
        TextureSubId = textureSubId;
        TexPos = texPos;
        BgraPixels = new int[width * height];
    }

    /// <summary>
    /// Dispose of texture target and release resources.
    /// </summary>
    public void Dispose()
    {
        if (Texture != null)
        {
            Texture.Dispose();
            Texture = null;
        }
    }

    /// <summary>
    /// Zeros out pixel buffer (r,g,b,a) = (0,0,0,0).
    /// </summary>
    public void ClearPixels()
    {
        for (int i = 0; i < BgraPixels.Length; i++)
        {
            BgraPixels[i] = 0;
        }
    }

    /// <summary>
    /// Fill out pixel buffer with a color (r,g,b,a).
    /// </summary>
    public void FillPixels(int color)
    {
        for (int i = 0; i < BgraPixels.Length; i++)
        {
            BgraPixels[i] = color;
        }
    }

    /// <summary>
    /// Writes bitmap pixels into texture target pixels buffer.
    /// This will error and seethe if the bitmap is larger than the
    /// allocated texture target or if the offset causes write
    /// out-of-bounds, caller must avoid doing that.
    /// Source pixels are in BGRA format because that is default texture
    /// loaded format in vintagestory.
    /// </summary>
    /// <param name="bmp"></param>
    /// <param name="xOffset"></param>
    /// <param name="yOffset"></param>
    /// <param name="overlay"></param>
    public void WritePixels(
        int[] srcPixelsBgra,
        int srcWidth,
        int srcHeight,
        int xOffset = 0,
        int yOffset = 0,
        bool overlay = true // overlay on top instead of overwrite
    ) {
        if (overlay)
        {
            for (int y = 0; y < srcHeight; y++)
            {
                for (int x = 0; x < srcWidth; x++)
                {
                    int idx = (y + yOffset) * Width + (x + xOffset);
                    if (idx >= 0 && idx < BgraPixels.Length)
                    {
                        // mix using alpha blending
                        int src = srcPixelsBgra[y * srcWidth + x];
                        int dst = BgraPixels[idx];
                        var (srcB, srcG, srcR, srcA) = KemonoColorUtil.ToRgba(src);
                        var (dstB, dstG, dstR, dstA) = KemonoColorUtil.ToRgba(dst);
                        BgraPixels[idx] = ColorUtil.ColorFromRgba(
                            (srcA * srcB + dstA * dstB * (255 - srcA) / 255) / 255,
                            (srcA * srcG + dstA * dstG * (255 - srcA) / 255) / 255,
                            (srcA * srcR + dstA * dstR * (255 - srcA) / 255) / 255,
                            srcA + dstA * (255 - srcA) / 255
                        );
                    }
                }
            }
        }
        else
        {
            // directly copy pixels into target location
            for (int y = 0; y < srcHeight; y++)
            {
                for (int x = 0; x < srcWidth; x++)
                {
                    int idx = (y + yOffset) * Width + (x + xOffset);
                    if (idx >= 0 && idx < BgraPixels.Length)
                    {
                        BgraPixels[idx] = srcPixelsBgra[y * srcWidth + x];
                    }
                }
            }
        }
    }
}


/// <summary>
/// Utility class for sharing "clear" textures across entities.
/// These textures are used to clear any paintable textures
/// (e.g. cutiemark) in the gui canvas painter. These can be shared
/// across all entities and are cached for reuse.
/// </summary>
public static class ClearTextureUtil
{
    // re-used textures for clearing paintable textures
    // this is mod global and shared across all entities
    public static Dictionary<(int, int), LoadedTexture> ClearTextures = new Dictionary<(int, int), LoadedTexture>();

    /// <summary>
    /// Gets cached clear pixels texture for given size.
    /// Lazily creates and caches clear texture for given size.
    /// </summary>
    /// <param name="capi"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    public static LoadedTexture GetClearTexture(ICoreClientAPI capi, int width, int height)
    {
        // try get cached texture for size
        if (ClearTextures.TryGetValue((width, height), out LoadedTexture cachedTex))
        {
            return cachedTex;
        }
        
        // create new clear texture, store in cache
        int[] clearPixels = new int[width * height];
        int clearColor = ColorUtil.ColorFromRgba(255, 255, 255, 0);

        // put clear pixels with alpha input
        for (int i = 0; i < clearPixels.Length; i += 1)
        {
            clearPixels[i] = clearColor;
        }

        bool linearMag = false; // use nearest rendering
        int clampMode = 0;      // clamp mode

        var tex = new LoadedTexture(capi)
        {
            Width = width,
            Height = height
        };

        capi.Render.LoadOrUpdateTextureFromRgba(
            clearPixels,
            linearMag,
            clampMode,
            ref tex
        );

        // store in cache
        ClearTextures[(width, height)] = tex;

        return tex;
    }

    /// <summary>
    /// Frees all cached clear textures.
    /// </summary>
    public static void FreeAllClearTextures()
    {
        foreach (var tex in ClearTextures.Values)
        {
            tex.Dispose();
        }

        ClearTextures.Clear();
    }

    /// <summary>
    /// Helper to clear texture atlas position with a clear texture.
    /// </summary>
    /// <param name="capi"></param>
    /// <param name="texAtlasPos"></param>
    /// <param name="width"></param>
    /// <param name="height"></param>
    public static void ClearTextureAtlasPos(
        ICoreClientAPI capi,
        TextureAtlasPosition texAtlasPos,
        int width,
        int height
    ) {
        // LoadedTexture emblemTexture = GetEmblemTexture();
        LoadedTexture clearTexture = GetClearTexture(capi, width, height);

        // this clears the atlas render location
        // i have no idea why this blend mode works lol lmao
        capi.Render.GlToggleBlend(true, EnumBlendMode.Glow);
        capi.EntityTextureAtlas.RenderTextureIntoAtlas(
            texAtlasPos.atlasTextureId,
            clearTexture,
            0,
            0,
            clearTexture.Width,
            clearTexture.Height,
            texAtlasPos.x1 * capi.EntityTextureAtlas.Size.Width,
            texAtlasPos.y1 * capi.EntityTextureAtlas.Size.Height,
            -1f
        );
        capi.Render.GlToggleBlend(false, EnumBlendMode.Glow);
    }
}

/// <summary>
/// Interface for kemono entity renderers. Player and regular entities
/// need separate renderers because player has extra garbage that
/// makes it unusable for npc entities. This interface abstracts
/// common functionality for kemono skins (e.g. texture allocation).
/// </summary>
public interface IKemonoRenderer
{
    // entity this is bound to
    public Entity Entity { get; set; }

    // texture atlas allocations: texture target name => texture atlas allocation
    // e.g. "#hair" => TextureAtlasAllocation
    public Dictionary<string, KemonoTextureAtlasAllocation> TextureAllocations { get; set; }

    // main allocation for body texture, used for overlaying clothes
    // points to same object inside TextureAllocations for "main"
    public KemonoTextureAtlasAllocation MainTexture { get; set; }

    // hard-coded name for main body texture
    public string MainTextureName { get; init; }

    // hard-coded size for main body texture
    public int MainTextureSize { get; init; }

    // re-run tesselation
    public void RunTesselation();
}

public static class IKemonoRendererExtensions
{
    /// <summary>
    /// Get unique entity-specific atlas key for a texture.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static string GetTextureAtlasKey(this IKemonoRenderer renderer, string name) 
    {
        return $"{name}-{renderer.Entity.EntityId}";
    }

    /// <summary>
    /// Allocate texture target for skin part specific to this entity.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="width"></param>
    /// <param name="height"></param>
    public static KemonoTextureAtlasAllocation GetOrAllocateTextureTarget(
        this IKemonoRenderer renderer,
        string name,
        int width,
        int height
    ) {
        // ASSUMES THIS SIDE IS CLIENT
        // if (entity.World.Side != EnumAppSide.Client) return;

        string key = renderer.GetTextureAtlasKey(name);

        if (renderer.TextureAllocations.TryGetValue(key, out KemonoTextureAtlasAllocation tex))
        {
            return tex; // found existing allocation for target
        }

        // need to create new allocation
        ICoreClientAPI capi = renderer.Entity.World.Api as ICoreClientAPI;

        capi.EntityTextureAtlas.AllocateTextureSpace(
            width,
            height,
            out int textureSubId,
            out TextureAtlasPosition texPos
        );

        // write empty alpha pixels into texture allocation
        // otherwise previous allocation pixel history will corrupt
        // due to the nonfunctional blend modes
        LoadedTexture clearTexture = ClearTextureUtil.GetClearTexture(capi, width, height);

        capi.Render.GlToggleBlend(false, EnumBlendMode.Standard);
        capi.Render.GlToggleBlend(false, EnumBlendMode.Overlay);

        capi.EntityTextureAtlas.RenderTextureIntoAtlas(
            texPos.atlasTextureId,
            clearTexture,
            0,
            0,
            width,
            height,
            texPos.x1 * capi.EntityTextureAtlas.Size.Width,
            texPos.y1 * capi.EntityTextureAtlas.Size.Height,
            -1f
        );

        var alloc = new KemonoTextureAtlasAllocation(name, width, height, textureSubId, texPos);

        renderer.TextureAllocations[key] = alloc;

        var keyAsAssetLocation = new AssetLocation("kemono", key);
        CompositeTexture ctex = new CompositeTexture() { Base = keyAsAssetLocation };
        ctex.Baked = new BakedCompositeTexture() { BakedName = keyAsAssetLocation, TextureSubId = textureSubId };
        
        // inject allocated textures into entity shape renderer
        renderer.Entity.Properties.Client.Textures[key] = ctex;

        return alloc;
    }

    /// <summary>
    /// Try to get texture target, return null if does not exist.
    /// </summary>
    /// <param name="name"></param>
    public static KemonoTextureAtlasAllocation GetTextureTarget(this IKemonoRenderer renderer, string name)
    {
        string key = renderer.GetTextureAtlasKey(name);
        return renderer.TextureAllocations.GetValueOrDefault(key, null);
    }

    public static void FreeTextureTarget(this IKemonoRenderer renderer, string name)
    {
        if (renderer.Entity.World.Side != EnumAppSide.Client) return;

        ICoreClientAPI capi = renderer.Entity.World.Api as ICoreClientAPI;

        string key = renderer.GetTextureAtlasKey(name);

        if (renderer.TextureAllocations.TryGetValue(key, out KemonoTextureAtlasAllocation tex))
        {
            tex.Dispose(); // free gpu texture
            capi.EntityTextureAtlas.FreeTextureSpace(tex.TextureSubId);
            renderer.TextureAllocations.Remove(key);
            renderer.Entity.Properties.Client.Textures.Remove(key);
        }
    }

    public static void FreeAllTextureTargets(this IKemonoRenderer renderer)
    {
        if (renderer.Entity.World.Side != EnumAppSide.Client) return;

        ICoreClientAPI capi = renderer.Entity.World.Api as ICoreClientAPI;
        var textures = renderer.Entity.Properties.Client.Textures;

        foreach (var alloc in renderer.TextureAllocations)
        {
            string key = alloc.Key;
            KemonoTextureAtlasAllocation tex = alloc.Value;
            tex.Dispose(); // free gpu texture

            capi.EntityTextureAtlas.FreeTextureSpace(tex.TextureSubId);
            textures.Remove(key);
        }

        renderer.TextureAllocations.Clear();

        // remove main texture reference
        renderer.MainTexture = null;
    }
}

/// <summary>
/// Basic kemono entity shape renderer. Use this for non-player entities.
/// </summary>
public class EntityKemonoShapeRenderer : EntityShapeRenderer, IKemonoRenderer
{
    // entity this is bound to
    public Entity Entity
    {
        get => entity;
        set => entity = value;
    }

    // texture atlas allocations: texture target name => texture atlas allocation
    // e.g. "#hair" => TextureAtlasAllocation
    public Dictionary<string, KemonoTextureAtlasAllocation> TextureAllocations { get; set; } = new Dictionary<string, KemonoTextureAtlasAllocation>();

    // main allocation for body texture, used for overlaying clothes
    // points to same object inside TextureAllocations for "main"
    public KemonoTextureAtlasAllocation MainTexture { get; set; }

    // hard-coded name for main body texture
    public string MainTextureName { get; init; } = "main";

    // hard-coded size for main body texture
    // TODO: make this configurable
    // for now, model main texture width/height has to match this
    public int MainTextureSize { get; init; } = 128;

    public void RunTesselation() => TesselateShape();

    public EntityKemonoShapeRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {

    }

    /// <summary>
    /// Return texture atlas position for target texture name.
    /// For kemono entities, this will try to search for entity-specific
    /// "keyed" textures (e.g. "hair-1234" for entity with id 1234)
    /// before falling back to vanilla shared textures.
    /// </summary>
    /// <param name="textureCode"></param>
    /// <returns></returns>
    public override TextureAtlasPosition this[string textureCode]
    {
        get
        {
            // first try to return unique per-entity keyed texture
            string key = this.GetTextureAtlasKey(textureCode);
            if (entity.Properties.Client.Textures.TryGetValue(key, out CompositeTexture tex))
            {
                return capi.EntityTextureAtlas.Positions[tex.Baked.TextureSubId];
            }

            // fallback to vanilla shared textures
            return base[textureCode];
        }
    }

    /// <summary>
    /// This gets the default "main" texture target for the entity
    /// (typically the body texture). This is needed here because
    /// VS needs to have a default texture associated with an entity.
    /// </summary>
    /// <returns></returns>
    protected override ITexPositionSource GetTextureSource()
    {
        if (MainTexture == null)
        {
            // do an initial allocation of main texture so that
            // `skinTexPos` points to a valid texture atlas location
            var alloc = this.GetOrAllocateTextureTarget(
                MainTextureName,
                MainTextureSize,
                MainTextureSize
            );
            MainTexture = alloc;
            skinTexPos = alloc.TexPos;
        }

        return base.GetTextureSource();
    }

    /// <summary>
    /// Dispose all texture allocations.
    /// </summary>
    public override void Dispose()
    {
        MainTexture?.Dispose();
        this.FreeAllTextureTargets();
        base.Dispose();
    }
}

/// <summary>
/// Override:
/// https://github.com/anegostudios/vsessentialsmod/blob/master/EntityRenderer/EntityPlayerShapeRenderer.cs
/// 
/// and add kemono-specific functionality.
/// This is used for the player entity.
/// </summary>
public class EntityKemonoPlayerShapeRenderer : EntityPlayerShapeRenderer, IKemonoRenderer
{
    // entity this is bound to
    public Entity Entity
    {
        get => entity;
        set => entity = value;
    }

    // texture atlas allocations: texture target name => texture atlas allocation
    // e.g. "#hair" => TextureAtlasAllocation
    public Dictionary<string, KemonoTextureAtlasAllocation> TextureAllocations { get; set; } = new Dictionary<string, KemonoTextureAtlasAllocation>();

    // main allocation for body texture, used for overlaying clothes
    // points to same object inside TextureAllocations for "main"
    public KemonoTextureAtlasAllocation MainTexture { get; set; }

    // hard-coded name for main body texture
    public string MainTextureName { get; init; } = "main";

    // hard-coded size for main body texture
    public int MainTextureSize { get; init; } = 128;

    public void RunTesselation() => TesselateShape();

    /// <summary>
    /// Return texture atlas position for target texture name.
    /// For kemono entities, this will try to search for entity-specific
    /// "keyed" textures (e.g. "hair-1234" for entity with id 1234)
    /// before falling back to vanilla shared textures.
    /// </summary>
    /// <param name="textureCode"></param>
    /// <returns></returns>
    public override TextureAtlasPosition this[string textureCode]
    {
        get
        {
            // first try to return unique per-entity keyed texture
            string key = this.GetTextureAtlasKey(textureCode);
            if (entity.Properties.Client.Textures.TryGetValue(key, out CompositeTexture tex))
            {
                return capi.EntityTextureAtlas.Positions[tex.Baked.TextureSubId];
            }

            // fallback to vanilla shared textures
            return base[textureCode];
        }
    }

    protected override ITexPositionSource GetTextureSource()
    {
        if (MainTexture == null)
        {
            // do an initial allocation of main texture so that
            // `skinTexPos` points to a valid texture atlas location
            var alloc = this.GetOrAllocateTextureTarget(
                this.MainTextureName,
                this.MainTextureSize,
                this.MainTextureSize
            );
            MainTexture = alloc;
            skinTexPos = alloc.TexPos;
        }

        return base.GetTextureSource();
    }

    // get private fields from EntityPlayerShapeRenderer
    private static FieldInfo entityPlayerField = typeof(EntityPlayerShapeRenderer).GetField("entityPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo thirdPersonMeshRefField = typeof(EntityPlayerShapeRenderer).GetField("thirdPersonMeshRef", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo firstPersonMeshRefField = typeof(EntityPlayerShapeRenderer).GetField("firstPersonMeshRef", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo watcherRegisteredField = typeof(EntityPlayerShapeRenderer).GetField("watcherRegistered", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo previfpModeField = typeof(EntityPlayerShapeRenderer).GetField("previfpMode", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo disposeMeshesMethod = typeof(EntityPlayerShapeRenderer).GetMethod("disposeMeshes", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo renderModeField = typeof(EntityPlayerShapeRenderer).GetField("renderMode", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo determineRenderModeMethod = typeof(EntityPlayerShapeRenderer).GetMethod("determineRenderMode", BindingFlags.NonPublic | BindingFlags.Instance);


    private EntityPlayer entityPlayer
    {
        get => (EntityPlayer)entityPlayerField.GetValue(this);
    }

    private MultiTextureMeshRef thirdPersonMeshRef
    {
        get => (MultiTextureMeshRef)thirdPersonMeshRefField.GetValue(this);
        set => thirdPersonMeshRefField.SetValue(this, value);
    }

    private MultiTextureMeshRef firstPersonMeshRef
    {
        get => (MultiTextureMeshRef)firstPersonMeshRefField.GetValue(this);
        set => firstPersonMeshRefField.SetValue(this, value);
    }

    private bool watcherRegistered
    {
        get => (bool)watcherRegisteredField.GetValue(this);
        set => watcherRegisteredField.SetValue(this, value);
    }

    private bool previfpMode
    {
        get => (bool)previfpModeField.GetValue(this);
        set => previfpModeField.SetValue(this, value);
    }

    private RenderMode renderMode
    {
        get => (RenderMode)renderModeField.GetValue(this);
        set => renderModeField.SetValue(this, value);
    }

    public EntityKemonoPlayerShapeRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {

    }

    private void determineRenderMode()
    {
        determineRenderModeMethod.Invoke(this, null);
    }

    private void disposeMeshes()
    {
        disposeMeshesMethod.Invoke(this, null);
    }

    public override void TesselateShape()
    {
        var inv = entityPlayer.GetBehavior<EntityBehaviorPlayerInventory>().Inventory;
        if (inv == null) return; // Player is not fully initialized yet

        // Need to call this before tesselate or we will reference the wrong texture
        defaultTexSource = GetTextureSource();

        OverrideTesselate();

        if (!watcherRegistered)
        {
            previfpMode = capi.Settings.Bool["immersiveFpMode"];
            if (IsSelf)
            {
                capi.Settings.Bool.AddWatcher("immersiveFpMode", (on) =>
                {
                    entity.MarkShapeModified();

                    (entityPlayer.AnimManager as PlayerAnimationManager).OnIfpModeChanged(previfpMode, on);
                });
            }

            watcherRegistered = true;
        }
    }

    // overriden EntityPlayerShapeRenderer.Tesselate
    //
    // change pose names to match kemono skeleton format:
    // .GetPosebyName("Neck") => .GetPosebyName("b_Neck")
    // .GetPosebyName("UpperArmL") => .GetPosebyName("b_ArmUpperL")
    // .GetPosebyName("UpperArmR") => .GetPosebyName("b_ArmUpperR")
    public void OverrideTesselate()
    {
        if (!IsSelf)
        {
            base.TesselateShape();
            return;
        }

        if (!loaded) return;

        TesselateShape((meshData) =>
        {
            disposeMeshes();

            if (capi.IsShuttingDown)
            {
                return;
            }

            if (meshData.VerticesCount > 0)
            {
                MeshData firstPersonMesh = meshData.EmptyClone();

                thirdPersonMeshRef = capi.Render.UploadMultiTextureMesh(meshData);

                determineRenderMode();
                if (renderMode == RenderMode.ImmersiveFirstPerson)
                {
                    HashSet<int> skipJointIds = new HashSet<int>();
                    loadJointIdsRecursive(entity.AnimManager.Animator.GetPosebyName("b_Neck"), skipJointIds);
                    firstPersonMesh.AddMeshData(meshData, (i) => !skipJointIds.Contains(meshData.CustomInts.Values[i * 4]));
                }
                else
                {
                    HashSet<int> includeJointIds = new HashSet<int>();
                    loadJointIdsRecursive(entity.AnimManager.Animator.GetPosebyName("b_ArmUpperL"), includeJointIds);
                    loadJointIdsRecursive(entity.AnimManager.Animator.GetPosebyName("b_ArmUpperR"), includeJointIds);

                    firstPersonMesh.AddMeshData(meshData, (i) => includeJointIds.Contains(meshData.CustomInts.Values[i * 4]));
                }

                firstPersonMeshRef = capi.Render.UploadMultiTextureMesh(firstPersonMesh);
            }
        });
    }

    private void loadJointIdsRecursive(ElementPose elementPose, HashSet<int> outList)
    {
        if (elementPose != null)
        {
            outList.Add(elementPose.ForElement.JointId);
            foreach (var childpose in elementPose.ChildElementPoses)
            {
                loadJointIdsRecursive(childpose, outList);
            }
        }
    }
    
    /// <summary>
    /// Dispose all texture allocations.
    /// </summary>
    public override void Dispose()
    {
        MainTexture?.Dispose();
        this.FreeAllTextureTargets();
        base.Dispose();
    }
}
