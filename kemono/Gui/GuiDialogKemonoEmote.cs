using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace kemono;

public class GuiDialogKemonoEmote : GuiDialog
{
    public override string ToggleKeyCombinationCode => null;

    // reference to mod
    private readonly KemonoMod kemono;

    // sizing and positioning values for each emote gui container
    public int IconSize = 70; // emote icon size
    public int IconPadding = 5; // padding between icons
    public int IconRows = 8; // number of rows of icons
    public int IconCols = 3; // number of columns of icons
    public int IconOuterMargin = 5; // outer margin around the emote grid
    public int ScrollbarWidth = 16; // width of the scrollbar
    public int ScrollbarMargin = 4; // margin to scrollbar
    public int EmoteGuiCenterOffset = 60; // additional offset from center of screen
    public int MaxHoverTextWidth = 200;

    // constant names of composed elements
    public const string EMOTE_LEFT = "kemonoemoteleft";
    public const string EMOTE_LEFT_CONTAINER = "kemonoemoteleftcontainer";
    public const string EMOTE_LEFT_SCROLLBAR = "kemonoemoteleftscrollbar";
    public const string EMOTE_RIGHT = "kemonoemoteright";
    public const string EMOTE_RIGHT_CONTAINER = "kemonoemoterightcontainer";
    public const string EMOTE_RIGHT_SCROLLBAR = "kemonoemoterightscrollbar";

    // references to composed elements
    private ElementBounds emoteLeftBounds;
    private GuiComposer emoteLeft;
    private ElementBounds emoteRightBounds;
    private GuiComposer emoteRight;

    // entity and skin behavior this is attached to
    private Entity Entity;
    private EntityBehaviorKemonoSkinnable Skin;

    public static TextBackground HoverTextBackground = new TextBackground
    {
        Padding = 5,
        Radius = 1.0,
        FillColor = new double[] { 0, 0, 0, 0.8 }, // dark gray
        BorderColor = GuiStyle.DialogBorderColor,
        BorderWidth = 2.0,
        Shade = true
    };

    public GuiDialogKemonoEmote(ICoreClientAPI capi, KemonoMod kemono) : base(capi)
    {
        this.kemono = kemono;
    }

    public void Initialize(Entity entity)
    {
        Entity = entity;
        Skin = entity.GetBehavior<EntityBehaviorKemonoSkinnable>();
    }

    public void Recompose()
    {
        // contains just emotes icon grid, without margin at edges
        int DialogInnerWidth = IconCols * IconSize + (IconCols - 1) * IconPadding;
        int DialogInnerHeight = IconRows * IconSize + (IconRows - 1) * IconPadding;

        ElementBounds dialogBoundsLeft = ElementBounds
            .Fixed(EnumDialogArea.CenterMiddle, -DialogInnerWidth - EmoteGuiCenterOffset, 0.0, DialogInnerWidth, DialogInnerHeight);

        ElementBounds dialogBoundsRight = ElementBounds
            .Fixed(EnumDialogArea.CenterMiddle, DialogInnerWidth + EmoteGuiCenterOffset, 0.0, DialogInnerWidth, DialogInnerHeight);

        // sort emotes into left and right containers
        List<KemonoEmote> emotesLeft = new List<KemonoEmote>();
        List<KemonoEmote> emotesRight = new List<KemonoEmote>();
        if (Skin?.Model != null)
        {
            foreach (var emote in Skin.Model.Emotes)
            {
                switch (emote.Gui)
                {
                    case KemonoEmoteGuiPosition.Left:
                        emotesLeft.Add(emote);
                        break;
                    case KemonoEmoteGuiPosition.Right:
                        emotesRight.Add(emote);
                        break;
                    case KemonoEmoteGuiPosition.Default:
                        // default behavior: emotes with animations go to right, others to left
                        if (emote.Animation != null)
                            emotesRight.Add(emote);
                        else
                            emotesLeft.Add(emote);
                        break;
                }
            }
        }

        ComposeEmoteGuiSelection(
            EMOTE_LEFT,
            EMOTE_LEFT_CONTAINER,
            EMOTE_LEFT_SCROLLBAR,
            emotesLeft,
            dialogBoundsLeft,
            true,
            OnNewScrollbarLeftValue
        );

        ComposeEmoteGuiSelection(
            EMOTE_RIGHT,
            EMOTE_RIGHT_CONTAINER,
            EMOTE_RIGHT_SCROLLBAR,
            emotesRight,
            dialogBoundsRight,
            false,
            OnNewScrollbarRightValue
        );

        // TODO: bottom or top gui to reset all emotes

        // store references to composed elements
        emoteLeft = Composers[EMOTE_LEFT];
        emoteLeftBounds = Composers[EMOTE_LEFT].Bounds;
        emoteRight = Composers[EMOTE_RIGHT];
        emoteRightBounds = Composers[EMOTE_RIGHT].Bounds;
    }

    /// <summary>
    /// Composse an emote gui element containing a scrollable grid of square
    /// emote icons, with a scrollbar on left or right side.
    /// Full emote gui contains multiple of these containers
    /// (left and right).
    /// </summary>
    /// <param name="name"></param>
    /// <param name="outerBounds"></param>
    /// <param name=""></param>
    public void ComposeEmoteGuiSelection(
        string name,
        string nameContainer,
        string nameScrollbar,
        List<KemonoEmote> emotes,
        ElementBounds dialogInnerBounds,
        bool leftScrollbar,
        Action<float> OnNewScrollbarValue
    ) {
        int rightSpacing = IconOuterMargin;
        int leftSpacing = IconOuterMargin;
        if (leftScrollbar)
        {
            leftSpacing += ScrollbarWidth + ScrollbarMargin;
        }
        else
        {
            rightSpacing += ScrollbarWidth + ScrollbarMargin;
        }

        // gui outer bounds
        ElementBounds outerBounds = dialogInnerBounds
            .ForkBoundingParent(leftSpacing, IconOuterMargin, rightSpacing, IconOuterMargin)
            .WithAlignment(EnumDialogArea.CenterMiddle);

        // inner bounds for children
        ElementBounds innerBounds = ElementBounds.Fixed(0, 0, outerBounds.fixedWidth, outerBounds.fixedHeight);

        ElementBounds emoteBounds = innerBounds.ForkChild()
            .WithAlignment(EnumDialogArea.LeftTop)
            .WithFixedOffset(leftScrollbar ? leftSpacing : IconOuterMargin, IconOuterMargin)
            .WithFixedSize(dialogInnerBounds.fixedWidth, dialogInnerBounds.fixedHeight);

        ElementBounds clippingBounds = emoteBounds.ForkBoundingParent();
        ElementBounds insetBounds = emoteBounds
            .FlatCopy()
            .FixedGrow(2 * IconOuterMargin)
            .WithFixedOffset(-IconOuterMargin, -IconOuterMargin);
        ElementBounds scrollbarBounds = innerBounds.ForkChild()
            .WithAlignment(leftScrollbar ? EnumDialogArea.LeftTop : EnumDialogArea.RightTop)
            .WithFixedSize(ScrollbarWidth, insetBounds.fixedHeight);

        ElementBounds containerBounds = clippingBounds.ForkContainingChild();

        // NOTE: vanilla will crash when adding container with no elements
        // due to a tyrone moment:
        // when clicking because OnMouseDown handler does an array access
        // into an empty array in the container causing game crash
        // solution is just to always make sure model has an emote which is
        // less annoying than adding logic to handle empty container here.

        Composers[name] = capi.Gui
            .CreateCompo(name, outerBounds)
            // .AddShadedDialogBG(ElementBounds.Fill, false, 1.0, 0.1f)
            .BeginChildElements(innerBounds)
            .AddInset(insetBounds, 3)
            .BeginClip(clippingBounds)
            .AddContainer(containerBounds, nameContainer)
            .EndClip()
            .AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, nameScrollbar)
            .EndChildElements()
        ;

        var container = Composers[name].GetContainer(nameContainer);

        // add emote icon buttons
        for (int i = 0; i < emotes.Count; i++)
        {
            var emote = emotes[i];
            bool active = Skin.ActiveEmotes.Any(x => x.Code == emote.Code);

            int row = i / IconCols;
            int col = i % IconCols;
            int x = col * (IconSize + IconPadding);
            int y = row * (IconSize + IconPadding);

            var gridItemBounds = ElementBounds.Fixed(EnumDialogArea.LeftTop, x, y, IconSize, IconSize);
            container.Add(new GuiElementImageButton(
                capi,
                gridItemBounds,
                emote.Icon,
                (selected) =>
                {
                    if (selected)
                    {
                        kemono.ClientEmoteStopAndBroadcast(Entity, emote.Code);
                    }
                    else
                    {
                        kemono.ClientEmoteStartAndBroadcast(Entity, emote.Code);
                    }
                    return !selected;
                },
                null,
                new float[] { 0.7f, 0.7f, 0.7f, 0.5f }, // white highlight tint
                selected: active
            ));

            // if no icon, add text overlay on button
            if (emote.Icon == null)
            {
                string label = null;
                if (emote.Name != null) label = emote.Name;
                else if (emote.Code != null) label = emote.Code;

                if (label != null)
                {
                    // Place label in the center of the button
                    var labelFont = CairoFont.WhiteSmallText();
                    var labelBounds = gridItemBounds.CopyOffsetedSibling(4, 4)
                        .WithFixedSize(IconSize, IconSize)
                        .WithAlignment(EnumDialogArea.LeftTop);

                    container.Add(new GuiElementRichtext(
                        capi,
                        VtmlUtil.Richtextify(capi, label, labelFont),
                        labelBounds
                    ));
                }
            }

            // make hover text the emote name
            if (emote.Name != null)
            {
                container.Add(new GuiElementHoverText(
                    capi,
                    $"{emote.Name}",
                    CairoFont.WhiteSmallText(),
                    MaxHoverTextWidth,
                    gridItemBounds,
                    HoverTextBackground
                ));
            }
        }

        container.CalcTotalHeight();
        container.Bounds.CalcWorldBounds();
        clippingBounds.CalcWorldBounds();

        var scrollbar = Composers[name].GetScrollbar(nameScrollbar);
        scrollbar.Bounds.CalcWorldBounds();
        scrollbar.SetHeights(
            (float)clippingBounds.fixedHeight,
            (float)containerBounds.fixedHeight
        );

        // render
        Composers[name].Compose();
    }

    public void OnNewScrollbarLeftValue(float value)
    {
        ElementBounds bounds = Composers[EMOTE_LEFT].GetContainer(EMOTE_LEFT_CONTAINER).Bounds;
        bounds.fixedY = 0 - value;
        bounds.CalcWorldBounds();
    }

    public void OnNewScrollbarRightValue(float value)
    {
        ElementBounds bounds = Composers[EMOTE_RIGHT].GetContainer(EMOTE_RIGHT_CONTAINER).Bounds;
        bounds.fixedY = 0 - value;
        bounds.CalcWorldBounds();
    }

    /// <summary>
    /// Route scroll wheel to the left or right emote container.
    /// </summary>
    /// <param name="args"></param>
    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        if (emoteLeftBounds?.PointInside(capi.Input.MouseX, capi.Input.MouseY) == true)
        {
            emoteLeft?.OnMouseWheel(args);
            args.SetHandled(true);
        }
        else if (emoteRightBounds?.PointInside(capi.Input.MouseX, capi.Input.MouseY) == true)
        {
            emoteRight?.OnMouseWheel(args);
            args.SetHandled(true);
        }
    }

    public override void OnGuiOpened()
    {
        Recompose();
    }
}
