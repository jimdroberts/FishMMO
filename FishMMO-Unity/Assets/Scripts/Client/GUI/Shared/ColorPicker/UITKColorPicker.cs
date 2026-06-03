using System;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit color picker supporting an HSV picking texture plus H/S/V and R/G/B/A sliders,
	/// inputs and a hex field. Mirrors the legacy UGUI <c>UIColorPicker</c> behaviour, generating
	/// spectrum textures via <see cref="TinyColor"/> and binding them to element background images.
	/// </summary>
	public class UITKColorPicker : UITKControl
	{
		/// <summary>
		/// Width of slider backgrounds in pixels.
		/// </summary>
		public const int SLIDER_BACKGROUND_WIDTH = 134;
		/// <summary>
		/// Height of slider backgrounds in pixels.
		/// </summary>
		public const int SLIDER_BACKGROUND_HEIGHT = 1;
		/// <summary>
		/// Width of HSV texture in pixels.
		/// </summary>
		public const int HSV_TEXTURE_WIDTH = 192;
		/// <summary>
		/// Height of HSV texture in pixels.
		/// </summary>
		public const int HSV_TEXTURE_HEIGHT = 192;
		/// <summary>
		/// Number of hue values supported (degrees).
		/// </summary>
		public const int NUM_HUES = 360;
		/// <summary>
		/// Maximum hue value (359).
		/// </summary>
		public const int HUE_MAX = 359;
		/// <summary>
		/// Maximum saturation/value (100).
		/// </summary>
		public const int SV_MAX = 100;
		/// <summary>
		/// Maximum RGBA value (255).
		/// </summary>
		public const int RGBA_MAX = 255;

		private const string CURRENT_SWATCH_NAME = "color-current";
		private const string HSV_TEXTURE_NAME = "hsv-texture";
		private const string HSV_CURSOR_NAME = "hsv-cursor";
		private const string HEX_INPUT_NAME = "hex-input";
		private const string CLOSE_BUTTON_NAME = "colorpicker-close-btn";

		private const string H_SLIDER_NAME = "h-slider";
		private const string H_INPUT_NAME = "h-input";
		private const string H_BACKGROUND_NAME = "h-background";
		private const string H_TEXTURE_NAME = "hsv-texture";

		private const string S_SLIDER_NAME = "s-slider";
		private const string S_INPUT_NAME = "s-input";
		private const string S_BACKGROUND_NAME = "s-background";

		private const string V_SLIDER_NAME = "v-slider";
		private const string V_INPUT_NAME = "v-input";
		private const string V_BACKGROUND_NAME = "v-background";

		private const string R_SLIDER_NAME = "r-slider";
		private const string R_INPUT_NAME = "r-input";
		private const string R_BACKGROUND_NAME = "r-background";

		private const string G_SLIDER_NAME = "g-slider";
		private const string G_INPUT_NAME = "g-input";
		private const string G_BACKGROUND_NAME = "g-background";

		private const string B_SLIDER_NAME = "b-slider";
		private const string B_INPUT_NAME = "b-input";
		private const string B_BACKGROUND_NAME = "b-background";

		private const string A_SLIDER_NAME = "a-slider";
		private const string A_INPUT_NAME = "a-input";
		private const string A_BACKGROUND_NAME = "a-background";

		/// <summary>
		/// Initial color for the picker.
		/// </summary>
		public Color InitialColor = Color.red;

		/// <summary>
		/// Callback invoked whenever the selected color changes.
		/// Consumers should assign this before calling <see cref="UITKControl.Show"/> to receive updates.
		/// </summary>
		public Action<Color> OnColorChanged;

		/// <summary>
		/// Cached HSV textures for each hue value.
		/// </summary>
		private readonly Texture2D[] cachedHSVTextures = new Texture2D[NUM_HUES];

		/// <summary>
		/// The currently selected color.
		/// </summary>
		private Color current = Color.red;

		/// <summary>
		/// When true, value-changed callbacks are ignored to prevent feedback loops during programmatic updates.
		/// </summary>
		private bool suppressCallbacks;

		private VisualElement currentSwatch;
		private VisualElement hsvTexture;
		private VisualElement hsvCursor;
		private VisualElement hBackground;
		private VisualElement sBackground;
		private VisualElement vBackground;
		private VisualElement rBackground;
		private VisualElement gBackground;
		private VisualElement bBackground;
		private VisualElement aBackground;

		private TextField hexInput;
		private Slider hSlider;
		private Slider sSlider;
		private Slider vSlider;
		private Slider rSlider;
		private Slider gSlider;
		private Slider bSlider;
		private Slider aSlider;
		private IntegerField hInput;
		private IntegerField sInput;
		private IntegerField vInput;
		private IntegerField rInput;
		private IntegerField gInput;
		private IntegerField bInput;
		private IntegerField aInput;

		/// <summary>
		/// Resolves elements, configures sliders, wires callbacks and applies the initial color.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			currentSwatch = Root.Q<VisualElement>(CURRENT_SWATCH_NAME);
			hsvTexture = Root.Q<VisualElement>(HSV_TEXTURE_NAME);
			hsvCursor = Root.Q<VisualElement>(HSV_CURSOR_NAME);
			hexInput = Root.Q<TextField>(HEX_INPUT_NAME);

			hSlider = Root.Q<Slider>(H_SLIDER_NAME);
			sSlider = Root.Q<Slider>(S_SLIDER_NAME);
			vSlider = Root.Q<Slider>(V_SLIDER_NAME);
			rSlider = Root.Q<Slider>(R_SLIDER_NAME);
			gSlider = Root.Q<Slider>(G_SLIDER_NAME);
			bSlider = Root.Q<Slider>(B_SLIDER_NAME);
			aSlider = Root.Q<Slider>(A_SLIDER_NAME);

			hInput = Root.Q<IntegerField>(H_INPUT_NAME);
			sInput = Root.Q<IntegerField>(S_INPUT_NAME);
			vInput = Root.Q<IntegerField>(V_INPUT_NAME);
			rInput = Root.Q<IntegerField>(R_INPUT_NAME);
			gInput = Root.Q<IntegerField>(G_INPUT_NAME);
			bInput = Root.Q<IntegerField>(B_INPUT_NAME);
			aInput = Root.Q<IntegerField>(A_INPUT_NAME);

			hBackground = Root.Q<VisualElement>(H_BACKGROUND_NAME);
			sBackground = Root.Q<VisualElement>(S_BACKGROUND_NAME);
			vBackground = Root.Q<VisualElement>(V_BACKGROUND_NAME);
			rBackground = Root.Q<VisualElement>(R_BACKGROUND_NAME);
			gBackground = Root.Q<VisualElement>(G_BACKGROUND_NAME);
			bBackground = Root.Q<VisualElement>(B_BACKGROUND_NAME);
			aBackground = Root.Q<VisualElement>(A_BACKGROUND_NAME);

			ConfigureSlider(hSlider, 0, HUE_MAX);
			ConfigureSlider(sSlider, 0, SV_MAX);
			ConfigureSlider(vSlider, 0, SV_MAX);
			ConfigureSlider(rSlider, 0, RGBA_MAX);
			ConfigureSlider(gSlider, 0, RGBA_MAX);
			ConfigureSlider(bSlider, 0, RGBA_MAX);
			ConfigureSlider(aSlider, 0, RGBA_MAX);

			RegisterHSV();
			RegisterRGBA();

			if (hexInput != null)
			{
				hexInput.RegisterCallback<KeyDownEvent>((evt) =>
				{
					if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
					{
						UpdateHexValue(hexInput.value);
					}
				});
			}

			if (hsvTexture != null)
			{
				hsvTexture.RegisterCallback<PointerDownEvent>(OnHSVPointerDown);
			}

			Button closeButton = Root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}

			// Static spectrum backgrounds that never change.
			SetBackground(vBackground, TinyColor.GenerateBrightnessSpectrum(HSV_TEXTURE_WIDTH, SLIDER_BACKGROUND_HEIGHT));
			SetBackground(aBackground, TinyColor.GenerateAlphaSpectrum(HSV_TEXTURE_WIDTH, SLIDER_BACKGROUND_HEIGHT));

			SetColor(InitialColor);
		}

		/// <summary>
		/// Configures a slider's range and whole-number behaviour.
		/// </summary>
		private void ConfigureSlider(Slider slider, float low, float high)
		{
			if (slider == null)
			{
				return;
			}
			slider.lowValue = low;
			slider.highValue = high;
		}

		/// <summary>
		/// Registers value-changed callbacks for the HSV sliders and inputs.
		/// </summary>
		private void RegisterHSV()
		{
			hSlider?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateHueSliderValue(evt.newValue); } });
			sSlider?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateSaturationSliderValue(evt.newValue); } });
			vSlider?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateValueSliderValue(evt.newValue); } });

			hInput?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateHueInputValue(evt.newValue); } });
			sInput?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateSaturationInputValue(evt.newValue); } });
			vInput?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateValueInputValue(evt.newValue); } });
		}

		/// <summary>
		/// Registers value-changed callbacks for the RGBA sliders and inputs.
		/// </summary>
		private void RegisterRGBA()
		{
			rSlider?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateRedSliderValue(evt.newValue); } });
			gSlider?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateGreenSliderValue(evt.newValue); } });
			bSlider?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateBlueSliderValue(evt.newValue); } });
			aSlider?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateAlphaSliderValue(evt.newValue); } });

			rInput?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateRedInputValue(evt.newValue); } });
			gInput?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateGreenInputValue(evt.newValue); } });
			bInput?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateBlueInputValue(evt.newValue); } });
			aInput?.RegisterValueChangedCallback((evt) => { if (!suppressCallbacks) { UpdateAlphaInputValue(evt.newValue); } });
		}

		/// <summary>
		/// Sets a background image on an element from a generated texture.
		/// </summary>
		private void SetBackground(VisualElement element, Texture2D texture)
		{
			if (element == null || texture == null)
			{
				return;
			}
			element.style.backgroundImage = new StyleBackground(texture);
		}

		/// <summary>
		/// Retrieves (and lazily generates) the HSV texture for the given hue index.
		/// </summary>
		private Texture2D GetHSVTexture(int hueIndex)
		{
			hueIndex = Mathf.Clamp(hueIndex, 0, HUE_MAX);
			Texture2D texture = cachedHSVTextures[hueIndex];
			if (texture == null)
			{
				texture = TinyColor.GenerateHSVTexture(hueIndex, HSV_TEXTURE_WIDTH, HSV_TEXTURE_HEIGHT);
				cachedHSVTextures[hueIndex] = texture;
			}
			return texture;
		}

		/// <summary>
		/// Sets the picker color and refreshes all sliders, inputs, backgrounds and the cursor.
		/// </summary>
		/// <param name="color">The color to set.</param>
		public void SetColor(Color color)
		{
			current = color;
			if (currentSwatch != null)
			{
				currentSwatch.style.backgroundColor = current;
			}
			SetHSV(TinyColor.RGBToHSV(current.r, current.g, current.b));
			SetRGB(current);
			SetSliderValue(aSlider, current.a * RGBA_MAX);
			SetInputValue(aInput, Mathf.RoundToInt(current.a * RGBA_MAX));
			UpdateBackgroundSprites();
		}

		/// <summary>
		/// Sets a slider value without firing user callbacks.
		/// </summary>
		private void SetSliderValue(Slider slider, float value)
		{
			if (slider == null)
			{
				return;
			}
			bool previous = suppressCallbacks;
			suppressCallbacks = true;
			slider.value = value;
			suppressCallbacks = previous;
		}

		/// <summary>
		/// Sets an integer input value without firing user callbacks.
		/// </summary>
		private void SetInputValue(IntegerField field, int value)
		{
			if (field == null)
			{
				return;
			}
			bool previous = suppressCallbacks;
			suppressCallbacks = true;
			field.value = value;
			suppressCallbacks = previous;
		}

		/// <summary>
		/// Updates the hex input text without firing user callbacks.
		/// </summary>
		private void SetHexText(string value)
		{
			if (hexInput == null)
			{
				return;
			}
			bool previous = suppressCallbacks;
			suppressCallbacks = true;
			hexInput.value = value;
			suppressCallbacks = previous;
		}

		/// <summary>
		/// Recomputes the current color from the RGBA sliders and refreshes HSV state.
		/// </summary>
		private void UpdateHSVFromRGB()
		{
			current = new Color(SliderValue(rSlider) / RGBA_MAX, SliderValue(gSlider) / RGBA_MAX, SliderValue(bSlider) / RGBA_MAX, SliderValue(aSlider) / RGBA_MAX);
			SetHSV(TinyColor.RGBToHSV(current.r, current.g, current.b));
			UpdateBackgroundSprites();
		}

		/// <summary>
		/// Recomputes the current color from the HSV sliders and refreshes RGB state.
		/// </summary>
		private void UpdateRGBFromHSV()
		{
			Color rgb = TinyColor.HSVToRGB(SliderValue(hSlider), SliderValue(sSlider) * 0.01f, SliderValue(vSlider) * 0.01f);
			current = new Color(rgb.r, rgb.g, rgb.b, current.a);
			SetRGB(current);
			UpdateBackgroundSprites();
		}

		private float SliderValue(Slider slider)
		{
			return slider != null ? slider.value : 0f;
		}

		/// <summary>
		/// Applies the given HSV color to the H/S/V sliders, inputs and the hex field.
		/// </summary>
		private void SetHSV(Color hsv)
		{
			SetSliderValue(hSlider, Mathf.RoundToInt(hsv.r));
			SetSliderValue(sSlider, Mathf.RoundToInt(hsv.g * SV_MAX));
			SetSliderValue(vSlider, Mathf.RoundToInt(hsv.b * SV_MAX));
			SetInputValue(hInput, Mathf.RoundToInt(SliderValue(hSlider)));
			SetInputValue(sInput, Mathf.RoundToInt(SliderValue(sSlider)));
			SetInputValue(vInput, Mathf.RoundToInt(SliderValue(vSlider)));
			SetHexText(current.ToHex());
		}

		/// <summary>
		/// Applies the given RGB color to the R/G/B sliders, inputs and the hex field.
		/// </summary>
		private void SetRGB(Color rgb)
		{
			SetSliderValue(rSlider, rgb.r * RGBA_MAX);
			SetSliderValue(gSlider, rgb.g * RGBA_MAX);
			SetSliderValue(bSlider, rgb.b * RGBA_MAX);
			SetInputValue(rInput, Mathf.RoundToInt(SliderValue(rSlider)));
			SetInputValue(gInput, Mathf.RoundToInt(SliderValue(gSlider)));
			SetInputValue(bInput, Mathf.RoundToInt(SliderValue(bSlider)));
			SetHexText(current.ToHex());
		}

		/// <summary>
		/// Regenerates all spectrum backgrounds and the HSV texture, repositions the cursor, and notifies listeners.
		/// </summary>
		private void UpdateBackgroundSprites()
		{
			Texture2D hsv = GetHSVTexture((int)SliderValue(hSlider));
			SetBackground(hsvTexture, hsv);

			SetBackground(hBackground, TinyColor.GenerateColorSpectrum(current.a, NUM_HUES, SLIDER_BACKGROUND_HEIGHT));
			SetBackground(sBackground, TinyColor.GenerateSaturationSpectrum(SliderValue(hSlider), SliderValue(vSlider) * 0.01f, current.a, SLIDER_BACKGROUND_WIDTH, SLIDER_BACKGROUND_HEIGHT));
			SetBackground(rBackground, TinyColor.GenerateRedSpectrum(current.g, current.b, current.a, SLIDER_BACKGROUND_WIDTH, SLIDER_BACKGROUND_HEIGHT));
			SetBackground(gBackground, TinyColor.GenerateGreenSpectrum(current.r, current.b, current.a, SLIDER_BACKGROUND_WIDTH, SLIDER_BACKGROUND_HEIGHT));
			SetBackground(bBackground, TinyColor.GenerateBlueSpectrum(current.r, current.g, current.a, SLIDER_BACKGROUND_WIDTH, SLIDER_BACKGROUND_HEIGHT));

			if (currentSwatch != null)
			{
				currentSwatch.style.backgroundColor = current;
			}
			SetCursor();

			OnColorChanged?.Invoke(current);
		}

		/// <summary>
		/// Picks a color from the HSV texture based on a pointer-down within the picking area.
		/// </summary>
		private void OnHSVPointerDown(PointerDownEvent evt)
		{
			if (hsvTexture == null)
			{
				return;
			}

			Texture2D texture = GetHSVTexture((int)SliderValue(hSlider));
			if (texture == null)
			{
				return;
			}

			Rect content = hsvTexture.contentRect;
			if (content.width <= 0f || content.height <= 0f)
			{
				return;
			}

			float nx = Mathf.Clamp01(evt.localPosition.x / content.width);
			float ny = Mathf.Clamp01(evt.localPosition.y / content.height);

			int texX = Mathf.Clamp(Mathf.RoundToInt(nx * (texture.width - 1)), 0, texture.width - 1);
			int texY = Mathf.Clamp(Mathf.RoundToInt((1f - ny) * (texture.height - 1)), 0, texture.height - 1);

			PositionCursor(nx, ny);

			current = texture.GetPixel(texX, texY);
			current.a = SliderValue(aSlider) / RGBA_MAX;
			SetRGB(current);
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Positions the HSV cursor element using normalized coordinates within the picking area.
		/// </summary>
		private void PositionCursor(float nx, float ny)
		{
			if (hsvCursor == null || hsvTexture == null)
			{
				return;
			}
			Rect content = hsvTexture.contentRect;
			hsvCursor.style.left = nx * content.width;
			hsvCursor.style.top = ny * content.height;
		}

		/// <summary>
		/// Positions the HSV cursor from the current color's saturation/value.
		/// </summary>
		public void SetCursor()
		{
			Color hsv = TinyColor.RGBToHSV(current.r, current.g, current.b);
			// x axis = value (brightness), y axis = saturation (inverted because UI origin is top-left).
			PositionCursor(hsv.b, 1f - hsv.g);
		}

		/// <summary>
		/// Updates the picker color from a hexadecimal string.
		/// </summary>
		/// <param name="value">The hexadecimal color string.</param>
		public void UpdateHexValue(string value)
		{
			Color newColor = Hex.ToColor(value);
			SetColor(newColor);
		}

		/// <summary>
		/// Updates the hue from the slider and refreshes the color.
		/// </summary>
		public void UpdateHueSliderValue(float value)
		{
			SetBackground(hsvTexture, GetHSVTexture((int)value));
			SetInputValue(hInput, Mathf.RoundToInt(value));
			UpdateRGBFromHSV();
		}

		/// <summary>
		/// Updates the hue from the input field and refreshes the color.
		/// </summary>
		public void UpdateHueInputValue(int value)
		{
			value = Mathf.Clamp(value, 0, HUE_MAX);
			SetSliderValue(hSlider, value);
			SetInputValue(hInput, value);
			UpdateRGBFromHSV();
		}

		/// <summary>
		/// Updates the saturation from the slider and refreshes the color.
		/// </summary>
		public void UpdateSaturationSliderValue(float value)
		{
			SetInputValue(sInput, Mathf.RoundToInt(value));
			UpdateRGBFromHSV();
		}

		/// <summary>
		/// Updates the saturation from the input field and refreshes the color.
		/// </summary>
		public void UpdateSaturationInputValue(int value)
		{
			value = Mathf.Clamp(value, 0, SV_MAX);
			SetSliderValue(sSlider, value);
			SetInputValue(sInput, value);
			UpdateRGBFromHSV();
		}

		/// <summary>
		/// Updates the value (brightness) from the slider and refreshes the color.
		/// </summary>
		public void UpdateValueSliderValue(float value)
		{
			SetInputValue(vInput, Mathf.RoundToInt(value));
			UpdateRGBFromHSV();
		}

		/// <summary>
		/// Updates the value (brightness) from the input field and refreshes the color.
		/// </summary>
		public void UpdateValueInputValue(int value)
		{
			value = Mathf.Clamp(value, 0, SV_MAX);
			SetSliderValue(vSlider, value);
			SetInputValue(vInput, value);
			UpdateRGBFromHSV();
		}

		/// <summary>
		/// Updates the red channel from the slider and refreshes the color.
		/// </summary>
		public void UpdateRedSliderValue(float value)
		{
			SetInputValue(rInput, Mathf.RoundToInt(value));
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Updates the red channel from the input field and refreshes the color.
		/// </summary>
		public void UpdateRedInputValue(int value)
		{
			value = Mathf.Clamp(value, 0, RGBA_MAX);
			SetSliderValue(rSlider, value);
			SetInputValue(rInput, value);
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Updates the green channel from the slider and refreshes the color.
		/// </summary>
		public void UpdateGreenSliderValue(float value)
		{
			SetInputValue(gInput, Mathf.RoundToInt(value));
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Updates the green channel from the input field and refreshes the color.
		/// </summary>
		public void UpdateGreenInputValue(int value)
		{
			value = Mathf.Clamp(value, 0, RGBA_MAX);
			SetSliderValue(gSlider, value);
			SetInputValue(gInput, value);
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Updates the blue channel from the slider and refreshes the color.
		/// </summary>
		public void UpdateBlueSliderValue(float value)
		{
			SetInputValue(bInput, Mathf.RoundToInt(value));
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Updates the blue channel from the input field and refreshes the color.
		/// </summary>
		public void UpdateBlueInputValue(int value)
		{
			value = Mathf.Clamp(value, 0, RGBA_MAX);
			SetSliderValue(bSlider, value);
			SetInputValue(bInput, value);
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Updates the alpha channel from the slider and refreshes the color.
		/// </summary>
		public void UpdateAlphaSliderValue(float value)
		{
			SetInputValue(aInput, Mathf.RoundToInt(value));
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Updates the alpha channel from the input field and refreshes the color.
		/// </summary>
		public void UpdateAlphaInputValue(int value)
		{
			value = Mathf.Clamp(value, 0, RGBA_MAX);
			SetSliderValue(aSlider, value);
			SetInputValue(aInput, value);
			UpdateHSVFromRGB();
		}
	}
}
