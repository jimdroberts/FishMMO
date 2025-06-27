using UnityEngine;
using FishNet.Serializing;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides extension methods for the <see cref="TinyColor"/> struct.
	/// </summary>
	public static class TinyColorExtensions
	{
		/// <summary>
		/// Converts a <see cref="TinyColor"/> to a Unity <see cref="Color"/>.
		/// </summary>
		/// <param name="color">The TinyColor to convert.</param>
		/// <returns>A Unity Color representation of the TinyColor.</returns>
		public static Color ToUnityColor(this TinyColor color)
		{
			// Normalize byte values (0-255) to float values (0.0-1.0) for Unity's Color.
			return new Color()
			{
				r = color.r / 255.0f,
				g = color.g / 255.0f,
				b = color.b / 255.0f,
				a = color.a / 255.0f,
			};
		}

		/// <summary>
		/// Converts a <see cref="TinyColor"/> to a hexadecimal string representation (RRGGBBAA).
		/// </summary>
		/// <param name="color">The TinyColor to convert.</param>
		/// <returns>A string representing the color in hexadecimal format (e.g., "FF0000FF" for red).</returns>
		public static string ToHex(this TinyColor color)
		{
			// Format each byte as a two-digit hexadecimal number.
			return string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", color.r, color.g, color.b, color.a);
		}
	}

	/// <summary>
	/// A lightweight color struct using byte values for R, G, B, and A components (0-255).
	/// Provides conversions, predefined colors, and utility functions for color manipulation.
	/// </summary>
	public class TinyColor
	{
		// Predefined common colors.
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

		// UI Colors - these are also predefined colors but categorized for UI purposes.
		public static readonly TinyColor darkGrey = new TinyColor(64, 64, 64, 255);    // Primary
		public static readonly TinyColor lightGrey = new TinyColor(192, 192, 192, 255);  // Text
		public static readonly TinyColor charcoal = new TinyColor(34, 34, 34, 255);    // Secondary
		public static readonly TinyColor bloodRed = new TinyColor(128, 0, 0, 255);    // Highlights
		public static readonly TinyColor maroon = new TinyColor(128, 0, 0, 255);    // Complementary to Blood Red
		public static readonly TinyColor olive = new TinyColor(128, 128, 0, 255);  // Complementary to Blood Red
		public static readonly TinyColor teal = new TinyColor(0, 128, 128, 255);    // Complementary to Blood Red
		public static readonly TinyColor silver = new TinyColor(192, 192, 192, 255);  // Complementary to Charcoal
		public static readonly TinyColor navy = new TinyColor(0, 0, 128, 255);    // Complementary to Charcoal

		/// <summary>
		/// The Red component of the color (0-255).
		/// </summary>
		public readonly byte r;
		/// <summary>
		/// The Green component of the color (0-255).
		/// </summary>
		public readonly byte g;
		/// <summary>
		/// The Blue component of the color (0-255).
		/// </summary>
		public readonly byte b;
		/// <summary>
		/// The Alpha (transparency) component of the color (0-255).
		/// </summary>
		public readonly byte a;

		/// <summary>
		/// Initializes a new instance of the <see cref="TinyColor"/> struct with RGB components and full opacity (alpha = 255).
		/// </summary>
		/// <param name="red">The red component (0-255).</param>
		/// <param name="green">The green component (0-255).</param>
		/// <param name="blue">The blue component (0-255).</param>
		public TinyColor(byte red, byte green, byte blue)
		{
			r = red;
			g = green;
			b = blue;
			a = 255; // Default to full alpha.
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="TinyColor"/> struct with RGBA components.
		/// </summary>
		/// <param name="red">The red component (0-255).</param>
		/// <param name="green">The green component (0-255).</param>
		/// <param name="blue">The blue component (0-255).</param>
		/// <param name="alpha">The alpha (transparency) component (0-255).</param>
		public TinyColor(byte red, byte green, byte blue, byte alpha)
		{
			r = red;
			g = green;
			b = blue;
			a = alpha;
		}

		/// <summary>
		/// Creates a <see cref="TinyColor"/> from a Unity <see cref="Color"/>.
		/// </summary>
		/// <param name="color">The Unity Color to convert.</param>
		/// <returns>A TinyColor representation of the Unity Color.</returns>
		public static TinyColor FromUnityColor(Color color)
		{
			// Scale float values (0.0-1.0) to byte values (0-255).
			return new TinyColor((byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255), (byte)(color.a * 255));
		}

		/// <summary>
		/// Converts a <see cref="TinyColor"/> to a Unity <see cref="Color"/>.
		/// Note: This is a duplicate of the extension method. Consider removing one if not both are needed.
		/// The extension method <see cref="TinyColorExtensions.ToUnityColor(TinyColor)"/> is generally preferred for consistency.
		/// </summary>
		/// <param name="color">The TinyColor to convert.</param>
		/// <returns>A Unity Color representation of the TinyColor.</returns>
		public static Color ToUnityColor(TinyColor color)
		{
			// Normalize byte values (0-255) to float values (0.0-1.0) for Unity's Color.
			return new Color()
			{
				r = color.r / 255.0f,
				g = color.g / 255.0f,
				b = color.b / 255.0f,
				a = color.a / 255.0f,
			};
		}

		/// <summary>
		/// Returns a string representation of the <see cref="TinyColor"/> in RGBA format.
		/// </summary>
		/// <returns>A string in the format "RGBA(R, G, B, A)".</returns>
		public override string ToString()
		{
			return string.Format("RGBA(" + r + ", " + g + ", " + b + ", " + a + ")");
		}

		/// <summary>
		/// Parses a string into a <see cref="TinyColor"/> object.
		/// Expected input format: "RGBA(R, G, B, A)" where R, G, B, A are byte values (0-255).
		/// </summary>
		/// <param name="colorString">The string to parse (e.g., "RGBA(128, 64, 255, 100)").</param>
		/// <returns>A new <see cref="TinyColor"/> object if parsing is successful, otherwise <see cref="TinyColor.indigo"/>.</returns>
		public static TinyColor FromString(string colorString)
		{
			// Example input format: "RGBA(0, 0, 0, 0)"
			// Remove the "RGBA(" and ")" parts to isolate the numerical components.
			string trimmedString = colorString.Trim();
			if (!trimmedString.StartsWith("RGBA(") || !trimmedString.EndsWith(")"))
			{
				// Log.Error requires a 'Log' class to be defined or replaced.
				// For demonstration, using Debug.LogError if in Unity context.
				Debug.LogError("Failed to parse color string: Invalid format. Expected 'RGBA(R, G, B, A)'. " + colorString);
				return TinyColor.indigo; // Return default color on invalid format.
			}

			string[] components = trimmedString.Substring(5, trimmedString.Length - 6).Split(',');

			// Parse the components into bytes.
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
				Debug.LogError("Failed to parse color string: Component parsing failed. " + colorString);
				return TinyColor.indigo; // Return default color on parsing error.
			}
		}

		/// <summary>
		/// Generates a horizontal color spectrum (hue gradient) with full saturation and value, and an alpha of 1.0f.
		/// </summary>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the color spectrum.</returns>
		public static Texture2D GenerateColorSpectrum(int width, int height)
		{
			return GenerateColorSpectrum(1.0f, width, height);
		}

		/// <summary>
		/// Generates a horizontal color spectrum (hue gradient) with full saturation and value, and a specified alpha.
		/// </summary>
		/// <param name="alpha">The alpha value for the generated colors (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the color spectrum.</returns>
		public static Texture2D GenerateColorSpectrum(float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			for (int x = 0; x < texture.width; ++x)
			{
				// Calculate hue based on X position (0-360 degrees).
				float hue = (float)x / texture.width * 360f;
				Color color = TinyColor.HSVToRGB(hue, 1.0f, 1.0f, alpha);
				for (int y = 0; y < texture.height; ++y)
				{
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		/// <summary>
		/// Generates a horizontal saturation spectrum (saturation gradient) for a given hue and value, with an alpha of 1.0f.
		/// </summary>
		/// <param name="hue">The hue value for the spectrum (0-360 degrees).</param>
		/// <param name="value">The value (brightness) for the spectrum (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the saturation spectrum.</returns>
		public static Texture2D GenerateSaturationSpectrum(float hue, float value, int width, int height)
		{
			return GenerateSaturationSpectrum(hue, value, 1.0f, width, height);
		}

		/// <summary>
		/// Generates a horizontal saturation spectrum (saturation gradient) for a given hue and value, with a specified alpha.
		/// </summary>
		/// <param name="hue">The hue value for the spectrum (0-360 degrees).</param>
		/// <param name="value">The value (brightness) for the spectrum (0.0f-1.0f).</param>
		/// <param name="alpha">The alpha value for the generated colors (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the saturation spectrum.</returns>
		public static Texture2D GenerateSaturationSpectrum(float hue, float value, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width; // Increment for saturation.
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

		/// <summary>
		/// Generates a horizontal brightness spectrum (grayscale gradient) with an alpha of 1.0f.
		/// </summary>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the brightness spectrum.</returns>
		public static Texture2D GenerateBrightnessSpectrum(int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width; // Increment for brightness.
			for (int x = 0; x < texture.width; ++x)
			{
				float brightness = d * x;
				Color color = new Color(brightness, brightness, brightness, 1.0f); // Grayscale color.
				for (int y = 0; y < texture.height; ++y)
				{
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		/// <summary>
		/// Generates a horizontal red color spectrum with specified green, blue, and full alpha values.
		/// </summary>
		/// <param name="green">The green component value (0.0f-1.0f).</param>
		/// <param name="blue">The blue component value (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the red spectrum.</returns>
		public static Texture2D GenerateRedSpectrum(float green, float blue, int width, int height)
		{
			return GenerateRedSpectrum(green, blue, 1.0f, width, height);
		}

		/// <summary>
		/// Generates a horizontal red color spectrum with specified green, blue, and alpha values.
		/// </summary>
		/// <param name="green">The green component value (0.0f-1.0f).</param>
		/// <param name="blue">The blue component value (0.0f-1.0f).</param>
		/// <param name="alpha">The alpha value for the generated colors (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the red spectrum.</returns>
		public static Texture2D GenerateRedSpectrum(float green, float blue, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width; // Increment for red component.
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

		/// <summary>
		/// Generates a horizontal green color spectrum with specified red, blue, and full alpha values.
		/// </summary>
		/// <param name="red">The red component value (0.0f-1.0f).</param>
		/// <param name="blue">The blue component value (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the green spectrum.</returns>
		public static Texture2D GenerateGreenSpectrum(float red, float blue, int width, int height)
		{
			return GenerateGreenSpectrum(red, blue, 1.0f, width, height);
		}

		/// <summary>
		/// Generates a horizontal green color spectrum with specified red, blue, and alpha values.
		/// </summary>
		/// <param name="red">The red component value (0.0f-1.0f).</param>
		/// <param name="blue">The blue component value (0.0f-1.0f).</param>
		/// <param name="alpha">The alpha value for the generated colors (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the green spectrum.</returns>
		public static Texture2D GenerateGreenSpectrum(float red, float blue, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width; // Increment for green component.
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

		/// <summary>
		/// Generates a horizontal blue color spectrum with specified red, green, and full alpha values.
		/// </summary>
		/// <param name="red">The red component value (0.0f-1.0f).</param>
		/// <param name="green">The green component value (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the blue spectrum.</returns>
		public static Texture2D GenerateBlueSpectrum(float red, float green, int width, int height)
		{
			return GenerateBlueSpectrum(red, green, 1.0f, width, height);
		}

		/// <summary>
		/// Generates a horizontal blue color spectrum with specified red, green, and alpha values.
		/// </summary>
		/// <param name="red">The red component value (0.0f-1.0f).</param>
		/// <param name="green">The green component value (0.0f-1.0f).</param>
		/// <param name="alpha">The alpha value for the generated colors (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the blue spectrum.</returns>
		public static Texture2D GenerateBlueSpectrum(float red, float green, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width; // Increment for blue component.
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

		/// <summary>
		/// Generates a horizontal alpha spectrum where RGB fades from white to black as alpha increases.
		/// </summary>
		/// <param name="width">The width of the generated texture.</param>
		/// <param name="height">The height of the generated texture.</param>
		/// <returns>A new <see cref="Texture2D"/> representing the alpha spectrum.</returns>
		public static Texture2D GenerateAlphaSpectrum(int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			float d = 1.0f / texture.width; // Increment for alpha.
			for (int x = 0; x < texture.width; ++x)
			{
				float alpha = d * x;
				// RGB fades from 1.0 (white) down to 0.0 (black) as alpha increases.
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

		/// <summary>
		/// Generates an HSV texture (saturation vs. value) for a given hue with an alpha of 1.0f.
		/// X-axis represents Value (brightness), Y-axis represents Saturation.
		/// </summary>
		/// <param name="hue">The hue value for the texture (0-360 degrees).</param>
		/// <param name="width">The width of the generated texture (represents value).</param>
		/// <param name="height">The height of the generated texture (represents saturation).</param>
		/// <returns>A new <see cref="Texture2D"/> representing the HSV color space.</returns>
		public static Texture2D GenerateHSVTexture(float hue, int width, int height)
		{
			return GenerateHSVTexture(hue, 1.0f, width, height);
		}

		/// <summary>
		/// Generates an HSV texture (saturation vs. value) for a given hue with a specified alpha.
		/// X-axis represents Value (brightness), Y-axis represents Saturation.
		/// </summary>
		/// <param name="hue">The hue value for the texture (0-360 degrees).</param>
		/// <param name="alpha">The alpha value for the generated colors (0.0f-1.0f).</param>
		/// <param name="width">The width of the generated texture (represents value).</param>
		/// <param name="height">The height of the generated texture (represents saturation).</param>
		/// <returns>A new <see cref="Texture2D"/> representing the HSV color space.</returns>
		public static Texture2D GenerateHSVTexture(float hue, float alpha, int width, int height)
		{
			Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			texture.filterMode = FilterMode.Bilinear;
			texture.wrapMode = TextureWrapMode.Clamp;
			float dA = 1.0f / (width - 1); // Value increment along X.
			float dB = 1.0f / (height - 1); // Saturation increment along Y.

			for (int x = 0; x < width; ++x)
			{
				float value = dA * x;
				for (int y = 0; y < height; ++y)
				{
					float saturation = dB * y;
					Color color = TinyColor.HSVToRGB(hue, saturation, value, alpha);
					texture.SetPixel(x, y, color);
				}
			}
			texture.Apply();
			return texture;
		}

		/// <summary>
		/// Converts an RGB color to HSV color representation with an alpha of 1.0f.
		/// </summary>
		/// <param name="red">The red component (0.0f-1.0f).</param>
		/// <param name="green">The green component (0.0f-1.0f).</param>
		/// <param name="blue">The blue component (0.0f-1.0f).</param>
		/// <returns>A Unity <see cref="Color"/> where R=Hue (0-360), G=Saturation (0.0-1.0), B=Value (0.0-1.0), A=1.0.</returns>
		public static Color RGBToHSV(float red, float green, float blue)
		{
			return RGBToHSV(red, green, blue, 1.0f);
		}

		/// <summary>
		/// Converts an RGB color to HSV color representation with a specified alpha.
		/// </summary>
		/// <param name="red">The red component (0.0f-1.0f).</param>
		/// <param name="green">The green component (0.0f-1.0f).</param>
		/// <param name="blue">The blue component (0.0f-1.0f).</param>
		/// <param name="alpha">The alpha component (0.0f-1.0f).</param>
		/// <returns>A Unity <see cref="Color"/> where R=Hue (0-360), G=Saturation (0.0-1.0), B=Value (0.0-1.0), A=alpha.</returns>
		public static Color RGBToHSV(float red, float green, float blue, float alpha)
		{
			float min = Mathf.Min(red, Mathf.Min(green, blue));
			float max = Mathf.Max(red, Mathf.Max(green, blue));
			float d = max - min; // Difference between max and min.
			float value = max; // Value (brightness) is the maximum component.
			float saturation = (max == 0) ? 0 : d / max; // Saturation is 0 if max is 0 (black).
			float hue = 0; // Initialize hue.

			if (saturation != 0)
			{
				if (red == max)
				{
					hue = (green - blue) / d; // Hue for reds.
				}
				else if (green == max)
				{
					hue = 2.0f + (blue - red) / d; // Hue for greens.
				}
				else
				{
					hue = 4.0f + (red - green) / d; // Hue for blues.
				}
				hue *= 60.0f; // Convert hue to degrees.
				if (hue < 0)
				{
					hue += 360; // Ensure hue is positive.
				}
			}
			return new Color(hue, saturation, value, alpha);
		}

		/// <summary>
		/// Converts an HSV color to RGB color representation with an alpha of 1.0f.
		/// </summary>
		/// <param name="hue">The hue component (0-360 degrees).</param>
		/// <param name="saturation">The saturation component (0.0f-1.0f).</param>
		/// <param name="value">The value (brightness) component (0.0f-1.0f).</param>
		/// <returns>A Unity <see cref="Color"/> in RGB format with full alpha.</returns>
		public static Color HSVToRGB(float hue, float saturation, float value)
		{
			return HSVToRGB(hue, saturation, value, 1.0f);
		}

		/// <summary>
		/// Converts an HSV color to RGB color representation with a specified alpha.
		/// </summary>
		/// <param name="hue">The hue component (0-360 degrees).</param>
		/// <param name="saturation">The saturation component (0.0f-1.0f).</param>
		/// <param name="value">The value (brightness) component (0.0f-1.0f).</param>
		/// <param name="alpha">The alpha component (0.0f-1.0f).</param>
		/// <returns>A Unity <see cref="Color"/> in RGB format with the specified alpha.</returns>
		public static Color HSVToRGB(float hue, float saturation, float value, float alpha)
		{
			if (saturation == 0)
			{
				return new Color(value, value, value, alpha); // Grayscale if saturation is 0.
			}
			saturation = Mathf.Clamp(saturation, 0.0f, 1.0f);
			value = Mathf.Clamp(value, 0.0f, 1.0f);
			hue /= 60.0f; // Convert hue to a 0-6 range.
			int i = Mathf.FloorToInt(hue); // Integer part of hue.
			float f = hue - i; // Fractional part of hue.

			float p = value * (1.0f - saturation);
			float q = value * (1.0f - saturation * f);
			float t = value * (1.0f - saturation * (1.0f - f));

			// Determine RGB values based on the hue sector.
			switch (i)
			{
				case 0: return new Color(value, t, p, alpha); // Between Red and Yellow
				case 1: return new Color(q, value, p, alpha); // Between Yellow and Green
				case 2: return new Color(p, value, t, alpha); // Between Green and Cyan
				case 3: return new Color(p, q, value, alpha); // Between Cyan and Blue
				case 4: return new Color(t, p, value, alpha); // Between Blue and Magenta
				default: return new Color(value, p, q, alpha); // Between Magenta and Red (case 5 and others)
			}
		}

		/// <summary>
		/// Reads a <see cref="TinyColor"/> from a <see cref="Reader"/> (FishNet serialization).
		/// </summary>
		/// <param name="reader">The FishNet Reader to read from.</param>
		/// <returns>A new <see cref="TinyColor"/> object deserialized from the reader.</returns>
		public static TinyColor Read(Reader reader)
		{
			byte r = reader.ReadUInt8Unpacked();
			byte g = reader.ReadUInt8Unpacked();
			byte b = reader.ReadUInt8Unpacked();
			byte a = reader.ReadUInt8Unpacked();
			return new TinyColor(r, g, b, a);
		}

		/// <summary>
		/// Writes the <see cref="TinyColor"/> to a <see cref="Writer"/> (FishNet serialization).
		/// </summary>
		/// <param name="writer">The FishNet Writer to write to.</param>
		public void Write(Writer writer)
		{
			writer.WriteUInt8Unpacked(r);
			writer.WriteUInt8Unpacked(g);
			writer.WriteUInt8Unpacked(b);
			writer.WriteUInt8Unpacked(a);
		}
	}
}