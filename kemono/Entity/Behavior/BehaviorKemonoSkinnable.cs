/**
Implements customizable entity shape, skin, voice, etc.

Implementation notes:

Two main functions:
1. OnReloadSkin: 
    Reloads all customizable textures, writes textures to atlas.
2. OnTesselation:
    Reload skin parts, rebuilds entity shape using `AddSkinPart(...)`,
    and reloads animations if needed.

# OnTesselation Notes

Body is split into a base shape cold path and face shapes hot path to support
faster shape building for facial expression changes.
      
    [SetModel]
    Load base shape
        |
        v
    [OnTesselation]
    Attach base shape skin parts
        |
        v
    Base body shape (cached)
        |
        v
    Attach face shape skin parts
        |
        v
    Complete body shape (cached)
        |
        v
    Filter remove clothing shape items
        |
        v
    Final shape (cached)

Ideally hot path dynamic elements should just use a separate rendering path
but VS entity renderers have like 4 layers of indirection making this shit
nightmare to synchronize.

# OnReloadSkin

Each skin part has an associated texture target, e.g. "hair", "eyes".
For each skin part in order, following procedure run:
     ______
    |      |
    |      |  1. Get or allocate "pixel buffer" on CPU side for texture
    |______|     target. This is re-used across calls.

       |      2. For each skin part in render order, write its texture
       v         pixels into the pixel buffer.
     ______
    | >,<  |
    | :^(  |  
    |______|
    
       |      3. Write pixel buffer to texture atlas.
       v
     ___________
    | >,<
    | :^(    ...
    |
    |  ...

The pixel buffer stores the content of the previous call and will only
re-write pixels if any skin part associated with the texture target has
changed. Reason: once player has done character creation, they are not
changing skin often ingame, majority of time these texture targets are
not changing content. So we do not need to redraw CPU skin part pixels.
However, we do need to re-write to atlas because clothing is rendered
on top of base skin parts, and clothing can change often. 

TODO:
- Maintain a "write stack" ordering for parts that write to same
  target, e.g. base. If body changes, then all overlaid textures
  (horn, eyes, etc.) also need to rewrite to buffer.

# OnTesselation

TODO

*/

using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using EntityBehaviorTexturedClothing = Vintagestory.GameContent.EntityBehaviorTexturedClothing;

namespace kemono;

public enum EnumKemonoSkinnableType
{
    Shape,
    Texture,
    Voice
}

/// Skinnable part in a model, e.g. hair, eyes, etc.
[JsonObject(MemberSerialization.OptOut)]
public class KemonoSkinnablePart
{
    /// <summary>
    /// Part identifier, e.g. "hair", "eye", etc.
    /// </summary>
    public string Code;

    /// <summary>
    /// Part render order, lower numbers render first in model.
    /// Purpose of render order is to enable a modder to add a new
    /// model addon part and control its render order relative to
    /// default built-in parts.
    /// 
    /// This is split into separate base and face shape groups.
    /// Face shape parts will always render after base shape parts.
    /// </summary>
    public int RenderOrder;

    /// <summary>
    /// Part type, e.g. shape, texture, voice, etc.
    /// </summary>
    public EnumKemonoSkinnableType Type;

    /// <summary>
    /// Flag this is a face part, to use hot path for face expressions.
    /// Will make this shape part always attach after base shape parts.
    /// </summary>
    public bool Face = false;

    /// <summary>
    /// Base shape template path string,
    /// e.g. "kemono:entity/horse/skinparts/hair/hair-{code}".
    /// where {code} is the variant code that will be replaced.
    /// </summary>
    public AssetLocation ShapeTemplate;

    // available variants (models, textures, etc.)
    public KemonoSkinnablePartVariant[] Variants;

    // shape texture path
    public AssetLocation Texture;

    // texture render uv coordinates (x, y)
    public Vec2i TextureRenderTo = new Vec2i();

    // name of the texture target in the model, e.g. "hair"
    public string TextureTarget;

    // width of texture target atlas location
    // needed for allocating atlas texture space
    public int TextureTargetWidth;

    // height of texture target atlas location
    // needed for allocating atlas texture space
    public int TextureTargetHeight;

    // render user paintable painting texture onto texture target
    public bool UsePainting = false;

    // painting texture name
    public string PaintingName;

    // painting size in pixels
    public int PaintingSize = 32;

    // copy texture from another texture target
    public string TextureCopyFrom = null;

    // use clothing textures on texture target
    public bool UseClothingTexture = false;

    // list of clothing inventory slots to steal textures from
    public EnumCharacterDressType[] ClothingTextures = {};

    // flag to texture blend overlay
    // during initialization, if texture target is first in the render
    // order, this will be disabled (no blend)
    public bool TextureBlendOverlay = true;

    // gui layout configuration
    public bool UseDropDown = true;
    public bool UseColorSlider = false;
    public bool UseTransformSlider = false;

    // when player has any of this list of clothes or armor on,
    // use skinpart's alt model codes
    public EnumCharacterDressType[] AltClothedRequirement = {};
    public EnumCharacterDressType[] AltArmoredRequirement = {};

    // reference to texture target index in model, must be assigned
    // after all texture targets are parsed in parent model
    [JsonIgnore]
    public int TextureTargetIndex = -1;

    // variant code => variant (same objects as in Variants)
    // (must be initialized after object created)
    [JsonIgnore]
    public Dictionary<string, KemonoSkinnablePartVariant> LoadedVariantsByCode = null;

    // lazy accessor for skin part code => skin part
    // creates lookup dictionary when first called
    [JsonIgnore]
    public Dictionary<string, KemonoSkinnablePartVariant> VariantsByCode
    {
        get
        {
            if (LoadedVariantsByCode == null)
            {
                LoadedVariantsByCode = new Dictionary<string, KemonoSkinnablePartVariant>();
                foreach (var variant in Variants)
                {
                    LoadedVariantsByCode[variant.Code] = variant;
                }
            }
            return LoadedVariantsByCode;
        }
    }
}

/// Variant of a skinnable part, e.g. hair types.
/// TODO: move color out of here, it should be in applied part only
public class KemonoSkinnablePartVariant
{
    // identifier
    public string Code;
    // alt variant template code when model clothed
    public string AltClothed;
    // alt variant template code when model armored
    public string AltArmored;
    // path to shape file, for shape attachments
    public AssetLocation Shape;
    // path to alt clothed shape file, for shape attachments
    public AssetLocation ShapeClothed;
    // path to alt clothed shape file, for shape attachments
    public AssetLocation ShapeArmored;
    // path to texture file, for texture overlays
    public AssetLocation Texture;
    // path to sound file, for voices
    public AssetLocation Sound;
    // flag to skip this variant when rendering, for empty parts
    public bool Skip = false;
}

/// Applied variant of a skinnable part, contains final values after
/// building the variants and mixing with other properties.
public class AppliedKemonoSkinnablePartVariant
{
    // part code string
    public string PartCode;
    // variant identifier
    public string Code;
    // alt variant code when model clothed
    public string AltClothed;
    // alt variant code when model armored
    public string AltArmored;
    // path to shape file, for shape attachments
    public AssetLocation Shape;
    // path to alt clothed shape file, for shape attachments
    public AssetLocation ShapeClothed;
    // path to alt clothed shape file, for shape attachments
    public AssetLocation ShapeArmored;
    // path to texture file, for texture overlays
    public AssetLocation Texture;
    // path to sound file, for voices
    public AssetLocation Sound;
    // flag to skip this variant when rendering, for empty parts
    public bool Skip;
    // applied color on top of variant texture
    public int Color;
    // applied glow value on part, only applied if > 0
    public int Glow;
    // loaded texture pixels
    public BitmapSimple TextureBitmap = null;
    // step parent filter
    public string[] stepParentFilter = null;
}

/// <summary>
/// Wrapper around skin part's ITreeAttribute tree to implement
/// IReadOnlyDictionary interface for KemonoRace.IsValid().
/// </summary>
public struct SkinPartsTreeDictionary : IReadOnlyDictionary<string, string>
{
    public ITreeAttribute tree;

    public SkinPartsTreeDictionary(ITreeAttribute tree)
    {
        this.tree = tree;
    }

    public string this[string key] => tree.GetString(key);

    public IEnumerable<string> Keys => tree.Select(v => v.Key);

    public IEnumerable<string> Values => tree.Values.Select(v => (v as StringAttribute).value);

    public int Count => tree.Count;

    public bool ContainsKey(string key)
    {
        return tree.HasAttribute(key);
    }

    public bool TryGetValue(string key, out string value)
    {
        if (tree.HasAttribute(key))
        {
            value = tree.GetString(key);
            return true;
        }
        else
        {
            value = null;
            return false;
        }
    }
    
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        foreach (var val in tree)
        {
            yield return new KeyValuePair<string, string>(val.Key, (val.Value as StringAttribute).value);
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

/// <summary>
/// Set of animations that can be added to models.
/// This is the "animations/" asset class type.
/// </summary>
[JsonObject(MemberSerialization.OptOut)]
public class KemonoAnimationSet
{
    public string Code;
    public Animation[] Animations;
}

/// Override properties regular animation meta data:
/// https://apidocs.vintagestory.at/api/Vintagestory.API.Common.AnimationMetaData.html
[JsonObject(MemberSerialization.OptOut)]
public class KemonoAnimationOverride
{
    public string Code;
    public string Animation;
    public float? Weight;
    public Dictionary<string, float> ElementWeight;
    public float? AnimationSpeed;
    public bool? MulWithWalkSpeed;
    public float? WeightCapFactor;
    public float? EaseInSpeed;
    public float? EaseOutSpeed;
    public AnimationTrigger TriggeredBy;
    public EnumAnimationBlendMode? BlendMode;
    public Dictionary<string, EnumAnimationBlendMode> ElementBlendMode = new Dictionary<string, EnumAnimationBlendMode>(StringComparer.OrdinalIgnoreCase);
    public bool? SupressDefaultAnimation;
    public float? HoldEyePosAfterEasein;
    public bool? ClientSide;
    public bool? WithFpVariant;
    public AnimationSound AnimationSound;

    public AnimationMetaData ToAnimationMeta()
    {
        return new AnimationMetaData
        {
            Code = Code,
            Animation = Animation,
            Weight = Weight ?? 1,
            ElementWeight = ElementWeight ?? new Dictionary<string, float>(),
            AnimationSpeed = AnimationSpeed ?? 1,
            WeightCapFactor = WeightCapFactor ?? 0,
            MulWithWalkSpeed = MulWithWalkSpeed ?? false,
            EaseInSpeed = EaseInSpeed ?? 10f,
            EaseOutSpeed = EaseOutSpeed ?? 10f,
            TriggeredBy = TriggeredBy?.Clone(),
            BlendMode = BlendMode ?? EnumAnimationBlendMode.Add,
            ElementBlendMode = ElementBlendMode ?? new Dictionary<string, EnumAnimationBlendMode>(),
            SupressDefaultAnimation = SupressDefaultAnimation ?? false,
            HoldEyePosAfterEasein = HoldEyePosAfterEasein ?? 99f,
            ClientSide = ClientSide ?? false,
            WithFpVariant = WithFpVariant ?? false,
            AnimationSound = AnimationSound?.Clone()
        };
    }

    /// <summary>
    /// Apply override properties to existing animation meta data.
    /// </summary>
    /// <param name="anim"></param>
    public void ApplyTo(AnimationMetaData anim)
    {
        // ignore code, assuming anim.Code == animOverride.Code by user
        if (Animation != null) anim.Animation = Animation;
        if (Weight != null) anim.Weight = Weight.Value;
        if (ElementWeight != null) anim.ElementWeight = new Dictionary<string, float>(ElementWeight);
        if (AnimationSpeed != null) anim.AnimationSpeed = AnimationSpeed.Value;
        if (MulWithWalkSpeed != null) anim.MulWithWalkSpeed = MulWithWalkSpeed.Value;
        if (WeightCapFactor != null) anim.WeightCapFactor = WeightCapFactor.Value;
        if (EaseInSpeed != null) anim.EaseInSpeed = EaseInSpeed.Value;
        if (EaseOutSpeed != null) anim.EaseOutSpeed = EaseOutSpeed.Value;
        if (TriggeredBy != null) anim.TriggeredBy = TriggeredBy.Clone();
        if (BlendMode != null) anim.BlendMode = BlendMode.Value;
        if (ElementBlendMode != null) anim.ElementBlendMode = new Dictionary<string, EnumAnimationBlendMode>(ElementBlendMode);
        if (SupressDefaultAnimation != null) anim.SupressDefaultAnimation = SupressDefaultAnimation.Value;
        if (HoldEyePosAfterEasein != null) anim.HoldEyePosAfterEasein = HoldEyePosAfterEasein.Value;
        if (ClientSide != null) anim.ClientSide = ClientSide.Value;
        if (WithFpVariant != null) anim.WithFpVariant = WithFpVariant.Value;
        if (AnimationSound != null) anim.AnimationSound = AnimationSound.Clone();
    }
}

/// <summary>
/// Defines a config for scaling a base model shape element part.
/// Typically used to define min/max scaling range for a bone,
/// which will scale all shape element children of the bone.
/// </summary>
public class KemonoModelScale
{
    // name of this scaling part, e.g. "head"
    // we don't want to use bone name because bone name can be weird
    // and because we need this code for storing in entity tree,
    // lang lookup, etc.
    public string Code;
    // target shape name, e.g. "b_Head"
    public string Target;
    // min scale
    public double Min;
    // max scale
    public double Max;
    // whether this is the "main" scale of a model, e.g. the root
    // bone is the head of the model tree and scales everything.
    // this is used to decide if scale should affect model hitbox
    // and player eyeheight
    public bool IsMain = false;
}

/// <summary>
/// Definition of a texture target for skin parts.
/// </summary>
public class KemonoTextureTarget
{
    // texture target name, e.g. "hair", "eyes"
    public string Code { get; init; }
    // skin part codes that render to this texture target
    // sorted by render order from low to high
    public string[] Parts { get; init; }
    // width of texture target
    public int Width { get; init; }
    // height of texture target
    public int Height { get; init; }

    /// <summary>
    /// Construct from set of skin parts. Will exception if parts empty.
    /// </summary>
    /// <param name="parts"></param>
    public KemonoTextureTarget(string code, KemonoSkinnablePart[] parts)
    {
        Code = code;
        Parts = parts.Select(p => p.Code).ToArray();
        Width = parts[0].TextureTargetWidth;
        Height = parts[0].TextureTargetHeight;
    }
}

/// <summary>
/// Definition of a painting target for skin parts. Each can be shared
/// by multiple skin parts.
/// </summary>
public class KemonoPaintingTarget
{
    // painting target name, e.g. "cutiemark", "body"
    public string Code { get; init; }
    // size of painting pixels (square sized)
    public int Size { get; init; }
    // texture targets that this painting renders to
    // used to mark dirty texture targets when painting changes
    public string[] TextureTargets { get; init; }

    /// <summary>
    /// Construct a painting target.
    /// </summary>
    /// <param name="code"></param>
    /// <param name="size"></param>
    public KemonoPaintingTarget(string code, int size, IEnumerable<KemonoSkinnablePart> parts)
    {
        HashSet<string> textureTargets = new HashSet<string>();
        foreach (var part in parts)
        {
            if (part.UsePainting && part.TextureTarget != null)
            {
                textureTargets.Add(part.TextureTarget);
            }
        }

        Code = code;
        Size = size;
        TextureTargets = textureTargets.ToArray();
    }
}

/// <summary>
/// Config to copy from one texture target to another. This is used
/// for parts that still a base texture with overlays on top.
/// E.g. copy "main" texture to "clothing" as a base for overlays.
/// From is "main" and to is "clothing".
/// </summary>
public class TextureTargetCopy
{
    // base texture target name
    public string From { get; init; }

    // target texture name
    public string To { get; init; }

    // if needed, add config to copy from crop of base texture target
}

/// <summary>
/// Config for clothing overlay textures. This is used to render
/// clothing texture overlays onto a base texture target.
/// </summary>
public class TextureClothingOverlay
{
    // base texture target name
    public string Target { get; init; }

    // clothing overlay texture target slots
    public EnumCharacterDressType[] ClothingSlots { get; init; }

    // overlay texture offsets
    public float OffsetX { get; init; }
    public float OffsetY { get; init; }
}

/// <summary>
/// Shape tree element wrapper with list for children.
/// ShapeElement uses fixed array of children, making adding elements slow
/// during shape building. The list is copy on write, flagged by `Owned`.
/// </summary>
public class KemonoShapeBaseElement
{
    // backing element
    public ShapeElement Element { get; init; }

    // virtual children elements
    public List<ShapeElement> Children = new List<ShapeElement>();

    // if true, can modify children list.
    // if false, clone children list first before modifying.
    public bool Owned = true;

    /// <summary>
    /// Add child shape element to this attachable base element.
    /// </summary>
    /// <param name="child"></param>
    public void Add(ShapeElement child)
    {
        if (!Owned)
        {
            Children = new List<ShapeElement>(Children);
            Owned = true;
        }

        Children.Add(child);
    }

    /// <summary>
    /// Returns a shallow clone with borrowed children list.
    /// </summary>
    /// <returns></returns>
    public KemonoShapeBaseElement Clone()
    {
        return new KemonoShapeBaseElement
        {
            Element = Element,
            Children = Children,
            Owned = false,
        };
    }
}

/// <summary>
/// Intermediate shape element tree for quickly adding skin part
/// shape elements to the base model. Used to split building final shape
/// into multiple cached stages. After adding shape parts, use
/// `Compile()` to write children into the base shape elements.
/// Both the source Shape and this tree use the same shape element object
/// references, so when this compiles it will modify the original Shape
/// as well.
/// </summary>
public class KemonoShapeTree
{
    /// <summary>
    /// Initial base shape elements loaded from file.
    /// These are only elements where skin parts can be attached and remain
    /// the same across skin part addition passes.
    /// </summary>
    public KemonoShapeBaseElement[] BaseElements { get; init; }

    /// <summary>
    /// Mapping from element name to shape element (same object
    /// references in BaseElements).
    /// </summary>
    public Dictionary<string, KemonoShapeBaseElement> BaseElementsByName { get; init; }

    /// <summary>
    /// Parameterless constructor for cloning.
    /// </summary>
    public KemonoShapeTree() { }

    /// <summary>
    /// Build from vs shape.
    /// </summary>
    /// <param name="shape"></param>
    public KemonoShapeTree(Shape shape)
    {
        List<KemonoShapeBaseElement> baseElements = new List<KemonoShapeBaseElement>();
        BaseElementsByName = new Dictionary<string, KemonoShapeBaseElement>(StringComparer.OrdinalIgnoreCase);

        // walk shape tree and create wrapper structure
        void ProcessElement(ShapeElement elem, ShapeElement parent)
        {
            // store parent element
            elem.ParentElement = parent;

            // store attachment point parent element
            if (elem.AttachmentPoints != null)
            {
                for (int i = 0; i < elem.AttachmentPoints.Length; i++)
                {
                    elem.AttachmentPoints[i].ParentElement = elem;
                }
            }

            // build wrapper
            var treeElem = new KemonoShapeBaseElement { Element = elem };
            treeElem.Children.AddRange(elem.Children ?? Array.Empty<ShapeElement>());
            baseElements.Add(treeElem);
            BaseElementsByName[elem.Name] = treeElem;

            if (treeElem.Children.Count == 0) return;

            // walk children
            foreach (var child in elem.Children)
            {
                ProcessElement(child, elem);
            }
        }

        // walk base shape element tree and process elements
        foreach (var elem in shape.Elements)
        {
            ProcessElement(elem, null);
        }

        // convert to array
        BaseElements = baseElements.ToArray();
    }

    /// <summary>
    /// Keeps the backing ShapeElements, clone virtual KemonoShapeTreeElement
    /// in children lists and ElementsByName dictionary.
    /// </summary>
    /// <returns></returns>
    public KemonoShapeTree Clone()
    {
        var clone = new KemonoShapeTree
        {
            BaseElements = BaseElements.Select(e => e.Clone()).ToArray(),
            BaseElementsByName = new Dictionary<string, KemonoShapeBaseElement>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var elem in clone.BaseElements)
        {
            clone.BaseElementsByName[elem.Element.Name] = elem;
        }

        return clone;
    }

    /// <summary>
    /// Keeps the backing ShapeElements, clone virtual KemonoShapeTreeElement
    /// in children lists and ElementsByName dictionary while filtering out
    /// elements in removed filter.
    /// </summary>
    /// <returns></returns>
    public KemonoShapeTree FilteredClone(HashSet<string> remove)
    {
        var clone = new KemonoShapeTree
        {
            BaseElements = BaseElements
                .Where(e => !remove.Contains(e.Element.Name))
                .Select(e => e.Clone())
                .ToArray(),
            BaseElementsByName = new Dictionary<string, KemonoShapeBaseElement>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var elem in clone.BaseElements)
        {
            clone.BaseElementsByName[elem.Element.Name] = elem;

            // filter out children that are removed
            elem.Children = elem.Children
                .Where(c => !remove.Contains(c.Name))
                .ToList();
        }

        return clone;
    }

    /// <summary>
    /// Add a skin part shape element to the tree.
    /// </summary>
    /// <returns>True if succesful, false if failed.</returns>
    public bool AddSkinPartShape(Entity entity, ShapeElement elem, ILogger logger = null, string shapePath = "")
    {
        if (elem.StepParentName == null)
        {
            logger?.Warning("Skin part shape element {0} in shape {1} defined in entity config {2} did not define a step parent element. Will not be visible.",
                elem.Name, shapePath, entity.Properties.Code);
            return false;
        }

        // attach top level elements to step parent element
        if (!BaseElementsByName.TryGetValue(elem.StepParentName, out KemonoShapeBaseElement parentElem))
        {
            logger?.Warning("Skin part shape {0} defined in entity config {1} requires step parent element with name {2}, but no such element found. Will not be visible.",
                shapePath, entity.Properties.Code, elem.StepParentName);
            return false;
        }

        // store parent element
        elem.ParentElement = parentElem.Element;
        parentElem.Add(elem);
        elem.SetJointIdRecursive(parentElem.Element.JointId);

        // store attachment point parent element
        if (elem.AttachmentPoints != null)
        {
            for (int i = 0; i < elem.AttachmentPoints.Length; i++)
            {
                elem.AttachmentPoints[i].ParentElement = elem;
            }
        }

        return true;
    }

    /// <summary>
    /// Clear children for base element from code.
    /// </summary>
    /// <param name="code"></param>
    public void ClearBaseElementChildren(string code)
    {
        // detach all children from their parent element
        if (!BaseElementsByName.TryGetValue(code, out KemonoShapeBaseElement baseElem))
        {
            return;
        }

        baseElem.Children.Clear();
    }

    /// <summary>
    /// Writes virtual children into the backing shape element children.
    /// </summary>
    /// <param name="shape"></param>
    public void Compile()
    {
        foreach (var treeElem in BaseElements)
        {
            treeElem.Element.Children = treeElem.Children.ToArray();
        }
    }
}

/// <summary>
/// Simple skin part selection class for use in emotes.
/// </summary>
[JsonObject]
public struct SkinPartSelection
{
    public string PartCode;
    public string VariantCode;
}


/// <summary>
/// Kemono emote gui column position.
/// </summary>
public enum KemonoEmoteGuiPosition
{
    Default,
    Left,
    Right
}

/// <summary>
/// Kemono emote.
/// </summary>
[JsonObject(MemberSerialization.OptOut)]
public class KemonoEmote
{
    /// <summary>
    /// Emote identifier name.
    /// </summary>
    public string Code;

    /// <summary>
    /// Optional emote name for display in gui.
    /// </summary>
    public string Name;

    /// <summary>
    /// Animation code to play on client when emote is active.
    /// </summary>
    public string Animation;

    /// <summary>
    /// Mapping from skin part to skin part choice when emote active.
    /// </summary>
    public Dictionary<string, string> SkinParts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// If count nonzero, only modify shape elements for parent shape
    /// elements inside this filter. Using array search because assumed
    /// this filter will be very small, e.g. 1-5 elements at most.
    /// </summary>
    public string[] SkinPartFilter = Array.Empty<string>();

    /// <summary>
    /// Icon for display in gui.
    /// </summary>
    public AssetLocation Icon = null;
    
    /// <summary>
    /// Manually set the gui column for emote.
    /// By default, emotes with animations will be placed on the right
    /// side, others on left. This overrides manual behavior to
    /// specify left or right side.
    /// </summary>
    public KemonoEmoteGuiPosition Gui = KemonoEmoteGuiPosition.Default;

    [JsonIgnore]
    public SkinPartSelection[] SkinPartsList = Array.Empty<SkinPartSelection>();

    public void Initialize()
    {
        // convert skin parts dictionary to list
        SkinPartsList = SkinParts.Select(x => new SkinPartSelection
        {
            PartCode = x.Key,
            VariantCode = x.Value
        }).ToArray();
    }
}

/// A model shape and its skinnable parts.
/// TODO: store model types per entity type, use EntityTypes attributes
/// https://apidocs.vintagestory.at/api/Vintagestory.API.Common.IWorldAccessor.html#Vintagestory_API_Common_IWorldAccessor_EntityTypes
[JsonObject(MemberSerialization.OptOut)]
public class KemonoSkinnableModel
{
    // model base name, e.g. "kemono"
    // should be unique, as models are accessed by code
    public string Code;
    
    // addon name, equal to model code it is addon for.
    // e.g. if this is "kemono0" it appends skinparts into the
    // "kemono0" model.
    public string Addon;

    // shapes folder path
    public AssetLocation ShapePath;

    // skin part textures folder path
    public AssetLocation TexturePath;

    // main model shape json file path
    public AssetLocation Model;

    // hidden from character creation gui, e.g. for testing or wip models
    public bool Hidden = false;

    // joint names used for head controller
    public string JointHead = "b_Head";
    public string JointNeck = "b_Neck";
    public string JointTorsoUpper = "UpperTorso";
    public string JointTorsoLower = "LowerTorso";
    public string JointLegUpperL = "b_FootUpperL";
    public string JointLegUpperR = "b_FootUpperR";

    // flat list of all available model scaling parts
    public List<KemonoModelScale> ScaleParts = new List<KemonoModelScale>();

    // flat list of all available skin parts
    public List<KemonoSkinnablePart> SkinParts = new List<KemonoSkinnablePart>();

    // flat list of all available emotes
    public List<KemonoEmote> Emotes = new List<KemonoEmote>();

    // emotes by code, same objects as in Emotes
    [JsonIgnore]
    public Dictionary<string, KemonoEmote> EmotesByCode;

    // mapping from vanilla bone => kemono bone name
    // for automatic `elementWeight` and `elementBlendMode` remapping
    public Dictionary<string, string> AnimationRemappings = new Dictionary<string, string>();

    // list of all animation overrides
    public List<KemonoAnimationOverride> AnimationOverrides = new List<KemonoAnimationOverride>();

    // list of shape animation addon sets codes
    // (stored in ModSystemKemono.AvailableAnimations)
    public List<string> Animations = new List<string>();
    
    // character creation gui layout
    public List<string[]> GuiLayout = new List<string[]>();

    // gui character rendering y height offset
    // (different models different heights, use this to center vertically)
    public double GuiRenderHeightOffset = 0; 

    // eye height override (default seraph is 1.7)
    public double EyeHeight = 1.7;

    // eye height offset slider bounds
    public double EyeHeightOffsetMin = -0.5;
    public double EyeHeightOffsetMax = 0.5;

    // hitbox size override
    public Vec2f HitBoxSize = new Vec2f(0.6f, 1.85f);

    // list of all shape paths, includes addon shape paths
    [JsonIgnore]
    public List<AssetLocation> ShapePaths = new List<AssetLocation>();

    // list of all texture paths, includes addon texture paths
    [JsonIgnore]
    public List<AssetLocation> TexturePaths = new List<AssetLocation>();

    // scale part target => scale part (NOT PART CODE)
    // this is used to lookup from element to scale part when walking
    // shape tree to apply scaling
    [JsonIgnore]
    public Dictionary<string, KemonoModelScale> ScalePartsByTarget;

    // skin parts sorted by part.RenderOrder value (low to high)
    [JsonIgnore]
    public List<KemonoSkinnablePart> SkinPartsByRenderOrder;

    // skin part code => skin part (same objects as in SkinParts list)
    // (must be initialized after object created)
    [JsonIgnore]
    public Dictionary<string, KemonoSkinnablePart> SkinPartsByCode;
    
    // parts associated with each texture target
    [JsonIgnore]
    public Dictionary<string, string[]> TextureTargetParts;

    // texture targets for skin parts
    [JsonIgnore]
    public KemonoTextureTarget[] TextureTargets;

    // custom paintable texture names, equal to name of parts that
    // use paintable textures
    [JsonIgnore]
    public KemonoPaintingTarget[] PaintingTargets;

    // List of texture copies determined from parts config
    // e.g. copy "main" texture to "clothing" as a base for clothing
    [JsonIgnore]
    public TextureTargetCopy[] TextureCopies;

    // List of clothing overlays for texture targets
    [JsonIgnore]
    public TextureClothingOverlay[] TextureClothingOverlays;

    // List of extra animations to be added to the model
    [JsonIgnore]
    public Animation[] AnimationsCompiled;

    /// <summary>
    /// Post creation initialization, called by mod system.
    /// Returns self so this can be chained after new instance creation. <summary>
    /// </summary>
    /// <returns></returns>        
    public KemonoSkinnableModel Initialize()
    {
        // add self shape and texture paths
        ShapePaths.Add(ShapePath);
        TexturePaths.Add(TexturePath);

        // map scale parts target => scale part
        ScalePartsByTarget = new Dictionary<string, KemonoModelScale>();
        foreach (var part in ScaleParts)
        {
            ScalePartsByTarget[part.Target] = part;
        }

        // map skin part code => skin part
        SkinPartsByCode = new Dictionary<string, KemonoSkinnablePart>();
        foreach (var part in SkinParts)
        {
            SkinPartsByCode[part.Code] = part;
        }

        // sort skin parts by render order
        SkinPartsByRenderOrder = SkinParts
            .Where(p => p.Type != EnumKemonoSkinnableType.Voice)
            .OrderBy(p => p.RenderOrder)
            .ToList();

        // pre-load texture targets
        var textureTargetNames = new HashSet<string>();
        var paintingTargetSizes = new Dictionary<string, int>();
        var textureCopies = new List<TextureTargetCopy>();
        var textureClothingOverlays = new List<TextureClothingOverlay>();
        
        foreach (var part in SkinPartsByRenderOrder)
        {
            if (part.TextureTarget != null)
            {
                if (textureTargetNames.Add(part.TextureTarget))
                {
                    // if true, this is first skin part using this texture target
                    // for this first part, we want to render into atlas
                    // without blending so that it acts as base texture
                    part.TextureBlendOverlay = false;
                }
            }

            if (part.PaintingName != null && part.PaintingSize > 0)
            {
                paintingTargetSizes[part.PaintingName] = part.PaintingSize;
            }

            if (part.TextureCopyFrom != null)
            {
                textureCopies.Add(new TextureTargetCopy
                {
                    From = part.TextureCopyFrom,
                    To = part.TextureTarget
                });
            }

            if (part.UseClothingTexture)
            {
                textureClothingOverlays.Add(new TextureClothingOverlay
                {
                    Target = part.TextureTarget,
                    ClothingSlots = part.ClothingTextures,
                    OffsetX = part.TextureRenderTo.X,
                    OffsetY = part.TextureRenderTo.Y
                });
            }
        }

        // convert texture targets into array and assign each skin part
        // to its texture target array index
        TextureTargets = textureTargetNames.Select(name => new KemonoTextureTarget(
            name,
            SkinPartsByRenderOrder
                .Where(p => p.TextureTarget == name)
                .ToArray()
        )).ToArray();

        // map each part to its texture target index
        for (int i = 0; i < TextureTargets.Length; i++)
        {
            var texTarget = TextureTargets[i];
            foreach (var partCode in texTarget.Parts)
            {
                SkinPartsByCode[partCode].TextureTargetIndex = i;
            }
        }

        // create list of painting targets
        PaintingTargets = paintingTargetSizes
            .Select(kv => new KemonoPaintingTarget(kv.Key, kv.Value, SkinPartsByRenderOrder))
            .ToArray();
        
        // create list of texture copies
        TextureCopies = textureCopies.ToArray();

        // create list of clothing overlays
        TextureClothingOverlays = textureClothingOverlays.ToArray();

        // initialize emotes
        foreach (var emote in Emotes)
        {
            emote.Initialize();
        }

        // map emotes by code
        EmotesByCode = Emotes.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);

        return this;
    }

    /// <summary>
    /// Apply another model as an "addon". Append another model's
    /// skin parts, animation overrides, and gui layout into this model.
    /// `Initialize()` needs to be called again after this.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public void ApplyAddon(KemonoSkinnableModel other)
    {
        // main model:
        // if other part model not null, overwrite this model
        if (other.Model != null)
        {
            Model = other.Model;
        }
        
        if (other.ShapePath != null)
        {
            ShapePaths.Add(other.ShapePath);
        }
        
        if (other.TexturePath != null)
        {
            TexturePaths.Add(other.TexturePath);
        }

        // scale parts:
        // directly append
        ScaleParts.AddRange(other.ScaleParts);

        // skinparts:
        // if part code already exists, append variants into existing part
        // otherwise, add new part
        foreach (var otherPart in other.SkinParts)
        {
            int idx = SkinParts.FindIndex(p => p.Code == otherPart.Code);
            if (idx != -1) // append variants
            {
                var existingPart = SkinParts[idx];
                existingPart.Variants = existingPart.Variants.Concat(otherPart.Variants).ToArray();
            }
            else // add new part
            {
                SkinParts.Add(otherPart);
            }
        }

        // animation overrides:
        // if animation code already exists, replace existing
        // otherwise, add new
        foreach (var anim in other.AnimationOverrides)
        {
            int idx = AnimationOverrides.FindIndex(a => a.Code == anim.Code);
            if (idx != -1) // replace existing
            {
                AnimationOverrides[idx] = anim;
            }
            else // add new
            {
                AnimationOverrides.Add(anim);
            }
        }

        // add animation sets
        if (other.Animations != null)
        {
            Animations.AddRange(other.Animations);
        }

        // add emotes
        if (other.Emotes != null)
        {
            Emotes.AddRange(other.Emotes);
        }

        // append gui layout directly
        GuiLayout.AddRange(other.GuiLayout);
    }
}

/// Preset configuration for a skinnable model.
[JsonObject(MemberSerialization.OptOut)]
public class KemonoSkinnableModelPreset
{
    // model name, e.g. "kemono0" or "horse0"
    public string Model { get; init; } = "";
    
    // part code => selected variant
    public Dictionary<string, string> Parts { get; init; } = new Dictionary<string, string>();

    // part code => scale factor
    public Dictionary<string, double> Scale { get; init; } = new Dictionary<string, double>();
    
    // part code => selected colors in RGBA
    public Dictionary<string, KemonoColorRGB> Colors { get; init; } = new Dictionary<string, KemonoColorRGB>();
}

#region EntityBehaviorKemonoSkinnable

/// <summary>
/// Entity behavior for kemono skinnable entities.
/// </summary>
public class EntityBehaviorKemonoSkinnable : EntityBehavior
{
    // skin tree keys for applied skinnable config
    // NOTE: vanilla uses "appliedParts", use "kemo___" prefix to
    // avoid conflicts with vanilla character config
    public const string APPLIED_MODEL = "kemonoAppliedModel";
    public const string APPLIED_VARIANT = "kemonoAppliedVariant";
    public const string APPLIED_COLOR = "kemonoAppliedColor";
    public const string APPLIED_SCALE = "kemonoAppliedScale";
    public const string APPLIED_GLOW = "kemonoAppliedGlow";
    public const string PAINTINGS = "kemonoPainting";
    public const string EYEHEIGHT = "kemonoEyeHeight";

    // CLIENT SETTINGS

    // client voice type and pitch: stores local changes on client during
    // character creation, before sending to server to synchronize with
    // other clients
    public string VoiceType = "altoflute";
    public string VoicePitch = "medium";

    // currently selected model (points to same object in mod system's AvailableModels)
    public KemonoSkinnableModel Model;

    /// <summary>
    /// Get currently applied model code stored in entity attributes.
    /// </summary>
    /// <returns></returns>
    public string AppliedModel
    {
        get => entity.WatchedAttributes.GetString(APPLIED_MODEL);
        set => entity.WatchedAttributes.SetString(APPLIED_MODEL, value);
    }

    /// Get dict for appplied colors if exists: part [string] => variant [string]
    public TreeAttribute AppliedVariant;

    /// Get dict for appplied colors if exists: part [string] => color [int]
    public TreeAttribute AppliedColor;

    /// Get dict for applied scales if exists: part [string] => scale [double]
    public TreeAttribute AppliedScale;

    /// Get dict for applied glow if exists: part [string] => glow [int]
    public TreeAttribute AppliedGlow;

    /// Get paintings tree, return null if it does not exist.
    public TreeAttribute Paintings;

    /// <summary>
    /// Get currently applied player eye height offset.
    /// </summary>
    /// <returns></returns>
    public double AppliedEyeHeightOffset
    {
        get => entity.WatchedAttributes.GetDouble(EYEHEIGHT, 0);
        set => entity.WatchedAttributes.SetDouble(EYEHEIGHT, value);
    }

    /// <summary>
    /// Currently active emotes being used, overrides base skin parts
    /// (and can play animations).
    /// </summary>
    public List<KemonoEmote> ActiveEmotes = new List<KemonoEmote>();

    /// <summary>
    /// Return applied skin part variants tree as a dictionary that satisfies
    /// IReadOnlyDictionary interface (used in race validation).
    /// </summary>
    /// <returns></returns>
    public SkinPartsTreeDictionary GetSkinPartsTreeDictionary() => new SkinPartsTreeDictionary(AppliedVariant);

    /// <summary>
    /// Base shape asset loaded during `SetModel()`.
    /// </summary>
    public Shape BaseShape;

    /// <summary>
    /// Base shape elements by name for animation building.
    /// </summary>
    public Dictionary<string, ShapeElement> BaseShapeElementsByName = new Dictionary<string, ShapeElement>();

    /// <summary>
    /// Base shape tree before adding any skin parts, derived from BaseShape.
    /// </summary>
    public KemonoShapeTree ShapeTreeBase = null;

    /// <summary>
    /// Cached shape tree after adding main skin parts to `ShapeTreeBase`.
    /// </summary>
    public KemonoShapeTree ShapeTreeAfterMainParts = null;

    /// <summary>
    /// Cached shape tree after adding face parts to `ShapeTreeAfterMainParts`.
    /// </summary>
    public KemonoShapeTree ShapeTreeAfterFaceParts = null;

    /// <summary>
    /// Cached shape tree after filtering parts from
    /// `ShapeTreeAfterFaceParts` from inventory item filtering.
    /// (e.g. hats hiding hair).
    /// </summary>
    public KemonoShapeTree ShapeTreeAfterFilter = null;

    /// <summary>
    /// Cached base animations defined in vanilla `entity/player.json`.
    /// These should be used as base animations before remapping
    /// and applying model-specific animation overrides.
    /// </summary>
    public Dictionary<string, AnimationMetaData> CachedBaseAnimations = new Dictionary<string, AnimationMetaData>();

    // flags to check for clothing or armor change to update shape.
    // these are true if player is using a skin part that has an alt
    // clothing or armor model. this must re-build shape when clothing
    // or armor changes.
    public bool RebuildWhenClothingChanged = false;
    public bool RebuildWhenArmorChanged = false;

    // slots to check for clothing or armor change
    public EnumCharacterDressType[] ClothingToCheck = { };
    public EnumCharacterDressType[] ArmorToCheck = { };

    // flags to check for previous clothing or armor state, to track
    // changes in clothing or armor state
    public bool PreviousIsClothed = false;
    public bool PreviousIsArmored = false;

    // previous tesselation set of shape elements removed by clothing,
    // used to decide if need to re-build shape when clothing changes
    public HashSet<string> PrevClothingRemovedElements = new HashSet<string>();

    // DIRTY FLAGS

    // flag that base shape is dirty and needs to be rebuilt
    public bool DirtyBaseShape = true;

    // flag that facial element shapes are dirty and need to be re-built
    // if base shape dirty, that will trigger face rebuild as well
    public bool DirtyFaceShape = true;

    // flag that animations changed and is dirty
    // occurs when model is changed or animations are reloaded
    // signals OnTesselation to rebuild joints and animation cache
    public bool DirtyAnimations = true;

    // TODO: optimization this shit
    // thoughts: set limit to number of paintings and
    // texture targets, e.g. max like 32 or 64 targets. Use a bit
    // array of dirty flags and map each part's painting/texture to 
    // its dirty bit index.
    // make paintings use indices to point to associated texture tagets

    // set of dirty paintings that need to be re-rendered
    public HashSet<string> DirtyPainting = new HashSet<string>();

    // flags that texture target is dirty (from changing texture or color)
    public HashSet<string> DirtyTexture = new HashSet<string>();

    public EntityBehaviorKemonoSkinnable(Entity entity) : base(entity)
    {
        // initialize applied part trees and paintings
        AppliedVariant = entity.WatchedAttributes.GetOrAddTreeAttribute(APPLIED_VARIANT) as TreeAttribute;
        AppliedColor = entity.WatchedAttributes.GetOrAddTreeAttribute(APPLIED_COLOR) as TreeAttribute;
        AppliedScale = entity.WatchedAttributes.GetOrAddTreeAttribute(APPLIED_SCALE) as TreeAttribute;
        AppliedGlow = entity.WatchedAttributes.GetOrAddTreeAttribute(APPLIED_GLOW) as TreeAttribute;
        Paintings = entity.WatchedAttributes.GetOrAddTreeAttribute(PAINTINGS) as TreeAttribute;
    }

    public override string PropertyName() => "kemonoskinnable";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        // re-initialize applied part trees and paintings
        AppliedVariant = entity.WatchedAttributes.GetOrAddTreeAttribute(APPLIED_VARIANT) as TreeAttribute;
        AppliedColor = entity.WatchedAttributes.GetOrAddTreeAttribute(APPLIED_COLOR) as TreeAttribute;
        AppliedScale = entity.WatchedAttributes.GetOrAddTreeAttribute(APPLIED_SCALE) as TreeAttribute;
        AppliedGlow = entity.WatchedAttributes.GetOrAddTreeAttribute(APPLIED_GLOW) as TreeAttribute;
        Paintings = entity.WatchedAttributes.GetOrAddTreeAttribute(PAINTINGS) as TreeAttribute;

        // add listeners to update stale attribute field references
        // because updates re-creates the class
        entity.WatchedAttributes.RegisterModifiedListener(APPLIED_VARIANT, delegate
        {
            AppliedVariant = entity.WatchedAttributes.GetTreeAttribute(APPLIED_VARIANT) as TreeAttribute;
        });
        entity.WatchedAttributes.RegisterModifiedListener(APPLIED_COLOR, delegate
        {
            AppliedColor = entity.WatchedAttributes.GetTreeAttribute(APPLIED_COLOR) as TreeAttribute;
        });
        entity.WatchedAttributes.RegisterModifiedListener(APPLIED_SCALE, delegate
        {
            AppliedScale = entity.WatchedAttributes.GetTreeAttribute(APPLIED_SCALE) as TreeAttribute;
        });
        entity.WatchedAttributes.RegisterModifiedListener(APPLIED_GLOW, delegate
        {
            AppliedGlow = entity.WatchedAttributes.GetTreeAttribute(APPLIED_GLOW) as TreeAttribute;
        });
        entity.WatchedAttributes.RegisterModifiedListener(PAINTINGS, delegate
        {
            Paintings = entity.WatchedAttributes.GetTreeAttribute(PAINTINGS) as TreeAttribute;
        });

        KemonoMod kemono = entity.Api.ModLoader.GetModSystem<KemonoMod>();

        // select initial model
        string initialModelName = AppliedModel ?? kemono.DefaultModelCode;
        // fallback to default if not found
        if (!kemono.AvailableModels.ContainsKey(initialModelName))
        {
            initialModelName = kemono.DefaultModelCode;
        }
        SetModel(initialModelName, force: true); // does skin part initialization as well

        // initialize voice
        VoiceType = entity.WatchedAttributes.GetString("voicetype");
        VoicePitch = entity.WatchedAttributes.GetString("voicepitch");
        ApplyVoice(VoiceType, VoicePitch, false);
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
        ClientInitialize();
    }

    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        ClientInitialize();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        base.OnEntityDespawn(despawn);

        var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
        krender?.FreeAllTextureTargets();
    }

    bool didClientInit = false;
    public void ClientInitialize()
    {
        if (entity.World.Side != EnumAppSide.Client) return;

        if (!didClientInit)
        {
            var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
            if (krender == null) throw new InvalidOperationException("The extra skinnable requires the entity to use a Kemono renderer.");

            var ebhtc = entity.GetBehavior<EntityBehaviorTexturedClothing>();
            if (ebhtc == null) throw new InvalidOperationException("The extra skinnable entity behavior requires the entity to have the TextureClothing entitybehavior.");

            didClientInit = true;
        }
    }

    /// Makes sure all skin parts have some initial variant selected.
    /// Runs on server and client.
    public void InitializeSkinParts()
    {
        KemonoMod kemo = entity.Api.ModLoader.GetModSystem<KemonoMod>();


        if (Model == null)
        {
            entity.Api.World.Logger.Error($"[InitializeSkinParts] Model is null");
            return;
        }

        // remove any applied parts that are no longer available
        List<string> toRemove = new List<string>();
        string[] treeNames = { "appliedVariant", "appliedColors" };
        ITreeAttribute[] trees = { AppliedVariant, AppliedColor };
        for (int i = 0; i < trees.Length; i++)
        {
            string treeName = treeNames[i];
            ITreeAttribute tree = trees[i];

            foreach (var val in tree)
            {
                // remove if key not in available parts
                if (!Model.SkinPartsByCode.TryGetValue(val.Key, out KemonoSkinnablePart part))
                {
                    toRemove.Add(val.Key);
                    continue;
                }

                // if string attribute (e.g. model or texture name)
                // remove if not in list of available variants
                if (val.Value is StringAttribute)
                {
                    if (part.Variants.Length == 0 || !part.VariantsByCode.ContainsKey((val.Value as StringAttribute).value))
                    {
                        toRemove.Add(val.Key);
                        continue;
                    }
                }
            }

            if (toRemove.Count > 0)
            {
                if (entity.Api.Side == EnumAppSide.Client)
                {
                    // only print on client to avoid spamming server logs
                    entity.Api.Logger.Warning($"[InitializeSkinParts] Removing {toRemove.Count} invalid attributes from {treeName}:");
                }

                foreach (var partCode in toRemove)
                {
                    tree.RemoveAttribute(partCode);
                }

                toRemove.Clear();
            }
        }

        // remove any applied scale parts that are no longer available
        foreach (var part in AppliedScale)
        {
            if (Model.ScaleParts.FindIndex(p => p.Code == part.Key) == -1)
            {
                toRemove.Add(part.Key);
            }
        }
        foreach (var partCode in toRemove)
        {
            AppliedScale.RemoveAttribute(partCode);
        }
        toRemove.Clear();


        // remove any painting targets either no longer available
        // or incorrect size (can cause overflows)
        foreach (var painting in Paintings)
        {
            int idx = Model.PaintingTargets.IndexOf(p => p.Code == painting.Key);
            if (idx == -1)
            {
                toRemove.Add(painting.Key);
            }
            else if (painting.Value is not ByteArrayAttribute)
            {
                toRemove.Add(painting.Key);
            }
            else
            {
                int size = Model.PaintingTargets[idx].Size;
                int bytesLength = size * size * 4;
                if ((painting.Value as ByteArrayAttribute).value.Length != bytesLength)
                {
                    toRemove.Add(painting.Key);
                }
            }
        }
        foreach (var paintingCode in toRemove)
        {
            Paintings.RemoveAttribute(paintingCode);

            if (entity.Api.Side == EnumAppSide.Client)
            {
                // only print on client to avoid spamming server logs
                entity.Api.Logger.Warning($"[InitializeSkinParts] Removing invalid painting attribute: {paintingCode}");
            }
        }
        toRemove.Clear();

        // create initial applied part for all available parts
        // use initial preset variant for model if available
        KemonoSkinnableModelPreset initialPreset = kemo.DefaultModelPresets.GetValueOrDefault(Model.Code);

        foreach (var part in Model.SkinParts)
        {
            // voice done somewhere else
            if (part.Type == EnumKemonoSkinnableType.Voice) continue;

            string partCode = part.Code;

            // initial variant
            if (part.Variants.Length > 0)
            {
                if (!AppliedVariant.HasAttribute(partCode))
                {
                    // default to first variant if no preset available
                    string initialCode = part.Variants[0].Code;

                    // if preset available, use preset variant
                    if (initialPreset?.Parts.TryGetValue(partCode, out string presetVariant) == true)
                    {
                        if (part.VariantsByCode.ContainsKey(presetVariant))
                        {
                            initialCode = presetVariant;
                        }
                    }

                    AppliedVariant[partCode] = new StringAttribute(initialCode);
                }
            }

            // initial random color
            if (!AppliedColor.HasAttribute(partCode))
            {
                // if preset available, use preset variant
                if (initialPreset?.Colors.TryGetValue(partCode, out KemonoColorRGB presetColor) == true)
                {
                    AppliedColor[partCode] = new IntAttribute(ColorUtil.ColorFromRgba(
                        presetColor.r,
                        presetColor.g,
                        presetColor.b,
                        255
                    ));
                }
                else
                {
                    // default to random color
                    AppliedColor[partCode] = new IntAttribute(ColorUtil.ColorFromRgba(
                        entity.Api.World.Rand.Next(255),
                        entity.Api.World.Rand.Next(255),
                        entity.Api.World.Rand.Next(255),
                        255
                    ));
                }
            }

            MarkPartTextureDirty(partCode);
        }

        DirtyBaseShape = true;
    }

    /// Randomize all skin parts and colors.
    /// This does not reload skin or re-tesselate the shape, the caller
    /// must make sure to properly re-render the entity.
    ///
    /// TODO: in future, make skin part config decide if it should randomize
    /// or force select a specific variant.
    public void RandomizeSkinParts()
    {
        if (Model == null)
        {
            entity.Api.World.Logger.Error($"[RandomizeSkinParts] Model is null");
            return;
        }

        foreach (var part in Model.SkinParts)
        {
            // do voice elsewhere
            if (part.Type == EnumKemonoSkinnableType.Voice) continue;

            string partCode = part.Code;

            // random variant
            if (part.Variants.Length > 1)
            {
                int idx = entity.Api.World.Rand.Next(part.Variants.Length);
                AppliedVariant[partCode] = new StringAttribute(part.Variants[idx].Code);
            }
            else if (part.Variants.Length == 1)
            {
                AppliedVariant[partCode] = new StringAttribute(part.Variants[0].Code);
            }

            // random part color
            if (part.UseColorSlider)
            {
                AppliedColor[partCode] = new IntAttribute(ColorUtil.ColorFromRgba(
                    entity.Api.World.Rand.Next(255),
                    entity.Api.World.Rand.Next(255),
                    entity.Api.World.Rand.Next(255),
                    255
                ));
            }

            // mark skin part texture dirty
            MarkPartTextureDirty(partCode);
        }

        DirtyBaseShape = true;
    }

    public AppliedKemonoSkinnablePartVariant ToAppliedVariant(
        KemonoSkinnablePart part,
        KemonoSkinnablePartVariant variant,
        string[] stepParentFilter = null
    ) {
        // if skinnable part type is a model and has a texture,
        // set the applied texture to base part's texture
        var appliedTexture = variant.Texture;
        if (part.Type == EnumKemonoSkinnableType.Shape && part.Texture != null)
        {
            appliedTexture = part.Texture;
        }

        // set variant color and glow if it exists
        int color = AppliedColor.TryGetInt(part.Code) ?? 0;
        int glow = AppliedGlow.TryGetInt(part.Code) ?? -1;

        return new AppliedKemonoSkinnablePartVariant
        {
            PartCode = part.Code,
            Code = variant.Code,
            AltClothed = variant.AltClothed,
            AltArmored = variant.AltArmored,
            Shape = variant.Shape,
            ShapeClothed = variant.ShapeClothed,
            ShapeArmored = variant.ShapeArmored,
            Texture = appliedTexture,
            Sound = variant.Sound,
            Skip = variant.Skip,
            Color = color,
            Glow = glow,
            stepParentFilter = stepParentFilter
        };
    }

    /// <summary>
    /// Get list of applied skin parts, optionally filtering by face parts.
    /// </summary>
    /// <param name="all"></param>
    /// <param name="faceParts"></param>
    /// <returns></returns>
    public List<AppliedKemonoSkinnablePartVariant> AppliedSkinParts(bool all, bool faceParts)
    {
        var appliedPartsList = new List<AppliedKemonoSkinnablePartVariant>();
        if (Model == null) return appliedPartsList;

        // emotes applied on top
        var appliedEmotePartsList = new List<AppliedKemonoSkinnablePartVariant>();
        var skipParts = new List<string>();

        // emote-prepass:
        // get list of emote parts (apply on top of base skin parts at end)
        // and determine which parts to skip.
        // this iterates in reverse so last emotes overwrite earlier ones
        for (int i = ActiveEmotes.Count - 1; i >= 0; i--)
        {
            var emote = ActiveEmotes[i];
            foreach (var (partCode, variantCode) in emote.SkinParts)
            {
                if (!Model.SkinPartsByCode.TryGetValue(partCode, out KemonoSkinnablePart part)) continue;
                if (part.Variants.Length == 0) continue;
                if (!all && (part.Face ^ faceParts)) continue; // XOR here, want (face & want face) or (not face & want not face)
                if (skipParts.Contains(partCode)) continue; // skipping this part from another emote

                if (part.VariantsByCode.TryGetValue(variantCode, out KemonoSkinnablePartVariant variant))
                {
                    appliedEmotePartsList.Add(ToAppliedVariant(part, variant, emote.SkinPartFilter));

                    if (emote.SkinPartFilter.Length == 0)
                    {
                        // no filter, replace entire part
                        skipParts.Add(partCode);
                    }
                }
            }
        }

        // parts are layered by their render order (from low to high)
        foreach (var part in Model.SkinPartsByRenderOrder)
        {
            if (part.Variants.Length == 0) continue;
            if (!all && (part.Face ^ faceParts)) continue; // XOR here, want (face & want face) or (not face & want not face)
            if (skipParts.Contains(part.Code)) continue; // skip parts overridden by emotes
            
            string variantCode = AppliedVariant.GetString(part.Code);
            if (variantCode == null) continue;

            if (part.VariantsByCode.TryGetValue(variantCode, out KemonoSkinnablePartVariant variant))
            {
                appliedPartsList.Add(ToAppliedVariant(part, variant));
            }
        }

        // add emote parts to end
        // note: this breaks normal render order, so make sure emotes dont
        // have any texture dependencies with other parts
        appliedPartsList.AddRange(appliedEmotePartsList);

        return appliedPartsList;
    }

    // helpers to get base and face skin parts
    public List<AppliedKemonoSkinnablePartVariant> AppliedBaseSkinParts() => AppliedSkinParts(false, false);
    public List<AppliedKemonoSkinnablePartVariant> AppliedFaceSkinParts() => AppliedSkinParts(false, true);
    public List<AppliedKemonoSkinnablePartVariant> AppliedAllSkinParts() => AppliedSkinParts(true, false);

    /// <summary>
    /// Helper to mark a skin part's texture target as dirty.
    /// </summary>
    /// <param name="partCode"></param>
    public void MarkPartTextureDirty(string partCode)
    {
        if (Model.SkinPartsByCode.TryGetValue(partCode, out KemonoSkinnablePart skinPart) == false)
        {
            entity.Api.World.Logger.Error($"[MarkPartTextureDirty] Part {partCode} not found in model {Model.Code}");
            return;
        }

        var target = skinPart.TextureTarget;
        if (target != null)
        {
            DirtyTexture.Add(target);
        }
    }

    /// <summary>
    /// Set a skin model, e.g. "kemono0", "horse0", etc. This initializes
    /// the skin parts and colors. Force required in cases when models
    /// are reloaded and currently stored model object is stale.
    /// </summary>
    /// <param name="code"></param>
    /// <param name="force"></param>
    public void SetModel(string code, bool force = false)
    {
        if (force == false && Model != null && code == Model.Code) return; // ignore if same model

        KemonoMod kemo = entity.Api.ModLoader.GetModSystem<KemonoMod>();

        if (kemo.AvailableModels.TryGetValue(code, out KemonoSkinnableModel newModel))
        {
            Model = newModel;
            AppliedModel = code;

            // load base shape asset
            var shapePath = newModel.Model.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            BaseShape = Shape.TryGet(entity.Api, shapePath).Clone();
            ShapeTreeBase = new KemonoShapeTree(BaseShape);
            BaseShapeElementsByName = new Dictionary<string, ShapeElement>();
            BaseShape.Elements.Foreach((elem) =>
                elem.WalkRecursive((el) => 
                {
                    if (el.Name != null) BaseShapeElementsByName[el.Name] = el;
                })
            );

            // store dummy clones of shape tree base
            ShapeTreeAfterMainParts = ShapeTreeBase;
            ShapeTreeAfterFaceParts = ShapeTreeBase;
            ShapeTreeAfterFilter = ShapeTreeBase;

            DirtyBaseShape = true;
            DirtyAnimations = true;
            entity.MarkShapeModified();

            if (entity.World.Side == EnumAppSide.Client)
            {
                // free texture allocations
                var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
                krender?.FreeAllTextureTargets();
            }

            // re-initialize skin parts
            InitializeSkinParts();
        }
    }

    /// <summary>
    /// Get skinnable custom drawn "painting" texture pixels.
    /// (Used for horse cutie mark). Creates default empty flat
    /// int array if no pixels found in skin tree.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="width"></param>
    /// <param name="height"></param>
    /// <returns></returns>
    public int[] GetPaintingPixels(string name, int width, int height)
    {
        byte[] paintingPixels = Paintings.GetBytes(name, null);
        if (paintingPixels != null)
        {
            // convert to int[]
            int[] pixels = new int[paintingPixels.Length / 4];
            Buffer.BlockCopy(paintingPixels, 0, pixels, 0, paintingPixels.Length);
            return pixels;
        }
        else
        {
            int[] pixels = new int[width * height];
            return pixels;
        }
    }

    /// <summary>
    /// Get painting pixels as a SkiaSharp bitmap, used for easy saving.
    /// </summary>
    /// <returns></returns>
    public SKBitmap GetPaintingBitmap(string name, int width, int height)
    {
        int[] pixels = GetPaintingPixels(name, width, height);

        SKBitmap bitmap = new SKBitmap(width, height);

        for (int y = 0; y < height; y += 1)
        {
            for (int x = 0; x < width; x += 1)
            {
                int pixel = pixels[y * width + x];
                var (b, g, r, a) = KemonoColorUtil.ToRgba(pixel); // load as bgra
                bitmap.SetPixel(x, y, new SKColor((byte)r, (byte)g, (byte)b, (byte)a));
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Set custom drawable "painting" texture pixels.
    /// (Used for horse cutie mark).
    /// </summary>
    /// <param name="name"></param>
    /// <param name="pixels"></param>
    public void SetPaintingPixels(string name, int[] pixels)
    {
        byte[] paintingBytes = Paintings.GetBytes(name, null);
        if (paintingBytes == null)
        {
            paintingBytes = new byte[4 * pixels.Length];
        }

        // check if same size
        if (paintingBytes.Length != 4 * pixels.Length)
        {
            entity.Api.World.Logger.Error($"[SetPaintingPixels] Pixels size mismatch for {name}");
            return;
        }

        // write pixels to bytes, save into skin tree
        Buffer.BlockCopy(pixels, 0, paintingBytes, 0, paintingBytes.Length);
        Paintings.SetBytes(name, paintingBytes);

        DirtyPainting.Add(name);

        // mark associated texture targets dirty
        var paintingTarget = Model.PaintingTargets.FirstOrDefault(t => t.Code == name);
        if (paintingTarget != null)
        {
            DirtyTexture.AddRange(paintingTarget.TextureTargets);
        }
    }

    /// <summary>
    /// Clear all pixels in a custom drawn "painting" texture.
    /// </summary>
    /// <param name="r"></param>
    /// <param name="g"></param>
    /// <param name="b"></param>
    /// <param name="alpha"></param>
    public void ClearPaintingPixels(string name, int r, int g, int b, int alpha = 0)
    {
        byte[] currentBytes = Paintings.GetBytes(name, null);
        if (currentBytes == null) return;

        int len = currentBytes.Length / 4;
        int[] clearPixels = new int[len];
        int clearColor = ColorUtil.ColorFromRgba(r, g, b, alpha);

        // fill with (r,g,b,a) pixels
        for (int i = 0; i < clearPixels.Length; i += 1)
        {
            clearPixels[i] = clearColor;
        }

        Buffer.BlockCopy(clearPixels, 0, currentBytes, 0, currentBytes.Length);
        Paintings.SetBytes(name, currentBytes);

        DirtyPainting.Add(name);

        // mark associated texture targets dirty
        var paintingTarget = Model.PaintingTargets.FirstOrDefault(t => t.Code == name);
        if (paintingTarget != null)
        {
            DirtyTexture.AddRange(paintingTarget.TextureTargets);
        }
    }


    /// <summary>
    /// Set painting pixels from a texture asset location. Texture must
    /// be 32x32 pixels. Return result with string error if fails.
    /// Path must be FULL TEXTURE PATH WITH DOMAIN, e.g.
    /// "kemono:textures/painting/vinylscratch.png".
    /// </summary>
    /// <param name="texPath"></param>
    public Result<bool, string> SetPaintingFromTexture(string name, int paintingSize, AssetLocation texPath)
    {
        var api = entity.Api;

        IAsset asset = api.Assets.TryGet(texPath);
        if (asset == null)
        {
            return Lang.Get("kemono:msg-painting-load-error-no-file-exist", texPath);
        }

        // convert texPath into full image path for loading
        string imgPath = asset.Origin.OriginPath + Path.DirectorySeparatorChar + texPath.Path;

        return SetPaintingFromImagePath(name, paintingSize, imgPath);
    }

    /// <summary>
    /// Set painting pixels from a image path. Image must be correct
    /// size x size pixels. Return result with string error if fails.
    /// </summary>
    /// <param name="texPath"></param>
    public Result<bool, string> SetPaintingFromImagePath(string name, int paintingSize, string imgPath)
    {
        // check if exists
        if (!File.Exists(imgPath))
        {
            return Lang.Get("kemono:msg-painting-load-error-no-file-exist", imgPath);
        }

        // load png into pixels (loads in BGRA format already)
        var bitmap = SKBitmap.Decode(imgPath);
        if (bitmap == null) return false;

        if (bitmap.Width != paintingSize || bitmap.Height != paintingSize)
        {
            return Lang.Get("kemono:msg-painting-load-error-size-wrong", paintingSize, paintingSize);
        }

        var skPixels = bitmap.Pixels;
        int[] pixels = new int[paintingSize * paintingSize];

        // make sure same length, note: error here should never occur
        if (skPixels.Length != pixels.Length)
        {
            return Lang.Get("kemono:msg-painting-load-error-size-wrong", paintingSize, paintingSize);
        }

        // copy pixels, then write to skin
        for (int i = 0; i < pixels.Length; i++)
        {
            var px = skPixels[i];
            pixels[i] = ColorUtil.ToRgba(px.Alpha, px.Red, px.Green, px.Blue);
        }

        SetPaintingPixels(name, pixels);

        return true;
    }

    /// <summary>
    /// Force clear all painting textures in atlas.
    /// </summary>
    public void ClearPaintingTextures()
    {
        ICoreClientAPI capi = entity.World.Api as ICoreClientAPI;
        var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
        var appliedParts = AppliedBaseSkinParts();

        foreach (var applied in appliedParts)
        {
            KemonoSkinnablePart part;
            Model.SkinPartsByCode.TryGetValue(applied.PartCode, out part);

            if (part.UsePainting && part.TextureTarget != null)
            {
                KemonoTextureAtlasAllocation texAtlasTarget = krender.GetOrAllocateTextureTarget(
                    part.TextureTarget,
                    part.TextureTargetWidth,
                    part.TextureTargetHeight
                );

                ClearTextureUtil.ClearTextureAtlasPos(
                    capi,
                    texAtlasTarget.TexPos,
                    texAtlasTarget.Width,
                    texAtlasTarget.Height
                );
            }
        }
    }

    /// <summary>
    /// Apply the preset config to this behavior.
    /// </summary>
    /// <param name="preset"></param>
    public void LoadPreset(KemonoSkinnableModelPreset preset)
    {
        KemonoMod kemo = entity.Api.ModLoader.GetModSystem<KemonoMod>();

        if (!kemo.AvailableModels.TryGetValue(preset.Model, out KemonoSkinnableModel newModel))
        {
            entity.Api.World.Logger.Error($"[kemono] LoadPreset {preset.Model} not found");
            return;
        }

        SetModel(preset.Model);

        // clear existing trees
        AppliedScale.Clear();

        // load applied variants, color
        foreach (var part in preset.Parts)
        {
            if (Model.SkinPartsByCode.TryGetValue(part.Key, out KemonoSkinnablePart skinpart))
            {
                if (skinpart.VariantsByCode.ContainsKey(part.Value))
                {
                    AppliedVariant[part.Key] = new StringAttribute(part.Value);

                    if (skinpart.Type == EnumKemonoSkinnableType.Shape)
                    {
                        DirtyBaseShape = true;
                    }

                    MarkPartTextureDirty(part.Key);
                }
            }
        }

        foreach (var scale in preset.Scale)
        {
            int scalePartIndex = Model.ScaleParts.FindIndex(p => p.Code == scale.Key);
            if (scalePartIndex == -1) continue;
            AppliedScale[scale.Key] = new DoubleAttribute(scale.Value);
            DirtyBaseShape = true;
        }

        foreach (var color in preset.Colors)
        {
            if (Model.SkinPartsByCode.ContainsKey(color.Key))
            {
                var rgb = color.Value;
                AppliedColor[color.Key] = new IntAttribute(ColorUtil.ColorFromRgba(
                    rgb.r,
                    rgb.g,
                    rgb.b,
                    255
                ));

                MarkPartTextureDirty(color.Key);
            }
        }

        if (entity.World.Side == EnumAppSide.Client && DirtyBaseShape)
        {
            entity.MarkShapeModified();
        }
    }

    /// <summary>
    /// Convert behavior's current applied skin to a preset.
    /// Return preset object. External caller should do file save to json.
    /// </summary>
    /// <returns>KemonoSkinnableModelPreset</returns>
    public KemonoSkinnableModelPreset SavePreset()
    {
        var appliedParts = new Dictionary<string, string>();
        var appliedScale = new Dictionary<string, double>();
        var appliedColors = new Dictionary<string, KemonoColorRGB>();

        if (Model != null)
        {
            // render order defined in config ensures correct layering
            foreach (var part in Model.SkinPartsByRenderOrder)
            {
                // save part variant selection
                if (part.Variants.Length > 0)
                {
                    string variantCode = AppliedVariant.GetString(part.Code);
                    if (variantCode != null && part.VariantsByCode.ContainsKey(variantCode))
                    {
                        appliedParts[part.Code] = variantCode;
                    }
                }

                // save part color selection
                int? color = AppliedColor.TryGetInt(part.Code);
                if (color != null)
                {
                    appliedColors[part.Code] = new KemonoColorRGB((int)color);
                }
            }

            // gather scale
            foreach (var part in Model.ScaleParts)
            {
                double? scale = AppliedScale.TryGetDouble(part.Code);
                if (scale != null)
                {
                    appliedScale[part.Code] = (double)scale;
                }
            }
        }

        return new KemonoSkinnableModelPreset
        {
            Model = Model.Code,
            Parts = appliedParts,
            Scale = appliedScale,
            Colors = appliedColors,
        };
    }


    /// <summary>
    /// Remove all skin part scaling.
    /// </summary>
    public void ClearModelPartScale(bool retesselateShape = true)
    {
        foreach (var scalePart in Model.ScaleParts)
        {
            AppliedScale.RemoveAttribute(scalePart.Code);
        }

        DirtyBaseShape = true;

        if (retesselateShape)
        {
            var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
            krender?.RunTesselation();
        }
    }

    public void SetModelPartScale(string partCode, double scale, bool retesselateShape = true)
    {
        AppliedScale[partCode] = new DoubleAttribute(scale);

        DirtyBaseShape = true;

        if (retesselateShape)
        {
            var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
            krender?.RunTesselation();
        }
    }

    public void SelectSkinPart(string partCode, string variantCode, bool retesselateShape = true, bool playVoice = true)
    {
        Model.SkinPartsByCode.TryGetValue(partCode, out var part);

        AppliedVariant[partCode] = new StringAttribute(variantCode);

        if (part?.Type == EnumKemonoSkinnableType.Voice)
        {
            entity.WatchedAttributes.SetString(partCode, variantCode);

            if (partCode == "voicetype")
            {
                VoiceType = variantCode;
            }
            else if (partCode == "voicepitch")
            {
                VoicePitch = variantCode;
            }

            ApplyVoice(VoiceType, VoicePitch, playVoice);
            return;
        }
        else if (part?.Type == EnumKemonoSkinnableType.Shape)
        {
            if (part.Face)
            {
                DirtyFaceShape = true;
            }
            else
            {
                DirtyBaseShape = true;
            }
        }

        // for now, just always mark texture dirty, easier
        // TODO: decide when necessary to re-render texture
        MarkPartTextureDirty(partCode);

        if (retesselateShape)
        {
            var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
            krender?.RunTesselation();
        }
    }

    public void SetSkinPartColor(string partCode, int color, bool retesselateShape = true)
    {
        AppliedColor[partCode] = new IntAttribute(color);

        // mark texture dirty
        MarkPartTextureDirty(partCode);

        if (retesselateShape)
        {
            var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
            krender?.RunTesselation();
        }
    }

    /// <summary>
    /// Set glow for a skin part. Glow is an integer value from 0 to 255.
    /// Retesselate shape should be used on client to retesselate.
    /// Sync should be used on server to force synchronizing with clients.
    /// </summary>
    /// <param name="partCode"></param>
    /// <param name="glow"></param>
    /// <param name="retesselateShape"></param>
    /// <param name="sync"></param>
    public void SetSkinPartGlow(
        string partCode,
        int glow,
        bool retesselateShape = true,
        bool sync = false
    ) {
        AppliedGlow[partCode] = new IntAttribute(glow);

        DirtyBaseShape = true;

        if (retesselateShape)
        {
            var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
            krender?.RunTesselation();
        }

        if (sync && entity.Api.Side == EnumAppSide.Server)
        {
            var kemono = entity.World.Api.ModLoader.GetModSystem<KemonoMod>();
            kemono?.ServerSyncGlow(this);
        }
    }

    // TODO: this is unnecessary, can just set some base eyeheight based 
    // on caching model eye height and scale, then just apply this on top
    // of the cached base eye height.
    public void SetEyeHeightOffset(double offset, bool retesselateShape = true)
    {
        if (Model != null)
        {
            offset = Math.Clamp(offset, Model.EyeHeightOffsetMin, Model.EyeHeightOffsetMax);
        }
        AppliedEyeHeightOffset = offset;

        DirtyBaseShape = true;

        if (retesselateShape)
        {
            var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
            krender?.RunTesselation();
        }
    }

    /// <summary>
    /// Remove glow from all skin parts.
    /// </summary>
    /// <param name="retesselateShape"></param>
    public void RemoveGlow(bool retesselateShape = false)
    {
        foreach (var part in Model.SkinParts)
        {
            if (AppliedGlow.HasAttribute(part.Code))
            {
                AppliedGlow.RemoveAttribute(part.Code);
            }
        }

        if (retesselateShape)
        {
            var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
            krender?.RunTesselation();
        }
    }


    /// <summary>
    /// Applies voice type and pitch to the entity. If player, sets voice
    /// settings in player talk util class.
    /// </summary>
    /// <param name="voiceType"></param>
    /// <param name="voicePitch"></param>
    /// <param name="testTalk"></param>
    public void ApplyVoice(string voiceType, string voicePitch, bool testTalk)
    {
        if (!Model.SkinPartsByCode.TryGetValue("voicetype", out var availVoices))
        {
            return;
        }

        if (voiceType == null || !availVoices.VariantsByCode.ContainsKey(voiceType))
        {
            if (availVoices.Variants.Length == 0)
            {
                entity.Api.World.Logger.Error("[ApplyVoice] No voice variants available");
                return;
            }

            voiceType = availVoices.Variants[0].Code;
        }

        VoiceType = voiceType;
        VoicePitch = voicePitch ?? "medium"; // default to medium pitch if null

        // apply to watched attributes (for server sync)
        entity.WatchedAttributes.SetString("voicetype", voiceType);
        entity.WatchedAttributes.SetString("voicepitch", voicePitch);

        if (entity is EntityPlayer plr && plr.talkUtil != null)
        {
            plr.talkUtil.soundName = availVoices.VariantsByCode[voiceType].Sound;

            float pitchMod = 1;
            switch (VoicePitch)
            {
                case "verylow": pitchMod = 0.6f; break;
                case "low": pitchMod = 0.8f; break;
                case "medium": pitchMod = 1f; break;
                case "high": pitchMod = 1.2f; break;
                case "veryhigh": pitchMod = 1.4f; break;
            }

            plr.talkUtil.pitchModifier = pitchMod;
            plr.talkUtil.chordDelayMul = 1.1f;

            if (testTalk)
            {
                plr.talkUtil.Talk(EnumTalkType.Idle);
            }
        }
    }

    /// <summary>
    /// Force reload entity model and animations. Use this after reloading
    /// models and assets in the mod (on client). This synchronizes the
    /// Model object reference stored in this behavior with the new
    /// Model object in the mod's AvailableModels after reloading and
    /// remaking Model objects.
    /// </summary>
    public void ReloadModel()
    {
        // force reload player model
        if (Model != null)
        {
            SetModel(Model.Code, true);
        }

        // clear entity animation cache
        ClearAnimationCache(AnimCacheKey());

        // force animation and model rebuild
        DirtyBaseShape = true;
        DirtyAnimations = true;

        // force re-tesselate
        var krender = entity.Properties.Client.Renderer as IKemonoRenderer;
        krender?.RunTesselation();
    }

    #endregion

    // ========================================================================
    #region RenderSkin
    // ========================================================================

    /// TODO:
    /// - Do texture colorization + merging in cpu space, try to do only
    ///   one write to texture atlas/gpu memory
    /// - Add masking for texture parts. Issue right now: have to add
    ///   a special white texture for eyes because base skin colorization
    ///   is colorizing eye white part. Adding masks would remove need
    ///   for special textures just to overwrite unnecessary changes
    ///   in base texture.
    public void RenderSkin()
    {
        if (entity.World.Side != EnumAppSide.Client) return; // should run on client only

        // PERFORMANCE DEBUGGING ONLY
        // var stopwatch = new Stopwatch();
        // stopwatch.Start();

        ICoreClientAPI capi = entity.World.Api as ICoreClientAPI;
        var krender = entity.Properties.Client.Renderer as IKemonoRenderer;

        var ebhtc = entity.GetBehavior<EntityBehaviorTexturedClothing>();
        var inv = ebhtc?.Inventory;

        foreach (var applied in AppliedAllSkinParts())
        {
            if (applied.Skip) continue;

            KemonoSkinnablePart part = Model.SkinPartsByCode[applied.PartCode];

            // Debug.WriteLine($"[OnReloadSkin] {part.Code} texIdx={part.TextureTargetIndex} {part.Type} {part.TextureTarget} {applied.Texture} {baseTexPos}");

            if (part.TextureTargetIndex == -1) continue;

            var target = Model.TextureTargets[part.TextureTargetIndex];
            var textureTarget = krender.GetOrAllocateTextureTarget(
                target.Code,
                target.Width,
                target.Height
            );

            bool dirtyTexture = DirtyTexture.Contains(target.Code);

            // load painting pixels into texture target
            if (part.UsePainting)
            {
                if (DirtyPainting.Contains(part.PaintingName) || dirtyTexture)
                {
                    var painting = GetPaintingPixels(part.PaintingName, part.PaintingSize, part.PaintingSize);
                    if (painting != null)
                    {
                        textureTarget.WritePixels(
                            painting,
                            part.PaintingSize,
                            part.PaintingSize,
                            part.TextureRenderTo.X,
                            part.TextureRenderTo.Y,
                            part.TextureBlendOverlay
                        );
                        DirtyTexture.Add(target.Code);
                        // remove dirty painting flag here to avoid double
                        // rendering for parts that re-use the same painting
                        DirtyPainting.Remove(part.PaintingName);
                    }
                }
            }
            // skip texture targets that use clothing textures, this is
            // rendered later in the clothing texture loop
            else if (part.UseClothingTexture)
            {
                continue;
            }
            // default: load part texture asset, render into pixel buffer
            else if (dirtyTexture && applied.Texture != null)
            {
                if (target.Parts[0] == part.Code) // clear pixels for base part
                {
                    textureTarget.ClearPixels();
                }

                var assetPath = applied.Texture.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png");

                IAsset skinAsset = capi.Assets.TryGet(assetPath);
                if (skinAsset == null)
                {
                    // try kemono domain
                    assetPath.Domain = "kemono";
                    skinAsset = capi.Assets.TryGet(assetPath);
                }

                if (skinAsset != null)
                {
                    // TODO: cleanup RGBA / BGRA formatting...
                    // im using RGBA format, but vintage textures load in BGRA
                    // format everything is format mismatched right now lmao,
                    // bandaged with rgba flipping in certain points.
                    // DO NOT convert loaded tex into GBRA, TOO SLOW LAGS GAME
                    Debug.WriteLine($"[OnReloadSkin] rendering assetPath={assetPath} to {target.Code}");

                    BitmapSimple bmp = new BitmapSimple(skinAsset.ToBitmap(capi));

                    if (part.UseColorSlider && applied.Color != 0)
                    {
                        // multiply part color onto texture bitmap
                        BitmapSimple bmpColored = new BitmapSimple(bmp);
                        bmpColored.MultiplyRgb(applied.Color);
                        bmp = bmpColored;
                    }

                    textureTarget.WritePixels(
                        bmp.Pixels,
                        bmp.Width,
                        bmp.Height,
                        part.TextureRenderTo.X,
                        part.TextureRenderTo.Y,
                        part.TextureBlendOverlay
                    );
                }
                else
                {
                    capi.World.Logger.Error($"[OnReloadSkin] Failed loading texture: {applied.Texture} for part {part.Code}");
                }
            }
        }

        // commits base and dirty texture targets into the entity tex atlas
        RenderTexturesToAtlas(capi, krender);

        // perform texture copies from base texture targets
        // e.g. custom clothing base texture copied from main texture
        RenderTextureCopies(capi, krender, Model.TextureCopies);

        // perform clothing texture overlays
        if (inv != null && !ebhtc.hideClothing)
        {
            RenderTextureClothing(capi, krender, inv, Model.TextureClothingOverlays);
        }

        // clear dirty lists
        DirtyTexture.Clear();

        // PERFORMANCE DEBUGGING ONLY
        // // end timestamp
        // stopwatch.Stop();
        // var dtTotal = stopwatch.Elapsed;
        // // print out time taken
        // Console.WriteLine($"[OnReloadSkin] Total: {dtTotal.Microseconds} us");
    }

    /// <summary>
    /// Commits all base and dirty texture targets into the entity
    /// texture atlas.
    /// </summary>
    public void RenderTexturesToAtlas(ICoreClientAPI capi, IKemonoRenderer krender)
    {
        foreach (var (key, tex) in krender.TextureAllocations)
        {
            // only render if:
            // 1. texture dirty
            // 2. texture is base texture: always render, because other
            //    systems render overlays onto base. gets too messy
            //    synchronizing dirty flag with other systems, so always
            //    render base texture for safety.
            if (!DirtyTexture.Contains(tex.Code) && tex.Code != krender.MainTextureName) continue;

            bool linearMag = false; // use nearest rendering
            int clampMode = 0;      // clamp mode
            float alphaTest = -1f; // need to use -1 when not overlaying fuck this game

            if (tex.Texture == null)
            {
                tex.Texture = new LoadedTexture(capi)
                {
                    Width = tex.Width,
                    Height = tex.Height
                };
            }

            capi.Render.LoadOrUpdateTextureFromBgra(
                tex.BgraPixels,
                linearMag,
                clampMode,
                ref tex.Texture
            );

            var texAtlasPos = tex.TexPos;

            capi.Render.GlToggleBlend(false, EnumBlendMode.Overlay);
            capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);

            capi.EntityTextureAtlas.RenderTextureIntoAtlas(
                texAtlasPos.atlasTextureId,
                tex.Texture,
                0,
                0,
                tex.Width,
                tex.Height,
                texAtlasPos.x1 * capi.EntityTextureAtlas.Size.Width,
                texAtlasPos.y1 * capi.EntityTextureAtlas.Size.Height,
                alphaTest
            );
        }
    }

    /// <summary>
    /// Perform texture to texture copies inside the entity texture atlas.
    /// </summary>
    public void RenderTextureCopies(ICoreClientAPI capi, IKemonoRenderer krender, TextureTargetCopy[] textureCopies)
    {
        if (textureCopies.Length == 0) return;

        capi.Render.GlToggleBlend(false, EnumBlendMode.Overlay);
        capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);

        foreach (var copy in textureCopies)
        {
            var fromTarget = krender.GetTextureTarget(copy.From);
            var toTarget = krender.GetTextureTarget(copy.To);

            if (fromTarget == null || toTarget == null) continue; // skip if not needed

            capi.EntityTextureAtlas.RenderTextureIntoAtlas(
                toTarget.TexPos.atlasTextureId,
                fromTarget.Texture,
                0,
                0,
                toTarget.Width,
                toTarget.Height,
                toTarget.TexPos.x1 * capi.EntityTextureAtlas.Size.Width,
                toTarget.TexPos.y1 * capi.EntityTextureAtlas.Size.Height,
                -1f
            );
        }
    }

    /// <summary>
    /// Render clothing overlays onto texture targets.
    /// </summary>
    /// <param name="capi"></param>
    /// <param name="krender"></param>
    /// <param name="clothingOverlays"></param>
    public void RenderTextureClothing(ICoreClientAPI capi, IKemonoRenderer krender, InventoryBase inv, TextureClothingOverlay[] clothingOverlays)
    {
        if (clothingOverlays.Length == 0) return;

        capi.Render.GlToggleBlend(false, EnumBlendMode.Standard);
        capi.Render.GlToggleBlend(true, EnumBlendMode.Overlay);

        // renders clothing textures onto textures using clothing
        foreach (var overlay in clothingOverlays)
        {
            var textureTarget = krender.GetTextureTarget(overlay.Target);

            if (textureTarget == null) continue; // skip if not needed

            foreach (var slot in overlay.ClothingSlots)
            {
                int slotid = (int)slot;

                ItemStack stack = inv[slotid]?.Itemstack;
                if (stack == null) continue;
                if (stack.Item.FirstTexture == null) continue; // Invalid/Unknown/Corrupted item

                foreach (var tex in stack.Item.Textures)
                {
                    // ignore "seraph" texture its literally empty, idk
                    // why tyrone still has this shit, probably legacy
                    // from old clothing system.
                    if (tex.Key == "seraph") continue;

                    int itemTextureSubId = tex.Value.Baked.TextureSubId;

                    TextureAtlasPosition itemTexPos = capi.ItemTextureAtlas.Positions[itemTextureSubId];

                    LoadedTexture itemAtlas = new LoadedTexture(null)
                    {
                        TextureId = itemTexPos.atlasTextureId,
                        Width = capi.ItemTextureAtlas.Size.Width,
                        Height = capi.ItemTextureAtlas.Size.Height
                    };

                    var texAtlasPos = textureTarget.TexPos;

                    // NOTE: this is unsafe, if item texture is
                    // larger than texture target, this will render
                    // off the texture target. properly should add
                    // bounds clamping
                    capi.EntityTextureAtlas.RenderTextureIntoAtlas(
                        texAtlasPos.atlasTextureId,
                        itemAtlas,
                        itemTexPos.x1 * capi.ItemTextureAtlas.Size.Width,
                        itemTexPos.y1 * capi.ItemTextureAtlas.Size.Height,
                        (itemTexPos.x2 - itemTexPos.x1) * capi.ItemTextureAtlas.Size.Width,
                        (itemTexPos.y2 - itemTexPos.y1) * capi.ItemTextureAtlas.Size.Height,
                        texAtlasPos.x1 * capi.EntityTextureAtlas.Size.Width + overlay.OffsetX,
                        texAtlasPos.y1 * capi.EntityTextureAtlas.Size.Height + overlay.OffsetY
                    );
                }
            }
        }

        capi.Render.GlToggleBlend(false, EnumBlendMode.Overlay);
        capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
    }

    #endregion

    // ========================================================================
    #region Tesselation
    // ========================================================================

    /// <summary>
    /// Event handler for entity renderer OnTesselation event. Constructs
    /// new kemono customized shape and replaces default entity shape.
    /// This is where all shape related aspects are handled: skin parts
    /// are attached to the base shape, skin part scaling, animation cache
    /// is reloaded and replaced.
    /// </summary>
    /// <param name="entityShape"></param>
    /// <param name="shapePathForLogging"></param>
    public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
    {
        // Console.WriteLine($"[OnTesselation] {entity.Api.Side}");

        //// PERFORMANCE DEBUGGING
        // print stack trace of caller
        // var side = entity.World.Side;
        // var st = new StackTrace(true);
        // // print full stack trace with line numbers
        // for (int i = 0; i < st.FrameCount; i++)
        // {
        //     var frame = st.GetFrame(i);
        //     Console.WriteLine($"[{side}] [OnTesselation] {frame.GetMethod()} {frame.GetFileName()}:{frame.GetFileLineNumber()}");
        // }

        // // performance debugging: initial timestamp for timing
        // var stopwatch = new Stopwatch();
        // stopwatch.Start();

        if (Model == null)
        {
            entity.Api.World.Logger.Error("[OnTesselation] Model is null");
            return; // no model selected yet
        }

        // setting entity shape to new loaded model shape
        entityShape = BaseShape;
        entity.Properties.Client.LoadedShapeForEntity = entityShape;

        var ebhtc = entity.GetBehavior<EntityBehaviorTexturedClothing>();
        var inv = ebhtc.Inventory;

        // check if clothing or armor change triggers skin part to use
        // alt model requiring shape re-build
        if (!DirtyBaseShape)
        {
            if (RebuildWhenClothingChanged)
            {
                bool isClothed = UseAltClothingModel(inv, ClothingToCheck) && !ebhtc.hideClothing;
                DirtyBaseShape = DirtyBaseShape || (isClothed != PreviousIsClothed);
                PreviousIsClothed = isClothed;
            }

            if (RebuildWhenArmorChanged)
            {
                bool isArmored = UseAltClothingModel(inv, ArmorToCheck) && !ebhtc.hideClothing;
                DirtyBaseShape = DirtyBaseShape || (isArmored != PreviousIsArmored);
                PreviousIsArmored = isArmored;
            }
        }

        // base shape creation
        if (DirtyBaseShape)
        {
            // GAME IS RETARDED AND HAS HIDDEN STATE FOR SHAPE SOMEWHERE
            // CAUSING SCALE TRANSFORM TO BE STORED AND FUCKED UP WHEN
            // CHANGING SCALE, SO WE HAVE TO KEEP RELOADING + CLONING SHAPE
            // EACH TIME UNTIL GAME REMOVES HIDDEN STATE
            // load base shape asset
            var shapePath = Model.Model.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            BaseShape = Shape.TryGet(entity.Api, shapePath).Clone();
            ShapeTreeBase = new KemonoShapeTree(BaseShape);
            BaseShapeElementsByName = new Dictionary<string, ShapeElement>();
            BaseShape.Elements.Foreach((elem) =>
                elem.WalkRecursive((el) =>
                {
                    if (el.Name != null) BaseShapeElementsByName[el.Name] = el;
                })
            );

            // store dummy clones of shape tree base
            ShapeTreeAfterMainParts = ShapeTreeBase;
            ShapeTreeAfterFaceParts = ShapeTreeBase;
            ShapeTreeAfterFilter = ShapeTreeBase;

            entityShape = BaseShape;
            entity.Properties.Client.LoadedShapeForEntity = entityShape;

            // main body scale (used for eye height and hitbox)
            double mainScale = 1.0;

            if (AppliedScale.Count > 0)
            {
                foreach (KemonoShapeBaseElement treeElem in ShapeTreeBase.BaseElements)
                {
                    ShapeElement elem = treeElem.Element;
                    if (Model.ScalePartsByTarget.TryGetValue(elem.Name, out KemonoModelScale scaling))
                    {
                        double? partScale = AppliedScale.TryGetDouble(scaling.Code);
                        if (partScale != null)
                        {
                            double scale = (double)partScale;
                            elem.ScaleX = scale;
                            elem.ScaleY = scale;
                            elem.ScaleZ = scale;

                            if (scaling.IsMain)
                            {
                                mainScale = (double)partScale;
                            }
                        }
                        else // reset scale
                        {
                            elem.ScaleX = 1.0;
                            elem.ScaleY = 1.0;
                            elem.ScaleZ = 1.0;
                        }
                    }
                }
            }

            // apply eye height
            // TODO: separate eyeheight property and slider
            entity.Properties.EyeHeight = (Model.EyeHeight * mainScale) + AppliedEyeHeightOffset;

            // apply hitbox size
            entity.SetCollisionBox(Model.HitBoxSize.X * (float)mainScale, Model.HitBoxSize.Y * (float)mainScale);

            // clear checks for clothing and armor change, re-determine
            // when adding parts
            RebuildWhenClothingChanged = false;
            RebuildWhenArmorChanged = false;

            HashSet<EnumCharacterDressType> clothingToCheck = new HashSet<EnumCharacterDressType>();
            HashSet<EnumCharacterDressType> armorToCheck = new HashSet<EnumCharacterDressType>();

            ShapeTreeAfterMainParts = ShapeTreeBase.Clone();

            // apply skin parts
            // (must be done on client and server, both sides need all shapes
            // for step parent attachments)
            var appliedParts = AppliedBaseSkinParts();

            foreach (var applied in appliedParts)
            {
                if (applied.Skip) continue;

                Model.SkinPartsByCode.TryGetValue(applied.PartCode, out KemonoSkinnablePart part);

                if (part != null && part.Type == EnumKemonoSkinnableType.Shape)
                {
                    if (applied.AltClothed != null && applied.AltClothed != "")
                    {
                        RebuildWhenClothingChanged = true;
                        clothingToCheck.AddRange(part.AltClothedRequirement);
                    }
                    if (applied.AltArmored != null && applied.AltArmored != "")
                    {
                        RebuildWhenArmorChanged = true;
                        armorToCheck.AddRange(part.AltArmoredRequirement);
                    }

                    Debug.WriteLine($"[OnTesselation] ADD SKIN PART {applied.PartCode}");
                    AddSkinPart(
                        ShapeTreeAfterMainParts,
                        ebhtc,
                        part,
                        applied,
                        entityShape,
                        shapePathForLogging,
                        applied.stepParentFilter
                    );
                }
            }

            // save clothing and armor slot checks
            ClothingToCheck = clothingToCheck.ToArray();
            ArmorToCheck = armorToCheck.ToArray();

            if (RebuildWhenClothingChanged)
            {
                PreviousIsClothed = UseAltClothingModel(inv, ClothingToCheck) && !ebhtc.hideClothing;
            }

            if (RebuildWhenArmorChanged)
            {
                PreviousIsArmored = UseAltClothingModel(inv, ArmorToCheck) && !ebhtc.hideClothing;
            }
        }

        // add face shape parts
        if (DirtyBaseShape || DirtyFaceShape)
        {
            ShapeTreeAfterFaceParts = ShapeTreeAfterMainParts.Clone();

            if (entity.World.Side == EnumAppSide.Client)
            {
                var appliedParts = AppliedFaceSkinParts();

                foreach (var applied in appliedParts)
                {
                    if (applied.Skip) continue;

                    Model.SkinPartsByCode.TryGetValue(applied.PartCode, out KemonoSkinnablePart part);

                    if (part != null && part.Face && part.Type == EnumKemonoSkinnableType.Shape)
                    {
                        AddSkinPart(
                            ShapeTreeAfterFaceParts,
                            ebhtc,
                            part,
                            applied,
                            entityShape,
                            shapePathForLogging,
                            applied.stepParentFilter
                        );
                    }
                }
            }

            DirtyFaceShape = false;
        }

        // remove parts in model when wearing clothing
        // e.g. hide hair when wearing helmet
        if (inv != null && ebhtc.hideClothing == false)
        {
            HashSet<string> clothingRemovedElements = new(0);

            foreach (var slot in inv)
            {
                if (slot.Empty) continue;

                ItemStack stack = slot.Itemstack;
                JsonObject attrObj = stack.Collectible.Attributes;

                var removeElements = attrObj?["disableElements"]?.AsArray<string>(null);
                if (removeElements != null)
                {
                    clothingRemovedElements.UnionWith(removeElements);
                }

                var keepEles = attrObj?["keepElements"]?.AsArray<string>(null);
                if (keepEles != null && willDeleteElements != null)
                {
                    foreach (var val in keepEles) willDeleteElements = willDeleteElements.Remove(val);
                }
            }

            // create filtered new shape with elements removed from
            // base shape.
            if (DirtyBaseShape || !clothingRemovedElements.Equals(PrevClothingRemovedElements))
            {
                ShapeTreeAfterFilter = ShapeTreeAfterFaceParts.FilteredClone(clothingRemovedElements);
            }
            else
            {
                ShapeTreeAfterFilter = ShapeTreeAfterFaceParts;
            }

            PrevClothingRemovedElements = clothingRemovedElements;
        }
        else
        {
            ShapeTreeAfterFilter = ShapeTreeAfterFaceParts;
            PrevClothingRemovedElements = new(0);
        }

        // apply to shape
        // (shape and shapetree use same underlying shape element references)
        ShapeTreeAfterFilter.Compile();

        // add additional animations
        // TODO: npc entities are not reloading animations properly...
        // need to figure out why. workaround and insight into bug:
        // making entity have item in hand with animation forces
        // animation to hot reload. so something not triggering
        // anim manager to properly reload animations.
        if (Model.Animations.Count > 0)
        {
            if (Model.AnimationsCompiled != null)
            {
                entityShape.Animations = Model.AnimationsCompiled;
            }
            else
            {
                // compile animations:
                // base shape animations 
                // + custom animation sets from other mods
                var kemo = entity.Api.ModLoader.GetModSystem<KemonoMod>();
                if (kemo != null)
                {
                    List<Animation> compiled = new List<Animation>();

                    // base shape animations
                    compiled.AddRange(entityShape.Animations);

                    // add custom animation sets stored in mod
                    foreach (var animSetCode in Model.Animations)
                    {
                        if (kemo.AvailableAnimations.TryGetValue(animSetCode, out var animSet))
                        {
                            compiled.AddRange(animSet);
                        }
                    }

                    // TODO: de-duplicate animations

                    Model.AnimationsCompiled = compiled.ToArray();
                }

                // if load successful replace entity shape animations
                // with compiled animations
                if (Model.AnimationsCompiled != null)
                {
                    entityShape.Animations = Model.AnimationsCompiled;
                }
            }
        }

        // new model animation initialization
        if (DirtyAnimations)
        {
            DirtyAnimations = false;
            Debug.WriteLine($"[OnTesselation] DirtyAnimations reloading joints and animation cache");

            // store base animations from entity if empty (anims not modified yet)
            if (CachedBaseAnimations.Count == 0)
            {
                foreach (var anim in entity.Properties.Client.Animations)
                {
                    CachedBaseAnimations[anim.Code] = anim.Clone();
                }
            }

            // restore base animations properties
            foreach (var anim in entity.Properties.Client.Animations)
            {
                if (!CachedBaseAnimations.TryGetValue(anim.Code, out var baseAnim)) continue;

                // this copies non-derived properties
                // (we cannot just clone and re-create anim meta data
                // because some internal state is set by anim manager and 
                // gets fucked up when re-creating. there might also be
                // stale references somewhere.)
                anim.Animation = baseAnim.Animation;
                anim.AnimationSound = baseAnim.AnimationSound?.Clone();
                anim.Weight = baseAnim.Weight;
                anim.Attributes = baseAnim.Attributes?.Clone();
                anim.ClientSide = baseAnim.ClientSide;
                anim.ElementWeight = new Dictionary<string, float>(baseAnim.ElementWeight);
                anim.AnimationSpeed = baseAnim.AnimationSpeed;
                anim.MulWithWalkSpeed = baseAnim.MulWithWalkSpeed;
                anim.EaseInSpeed = baseAnim.EaseInSpeed;
                anim.EaseOutSpeed = baseAnim.EaseOutSpeed;
                anim.TriggeredBy = baseAnim.TriggeredBy?.Clone();
                anim.BlendMode = baseAnim.BlendMode;
                anim.ElementBlendMode = new Dictionary<string, EnumAnimationBlendMode>(baseAnim.ElementBlendMode);
                anim.HoldEyePosAfterEasein = baseAnim.HoldEyePosAfterEasein;
                anim.SupressDefaultAnimation = baseAnim.SupressDefaultAnimation;
                anim.WeightCapFactor = baseAnim.WeightCapFactor;
            }

            // apply shape name remappings
            if (Model.AnimationRemappings.Count > 0)
            {
                List<(string, float)> weightRemappings = new();
                List<(string, EnumAnimationBlendMode)> blendRemappings = new();

                foreach (var anim in entity.Properties.Client.Animations)
                {
                    // gather remappings (cannot modify dict in loop)
                    foreach (var weight in anim.ElementWeight)
                    {
                        if (Model.AnimationRemappings.TryGetValue(weight.Key, out var remapped))
                        {
                            weightRemappings.Add((remapped, weight.Value));
                        }
                    }
                    foreach (var blend in anim.ElementBlendMode)
                    {
                        if (Model.AnimationRemappings.TryGetValue(blend.Key, out var remapped))
                        {
                            blendRemappings.Add((remapped, blend.Value));
                        }
                    }

                    // commit remappings
                    foreach (var (remapping, weight) in weightRemappings)
                    {
                        anim.ElementWeight[remapping] = weight;
                    }
                    foreach (var (remapping, blend) in blendRemappings)
                    {
                        anim.ElementBlendMode[remapping] = blend;
                    }

                    weightRemappings.Clear();
                    blendRemappings.Clear();
                }
            }

            // apply entity animation overrides
            foreach (var animOverride in Model.AnimationOverrides)
            {
                if (CachedBaseAnimations.ContainsKey(animOverride.Code) &&
                    entity.Properties.Client.AnimationsByMetaCode.TryGetValue(animOverride.Code, out var anim))
                {
                    animOverride.ApplyTo(anim);
                }
                else
                {
                    // create new animation (if code defined)
                    if (animOverride.Code != null)
                    {
                        var animMeta = animOverride.ToAnimationMeta().Init();
                        entity.Properties.Client.AnimationsByMetaCode[animMeta.Code] = animMeta;
                        entity.Properties.Client.AnimationsByCrc32[animMeta.CodeCrc32] = animMeta;
                    }
                    else
                    {
                        entity.Api.Logger.Error($"[OnTesselation] Animation override missing code: {animOverride.Animation}");
                    }
                }
            }

            // rebuild joints (bones), which re-calculates matrix world
            // transforms for entire model tree 
            entityShape.ResolveReferences(entity.Api.World.Logger, Model.Model.ToString());
            CacheInvTransforms(entityShape.Elements);

            // rebuild animation cache
            // https://github.com/anegostudios/vsapi/blob/master/Common/Model/Animation/AnimationCache.cs
            string animCacheKey = AnimCacheKey();
            Debug.WriteLine($"[OnTesselation] animCacheKey={animCacheKey}");

            entityShape.ResolveAndFindJoints(entity.Api.World.Logger, animCacheKey);

            entity.AnimManager = UpdateAnimations(
                entity.Api,
                entity.AnimManager,
                entity,
                entityShape,
                animCacheKey
            );

            // NOTE: head controller modified in PatchEntityPlayer.cs
            // because EntityPlayer modifes head controller after
            // OnTesselation call, so we have to replace it afterwards 
            // if (entity is EntityPlayer)
            // {
            //     Debug.WriteLine($"Replacing head controller");
            //     string head = "b_Head";
            //     string neck = "b_Neck";
            //     string torsoUpper = "b_TorsoUpper";
            //     string torsoLower = "b_TorsoLower";
            //     string footUpperL = "b_FootUpperL";
            //     string footUpperR = "b_FootUpperR";

            //     entity.AnimManager.HeadController = new KemonoPlayerHeadController(
            //         entity.AnimManager,
            //         entity as EntityPlayer,
            //         entityShape,
            //         head,
            //         neck,
            //         torsoUpper,
            //         torsoLower,
            //         footUpperL,
            //         footUpperR
            //     );
            // }
        }
        else if (DirtyBaseShape)
        {
            // just reload joints for skin parts shapes
            CacheInvTransforms(entityShape.Elements);
            // entityShape.ResolveAndLoadJoints("head"); // deprecated in 1.19
            entityShape.ResolveAndFindJoints(entity.Api.World.Logger, AnimCacheKey());
        }

        // clear dirty shape flag
        DirtyBaseShape = false;

        // render textures to atlas
        RenderSkin();

        /// debug
        // Debug.WriteLine($"[OnTesselation] NEW shape joints:");
        // DebugPrintJoints(entityShape);
        // Debug.WriteLine($"[OnTesselation] NEW shape attachment points:");
        // DebugPrintAttachmentPoints(entityShape);
        // Debug.WriteLine($"[OnTesselation] NEW shape textures count {entityShape.Textures.Count}");
        // DebugPrintTextures(entityShape);

        //// PERFORMANCE DEBUGGING
        // stopwatch.Stop();
        // var dtTotal = stopwatch.Elapsed;
        // Console.WriteLine($"[OnTesselation] {entity.Api.Side} Total: {dtTotal.Microseconds} us");
    }

    public void DebugPrintJoints(Shape shape)
    {
        foreach (var joint in shape.JointsById)
        {
            Console.WriteLine($"- {joint.Key}: {joint.Value.JointId} {joint.Value.Element.Name} {joint.Value.Element.inverseModelTransform}");
        }
    }

    public void DebugPrintTextures(Shape shape)
    {
        foreach (var val in shape.Textures)
        {
            Console.WriteLine($"- {val.Key} {val.Value}");
        }
    }

    public void DebugPrintAttachmentPoints(Shape shape)
    {
        // recurse shape elements and print attachment points
        void PrintShapeElementAttachmentPoints(ShapeElement elem)
        {
            foreach (var attachpoint in elem.AttachmentPoints)
            {
                Console.WriteLine($"- {attachpoint.Code}: {attachpoint} {elem.Name}");
            }

            if (elem.Children != null)
            {
                foreach (var child in elem.Children)
                {
                    PrintShapeElementAttachmentPoints(child);
                }
            }
        }

        foreach (var elem in shape.Elements)
        {
            PrintShapeElementAttachmentPoints(elem);
        }
    }

    public void DebugPrintTextureKeys()
    {
        var textures = entity.Properties.Client.Textures;

        Console.WriteLine("entity.Properties.Client.Textures:");
        foreach (var tex in textures)
        {
            Console.WriteLine($"- textures[{tex.Key}] = {tex.Value}");
        }
    }

    public void CacheInvTransforms(ShapeElement[] elements)
    {
        if (elements == null) return;
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i].CacheInverseTransformMatrix();
            CacheInvTransforms(elements[i].Children);
        }
    }

    /// <summary>
    /// Return true if any of the altClothingRequirement slots are true,
    /// indicating skin part should use alternate clothing model.
    /// </summary>
    /// <param name="inv"></param>
    /// <param name="altClothingRequirement"></param>
    /// <returns></returns>
    public bool UseAltClothingModel(InventoryBase inv, EnumCharacterDressType[] altClothingRequirement)
    {
        foreach (var slot in altClothingRequirement)
        {
            int slotid = (int)slot;
            ItemStack stack = inv[slotid]?.Itemstack;
            if (stack != null && stack.Item.FirstTexture != null) // check item texture is valid
            {
                return true;
            }
        }

        return false;
    }

    public void AddSkinPart(
        KemonoShapeTree shapeTree,
        EntityBehaviorTexturedClothing ebhtc,
        KemonoSkinnablePart part,
        AppliedKemonoSkinnablePartVariant variant,
        Shape entityShape,
        string shapePathForLogging,
        string[] stepParentFilter = null // if not null, only attach to these
    ) {
        var krender = entity.Properties.Client.Renderer as IKemonoRenderer;

        // alternate model when character is clothed or armored
        // ordering is
        //   1. altArmored
        //   2. altClothed
        //   3. default
        bool useAltClothed = false;
        bool useAltArmored = false;
        var inv = ebhtc.Inventory;
        if (inv != null && !ebhtc.hideClothing)
        {
            useAltClothed = UseAltClothingModel(inv, part.AltClothedRequirement);
            useAltArmored = UseAltClothingModel(inv, part.AltArmoredRequirement);
        }

        ICoreAPI api = entity.World.Api;
        AssetLocation template = Model.SkinPartsByCode[variant.PartCode].ShapeTemplate;
        AssetLocation shapePath;

        if (variant.Shape == null && template != null)
        {
            shapePath = template.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            if (useAltArmored && variant.AltArmored != null)
            {
                shapePath.Path = shapePath.Path.Replace("{code}", variant.AltArmored);
            }
            else if (useAltClothed && variant.AltClothed != null)
            {
                shapePath.Path = shapePath.Path.Replace("{code}", variant.AltClothed);
            }
            else
            {
                shapePath.Path = shapePath.Path.Replace("{code}", variant.Code);
            }
        }
        else
        {
            if (useAltArmored && variant.ShapeArmored != null)
            {
                shapePath = variant.ShapeArmored.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            }
            else if (useAltClothed && variant.ShapeClothed != null)
            {
                shapePath = variant.ShapeClothed.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            }
            else
            {
                shapePath = variant.Shape.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            }
        }


        Shape partShape = Shape.TryGet(api, shapePath);
        if (partShape == null)
        {
            api.World.Logger.Warning("Entity skin shape {0} defined in entity config {1} not found or errored, was supposed to be at {2}. Skin part will be invisible.", shapePath, entity.Properties.Code, shapePath);
            return;
        }

        // recursive processing on each shape element faces
        void ProcessShapeElementFaces(ShapeElement elem, int glow)
        {
            // apply face glow to element tree
            foreach (var face in elem.FacesResolved)
            {
                if (face != null)
                {
                    // face.WindMode = new sbyte[] { 0, 0, 0, 0 }; // doesnt do anything
                    // face.WindData = new sbyte[] { 0, 0, 0, 0 }; // doesnt do anything
                    face.Glow = glow;
                }
            }

            // walk children
            if (elem.Children != null)
            {
                foreach (var child in elem.Children)
                {
                    ProcessShapeElementFaces(child, glow);
                }
            }
        }

        // if filter used, remove existing elements from parts
        if (stepParentFilter != null)
        {
            foreach (var elemCode in stepParentFilter)
            {
                shapeTree.ClearBaseElementChildren(elemCode);
            }
        }

        bool added = false;
        foreach (var elem in partShape.Elements)
        {
            if (stepParentFilter != null && stepParentFilter.Length > 0 && !stepParentFilter.Contains(elem.StepParentName)) continue;

            if (shapeTree.AddSkinPartShape(entity, elem, api.World.Logger, shapePath))
            {
                added = true;
            }

            // recursively apply face glow to element tree
            // (or other face processing in future)
            if (variant.Glow > 0)
            {
                ProcessShapeElementFaces(elem, variant.Glow);
            }
        }

        Debug.WriteLine($"[AddSkinPart] added skin part {variant.PartCode} {variant.Code} {added} {partShape.Textures}");

        // only load textures for clients
        if (api.Side == EnumAppSide.Client && added && partShape.Textures != null)
        {
            foreach (var tex in partShape.Textures)
            {
                Debug.WriteLine($"[AddSkinPart] loading texture for skin part {variant.PartCode} {tex.Key} {tex.Value}");

                // for global texture targets defined in model, must
                // pre-allocate texture target so that texture id can
                // be assigned to shape. only allocates if the texture
                // key is the same as part texture target. (I FORGET WHY LOL)
                // this allows parts to reference texture targets defined in other
                // parts (e.g. hairextra part using "hair" texture)
                // without allocating here.
                if (Array.FindIndex(Model.TextureTargets, x => x.Code == tex.Key) != -1)
                {
                    if (tex.Key == part.TextureTarget)
                    {
                        krender.GetOrAllocateTextureTarget(
                            part.TextureTarget,
                            part.TextureTargetWidth,
                            part.TextureTargetHeight
                        );
                    }
                }
                else // need to load custom texture specific to model
                {
                    int[] partTextureSizes = partShape.TextureSizes.Get(tex.Key);
                    int partTextureWidth = partTextureSizes != null ? partTextureSizes[0] : partShape.TextureWidth;
                    int partTextureHeight = partTextureSizes != null ? partTextureSizes[1] : partShape.TextureHeight;

                    LoadTexture(
                        entityShape,
                        tex.Key,
                        tex.Value,
                        partTextureWidth,
                        partTextureHeight,
                        shapePathForLogging
                    );
                }
            }

            foreach (var val in partShape.TextureSizes)
            {
                entityShape.TextureSizes[val.Key] = val.Value;
            }
        }
    }

    /// <summary>
    /// Used for loading extra textures for skin parts, which do not
    /// use the part's main texture target.
    /// </summary>
    /// <param name="entityShape"></param>
    /// <param name="code"></param>
    /// <param name="shapeTexloc"></param>
    /// <param name="textureWidth"></param>
    /// <param name="textureHeight"></param>
    /// <param name="shapePathForLogging"></param>
    public void LoadTexture(
        Shape entityShape,
        string code,
        AssetLocation shapeTexloc,
        int textureWidth,
        int textureHeight,
        string shapePathForLogging
    )
    {
        var textures = entity.Properties.Client.Textures;
        ICoreClientAPI capi = entity.World.Api as ICoreClientAPI;

        var cmpt = textures[code] = new CompositeTexture(shapeTexloc);
        cmpt.Bake(capi.Assets);

        int textureSubId = 0;

        IAsset texAsset = capi.Assets.TryGet(shapeTexloc.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        if (texAsset != null)
        {
            BitmapRef bmp = texAsset.ToBitmap(capi);
            capi.EntityTextureAtlas.GetOrInsertTexture(
                cmpt.Baked.TextureFilenames[0],
                out textureSubId,
                out _,
                () => { return bmp; },
                -1.0f
            );
        }
        else
        {
            capi.World.Logger.Warning($"[BehaviorKemonoSkinnable.loadTexture] Skin part shape {shapePathForLogging} defined texture {shapeTexloc}, no such texture found.");
        }

        cmpt.Baked.TextureSubId = textureSubId;

        entityShape.TextureSizes[code] = new int[] { textureWidth, textureHeight };
        textures[code] = cmpt;
    }

    /// Returns animation cache key for this entity and selected model.
    public string AnimCacheKey()
    {
        return entity.Code + "-" + Model.Model.ToString();
    }

    /// Clear animation cache for cache key. Must clear when hot-reloading
    /// shape animations.
    public void ClearAnimationCache(string animCacheKey)
    {
        object animCacheObj;
        entity.Api.ObjectCache.TryGetValue("animCache", out animCacheObj);

        Dictionary<string, AnimCacheEntry> animCache;
        animCache = animCacheObj as Dictionary<string, AnimCacheEntry>;
        if (animCache == null)
        {
            return; // nothing to clear
        }

        if (animCache.ContainsKey(animCacheKey))
        {
            animCache.Remove(animCacheKey);
        }
    }

    /// Copy of animation cache initialization, but allows inputting anim cache key.
    /// https://github.com/anegostudios/vsapi/blob/master/Common/Model/Animation/AnimationCache.cs#L48
    public static IAnimationManager UpdateAnimations(
        ICoreAPI api,
        IAnimationManager manager,
        Entity entity,
        Shape entityShape,
        string animCacheKey
    ) {
        object animCacheObj;
        Dictionary<string, AnimCacheEntry> animCache;
        entity.Api.ObjectCache.TryGetValue("animCache", out animCacheObj);
        animCache = animCacheObj as Dictionary<string, AnimCacheEntry>;
        if (animCache == null)
        {
            entity.Api.ObjectCache["animCache"] = animCache = new Dictionary<string, AnimCacheEntry>();
        }

        IAnimator animator;

        AnimCacheEntry cacheObj;
        if (animCache.TryGetValue(animCacheKey, out cacheObj))
        {
            //// should not need to do, already done
            // manager.Init(entity.Api, entity);

            animator = api.Side == EnumAppSide.Client ?
                ClientAnimator.CreateForEntity(entity, cacheObj.RootPoses, cacheObj.Animations, cacheObj.RootElems, entityShape.JointsById) :
                ServerAnimator.CreateForEntity(entity, cacheObj.RootPoses, cacheObj.Animations, cacheObj.RootElems, entityShape.JointsById)
            ;

            // save running animations to copy over
            var copyOverAnims = manager.Animator?.Animations.Where(x => x.Active).ToArray();
            manager.Animator = animator;
            manager.CopyOverAnimStates(copyOverAnims, animator);
        }
        else
        {
            //// should not need to do, already done
            // entityShape.ResolveAndLoadJoints(requireJointsForElements);
            // manager.Init(entity.Api, entity);

            IAnimator animatorbase = api.Side == EnumAppSide.Client ?
                ClientAnimator.CreateForEntity(entity, entityShape.Animations, entityShape.Elements, entityShape.JointsById) :
                ServerAnimator.CreateForEntity(entity, entityShape.Animations, entityShape.Elements, entityShape.JointsById)
            ;

            var copyOverAnims = manager.Animator?.Animations.Where(x => x.Active).ToArray();
            manager.Animator = animatorbase;
            manager.CopyOverAnimStates(copyOverAnims, animatorbase);

            animCache[animCacheKey] = new AnimCacheEntry()
            {
                Animations = entityShape.Animations,
                RootElems = (animatorbase as ClientAnimator).RootElements,
                RootPoses = (animatorbase as ClientAnimator).RootPoses
            };
        }

        return manager;
    }

}

#endregion
