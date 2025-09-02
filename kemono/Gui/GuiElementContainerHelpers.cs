using Cairo;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace kemono;

/// Implements GuiComposer like methods for GuiElementContainer.
/// Follow same api formats as GuiComposerHelpers:
/// https://apidocs.vintagestory.at/api/Vintagestory.API.Client.GuiComposerHelpers.html
public class GuiElementContainerWrapper
{
    public ICoreClientAPI api { get; protected set; }
    public GuiElementContainer container { get; protected set; }
    
    // map key => element
    public Dictionary<string, GuiElement> elements { get; protected set; }


    public GuiElementContainerWrapper(ICoreClientAPI capi, GuiElementContainer container)
    {
        this.api = capi;
        this.container = container;
        this.elements = new Dictionary<string, GuiElement>();
    }

    /// Wrapper for adding GuiElement to container and elements map.
    public GuiElementContainerWrapper AddGuiElement(string key, GuiElement element)
    {
        container.Add(element);
        if (key != null) elements[key] = element;
        return this;
    }

    public GuiElementContainerWrapper AddInset(ElementBounds bounds, int depth = 4, float brightness = 0.85f, string key = null)
    {
        return AddGuiElement(key, new GuiElementInsetCustom(api, bounds, depth, brightness));
    }

    public GuiElementContainerWrapper AddPadding(ElementBounds bounds, string key = null)
    {
        return AddGuiElement(key, new GuiElementPadding(api, bounds));
    }

    public GuiElementContainerWrapper AddElementListPicker<T>(Type pickertype, T[] elems, Action<int> onToggle, ElementBounds startBounds, int maxLineWidth, string key)
    {
        if (key == null) key = "elementlistpicker";

        int quantityButtons = elems.Length;
        double lineWidth = 0;

        for (int i = 0; i < elems.Length; i++)
        {
            int index = i;

            if (lineWidth > maxLineWidth)
            {
                startBounds.fixedX -= lineWidth;
                startBounds.fixedY += startBounds.fixedHeight + 5;
                lineWidth = 0;
            }

            var elem = Activator.CreateInstance(pickertype, api, elems[i], startBounds.FlatCopy()) as GuiElement;
            this.AddGuiElement(key + "-" + i, elem);

            (elements[key + "-" + i] as GuiElementElementListPickerBase<T>).handler = (on) =>
            {
                if (on)
                {
                    onToggle(index);
                    for (int j = 0; j < quantityButtons; j++)
                    {
                        if (j == index) continue;
                        (elements[(key + "-" + j)] as GuiElementElementListPickerBase<T>).SetValue(false);
                    }
                }
                else
                {
                    (elements[key + "-" + index] as GuiElementElementListPickerBase<T>).SetValue(true);
                }
            };

            startBounds.fixedX += startBounds.fixedWidth + 5;

            lineWidth += startBounds.fixedWidth + 5;
        }

        return this;
    }

    public GuiElementContainerWrapper AddColorListPicker(int[] colors, Action<int> onToggle, ElementBounds startBounds, int maxLineWidth, string key = null)
    {
        return this.AddElementListPicker<int>(typeof(GuiElementColorListPicker), colors, onToggle, startBounds, maxLineWidth, key);
    }

    public GuiElementContainerWrapper AddDropDown(string[] values, string[] names, int selectedIndex, SelectionChangedDelegate onSelectionChanged, ElementBounds bounds, string key = null)
    {
        var elem = new GuiElementDropDown(api, values, names, selectedIndex, onSelectionChanged, bounds, CairoFont.WhiteSmallText(), false);
        return this.AddGuiElement(key, new GuiElementDropDown(
            api,
            values,
            names,
            selectedIndex,
            onSelectionChanged,
            bounds,
            CairoFont.WhiteSmallText(),
            false
        ));
    }

    public GuiElementContainerWrapper AddHoverText(string text, CairoFont font, int width, ElementBounds bounds, string key = null)
    {
        return this.AddGuiElement(key, new GuiElementHoverText(api, text, font, width, bounds, null));
    }

    public GuiElementContainerWrapper AddHoverText(string text, CairoFont font, int width, ElementBounds bounds, TextBackground background, string key = null)
    {
        return this.AddGuiElement(key, new GuiElementHoverText(api, text, font, width, bounds, background));
    }
    
    public GuiElementContainerWrapper AddRichtext(string vtmlCode, CairoFont baseFont, ElementBounds bounds, string key = null)
    {
        return this.AddGuiElement(key, new GuiElementRichtext(
            api,
            VtmlUtil.Richtextify(api, vtmlCode, baseFont),
            bounds
        ));
    }

    public GuiElementContainerWrapper AddRichtext(string vtmlCode, CairoFont baseFont, ElementBounds bounds, Action<LinkTextComponent> didClickLink, string key = null)
    {
        return this.AddGuiElement(key, new GuiElementRichtext(
            api,
            VtmlUtil.Richtextify(api, vtmlCode, baseFont, didClickLink),
            bounds
        ));
    }

    public GuiElementContainerWrapper AddTextInput(ElementBounds bounds, Action<string> OnTextChanged, CairoFont font = null, string placeholder = null, string key = null)
    {
        if (font == null)
        {
            font = CairoFont.TextInput();
        }

        var elem = new GuiElementTextInputCustom(
            api,
            bounds,
            OnTextChanged,
            font
        );

        if (placeholder != null)
        {
            elem.SetPlaceHolderText(placeholder);
        }

        return this.AddGuiElement(key, elem);
    }

    public GuiElementContainerWrapper AddButton(string text, ActionConsumable onClick, ElementBounds bounds, EnumButtonStyle style = EnumButtonStyle.Normal, string key = null)
    {
        GuiElementTextButton elem = new GuiElementTextButton(api, text, CairoFont.ButtonText(), CairoFont.ButtonPressedText(), onClick, bounds, style);
        elem.SetOrientation(CairoFont.ButtonText().Orientation);
        return this.AddGuiElement(key, elem);
    }

    public GuiElementContainerWrapper AddSmallButton(string text, ActionConsumable onClick, ElementBounds bounds, EnumButtonStyle style = EnumButtonStyle.Normal, string key = null)
    {
        CairoFont font1 = CairoFont.ButtonText();
        CairoFont font2 = CairoFont.ButtonPressedText();
        font1.Fontname = GuiStyle.StandardFontName;
        font2.Fontname = GuiStyle.StandardFontName;
        if (style != EnumButtonStyle.Small)
        {
            font1.FontWeight = FontWeight.Bold;
            font2.FontWeight = FontWeight.Bold;
        } else
        {
            font1.FontWeight = FontWeight.Normal;
            font2.FontWeight = FontWeight.Normal;
        }
        font1.UnscaledFontsize = GuiStyle.SmallFontSize;
        font2.UnscaledFontsize = GuiStyle.SmallFontSize;

        GuiElementTextButton elem = new GuiElementTextButton(api, text, font1, font2, onClick, bounds, style);
        elem.SetOrientation(font1.Orientation);
        return this.AddGuiElement(key, elem);
    }

    /// Creates a toggling button with the given parameters.
    public GuiElementContainerWrapper AddToggleButton(
        string text,
        CairoFont font,
        Action<bool> onToggle,
        ElementBounds bounds,
        string key = null
    ) {
        var elem = new GuiElementToggleButton(api, "", text, font, onToggle, bounds, true);
        return this.AddGuiElement(key, elem);
    }

    public GuiElementContainerWrapper AddSlider(
        ActionConsumable<int> onNewSliderValue,
        ElementBounds bounds,
        int minValue = 0,
        int maxValue = 100,
        int step = 1,
        int currentValue = 0,
        string unit = "",
        string key = null
    )
    {
        var slider = new GuiElementSliderCustom(api, onNewSliderValue, bounds);
        slider.SetValues(currentValue, minValue, maxValue, step, unit);
        return this.AddGuiElement(key, slider);
    }

    /// Helper to create a scale slider.
    public GuiElementContainerWrapper AddScaleSlider(
        string label,
        string key, // required, callbacks need references
        ElementBounds bounds,
        int widthLabel,
        int widthValue,
        int minValue,
        int maxValue,
        int step,
        int initialValue,
        Action<int> OnChange
    ) {
        int height = (int) bounds.fixedHeight;
        int widthSlider = (int) bounds.fixedWidth - widthLabel - widthValue;
        var fontsmall = CairoFont.WhiteSmallText();
        string inputKey = key + "-input";
        string sliderKey = key + "-slider";
        
        // experiment adding sliders that render color
        ElementBounds sliderLabel = bounds
            .FlatCopy()
            .WithFixedSize(widthLabel, height);
        
        ElementBounds sliderBounds = sliderLabel
            .RightCopy(8)
            .WithFixedSize(widthSlider, height);
        
        ElementBounds sliderValueBounds = sliderBounds
            .RightCopy(16)
            .WithFixedSize(widthValue, height);
        
        // In callbacks below, we rely on using dictionary to get the
        // gui element, instead of directly storing references.
        // This is in order to reduce direct reference coupling between
        // the different elements inside the closures. 

        // note ActionConsumable<T> is vintagestory api defined type
        bool updateSliderValueInputThenCallback(int newVal) {
            // update slider input value on right side
            GuiElementTextInputCustom sliderValueInput = (GuiElementTextInputCustom) elements[inputKey];
            sliderValueInput?.SetValue(newVal.ToString());
            // callback
            OnChange(newVal);
            return true;
        }

        // note Action<string> is vintagestory api defined type
        void updateSliderThenCallback(string newValStr)
        {
            int newVal;
            if (int.TryParse(newValStr, out newVal)) {
                newVal = GameMath.Clamp(newVal, minValue, maxValue);
                
                // only update if value changed
                GuiElementSliderCustom slider = (GuiElementSliderCustom) elements[sliderKey];
                if (newVal != slider.currentValue)
                {
                    // update slider position
                    slider?.SetValue(newVal);
                    // callback
                    OnChange(newVal);
                }
            }
        }
        
        // label on left
        AddRichtext(label, fontsmall, sliderLabel);

        // slider in middle
        var slider = new GuiElementSliderCustom(
            api,
            updateSliderValueInputThenCallback,
            sliderBounds
        );
        slider.SetValues(initialValue, minValue, maxValue, step, "");

        // value input on right
        var sliderValueInput = new GuiElementTextInputCustom(
            api,
            sliderValueBounds,
            updateSliderThenCallback,
            fontsmall
        );

        AddGuiElement(sliderKey, slider);
        AddGuiElement(inputKey, sliderValueInput);
        // has to be placed here after adding to elements lol wtf
        sliderValueInput.SetPlaceHolderText("Scale");
        sliderValueInput.SetValueWithoutChange(initialValue.ToString());
        
        return this;
    }

    public GuiElementContainerWrapper AddColorHueSlider(
        ElementBounds bounds,
        double initialHue,
        ActionConsumable<double> onNewSliderValue,
        string key = null
    )
    {
        return this.AddGuiElement(key, new GuiElementColorHueSlider(
            api,
            bounds,
            initialHue,
            onNewSliderValue
        ));
    }

    public GuiElementContainerWrapper AddColorRectangle(
        ElementBounds bounds,
        int r,
        int g,
        int b,
        int alpha = 255,
        bool useHsv = false,
        string key = null
    )
    {
        return this.AddGuiElement(key, new GuiElementColorRectangle(api, bounds, r, g, b, alpha, useHsv));
    }

    /// Helper to create RGB color sliders
    public GuiElementContainerWrapper AddRGBColorSliders(
        string title,
        string key, // required, callbacks need references
        int width,
        int height, // per slider
        ElementBounds bounds,
        int initialRed,
        int initialGreen,
        int initialBlue,
        Action<int> OnChangeColor
    )
    {
        int widthLabel = 20;
        int widthValueInput = 40;
        int widthSlider = width - widthLabel - widthValueInput;
        int widthColor = width - 160;
        var fontsmall = CairoFont.WhiteSmallText();
        const int IDX_RED = 0;
        const int IDX_GREEN = 1;
        const int IDX_BLUE = 2;
        string colorKey = key + "-color";

        // experiment adding sliders that render color
        ElementBounds sliderTitle = bounds
            .FlatCopy()
            .WithFixedWidth(160)
            .WithFixedHeight(26);
        ElementBounds sliderColor = bounds
            .FlatCopy()
            .WithFixedWidth(widthColor)
            .WithFixedHeight(18)
            .FixedRightOf(sliderTitle, 0)
            .WithFixedPadding(0, 4);
        
        // red
        ElementBounds sliderRedLabel = sliderTitle
            .BelowCopy(0, 0)
            .WithFixedSize(widthLabel, height);
        ElementBounds sliderRedBounds = sliderRedLabel
            .RightCopy()
            .WithFixedSize(widthSlider, height);
        ElementBounds sliderRedValueBounds = sliderRedBounds
            .RightCopy()
            .WithFixedSize(widthValueInput, height);
        
        // green
        ElementBounds sliderGreenLabel = sliderRedLabel
            .BelowCopy(0, 4)
            .WithFixedSize(widthLabel, height);
        ElementBounds sliderGreenBounds = sliderGreenLabel
            .RightCopy()
            .WithFixedSize(widthSlider, height);
        ElementBounds sliderGreenValueBounds = sliderGreenBounds
            .RightCopy()
            .WithFixedSize(widthValueInput, height);
        
        // blue
        ElementBounds sliderBlueLabel = sliderGreenLabel
            .BelowCopy(0, 4)
            .WithFixedSize(widthLabel, height);
        ElementBounds sliderBlueBounds = sliderBlueLabel
            .RightCopy()
            .WithFixedSize(widthSlider, height);
        ElementBounds sliderBlueValueBounds = sliderBlueBounds
            .RightCopy()
            .WithFixedSize(widthValueInput, height);
        
        // In callbacks below, we rely on using dictionary to get the
        // gui element, instead of directly storing references.
        // This is in order to reduce direct reference coupling between
        // the different elements inside the closures. 

        // note ActionConsumable<T> is vintagestory api defined type
        ActionConsumable<int> updateSliderValueInputThenCallback(
            string sliderValueKey,
            int rgbIndex
        )
        {
            return (v) => {
                // only update if value in index changed
                GuiElementColorRectangle colorRect = (GuiElementColorRectangle) elements[colorKey];
                int prevVal = colorRect.GetRGBIndex(rgbIndex);
                if (prevVal != v)
                {
                    // update color rectangle
                    colorRect.SetRGBIndex(rgbIndex, v);
                    // update slider input value on right side
                    GuiElementTextInputCustom sliderValueInput = (GuiElementTextInputCustom) elements[sliderValueKey];
                    sliderValueInput?.SetValue(v.ToString());
                    // callback
                    OnChangeColor(colorRect.rgba);
                }
                return true; 
            };
        }

        // note Action<string> is vintagestory api defined type
        Action<string> updateSliderThenCallback(
            string sliderKey,
            int rgbIndex
        )
        {
            return (v) => {
                int val;
                if (int.TryParse(v, out val)) {
                    val = GameMath.Clamp(val, 0, 255);
                    
                    // only update if value in index changed
                    GuiElementColorRectangle colorRect = (GuiElementColorRectangle) elements[colorKey];
                    int prevVal = colorRect.GetRGBIndex(rgbIndex);
                    if (prevVal != val)
                    {
                        // update color rectangle
                        colorRect.SetRGBIndex(rgbIndex, val);
                        // update slider position
                        GuiElementSliderCustom slider = (GuiElementSliderCustom) elements[sliderKey];
                        slider?.SetValue(val);
                        // callback
                        OnChangeColor(colorRect.rgba);
                    }
                }
                return; 
            };
        }
        
        // title and current color
        AddRichtext(title, fontsmall, sliderTitle);
        AddColorRectangle(sliderColor, initialRed, initialGreen, initialBlue, 255, false, colorKey);

        // RED
        AddRichtext(Lang.Get("kemono:gui-symbol-red"), fontsmall, sliderRedLabel);
        var sliderRed = new GuiElementSliderCustom(
            api,
            updateSliderValueInputThenCallback(key + "-slider-r-value", IDX_RED),
            sliderRedBounds
        );
        sliderRed.SetValues(initialRed, 0, 255, 1, "");

        var sliderRedValueInput = new GuiElementTextInputCustom(
            api,
            sliderRedValueBounds,
            updateSliderThenCallback(key + "-slider-r", IDX_RED),
            fontsmall
        );

        AddGuiElement(key + "-slider-r", sliderRed);
        AddGuiElement(key + "-slider-r-value", sliderRedValueInput);
        // has to be placed here after adding to elements lol wtf
        sliderRedValueInput.SetPlaceHolderText("R");
        sliderRedValueInput.SetValueWithoutChange(initialRed.ToString());


        // GREEN
        AddRichtext(Lang.Get("kemono:gui-symbol-green"), fontsmall, sliderGreenLabel);
        var sliderGreen = new GuiElementSliderCustom(
            api,
            updateSliderValueInputThenCallback(key + "-slider-g-value", IDX_GREEN),
            sliderGreenBounds
        );
        sliderGreen.SetValues(initialGreen, 0, 255, 1, "");

        var sliderGreenValueInput = new GuiElementTextInputCustom(
            api,
            sliderGreenValueBounds,
            updateSliderThenCallback(key + "-slider-g", IDX_GREEN),
            fontsmall
        );

        AddGuiElement(key + "-slider-g", sliderGreen);
        AddGuiElement(key + "-slider-g-value", sliderGreenValueInput);
        // has to be placed here after adding to elements lol wtf
        sliderGreenValueInput.SetPlaceHolderText("G");
        sliderGreenValueInput.SetValueWithoutChange(initialGreen.ToString());


        // BLUE
        AddRichtext(Lang.Get("kemono:gui-symbol-blue"), fontsmall, sliderBlueLabel);
        var sliderBlue = new GuiElementSliderCustom(
            api,
            updateSliderValueInputThenCallback(key + "-slider-b-value", IDX_BLUE),
            sliderBlueBounds
        );
        sliderBlue.SetValues(initialBlue, 0, 255, 1, "");

        var sliderBlueValueInput = new GuiElementTextInputCustom(
            api,
            sliderBlueValueBounds,
            updateSliderThenCallback(key + "-slider-b", IDX_BLUE),
            fontsmall
        );

        AddGuiElement(key + "-slider-b", sliderBlue);
        AddGuiElement(key + "-slider-b-value", sliderBlueValueInput);
        // has to be placed here after adding to elements lol wtf
        sliderBlueValueInput.SetPlaceHolderText("B");
        sliderBlueValueInput.SetValueWithoutChange(initialBlue.ToString());
        
        return this;
    }

    /// Helper to create HSV color sliders.
    /// Convention for slider ranges is:
    /// Hue: [0, 360]
    /// Sat: [0, 100]
    /// Val: [0, 100]
    /// Output will be integers in these ranges. Client user must
    /// convert to desired output range based on these slide ranges.
    public GuiElementContainerWrapper AddHSVColorSliders(
        string title,
        string key, // required, callbacks need references
        int width,
        int height, // per slider
        ElementBounds bounds,
        double initialHue, // [0, 1]
        double initialSat, // [0, 1]
        double initialVal, // [0, 1]
        Action<int> OnChangeColor
    ) {
        int widthLabel = 20;
        int widthValueInput = 40;
        int widthSlider = width - widthLabel - widthValueInput;
        int widthColor = width - 160;
        var fontsmall = CairoFont.WhiteSmallText();
        string colorKey = key + "-color";

        // element keys
        string sliderHueKey = key + "-slider-h";
        string sliderHueValueKey = key + "-slider-h-value";
        string sliderSatKey = key + "-slider-s";
        string sliderSatValueKey = key + "-slider-s-value";
        string sliderValKey = key + "-slider-v";
        string sliderValValueKey = key + "-slider-v-value";

        // scaled to display range
        int initialHueScaled = (int) (360.0 * initialHue);
        int initialSatScaled = (int) (100.0 * initialSat);
        int initialValScaled = (int) (100.0 * initialVal);

        // calculate initial RGB from initial HSV
        (int initialRed, int initialGreen, int initialBlue) = KemonoColorUtil.HsvToRgb(initialHue, initialSat, initialVal);

        // experiment adding sliders that render color
        ElementBounds sliderTitle = bounds
            .FlatCopy()
            .WithFixedWidth(160)
            .WithFixedHeight(26);
        ElementBounds sliderColor = bounds
            .FlatCopy()
            .WithFixedWidth(widthColor)
            .WithFixedHeight(18)
            .FixedRightOf(sliderTitle, 0)
            .WithFixedPadding(0, 4);
        
        // hue
        ElementBounds sliderHueLabel = sliderTitle
            .BelowCopy(0, 0)
            .WithFixedSize(widthLabel, height);
        ElementBounds sliderHueBounds = sliderHueLabel
            .RightCopy()
            .WithFixedOffset(0, 2)
            .WithFixedSize(widthSlider, height - 4);
        ElementBounds sliderHueValueBounds = sliderHueBounds
            .RightCopy()
            .WithFixedOffset(0, -2)
            .WithFixedSize(widthValueInput, height);
        
        // sat
        ElementBounds sliderSatLabel = sliderHueLabel
            .BelowCopy(0, 4)
            .WithFixedSize(widthLabel, height);
        ElementBounds sliderSatBounds = sliderSatLabel
            .RightCopy()
            .WithFixedSize(widthSlider, height);
        ElementBounds sliderSatValueBounds = sliderSatBounds
            .RightCopy()
            .WithFixedSize(widthValueInput, height);
        
        // val
        ElementBounds sliderValLabel = sliderSatLabel
            .BelowCopy(0, 4)
            .WithFixedSize(widthLabel, height);
        ElementBounds sliderValBounds = sliderValLabel
            .RightCopy()
            .WithFixedSize(widthSlider, height);
        ElementBounds sliderValValueBounds = sliderValBounds
            .RightCopy()
            .WithFixedSize(widthValueInput, height);
        
        // In callbacks below, we rely on using dictionary to get the
        // gui element, instead of directly storing references.
        // This is in order to reduce direct reference coupling between
        // the different elements inside the closures. 

        // called when hue slider moved
        bool updateHueSliderValueInputThenCallback(double newHueInput)
        {
            double newHue = GameMath.Clamp(newHueInput, 0.0, 1.0);

            // only update if changed
            GuiElementColorRectangle colorRect = (GuiElementColorRectangle) elements[colorKey];
            double prevHue = colorRect.hue;
            if (newHue != prevHue)
            {
                colorRect.SetHue(newHue);
                // update slider input value on right side
                GuiElementTextInputCustom sliderValueInput = (GuiElementTextInputCustom) elements[sliderHueValueKey];
                sliderValueInput?.SetValue(((int)(newHueInput * 360.0)).ToString());
                // callback
                OnChangeColor(colorRect.rgba);
            }
            return true;
        }

        // called when hue input box changed
        void updateHueSliderThenCallback(string newHueInput)
        {
            int newHueInt;
            if (int.TryParse(newHueInput, out newHueInt)) {
                newHueInt = GameMath.Clamp(newHueInt, 0, 360);
                double newHue = newHueInt / 360.0;
                
                // only update if changed
                GuiElementColorRectangle colorRect = (GuiElementColorRectangle) elements[colorKey];
                double prevHue = colorRect.hue;
                if (newHue != prevHue)
                {
                    colorRect.SetHue(newHue);
                    // update slider position
                    GuiElementColorHueSlider slider = (GuiElementColorHueSlider) elements[sliderHueKey];
                    slider?.SetHue(newHue);
                    // callback
                    OnChangeColor(colorRect.rgba);
                }
            }
        }

        // called when sat slider moved
        bool updateSatSliderValueInputThenCallback(int newSatInput)
        {
            double newSat = GameMath.Clamp(newSatInput, 0, 100) / 100.0;

            // only update if changed
            GuiElementColorRectangle colorRect = (GuiElementColorRectangle) elements[colorKey];
            double prevSat = colorRect.sat;
            if (newSat != prevSat)
            {
                colorRect.SetSat(newSat);
                // update slider input value on right side
                GuiElementTextInputCustom sliderValueInput = (GuiElementTextInputCustom) elements[sliderSatValueKey];
                sliderValueInput?.SetValue(newSatInput.ToString());
                // callback
                OnChangeColor(colorRect.rgba);
            }
            return true;
        }

        // called when sat input box changed
        void updateSatSliderThenCallback(string newSatInput)
        {
            int newSatInt;
            if (int.TryParse(newSatInput, out newSatInt)) {
                newSatInt = GameMath.Clamp(newSatInt, 0, 100);
                double newSat = newSatInt / 100.0;
                
                // only update if changed
                GuiElementColorRectangle colorRect = (GuiElementColorRectangle) elements[colorKey];
                double prevSat = colorRect.sat;
                if (newSat != prevSat)
                {
                    colorRect.SetSat(newSat);
                    // update slider position
                    GuiElementSliderCustom slider = (GuiElementSliderCustom) elements[sliderSatKey];
                    slider?.SetValue(newSatInt);
                    // callback
                    OnChangeColor(colorRect.rgba);
                }
            }
        }

        // called when val slider moved
        bool updateValSliderValueInputThenCallback(int newValInput)
        {
            double newVal = GameMath.Clamp(newValInput, 0, 100) / 100.0;

            // only update if changed
            GuiElementColorRectangle colorRect = (GuiElementColorRectangle) elements[colorKey];
            double prevVal = colorRect.val;
            if (newVal != prevVal)
            {
                colorRect.SetVal(newVal);
                // update slider input value on right side
                GuiElementTextInputCustom sliderValueInput = (GuiElementTextInputCustom) elements[sliderValValueKey];
                sliderValueInput?.SetValue(newValInput.ToString());
                // callback
                OnChangeColor(colorRect.rgba);
            }
            return true;
        }

        // called when val input box changed
        void updateValSliderThenCallback(string newValInput)
        {
            int newValInt;
            if (int.TryParse(newValInput, out newValInt)) {
                newValInt = GameMath.Clamp(newValInt, 0, 100);
                double newVal = newValInt / 100.0;
                
                // only update if changed
                GuiElementColorRectangle colorRect = (GuiElementColorRectangle) elements[colorKey];
                double prevVal = colorRect.val;
                if (newVal != prevVal)
                {
                    colorRect.SetVal(newVal);
                    // update slider position
                    GuiElementSliderCustom slider = (GuiElementSliderCustom) elements[sliderValKey];
                    slider?.SetValue(newValInt);
                    // callback
                    OnChangeColor(colorRect.rgba);
                }
            }
        }
        
        // title and current color
        AddRichtext(title, fontsmall, sliderTitle);
        AddColorRectangle(sliderColor, initialRed, initialGreen, initialBlue, 255, true, colorKey);

        // HUE
        AddRichtext(Lang.Get("kemono:gui-symbol-hue"), fontsmall, sliderHueLabel);
        var sliderHue = new GuiElementColorHueSlider(
            api,
            sliderHueBounds,
            initialHue,
            updateHueSliderValueInputThenCallback
        );
        //// old normal slider
        // var sliderHue = new GuiElementSliderCustom(
        //     api,
        //     updateHueSliderValueInputThenCallback,
        //     sliderHueBounds
        // );
        // sliderHue.SetValues(initialHue, 0, 360, 1, "");

        var sliderHueValueInput = new GuiElementTextInputCustom(
            api,
            sliderHueValueBounds,
            updateHueSliderThenCallback,
            fontsmall
        );

        AddGuiElement(sliderHueKey, sliderHue);
        AddGuiElement(sliderHueValueKey, sliderHueValueInput);
        // has to be placed here after adding to elements lol wtf
        sliderHueValueInput.SetPlaceHolderText("H");
        sliderHueValueInput.SetValueWithoutChange(initialHueScaled.ToString());


        // SAT
        AddRichtext(Lang.Get("kemono:gui-symbol-sat"), fontsmall, sliderSatLabel);
        var sliderSat = new GuiElementSliderCustom(
            api,
            updateSatSliderValueInputThenCallback,
            sliderSatBounds
        );
        sliderSat.SetValues(initialSatScaled, 0, 100, 1, "");

        var sliderSatValueInput = new GuiElementTextInputCustom(
            api,
            sliderSatValueBounds,
            updateSatSliderThenCallback,
            fontsmall
        );

        AddGuiElement(sliderSatKey, sliderSat);
        AddGuiElement(sliderSatValueKey, sliderSatValueInput);
        // has to be placed here after adding to elements lol wtf
        sliderSatValueInput.SetPlaceHolderText("S");
        sliderSatValueInput.SetValueWithoutChange(initialSatScaled.ToString());


        // VAL
        AddRichtext(Lang.Get("kemono:gui-symbol-val"), fontsmall, sliderValLabel);
        var sliderVal = new GuiElementSliderCustom(
            api,
            updateValSliderValueInputThenCallback,
            sliderValBounds
        );
        sliderVal.SetValues(initialValScaled, 0, 100, 1, "");

        var sliderValValueInput = new GuiElementTextInputCustom(
            api,
            sliderValValueBounds,
            updateValSliderThenCallback,
            fontsmall
        );

        AddGuiElement(sliderValKey, sliderVal);
        AddGuiElement(sliderValValueKey, sliderValValueInput);
        // has to be placed here after adding to elements lol wtf
        sliderValValueInput.SetPlaceHolderText("V");
        sliderValValueInput.SetValueWithoutChange(initialValScaled.ToString());
        
        return this;
    }

    // ====================================================================
    // ADDITIONAL HELPER METHODS
    // ====================================================================

    public T GetElementAs<T>(string key) where T : GuiElement
    {
        GuiElement val;
        elements.TryGetValue(key, out val);
        if (val is T)
        {
            return val as T;
        }
        else
        {
            return null;
        }
    }

    public GuiElementColorListPicker GetColorListPicker(string key)
    {
        GuiElement val;
        elements.TryGetValue(key, out val);
        if (val is GuiElementColorListPicker)
        {
            return val as GuiElementColorListPicker;
        }
        else
        {
            return null;
        }
    }

    public void ColorListPickerSetValue(string key, int selectedIndex)
    {
        int i = 0;
        GuiElementColorListPicker btn;
        while ((btn = this.GetColorListPicker(key + "-" + i)) != null)
        {
            btn.SetValue(i == selectedIndex);
            i++;
        }
    }
}
