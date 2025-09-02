using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using CharacterClass = Vintagestory.GameContent.CharacterClass;
using EntityBehaviorTexturedClothing = Vintagestory.GameContent.EntityBehaviorTexturedClothing;

namespace kemono;

public class GuiDialogCreateCharacterKemono : GuiDialog
{
    // constants:
    public const string CHARACTER = "kemonocreatecharacter";
    public const string RIGHTPANEL = "kemonocreaterightpanel";
    // gui tab integers
    public const int TAB_CHARACTER = 0;
    public const int TAB_EMBLEM = 1;
    public const int TAB_RACE = 2;
    public const int TAB_CLASS = 3;
    public const int TAB_DEBUG_RENDER = 4;

    // number of races per pagination page
    public const int RACES_PER_PAGE = 4;

    // tint for invalid race icon
    public static readonly double[] INVALID_RACE_TINT = {0.5, 0.0, 0.0, 0.5};

    // font for invalid race error message
    public static readonly CairoFont FONT_ERROR = CairoFont.WhiteMediumText().WithColor(new double[4] {0.8, 0, 0, 1.0});

    // title of dialog
    public string Title { get; init; }

    // entity bound to this character editing dialog
    public Entity Entity { get; init; }

    // player bound to this character editing dialog
    public IPlayer Player { get; init; }

    // callback to run when dialog is closed (i.e. selection confirmed or cancelled)
    public Action<GuiDialogCreateCharacterKemono> OnGuiClosedCallback { get; init; }

    // did select with internal set
    public bool DidSelect { get; private set; } = false;

    Dictionary<EnumCharacterDressType, int> DressPositionByTressType = new Dictionary<EnumCharacterDressType, int>();
    Dictionary<EnumCharacterDressType, ItemStack[]> DressesByDressType = new Dictionary<EnumCharacterDressType, ItemStack[]>();
    
    KemonoMod modSys;

    // GUI SIZING CONSTANTS
    const int innerWidth = 800;       // width of inner dialog, outer - 2*pad
    const int innerHeight = 440;      // height of inner dialog, outer - 2*pad
    const int titleHeight = 32;       // title bar height
    const int pad = 20;               // main screen padding on sides
    // outer width, height = inner + title + 2*pad
    const int outerWidth = innerWidth + 2 * pad;
    const int outerHeight = innerHeight + titleHeight + 2 * pad; 
    // inner screen elements
    const int charRenderWidth = 260;  // width of character renderer inset
    const int charRenderWidthHalf = charRenderWidth / 2; // half width of character renderer inset
    // vertical scrollbar width
    const int scrollbarWidth = 20;
    // width, height of right side panel (contains customization selections)
    const int rightPanelWidth = innerWidth - charRenderWidth - scrollbarWidth - 2 * 20;
    const int rightPanelHeight = innerHeight - 50;
    // y0 for screen top elements (buttons, save/load, etc.)
    const int yTop = titleHeight + 20;
    // y0 for inner screen elements
    const int y0 = yTop + 50;
    // x0 position for right side panel (right of char renderer)
    const int x0 = pad + charRenderWidth + 20;

    // character render inset on left for all tabs
    protected ElementBounds charRenderBounds;


    // MUTABLE STATE, ONLY INTERNALLY MODIFIED
    public int SelectedRaceIndex { get; private set; } = 0; // selected in gui, but may not be valid
    public int SelectedRacePaginationPage { get; private set; } = 0; // pagination page
    public int SelectedClassIndex { get; private set; } = 0;
    bool warnedRaceInvalid = false; // warned user race is invalid
    int currTab = TAB_CHARACTER;

    // character tab right panel container
    GuiElementContainerWrapper CharacterPanel = null;
    
    // engine built-in skin preset
    public string LoadBuiltinSkin = "";
    public string[] BuiltinSkinPresetCodes = {""};

    // user local skin presets
    string saveSkinFilename = "";
    string[] loadSkinFilenames = {""};
    string loadSkinFilename = "";
    
    string savePaintingFilename = "";
    string[] loadPaintingFilenames = {""};
    string loadPaintingFilename = "";

    string selectedPainting = ""; // painting code, may not be valid

    float charZoom = 1f;
    bool charNaked = true;

    // color slider mode (default RGB, optional HSV)
    bool ColorSliderModeHSV = false;

    // painting/cutie mark painter color state
    int colRed255 = 255;
    int colGreen255 = 0;
    int colBlue255 = 0;
    // double colAlpha255 = 255; // unused for now
    double colRed = 1.0;
    double colGreen = 0.0;
    double colBlue = 0.0;
    // double colAlpha = 1.0; // unused for now
    // color HSV format
    double colHue = 0.0;
    double colSat = 0.5;
    double colVal = 0.5;
    // color in vintage rgba format
    int colRGBA = ColorUtil.ColorFromRgba(255, 0, 0, 255);
    // canvas background color options
    const int CANVAS_COLOR_CLEAR = 0;
    const int CANVAS_COLOR_WHITE = ColorUtil.WhiteArgb; // ~0
    const int CANVAS_COLOR_BLACK = ColorUtil.OpaqueAlpha; // 255 << 24
    // canvas background color selected
    int canvasBackgroundColor = 0;

    // character render translate/offset
    // (for debugging)
    public double charRenderOffsetX = 0;
    public double charRenderOffsetY = 0;
    public double charRenderOffsetZ = 0;
    public float charRenderRotateX = -14;
    public float charRenderScaleX = -1;


    /// <summary>
    /// Create a new kemono character creation dialog that edits a
    /// specific entity and/or associated player.
    /// TODO: for editing npc characters, need to distinguish between
    /// player and entity, e.g. may need to add flag whether this is "self"
    /// or another entity.
    /// </summary>
    /// <param name="capi"></param>
    /// <param name="modSys"></param>
    /// <param name="entity">Entity being edited.</param>
    /// <param name="player">Player associated with entity.</param>
    /// <param name="title">Optional title for dialog, e.g. player name</param>
    public GuiDialogCreateCharacterKemono(
        ICoreClientAPI capi,
        KemonoMod modSys,
        Entity entity,
        IPlayer player,
        string title = null,
        Action<GuiDialogCreateCharacterKemono> onGuiClosedCallback = null
    ) : base(capi) {
        this.modSys = modSys;
        Entity = entity;
        Player = player;
        Title = title;
        OnGuiClosedCallback = onGuiClosedCallback;

        // get race and class index from current entity
        string raceCode = Entity.WatchedAttributes.GetString("characterRace");

        // determine initial race index
        if (raceCode != null)
        {
            // find first index in modSys.Races that matches raceCode
            SelectedRaceIndex = modSys.Races.Select((value, index) => new { value, index = index + 1 })
                .Where(pair => raceCode == pair.value.Code)
                .Select(pair => pair.index)
                .FirstOrDefault() - 1;
            if (SelectedRaceIndex >= 0)
            {
                SelectedRacePaginationPage = SelectedRaceIndex / RACES_PER_PAGE;
            }
            else
            {
                SelectedRaceIndex = 0;
                SelectedRacePaginationPage = 0;
            }
        }
        else
        {
            SelectedRaceIndex = 0;
            SelectedRacePaginationPage = 0;
        }

        // determine initial class code index
        string classCode = entity.WatchedAttributes.GetString("characterClass");

        SelectedClassIndex = modSys.baseCharSys.characterClasses.Select((value, index) => new { value, index = index + 1 })
            .Where(pair => classCode == pair.value.Code)
            .Select(pair => pair.index)
            .FirstOrDefault() - 1;
        if (SelectedClassIndex < 0) SelectedClassIndex = 0;

        // initialize built-in skin presets from mod system
        string[] none = {""};
        BuiltinSkinPresetCodes = none.Concat(modSys.SkinPresetCodes).ToArray(); // TODO: load from resources

        // initializes correct RGBA colors from initial HSV values
        UpdateRgbaFromHsv();
    }

    protected void ComposeGuis()
    {
        var ebhtc = Entity.GetBehavior<EntityBehaviorTexturedClothing>();
        if (ebhtc != null) ebhtc.hideClothing = charNaked;

        // clear old character panel
        CharacterPanel = null;

        ElementBounds tabBounds = ElementBounds.Fixed(0, -32, innerWidth, 32);
        
        GuiTab[] tabs;
        int[] tabToActiveIndex;
        if (modSys.Races.Count > 1)
        {
            tabs = new GuiTab[] {
                new GuiTab() { Name = Lang.Get("kemono:gui-tab-character"), DataInt = TAB_CHARACTER },
                new GuiTab() { Name = Lang.Get("kemono:gui-tab-painting"), DataInt = TAB_EMBLEM },
                new GuiTab() { Name = Lang.Get("kemono:gui-tab-race"), DataInt = TAB_RACE },
                new GuiTab() { Name = Lang.Get("kemono:gui-tab-class"), DataInt = TAB_CLASS },
            };
            tabToActiveIndex = new int[] {0, 1, 2, 3, 4};
        }
        else
        {
            tabs = new GuiTab[] {
                new GuiTab() { Name = Lang.Get("kemono:gui-tab-character"), DataInt = TAB_CHARACTER },
                new GuiTab() { Name = Lang.Get("kemono:gui-tab-painting"), DataInt = TAB_EMBLEM },
                new GuiTab() { Name = Lang.Get("kemono:gui-tab-class"), DataInt = TAB_CLASS },
            };
            tabToActiveIndex = new int[] {0, 1, 2, 2, 3}; // no race tab
        }

        // tab-specific general layout
        string guiTitle = Title == null ? "" : $"{Title}: ";
        int charRenderHeight;
        int yCharRender;
        switch (currTab)
        {
            case TAB_CHARACTER:
                guiTitle += Lang.Get("kemono:gui-title-character");
                charRenderHeight = rightPanelHeight;
                yCharRender = y0;
                break;
            case TAB_EMBLEM:
                guiTitle += Lang.Get("kemono:gui-title-painting");
                charRenderHeight = innerHeight;
                yCharRender = yTop;
                break;
            case TAB_RACE:
                guiTitle += Lang.Get("kemono:gui-title-race");
                charRenderHeight = innerHeight;
                yCharRender = yTop;
                break;
            case TAB_CLASS:
                guiTitle += Lang.Get("kemono:gui-title-class");
                charRenderHeight = innerHeight;
                yCharRender = yTop;
                break;
            default:
                guiTitle += Lang.Get("kemono:gui-title-character");
                charRenderHeight = rightPanelHeight;
                yCharRender = y0;
                break;
        };

        ElementBounds dialogBounds = ElementBounds
            .FixedSize(outerWidth, outerHeight)
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(GuiStyle.DialogToScreenPadding, -40);
        
        ElementBounds bgBounds = ElementBounds
            .FixedSize(outerWidth, outerHeight);
        
        charRenderBounds = ElementBounds
            .Fixed(pad, yCharRender, charRenderWidth, charRenderHeight);
        
        Composers[CHARACTER] =
            capi.Gui
            .CreateCompo(CHARACTER, dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar(guiTitle, OnTitleBarClose)
            .AddHorizontalTabs(tabs, tabBounds, onTabClicked, CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold), CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold), "tabs")
            .AddInset(charRenderBounds, 2)
            .BeginChildElements(bgBounds);
        
        var tabElem = Composers[CHARACTER].GetHorizontalTabs("tabs");
        tabElem.unscaledTabSpacing = 20;
        tabElem.unscaledTabPadding = 20;
        tabElem.activeElement = tabToActiveIndex[currTab];
        
        // tab specific rendering
        if (currTab == TAB_CHARACTER)
        {
            ComposeTabCharacter();
        }
        else if (currTab == TAB_EMBLEM)
        {
            ComposeTabPainting();
        }
        else if (currTab == TAB_RACE)
        {
            ComposeTabRace();
        }
        else if (currTab == TAB_CLASS)
        {
            ComposeTabClass();
        }
        else if (currTab == TAB_DEBUG_RENDER)
        {
            ComposeTabDebugRender();
        }
        
        Composers[CHARACTER].Compose();
    }

    /// Create tab panel for main character model customization.
    private void ComposeTabCharacter()
    {
        // get saved skin filenames from local save directory
        GetLocalSkinFilenames();

        ElementBounds rightPanelBounds = ElementBounds.Fixed(x0, y0, rightPanelWidth, rightPanelHeight);
        ElementBounds rightPanelClipBounds = rightPanelBounds.ForkBoundingParent();
        ElementBounds rightPanelInsetBounds = ElementBounds.Fixed(x0 - 10, y0, rightPanelWidth + 26, rightPanelHeight);
        
        // vertical scrollbar
        ElementBounds scrollbarBounds = ElementBounds
            .FixedSize(0, rightPanelHeight)
            .WithAlignment(EnumDialogArea.RightFixed)
            .WithFixedWidth(20)
            .WithFixedAlignmentOffset(-pad, y0);
        
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();

        var ebhtc = Entity.GetBehavior<EntityBehaviorTexturedClothing>();
        if (ebhtc != null) ebhtc.hideClothing = charNaked;

        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr.RunTesselation();

        CairoFont smallfont = CairoFont.WhiteSmallText();
        var textExt = smallfont.GetTextExtents(Lang.Get("Show dressed"));

        // buttons on top
        ElementBounds toggleDressedButtonBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, pad, yTop, 120, 20)
            .WithFixedPadding(10, 6);
        
        ElementBounds randomizeButtonBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, 0, yTop, 100, 20)
            .FixedRightOf(toggleDressedButtonBounds, 20)
            .WithFixedPadding(10, 6);

        ElementBounds loadInputTextBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, x0 - 10, yTop + 2, 130, 30);
        
        ElementBounds loadButtonBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, 0, yTop, 60, 20)
            .WithFixedPadding(10, 6)
            .FixedRightOf(loadInputTextBounds, 1);

        ElementBounds saveInputTextBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, 0, yTop + 2, 120, 30)
            .FixedRightOf(loadButtonBounds, 30);
        
        ElementBounds saveButtonBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, 0, yTop, 60, 20)
            .FixedRightOf(saveInputTextBounds, 1)
            .WithFixedPadding(10, 6);
        
        ElementBounds confirmButtonBounds = ElementBounds
            .Fixed(EnumDialogArea.RightTop, -pad + 2, yTop, 80, 20)
            .WithFixedPadding(10, 6);

        Composers[CHARACTER]
            .AddToggleButton(Lang.Get("kemono:gui-show-dressed-btn"), smallfont, OnToggleDressOnOff, toggleDressedButtonBounds, "showdressedtoggle")
            .AddSmallButton(Lang.Get("kemono:gui-randomize-btn"), OnRandomizeSkin, randomizeButtonBounds, EnumButtonStyle.Normal)
            .AddDropDown(loadSkinFilenames, loadSkinFilenames, 0, OnLoadSkinFilenameChange, loadInputTextBounds, "loadskinfilenamesdropdown")
            .AddSmallButton(Lang.Get("kemono:gui-load-btn"), OnLoadSkin, loadButtonBounds, EnumButtonStyle.Normal)
            // .AddTextInput(loadInputTextBounds, OnLoadInputTextChanged, null, "loadinputtext") // using dropdown instead now
            .AddTextInput(saveInputTextBounds, OnSaveSkinInputChanged, null, "saveinputtext")
            .AddSmallButton(Lang.Get("kemono:gui-save-btn"), OnSaveSkin, saveButtonBounds, EnumButtonStyle.Normal)
            .AddSmallButton(Lang.Get("kemono:gui-confirm-skin-btn"), OnNext, confirmButtonBounds, EnumButtonStyle.Normal)
        ;

        Composers[CHARACTER].GetToggleButton("showdressedtoggle").SetValue(!charNaked);
        // Composers[CHARACTER].GetTextInput("loadinputtext").SetPlaceHolderText(Lang.Get("kemono:gui-filename-placeholder"));
        Composers[CHARACTER].GetTextInput("saveinputtext").SetPlaceHolderText(Lang.Get("kemono:gui-filename-placeholder"));
        Composers[CHARACTER].GetTextInput("saveinputtext").SetValue(saveSkinFilename);
        
        Composers[CHARACTER]
            .AddInset(rightPanelInsetBounds, 2)
            .BeginClip(rightPanelClipBounds)
            .AddContainer(rightPanelBounds, "rightpanel");
        
        var rightPanel = new GuiElementContainerWrapper(capi, Composers[CHARACTER].GetContainer("rightpanel"));
        CharacterPanel = rightPanel;
        CharacterPanel.container.Tabbable = true; // required in 1.21 for text inputs in container

        // ----------------------------------------
        // BEGIN RIGHT PANEL CUSTOMIZATION ELEMENTS
        // ----------------------------------------
        int columnWidth = 220; // width of each column in gui row
        int columnOffset = columnWidth + 30; // column offset in gui row

        string[] modelTypes = modSys.AvailableModelList
            .Where(m => !m.Hidden)
            .Select(m => m.Code)
            .ToArray();

        // get selected model index
        int selectedModelIndex = Array.IndexOf(modelTypes, skin.Model.Code);
        if (selectedModelIndex < 0) selectedModelIndex = 0;

        // main model selection
        ElementBounds colorSliderModeLabelBounds = ElementBounds
            .Fixed(0, 10, 90, 30);
        ElementBounds colorSliderModeRGBBtnBounds = ElementBounds
            .Fixed(0, 10, 60, 30)
            .RightOf(colorSliderModeLabelBounds, 5);
        ElementBounds colorSliderModeHSVBtnBounds = ElementBounds
            .Fixed(0, 10, 60, 30)
            .RightOf(colorSliderModeRGBBtnBounds, 5);
        
        // main model selection
        ElementBounds modelSelectLabelBounds = ElementBounds
            .Fixed(0, 10, columnWidth, 30)
            .FixedUnder(colorSliderModeLabelBounds, 20);
        ElementBounds modelSelectBounds = ElementBounds
            .Fixed(0, 0, columnWidth, 25)
            .FixedUnder(modelSelectLabelBounds, 0);

        // presets load button and dropdown selection
        ElementBounds presetLoadButtonBounds = ElementBounds
            .Fixed(columnOffset, 10, columnWidth, 30)
            .FixedUnder(colorSliderModeLabelBounds, 20);
        
        ElementBounds presetLoadDropdownBounds = ElementBounds
            .Fixed(columnOffset, 0, columnWidth, 25)
            .FixedUnder(presetLoadButtonBounds, 0);
        
        rightPanel
            .AddRichtext(Lang.Get("kemono:gui-color-slider-mode"), CairoFont.WhiteSmallishText(), colorSliderModeLabelBounds)
            .AddToggleButton(
                Lang.Get("kemono:gui-symbol-rgb"),
                CairoFont.WhiteSmallText(),
                (newVal) => { OnSetColorSliderModeHSV(false); },
                colorSliderModeRGBBtnBounds,
                "color-mode-btn-rgb"
            )
            .AddToggleButton(
                Lang.Get("kemono:gui-symbol-hsv"),
                CairoFont.WhiteSmallText(),
                (newVal) => { OnSetColorSliderModeHSV(true); },
                colorSliderModeHSVBtnBounds,
                "color-mode-btn-hsv"
            )
            .AddRichtext(Lang.Get("kemono:gui-model-label"), CairoFont.WhiteSmallishText(), modelSelectLabelBounds)
            .AddDropDown(modelTypes, modelTypes, selectedModelIndex, OnSelectModel, modelSelectBounds, "dropdown-model")
            .AddSmallButton(Lang.Get("kemono:gui-preset-btn"), OnLoadBuiltinSkinPreset, presetLoadButtonBounds, EnumButtonStyle.Normal)
            .AddDropDown(BuiltinSkinPresetCodes, BuiltinSkinPresetCodes, 0, OnSelectBuiltinSkinPreset, presetLoadDropdownBounds, "dropdown-preset")
        ;

        // set color slider mode toggle button state
        rightPanel.GetElementAs<GuiElementToggleButton>("color-mode-btn-rgb").SetValue(!ColorSliderModeHSV);
        rightPanel.GetElementAs<GuiElementToggleButton>("color-mode-btn-hsv").SetValue(ColorSliderModeHSV);

        // start of customization options (dummy bounds)
        ElementBounds startCustomization = ElementBounds
            .Fixed(0, 0, 200, 20)
            .FixedUnder(presetLoadDropdownBounds, 40);
        
        int yRow = (int) startCustomization.fixedY;

        // ================================================================
        // SCALE PARTS CUSTOMIZATION
        // ================================================================
        // get current applied parts
        var appliedScale = skin.AppliedScale;

        int scaleTitleHeight = 40;
        int scaleTitlePadding = 2;
        ElementBounds boundsScaleTitle = ElementBounds
            .Fixed(0, yRow, rightPanelWidth, scaleTitleHeight);
        ElementBounds boundsScaleResetBtn = ElementBounds
            .Fixed(EnumDialogArea.RightFixed, 0, yRow, 120, scaleTitleHeight - 10);
        
        yRow += scaleTitleHeight + scaleTitlePadding;

        rightPanel
            .AddRichtext(
                Lang.Get("kemono:gui-scalepart-title"),
                CairoFont.WhiteSmallishText(),
                boundsScaleTitle
            )
            .AddSmallButton(
                Lang.Get("kemono:gui-reset-btn"),
                OnPartScaleReset,
                boundsScaleResetBtn,
                EnumButtonStyle.Normal
            );
        
        foreach (var part in skin.Model.ScaleParts)
        {
            int yRowSize = 16;
            int yRowPadding = 12;
            int widthLabel = 120;
            int widthValue = 80;

            ElementBounds boundsSlider = ElementBounds.Fixed(0, yRow, rightPanelWidth, yRowSize);

            double initialScale = appliedScale?.TryGetDouble(part.Code) ?? 1.0;
            int initialScaleInt = (int)(initialScale * 100);
            int scaleMin = (int)(part.Min * 100);
            int scaleMax = (int)(part.Max * 100);

            rightPanel.AddScaleSlider(
                Lang.Get("kemono:scalepart-" + part.Code),
                part.Code,
                boundsSlider,
                widthLabel,
                widthValue,
                scaleMin,
                scaleMax,
                1,
                initialScaleInt,
                (scale) => { OnPartScaleChange(part.Code, ((double)scale) / 100.0); }
            );

            yRow += (yRowSize + yRowPadding);
        }

        yRow += 8; // add some padding after scale parts

        // ================================================================
        // EYE HEIGHT OFFSET
        // ================================================================
        // get current applied parts
        var appliedEyeHeight = skin.AppliedEyeHeightOffset;

        ElementBounds boundsEyeHeightTitle = ElementBounds
            .Fixed(0, yRow, rightPanelWidth, scaleTitleHeight);
        ElementBounds boundsEyeHeightResetBtn = ElementBounds
            .Fixed(EnumDialogArea.RightFixed, 0, yRow, 120, scaleTitleHeight - 10);

        yRow += scaleTitleHeight + scaleTitlePadding;

        rightPanel
            .AddRichtext(
                Lang.Get("kemono:gui-eyeheight-title"),
                CairoFont.WhiteSmallishText(),
                boundsEyeHeightTitle
            )
            .AddSmallButton(
                Lang.Get("kemono:gui-reset-btn"),
                OnPartEyeHeightReset,
                boundsEyeHeightResetBtn,
                EnumButtonStyle.Normal
            );

        double initialEyeHeightOffset = skin.AppliedEyeHeightOffset;
        int initialEyeHeightOffsetInt = (int) (initialEyeHeightOffset * 100);
        int eyeHeightOffsetMin = (int) (skin.Model.EyeHeightOffsetMin * 100);
        int eyeHeightOffsetMax = (int) (skin.Model.EyeHeightOffsetMax * 100);
        
        int eyeOffsetYRowSize = 16;
        int eyeOffsetYRowPadding = 12;
        int eyeOffsetWidthLabel = 120;
        int eyeOffsetWidthValue = 80;

        ElementBounds boundsEyeHeightSlider = ElementBounds.Fixed(0, yRow, rightPanelWidth, eyeOffsetYRowSize);

        rightPanel.AddScaleSlider(
            Lang.Get("kemono:gui-eyeheight-label"),
            "eyeHeightOffset",
            boundsEyeHeightSlider,
            eyeOffsetWidthLabel,
            eyeOffsetWidthValue,
            eyeHeightOffsetMin,
            eyeHeightOffsetMax,
            1,
            initialEyeHeightOffsetInt,
            (offset) => { OnPartEyeHeightChange(offset / 100.0); }
        );

        yRow += eyeOffsetYRowSize + eyeOffsetYRowPadding;
        
        // ================================================================
        // SKIN PARTS CUSTOMIZATION
        // ================================================================

        // get current applied parts
        var appliedVariants = skin.AppliedAllSkinParts();
        var appliedColors = skin.AppliedColor;

        foreach (var rowParts in skin.Model.GuiLayout)
        {
            // start new row in gui layout

            int x = 0;
            int yRowSize = 40; // row height = max { col left height, col right height }
            int yRowPadding = 40;

            foreach (var code in rowParts)
            {
                // each skin part in the row

                int y = yRow; // y position of element in row, increases as elements added
                int columnHeight = 0; // calculated from elements, for setting yRowHeight
                int dy = 0; // offset between elements in column
                ElementBounds bounds = ElementBounds.Fixed(x, y, columnWidth, 40);

                KemonoSkinnablePart skinpart;
                if (!skin.Model.SkinPartsByCode.TryGetValue(code, out skinpart))
                {
                    rightPanel.AddRichtext("MISSING: " + code, CairoFont.WhiteSmallishText(), bounds);
                    continue;
                }

                // label
                rightPanel.AddRichtext(
                    Lang.Get("kemono:skinpart-" + code),
                    CairoFont.WhiteSmallishText(),
                    bounds = bounds.BelowCopy(0, 0).WithFixedSize(240, 30)
                );

                string appliedVariantCode = "";
                if (skinpart.Type == EnumKemonoSkinnableType.Voice)
                {
                    // voice parts are stored separately in entity watched attributes
                    if (skinpart.Code == "voicetype")
                    {
                        appliedVariantCode = skin.entity.WatchedAttributes.GetString("voicetype", "");
                    }
                    else if (skinpart.Code == "voicepitch")
                    {
                        appliedVariantCode = skin.entity.WatchedAttributes.GetString("voicepitch", "");
                    }
                }
                else
                {
                    AppliedKemonoSkinnablePartVariant appliedVar = appliedVariants.FirstOrDefault(sp => sp.PartCode == code);
                    appliedVariantCode = appliedVar?.Code ?? "";
                }

                // dropdown variant selection
                if (skinpart.UseDropDown)
                {
                    if (skinpart.Variants.Length > 0)
                    {
                        string[] names = new string[skinpart.Variants.Length];
                        string[] values = new string[skinpart.Variants.Length];

                        int selectedIndex = 0;

                        for (int i = 0; i < skinpart.Variants.Length; i++)
                        {
                            names[i] = Lang.Get("kemono:skinpart-" + code + "-" + skinpart.Variants[i].Code);
                            values[i] = skinpart.Variants[i].Code;

                            //Console.WriteLine("\"" + names[i] + "\": \"" + skinpart.Variants[i].Code + "\",");

                            if (appliedVariantCode == values[i]) selectedIndex = i;
                        }

                        string tooltip = Lang.GetIfExists("kemono:skinpartdesc-" + code);
                        if (tooltip != null)
                        {
                            rightPanel.AddHoverText(tooltip, CairoFont.WhiteSmallText(), 300, bounds = bounds.FlatCopy());
                        }

                        rightPanel.AddDropDown(
                            values,
                            names,
                            selectedIndex,
                            (variantcode, selected) => OnSelectSkinPart(code, variantcode),
                            bounds = bounds.BelowCopy(0, dy).WithFixedSize(columnWidth, 25),
                            "dropdown-" + code
                        );
                    }
                    else
                    {
                        bounds = bounds.BelowCopy(0, dy).WithFixedSize(columnWidth, 25);
                        rightPanel.AddInset(bounds, 2);
                    }

                    columnHeight += 45;
                    dy = 10;
                }

                // RGB color sliders
                if (skinpart.UseColorSlider)
                {
                    // set initial color from skinpart
                    int initialRed = 69;
                    int initialGreen = 69;
                    int initialBlue = 69;

                    int? initialColor = appliedColors?.TryGetInt(code);
                    if (initialColor != null) {
                        // unpack initial color
                        (initialRed, initialGreen, initialBlue) = KemonoColorUtil.ToRgb((int) initialColor);
                    } else {
                        // select initial color for skinpart
                        int packedColor = ColorUtil.ColorFromRgba(69, 69, 69, 255);
                        skin.SetSkinPartColor(code, packedColor);
                    }

                    string descPartColorCode = "kemono:skinpart-" + code + "-color";

                    // TODO: currently converting between RGB <-> HSV is LOSSY
                    // if you switch between modes, exact values will change
                    // this is due to the integer/double conversions and different
                    // resolutions used in HSV (0, 100) vs RGB (0, 255)
                    // need to decide how to handle this in future...
                    if ( ColorSliderModeHSV )
                    {
                        (double initialHue, double initialSat, double initialVal) = KemonoColorUtil.RgbToHsv(initialRed, initialGreen, initialBlue);
                        
                        rightPanel.AddHSVColorSliders(
                            Lang.Get(descPartColorCode),
                            descPartColorCode,
                            columnWidth, // width
                            16,          // height
                            bounds = bounds.BelowCopy(0, dy).WithFixedSize(columnWidth, 80),
                            initialHue,
                            initialSat,
                            initialVal,
                            (col) => { OnPartColorChange(code, col); }
                        );
                    }
                    else
                    {
                        rightPanel.AddRGBColorSliders(
                            Lang.Get(descPartColorCode),
                            descPartColorCode,
                            columnWidth, // width
                            16,          // height
                            bounds = bounds.BelowCopy(0, dy).WithFixedSize(columnWidth, 80),
                            initialRed,
                            initialGreen,
                            initialBlue,
                            (col) => { OnPartColorChange(code, col); }
                        );
                    }

                    columnHeight += 100;
                    dy = 10;
                }

                // transform sliders
                // -> always place on right column side
                if (skinpart.UseTransformSlider)
                {
                    // TODO
                }

                x += columnOffset;

                yRowSize = Math.Max(yRowSize, columnHeight);
            }

            yRow += (yRowSize + yRowPadding);
        }

        ElementBounds bottomPadding = ElementBounds.Fixed(0, yRow, 200, 40);
        rightPanel.AddPadding(bottomPadding);
        
        Composers[CHARACTER]
            .EndClip()
            .AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, "scrollbar");

        rightPanel.container.CalcTotalHeight();
        Composers[CHARACTER].GetScrollbar("scrollbar").Bounds.CalcWorldBounds();
        Composers[CHARACTER].GetScrollbar("scrollbar")?.SetHeights(
            (float)(rightPanelHeight),
            (float)(rightPanel.container.Bounds.fixedHeight)
        );
    }

    /// Create tab panel for painting customization.
    private void ComposeTabPainting()
    {
        // get saved painting filenames from local save directory
        GetLocalPaintingFilenames();

        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();

        var ebhtc = Entity.GetBehavior<EntityBehaviorTexturedClothing>();
        if (ebhtc != null) ebhtc.hideClothing = charNaked;
        
        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr.RunTesselation();
        
        // save/load painting buttons on top
        ElementBounds loadInputTextBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, x0 - 10, yTop + 2, 130, 30);

        ElementBounds loadButtonBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, 0, yTop, 60, 20)
            .WithFixedPadding(10, 6)
            .FixedRightOf(loadInputTextBounds, 1);
        
        ElementBounds saveInputTextBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, 0, yTop + 2, 120, 30)
            .FixedRightOf(loadButtonBounds, 30);
        
        ElementBounds saveButtonBounds = ElementBounds
            .Fixed(EnumDialogArea.LeftTop, 0, yTop, 60, 20)
            .FixedRightOf(saveInputTextBounds, 1)
            .WithFixedPadding(10, 6);
        
        ElementBounds confirmButtonBounds = ElementBounds
            .Fixed(EnumDialogArea.RightTop, -pad + 2, yTop, 80, 20)
            .WithFixedPadding(10, 6);

        Composers[CHARACTER]
            .AddDropDown(loadPaintingFilenames, loadPaintingFilenames, 0, OnLoadPaintingFilenameChange, loadInputTextBounds, "loadpaintingfilenamesdropdown")
            .AddSmallButton(Lang.Get("kemono:gui-load-btn"), OnLoadPainting, loadButtonBounds, EnumButtonStyle.Normal)
            // .AddTextInput(loadInputTextBounds, OnLoadInputTextChanged, null, "loadpaintingtext") // using dropdown instead now
            .AddTextInput(saveInputTextBounds, OnSavePaintingInputChanged, null, "savepaintingtext")
            .AddSmallButton(Lang.Get("kemono:gui-save-btn"), OnSavePainting, saveButtonBounds, EnumButtonStyle.Normal)
            .AddSmallButton(Lang.Get("kemono:gui-confirm-painting-btn"), OnNext, confirmButtonBounds, EnumButtonStyle.Normal)
        ;

        // Composers[CHARACTER].GetTextInput("loadpaintingtext").SetPlaceHolderText(Lang.Get("kemono:gui-filename-placeholder"));
        Composers[CHARACTER].GetTextInput("savepaintingtext").SetPlaceHolderText(Lang.Get("kemono:gui-filename-placeholder"));
        Composers[CHARACTER].GetTextInput("savepaintingtext").SetValue(savePaintingFilename);

        // make painter

        // LEFT SIDE: canvas
        ElementBounds canvasBounds = ElementBounds
            .Fixed(x0, y0, 320, 320);
        
        // add dropdown for selecting painting
        ElementBounds selectPaintingLabelBounds = ElementBounds
            .Fixed(x0, 0, 100, 30)
            .FixedUnder(canvasBounds, 12);
        ElementBounds selectPaintingDropdownBounds = ElementBounds
            .Fixed(x0, 0, 200, 30)
            .FixedUnder(canvasBounds, 10)
            .FixedRightOf(selectPaintingLabelBounds, 10);

        // instructions on bottom
        ElementBounds instructionBounds = ElementBounds
            .Fixed(x0, -pad, 500, 30)
            .WithAlignment(EnumDialogArea.LeftBottom);

        // RIGHT SIDE: tools and color selection
        
        // sat/val selection box 
        ElementBounds satValBounds = ElementBounds
            .Fixed(0, y0, 150, 150)
            .FixedRightOf(canvasBounds, 10);
        // hue slider
        ElementBounds hueSliderBounds = ElementBounds
            .Fixed(0, 0, 150, 20)
            .FixedRightOf(canvasBounds, 10)
            .FixedUnder(satValBounds, 10);
        
        // get painting names and initial painting pixels
        string[] paintingNames = skin.Model.PaintingTargets.Select(p => p.Code).ToArray();
        int selectedPaintingIndex = 0;
        bool canvasEnabled = true;
        BitmapSimple initialBmp;
        if (paintingNames.Length > 0)
        {
            selectedPaintingIndex = Array.IndexOf(paintingNames, selectedPainting);
            if (selectedPaintingIndex < 0)
            {
                selectedPaintingIndex = 0;
                selectedPainting = paintingNames[0];
            }
            var selectedPaintingTarget = skin.Model.PaintingTargets[selectedPaintingIndex];
            int paintingSize = selectedPaintingTarget.Size;
            int[] pixels = skin.GetPaintingPixels(selectedPaintingTarget.Code, paintingSize, paintingSize);
            initialBmp = new BitmapSimple(paintingSize, paintingSize, pixels);
        }
        else
        {
            paintingNames = new string[] {""};
            canvasEnabled = false;
            initialBmp = new BitmapSimple(32, 32);
        }
        
        Composers[CHARACTER].AddCanvas(canvasBounds, canvasEnabled, colRGBA, initialBmp, OnCanvasPaint, OnCanvasSelectColor, "canvas");
        Composers[CHARACTER].AddColorSatValSelector(satValBounds, colHue, colSat, colVal, OnPainterSatValChange, "satvalselector");
        Composers[CHARACTER].AddColorHueSlider(hueSliderBounds, colHue, OnPainterHueChange, "hueslider");
        Composers[CHARACTER].AddRichtext(
            Lang.Get("kemono:gui-select-painting"),
            CairoFont.WhiteSmallishText(),
            selectPaintingLabelBounds
        );
        Composers[CHARACTER].AddDropDown(
            paintingNames,
            paintingNames,
            selectedPaintingIndex,
            OnSelectPainting,
            selectPaintingDropdownBounds,
            "selectpaintingdropdown"
        );
        Composers[CHARACTER].AddRichtext(
            Lang.Get("kemono:gui-canvas-instructions"),
            CairoFont.WhiteDetailText(),
            instructionBounds
        );

        // set background color selection 
        ElementBounds canvasBackgroundColorLabelBounds = ElementBounds
            .Fixed(0, 0, 150, 20)
            .FixedRightOf(canvasBounds, 10)
            .FixedUnder(hueSliderBounds, 16);
        
        ElementBounds canvasBackgroundColorClearBounds = ElementBounds
            .Fixed(0, 0, 20, 20)
            .FixedRightOf(canvasBounds, 10)
            .FixedUnder(canvasBackgroundColorLabelBounds, 4);
        ElementBounds canvasBackgroundColorClearLabelBounds = ElementBounds
            .Fixed(0, 0, 120, 22)
            .FixedRightOf(canvasBackgroundColorClearBounds, 4)
            .FixedUnder(canvasBackgroundColorLabelBounds, 4);
        
        ElementBounds canvasBackgroundColorWhiteBounds = ElementBounds
            .Fixed(0, 0, 20, 20)
            .FixedRightOf(canvasBounds, 10)
            .FixedUnder(canvasBackgroundColorClearLabelBounds, 4);
        ElementBounds canvasBackgroundColorWhiteLabelBounds = ElementBounds
            .Fixed(0, 0, 120, 22)
            .FixedRightOf(canvasBackgroundColorWhiteBounds, 4)
            .FixedUnder(canvasBackgroundColorClearLabelBounds, 4);
        
        ElementBounds canvasBackgroundColorBlackBounds = ElementBounds
            .Fixed(0, 0, 20, 20)
            .FixedRightOf(canvasBounds, 10)
            .FixedUnder(canvasBackgroundColorWhiteLabelBounds, 4);
        ElementBounds canvasBackgroundColorBlackLabelBounds = ElementBounds
            .Fixed(0, 0, 120, 22)
            .FixedRightOf(canvasBackgroundColorBlackBounds, 4)
            .FixedUnder(canvasBackgroundColorWhiteLabelBounds, 4);
        
        Composers[CHARACTER].AddRichtext(
            Lang.Get("kemono:gui-canvas-background-color-label"),
            CairoFont.WhiteSmallText(),
            canvasBackgroundColorLabelBounds
        );

        Composers[CHARACTER].AddInset(
            canvasBackgroundColorClearBounds,
            2
        );
        Composers[CHARACTER].AddToggleButton(
            Lang.Get("kemono:gui-canvas-background-color-clear-label"),
            CairoFont.WhiteSmallText(),
            (newVal) => { OnSetCanvasBackgroundColor(false, CANVAS_COLOR_CLEAR); },
            canvasBackgroundColorClearLabelBounds,
            "canvas-background-color-clear"
        );

        Composers[CHARACTER].AddColorRectangle(
            canvasBackgroundColorWhiteBounds,
            255,
            255,
            255
        );
        Composers[CHARACTER].AddToggleButton(
            Lang.Get("kemono:gui-canvas-background-color-white-label"),
            CairoFont.WhiteSmallText(),
            (newVal) => { OnSetCanvasBackgroundColor(true, CANVAS_COLOR_WHITE); },
            canvasBackgroundColorWhiteLabelBounds,
            "canvas-background-color-white"
        );

        Composers[CHARACTER].AddColorRectangle(
            canvasBackgroundColorBlackBounds,
            0,
            0,
            0
        );
        Composers[CHARACTER].AddToggleButton(
            Lang.Get("kemono:gui-canvas-background-color-black-label"),
            CairoFont.WhiteSmallText(),
            (newVal) => { OnSetCanvasBackgroundColor(true, CANVAS_COLOR_BLACK); },
            canvasBackgroundColorBlackLabelBounds,
            "canvas-background-color-black"
        );

        // initialize canvas back ground color
        switch ( canvasBackgroundColor )
        {
            case CANVAS_COLOR_CLEAR:
                OnSetCanvasBackgroundColor(false, CANVAS_COLOR_CLEAR);
                break;
            case CANVAS_COLOR_WHITE:
                OnSetCanvasBackgroundColor(true, CANVAS_COLOR_WHITE);
                break;
            case CANVAS_COLOR_BLACK:
                OnSetCanvasBackgroundColor(true, CANVAS_COLOR_BLACK);
                break;
            default: // clear
                OnSetCanvasBackgroundColor(false, CANVAS_COLOR_CLEAR);
                break;
        }

        // clear painting button
        ElementBounds clearPaintingBtnBounds = ElementBounds
            .Fixed(0, 0, 144, 30)
            .FixedRightOf(canvasBounds, 10)
            .FixedUnder(canvasBackgroundColorBlackBounds, 20);

        Composers[CHARACTER].AddSmallButton(
            Lang.Get("kemono:gui-clear-painting"),
            OnClearPainting,
            clearPaintingBtnBounds
        );
    }

    /// Create tab panel for character race selection.
    private void ComposeTabRace()
    {
        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr.RunTesselation();
        
        // size of char selection toggle on top of right panel
        const int selectionHeight = 100;
        const int selectionBtnPad = 8;
        const int selectionBtnWidth = 60 - 2 * selectionBtnPad;
        const int selectionBtnOffset = 10;
        const int selectionMiddleWidth = rightPanelWidth - 2 * selectionBtnWidth - 2 * selectionBtnOffset;
        const int raceTooltipWidth = 300;

        ElementBounds prevButtonBounds = ElementBounds
            .Fixed(x0, yTop, selectionBtnWidth, selectionHeight)
            .WithFixedPadding(selectionBtnPad);

        ElementBounds centerTextBounds = ElementBounds
            .Fixed(0, yTop, selectionMiddleWidth, selectionHeight + 8)
            .FixedRightOf(prevButtonBounds, 4 + selectionBtnPad + selectionBtnOffset);
        
        ElementBounds charclassInset = centerTextBounds.ForkBoundingParent(4, 4, 4, 4);

        ElementBounds nextButtonBounds = ElementBounds
            .Fixed(0, yTop, selectionBtnWidth, selectionHeight)
            .WithFixedPadding(selectionBtnPad)
            .FixedRightOf(charclassInset, selectionBtnOffset);

        CairoFont font = CairoFont.WhiteMediumText();
        centerTextBounds.fixedY += (centerTextBounds.fixedHeight - font.GetFontExtents().Height / RuntimeEnv.GUIScale) / 2;

        ElementBounds nameBounds = ElementBounds
            .Fixed(x0, 0, rightPanelWidth, 20)
            .FixedUnder(prevButtonBounds, 28);
        
        ElementBounds descBounds = ElementBounds
            .Fixed(x0, 0, rightPanelWidth, 100)
            .FixedUnder(nameBounds, 16);

        ElementBounds confirmBtnBounds = ElementBounds
            .Fixed(-pad, -pad, 100, 20)
            .WithAlignment(EnumDialogArea.RightBottom)
            .WithFixedPadding(12, 6);
        
        // if invalid, put error text on side
        ElementBounds errorTextBounds = ElementBounds
            .Fixed(x0 + 120, -pad, rightPanelWidth - 100, 20)
            .WithAlignment(EnumDialogArea.LeftBottom);
        
        Composers[CHARACTER]
            .AddIconButton("left", (on) => changeRacePaginationPage(-1), prevButtonBounds.FlatCopy())
            // .AddInset(charclassInset, 2)
            // .AddDynamicText("Unknown", font.Clone().WithOrientation(EnumTextOrientation.Center), centerTextBounds, "raceName")
            .AddIconButton("right", (on) => changeRacePaginationPage(1), nextButtonBounds.FlatCopy())

            .AddRichtext("", CairoFont.WhiteSmallishText(), nameBounds, "raceName")
            .AddRichtext("", CairoFont.WhiteDetailText(), descBounds, "raceDesc")
            .AddSmallButton(Lang.Get("kemono:gui-confirm-race-btn"), OnNext, confirmBtnBounds, EnumButtonStyle.Normal, "confirmracebtn")
            .AddRichtext("", CairoFont.WhiteMediumText().WithColor(new double[4] {0.8, 0, 0, 1.0}), errorTextBounds, "raceError")
        ;

        // race selection paginated icons and text
        const int selectionIconPad = 4;
        const int selectionIconSize = ((selectionMiddleWidth + 8) / RACES_PER_PAGE) - 2*selectionIconPad;
        for (var n = 0; n < RACES_PER_PAGE; n++)
        {
            // actual selected race index
            int i = SelectedRacePaginationPage * RACES_PER_PAGE + n;

            // x gui offset for selection icon
            int x = 4 + selectionBtnPad + selectionBtnOffset + n * (selectionIconSize + 2*selectionIconPad);

            int insetDepth = 1;

            ElementBounds selectionBounds = ElementBounds
                .Fixed(0, yTop, selectionIconSize, selectionIconSize)
                .FixedRightOf(prevButtonBounds, x)
                .WithFixedPadding(selectionIconPad, selectionIconPad);
            
            ElementBounds iconBounds = selectionBounds.FlatCopy();
            
            ElementBounds raceLabelBounds = ElementBounds
                .Fixed(0, 0, selectionIconSize, 20)
                .FixedRightOf(prevButtonBounds, x)
                .WithFixedPadding(selectionIconPad, 0)
                .FixedUnder(selectionBounds, 4);
            
            CairoFont labelFont = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);

            double[] iconTint = null;

            // selected race unique adjustments
            if (i == SelectedRaceIndex)
            {
                insetDepth = 12;
                labelFont = labelFont.WithWeight(Cairo.FontWeight.Bold);
            }
            
            // character icon
            if (i < modSys.Races.Count)
            {
                KemonoRace race = modSys.Races[i];
                AssetLocation icon = race.Icon != null ? race.Icon : KemonoRace.DefaultIcon;

                // check if race valid
                var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
                var skinparts = skin.GetSkinPartsTreeDictionary();
                if (skinparts.tree != null)
                {
                    bool raceIsValid = KemonoRace.IsValid(race, skin.Model.Code, skinparts, Player);
                    if (!raceIsValid)
                    {
                        labelFont = labelFont.WithColor(new double[] {0.8, 0.0, 0.0, 1.0});
                        iconTint = INVALID_RACE_TINT;

                        if (i == SelectedRaceIndex)
                        {
                            Composers[CHARACTER].GetButton("confirmracebtn").Enabled = false;
                            Composers[CHARACTER].GetRichtext("raceError").SetNewTextWithoutRecompose(Lang.Get("kemono:race-error-invalid"), FONT_ERROR);
                        }
                    }
                }

                // create race tooltip showing requirements
                StringBuilder tooltip = new StringBuilder();

                tooltip.AppendLine("<font weight=\"bold\" size=20>" + Lang.Get($"kemono:race-name-{race.Code}") + "</font>");
                tooltip.AppendLine();

                if (race.Privilege != null)
                {
                    tooltip.AppendLine("<i>" + Lang.Get($"kemono:race-required-privilege") + "</i>: " + race.Privilege);
                    tooltip.AppendLine();
                }

                if (race.Model != null)
                {
                    tooltip.AppendLine("<i>" + Lang.Get($"kemono:race-required-model") + "</i>: " + race.Model);
                    tooltip.AppendLine();
                }
                
                if (race.RequiredParts.Count > 0)
                {
                    tooltip.AppendLine("<i>" + Lang.Get($"kemono:race-required-parts") + "</i>:");
                    foreach (var required in race.RequiredParts)
                    {
                        tooltip.AppendLine("   " + Lang.Get($"kemono:skinpart-{required.Key}") + ":");
                        foreach (var variant in required.Value)
                        {
                            if (variant == "*")
                            {
                                tooltip.AppendLine("     *");
                            }
                            else
                            {
                                tooltip.AppendLine("     - " + Lang.Get($"kemono:skinpart-{required.Key}-{variant}"));
                            }
                        }
                    }
                    tooltip.AppendLine();
                }

                if (race.BannedParts.Count > 0)
                {
                    tooltip.AppendLine("<i>" + Lang.Get($"kemono:race-banned-parts") + "</i>:");
                    foreach (var banned in race.BannedParts)
                    {
                        tooltip.AppendLine("   " + Lang.Get($"kemono:skinpart-{banned.Key}") + ":");
                        foreach (var variant in banned.Value)
                        {
                            tooltip.AppendLine("     - " + Lang.Get($"kemono:skinpart-{banned.Key}-{variant}"));
                        }
                    }
                    tooltip.AppendLine();
                }

                Composers[CHARACTER]
                    .AddImageButton(iconBounds, icon, (_) => { changeRace(i); return false; }, iconTint)
                    .AddInset(selectionBounds, insetDepth)
                    .AddInset(raceLabelBounds.FlatCopy(), 1) // need to .FloatCopy() because text modifies inset
                    .AddDynamicText(Lang.Get($"kemono:race-name-{race.Code}"), labelFont, raceLabelBounds)
                    .AddHoverText(tooltip.ToString(), CairoFont.WhiteDetailText(), raceTooltipWidth, iconBounds)
                ;
            }
            else // just insets
            {
                Composers[CHARACTER]
                    .AddInset(selectionBounds, insetDepth)
                    .AddInset(raceLabelBounds.FlatCopy(), 1) // need to .FlatCopy() because text modifies inset
                ;
            }

        }

        loadRaceDescription(SelectedRaceIndex);
    }

    /// Create tab panel for character class selection.
    private void ComposeTabClass()
    {
        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr.RunTesselation();
        
        // size of char selection name
        const int selectionHeight = 50;
        const int selectionBtnPad = 8;
        const int selectionBtnWidth = 60 - 2 * selectionBtnPad;
        const int selectionBtnOffset = 20;
        const int selectionTitleWidth = rightPanelWidth - 2 * selectionBtnWidth - 2 * selectionBtnOffset;

        ElementBounds prevButtonBounds = ElementBounds
            .Fixed(x0, yTop, selectionBtnWidth, selectionHeight)
            .WithFixedPadding(selectionBtnPad);

        ElementBounds centerTextBounds = ElementBounds
            .Fixed(0, yTop, selectionTitleWidth, selectionHeight + 8)
            .FixedRightOf(prevButtonBounds, 4 + selectionBtnPad + selectionBtnOffset); // wtf?
        ElementBounds charclasssInset = centerTextBounds.ForkBoundingParent(4, 4, 4, 4);

        ElementBounds nextButtonBounds = ElementBounds
            .Fixed(0, yTop, selectionBtnWidth, selectionHeight)
            .WithFixedPadding(selectionBtnPad)
            .FixedRightOf(charclasssInset, selectionBtnOffset);

        CairoFont font = CairoFont.WhiteMediumText();
        centerTextBounds.fixedY += (centerTextBounds.fixedHeight - font.GetFontExtents().Height / RuntimeEnv.GUIScale) / 2;

        ElementBounds charTextBounds = ElementBounds.Fixed(x0, 0, rightPanelWidth, 100).FixedUnder(prevButtonBounds, 32);

        ElementBounds confirmBtnBounds = ElementBounds
            .Fixed(-pad, -pad, 100, 20)
            .WithAlignment(EnumDialogArea.RightBottom)
            .WithFixedPadding(12, 6);

        Composers[CHARACTER]
            .AddIconButton("left", (on) => changeClass(-1), prevButtonBounds.FlatCopy())
            .AddInset(charclasssInset, 2)
            .AddDynamicText("Commoner", font.Clone().WithOrientation(EnumTextOrientation.Center), centerTextBounds, "className")
            .AddIconButton("right", (on) => changeClass(1), nextButtonBounds.FlatCopy())

            .AddRichtext("", CairoFont.WhiteDetailText(), charTextBounds, "characterDesc")
            .AddSmallButton(Lang.Get("Confirm Class"), OnConfirm, confirmBtnBounds, EnumButtonStyle.Normal)
        ;

        changeClass(0);
    }

    /// DEBUGGING TAB
    /// Default engine RenderEntityToGui flips x-axis
    /// This tab is for moving character render to figure out how
    /// to re-flip it properly >:^(
    private void ComposeTabDebugRender()
    {
        ElementBounds translateLabel = ElementBounds
            .Fixed(x0, y0, 200, 20);
        ElementBounds translateXBounds = ElementBounds
            .Fixed(x0, 0, 200, 20)
            .FixedUnder(translateLabel, 4);
        ElementBounds translateYBounds = ElementBounds
            .Fixed(x0, 0, 200, 20)
            .FixedUnder(translateXBounds, 4);
        ElementBounds translateZBounds = ElementBounds
            .Fixed(x0, 0, 200, 20)
            .FixedUnder(translateYBounds, 4);
        
        ElementBounds rotateLabel = ElementBounds
            .Fixed(x0, 0, 200, 20)
            .FixedUnder(translateZBounds, 8);
        ElementBounds rotateXBounds = ElementBounds
            .Fixed(x0, 0, 200, 20)
            .FixedUnder(rotateLabel, 4);
        
        ElementBounds scaleLabel = ElementBounds
            .Fixed(x0, 0, 200, 20)
            .FixedUnder(rotateXBounds, 8);
        ElementBounds scaleXBounds = ElementBounds
            .Fixed(x0, 0, 200, 20)
            .FixedUnder(scaleLabel, 4);
        
        Composers[CHARACTER]
            .AddRichtext("Translate", CairoFont.WhiteSmallText(), translateLabel)
            .AddSlider(
                (newVal) => { charRenderOffsetX = newVal; return true; },
                translateXBounds,
                "debugtranslatex"
            )
            .AddSlider(
                (newVal) => { charRenderOffsetY = newVal; return true; },
                translateYBounds,
                "debugtranslatey"
            )
            .AddSlider(
                (newVal) => { charRenderOffsetZ = newVal; return true; },
                translateZBounds,
                "debugtranslatez"
            )

            .AddRichtext("Rotate", CairoFont.WhiteSmallText(), rotateLabel)
            .AddSlider(
                (newVal) => { charRenderRotateX = (float) newVal; return true; },
                rotateXBounds,
                "debugrotatex"
            )

            .AddRichtext("Scale", CairoFont.WhiteSmallText(), scaleLabel)
            .AddSlider(
                (newVal) => { charRenderScaleX = ((float)newVal)/100f; return true; },
                scaleXBounds,
                "debugscalex"
            )
        ;

        Composers[CHARACTER].GetSlider("debugtranslatex").SetValues((int)charRenderOffsetX, -1000, 1000, 1);
        Composers[CHARACTER].GetSlider("debugtranslatey").SetValues((int)charRenderOffsetY, -1000, 1000, 1);
        Composers[CHARACTER].GetSlider("debugtranslatez").SetValues((int)charRenderOffsetZ, -1000, 1000, 1);
        Composers[CHARACTER].GetSlider("debugrotatex").SetValues((int) charRenderRotateX, -180, 180, 1);
        Composers[CHARACTER].GetSlider("debugscalex").SetValues((int)(charRenderScaleX * 100.0), -200, 200, 1);
    }

    public bool OnNext()
    {
        // handle manually because race tab may not exist
        switch (currTab)
        {
            case TAB_CHARACTER:
                currTab = TAB_EMBLEM;
                break;
            case TAB_EMBLEM:
                currTab = (modSys.Races.Count > 1) ? TAB_RACE : TAB_CLASS;
                break;
            case TAB_RACE:
                currTab = TAB_CLASS;
                break;
            default:
                currTab = TAB_CHARACTER;
                break;
        }
        ComposeGuis();
        return true;
    }

    public void onTabClicked(int tabid)
    {
        currTab = tabid;
        ComposeGuis();
    }

    public override void OnGuiOpened()
    {
        if (Entity is EntityPlayer entityPlayer)
        {
            string charclass = entityPlayer.WatchedAttributes.GetString("characterClass");
            if (charclass != null)
            {
                modSys.baseCharSys.setCharacterClass(entityPlayer, charclass, true);
            }
            else 
            {
                modSys.baseCharSys.setCharacterClass(entityPlayer, modSys.baseCharSys.characterClasses[0].Code, true);
            }
        }

        ComposeGuis();
    }


    public override void OnGuiClosed()
    {
        var ebhtc = Entity.GetBehavior<EntityBehaviorTexturedClothing>();
        if (ebhtc != null) ebhtc.hideClothing = false;

        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr.RunTesselation();

        if (OnGuiClosedCallback != null)
        {
            OnGuiClosedCallback(this);
        }
    }

    public bool OnConfirm()
    {
        // validate if selected race is valid for skin
        // if not, warn once
        KemonoRace race = modSys.Races[SelectedRaceIndex];
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        var skinparts = skin.GetSkinPartsTreeDictionary();
        if (skinparts.tree != null)
        {
            bool isRaceValid = KemonoRace.IsValid(race, skin.Model.Code, skinparts, Player);
            if (!isRaceValid && !warnedRaceInvalid)
            {
                warnedRaceInvalid = true;
                currTab = TAB_RACE;
                ComposeGuis();
                return true;
            }
        }

        DidSelect = true;
        TryClose();
        return true;
    }

    protected virtual void OnTitleBarClose()
    {
        TryClose();
    }

    public void OnNewScrollbarValue(float value)
    {
        var rightPanel = Composers[CHARACTER].GetContainer("rightpanel");
        if (rightPanel != null) {
            rightPanel.Bounds.fixedY = 3 - value;
            rightPanel.Bounds.CalcWorldBounds();
        }
    }

    public void GetLocalSkinFilenames()
    {
        string[] none = {""};
        string[] files = Directory
            .GetFiles(modSys.SaveDir, "*.json")
            .Select(Path.GetFileName)
            .Select(filename => filename.EndsWith(".json") ? filename.Substring(0, filename.Length - 5) : filename)
            .ToArray();
        loadSkinFilenames = none.Concat(files).ToArray();
    }

    public void UpdateSkinLoadFilenamesDropdown()
    {
        var filenamesDropdown = Composers[CHARACTER].GetDropDown("loadskinfilenamesdropdown");
        if (filenamesDropdown != null)
        {
            filenamesDropdown.SetSelectedIndex(0);
            filenamesDropdown.SetList(loadSkinFilenames, loadSkinFilenames);
        }
    }

    public void OnLoadSkinFilenameChange(string variantcode, bool selected)
    {
        loadSkinFilename = variantcode;
    }

    public bool OnLoadSkin()
    {
        if (loadSkinFilename == "") return false; // ignore

        bool result = modSys.SkinLoad(Entity, loadSkinFilename);

        // re-compose since most options may have changed
        if (result == true)
        {
            ComposeGuis();
        }
        
        return true;
    }

    public void OnSaveSkinInputChanged(string value)
    {
        saveSkinFilename = value;
    }

    public bool OnSaveSkin()
    {
        bool result = modSys.SkinSave(Entity, saveSkinFilename);

        // if save successful, reload load skin filenames dropdown list
        if (result == true)
        {
            GetLocalSkinFilenames();
            UpdateSkinLoadFilenamesDropdown();
        }

        return true;
    }

    public void GetLocalPaintingFilenames()
    {
        string[] none = {""};
        string[] files = Directory
            .GetFiles(modSys.SaveDir, "*.png")
            .Select(Path.GetFileName)
            .Select(filename => filename.EndsWith(".png") ? filename.Substring(0, filename.Length - 4) : filename)
            .ToArray();
        loadPaintingFilenames = none.Concat(files).ToArray();
    }

    public void UpdatePaintingLoadFilenamesDropdown()
    {
        var filenamesDropdown = Composers[CHARACTER].GetDropDown("loadpaintingfilenamesdropdown");
        if (filenamesDropdown != null)
        {
            filenamesDropdown.SetSelectedIndex(0);
            filenamesDropdown.SetList(loadPaintingFilenames, loadPaintingFilenames);
        }
    }

    public void OnLoadPaintingFilenameChange(string variantcode, bool selected)
    {
        loadPaintingFilename = variantcode;
    }

    public bool OnLoadPainting()
    {
        if (loadPaintingFilename == "" || selectedPainting == "") return false; // ignore

        bool result = modSys.PaintingLoad(Entity, selectedPainting, loadPaintingFilename);

        // re-compose since most options may have changed
        if (result == true)
        {
            ComposeGuis();
        }
        
        return true;
    }

    public void OnSavePaintingInputChanged(string value)
    {
        savePaintingFilename = value;
    }

    public bool OnSavePainting()
    {
        if (savePaintingFilename == "" || selectedPainting == "") return false; // ignore

        bool result = modSys.PaintingSave(Entity, selectedPainting, savePaintingFilename);

        // if save successful, reload load skin filenames dropdown list
        if (result == true)
        {
            GetLocalPaintingFilenames();
            UpdatePaintingLoadFilenamesDropdown();
        }

        return true;
    }

    public void OnToggleDressOnOff(bool on)
    {
        charNaked = !on;

        var ebhtc = Entity.GetBehavior<EntityBehaviorTexturedClothing>();
        if (ebhtc != null) ebhtc.hideClothing = charNaked;

        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr.RunTesselation();
    }

    public void OnSelectModel(string modelname, bool selected)
    {
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.SetModel(modelname);

        // clear selected painting
        selectedPainting = "";

        ComposeGuis(); // re-compose, selection options will change
    }

    public void OnSelectBuiltinSkinPreset(string presetCode, bool selected)
    {
        LoadBuiltinSkin = presetCode;
    }

    public bool OnLoadBuiltinSkinPreset()
    {
        if (LoadBuiltinSkin == "") return false; // ignore

        if (modSys.SkinPresetsByCode.TryGetValue(LoadBuiltinSkin, out KemonoSkinnableModelPreset preset))
        {
            var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
            skin.LoadPreset(preset);
            ComposeGuis(); // re-compose, selection options will change
        }

        return true;
    }

    public void OnSetColorSliderModeHSV(bool useHSV)
    {
        if (useHSV != ColorSliderModeHSV)
        {
            ColorSliderModeHSV = useHSV;
            ComposeGuis(); // all sliders will change, need to re-compose
        }
        else
        {
            // no change in mode, just force state in toggle buttons
            if (CharacterPanel != null)
            {
                CharacterPanel.GetElementAs<GuiElementToggleButton>("color-mode-btn-rgb").SetValue(!ColorSliderModeHSV);
                CharacterPanel.GetElementAs<GuiElementToggleButton>("color-mode-btn-hsv").SetValue(ColorSliderModeHSV);
            }
        }
    }

    public bool OnPartScaleReset()
    {
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.ClearModelPartScale(false);
        ComposeGuis(); // this will re-tesselate shape
        return true;
    }

    public void OnPartScaleChange(string partCode, double scale)
    {
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.SetModelPartScale(partCode, scale);
    }

    public bool OnPartEyeHeightReset()
    {
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.SetEyeHeightOffset(0.0);
        ComposeGuis(); // this will re-tesselate shape
        return true;
    }

    public void OnPartEyeHeightChange(double eyeHeightOffset)
    {
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.SetEyeHeightOffset(eyeHeightOffset);
    }
    
    public void OnPartColorChange(string partCode, int color)
    {
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.SetSkinPartColor(partCode, color);
    }

    public void OnSelectSkinPart(string partCode, string variantCode)
    {
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.SelectSkinPart(partCode, variantCode);
    }

    public bool OnRandomizeSkin()
    {
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.RandomizeSkinParts();

        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr.RunTesselation();

        ComposeGuis();

        return true;
    }

    // ====================================================================
    // PAINTING TAB
    // ====================================================================

    public void OnSelectPainting(string paintingName, bool selected)
    {
        selectedPainting = paintingName;
        ComposeGuis(); // re-compose, selection options will change
    }

    public void UpdateRgbaFromHsv()
    {
        // calculate rgb from hsv
        var rgb = KemonoColorUtil.HsvToRgb(this.colHue, this.colSat, this.colVal);
        this.colRed255 = rgb.r;
        this.colGreen255 = rgb.g;
        this.colBlue255 = rgb.b;
        this.colRed = rgb.r / 255.0;
        this.colGreen = rgb.g / 255.0;
        this.colBlue = rgb.b / 255.0;
        this.colRGBA = ColorUtil.ColorFromRgba(colRed255, colGreen255, colBlue255, 255); // r,g,b,a
    }

    public void UpdateHsvFromRgb()
    {
        // get normalized rgb
        this.colRed = this.colRed255 / 255.0;
        this.colGreen = this.colGreen255 / 255.0;
        this.colBlue = this.colBlue255 / 255.0;

        // calculate rgb from hsv
        var hsv = KemonoColorUtil.RgbToHsv(this.colRed, this.colGreen, this.colBlue);
        this.colHue = hsv.h;
        this.colSat = hsv.s;
        this.colVal = hsv.v;
    }
    
    public bool OnPainterHueChange(double newHue)
    {
        if ( newHue == this.colHue ) return false; // no change

        this.colHue = newHue;
        UpdateRgbaFromHsv();

        // set sat/val color selector rectangle
        Composers[CHARACTER].GetColorSatValSelector("satvalselector")?.SetHue(this.colHue);
        
        // set rgb box inputs
        // TODO

        // set canvas brush color
        Composers[CHARACTER].GetCanvas("canvas")?.SetBrushColor(this.colRGBA);

        return true;
    }

    public bool OnPainterSatValChange(double newSat, double newVal)
    {
        if ( newSat == this.colSat && newVal == this.colVal ) return false; // no change

        this.colSat = newSat;
        this.colVal = newVal;
        UpdateRgbaFromHsv();

        // set rgb box inputs
        // TODO

        // set canvas brush color
        Composers[CHARACTER].GetCanvas("canvas")?.SetBrushColor(this.colRGBA);
        
        return true;
    }

    public bool OnCanvasPaint()
    {
        int[] pixels = Composers[CHARACTER].GetCanvas("canvas").GetPixels();
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        skin.SetPaintingPixels(selectedPainting, pixels);

        // re-render character
        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr.RunTesselation();

        return true;
    }

    public bool OnCanvasSelectColor(int color)
    {
        var rgb = KemonoColorUtil.ToRgb(color);
        this.colRed255 = rgb.r;
        this.colGreen255 = rgb.g;
        this.colBlue255 = rgb.b;
        this.colRGBA = ColorUtil.ColorFromRgba(rgb.r, rgb.g, rgb.b, 255); // r,g,b,a
        UpdateHsvFromRgb();

        // set hue/sat/val color selectors
        Composers[CHARACTER].GetColorHueSlider("hueslider")?.SetHue(this.colHue);
        Composers[CHARACTER].GetColorSatValSelector("satvalselector")?.SetHueSatVal(this.colHue, this.colSat, this.colVal);

        return true;
    }

    public bool OnSetCanvasBackgroundColor(bool useBackground, int backgroundColor)
    {
        this.canvasBackgroundColor = backgroundColor;

        switch (canvasBackgroundColor)
        {
            case CANVAS_COLOR_CLEAR:
                Composers[CHARACTER].GetToggleButton("canvas-background-color-clear").SetValue(true);
                Composers[CHARACTER].GetToggleButton("canvas-background-color-white").SetValue(false);
                Composers[CHARACTER].GetToggleButton("canvas-background-color-black").SetValue(false);
                break;
            case CANVAS_COLOR_WHITE:
                Composers[CHARACTER].GetToggleButton("canvas-background-color-clear").SetValue(false);
                Composers[CHARACTER].GetToggleButton("canvas-background-color-white").SetValue(true);
                Composers[CHARACTER].GetToggleButton("canvas-background-color-black").SetValue(false);
                break;
            case CANVAS_COLOR_BLACK:
                Composers[CHARACTER].GetToggleButton("canvas-background-color-clear").SetValue(false);
                Composers[CHARACTER].GetToggleButton("canvas-background-color-white").SetValue(false);
                Composers[CHARACTER].GetToggleButton("canvas-background-color-black").SetValue(true);
                break;
            default: // clear
                Composers[CHARACTER].GetToggleButton("canvas-background-color-clear").SetValue(true);
                Composers[CHARACTER].GetToggleButton("canvas-background-color-white").SetValue(false);
                Composers[CHARACTER].GetToggleButton("canvas-background-color-black").SetValue(false);
                break;
        }

        Composers[CHARACTER].GetCanvas("canvas").SetBackground(useBackground, backgroundColor);
        
        // set painting cursor color based on background color
        int cursorColor;
        switch (canvasBackgroundColor)
        {
            case CANVAS_COLOR_CLEAR:
                cursorColor = CANVAS_COLOR_WHITE;
                break;
            case CANVAS_COLOR_WHITE:
                cursorColor = CANVAS_COLOR_BLACK;
                break;
            case CANVAS_COLOR_BLACK:
                cursorColor = CANVAS_COLOR_WHITE;
                break;
            default: // clear
                cursorColor = CANVAS_COLOR_BLACK;
                break;
        }

        Composers[CHARACTER].GetCanvas("canvas").SetCursorColor(cursorColor);

        return true;
    }

    public bool OnClearPainting()
    {
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();

        // clear pixels and re-tessellate
        // I HAVE NO IDEA WHY THIS WORKS (?) LOL LMAO
        skin.ClearPaintingPixels(selectedPainting, 0, 0, 0, 0);
        skin.ClearPaintingTextures();

        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr?.RunTesselation();

        // recompose after painting cleared
        ComposeGuis();

        return true;
    }

    // ====================================================================
    // Race TAB
    // ====================================================================

    void changeRacePaginationPage(int dir)
    {
        if (modSys.Races.Count > 0)
        {
            int pages = 1 + ((modSys.Races.Count - 1) / RACES_PER_PAGE);
            SelectedRacePaginationPage = GameMath.Mod(SelectedRacePaginationPage + dir, pages);
        }
        else
        {
            SelectedRacePaginationPage = 0;
        }

        ComposeGuis();
    }

    void changeRace(int index)
    {
        SelectedRaceIndex = GameMath.Mod(index, modSys.Races.Count);
        ComposeGuis();
    }

    void loadRaceDescription(int index)
    {            
        if (index < 0 || index > modSys.Races.Count) return;

        KemonoRace race = modSys.Races[index];

        Composers[CHARACTER].GetRichtext("raceName").SetNewText(Lang.Get("kemono:race-name-" + race.Code), CairoFont.WhiteSmallishText());

        StringBuilder fulldesc = new StringBuilder();
        StringBuilder attributes = new StringBuilder();

        fulldesc.AppendLine(Lang.Get("kemono:race-desc-" + race.Code));
        fulldesc.AppendLine();
        fulldesc.AppendLine(Lang.Get("game:traits-title"));

        var traits = race.Traits.Select(code => modSys.RaceTraitsByCode[code]).OrderBy(trait => (int)trait.Type);

        if (race.Traits.Length > 0)
        {
            foreach (var trait in traits) 
            {
                attributes.Clear();
                foreach (var val in trait.Attributes)
                {
                    if (attributes.Length > 0) attributes.Append(", ");
                    attributes.Append(Lang.Get(string.Format(GlobalConstants.DefaultCultureInfo, "kemono:charattribute-{0}-{1}", val.Key, val.Value)));
                }

                if (attributes.Length > 0)
                {
                    fulldesc.AppendLine(Lang.Get("game:traitwithattributes", Lang.Get("kemono:trait-" + trait.Code), attributes));
                }
                else
                {
                    string desc = Lang.GetIfExists("kemono:traitdesc-" + trait.Code);
                    if (desc != null)
                    {
                        fulldesc.AppendLine(Lang.Get("game:traitwithattributes", Lang.Get("kemono:trait-" + trait.Code), desc));
                    }
                    else
                    {
                        fulldesc.AppendLine(Lang.Get("kemono:trait-" + trait.Code));
                    }
                }
            }
        }
        else
        {
            fulldesc.AppendLine(Lang.Get("No positive or negative traits"));
        }

        Composers[CHARACTER].GetRichtext("raceDesc").SetNewText(fulldesc.ToString(), CairoFont.WhiteDetailText());
    }

    // ====================================================================
    // CLASS TAB
    // ====================================================================

    void changeClass(int dir)
    {
        SelectedClassIndex = GameMath.Mod(SelectedClassIndex + dir, modSys.baseCharSys.characterClasses.Count);

        CharacterClass chclass = modSys.baseCharSys.characterClasses[SelectedClassIndex];
        Composers[CHARACTER].GetDynamicText("className").SetNewText(Lang.Get("characterclass-" + chclass.Code));

        StringBuilder fulldesc = new StringBuilder();
        StringBuilder attributes = new StringBuilder();

        fulldesc.AppendLine(Lang.Get("characterdesc-" + chclass.Code));
        fulldesc.AppendLine();
        fulldesc.AppendLine(Lang.Get("traits-title"));

        var chartraits = chclass.Traits.Select(code => modSys.baseCharSys.TraitsByCode[code]).OrderBy(trait => (int)trait.Type);

        foreach (var trait in chartraits) 
        {
            attributes.Clear();
            foreach (var val in trait.Attributes)
            {
                if (attributes.Length > 0) attributes.Append(", ");
                attributes.Append(Lang.Get(string.Format(GlobalConstants.DefaultCultureInfo, "charattribute-{0}-{1}", val.Key, val.Value)));
            }

            if (attributes.Length > 0)
            {
                fulldesc.AppendLine(Lang.Get("traitwithattributes", Lang.Get("trait-" + trait.Code), attributes));
            }
            else
            {
                string desc = Lang.GetIfExists("traitdesc-" + trait.Code);
                if (desc != null)
                {
                    fulldesc.AppendLine(Lang.Get("traitwithattributes", Lang.Get("trait-" + trait.Code), desc));
                }
                else
                {
                    fulldesc.AppendLine(Lang.Get("trait-" + trait.Code));
                }
            }
        }

        if (chclass.Traits.Length == 0)
        {
            fulldesc.AppendLine(Lang.Get("No positive or negative traits"));
        }

        Composers[CHARACTER].GetRichtext("characterDesc").SetNewText(fulldesc.ToString(), CairoFont.WhiteDetailText());

        if (Entity is EntityPlayer entityPlayer)
        {
            modSys.baseCharSys.setCharacterClass(entityPlayer, chclass.Code, true);
        }

        var esr = Entity.Properties.Client.Renderer as IKemonoRenderer;
        esr.RunTesselation();
    }

    
    public void PrepAndOpen()
    {
        GatherDresses(EnumCharacterDressType.Foot);
        GatherDresses(EnumCharacterDressType.Hand);
        GatherDresses(EnumCharacterDressType.Shoulder);
        GatherDresses(EnumCharacterDressType.UpperBody);
        GatherDresses(EnumCharacterDressType.LowerBody);
        TryOpen();
    }

    public void GatherDresses(EnumCharacterDressType type)
    {
        List<ItemStack> dresses = new List<ItemStack>();
        dresses.Add(null);

        string stringtype = type.ToString().ToLowerInvariant();

        IList<Item> items = capi.World.Items;

        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            if (item == null || item.Code == null || item.Attributes == null) continue;

            string clothcat = item.Attributes["clothescategory"]?.AsString();
            bool allow = item.Attributes["inCharacterCreationDialog"]?.AsBool() == true;

            if (allow && clothcat?.ToLowerInvariant() == stringtype)
            {
                dresses.Add(new ItemStack(item));
            }
        }

        DressesByDressType[type] = dresses.ToArray();
        DressPositionByTressType[type] = 0;
    }


    public override bool CaptureAllInputs()
    {
        return IsOpened();
    }


    public override string ToggleKeyCombinationCode
    {
        get { return null; }
    }
    

    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        if (charRenderBounds.PointInside(capi.Input.MouseX, capi.Input.MouseY) && (currTab == TAB_CHARACTER || currTab == TAB_EMBLEM || currTab == TAB_DEBUG_RENDER))
        {
            charZoom = GameMath.Clamp(charZoom + args.deltaPrecise / 10f, 0.5f, 1f);
            args.SetHandled(true); // prevents further scroll event handling
        }
        base.OnMouseWheel(args);
    }

    public override bool PrefersUngrabbedMouse => true;


    #region Character render 
    protected float yaw = -GameMath.PIHALF + 0.3f;
    protected bool rotateCharacter;
    public override void OnMouseDown(MouseEvent args)
    {
        base.OnMouseDown(args);

        rotateCharacter = charRenderBounds.PointInside(args.X, args.Y);
    }

    public override void OnMouseUp(MouseEvent args)
    {
        base.OnMouseUp(args);

        rotateCharacter = false;
    }

    public override void OnMouseMove(MouseEvent args)
    {
        base.OnMouseMove(args);

        if (rotateCharacter) yaw += args.DeltaX / 100f;
    }


    Vec4f lightPos = new Vec4f(-1, -1, 0, 0).NormalizeXYZ();
    Matrixf mat = new Matrixf();

    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);

        if (capi.IsGamePaused)
        {
            (Entity as EntityPlayer)?.talkUtil.OnGameTick(deltaTime);
        }
        
        capi.Render.GlPushMatrix();

        // charRenderScaleX is setup to flip -X axis
        // because default RenderEntityToGui is inverted relative to game rendering
        capi.Render.GlScale(charRenderScaleX, 1f, 1f);
        capi.Render.GlRotate(charRenderRotateX, 1, 0, 0);
        capi.Render.GlTranslate(charRenderOffsetX, charRenderOffsetY, charRenderOffsetZ);

        mat.Identity();
        mat.RotateXDeg(-14);
        Vec4f lightRot = mat.TransformVector(lightPos);
        double pad = GuiElement.scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);

        capi.Render.CurrentActiveShader.Uniform("lightPosition", new Vec3f(lightRot.X, lightRot.Y, lightRot.Z));

        double renderZoom;
        double renderYOffset;
        if (currTab == TAB_CHARACTER)
        {
            renderZoom = charZoom;
            renderYOffset = -220;
        }
        else if (currTab == TAB_EMBLEM)
        {
            renderZoom = charZoom;
            renderYOffset = -170;
        }
        else if (currTab == TAB_DEBUG_RENDER)
        {
            renderZoom = charZoom;
            renderYOffset = -170;
        }
        else
        {
            renderZoom = 0.5;
            renderYOffset = -170;
        }

        // get character model specific y render height offset
        var skin = Entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
        if (skin != null)
        {
            renderYOffset += skin.Model.GuiRenderHeightOffset;
        }

        capi.Render.PushScissor(charRenderBounds);

        // render character into left inset
        capi.Render.RenderEntityToGui(
            deltaTime,
            Entity,
            -charRenderBounds.renderX - pad - GuiElement.scaled(460) * renderZoom - GuiElement.scaled(115 * (1-renderZoom)), // pos x
            charRenderBounds.renderY + pad + GuiElement.scaled(renderYOffset), // pos y (doesn't matter?)
            // charRenderBounds.renderY + pad + GuiElement.scaled(10 * (1 - renderZoom)), // pos y
            (float) GuiElement.scaled(1000), // pos z (must be large otherwise object clips with far plane)
            yaw, // yaw delta
            (float) GuiElement.scaled(330 * renderZoom), // size
            ColorUtil.WhiteArgb); // color
        
        capi.Render.PopScissor();

        capi.Render.CurrentActiveShader.Uniform("lightPosition", new Vec3f(1, -1, 0).Normalize());

        capi.Render.GlPopMatrix();
    }
    #endregion


    public override float ZSize
    {
        get { return (float)GuiElement.scaled(280); }
    }
}
