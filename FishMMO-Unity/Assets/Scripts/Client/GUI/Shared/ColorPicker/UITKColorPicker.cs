using System;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit color picker supporting an HSV picking texture plus H/S/V and R/G/B/A sliders,
	/// inputs and a hex field. Spectrum textures are generated via <see cref="TinyColor"/> and
	/// bound to element background images.
	/// </summary>
	public class UITKColorPicker : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Modal;

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

		/// <summary>
		/// Name of the current color swatch element.
		/// </summary>
		private const string CURRENT_SWATCH_NAME = "color-current";
		/// <summary>
		/// Name of the HSV picking texture element.
		/// </summary>
		private const string HSV_TEXTURE_NAME = "hsv-texture";
		/// <summary>
		/// Name of the HSV cursor element.
		/// </summary>
		private const string HSV_CURSOR_NAME = "hsv-cursor";
		/// <summary>
		/// Name of the hex input field element.
		/// </summary>
		private const string HEX_INPUT_NAME = "hex-input";
		/// <summary>
		/// Name of the close button element.
		/// </summary>
		private const string CLOSE_BUTTON_NAME = "colorpicker-close-btn";

		/// <summary>
		/// Name of the hue slider element.
		/// </summary>
		private const string H_SLIDER_NAME = "h-slider";
		/// <summary>
		/// Name of the hue integer input field element.
		/// </summary>
		private const string H_INPUT_NAME = "h-input";
		/// <summary>
		/// Name of the hue slider background element.
		/// </summary>
		private const string H_BACKGROUND_NAME = "h-background";
		/// <summary>
		/// Name of the saturation slider element.
		/// </summary>
		private const string S_SLIDER_NAME = "s-slider";
		/// <summary>
		/// Name of the saturation integer input field element.
		/// </summary>
		private const string S_INPUT_NAME = "s-input";
		/// <summary>
		/// Name of the saturation slider background element.
		/// </summary>
		private const string S_BACKGROUND_NAME = "s-background";

		/// <summary>
		/// Name of the value (brightness) slider element.
		/// </summary>
		private const string V_SLIDER_NAME = "v-slider";
		/// <summary>
		/// Name of the value (brightness) integer input field element.
		/// </summary>
		private const string V_INPUT_NAME = "v-input";
		/// <summary>
		/// Name of the value slider background element.
		/// </summary>
		private const string V_BACKGROUND_NAME = "v-background";

		/// <summary>
		/// Name of the red channel slider element.
		/// </summary>
		private const string R_SLIDER_NAME = "r-slider";
		/// <summary>
		/// Name of the red channel integer input field element.
		/// </summary>
		private const string R_INPUT_NAME = "r-input";
		/// <summary>
		/// Name of the red channel slider background element.
		/// </summary>
		private const string R_BACKGROUND_NAME = "r-background";

		/// <summary>
		/// Name of the green channel slider element.
		/// </summary>
		private const string G_SLIDER_NAME = "g-slider";
		/// <summary>
		/// Name of the green channel integer input field element.
		/// </summary>
		private const string G_INPUT_NAME = "g-input";
		/// <summary>
		/// Name of the green channel slider background element.
		/// </summary>
		private const string G_BACKGROUND_NAME = "g-background";

		/// <summary>
		/// Name of the blue channel slider element.
		/// </summary>
		private const string B_SLIDER_NAME = "b-slider";
		/// <summary>
		/// Name of the blue channel integer input field element.
		/// </summary>
		private const string B_INPUT_NAME = "b-input";
		/// <summary>
		/// Name of the blue channel slider background element.
		/// </summary>
		private const string B_BACKGROUND_NAME = "b-background";

		/// <summary>
		/// Name of the alpha channel slider element.
		/// </summary>
		private const string A_SLIDER_NAME = "a-slider";
		/// <summary>
		/// Name of the alpha channel integer input field element.
		/// </summary>
		private const string A_INPUT_NAME = "a-input";
		/// <summary>
		/// Name of the alpha channel slider background element.
		/// </summary>
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
		/// A one-pixel-tall spectrum strip and the pixel buffer it is refilled from.
		/// </summary>
		/// <remarks>
		/// Every slider background used to be a brand new <see cref="Texture2D"/> built by
		/// <c>TinyColor.Generate*Spectrum</c>, and five of them were rebuilt on every value
		/// change — so dragging a slider allocated five textures a frame and dropped the
		/// previous five, which are native allocations the garbage collector does not hurry to
		/// reclaim. The strip is allocated once per channel and its pixels are rewritten in
		/// place instead.
		/// </remarks>
		private sealed class Strip
		{
			/// <summary>The texture bound to the element's background.</summary>
			public readonly Texture2D Texture;

			/// <summary>Scratch pixels, exactly one row wide.</summary>
			public readonly Color[] Pixels;

			/// <summary>Creates a strip of the given width.</summary>
			/// <param name="width">Number of horizontal samples.</param>
			public Strip(int width)
			{
				Pixels = new Color[width];
				Texture = new Texture2D(width, SLIDER_BACKGROUND_HEIGHT, TextureFormat.ARGB32, false)
				{
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					// Nothing else may free this; it is owned by the picker and destroyed with it.
					hideFlags = HideFlags.HideAndDontSave,
				};
			}

			/// <summary>Uploads the current pixel buffer.</summary>
			public void Apply()
			{
				Texture.SetPixels(Pixels);
				Texture.Apply(false);
			}
		}

		/// <summary>Hue spectrum behind the H slider.</summary>
		private Strip hStrip;
		/// <summary>Saturation spectrum behind the S slider.</summary>
		private Strip sStrip;
		/// <summary>Brightness spectrum behind the V slider. Static once built.</summary>
		private Strip vStrip;
		/// <summary>Red spectrum behind the R slider.</summary>
		private Strip rStrip;
		/// <summary>Green spectrum behind the G slider.</summary>
		private Strip gStrip;
		/// <summary>Blue spectrum behind the B slider.</summary>
		private Strip bStrip;
		/// <summary>Alpha spectrum behind the A slider. Static once built.</summary>
		private Strip aStrip;

		/// <summary>
		/// The saturation/value square, regenerated in place whenever the hue changes.
		/// </summary>
		/// <remarks>
		/// This replaces a 360-entry array of 192x192 textures. Fully populated that cache held
		/// 360 * 192 * 192 * 4 bytes — a little over 50 MB of texture memory that was never
		/// released, on a panel most players open once to pick a UI colour. One texture is
		/// enough: the hue only changes when the player moves the hue slider, and refilling
		/// 36,864 pixels costs less than the allocation it replaces.
		/// </remarks>
		private Texture2D hsvSurface;

		/// <summary>Scratch pixels for <see cref="hsvSurface"/>.</summary>
		private Color[] hsvPixels;

		/// <summary>Hue <see cref="hsvSurface"/> currently holds, or -1 when it is unbuilt.</summary>
		private int hsvSurfaceHue = -1;

		/// <summary>Pointer id captured by a drag on the saturation/value square, or -1.</summary>
		/// <remarks>
		/// A capture that is never released blocks every other element in the panel from
		/// receiving pointer events, which presents as the whole UI freezing, so every exit —
		/// pointer up, pointer cancel, losing the capture to something else, hiding the panel
		/// and destroying it — releases it.
		/// </remarks>
		private int hsvPointerId = -1;

		/// <summary>
		/// The currently selected color.
		/// </summary>
		private Color current = Color.red;

		/// <summary>
		/// When true, value-changed callbacks are ignored to prevent feedback loops during programmatic updates.
		/// </summary>
		private bool suppressCallbacks;

		/// <summary>
		/// When true, <see cref="OnColorChanged"/> is not raised.
		/// </summary>
		/// <remarks>
		/// Distinct from <see cref="suppressCallbacks"/>, which is about the picker's own
		/// elements talking to each other. This one is about the picker talking to whoever
		/// opened it, and it is set while the picker is being seeded rather than driven.
		/// </remarks>
		private bool suppressNotify;

		/// <summary>
		/// The current color swatch element.
		/// </summary>
		private VisualElement currentSwatch;
		/// <summary>
		/// The HSV texture display element.
		/// </summary>
		private VisualElement hsvTexture;
		/// <summary>
		/// The HSV cursor overlay element.
		/// </summary>
		private VisualElement hsvCursor;
		/// <summary>
		/// The hue slider background element.
		/// </summary>
		private VisualElement hBackground;
		/// <summary>
		/// The saturation slider background element.
		/// </summary>
		private VisualElement sBackground;
		/// <summary>
		/// The value slider background element.
		/// </summary>
		private VisualElement vBackground;
		/// <summary>
		/// The red channel slider background element.
		/// </summary>
		private VisualElement rBackground;
		/// <summary>
		/// The green channel slider background element.
		/// </summary>
		private VisualElement gBackground;
		/// <summary>
		/// The blue channel slider background element.
		/// </summary>
		private VisualElement bBackground;
		/// <summary>
		/// The alpha channel slider background element.
		/// </summary>
		private VisualElement aBackground;

		/// <summary>
		/// The hex text input field.
		/// </summary>
		private TextField hexInput;
		/// <summary>
		/// The hue slider.
		/// </summary>
		private Slider hSlider;
		/// <summary>
		/// The saturation slider.
		/// </summary>
		private Slider sSlider;
		/// <summary>
		/// The value (brightness) slider.
		/// </summary>
		private Slider vSlider;
		/// <summary>
		/// The red channel slider.
		/// </summary>
		private Slider rSlider;
		/// <summary>
		/// The green channel slider.
		/// </summary>
		private Slider gSlider;
		/// <summary>
		/// The blue channel slider.
		/// </summary>
		private Slider bSlider;
		/// <summary>
		/// The alpha channel slider.
		/// </summary>
		private Slider aSlider;
		/// <summary>
		/// The hue integer input field.
		/// </summary>
		private IntegerField hInput;
		/// <summary>
		/// The saturation integer input field.
		/// </summary>
		private IntegerField sInput;
		/// <summary>
		/// The value (brightness) integer input field.
		/// </summary>
		private IntegerField vInput;
		/// <summary>
		/// The red channel integer input field.
		/// </summary>
		private IntegerField rInput;
		/// <summary>
		/// The green channel integer input field.
		/// </summary>
		private IntegerField gInput;
		/// <summary>
		/// The blue channel integer input field.
		/// </summary>
		private IntegerField bInput;
		/// <summary>
		/// The alpha channel integer input field.
		/// </summary>
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
				/* Down, move, up and the two capture-loss events. A picker that only listened
				 * for PointerDown could be clicked but not dragged, which is the one thing a
				 * saturation/value square is for. */
				hsvTexture.RegisterCallback<PointerDownEvent>(OnHSVPointerDown);
				hsvTexture.RegisterCallback<PointerMoveEvent>(OnHSVPointerMove);
				hsvTexture.RegisterCallback<PointerUpEvent>(OnHSVPointerUp);
				hsvTexture.RegisterCallback<PointerCancelEvent>(OnHSVPointerUp);
				hsvTexture.RegisterCallback<PointerCaptureOutEvent>(OnHSVPointerCaptureOut);
			}

			Button closeButton = Root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += OnClick_Close;
			}

			Root.UnregisterCallback<KeyDownEvent>(OnPickerKeyDown, TrickleDown.TrickleDown);
			Root.RegisterCallback<KeyDownEvent>(OnPickerKeyDown, TrickleDown.TrickleDown);

			// Static spectrum backgrounds that never change.
			BuildStaticStrips();

			SetColor(InitialColor);
		}

		/// <summary>
		/// Re-binds the textures and re-applies the colour after a visual tree rebuild.
		/// </summary>
		/// <remarks>
		/// The strips survive the rebuild but the elements they were bound to do not, so a
		/// re-shown picker came back with no spectrum behind any of its sliders.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			BuildStaticStrips();
			ApplyColor(current, notify: false);
		}

		/// <summary>
		/// Re-applies the picker's colour to the tree the player will actually see.
		/// </summary>
		protected override void OnAfterShow()
		{
			BuildStaticStrips();
			ApplyColor(current, notify: false);
		}

		/// <summary>
		/// Closes the picker from the close button.
		/// </summary>
		private void OnClick_Close()
		{
			Hide();
		}

		/// <summary>
		/// Escape closes the picker; Enter commits whatever is typed in the hex field.
		/// </summary>
		private void OnPickerKeyDown(KeyDownEvent evt)
		{
			if (!Visible)
			{
				return;
			}

			switch (evt.keyCode)
			{
				case KeyCode.Escape:
					evt.StopPropagation();
					Hide();
					return;
				case KeyCode.Return:
				case KeyCode.KeypadEnter:
					/* Only when the caret is in the hex field. Enter anywhere else in the picker
					 * belongs to whatever is focused there, and stealing it would stop the
					 * numeric fields committing what was typed into them. */
					if (hexInput != null && evt.target is VisualElement target && hexInput.Contains(target))
					{
						evt.StopPropagation();
						UpdateHexValue(hexInput.value);
					}
					return;
			}
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
		/// Returns the saturation/value square for a hue, refilling it only when the hue moved.
		/// </summary>
		/// <param name="hueIndex">Hue in degrees, 0-359.</param>
		/// <returns>The shared surface, or null if it could not be created.</returns>
		private Texture2D GetHSVTexture(int hueIndex)
		{
			hueIndex = Mathf.Clamp(hueIndex, 0, HUE_MAX);

			if (hsvSurface == null)
			{
				hsvPixels = new Color[HSV_TEXTURE_WIDTH * HSV_TEXTURE_HEIGHT];
				hsvSurface = new Texture2D(HSV_TEXTURE_WIDTH, HSV_TEXTURE_HEIGHT, TextureFormat.ARGB32, false)
				{
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.HideAndDontSave,
				};
				hsvSurfaceHue = -1;
			}

			if (hsvSurfaceHue == hueIndex)
			{
				return hsvSurface;
			}

			/* Same mapping the old generator used: X is value, Y is saturation, with Y counted
			 * from the bottom of the texture. The picking code below relies on it. */
			float dValue = 1.0f / (HSV_TEXTURE_WIDTH - 1);
			float dSaturation = 1.0f / (HSV_TEXTURE_HEIGHT - 1);

			for (int y = 0; y < HSV_TEXTURE_HEIGHT; ++y)
			{
				float saturation = dSaturation * y;
				int row = y * HSV_TEXTURE_WIDTH;
				for (int x = 0; x < HSV_TEXTURE_WIDTH; ++x)
				{
					hsvPixels[row + x] = TinyColor.HSVToRGB(hueIndex, saturation, dValue * x, 1.0f);
				}
			}

			hsvSurface.SetPixels(hsvPixels);
			hsvSurface.Apply(false);
			hsvSurfaceHue = hueIndex;
			return hsvSurface;
		}

		/// <summary>
		/// Creates a strip on first use.
		/// </summary>
		/// <param name="strip">The strip field to fill in.</param>
		/// <param name="width">Number of horizontal samples.</param>
		/// <returns>The strip.</returns>
		private static Strip EnsureStrip(ref Strip strip, int width)
		{
			if (strip == null)
			{
				strip = new Strip(width);
			}
			return strip;
		}

		/// <summary>
		/// Rewrites the hue spectrum in place and binds it to its element.
		/// </summary>
		private void FillHueStrip(float alpha)
		{
			Strip strip = EnsureStrip(ref hStrip, NUM_HUES);
			float d = 360.0f / strip.Pixels.Length;
			for (int x = 0; x < strip.Pixels.Length; ++x)
			{
				strip.Pixels[x] = TinyColor.HSVToRGB(d * x, 1.0f, 1.0f, alpha);
			}
			strip.Apply();
			SetBackground(hBackground, strip.Texture);
		}

		/// <summary>
		/// Rewrites the saturation spectrum in place and binds it to its element.
		/// </summary>
		private void FillSaturationStrip(float hue, float value, float alpha)
		{
			Strip strip = EnsureStrip(ref sStrip, SLIDER_BACKGROUND_WIDTH);
			float d = 1.0f / strip.Pixels.Length;
			for (int x = 0; x < strip.Pixels.Length; ++x)
			{
				strip.Pixels[x] = TinyColor.HSVToRGB(hue, d * x, value, alpha);
			}
			strip.Apply();
			SetBackground(sBackground, strip.Texture);
		}

		/// <summary>
		/// Rewrites one RGB channel spectrum in place and binds it to its element.
		/// </summary>
		/// <param name="strip">The strip field for this channel.</param>
		/// <param name="element">The element to bind the texture to.</param>
		/// <param name="channel">0 = red, 1 = green, 2 = blue.</param>
		/// <param name="colour">The colour the other two channels are read from.</param>
		private void FillChannelStrip(ref Strip strip, VisualElement element, int channel, Color colour)
		{
			Strip s = EnsureStrip(ref strip, SLIDER_BACKGROUND_WIDTH);
			float d = 1.0f / s.Pixels.Length;
			for (int x = 0; x < s.Pixels.Length; ++x)
			{
				float ramp = d * x;
				s.Pixels[x] = channel == 0 ? new Color(ramp, colour.g, colour.b, colour.a)
					: channel == 1 ? new Color(colour.r, ramp, colour.b, colour.a)
					: new Color(colour.r, colour.g, ramp, colour.a);
			}
			s.Apply();
			SetBackground(element, s.Texture);
		}

		/// <summary>
		/// Builds the two spectra that never change, once.
		/// </summary>
		private void BuildStaticStrips()
		{
			if (vStrip == null)
			{
				Strip strip = EnsureStrip(ref vStrip, HSV_TEXTURE_WIDTH);
				float d = 1.0f / strip.Pixels.Length;
				for (int x = 0; x < strip.Pixels.Length; ++x)
				{
					float brightness = d * x;
					strip.Pixels[x] = new Color(brightness, brightness, brightness, 1.0f);
				}
				strip.Apply();
			}
			SetBackground(vBackground, vStrip.Texture);

			if (aStrip == null)
			{
				Strip strip = EnsureStrip(ref aStrip, HSV_TEXTURE_WIDTH);
				int width = strip.Pixels.Length;
				float d = 1.0f / width;
				for (int x = 0; x < width; ++x)
				{
					// RGB fades from white down to black as alpha rises, as it always has.
					float rgb = d * (width - x);
					strip.Pixels[x] = new Color(rgb, rgb, rgb, d * x);
				}
				strip.Apply();
			}
			SetBackground(aBackground, aStrip.Texture);
		}

		/// <summary>
		/// Destroys every texture this picker owns.
		/// </summary>
		/// <remarks>
		/// Textures created from script are native objects; letting the managed references go
		/// does not free them, so a panel that is destroyed without this leaks the lot.
		/// </remarks>
		private void ReleaseTextures()
		{
			DestroyStrip(ref hStrip);
			DestroyStrip(ref sStrip);
			DestroyStrip(ref vStrip);
			DestroyStrip(ref rStrip);
			DestroyStrip(ref gStrip);
			DestroyStrip(ref bStrip);
			DestroyStrip(ref aStrip);

			if (hsvSurface != null)
			{
				Destroy(hsvSurface);
				hsvSurface = null;
			}
			hsvPixels = null;
			hsvSurfaceHue = -1;
		}

		/// <summary>
		/// Destroys one strip's texture and forgets it.
		/// </summary>
		private void DestroyStrip(ref Strip strip)
		{
			if (strip == null)
			{
				return;
			}
			if (strip.Texture != null)
			{
				Destroy(strip.Texture);
			}
			strip = null;
		}

		/// <summary>
		/// Shows the picker seeded with a colour, reporting each change to one caller.
		/// </summary>
		/// <param name="initial">Colour to start from.</param>
		/// <param name="onChanged">Invoked with the selected colour as it changes.</param>
		/// <remarks>
		/// The picker is a single shared panel, so <see cref="OnColorChanged"/> is a field that
		/// every caller writes to in turn. Assigning it directly means whoever opened the picker
		/// last silently keeps receiving colours after they are done with it — this replaces the
		/// subscriber outright on each open, so exactly one caller is ever listening.
		/// </remarks>
		public void Open(Color initial, Action<Color> onChanged)
		{
			OnColorChanged = onChanged;
			InitialColor = initial;

			/* The colour is set as state before Show, and written into the tree by OnAfterShow.
			 * Writing it into the elements here would be lost — enabling the document re-clones
			 * the UXML — and letting OnAfterShow run against the previous colour would report
			 * that stale colour to the caller that has only just subscribed. */
			current = initial;

			Show();

			// Already visible: Show is a no-op, so seed the live tree directly.
			ApplyColor(initial, notify: false);
		}

		/// <summary>
		/// Hides the picker and stops reporting changes to whoever opened it.
		/// </summary>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		/// <remarks>
		/// The override is on <c>Hide(bool)</c>, not on <c>Hide()</c>. <c>Hide()</c> forwards
		/// here, but quit-to-login calls <c>Hide(false)</c> directly — so an override on the
		/// parameterless form alone left the Options panel still subscribed to colour changes
		/// after the player had returned to the login screen.
		/// </remarks>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (overrideIsAlwaysOpen || Document == null)
			{
				// The base refused the hide; the picker is still up and still in use.
				return;
			}

			ReleaseHSVPointer();
			OnColorChanged = null;
		}

		/// <summary>
		/// Releases every texture and any outstanding pointer capture.
		/// </summary>
		public override void OnDestroying()
		{
			ReleaseHSVPointer();
			ReleaseTextures();
		}

		/// <summary>
		/// Sets the picker color and refreshes all sliders, inputs, backgrounds and the cursor.
		/// </summary>
		/// <param name="color">The color to set.</param>
		public void SetColor(Color color)
		{
			ApplyColor(color, notify: true);
		}

		/// <summary>
		/// Sets the picker colour, optionally without telling the subscriber about it.
		/// </summary>
		/// <param name="color">The colour to set.</param>
		/// <param name="notify">
		/// False while seeding the picker from a caller's current colour. Reporting a colour the
		/// caller already has looks to it like the player changed something — and its handler is
		/// free to be expensive, which is exactly what the Options panel's is.
		/// </param>
		private void ApplyColor(Color color, bool notify)
		{
			current = color;
			if (currentSwatch != null)
			{
				currentSwatch.style.backgroundColor = current;
			}

			bool previous = suppressNotify;
			suppressNotify = !notify;
			try
			{
				SetHSV(TinyColor.RGBToHSV(current.r, current.g, current.b));
				SetRGB(current);
				SetSliderValue(aSlider, current.a * RGBA_MAX);
				SetInputValue(aInput, Mathf.RoundToInt(current.a * RGBA_MAX));
				UpdateBackgroundSprites();
			}
			finally
			{
				suppressNotify = previous;
			}
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

		/// <summary>
		/// Returns the current value of the slider, or zero if the slider is null.
		/// </summary>
		/// <param name="slider">The slider to read.</param>
		/// <returns>The slider's value, or 0f if null.</returns>
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
			SetBackground(hsvTexture, GetHSVTexture((int)SliderValue(hSlider)));

			FillHueStrip(current.a);
			FillSaturationStrip(SliderValue(hSlider), SliderValue(vSlider) * 0.01f, current.a);
			FillChannelStrip(ref rStrip, rBackground, 0, current);
			FillChannelStrip(ref gStrip, gBackground, 1, current);
			FillChannelStrip(ref bStrip, bBackground, 2, current);

			if (currentSwatch != null)
			{
				currentSwatch.style.backgroundColor = current;
			}
			SetCursor();

			if (!suppressNotify)
			{
				OnColorChanged?.Invoke(current);
			}
		}

		/// <summary>
		/// Begins a pick on the saturation/value square and captures the pointer for the drag.
		/// </summary>
		private void OnHSVPointerDown(PointerDownEvent evt)
		{
			if (hsvTexture == null)
			{
				return;
			}

			hsvTexture.CapturePointer(evt.pointerId);
			hsvPointerId = evt.pointerId;

			PickFromSquare(evt.localPosition);
			evt.StopPropagation();
		}

		/// <summary>
		/// Continues a pick while the pointer is held down.
		/// </summary>
		private void OnHSVPointerMove(PointerMoveEvent evt)
		{
			if (hsvPointerId == -1 || evt.pointerId != hsvPointerId)
			{
				return;
			}

			PickFromSquare(evt.localPosition);
			evt.StopPropagation();
		}

		/// <summary>
		/// Ends a pick and gives the pointer back.
		/// </summary>
		private void OnHSVPointerUp(PointerUpEvent evt)
		{
			if (hsvPointerId == -1 || evt.pointerId != hsvPointerId)
			{
				return;
			}

			ReleaseHSVPointer();
			evt.StopPropagation();
		}

		/// <summary>
		/// Ends a pick that was cancelled rather than completed.
		/// </summary>
		private void OnHSVPointerUp(PointerCancelEvent evt)
		{
			if (hsvPointerId == -1 || evt.pointerId != hsvPointerId)
			{
				return;
			}

			ReleaseHSVPointer();
		}

		/// <summary>
		/// Forgets the capture when something else takes it.
		/// </summary>
		/// <remarks>
		/// Without this the picker would still believe it owns a pointer it has lost, and would
		/// keep tracking a drag that belongs to another element.
		/// </remarks>
		private void OnHSVPointerCaptureOut(PointerCaptureOutEvent evt)
		{
			if (evt.pointerId == hsvPointerId)
			{
				hsvPointerId = -1;
			}
		}

		/// <summary>
		/// Releases the pointer captured by a square drag, if there is one.
		/// </summary>
		/// <remarks>
		/// A capture that outlives its drag silently swallows every pointer event in the panel —
		/// buttons stop responding, sliders stop moving, and the only symptom is that the UI has
		/// stopped working. Every path out of a drag comes through here.
		/// </remarks>
		private void ReleaseHSVPointer()
		{
			if (hsvPointerId == -1)
			{
				return;
			}

			int pointerId = hsvPointerId;
			hsvPointerId = -1;

			if (hsvTexture != null && hsvTexture.HasPointerCapture(pointerId))
			{
				hsvTexture.ReleasePointer(pointerId);
			}
		}

		/// <summary>
		/// Sets the colour from a position inside the saturation/value square.
		/// </summary>
		/// <param name="localPosition">Pointer position in the square's local space.</param>
		/// <remarks>
		/// The colour is computed from the axes rather than read back out of the texture. Reading
		/// a pixel and converting it back to HSV throws the hue away wherever the square is grey
		/// — the whole left edge and the whole bottom edge — so clicking there reset the hue
		/// slider to red and repainted the square, which is not what the player asked for.
		/// </remarks>
		private void PickFromSquare(Vector3 localPosition)
		{
			if (hsvTexture == null)
			{
				return;
			}

			Rect content = hsvTexture.contentRect;
			if (content.width <= 0f || content.height <= 0f)
			{
				return;
			}

			float nx = Mathf.Clamp01(localPosition.x / content.width);
			float ny = Mathf.Clamp01(localPosition.y / content.height);

			PositionCursor(nx, ny);

			// X is value, Y is saturation counted from the bottom. Hue stays where it was.
			float hue = SliderValue(hSlider);
			float value = nx;
			float saturation = 1.0f - ny;

			SetSliderValue(sSlider, saturation * SV_MAX);
			SetSliderValue(vSlider, value * SV_MAX);
			SetInputValue(sInput, Mathf.RoundToInt(saturation * SV_MAX));
			SetInputValue(vInput, Mathf.RoundToInt(value * SV_MAX));

			Color rgb = TinyColor.HSVToRGB(hue, saturation, value);
			current = new Color(rgb.r, rgb.g, rgb.b, SliderValue(aSlider) / RGBA_MAX);
			SetRGB(current);
			UpdateBackgroundSprites();
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
			if (!TryParseHex(value, out Color newColor))
			{
				/* Malformed input is put back rather than applied. Hex.ToColor treats any
				 * unparseable pair as zero and anything shorter than six characters as white, so
				 * a half-typed or mistyped code used to silently repaint the colour — and, worse,
				 * report that colour to whoever opened the picker. */
				SetHexText(current.ToHex());
				return;
			}

			SetColor(newColor);
		}

		/// <summary>
		/// Parses a hex colour, accepting RGB, RGBA, RRGGBB and RRGGBBAA with an optional '#'.
		/// </summary>
		/// <param name="value">The text the player typed.</param>
		/// <param name="color">The parsed colour.</param>
		/// <returns>False when the text is not a complete, valid hex colour.</returns>
		private static bool TryParseHex(string value, out Color color)
		{
			color = Color.white;

			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}

			string text = value.Trim();
			if (text.Length > 0 && text[0] == '#')
			{
				text = text.Substring(1);
			}

			// Short forms are expanded by doubling each digit, the way CSS does.
			bool shortForm = text.Length == 3 || text.Length == 4;
			if (!shortForm && text.Length != 6 && text.Length != 8)
			{
				return false;
			}

			for (int i = 0; i < text.Length; ++i)
			{
				char c = text[i];
				bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
				if (!isHex)
				{
					return false;
				}
			}

			if (shortForm)
			{
				System.Text.StringBuilder expanded = new System.Text.StringBuilder(text.Length * 2);
				for (int i = 0; i < text.Length; ++i)
				{
					expanded.Append(text[i]).Append(text[i]);
				}
				text = expanded.ToString();
			}

			float r = Hex.ToInt(text.Substring(0, 2));
			float g = Hex.ToInt(text.Substring(2, 2));
			float b = Hex.ToInt(text.Substring(4, 2));
			float a = text.Length == 8 ? Hex.ToInt(text.Substring(6, 2)) : RGBA_MAX;

			color = new Color(r / RGBA_MAX, g / RGBA_MAX, b / RGBA_MAX, a / RGBA_MAX);
			return true;
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
