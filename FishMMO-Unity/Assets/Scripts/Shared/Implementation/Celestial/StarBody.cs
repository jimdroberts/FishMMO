using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>A star. The first star in a <see cref="SolarSystemProfile"/> is the primary: the others orbit it.</summary>
	[CreateAssetMenu(fileName = "New Star", menuName = "FishMMO/World/Star", order = 11)]
	public class StarBody : CelestialBody
	{
		[Tooltip("Light output relative to the home sun. Starlight on a body is the sum of luminosity ÷ distance² in AU.")]
		[Min(0f)] public float Luminosity = 1f;
		[Tooltip("Colour temperature. Together with Tint it sets the star's colour: its disc, its light, and the hue of the sky while it is up.")]
		[Range(1000f, 40000f)] public float TemperatureK = 5800f;

		/// <summary>The temperature the sky profiles are authored for: a star this hot leaves them as they are.</summary>
		public const float ReferenceTemperatureK = 5800f;

		/// <summary>The tint the sky profiles are authored for (the example Sun's).</summary>
		public static readonly Color ReferenceTint = new Color(1f, 0.96f, 0.86f, 1f);

		/// <summary>A black-body-ish colour for a temperature in kelvin.</summary>
		public static Color TemperatureColor(float kelvin)
		{
			float t = Mathf.InverseLerp(2500f, 12000f, kelvin);
			Color cool = new Color(1f, 0.62f, 0.35f);
			Color sun = new Color(1f, 0.96f, 0.9f);
			Color hot = new Color(0.66f, 0.76f, 1f);
			return t < 0.35f ? Color.Lerp(cool, sun, t / 0.35f) : Color.Lerp(sun, hot, (t - 0.35f) / 0.65f);
		}

		/// <summary>
		/// The star's colour: its tint, shifted by how much hotter or cooler it is than the
		/// reference. The editor draws the star in this colour and the sky lights with it.
		/// </summary>
		public Color StarColor
		{
			get
			{
				Color t = TemperatureColor(TemperatureK), r = TemperatureColor(ReferenceTemperatureK);
				return new Color(Tint.r * t.r / r.r, Tint.g * t.g / r.g, Tint.b * t.b / r.b, 1f);
			}
		}

		/// <summary>
		/// How this star recolours a sky authored for the reference sun: white for the reference,
		/// otherwise its hue relative to it, at the same brightness.
		/// </summary>
		public Color SkyTint => RelativeHue(StarColor);

		/// <summary>A colour's hue relative to <see cref="ReferenceTint"/>, scaled to unit luminance.</summary>
		public static Color RelativeHue(Color color)
		{
			var relative = new Color(color.r / ReferenceTint.r, color.g / ReferenceTint.g, color.b / ReferenceTint.b, 1f);
			float luminance = relative.r * 0.2126f + relative.g * 0.7152f + relative.b * 0.0722f;
			if (luminance <= 1e-4f)
			{
				return Color.white;
			}
			return new Color(relative.r / luminance, relative.g / luminance, relative.b / luminance, 1f);
		}

		private void Reset()
		{
			SkyRadiusKm = 696000f;
			Tint = new Color(1f, 0.95f, 0.84f);
		}
	}
}
