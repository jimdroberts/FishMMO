using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIColorPicker : UIControl
	{
		public const int SLIDER_BACKGROUND_WIDTH = 134;
		public const int SLIDER_BACKGROUND_HEIGHT = 1;
		public const int HSV_TEXTURE_WIDTH = 192;
		public const int HSV_TEXTURE_HEIGHT = 192;
		public const int NUM_HUES = 360;
		public const int HUE_MAX = 359;
		public const int SV_MAX = 100;
		public const int RGBA_MAX = 255;
		public const int MAX_NEW_PALETTES = 32;

		public Color InitialColor = Color.red;
		public Sprite[] CachedHSVSprites = new Sprite[NUM_HUES];
		public Image Current = null;
		public Image Cursor = null;

		public RageInputField HexInputField = null;

		public Image HTexture = null;
		public Image HBackground = null;
		public RageSlider HSlider = null;
		public RageInputField HInputField = null;

		public Image SBackground = null;
		public RageSlider SSlider = null;
		public RageInputField SInputField = null;

		public Image VBackground = null;
		public RageSlider VSlider = null;
		public RageInputField VInputField = null;

		public Image RBackground = null;
		public RageSlider RSlider = null;
		public RageInputField RInputField = null;

		public Image GBackground = null;
		public RageSlider GSlider = null;
		public RageInputField GInputField = null;

		public Image BBackground = null;
		public RageSlider BSlider = null;
		public RageInputField BInputField = null;

		public Image ABackground = null;
		public RageSlider ASlider = null;
		public RageInputField AInputField = null;

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

		public void SetCursor()
		{
			Color HSV = TinyColor.RGBToHSV(Current.color.r, Current.color.g, Current.color.b);
			float y = (HSV.g * HTexture.sprite.texture.height) - (HTexture.sprite.texture.height * 0.5f);
			float x = (HSV.b * HTexture.sprite.texture.width) - (HTexture.sprite.texture.width * 0.5f);
			Cursor.rectTransform.anchoredPosition = new Vector2(x, y);
		}

		public void UpdateHexValue(string value)
		{
			Color newColor = Hex.ToColor(value);
			SetColor(newColor);
		}

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

		public void UpdateSaturationSliderValue(float value)
		{
			SInputField.SetText(value.ToString());
			UpdateRGBFromHSV();
		}
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

		public void UpdateValueSliderValue(float value)
		{
			VInputField.SetText(value.ToString());
			UpdateRGBFromHSV();
		}
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

		public void UpdateRedSliderValue(float value)
		{
			RInputField.SetText(value.ToString());
			UpdateHSVFromRGB();
		}
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

		public void UpdateGreenSliderValue(float value)
		{
			GInputField.SetText(value.ToString());
			UpdateHSVFromRGB();
		}
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

		public void UpdateBlueSliderValue(float value)
		{
			BInputField.SetText(value.ToString());
			UpdateHSVFromRGB();
		}
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

		public void UpdateAlphaSliderValue(float value)
		{
			AInputField.SetText(value.ToString());
			UpdateHSVFromRGB();
		}
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