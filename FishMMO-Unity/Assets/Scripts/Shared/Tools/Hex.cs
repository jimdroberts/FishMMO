using System.Globalization;
using UnityEngine;

public static class Hex
{
	public static Color ColorNormalize(Color color)
	{
		return ColorNormalize(color.r, color.g, color.b, color.a);
	}
	public static Color ColorNormalize(float r, float g, float b)
	{
		return ColorNormalize(r, g, b, 1.0f);
	}
	public static Color ColorNormalize(float r, float g, float b, float a)
	{
		float max = 255.0f;
		float tmp = Mathf.Max(r, Mathf.Max(g, Mathf.Max(b, a)));
		if (tmp > max)
		{
			max = tmp;
		}
		r = (r < float.Epsilon) ? 0.0f : r / max;
		g = (g < float.Epsilon) ? 0.0f : g / max;
		b = (b < float.Epsilon) ? 0.0f : b / max;
		a = (a < float.Epsilon) ? 0.0f : a / max;
		return new Color(r, g, b, a);
	}

	public static int ToInt(string value)
	{
		int result;
		if (int.TryParse(value, NumberStyles.HexNumber, CultureInfo.CurrentCulture, out result))
		{
			return result;
		}
		return 0;
	}

	public static Color ToColor(string s)
	{
		if (s.Length < 6)
		{
			return Color.white;
		}

		float r = ToInt(s.Substring(0, 2));
		float g = ToInt(s.Substring(2, 2));
		float b = ToInt(s.Substring(4, 2));
		if (s.Length < 8)
		{
			return ColorNormalize(r, g, b, 255.0f);
		}
		float a = ToInt(s.Substring(6, 2));

		return ColorNormalize(r, g, b, a);
	}

	public static string ColorToHex(Color color)
	{
		int r = (int)(color.r * 255.0f);
		int g = (int)(color.g * 255.0f);
		int b = (int)(color.b * 255.0f);
		int a = (int)(color.a * 255.0f);

		return string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", r, g, b, a);
	}
}