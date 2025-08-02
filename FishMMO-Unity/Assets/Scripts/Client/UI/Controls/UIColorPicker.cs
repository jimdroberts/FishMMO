using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for picking colors using HSV and RGB sliders, and displaying the color in various formats.
	/// </summary>
	public class UIColorPicker : UIControl
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
		/// <summary>
		/// Maximum number of new palettes supported.
		/// </summary>
		public const int MAX_NEW_PALETTES = 32;

		/// <summary>
		/// Initial color for the picker.
		/// </summary>
		public Color InitialColor = Color.red;
		/// <summary>
		/// Cached HSV sprites for each hue value.
		/// </summary>
		public Sprite[] CachedHSVSprites = new Sprite[NUM_HUES];
		/// <summary>
		/// Image displaying the currently selected color.
		/// </summary>
		public Image Current = null;
		/// <summary>
		/// Image representing the cursor position in the HSV texture.
		/// </summary>
		public Image Cursor = null;

		/// <summary>
		/// Input field for hexadecimal color value.
		/// </summary>
		public RageInputField HexInputField = null;

		/// <summary>
		/// Image and slider/input for hue selection.
		/// </summary>
		public Image HTexture = null;
		public Image HBackground = null;
		public RageSlider HSlider = null;
		public RageInputField HInputField = null;

		/// <summary>
		/// Image and slider/input for saturation selection.
		/// </summary>
		public Image SBackground = null;
		public RageSlider SSlider = null;
		public RageInputField SInputField = null;

		/// <summary>
		/// Image and slider/input for value (brightness) selection.
		/// </summary>
		public Image VBackground = null;
		public RageSlider VSlider = null;
		public RageInputField VInputField = null;

		/// <summary>
		/// Image and slider/input for red color channel.
		/// </summary>
		public Image RBackground = null;
		public RageSlider RSlider = null;
		public RageInputField RInputField = null;

		/// <summary>
		/// Image and slider/input for green color channel.
		/// </summary>
		public Image GBackground = null;
		public RageSlider GSlider = null;
		public RageInputField GInputField = null;

		/// <summary>
		/// Image and slider/input for blue color channel.
		/// </summary>
		public Image BBackground = null;
		public RageSlider BSlider = null;
		public RageInputField BInputField = null;

		/// <summary>
		/// Image and slider/input for alpha (transparency) channel.
		/// </summary>
		public Image ABackground = null;
		public RageSlider ASlider = null;
		public RageInputField AInputField = null;

		/// <summary>
		/// Cache HSV sprites and set the initial color.
		/// </summary>
		public override void OnStarting()
		{
			//cache HSV sprites
			for (int i = 0; i < NUM_HUES; ++i)
			{
				GetHSVSprite(i);
			}

			//default backgrounds
			VBackground.sprite = TextureToSprite(TinyColor.GenerateBrightnessSpectrum(HSV_TEXTURE_WIDTH, SLIDER_BACKGROUND_HEIGHT));
			ABackground.sprite = TextureToSprite(TinyColor.GenerateAlphaSpectrum(HSV_TEXTURE_WIDTH, SLIDER_BACKGROUND_HEIGHT));

			SetColor(InitialColor);
		}

		/// <summary>
		/// Set the color of the picker and update all related UI elements.
		/// </summary>
		/// <param name="color">The color to set.</param>
		public void SetColor(Color color)
		{
			Current.color = color;
			SetHSV(TinyColor.RGBToHSV(Current.color.r, Current.color.g, Current.color.b));
			SetRGB(Current.color);
			ASlider.SetValue(Current.color.a * RGBA_MAX);
			AInputField.SetText(ASlider.value.ToString());
			UpdateBackgroundSprites();
		}

		private Sprite TextureToSprite(Texture2D texture)
		{
			if (texture == null)
			{
				return null;
			}
			return Sprite.Create(texture, new Rect(0.0f, 0.0f, texture.width, texture.height), Vector2.zero, 100.0f);
		}

		private Sprite GetHSVSprite(int hueIndex)
		{
			hueIndex = Mathf.Clamp(hueIndex, 0, HUE_MAX);
			Sprite hsvSprite = CachedHSVSprites[hueIndex];
			if (hsvSprite == null)
			{
				Texture2D hsvTexture = TinyColor.GenerateHSVTexture(hueIndex, HSV_TEXTURE_WIDTH, HSV_TEXTURE_HEIGHT);
				if (hsvTexture == null)
				{
					return null;
				}
				hsvSprite = TextureToSprite(hsvTexture);
				if (hsvSprite == null)
				{
					return null;
				}
				CachedHSVSprites[hueIndex] = hsvSprite;
			}
			return hsvSprite;
		}

		private void UpdateHSVFromRGB()
		{
			Current.color = new Color(RSlider.value / RGBA_MAX, GSlider.value / RGBA_MAX, BSlider.value / RGBA_MAX, ASlider.value / RGBA_MAX);
			SetHSV(TinyColor.RGBToHSV(Current.color.r, Current.color.g, Current.color.b));
			UpdateBackgroundSprites();
		}

		private void UpdateRGBFromHSV()
		{
			Color rgb = TinyColor.HSVToRGB(HSlider.value, SSlider.value * 0.01f, VSlider.value * 0.01f);
			Current.color = new Color(rgb.r, rgb.g, rgb.b, Current.color.a);
			SetRGB(Current.color);
			UpdateBackgroundSprites();
		}

		private void SetHSV(Color HSV)
		{
			HSlider.SetValue(Mathf.RoundToInt(HSV.r));
			SSlider.SetValue(Mathf.RoundToInt(HSV.g * SV_MAX));
			VSlider.SetValue(Mathf.RoundToInt(HSV.b * SV_MAX));
			HInputField.SetText(HSlider.value.ToString());
			SInputField.SetText(SSlider.value.ToString());
			VInputField.SetText(VSlider.value.ToString());
			HexInputField.text = Current.color.ToHex();
		}

		private void SetRGB(Color RGB)
		{
			RSlider.SetValue(RGB.r * RGBA_MAX);
			GSlider.SetValue(RGB.g * RGBA_MAX);
			BSlider.SetValue(RGB.b * RGBA_MAX);
			RInputField.SetText(RSlider.value.ToString());
			GInputField.SetText(GSlider.value.ToString());
			BInputField.SetText(BSlider.value.ToString());
			HexInputField.text = Current.color.ToHex();
		}

		private void UpdateBackgroundSprites()
		{
			Sprite hsvSprite = GetHSVSprite((int)HSlider.value);
			if (hsvSprite != null)
			{
				HTexture.sprite = hsvSprite;
			}
			HBackground.sprite = TextureToSprite(TinyColor.GenerateColorSpectrum(Current.color.a, NUM_HUES, SLIDER_BACKGROUND_HEIGHT));
			SBackground.sprite = TextureToSprite(TinyColor.GenerateSaturationSpectrum(HSlider.value, VSlider.value * 0.01f, Current.color.a, SLIDER_BACKGROUND_WIDTH, SLIDER_BACKGROUND_HEIGHT));
			RBackground.sprite = TextureToSprite(TinyColor.GenerateRedSpectrum(Current.color.g, Current.color.b, Current.color.a, SLIDER_BACKGROUND_WIDTH, SLIDER_BACKGROUND_HEIGHT));
			GBackground.sprite = TextureToSprite(TinyColor.GenerateGreenSpectrum(Current.color.r, Current.color.b, Current.color.a, SLIDER_BACKGROUND_WIDTH, SLIDER_BACKGROUND_HEIGHT));
			BBackground.sprite = TextureToSprite(TinyColor.GenerateBlueSpectrum(Current.color.r, Current.color.g, Current.color.a, SLIDER_BACKGROUND_WIDTH, SLIDER_BACKGROUND_HEIGHT));
			SetCursor();
		}

		/// <summary>
		/// Pick a color from the texture based on the cursor's position and update the color picker.
		/// </summary>
		/// <param name="baseData">Event data containing the pointer's position.</param>
		public void PickTexture(BaseEventData baseData)
		{
			PointerEventData data = baseData as PointerEventData;
			Vector2 position;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(HTexture.rectTransform, data.pressPosition, data.pressEventCamera, out position))
			{
				return;
			}
			float offsetX = HTexture.sprite.texture.width * 0.5f;
			float offsetY = HTexture.sprite.texture.height * 0.5f;
			int x = Mathf.Clamp(Mathf.RoundToInt(offsetX + position.x), 0, HTexture.sprite.texture.width - 1);
			int y = Mathf.Clamp(Mathf.RoundToInt(offsetY + position.y), 0, HTexture.sprite.texture.height - 1);
			Cursor.rectTransform.anchoredPosition = new Vector2(x - (HTexture.sprite.texture.width * 0.5f), y - (HTexture.sprite.texture.height * 0.5f));
			Current.color = HTexture.sprite.texture.GetPixel(x, y);
			SetRGB(Current.color);
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Set the cursor's position based on the current color's HSV values.
		/// </summary>
		public void SetCursor()
		{
			Color HSV = TinyColor.RGBToHSV(Current.color.r, Current.color.g, Current.color.b);
			float y = (HSV.g * HTexture.sprite.texture.height) - (HTexture.sprite.texture.height * 0.5f);
			float x = (HSV.b * HTexture.sprite.texture.width) - (HTexture.sprite.texture.width * 0.5f);
			Cursor.rectTransform.anchoredPosition = new Vector2(x, y);
		}

		/// <summary>
		/// Update the color picker value from a hexadecimal string.
		/// </summary>
		/// <param name="value">The hexadecimal color string.</param>
		public void UpdateHexValue(string value)
		{
			Color newColor = Hex.ToColor(value);
			SetColor(newColor);
		}

		/// <summary>
		/// Update the hue slider and related values when the hue is changed.
		/// </summary>
		/// <param name="value">The new hue value.</param>
		public void UpdateHueSliderValue(float value)
		{
			Sprite hsvSprite = GetHSVSprite((int)value);
			if (hsvSprite != null)
			{
				HTexture.sprite = hsvSprite;
			}
			HInputField.SetText(value.ToString());
			UpdateRGBFromHSV();
		}
		/// <summary>
		/// Update the hue value from an input field and refresh the UI.
		/// </summary>
		/// <param name="value">The hue value as a string.</param>
		public void UpdateHueInputValue(string value)
		{
			int newValue;
			if (!int.TryParse(value, out newValue))
			{
				return;
			}
			newValue = Mathf.Clamp(newValue, 0, HUE_MAX);
			HSlider.SetValue(newValue);
			HInputField.SetText(newValue.ToString());
			UpdateRGBFromHSV();
		}

		/// <summary>
		/// Update the saturation value and refresh the color display.
		/// </summary>
		/// <param name="value">The new saturation value.</param>
		public void UpdateSaturationSliderValue(float value)
		{
			SInputField.SetText(value.ToString());
			UpdateRGBFromHSV();
		}
		/// <summary>
		/// Update the saturation value from an input field and refresh the UI.
		/// </summary>
		/// <param name="value">The saturation value as a string.</param>
		public void UpdateSaturationInputValue(string value)
		{
			int newValue;
			if (!int.TryParse(value, out newValue))
			{
				return;
			}
			newValue = Mathf.Clamp(newValue, 0, SV_MAX);
			SSlider.SetValue(newValue);
			SInputField.SetText(newValue.ToString());
			UpdateRGBFromHSV();
		}

		/// <summary>
		/// Update the value (brightness) slider and refresh the color display.
		/// </summary>
		/// <param name="value">The new value (brightness) value.</param>
		public void UpdateValueSliderValue(float value)
		{
			VInputField.SetText(value.ToString());
			UpdateRGBFromHSV();
		}
		/// <summary>
		/// Update the value (brightness) from an input field and refresh the UI.
		/// </summary>
		/// <param name="value">The value (brightness) as a string.</param>
		public void UpdateValueInputValue(string value)
		{
			int newValue;
			if (!int.TryParse(value, out newValue))
			{
				return;
			}
			newValue = Mathf.Clamp(newValue, 0, SV_MAX);
			VSlider.SetValue(newValue);
			VInputField.SetText(newValue.ToString());
			UpdateRGBFromHSV();
		}

		/// <summary>
		/// Update the red color channel slider and refresh the color display.
		/// </summary>
		/// <param name="value">The new red value.</param>
		public void UpdateRedSliderValue(float value)
		{
			RInputField.SetText(value.ToString());
			UpdateHSVFromRGB();
		}
		/// <summary>
		/// Update the red color channel value from an input field and refresh the UI.
		/// </summary>
		/// <param name="value">The red value as a string.</param>
		public void UpdateRedInputValue(string value)
		{
			int newValue;
			if (!int.TryParse(value, out newValue))
			{
				return;
			}
			newValue = Mathf.Clamp(newValue, 0, RGBA_MAX);
			RSlider.SetValue(newValue);
			RInputField.SetText(newValue.ToString());
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Update the green color channel slider and refresh the color display.
		/// </summary>
		/// <param name="value">The new green value.</param>
		public void UpdateGreenSliderValue(float value)
		{
			GInputField.SetText(value.ToString());
			UpdateHSVFromRGB();
		}
		/// <summary>
		/// Update the green color channel value from an input field and refresh the UI.
		/// </summary>
		/// <param name="value">The green value as a string.</param>
		public void UpdateGreenInputValue(string value)
		{
			int newValue;
			if (!int.TryParse(value, out newValue))
			{
				return;
			}
			newValue = Mathf.Clamp(newValue, 0, RGBA_MAX);
			GSlider.SetValue(newValue);
			GInputField.SetText(newValue.ToString());
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Update the blue color channel slider and refresh the color display.
		/// </summary>
		/// <param name="value">The new blue value.</param>
		public void UpdateBlueSliderValue(float value)
		{
			BInputField.SetText(value.ToString());
			UpdateHSVFromRGB();
		}
		/// <summary>
		/// Update the blue color channel value from an input field and refresh the UI.
		/// </summary>
		/// <param name="value">The blue value as a string.</param>
		public void UpdateBlueInputValue(string value)
		{
			int newValue;
			if (!int.TryParse(value, out newValue))
			{
				return;
			}
			newValue = Mathf.Clamp(newValue, 0, RGBA_MAX);
			BSlider.SetValue(newValue);
			BInputField.SetText(newValue.ToString());
			UpdateHSVFromRGB();
		}

		/// <summary>
		/// Update the alpha (transparency) slider and refresh the color display.
		/// </summary>
		/// <param name="value">The new alpha value.</param>
		public void UpdateAlphaSliderValue(float value)
		{
			AInputField.SetText(value.ToString());
			UpdateHSVFromRGB();
		}
		/// <summary>
		/// Update the alpha (transparency) value from an input field and refresh the UI.
		/// </summary>
		/// <param name="value">The alpha value as a string.</param>
		public void UpdateAlphaInputValue(string value)
		{
			int newValue;
			if (!int.TryParse(value, out newValue))
			{
				return;
			}
			newValue = Mathf.Clamp(newValue, 0, RGBA_MAX);
			ASlider.SetValue(newValue);
			AInputField.SetText(newValue.ToString());
			UpdateHSVFromRGB();
		}
	}
}