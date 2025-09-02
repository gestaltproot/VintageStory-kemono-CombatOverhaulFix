using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ProtoBuf;
using HarmonyLib;
using Vintagestory.API.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.API.Common.Entities;


[assembly: ModInfo("kemono",
    Description = "manmade horrors beyond your comprehension",
    Website     = "",
    Authors     = new []{ "xeth" })]
namespace kemono;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoCharacterSelectedState
{
    public bool DidSelectSkin { get; init; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoSkinSelection
{
    public long EntityId { get; init; }
    public string Model { get; init; }
    public Dictionary<string, double> ModelScale { get; init; }
    public Dictionary<string, string> SkinParts { get; init; }
    public Dictionary<string, int> SkinColors { get; init; }
    public Dictionary<string, int[]> Paintings { get; init; }
    public double EyeHeightOffset { get; init; }
    public string VoiceType { get; init; }
    public string VoicePitch { get; init; }

    public static KemonoSkinSelection FromEntity(Entity entity) {
        var bh = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        string modelCode = bh?.Model?.Code ?? "";
        Dictionary<string, double> modelScale = new Dictionary<string, double>();
        Dictionary<string, string> skinParts = new Dictionary<string, string>();
        Dictionary<string, int> skinColors = new Dictionary<string, int>();
        Dictionary<string, int[]> paintings = new Dictionary<string, int[]>();
        double eyeHeightOffset = bh?.AppliedEyeHeightOffset ?? 0.0;
        string voiceType = bh?.VoiceType ?? "altoflute";
        string voicePitch = bh?.VoicePitch ?? "medium";

        if (bh != null)
        {
            var appliedScale = bh.AppliedScale;
            foreach (var scalePart in bh.Model.ScaleParts)
            {
                double? scale = appliedScale.TryGetDouble(scalePart.Code);
                if (scale != null)
                {
                    modelScale[scalePart.Code] = (double)scale;
                }
            }

            var appliedParts = bh.AppliedAllSkinParts();
            foreach (var applied in appliedParts)
            {
                skinParts[applied.PartCode] = applied.Code;
                skinColors[applied.PartCode] = applied.Color;
            }

            // get paintings
            var skinPaintings = bh.Paintings;
            if (skinPaintings != null)
            {
                foreach (var painting in bh.Model.PaintingTargets)
                {
                    var paintingBytes = skinPaintings.GetBytes(painting.Code);
                    if (paintingBytes != null)
                    {
                        // convert byte[] to int[]
                        int[] pixels = new int[paintingBytes.Length / 4];
                        Buffer.BlockCopy(paintingBytes, 0, pixels, 0, paintingBytes.Length);
                        paintings[painting.Code] = pixels;
                    }
                }
            }
        }

        return new KemonoSkinSelection()
        {
            EntityId = entity.EntityId,
            Model = modelCode,
            ModelScale = modelScale,
            SkinParts = skinParts,
            SkinColors = skinColors,
            Paintings = paintings,
            EyeHeightOffset = eyeHeightOffset,
            VoiceType = voiceType,
            VoicePitch = voicePitch,
        };
    }
}

/// <summary>
/// Sent from client to server when player selects/loads a new character skin.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoClientCharacterSelectionPacket
{
    public bool DidSelectSkin { get; init; }
    public string Race { get; init; }
    public string CharacterClass { get; init; }
    public KemonoSkinSelection Skin { get; init; }
}

/// <summary>
/// Broadcast from server to clients to synchronize character skin selection.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoServerCharacterPacket
{
    public string Race { get; init; }
    public string CharacterClass { get; init; }
    public KemonoSkinSelection Skin { get; init; }
}

/// <summary>
/// Partial update packet to synchronize glow effect on skin parts.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoServerGlowPacket
{
    public long EntityId { get; init; }
    public Dictionary<string, int> AppliedGlow { get; init; }
}

/// <summary>
/// Client request to server to remove glow.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoClientRemoveGlowPacket
{
    public long EntityId { get; init; }
}

/// <summary>
/// Server response to clients to remove glow.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoServerRemoveGlowPacket
{
    public long EntityId { get; init; }
}

/// <summary>
/// Packet from client to server to start an emote. 
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoClientEmoteStartPacket
{
    public string Code { get; init; }
}

/// <summary>
/// Packet from client to server to stop an emote. 
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoClientEmoteStopPacket
{
    public string Code { get; init; }
}

/// <summary>
/// Packet from client to server to stop all emotes.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoClientEmoteStopAllPacket { }

/// <summary>
/// Packet from server to client to start an emote on an entity.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoServerEmoteStartPacket
{
    public long EntityId { get; init; }
    public string Code { get; init; }
}

/// <summary>
/// Packet from server to client to stop an emote on an entity.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoServerEmoteStopPacket
{
    public long EntityId { get; init; }
    public string Code { get; init; }
}

/// <summary>
/// Packet from server to client to stop all emotes on an entity.
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KemonoServerEmoteStopAllPacket
{
    public long EntityId { get; init; }
}

/// <summary>
/// Kemono race trait system, similar to character classes:
/// https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/Character/Trait.cs
/// </summary>
public class KemonoRaceTrait
{
    public string Code;
    public EnumTraitType Type;
    public Dictionary<string, double> Attributes;
}

/// <summary>
/// Race system for kemono character.
/// 
/// Traits system similar to classes:
/// https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/Character/Trait.cs
/// 
/// But added complexity for race is skinpart requirements to
/// qualify for a race selection, e.g. pegasus must have wings,
/// earth pony cannot have wings. Race system supports boolean logic
/// to specify required skinpart set.
/// </summary>
public class KemonoRace
{
    public string Code;
    // optional icon visual
    public AssetLocation Icon;
    // list of KemonoRaceTrait code references given to character
    public string[] Traits;
    // player group privilege code required to choose race
    public string Privilege;
    // name of model required for race, null if any allowed
    public string Model;
    // map of skinpart type => set of required part names
    public Dictionary<string, HashSet<string>> RequiredParts;
    // map of skinpart type => set of banned part names
    public Dictionary<string, HashSet<string>> BannedParts;

    public static AssetLocation DefaultIcon = new AssetLocation("kemono:icon/icon-race-unknown.png");

    public static bool IsValid(KemonoRace race, string model, IReadOnlyDictionary<string, string> parts, IPlayer player = null)
    {
        // check if player privilege satisfied
        // TODO: how to use privileges for modifying npcs?
        if (race.Privilege != null)
        {
            if (player == null) // no player, assume invalid for now, in future need perms system for npcs
            {
                return false;
            }
            else if (!player.HasPrivilege(Vintagestory.API.Server.Privilege.controlserver) && !player.HasPrivilege(race.Privilege))
            {
                return false;
            }
        }

        // check if model satisfied
        if (race.Model != null && race.Model != model) return false;

        // check if skin parts satisfied
        if (parts != null)
        {
            foreach (var condition in race.RequiredParts)
            {
                string partCode = condition.Key;
                HashSet<string> required = condition.Value;
                parts.TryGetValue(partCode, out string applied);
                if (applied == null || !required.Contains(applied))
                {
                    return false;
                }
            }

            foreach (var condition in race.BannedParts)
            {
                string partCode = condition.Key;
                HashSet<string> banned = condition.Value;
                parts.TryGetValue(partCode, out string applied);
                if (applied != null && banned.Contains(applied))
                {
                    return false;
                }
            }
        }
        else
        {
            // no skin selected (should not normally happen)
            // only invalid race if any required part criteria exist
            return race.RequiredParts.Count == 0;
        }

        return true;
    }
}

public class KemonoMod : ModSystem
{
    public static string PATH { get; } = "kemono";

    // server and client
    ICoreAPI api;
    internal Harmony Harmony { get; private set; }

    // reference to original vs character system (manages classes, traits)
    public CharacterSystem baseCharSys;

    // server
    ICoreServerAPI sapi;
    IServerNetworkChannel serverToClientChannel;

    // client
    ICoreClientAPI capi;
    IClientNetworkChannel clientToServerChannel;

    // gui dialog for kemono select character skin editor menu
    public GuiDialogCreateCharacterKemono guiCharEditor;

    // gui dialog for kemono entity emote menu
    public GuiDialogKemonoEmote guiEmote;

    // path to save character and painting image configs
    public string SaveDir { get; private set; }

    // default model for new characters
    public string DefaultModelCode = "kemono0";

    // default race code
    public string DefaultRaceCode = "unknown";

    // dict of available animation sets by code
    public Dictionary<string, Animation[]> AvailableAnimations { get; private set; }
        = new Dictionary<string, Animation[]>();

    // dict of available models by code
    public Dictionary<string, KemonoSkinnableModel> AvailableModels { get; private set; }
        = new Dictionary<string, KemonoSkinnableModel>();

    // flat list of available models, same objects as in AvailableModels
    public KemonoSkinnableModel[] AvailableModelList { get; private set; }
        = new KemonoSkinnableModel[0];

    // flat list of available model codes
    public string[] AvailableModelCodes { get; private set; }
        = new string[0];

    // list of available skin presets by code (must be unique)
    public Dictionary<string, KemonoSkinnableModelPreset> SkinPresetsByCode { get; private set; }
        = new Dictionary<string, KemonoSkinnableModelPreset>();

    // flat list of available skin presets, same objects as in SkinPresetsByCode
    public KemonoSkinnableModelPreset[] SkinPresetsList { get; private set; }
        = new KemonoSkinnableModelPreset[0];

    // flat list of available skin preset code names
    public string[] SkinPresetCodes { get; private set; }
        = new string[0];

    // default model preset configs for each model code,
    // used by dependent code mods to set default model presets
    // TODO: make this json configurable
    public Dictionary<string, KemonoSkinnableModelPreset> DefaultModelPresets { get; private set; }
        = new Dictionary<string, KemonoSkinnableModelPreset>();

    // flat list of available races
    public List<KemonoRace> Races { get; private set; }
        = new List<KemonoRace>();

    // dict of available races by code
    public Dictionary<string, KemonoRace> RacesByCode { get; private set; }
        = new Dictionary<string, KemonoRace>();

    // race trait modifiers by code
    public Dictionary<string, Trait> RaceTraitsByCode = new Dictionary<string, Trait>();

    // store client player state whether player has finished character selection
    bool didSelectSkin = false;

    public override void StartPre(ICoreAPI api)
    {
        // create custom asset categories to store mod's models and presets
        // so we can share this data across all entities
        AssetCategory.categories["animations"] = new AssetCategory("animations", true, EnumAppSide.Universal);
        AssetCategory.categories["models"] = new AssetCategory("models", true, EnumAppSide.Universal);
        AssetCategory.categories["modelpresets"] = new AssetCategory("modelpresets", true, EnumAppSide.Universal);
        AssetCategory.categories["race"] = new AssetCategory("race", true, EnumAppSide.Universal);

        // harmony patching
        // Harmony.DEBUG = true;
        Harmony ??= new Harmony("kemono");
        Harmony.PatchAll();
    }

    public override void Start(ICoreAPI api)
    {
        this.api = api;

        // reference to base character system
        baseCharSys = api.ModLoader.GetModSystem<CharacterSystem>();

        // entity behavior for kemono skinnable entities
        api.RegisterEntityBehaviorClass("kemonoskinnable", typeof(EntityBehaviorKemonoSkinnable));

        // fake entity for attaching clothing so clothing displays in gui
        api.RegisterEntity("EntityKemonoClothingAttach", typeof(EntityKemonoClothingAttach));

        // register network channel
        INetworkChannel networkChannel;
        if (api.Side == EnumAppSide.Client)
        {
            clientToServerChannel = (api as ICoreClientAPI).Network.RegisterChannel("kemono");
            networkChannel = clientToServerChannel;
        }
        else if (api.Side == EnumAppSide.Server)
        {
            serverToClientChannel = (api as ICoreServerAPI).Network.RegisterChannel("kemono");
            networkChannel = serverToClientChannel;
        }
        else
        {
            throw new InvalidOperationException("Kemono must be run on client or server side.");
        }

        // register packets (must match on server and client)
        networkChannel
            .RegisterMessageType<KemonoClientCharacterSelectionPacket>()
            .RegisterMessageType<KemonoClientEmoteStartPacket>()
            .RegisterMessageType<KemonoClientEmoteStopPacket>()
            .RegisterMessageType<KemonoClientEmoteStopAllPacket>()
            .RegisterMessageType<KemonoClientRemoveGlowPacket>()
            .RegisterMessageType<KemonoServerCharacterPacket>()
            .RegisterMessageType<KemonoServerEmoteStartPacket>()
            .RegisterMessageType<KemonoServerEmoteStopPacket>()
            .RegisterMessageType<KemonoServerEmoteStopAllPacket>()
            .RegisterMessageType<KemonoServerGlowPacket>()
            .RegisterMessageType<KemonoServerRemoveGlowPacket>()
            .RegisterMessageType<KemonoCharacterSelectedState>()
        ;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        api.RegisterEntityRendererClass("KemonoShape", typeof(EntityKemonoShapeRenderer));
        api.RegisterEntityRendererClass("KemonoPlayerShape", typeof(EntityKemonoPlayerShapeRenderer));

        clientToServerChannel
            .SetMessageHandler<KemonoCharacterSelectedState>(ClientOnSelectedState)
            .SetMessageHandler<KemonoServerCharacterPacket>(ClientOnCharacterPacket)
            .SetMessageHandler<KemonoServerGlowPacket>(ClientOnGlowPacket)
            .SetMessageHandler<KemonoServerRemoveGlowPacket>(ClientOnRemoveGlowPacket)
            .SetMessageHandler<KemonoServerEmoteStartPacket>(ClientOnEmoteStartPacket)
            .SetMessageHandler<KemonoServerEmoteStopPacket>(ClientOnEmoteStopPacket)
            .SetMessageHandler<KemonoServerEmoteStopAllPacket>(ClientOnEmoteStopAllPacket)
        ;

        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands
            .Create("kemonocharsel") // extra alias for charsel
            .RequiresPrivilege("")
            .HandleWith(OnCharSelCmd)
        ;

        api.ChatCommands
            .Create("skinsave")
            .RequiresPrivilege("")
            .WithArgs(parsers.Word("filename"))
            .HandleWith(OnCmdSkinSave)
        ;

        api.ChatCommands
            .Create("skinload")
            .RequiresPrivilege("")
            .WithArgs(parsers.Word("filename"))
            .HandleWith(OnCmdSkinLoad)
        ;

        api.ChatCommands
            .Create("skinreload")
            .RequiresPrivilege("")
            .WithArgs(parsers.OptionalWord("reloadtype0"), parsers.OptionalWord("reloadtype1"), parsers.OptionalWord("reloadtype2"))
            .HandleWith(OnCmdSkinReload)
        ;

        api.ChatCommands
            .Create("paintingsave")
            .RequiresPrivilege("")
            .WithArgs(parsers.Word("filename"), parsers.OptionalWord("paintingName"))
            .HandleWith(OnCmdPaintingSave)
        ;

        api.ChatCommands
            .Create("paintingload")
            .RequiresPrivilege("")
            .WithArgs(parsers.Word("filename"), parsers.OptionalWord("paintingName"))
            .HandleWith(OnCmdPaintingLoad)
        ;

        api.ChatCommands
            .Create("paintingtexload")
            .RequiresPrivilege("")
            .WithArgs(parsers.Word("filename"), parsers.OptionalWord("paintingName"))
            .HandleWith(OnCmdPaintingTexLoad)
        ;

        api.ChatCommands
            .Create("glowreset")
            .RequiresPrivilege("")
            .HandleWith(OnCmdGlowReset)
        ;

        api.ChatCommands
            .Create("emote")
            .RequiresPrivilege("")
            .HandleWith(OnCmdEmote)
        ;

        // emote menu hotkey
        capi.Input.RegisterHotKey("kemonoemote", Lang.Get("kemono:hotkey-emote"), GlKeys.T, HotkeyType.CharacterControls, ctrlPressed: true);
        capi.Input.SetHotKeyHandler("kemonoemote", OnHotkeyEmote);

        api.Event.IsPlayerReady += EventIsPlayerReadyClient; // idk why but we need this
        api.Event.PlayerJoin += EventPlayerJoinClient;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;

        serverToClientChannel
            .SetMessageHandler<KemonoClientCharacterSelectionPacket>(ServerOnCharacterSelection)
            .SetMessageHandler<KemonoClientRemoveGlowPacket>(ServerOnRemoveGlow)
            .SetMessageHandler<KemonoClientEmoteStartPacket>(ServerOnEmoteStartPacket)
            .SetMessageHandler<KemonoClientEmoteStopPacket>(ServerOnEmoteStopPacket)
            .SetMessageHandler<KemonoClientEmoteStopAllPacket>(ServerOnEmoteStopAllPacket)
        ;

        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands
            .Create("racechangeonce")
            .RequiresPrivilege("commandplayer")
            .WithArgs(parsers.OnlinePlayer("playername"))
            .HandleWith(OnRaceChangeOnceCmd)
        ;

        api.ChatCommands
            .Create("serverskinreload")
            .RequiresPrivilege("commandplayer")
            .HandleWith(OnCmdServerSkinReload)
        ;

        api.Event.PlayerJoin += EventPlayerJoinServer;
    }

    /// <summary>
    /// When mod is disposed, cleanup assets.
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();

        // unpatch harmony
        if (Harmony != null)
        {
            Harmony.UnpatchAll(Harmony.Id);
            Harmony = null;
        }

        // free shared loaded textures
        ClearTextureUtil.FreeAllClearTextures();
    }

    /// <summary>
    /// Load animations assets. These are re-usable animations that can
    /// be attached to different models. Allow mod authors to easily
    /// add new animations without needing to modify existing models,
    /// while supporting runtime hot reloading.
    /// </summary>
    /// <param name="api"></param>
    public void ReloadAnimations(ICoreAPI api, bool reloadAssets = true)
    {
        if (reloadAssets)
        {
            api.Assets.Reload(AssetCategory.categories["animations"]);
        }

        // load animations
        Dictionary<string, Animation[]> animationsByCode = new Dictionary<string, Animation[]>();
        List<AssetLocation> assetPaths = api.Assets.GetLocations("animations/");
        foreach (var assetPath in assetPaths)
        {
            var animsAsset = api.Assets.TryGet(assetPath);
            var animsLoaded = animsAsset.ToObject<KemonoAnimationSet>();

            // if code null, use asset name as code
            if (string.IsNullOrEmpty(animsLoaded.Code))
            {
                animsLoaded.Code = assetPath.GetName().RemoveFileEnding();
            }

            if (animationsByCode.ContainsKey(animsLoaded.Code))
            {
                api.Logger.Warning($"[kemono] {assetPath} Overwriting model {animsLoaded.Code}");
            }
            animationsByCode[animsLoaded.Code] = animsLoaded.Animations;
            api.Logger.Notification($"[kemono] Loaded animations: {animsLoaded.Code}");
        }

        AvailableAnimations = animationsByCode;
    }

    /// <summary>
    /// Load model and skin presets. These are stored in the mod's assets
    /// folders in custom directories "models" and "modelpresets".
    /// </summary>
    /// <param name="api"></param>
    public void ReloadModels(ICoreAPI api, bool reloadAssets = true)
    {
        if (reloadAssets)
        {
            api.Assets.Reload(AssetCategory.categories["models"]);
        }

        // load model assets into real models and addons
        // note: loading models by code first so models override
        // each other to prevent duplicate model codes
        Dictionary<string, KemonoSkinnableModel> modelsToInit = new Dictionary<string, KemonoSkinnableModel>();
        List<KemonoSkinnableModel> addonModels = new List<KemonoSkinnableModel>();
        List<AssetLocation> modelPaths = api.Assets.GetLocations("models/");
        foreach (var modelPath in modelPaths)
        {
            var modelAsset = api.Assets.TryGet(modelPath);
            var modelLoaded = modelAsset.ToObject<KemonoSkinnableModel>();
            if (modelLoaded.Addon != null)
            {
                addonModels.Add(modelLoaded);
            }
            else
            {
                if (modelsToInit.ContainsKey(modelLoaded.Code))
                {
                    api.Logger.Warning($"[kemono] {modelPath} Overwriting model {modelLoaded.Code}");
                }
                modelsToInit[modelLoaded.Code] = modelLoaded;
                api.Logger.Notification($"[kemono] Loaded model: {modelLoaded.Code}");
            }
        }

        // apply addon models
        foreach (var addon in addonModels)
        {
            if (modelsToInit.TryGetValue(addon.Addon, out KemonoSkinnableModel model))
            {
                model.ApplyAddon(addon);
                api.Logger.Notification($"[kemono] Loaded addon: {addon.Addon}");
            }
            else
            {
                api.Logger.Warning($"[kemono] Addon for model not found: {addon.Addon}");
            }
        }

        // finish initializing models
        foreach (var model in modelsToInit)
        {
            AvailableModels[model.Value.Code] = model.Value.Initialize();
        }

        // create final flat list of available models and their names
        AvailableModelList = AvailableModels.Values.ToArray();
        AvailableModelCodes = AvailableModels.Keys.ToArray();

        // load model preset configs
        List<AssetLocation> presetPaths = api.Assets.GetLocations("modelpresets/");
        foreach (var presetPath in presetPaths)
        {
            var presetAsset = api.Assets.TryGet(presetPath);
            // get preset code from filename
            string presetCode = presetPath.Path.Split('/').Last().Split('.').First();
            var presetLoaded = presetAsset.ToObject<KemonoSkinnableModelPreset>();
            SkinPresetsByCode[presetCode] = presetLoaded;
        }

        // create flat lists of available skin presets and their names,
        // but sort codes by 1. model type and then 2. alphabetically by code in each type:
        // first sort presets into their model types
        Dictionary<string, List<string>> presetCodesByModel = new Dictionary<string, List<string>>();
        List<string> presetModelTypes = new List<string>();
        foreach (var preset in SkinPresetsByCode)
        {
            var presetCode = preset.Key;
            var presetVal = preset.Value;
            if (!presetCodesByModel.ContainsKey(presetVal.Model))
            {
                presetCodesByModel[presetVal.Model] = new List<string>();
                presetModelTypes.Add(presetVal.Model);
            }
            presetCodesByModel[presetVal.Model].Add(presetCode);
        }

        // sort preset codes in each model type
        foreach (var modelType in presetModelTypes)
        {
            presetCodesByModel[modelType].Sort();
        }

        // sort model type names
        presetModelTypes.Sort();

        // flatten sorted preset codes into a single list
        List<string> presetCodes = new List<string>();
        List<KemonoSkinnableModelPreset> presetList = new List<KemonoSkinnableModelPreset>();
        foreach (var modelType in presetModelTypes)
        {
            if (!AvailableModels.TryGetValue(modelType, out KemonoSkinnableModel model))
            {
                api.Logger.Warning($"[kemono] Model not found for preset model type: {modelType}");
                continue;
            }
            if (model.Hidden) continue; // skip presets for hidden models

            foreach (var presetCode in presetCodesByModel[modelType])
            {
                presetCodes.Add(presetCode);
                presetList.Add(SkinPresetsByCode[presetCode]);
            }
        }

        SkinPresetsList = presetList.ToArray();
        SkinPresetCodes = presetCodes.ToArray();
    }

    /// <summary>
    /// After assets loaded, load models and skin presets
    /// (treating models like assets, load during AssetsLoaded phase).
    /// </summary>
    /// <param name="api"></param>
    public override void AssetsLoaded(ICoreAPI api)
    {
        base.AssetsLoaded(api);
        Debug.WriteLine($"[kemono] DataPathMods={GamePaths.DataPathMods}");
        Debug.WriteLine($"[kemono] DataPathServerMods={GamePaths.DataPathServerMods}");
        Debug.WriteLine($"[kemono] AssetsPath={GamePaths.AssetsPath}");

        // load animations
        // (custom per mod animations that can be added to different
        // models)
        ReloadAnimations(api, false);

        // load models and model presets
        ReloadModels(api, false);

        // create user local save directory
        // (for user to save character + painting configs)
        SaveDir = GamePaths.DataPathMods + Path.DirectorySeparatorChar + "kemono";
        if (!Directory.Exists(SaveDir))
        {
            api.Logger.Notification($"[Kemono] Creating save directory: {SaveDir}");
            Directory.CreateDirectory(SaveDir);
        }

        // generate dummy modinfo.json to prevent errors
        GenerateSkinModInfo(SaveDir);

        // load races and race traits
        List<AssetLocation> racePaths = api.Assets.GetLocations("race/");
        foreach (var racePath in racePaths)
        {
            // if path ends with trait.json or traits.json, load as trait
            if (racePath.Path.EndsWith("trait.json") || racePath.Path.EndsWith("traits.json"))
            {
                var traits = api.Assets.Get(racePath).ToObject<List<Trait>>();
                foreach (var trait in traits)
                {
                    if (RaceTraitsByCode.ContainsKey(trait.Code))
                    {
                        api.Logger.Notification($"[kemono] {racePath} Overwriting trait {trait.Code}");
                    }
                    RaceTraitsByCode[trait.Code] = trait;
                }
            }
            else // load race object definitions
            {
                var raceAsset = api.Assets.TryGet(racePath);
                var raceLoaded = raceAsset.ToObject<List<KemonoRace>>();
                foreach (var race in raceLoaded)
                {
                    AddRace(race);
                    api.Logger.Notification($"[kemono] Loaded race: {race.Code}");
                }

            }
        }
    }

    /// <summary>
    /// Open's player's character selection dialog.
    /// </summary>
    public void OpenPlayerCharSelectGui()
    {
        if (guiCharEditor == null)
        {
            guiCharEditor = new GuiDialogCreateCharacterKemono(capi, this, capi.World.Player.Entity, capi.World.Player, capi.World.Player.PlayerName, OnEditCharGuiClosed);
            guiCharEditor.PrepAndOpen();
        }

        if (!guiCharEditor.IsOpened())
        {
            guiCharEditor.TryOpen();
        }
    }

    /// <summary>
    /// Wrapper for opening character selection dialog.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public TextCommandResult OnCharSelCmd(TextCommandCallingArgs args)
    {
        OpenPlayerCharSelectGui();

        return TextCommandResult.Success();
    }

    /// <summary>
    /// Runs on client when local player or another player joins game.
    /// </summary>
    /// <param name="byPlayer"></param>
    public void EventPlayerJoinClient(IClientPlayer byPlayer)
    {
        // refresh race traits
        if (byPlayer.Entity != null) // new player entity is null on server?
        {
            ApplyRaceTraits(byPlayer.Entity);
        }

        // local player, decide whether to open character selection dialog
        if (byPlayer.PlayerUID == capi.World.Player.PlayerUID)
        {
            // conditions to open dialog
            // - player has not selected skin yet
            // - player race is default or non-existent and there are more
            //   than 1 race. check is required to make mod robust to player
            //   adding new race mods or race mod codes changing
            string raceCode = byPlayer.Entity?.WatchedAttributes.GetString("characterRace");
            bool shouldSelectCharacter = !didSelectSkin ||
                (Races.Count > 1 && (raceCode == null || raceCode == DefaultRaceCode || (raceCode != null && !RacesByCode.ContainsKey(raceCode))))
            ;

            // note: when debugging gui, disable this check
            if (shouldSelectCharacter)
            {
                guiCharEditor = new GuiDialogCreateCharacterKemono(capi, this, capi.World.Player.Entity, capi.World.Player, capi.World.Player.PlayerName, OnEditCharGuiClosed);
                guiCharEditor.PrepAndOpen();
                guiCharEditor.OnClosed += () => capi.PauseGame(false);
                capi.Event.EnqueueMainThreadTask(() => capi.PauseGame(true), "pausegame");
            }
        }
    }

    /// <summary>
    /// idk why but we need this:
    /// https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/Character/Character.cs#L330
    /// </summary>
    private bool EventIsPlayerReadyClient(ref EnumHandling handling)
    {
        if (didSelectSkin) return true;

        handling = EnumHandling.PreventDefault;
        return false;
    }

    /// <summary>
    /// Generates a skin modinfo.json file in the kemono save directory 
    /// to prevent mod loading errors.
    /// </summary>
    /// <param name="skinmodinfo">The path to the kemono save directory</param>
    private void GenerateSkinModInfo(string path)
    {
        string modinfoPath = Path.Combine(path, "modinfo.json");
        
        // Always check and create/update the modinfo.json file on startup
        if (!File.Exists(modinfoPath))
        {
            api.Logger.Notification($"[Kemono] Creating skin modinfo.json: {modinfoPath}");
        }
        
        // Create the modinfo.json content
        string modinfoContent = """
        {
            "type": "content",
            "name": "kemono",
            "modid": "kemonoskins",
            "version": "0.0.0",

            "description": "Auto-generated folder contains kemono saved skin and painting presets",
            "website": "http://gitlab.com/xeth/kemono",
            "authors": [ "xeth" ],

            "requiredOnClient": false,
            "requiredOnServer": false
        }
        """;
        
        try
        {
            File.WriteAllText(modinfoPath, modinfoContent);
        }
        catch (Exception ex)
        {
            api.Logger.Error($"[Kemono] Failed to create modinfo.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs on server when player joins game. Checks if player has 
    /// already selected skin and sends back to client.
    /// </summary>
    /// <param name="byPlayer"></param>
    public void EventPlayerJoinServer(IServerPlayer byPlayer)
    {
        bool playerDidSelectSkin = SerializerUtil.Deserialize(byPlayer.GetModdata("createCharacter"), false);

        if (byPlayer.Entity != null) // new player entity is null on server?
        {
            // refresh race traits
            ApplyRaceTraits(byPlayer.Entity);

            if (!playerDidSelectSkin)
            {
                baseCharSys.setCharacterClass(byPlayer.Entity, baseCharSys.characterClasses[0].Code, false);
            }
        }

        serverToClientChannel.SendPacket(new KemonoCharacterSelectedState() { DidSelectSkin = playerDidSelectSkin }, byPlayer);
    }

    #region Sync

    /// <summary>
    /// Server handler for applying skin selection to an entity. This is
    /// split from the player character selection handler so that this
    /// can be used for other entities (e.g. npc slaves, enemies, etc.).
    /// This only applies skin and does not handle class and race, which is
    /// specific to player entities and not other entity types.
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="p"></param>
    public void ApplySkin(Entity entity, KemonoSkinSelection skin)
    {
        EntityBehaviorKemonoSkinnable bh = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();

        if (bh == null)
        {
            api.Logger.Error($"[kemono] Behavior EntityBehaviorKemonoSkinnable not found for {entity}!");
            return;
        }

        // set model
        bh.SetModel(skin.Model);

        // voice and pitch
        bh.ApplyVoice(skin.VoiceType, skin.VoicePitch, false);

        // null checks below because packet will null them if empty

        // apply model scale
        bh.AppliedScale.Clear();
        if (skin.ModelScale != null)
        {
            foreach (var scale in skin.ModelScale)
            {
                bh.SetModelPartScale(scale.Key, scale.Value, false);
            }
        }

        // apply eye height offset
        bh.SetEyeHeightOffset(skin.EyeHeightOffset, false);

        // apply skin part variants
        if (skin.SkinParts != null)
        {
            foreach (var skinpart in skin.SkinParts)
            {
                bh.SelectSkinPart(skinpart.Key, skinpart.Value, false);
            }
        }

        // apply skin part colors
        if (skin.SkinColors != null)
        {
            foreach (var skincolor in skin.SkinColors)
            {
                bh.SetSkinPartColor(skincolor.Key, skincolor.Value, false);
            }
        }

        // apply painting texture
        if (skin.Paintings != null)
        {
            foreach (var painting in skin.Paintings)
            {
                bh.SetPaintingPixels(painting.Key, painting.Value);
            }
        }

        entity.MarkShapeModified();
    }

    /// <summary>
    /// Server side handling for player selecting skin + class + race.
    /// Validates if player has selected before and cancels if player has
    /// already selected character. Returns new character selection that
    /// needs to be synchronized to clients. Returns null if selection
    /// is invalid and should not be applied.
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="fromPlayer"></param>
    /// <param name="p"></param>
    /// <returns>KemonoServerCharacterPacket</returns>
    public KemonoServerCharacterPacket ServerTryApplyPlayerCharacter(
        Entity entity,
        IServerPlayer fromPlayer,
        KemonoClientCharacterSelectionPacket p
    )
    {
        // check if race is valid for skin parts, otherwise reject skin change
        bool didSelectBefore = SerializerUtil.Deserialize(fromPlayer.GetModdata("createCharacter"), false);
        bool didSelectRace = SerializerUtil.Deserialize(fromPlayer.GetModdata("didSelectRace"), false);

        if (!RacesByCode.TryGetValue(p.Race, out KemonoRace race))
        {
            fromPlayer.SendIngameError("racedoesnotexist", Lang.Get("kemono:msg-error-race-not-exist"));
            return null;
        }
        if (!KemonoRace.IsValid(race, p.Skin.Model, p.Skin.SkinParts, fromPlayer))
        {
            fromPlayer.SendIngameError("raceskininvalid", Lang.Get("kemono:msg-error-skin-race-invalid"));
            return null;
        }

        // check if race changed, only allow skin change if:
        // - has not selected race
        // - current race is default or null
        // - in creative mode
        if (didSelectRace && fromPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            var currRace = GetEntityRace(entity);
            if (currRace != null && currRace.Code != DefaultRaceCode && p.Race != currRace.Code)
            {
                fromPlayer.SendIngameError("racechangenotallowed", Lang.Get("kemono:msg-error-race-change-not-allowed"));
                return null;
            }
        }

        // do race selection
        fromPlayer.SetModdata("didSelectRace", SerializerUtil.Serialize(true));
        SetEntityRace(entity, race);

        // class selection: allow first time or in creative mode
        // if not allowed, continue with skin change but don't change class
        fromPlayer.SetModdata("createCharacter", SerializerUtil.Serialize(p.DidSelectSkin));
        bool allowClassChange = entity.WatchedAttributes.GetBool("allowcharselonce", false);

        if (!didSelectBefore || allowClassChange || fromPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            if (entity is EntityPlayer entityPlayer)
            {
                bool initializeGear = !didSelectBefore;
                baseCharSys.setCharacterClass(entityPlayer, p.CharacterClass, initializeGear);

                if (allowClassChange)
                {
                    entity.WatchedAttributes.RemoveAttribute("allowcharselonce");
                }
            }
        }
        else
        {
            string currClass = entity.WatchedAttributes.GetString("characterClass");
            if (currClass != p.CharacterClass)
            {
                fromPlayer.SendIngameError("classchangenotallowed", Lang.Get("kemono:msg-error-class-change-not-allowed"));
            }
        }

        // validation passed, apply skin to entity
        ApplySkin(entity, p.Skin);

        // return new character packet to be sent to clients
        return new KemonoServerCharacterPacket()
        {
            Race = entity.WatchedAttributes.GetString("characterRace"),
            CharacterClass = entity.WatchedAttributes.GetString("characterClass"),
            Skin = p.Skin,
        };
    }

    public void ServerOnCharacterSelection(IServerPlayer fromPlayer, KemonoClientCharacterSelectionPacket p)
    {
        var entity = sapi.World.GetEntityById(p.Skin.EntityId);
        if (entity == null)
        {
            api.Logger.Error($"[kemono] ServerOnCharacterSelection entity id {p.Skin.EntityId} not found.");
            return;
        }

        if (p.DidSelectSkin)
        {
            var newCharSelection = ServerTryApplyPlayerCharacter(entity, fromPlayer, p);
            if (newCharSelection != null) // success, send to all clients
            {
                fromPlayer.BroadcastPlayerData(true); // updates player + inventory
                serverToClientChannel.BroadcastPacket(newCharSelection);
                return;
            }
        }

        // failure base case, selection invalid:
        // only send back to source player its original character skin
        fromPlayer.BroadcastPlayerData(true); // updates player + inventory
        serverToClientChannel.SendPacket(new KemonoServerCharacterPacket()
        {
            Race = entity.WatchedAttributes.GetString("characterRace"),
            CharacterClass = entity.WatchedAttributes.GetString("characterClass"),
            Skin = KemonoSkinSelection.FromEntity(entity),
        }, fromPlayer);
    }

    /// <summary>
    /// Client handler for server packet that synchronizes character skin.
    /// This is called for client player's entity, other player entities, and
    /// non-player entities.
    /// </summary>
    /// <param name="p"></param>
    public void ClientOnCharacterPacket(KemonoServerCharacterPacket p)
    {
        var entity = capi.World.GetEntityById(p.Skin.EntityId);
        if (entity == null) return;

        if (p.CharacterClass != null && entity is EntityPlayer entityPlayer)
        {
            baseCharSys.setCharacterClass(entityPlayer, p.CharacterClass, false);
        }

        if (p.Race != null)
        {
            SetEntityRaceFromCode(entity, p.Race, true);
        }

        if (p.Skin != null)
        {
            ApplySkin(entity, p.Skin);
        }
    }

    /// <summary>
    /// Callback that runs after player closes the character selection gui.
    /// Sends character selection to server and marks player as ready.
    /// </summary>
    /// <param name="gui"></param>
    public void OnEditCharGuiClosed(GuiDialogCreateCharacterKemono gui)
    {
        guiCharEditor = null;
        KemonoRace race = Races[gui.SelectedRaceIndex];
        CharacterClass chclass = baseCharSys.characterClasses[gui.SelectedClassIndex];
        ClientSendCharacter(gui.Entity, race.Code, chclass.Code, gui.DidSelect);
        capi.Network.SendPlayerNowReady();
    }

    /// <summary>
    /// Called when client finishes local character selection screen.
    /// 1. Sends client skin config to server.
    /// 2. Server validates skin, race, class
    /// 3. Server accepts or rejects change, then broadcasts updated
    ///    player skin config back to client (and other clients)
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="characterRace"></param>
    /// <param name="characterClass"></param>
    /// <param name="didSelect"></param>
    public void ClientSendCharacter(
        Entity entity,
        string characterRace,
        string characterClass,
        bool didSelect
    )
    {
        var skin = KemonoSkinSelection.FromEntity(entity);

        // client-side validate race is valid before sending
        string chosenRace = DefaultRaceCode;
        if (RacesByCode.TryGetValue(characterRace, out KemonoRace race))
        {
            if (KemonoRace.IsValid(race, skin.Model, skin.SkinParts, capi.World.Player))
            {
                chosenRace = characterRace;
            }
        }

        clientToServerChannel.SendPacket(new KemonoClientCharacterSelectionPacket()
        {
            DidSelectSkin = didSelect,
            Race = chosenRace,
            CharacterClass = characterClass,
            Skin = skin
        });
    }

    /// Client handler for server packet whether player has already 
    /// done character selection. Called when player joins server.
    public void ClientOnSelectedState(KemonoCharacterSelectedState p)
    {
        didSelectSkin = p.DidSelectSkin;
    }

    /// <summary>
    /// Synchronize skin glow effect.
    /// </summary>
    /// <param name="entity"></param>
    public void ServerSyncGlow(EntityBehaviorKemonoSkinnable skin)
    {
        var entity = skin.entity;

        Dictionary<string, int> appliedGlow = new Dictionary<string, int>();
        foreach (var code in skin.AppliedGlow.Keys)
        {
            int? glow = skin.AppliedGlow.TryGetInt(code);
            if (glow.HasValue) appliedGlow[code] = glow.Value;
        }

        serverToClientChannel.BroadcastPacket(new KemonoServerGlowPacket()
        {
            EntityId = entity.EntityId,
            AppliedGlow = appliedGlow,
        });
    }

    /// <summary>
    /// Client handler to sync glow on skin parts.
    /// </summary>
    /// <param name="p"></param>
    public void ClientOnGlowPacket(KemonoServerGlowPacket p)
    {
        var entity = capi.World.GetEntityById(p.EntityId);
        if (entity == null) return;

        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null) return;

        skin.AppliedGlow.Clear();
        foreach (var kvp in p.AppliedGlow)
        {
            skin.AppliedGlow.SetInt(kvp.Key, kvp.Value);
        }
        skin.DirtyBaseShape = true;
        entity.MarkShapeModified();
    }

    /// <summary>
    /// Client handler to remove glow on skin parts.
    /// </summary>
    /// <param name="p"></param>
    public void ClientOnRemoveGlowPacket(KemonoServerRemoveGlowPacket p)
    {
        var entity = capi.World.GetEntityById(p.EntityId);
        if (entity == null) return;

        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null) return;

        skin.RemoveGlow();
        skin.DirtyBaseShape = true;
        entity.MarkShapeModified();
    }

    /// <summary>
    /// Server handler for client command to remove glow.
    /// </summary>
    /// <param name="fromPlayer"></param>
    /// <param name="p"></param>
    public void ServerOnRemoveGlow(IServerPlayer fromPlayer, KemonoClientRemoveGlowPacket p)
    {
        var entity = sapi.World.GetEntityById(p.EntityId);
        if (entity == null)
        {
            api.Logger.Error($"[kemono] ServerOnRemoveGlow entity id {p.EntityId} not found.");
            return;
        }

        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null) return;
        skin.RemoveGlow();

        serverToClientChannel.BroadcastPacket(new KemonoServerRemoveGlowPacket()
        {
            EntityId = entity.EntityId,
        });
    }

    #endregion


    #region Race

    public static void AllowRaceChange(IServerPlayer player)
    {
        player.SetModdata("didSelectRace", SerializerUtil.Serialize(false));
    }

    /// <summary>
    /// Add race to available races. If race by code already exists, this
    /// will overwrite that race, but keep the ordering in the Races list.
    /// This is because the race ordering may matter, but we still need
    /// different mods to be able to override races based on load order
    /// so mods can customize other race mods.
    /// </summary>
    /// <param name="race"></param>
    public void AddRace(KemonoRace race)
    {
        if (RacesByCode.ContainsKey(race.Code))
        {
            RacesByCode[race.Code] = race;
            for (int i = 0; i < Races.Count; i++)
            {
                if (Races[i].Code == race.Code)
                {
                    Races[i] = race;
                    break;
                }
            }
        }
        else
        {
            // if race code is default, make sure it is at index 0
            if (race.Code == DefaultRaceCode)
            {
                Races.Insert(0, race);
            }
            else
            {
                Races.Add(race);
            }
            RacesByCode[race.Code] = race;
        }
    }

    /// <summary>
    /// Remove race with given code name string if it exists. Code is case
    /// sensitive. Returns true if race removed exists.
    /// </summary>
    /// <param name="code"></param>
    public bool RemoveRace(string code)
    {
        // find race by code in Races list and remove
        // then update races by filtering existing races list by code
        // (this preserves race list ordering)
        if (RacesByCode.Remove(code))
        {
            for (int i = 0; i < Races.Count; i++)
            {
                if (Races[i].Code == code)
                {
                    Races.RemoveAt(i);
                    break;
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Get race for entity, return null if race not found.
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    public KemonoRace GetEntityRace(Entity entity)
    {
        string raceCode = entity.WatchedAttributes.GetString("characterRace");
        if (raceCode != null && RacesByCode.TryGetValue(raceCode, out KemonoRace race))
        {
            return race;
        }
        return null;
    }

    /// <summary>
    /// Apply entity race selection by code. Throws error if race does
    /// not exist. If race does not exist, sets the 
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="raceCode"></param>
    public void SetEntityRaceFromCode(Entity entity, string raceCode, bool applyTraits = true)
    {
        if (RacesByCode.TryGetValue(raceCode, out KemonoRace race))
        {
            SetEntityRace(entity, race, applyTraits);
        }
        else
        {
            // set race to default built-in race "unknown"
            api.Logger.Error($"[kemono] Invalid race during SetEntityRace: {raceCode}");
            if (RacesByCode.TryGetValue(DefaultRaceCode, out race))
            {
                SetEntityRace(entity, race, applyTraits);
            }
        }
    }

    /// <summary>
    /// Apply race to entity. 
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="race"></param>
    public void SetEntityRace(Entity entity, KemonoRace race, bool applyTraits = true)
    {
        entity.WatchedAttributes.SetString("characterRace", race.Code);
        if (applyTraits) ApplyRaceTraits(entity);
    }


    /// <summary>
    /// Apply race trait attributes, based on vanilla character class:
    /// https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/Character/Character.cs#L257
    /// </summary>
    /// <param name="entity"></param>
    /// <exception cref="ArgumentException"></exception>
    public void ApplyRaceTraits(Entity entity)
    {
        // reset race traits 
        foreach (var stats in entity.Stats)
        {
            foreach (var statmod in stats.Value.ValuesByKey)
            {
                if (statmod.Key == "race")
                {
                    stats.Value.Remove(statmod.Key);
                    break;
                }
            }
        }

        string raceCode = entity.WatchedAttributes.GetString("characterRace", DefaultRaceCode);
        if (!RacesByCode.TryGetValue(raceCode, out KemonoRace race))
        {
            // log error invalid race and return
            api.Logger.Error($"[kemono] Invalid race during ApplyRaceTraits: {raceCode}");
            return;
        }

        // apply traits
        string[] extraTraits = entity.WatchedAttributes.GetStringArray("extraTraits");
        IEnumerable<string> allTraits;
        if (race.Traits != null && extraTraits != null)
        {
            allTraits = race.Traits.Concat(extraTraits);
        }
        else if (race.Traits != null)
        {
            allTraits = race.Traits;
        }
        else if (extraTraits != null)
        {
            allTraits = extraTraits;
        }
        else // both are null
        {
            return;
        }

        foreach (var traitcode in allTraits)
        {
            if (RaceTraitsByCode.TryGetValue(traitcode, out Trait trait))
            {
                foreach (var val in trait.Attributes)
                {
                    string attrcode = val.Key;
                    double attrvalue = val.Value;

                    entity.Stats.Set(attrcode, "race", (float)attrvalue, true);
                }
            }
        }

        entity.GetBehavior<EntityBehaviorHealth>()?.MarkDirty();
    }

    public bool HasRaceTrait(Entity entity, string trait)
    {
        string raceCode = entity.WatchedAttributes.GetString("characterRace");
        if (raceCode != null && RacesByCode.TryGetValue(raceCode, out KemonoRace race))
        {
            if (race.Traits != null) return race.Traits.Contains(trait);
        }
        return false;
    }

    public bool HasAnyRaceTrait(Entity entity, params string[] traits)
    {
        string raceCode = entity.WatchedAttributes.GetString("characterRace");
        if (raceCode != null && RacesByCode.TryGetValue(raceCode, out KemonoRace race))
        {
            if (race.Traits != null)
            {
                foreach (var trait in traits)
                {
                    if (race.Traits.Contains(trait)) return true;
                }
            }
        }
        return false;
    }

    /// Server command to allow an online player to change their race.
    public TextCommandResult OnRaceChangeOnceCmd(TextCommandCallingArgs args)
    {
        IServerPlayer player = args.Parsers[0].GetValue() as IServerPlayer;
        if (player != null)
        {
            AllowRaceChange(player);
            return TextCommandResult.Success(Lang.Get("kemono:msg-cmd-racechange-success", player.PlayerName));
        }
        else
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-racechange-error-no-player", player.PlayerName));
        }
    }

    #endregion
    

    #region Save/load

    /// Common helper to save client player's skin config to local
    /// json file. Return true if successfully saved.
    public bool SkinSave(Entity entity, string filename)
    {
        if (filename == "")
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-skin-save-error-no-file"));
            return false;
        }

        // make sure filename ends in json
        string filenameJson = filename;
        if (!filenameJson.EndsWith(".json"))
        {
            filenameJson = filenameJson + ".json";
        }

        string savePath = SaveDir + Path.DirectorySeparatorChar + filenameJson;
        capi.ShowChatMessage(Lang.Get("kemono:msg-skin-save-file", filenameJson));

        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        var preset = skin.SavePreset();

        // serialize to json
        using (StreamWriter file = File.CreateText(savePath))
        {
            // make serializer with camelcase and 2 space indents
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented
            };
            JsonSerializer serializer = JsonSerializer.Create(settings);
            serializer.Serialize(file, preset);
        }

        return true;
    }

    /// Common helper to load client player's skin config from
    /// local json files.
    public bool SkinLoad(Entity entity, string filename)
    {
        if (filename == "")
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-skin-load-error-no-file"));
            return false;
        }

        // make sure filename ends in json
        string filenameJson = filename;
        if (!filenameJson.EndsWith(".json"))
        {
            filenameJson = filenameJson + ".json";
        }

        string loadPath = SaveDir + Path.DirectorySeparatorChar + filenameJson;

        // check if exists
        if (!File.Exists(loadPath))
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-skin-load-error-no-file-exist", filenameJson));
            return false;
        }

        capi.ShowChatMessage(Lang.Get("kemono:msg-skin-load-file", filenameJson));

        string jsonString = File.ReadAllText(loadPath);
        var preset = JsonConvert.DeserializeObject<KemonoSkinnableModelPreset>(jsonString);
        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.LoadPreset(preset);

        return true;
    }

    /// Common helper to save client player's painting texture to local
    /// png file. Return true if successfully saved.
    /// TODO: lang file strings
    public bool PaintingSave(Entity entity, string paintingName, string filename)
    {
        if (filename == "")
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-painting-save-error-no-file"));
            return false;
        }

        // make sure filename ends in png
        string filenameImg = filename;
        if (!filenameImg.EndsWith(".png"))
        {
            filenameImg = filenameImg + ".png";
        }

        string imgPath = SaveDir + Path.DirectorySeparatorChar + filenameImg;
        capi.ShowChatMessage(Lang.Get("kemono:msg-painting-save-file", filenameImg));

        // serialize to png
        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        var paintingTarget = skin.Model?.PaintingTargets?.FirstOrDefault(pt => pt.Code == paintingName, null);
        if (paintingTarget == null)
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-painting-save-error-no-painting", paintingName));
            return false;
        }

        var bmp = skin.GetPaintingBitmap(paintingTarget.Code, paintingTarget.Size, paintingTarget.Size);
        using Stream fileStream = File.OpenWrite(imgPath);
        bmp.Encode(SKEncodedImageFormat.Png, 100).SaveTo(fileStream);

        return true;
    }

    /// Common helper to load client player's skin config from
    /// local json files.
    /// TODO: lang file strings
    public bool PaintingLoad(Entity entity, string paintingName, string filename)
    {
        if (filename == "")
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-painting-load-error-no-file"));
            return false;
        }

        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null)
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-error-player-not-kemono"));
            return false;
        }

        // make sure filename ends in json
        string filenameImg = filename;
        if (!filenameImg.EndsWith(".png"))
        {
            filenameImg = filenameImg + ".png";
        }

        string imgPath = SaveDir + Path.DirectorySeparatorChar + filenameImg;

        var paintingTarget = skin.Model?.PaintingTargets?.FirstOrDefault(pt => pt.Code == paintingName, null);
        if (paintingTarget == null)
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-painting-error-no-painting", paintingName));
            return false;
        }

        var result = skin.SetPaintingFromImagePath(paintingTarget.Code, paintingTarget.Size, imgPath);
        if (result.IsOk)
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-painting-load-file", filenameImg));
            return true;
        }
        else // is error
        {
            capi.ShowChatMessage(result.Error);
            return false;
        }
    }

    /// Client command to save current skin config to json file.
    public TextCommandResult OnCmdSkinSave(TextCommandCallingArgs args)
    {
        string filename = args.Parsers[0].GetValue() as string;
        bool result = SkinSave(capi.World.Player.Entity, filename);
        if (result == true)
        {
            return TextCommandResult.Success();
        }
        else
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-skin-save-error", filename));
        }
    }

    /// Client command to load skin config from json file.
    public TextCommandResult OnCmdSkinLoad(TextCommandCallingArgs args)
    {
        string filename = args.Parsers[0].GetValue() as string;
        var entity = capi.World.Player.Entity;
        bool result = SkinLoad(entity, filename);
        if (result == true)
        {
            // send new skin selection to server
            string raceCode = entity.WatchedAttributes.GetString("characterRace", DefaultRaceCode);
            string classCode = entity.WatchedAttributes.GetString("characterClass", "");
            ClientSendCharacter(entity, raceCode, classCode, true);

            return TextCommandResult.Success();
        }
        else
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-skin-load-error", filename));
        }
    }

    public TextCommandResult OnCmdSkinReload(TextCommandCallingArgs args)
    {
        bool reloadSkinparts = false;
        bool reloadClothing = false;
        bool reloadTextures = false;

        foreach (var parser in args.Parsers)
        {
            string arg = parser.GetValue() as string;
            if (arg == null)
            {
                continue;
            }

            arg = arg.ToLower();

            if (arg == "parts" || arg == "skinpart" || arg == "skinparts")
            {
                reloadSkinparts = true;
            }
            else if (arg == "cloth" || arg == "clothes" || arg == "clothing")
            {
                reloadClothing = true;
            }
            else if (arg == "texture" || arg == "textures")
            {
                reloadTextures = true;
            }
        }

        // reload animations
        ReloadAnimations(capi);

        // reload models (reloads model assets folder)
        ReloadModels(capi);

        // reload assets for all models
        int numSkinpartsReloaded = 0;
        int numClothingReloaded = 0;
        int numTexturesReloaded = 0;

        foreach (var model in AvailableModelList)
        {
            // reload all model shape paths (addons can add additional paths)
            foreach (var shapePath in model.ShapePaths)
            {
                // reload main model shape
                AssetLocation mainShapeFolder = shapePath.Clone().WithPathAppendixOnce("main/");
                capi.Assets.Reload(mainShapeFolder);

                if (reloadSkinparts)
                {
                    AssetLocation skinPartsFolder = shapePath.Clone().WithPathAppendixOnce("skinparts/");
                    numSkinpartsReloaded += capi.Assets.Reload(skinPartsFolder);
                }

                if (reloadClothing)
                {
                    AssetLocation clothingFolder = shapePath.Clone().WithPathAppendixOnce("clothing/");
                    numClothingReloaded += capi.Assets.Reload(clothingFolder);
                }
            }

            if (reloadTextures)
            {
                // reload all texture paths (addons can add additional paths)
                foreach (var texturePath in model.TexturePaths)
                {
                    numTexturesReloaded += capi.Assets.Reload(texturePath);
                }
            }
        }

        capi.ShowChatMessage(Lang.Get("kemono:msg-cmd-skin-reload-main"));

        if (numSkinpartsReloaded > 0)
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-cmd-skin-reload-parts", numSkinpartsReloaded));
        }
        if (numClothingReloaded > 0)
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-cmd-skin-reload-clothing", numClothingReloaded));
        }
        if (numTexturesReloaded > 0)
        {
            capi.ShowChatMessage(Lang.Get("kemono:msg-cmd-skin-reload-textures", numTexturesReloaded));
        }

        // reload all kemono entities loaded in world
        int numEntitiesReloaded = 0;
        foreach (var entity in capi.World.LoadedEntities.Values)
        {
            var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();

            if (skin == null) continue;

            skin.ReloadModel();
            numEntitiesReloaded++;
        }

        capi.ShowChatMessage(Lang.Get("kemono:msg-cmd-skin-reload-entities", numEntitiesReloaded));

        return TextCommandResult.Success();
    }

    /// <summary>
    /// Server command to reload animations and models.
    /// Needed for re-generating server-side animations cache.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public TextCommandResult OnCmdServerSkinReload(TextCommandCallingArgs args)
    {
        // reload animations and models
        ReloadAnimations(sapi);
        ReloadModels(sapi);

        return TextCommandResult.Success(Lang.Get("kemono:msg-cmd-serverskinreload-success"));
    }

    /// <summary>
    /// Helper to get default painting target name in entity's kemono skin,
    /// returns null if no painting targets exist.
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    public string GetEntityDefaultPaintingName(Entity entity)
    {
        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        var paintingTargets = skin?.Model?.PaintingTargets;
        if (paintingTargets == null || paintingTargets.Length == 0)
        {
            return null;
        }
        return paintingTargets[0].Code;
    }

    /// <summary>
    /// Client command to save current painting image to png.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public TextCommandResult OnCmdPaintingSave(TextCommandCallingArgs args)
    {
        string filename = args.Parsers[0].GetValue() as string;
        string paintingName = args.Parsers[1].GetValue() as string;

        // if painting name is not provided, use default first painting
        var entity = capi.World.Player.Entity;
        paintingName = paintingName ?? GetEntityDefaultPaintingName(entity);
        if (paintingName == null)
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-painting-error-no-paintings"));
        }

        bool result = PaintingSave(entity, paintingName, filename);
        if (result == true)
        {
            return TextCommandResult.Success();
        }
        else
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-painting-save-error", filename));
        }
    }

    /// <summary>
    /// Client command to load painting from locally stored png file.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public TextCommandResult OnCmdPaintingLoad(TextCommandCallingArgs args)
    {
        string filename = args.Parsers[0].GetValue() as string;
        string paintingName = args.Parsers[1].GetValue() as string;

        // if painting name is not provided, use default first painting
        var entity = capi.World.Player.Entity;
        paintingName = paintingName ?? GetEntityDefaultPaintingName(entity);
        if (paintingName == null)
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-painting-error-no-paintings"));
        }

        bool result = PaintingLoad(entity, paintingName, filename);
        if (result == true)
        {
            // send new skin to server
            string raceCode = entity.WatchedAttributes.GetString("characterRace", DefaultRaceCode);
            string classCode = entity.WatchedAttributes.GetString("characterClass", "");
            ClientSendCharacter(entity, raceCode, classCode, true);

            return TextCommandResult.Success();
        }
        else
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-painting-load-error", filename));
        }
    }

    /// <summary>
    /// Client command to load painting from a painting texture asset.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public TextCommandResult OnCmdPaintingTexLoad(TextCommandCallingArgs args)
    {
        string filename = args.Parsers[0].GetValue() as string;
        string paintingName = args.Parsers[1].GetValue() as string;

        var entity = capi.World.Player.Entity;
        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null)
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-error-player-not-kemono"));
        }

        // if skin has no painting targets, return error
        var paintingTargets = skin?.Model?.PaintingTargets;
        if (paintingTargets == null || paintingTargets.Length == 0)
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-painting-error-no-paintings"));
        }

        // get painting target by name or use default first painting
        KemonoPaintingTarget paintingTarget = paintingTargets[0];
        if (paintingName != null)
        {
            paintingTarget = paintingTargets.FirstOrDefault(pt => pt.Code == paintingName, null);
            if (paintingTarget == null)
            {
                return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-painting-find-error", paintingName));
            }
        }

        try
        {
            var result = skin.SetPaintingFromTexture(paintingTarget.Code, paintingTarget.Size, new AssetLocation(filename));
            if (result.IsErr)
            {
                capi.ShowChatMessage(result.Error);
                return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-painting-load-error", filename));
            }

            // send new skin to server
            string raceCode = entity.WatchedAttributes.GetString("characterRace", DefaultRaceCode);
            string classCode = entity.WatchedAttributes.GetString("characterClass", "");
            ClientSendCharacter(entity, raceCode, classCode, true);

            return TextCommandResult.Success();
        }
        catch (Exception e)
        {
            entity.Api.Logger.Error($"Error loading painting texture {filename}: " + e);
            return TextCommandResult.Error(Lang.Get("kemono:msg-cmd-painting-load-error", filename));
        }
    }

    #endregion


    #region Emote

    /// <summary>
    /// Make client start emote on entity. Return true on success.
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="emoteCode"></param>
    public bool ClientEmoteStart(Entity entity, string emoteCode)
    {
        if (api.Side != EnumAppSide.Client) return false;

        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null)
        {
            api.Logger.Error($"[kemono] ClientEmoteStart on non kemono entity: {entity.GetName()}");
            return false;
        }

        // get emote from skin's model
        var emote = skin.Model?.EmotesByCode.GetValueOrDefault(emoteCode, null);

        if (emote == null)
        {
            return false;
        }

        // add to active emotes if not already there
        if (skin.ActiveEmotes.Any(e => e.Code == emoteCode)) return false; // skip
        skin.ActiveEmotes.Add(emote);

        // apply skin part changes and mark for re-tesslation
        if (emote.SkinParts?.Count > 0)
        {
            // determine if base shape or face needs is dirty
            foreach (var partCode in emote.SkinParts.Keys)
            {
                if (skin.Model.SkinPartsByCode.TryGetValue(partCode, out var part))
                {
                    if (part.Face) skin.DirtyFaceShape = true;
                    else skin.DirtyBaseShape = true;
                }
            }
            entity.MarkShapeModified();
        }

        // TODO: check for max animation count limit = 16
        // ClientAnimator.MaxConcurrentAnimations  = 16
        // https://github.com/anegostudios/vsapi/blob/master/Common/Model/Animation/ClientAnimator.cs#L24

        // run animation
        // during loading or if entity unloaded, Animator is null lol lmao
        if (emote.Animation != null && entity.AnimManager?.Animator != null)
        {
            if (entity.Properties.Client.AnimationsByMetaCode.TryGetValue(emote.Animation, out AnimationMetaData animMeta))
            {
                var animMetaClient = animMeta.Clone();
                animMetaClient.ClientSide = true; // force to appear on client
                entity.AnimManager.TryStartAnimation(animMetaClient);
            }
            else
            {
                entity.AnimManager.TryStartAnimation(new AnimationMetaData()
                {
                    Code = emote.Animation,
                    Animation = emote.Animation,
                    AnimationSpeed = 1f,
                    EaseInSpeed = 10f,
                    EaseOutSpeed = 10f,
                    BlendMode = EnumAnimationBlendMode.Add,
                    SupressDefaultAnimation = true,
                    ClientSide = true,
                }.Init());
            }
        }

        return true;
    }

    /// <summary>
    /// Make client entity stop emote with code. Return true on success.
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="emoteCode"></param>
    /// <returns></returns>
    public bool ClientEmoteStop(Entity entity, string emoteCode)
    {
        if (api.Side != EnumAppSide.Client) return false;

        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null)
        {
            api.Logger.Error($"[kemono] ClientEmoteStop on non kemono entity: {entity.GetName()}");
            return false;
        }

        // get emote from skin's active emotes
        var emoteIndex = skin.ActiveEmotes.FindIndex(e => e.Code == emoteCode);
        if (emoteIndex == -1) return false; // not found

        var emote = skin.ActiveEmotes[emoteIndex];
        skin.ActiveEmotes.RemoveAt(emoteIndex);

        // apply skin part changes and mark for re-tesslation
        if (emote.SkinParts?.Count > 0)
        {
            // determine if base shape or face needs is dirty
            foreach (var partCode in emote.SkinParts.Keys)
            {
                if (skin.Model.SkinPartsByCode.TryGetValue(partCode, out var part))
                {
                    if (part.Face) skin.DirtyFaceShape = true;
                    else skin.DirtyBaseShape = true;
                }
            }
            entity.MarkShapeModified();
        }

        // stop animation
        if (emote.Animation != null)
        {
            entity.AnimManager.StopAnimation(emote.Animation);
        }

        return true;
    }

    /// <summary>
    /// Make client entity stop all emotes. Return true on success.
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="emoteCode"></param>
    /// <returns></returns>
    public bool ClientEmoteStopAll(Entity entity)
    {
        if (api.Side != EnumAppSide.Client) return false;

        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null)
        {
            api.Logger.Error($"[kemono] ClientEmoteStopAll on non kemono entity: {entity.GetName()}");
            return false;
        }

        // check if we need to re-tessellate (re-create shape)
        bool retesselate = false;

        // stop all active emotes
        foreach (var emote in skin.ActiveEmotes)
        {
            if (emote.Animation != null)
            {
                entity.AnimManager.StopAnimation(emote.Animation);
            }

            // determine if base shape or face needs is dirty
            if (emote.SkinParts?.Count > 0)
            {
                retesselate = true;
                foreach (var partCode in emote.SkinParts.Keys)
                {
                    if (skin.Model.SkinPartsByCode.TryGetValue(partCode, out var part))
                    {
                        if (part.Face) skin.DirtyFaceShape = true;
                        else skin.DirtyBaseShape = true;
                    }
                }
            }
        }

        // clear active emotes
        skin.ActiveEmotes = new List<KemonoEmote>();

        if (retesselate) // re-create shape for skin part changes
        {
            entity.MarkShapeModified();
        }

        return true;
    }

    /// <summary>
    /// Client side start emote on entity and notify server.
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="emoteCode"></param>
    public void ClientEmoteStartAndBroadcast(Entity entity, string emoteCode)
    {
        bool success = ClientEmoteStart(entity, emoteCode);
        if (!success) return;

        clientToServerChannel.SendPacket(new KemonoClientEmoteStartPacket()
        {
            Code = emoteCode,
        });
    }

    /// <summary>
    /// Client side stop emote on entity and notify server.
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="emoteCode"></param>
    public void ClientEmoteStopAndBroadcast(Entity entity, string emoteCode)
    {
        bool success = ClientEmoteStop(entity, emoteCode);
        if (!success) return;

        clientToServerChannel.SendPacket(new KemonoClientEmoteStopPacket()
        {
            Code = emoteCode,
        });
    }

    /// <summary>
    /// Client side stop all emotes on entity and notify server.
    /// </summary>
    /// <param name="entity"></param>
    public void ClientEmoteStopAllAndBroadcast(Entity entity)
    {
        bool success = ClientEmoteStopAll(entity);
        if (!success) return;

        clientToServerChannel.SendPacket(new KemonoClientEmoteStopAllPacket());
    }

    /// <summary>
    /// Server handler for emote start, broadcast to other clients.
    /// Routes directly to other clients, no emote handling on server.
    /// </summary>
    /// <param name="fromPlayer"></param>
    /// <param name="p"></param>
    public void ServerOnEmoteStartPacket(IServerPlayer fromPlayer, KemonoClientEmoteStartPacket p)
    {
        // route directly to other clients, dont do any emotes on server
        var playerEntity = fromPlayer?.Entity;
        if (playerEntity == null) return;

        serverToClientChannel.BroadcastPacket(new KemonoServerEmoteStartPacket()
        {
            EntityId = playerEntity.EntityId,
            Code = p.Code,
        }, fromPlayer);
    }


    /// <summary>
    /// Server handler for emote stop, broadcast to other clients.
    /// Routes directly to other clients, no emote handling on server.
    /// </summary>
    /// <param name="fromPlayer"></param>
    /// <param name="p"></param>
    public void ServerOnEmoteStopPacket(IServerPlayer fromPlayer, KemonoClientEmoteStopPacket p)
    {
        var playerEntity = fromPlayer?.Entity;
        if (playerEntity == null) return;

        serverToClientChannel.BroadcastPacket(new KemonoServerEmoteStopPacket()
        {
            EntityId = playerEntity.EntityId,
            Code = p.Code,
        }, fromPlayer);
    }

    /// <summary>
    /// Server handler for emote stop all, broadcast to other clients.
    /// Routes directly to other clients, no emote handling on server.
    /// </summary>
    /// <param name="fromPlayer"></param>
    /// <param name="p"></param>
    public void ServerOnEmoteStopAllPacket(IServerPlayer fromPlayer, KemonoClientEmoteStopAllPacket p)
    {
        var playerEntity = fromPlayer?.Entity;
        if (playerEntity == null) return;

        serverToClientChannel.BroadcastPacket(new KemonoServerEmoteStopAllPacket()
        {
            EntityId = playerEntity.EntityId,
        }, fromPlayer);
    }

    /// <summary>
    /// Client handler for emote start, play emote locally.
    /// </summary>
    /// <param name="p"></param>
    public void ClientOnEmoteStartPacket(KemonoServerEmoteStartPacket p)
    {
        var entity = capi.World.GetEntityById(p.EntityId);
        if (entity == null) return;
        ClientEmoteStart(entity, p.Code);
    }

    /// <summary>
    /// Client handler for emote stop.
    /// </summary>
    /// <param name="p"></param>
    public void ClientOnEmoteStopPacket(KemonoServerEmoteStopPacket p)
    {
        var entity = capi.World.GetEntityById(p.EntityId);
        if (entity == null) return;
        ClientEmoteStop(entity, p.Code);
    }

    /// <summary>
    /// Client handler for emote stop all.
    /// </summary>
    /// <param name="p"></param>
    public void ClientOnEmoteStopAllPacket(KemonoServerEmoteStopAllPacket p)
    {
        var entity = capi.World.GetEntityById(p.EntityId);
        if (entity == null) return;
        ClientEmoteStopAll(entity);
    }

    /// <summary>
    /// Client command to open emote dialog.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public TextCommandResult OnCmdEmote(TextCommandCallingArgs args)
    {
        var entity = capi.World.Player.Entity;
        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null)
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-error-player-not-kemono"));
        }

        // TODO: add arg to open for another entity player is looking at

        ClientOpenEmoteDialog(entity);

        return TextCommandResult.Success();
    }

    /// <summary>
    /// Client hotkey handler to open player kemono emote dialog.
    /// </summary>
    /// <param name="keys"></param>
    /// <returns></returns>
    public bool OnHotkeyEmote(KeyCombination keys)
    {
        ClientOpenEmoteDialog(capi.World.Player.Entity);
        return true;
    }

    /// <summary>
    /// Make client open emote dialog.
    /// Should only run on client side.
    /// </summary>
    /// <param name="entity"></param>
    public void ClientOpenEmoteDialog(Entity entity)
    {
        if (api.Side != EnumAppSide.Client)
        {
            api.Logger.Error("[kemono] ClientOpenEmoteDialog should only run on client side.");
            return;
        }

        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null)
        {
            api.Logger.Error($"[kemono] ClientOpenEmoteDialog on non kemono entity: {entity.GetName()}");
            return;
        }

        if (guiEmote == null)
        {
            guiEmote = new GuiDialogKemonoEmote(capi, this);
        }

        guiEmote.Initialize(entity);

        if (!guiEmote.IsOpened())
        {
            guiEmote.TryOpen();
        }
    }

    #endregion


    #region Misc

    /// <summary>
    /// Client command to request server to reset skin glow effects
    /// (glow parts can get glitched/stuck in pony races mod).
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public TextCommandResult OnCmdGlowReset(TextCommandCallingArgs args)
    {
        var entity = capi.World.Player.Entity;
        var skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin == null)
        {
            return TextCommandResult.Error(Lang.Get("kemono:msg-error-player-not-kemono"));
        }

        // send packet to server to remove skin glow
        clientToServerChannel.SendPacket(new KemonoClientRemoveGlowPacket()
        {
            EntityId = entity.EntityId
        });

        return TextCommandResult.Success();
    }

    #endregion
}
