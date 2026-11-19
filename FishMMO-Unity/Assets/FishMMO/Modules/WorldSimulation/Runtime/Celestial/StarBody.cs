using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>A star. The first star in a <see cref="SolarSystemProfile"/> is the primary: the others orbit it.</summary>
	[CreateAssetMenu(fileName = "New Star", menuName = "FishMMO/World/Star", order = 11)]
	public class StarBody : CelestialBody
	{
		[Tooltip("Light output relative to the home sun. Starlight on a body is the sum of luminosity ÷ distance² in AU.")]
		[Min(0f)] public float Luminosity = 1f;
		[Tooltip("Surface temperature. It sets the star's true colour (a black body's), which the sky and its light take as an eye adapted to this sun sees them: nearly white for a sun like ours, warm under a red dwarf, cool under a blue giant. Tint, kept at the reference, is art direction on top.")]
		[Range(1000f, 40000f)] public float TemperatureK = 5800f;

		/// <summary>The temperature the sky profiles are authored for: a star this hot leaves them as they are.</summary>
		public const float ReferenceTemperatureK = 5800f;

		/// <summary>
		/// The tint the sky profiles are authored for (the example Sun's), and so the tint every star should keep unless it is
		/// meant to look other than its temperature makes it: the temperature already colours it.
		/// </summary>
		public static readonly Color ReferenceTint = new Color(1f, 0.96f, 0.86f, 1f);

		/// <summary>
		/// The gamma a tint is carried in. Unity colours are sRGB-encoded, and the sky multiplies them encoded: a power law
		/// keeps a product a product, so a linear multiplier m is m^(1/2.2) encoded.
		/// </summary>
		private const float Gamma = 2.2f;

		/// <summary>
		/// A black body's colour at <paramref name="kelvin"/>, sRGB-encoded, brightest channel 1: the Planckian locus
		/// (Kim et al.'s cubic fit, 1667–25000 K; clamped past its ends) taken through CIE XYZ into linear sRGB. 6500 K is
		/// about white, our sun (5800 K) faintly warm, 3000 K the orange of a red dwarf or a candle.
		/// </summary>
		public static Color TemperatureColor(float kelvin)
		{
			double t = Mathf.Clamp(kelvin, 1667f, 25000f);
			double t2 = t * t, t3 = t2 * t;
			double x = t <= 4000.0
				? -0.2661239e9 / t3 - 0.2343589e6 / t2 + 0.8776956e3 / t + 0.179910
				: -3.0258469e9 / t3 + 2.1070379e6 / t2 + 0.2226347e3 / t + 0.240390;
			double x2 = x * x, x3 = x2 * x;
			double y = t <= 2222.0 ? -1.1063814 * x3 - 1.34811020 * x2 + 2.18555832 * x - 0.20219683
				: t <= 4000.0 ? -0.9549476 * x3 - 1.37418593 * x2 + 2.09137015 * x - 0.16748867
				: 3.0817580 * x3 - 5.87338670 * x2 + 3.75112997 * x - 0.37001483;
			double bigX = x / y, bigZ = (1.0 - x - y) / y;
			double r = System.Math.Max(0.0, 3.2406 * bigX - 1.5372 - 0.4986 * bigZ);
			double g = System.Math.Max(0.0, -0.9689 * bigX + 1.8758 + 0.0415 * bigZ);
			double b = System.Math.Max(0.0, 0.0557 * bigX - 0.2040 + 1.0570 * bigZ);
			double brightest = System.Math.Max(r, System.Math.Max(g, b));
			return new Color(ToSrgb((float)(r / brightest)), ToSrgb((float)(g / brightest)), ToSrgb((float)(b / brightest)), 1f);
		}

		/// <summary>
		/// How far an eye adapts to a sun of <paramref name="kelvin"/>, relative to ours: 1 would see its light as white, 0
		/// as a camera does. Adaptation is nearly complete for daylight-like light and falls away from it — why a candle-lit
		/// room still looks yellow after an evening in it, and why a blue overcast never quite turns white. Here 0.95 at our
		/// sun, less by 0.0018 per mired (10⁶ / K) away from it, never under 0.5: a fit to the incomplete adaptation measured
		/// under warm and cold white light, about 0.66 at 3000 K and 0.79 at 12000 K.
		/// </summary>
		public static float Adaptation(float kelvin)
		{
			float mireds = Mathf.Abs(1e6f / Mathf.Max(1000f, kelvin) - 1e6f / ReferenceTemperatureK);
			return Mathf.Clamp(0.95f - 0.0018f * mireds, 0.5f, 0.95f);
		}

		/// <summary>
		/// How a sun of <paramref name="kelvin"/> recolours light authored for the reference sun, as an eye adapted to it
		/// sees it (sRGB-encoded, unit luminance): its black body against the reference's, in linear light, with what the eye
		/// adapts away taken out (von Kries: partial adaptation to an illuminant is a lerp of its white towards the
		/// reference's). Galris's K star, 4975 K, comes out (1.004, 0.999, 0.993): its snow is white.
		/// </summary>
		public static Color TemperatureTint(float kelvin)
		{
			Color star = TemperatureColor(kelvin), reference = TemperatureColor(ReferenceTemperatureK);
			float keep = 1f - Adaptation(kelvin);
			float r = Mathf.Lerp(1f, Linear(star.r) / Linear(reference.r), keep);
			float g = Mathf.Lerp(1f, Linear(star.g) / Linear(reference.g), keep);
			float b = Mathf.Lerp(1f, Linear(star.b) / Linear(reference.b), keep);
			float luminance = r * 0.2126f + g * 0.7152f + b * 0.0722f;
			return new Color(Encoded(r / luminance), Encoded(g / luminance), Encoded(b / luminance), 1f);
		}

		/// <summary>
		/// The star's true colour: its tint, shifted by how much hotter or cooler it is than the reference — as it would look
		/// against our own sun's white, which is how the editor's orrery and sky preview draw it.
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
		/// How this star recolours a sky authored for the reference sun, seen from under it: its temperature's tint as an eye
		/// adapted to it sees it (<see cref="TemperatureTint"/>), times its tint's hue against the reference's (art
		/// direction, not adapted away). White for the reference; unit luminance.
		/// </summary>
		public Color SkyTint
		{
			get
			{
				Color art = RelativeHue(Tint), temperature = TemperatureTint(TemperatureK);
				return RelativeHue(new Color(art.r * temperature.r * ReferenceTint.r, art.g * temperature.g * ReferenceTint.g,
					art.b * temperature.b * ReferenceTint.b, 1f));
			}
		}

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

		/// <summary>The sRGB transfer curve, here rather than Mathf's (a native call) so the colours can be checked off the engine.</summary>
		private static float ToSrgb(float linear)
			=> linear <= 0.0031308f ? linear * 12.92f : 1.055f * Mathf.Pow(linear, 1f / 2.4f) - 0.055f;

		private static float Linear(float encoded)
			=> encoded <= 0.04045f ? Mathf.Max(0f, encoded) / 12.92f : Mathf.Pow((encoded + 0.055f) / 1.055f, 2.4f);

		private static float Encoded(float linear) => Mathf.Pow(Mathf.Max(0f, linear), 1f / Gamma);

		private void Reset()
		{
			SkyRadiusKm = 696000f;
			Tint = ReferenceTint;
		}
	}
}
