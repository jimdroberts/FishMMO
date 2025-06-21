using UnityEngine;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	public static class TinyColorExtensions
	{
		public static Color ToUnityColor(this TinyColor color)
		{
			return new Color()
			{
				r = color.r / 255.0f,
				g = color.g / 255.0f,
				b = color.b / 255.0f,
				a = color.a / 255.0f,
			};
		}

		public static string ToHex(this TinyColor color)
		{
			return string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", color.r, color.g, color.b, color.a);
		}
	}

	public class TinyColor
	{
		public static readonly TinyColor transparent = new TinyColor(0, 0, 0, 0);
		public static readonly TinyColor white = new TinyColor(255, 255, 255, 255);
		public static readonly TinyColor black = new TinyColor(0, 0, 0, 255);
		public static readonly TinyColor red = new TinyColor(255, 0, 0, 255);
		public static readonly TinyColor orange = new TinyColor(255, 165, 0, 255);
		public static readonly TinyColor yellow = new TinyColor(255, 255, 0, 255);
		public static readonly TinyColor green = new TinyColor(0, 255, 0, 255);
		public static readonly TinyColor blue = new TinyColor(0, 0, 255, 255);
		public static readonly TinyColor indigo = new TinyColor(75, 0, 130, 255);
		public static readonly TinyColor violet = new TinyColor(148, 0, 211, 255);
		public static readonly TinyColor skyBlue = new TinyColor(135, 206, 250, 255);
		public static readonly TinyColor limeGreen = new TinyColor(50, 205, 50, 255);
		public static readonly TinyColor magenta = new TinyColor(255, 0, 255, 255);
		public static readonly TinyColor turquoise = new TinyColor(64, 224, 208, 255);
		public static readonly TinyColor goldenrod = new TinyColor(218, 165, 32, 255);
		public static readonly TinyColor lavender = new TinyColor(230, 230, 250, 255);
		public static readonly TinyColor chocolate = new TinyColor(210, 105, 30, 255);
		public static readonly TinyColor plum = new TinyColor(221, 160, 221, 255);
		public static readonly TinyColor forestGreen = new TinyColor(34, 139, 34, 255);

		// UI Colors
		public static readonly TinyColor darkGrey = new TinyColor(64, 64, 64, 255);  // Primary
		public static readonly TinyColor lightGrey = new TinyColor(192, 192, 192, 255);  // Text
		public static readonly TinyColor charcoal = new TinyColor(34, 34, 34, 255);  // Secondary
		public static readonly TinyColor bloodRed = new TinyColor(128, 0, 0, 255);  // Hightlights
		public static readonly TinyColor maroon = new TinyColor(128, 0, 0, 255);  // Complementary to Blood Red
		public static readonly TinyColor olive = new TinyColor(128, 128, 0, 255);  // Complementary to Blood Red
		public static readonly TinyColor teal = new TinyColor(0, 128, 128, 255);  // Complementary to Blood Red
		public static readonly TinyColor silver = new TinyColor(192, 192, 192, 255);  // Complementary to Charcoal
		public static readonly TinyColor navy = new TinyColor(0, 0, 128, 255);  // Complementary to Charcoal

		public readonly byte r;
		public readonly byte g;
		public readonly byte b;
		public readonly byte a;

		public TinyColor(byte red, byte green, byte blue)
		{
			r = red;
			g = green;
			b = blue;
			a = 255;
		}

		public TinyColor(byte red, byte green, byte blue, byte alpha)
		{
			r = red;
			g = green;
			b = blue;
			a = alpha;
		}

		public static TinyColor FromUnityColor(Color color)
		{
			return new TinyColor((byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255), (byte)(color.a * 255));
		}

		public static Color ToUnityColor(TinyColor color)
		{
			return new Color()
			{
				r = color.r / 255.0f,
				g = color.g / 255.0f,
				b = color.b / 255.0f,
				a = color.a / 255.0f,
			};
		}

		public override string ToString()
		{
			return string.Format("RGBA(" + r + ", " + g + ", " + b + ", " + a + ")");
		}

		public static TinyColor FromString(string colorString)
		{
			// Example input format: "RGBA(0.5, 0.2, 0.8, 1.0)"

			// Remove the "RGBA(" and ")" parts
			string[] components = colorString.Substring(5, colorString.Length - 6).Split(',');

			// Parse the components into floats
			if (components.Length == 4 &&
				byte.TryParse(components[0].Trim(), out byte r) &&
				byte.TryParse(components[1].Trim(), out byte g) &&
				byte.TryParse(components[2].Trim(), out byte b) &&
				byte.TryParse(components[3].Trim(), out byte a))
			{
				return new TinyColor(r, g, b, a);
			}
			else
			{
				// Handle invalid input gracefully, e.g., by returning a default color
				Log.Error("Failed to parse color string: " + colorString);
				return TinyColor.indigo;
			}
		}

		public static Texture2D GenerateColorSpectrum(int width, int height)
		{
			return GenerateColorSpectrum(1.0f, width, height);
		}
		public static Texture2D GenerateColorSpectrum(float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			for (int x = 0; x < texture.width; ++x)
			{
				Color color = TinyColor.HSVToRGB(x, 1.0f, 1.0f, alpha);
				for (int y = 0; y < texture.height; ++y)
				{
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		public static Texture2D GenerateSaturationSpectrum(float hue, float value, int width, int height)
		{
			return GenerateSaturationSpectrum(hue, value, 1.0f, width, height);
		}
		public static Texture2D GenerateSaturationSpectrum(float hue, float value, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width;
			for (int x = 0; x < texture.width; ++x)
			{
				float saturation = d * x;
				Color color = TinyColor.HSVToRGB(hue, saturation, value, alpha);
				for (int y = 0; y < texture.height; ++y)
				{
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		public static Texture2D GenerateBrightnessSpectrum(int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width;
			for (int x = 0; x < texture.width; ++x)
			{
				float brightness = d * x;
				Color color = new Color(brightness, brightness, brightness, 1.0f);
				for (int y = 0; y < texture.height; ++y)
				{
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		public static Texture2D GenerateRedSpectrum(float green, float blue, int width, int height)
		{
			return GenerateRedSpectrum(green, blue, 1.0f, width, height);
		}
		public static Texture2D GenerateRedSpectrum(float green, float blue, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width;
			for (int x = 0; x < texture.width; ++x)
			{
				float red = d * x;
				Color color = new Color(red, green, blue, alpha);
				for (int y = 0; y < texture.height; ++y)
				{
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		public static Texture2D GenerateGreenSpectrum(float red, float blue, int width, int height)
		{
			return GenerateGreenSpectrum(red, blue, 1.0f, width, height);
		}
		public static Texture2D GenerateGreenSpectrum(float red, float blue, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width;
			for (int x = 0; x < texture.width; ++x)
			{
				float green = d * x;
				Color color = new Color(red, green, blue, alpha);
				for (int y = 0; y < texture.height; ++y)
				{
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		public static Texture2D GenerateBlueSpectrum(float red, float green, int width, int height)
		{
			return GenerateBlueSpectrum(red, green, 1.0f, width, height);
		}
		public static Texture2D GenerateBlueSpectrum(float red, float green, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width;
			for (int x = 0; x < texture.width; ++x)
			{
				float blue = d * x;
				Color color = new Color(red, green, blue, alpha);
				for (int y = 0; y < texture.height; ++y)
				{
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		public static Texture2D GenerateAlphaSpectrum(int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width;
			for (int x = 0; x < texture.width; ++x)
			{
				float alpha = d * x;
				float rgb = d * (texture.width - x);
				Color color = new Color(rgb, rgb, rgb, alpha);
				for (int y = 0; y < texture.height; ++y)
				{
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		public static Texture2D GenerateHSVTexture(float hue, int width, int height)
		{
			return GenerateHSVTexture(hue, 1.0f, width, height);
		}
		public static Texture2D GenerateHSVTexture(float hue, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			texture.wrapMode = TextureWrapMode.Clamp;
			float dA = 1.0f / texture.width;
			float dB = 1.0f / texture.height;
			for (int x = 0; x < texture.width; ++x)
			{
				float value = dA * x;
				for (int y = 0; y < texture.height; ++y)
				{
					float saturation = dB * y;
					Color color = TinyColor.HSVToRGB(hue, saturation, value, alpha);
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		public static Color RGBToHSV(float red, float green, float blue)
		{
			return RGBToHSV(red, green, blue, 1.0f);
		}
		public static Color RGBToHSV(float red, float green, float blue, float alpha)
		{
			float min = Mathf.Min(red, Mathf.Min(green, blue));
			float max = Mathf.Max(red, Mathf.Max(green, blue));
			float d = max - min;
			float value = max;
			float saturation = (max == 0) ? 0 : d / max;
			float hue = 0;
			if (saturation != 0)
			{
				if (red == max)
				{
					hue = (green - blue) / d;
				}
				else if (green == max)
				{
					hue = 2.0f + (blue - red) / d;
				}
				else
				{
					hue = 4.0f + (red - green) / d;
				}
				hue *= 60.0f;
				if (hue < 0)
				{
					hue += 360;
				}
			}
			return new Color(hue, saturation, value, alpha);
		}

		public static Color HSVToRGB(float hue, float saturation, float value)
		{
			return HSVToRGB(hue, saturation, value, 1.0f);
		}
		public static Color HSVToRGB(float hue, float saturation, float value, float alpha)
		{
			if (saturation == 0)
			{
				return new Color(value, value, value, alpha);
			}
			saturation = Mathf.Clamp(saturation, 0.0f, 1.0f);
			value = Mathf.Clamp(value, 0.0f, 1.0f);
			hue /= 60.0f;
			int i = Mathf.FloorToInt(hue);
			float f = hue - i;
			float p = value * (1.0f - saturation);
			float q = value * (1.0f - saturation * f);
			float t = value * (1.0f - saturation * (1.0f - f));
			switch (i)
			{
				case 0: return new Color(value, t, p, alpha);
				case 1: return new Color(q, value, p, alpha);
				case 2: return new Color(p, value, t, alpha);
				case 3: return new Color(p, q, value, alpha);
				case 4: return new Color(t, p, value, alpha);
				default: return new Color(value, p, q, alpha);
			}
		}

		public static TinyColor Read(Reader reader)
		{
			byte r = reader.ReadUInt8Unpacked();
			byte g = reader.ReadUInt8Unpacked();
			byte b = reader.ReadUInt8Unpacked();
			byte a = reader.ReadUInt8Unpacked();
			return new TinyColor(r, g, b, a);
		}

		public void Write(Writer writer)
		{
			writer.WriteUInt8Unpacked(r);
			writer.WriteUInt8Unpacked(g);
			writer.WriteUInt8Unpacked(b);
			writer.WriteUInt8Unpacked(a);
		}
	}
}