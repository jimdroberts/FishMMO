using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Everything the cloud renderer knows about the sky it is drawing, in numbers: what each band
	/// is doing, what the whole stack costs, and how much of the screen it actually ended up on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A slider tells you what you asked for. It does not tell you what you got, and with a noise
	/// field cut by a threshold those are very different things — a coverage of 0.4 on a band whose
	/// onset is 0.55 produces nothing at all, and nothing about the slider says so. These figures
	/// close that gap: every band reports the coverage it was actually given, the cut that coverage
	/// puts on the noise, and whether the result is empty.
	/// </para>
	/// <para>
	/// The one number that cannot be derived — how much of the sky the clouds cover once drawn — is
	/// read back off the cloud buffer itself, asynchronously and a few times a second, so a live
	/// readout costs a downsample and never stalls the frame. It is the same measurement the render
	/// probe makes, which is what keeps the panel and the probe honest about the same sky.
	/// </para>
	/// </remarks>
	public static class CloudStats
	{
		/// <summary>
		/// The share of the sky the clouds were last measured on, 0..1, or negative before the
		/// first read comes back.
		/// </summary>
		public static float MeasuredCover { get; private set; } = -1f;

		/// <summary>How much of the buffer had any cloud in it at all, however thin.</summary>
		public static float MeasuredAnyCloud { get; private set; } = -1f;

		/// <summary>The size of the buffer those figures were read from.</summary>
		public static Vector2Int BufferSize { get; private set; }

		private const int SampleWidth = 96;
		private const int SampleHeight = 54;
		private static bool reading;
		private static float nextRead;

		/// <summary>
		/// Asks for a fresh measurement if one is due. Safe to call every frame from a panel: it
		/// throttles itself and never blocks.
		/// </summary>
		public static void Poll()
		{
			if (reading || Time.unscaledTime < nextRead)
			{
				return;
			}
			Texture buffer = FishCloudsFeature.LastCloudBuffer;
			if (buffer == null || !SystemInfo.supportsAsyncGPUReadback)
			{
				return;
			}
			BufferSize = new Vector2Int(buffer.width, buffer.height);
			nextRead = Time.unscaledTime + 0.25f;
			reading = true;

			// Downsample first: the readback is then a few kilobytes whatever the tier draws at, and
			// the average of a block of transmittance is a fair enough stand-in for the block.
			RenderTexture small = RenderTexture.GetTemporary(SampleWidth, SampleHeight, 0, RenderTextureFormat.ARGBHalf);
			Graphics.Blit(buffer, small);
			// Asked back as full floats so the data can be read as Color directly; the conversion is
			// the readback's own and costs nothing at this size.
			AsyncGPUReadback.Request(small, 0, TextureFormat.RGBAFloat, request =>
			{
				reading = false;
				RenderTexture.ReleaseTemporary(small);
				if (request.hasError)
				{
					return;
				}
				var pixels = request.GetData<Color>();
				int counted = 0, cloudy = 0, any = 0;
				// The upper two thirds only: the bottom of the frame is ground, and counting it
				// would make every sky look clearer the lower the camera points.
				int rows = Mathf.Max(1, Mathf.RoundToInt(SampleHeight * 0.66f));
				for (int y = SampleHeight - rows; y < SampleHeight; y++)
				{
					for (int x = 0; x < SampleWidth; x++)
					{
						float alpha = pixels[y * SampleWidth + x].a;
						counted++;
						if (alpha < 0.6f)
						{
							cloudy++;
						}
						if (alpha < 0.98f)
						{
							any++;
						}
					}
				}
				if (counted > 0)
				{
					MeasuredCover = cloudy / (float)counted;
					MeasuredAnyCloud = any / (float)counted;
				}
			});
		}

		/// <summary>
		/// The cut the shader puts on the noise at a given coverage: the same curve as
		/// FishCloudVolume.hlsl, so the panel reports the threshold the sky is actually drawn with.
		/// </summary>
		public static float ThresholdAt(VolumetricCloudSettings clouds, float coverage)
		{
			float cover = Mathf.Clamp01(coverage);
			float curve = cover * cover;
			return Mathf.Lerp(clouds.CoverageCutClear, clouds.CoverageCutFull, cover) - clouds.CoverageBend * curve * curve;
		}

		/// <summary>
		/// What share of a band's noise survives that cut, as a rough fraction. Measured from the
		/// shape volume on the CPU (scratchpad/noisestats.py) and kept as a curve here, because the
		/// field is far narrower than its 0..1 range suggests and a designer reading "cut 0.66"
		/// otherwise has no way to know that means a nearly empty sky.
		/// </summary>
		public static float FillForThreshold(float threshold)
		{
			// body: p5 0.459, p25 0.526, p50 0.569, p75 0.609, p90 0.642, p95 0.660, p99 0.696,
			// min 0.325, max 0.759. Read the other way: the share of the field above a cut.
			(float value, float above)[] curve =
			{
				(0.325f, 1f), (0.459f, 0.95f), (0.526f, 0.75f), (0.569f, 0.5f),
				(0.609f, 0.25f), (0.642f, 0.10f), (0.660f, 0.05f), (0.696f, 0.01f), (0.759f, 0f),
			};
			if (threshold <= curve[0].value)
			{
				return 1f;
			}
			for (int i = 1; i < curve.Length; i++)
			{
				if (threshold <= curve[i].value)
				{
					float t = Mathf.InverseLerp(curve[i - 1].value, curve[i].value, threshold);
					return Mathf.Lerp(curve[i - 1].above, curve[i].above, t);
				}
			}
			return 0f;
		}

		/// <summary>The whole-sky figures, as lines for a readout.</summary>
		public static string Sky(SkySystem sky, VolumetricCloudSettings clouds)
		{
			if (sky == null || clouds == null)
			{
				return "No sky system, so there is nothing to measure.";
			}
			if (!SkySystem.CloudsReady)
			{
				return "The cloud volumes are not baked: Weather Tools → Bake cloud noise.";
			}
			var text = new StringBuilder();
			CloudTierSettings tier = sky.CloudTier;
			var bands = sky.CloudBands;
			int count = bands != null ? bands.Count : 0;
			int active = 0;
			float deepest = 0f;
			var coverage = sky.CloudBandCoverage;
			for (int i = 0; i < count && coverage != null && i < coverage.Count; i++)
			{
				if (coverage[i] > 0.001f && bands[i] != null && bands[i].Density > 0.001f)
				{
					active++;
					deepest = Mathf.Max(deepest, bands[i].Thickness);
				}
			}

			text.AppendLine($"Forecast · cover {Pct(sky.CloudCover)} · precipitation {Pct(sky.CloudPrecipitation)} · storm {Pct(sky.CloudStorm)}");
			// The formations: how far the bank or gap the camera stands under has moved the cover
			// from the sky's own figure. Zero with the sliders or a pinned preset, which have none.
			float meso = sky.CloudMesoscaleAtCamera;
			text.AppendLine(Mathf.Abs(meso) > 0.0005f
				? $"Formations · {(meso >= 0f ? "in a bank" : "in a gap")}, {meso * 100f:+0;-0}% cover here against the sky's mean · masses every ~{WeatherDriver.MesoscaleMetres / 1000f:0} km, up to ±{WeatherDriver.MesoscaleAmplitude * 100f:0}%"
				: "Formations · none (the channels or a preset decide the weather, and they have no banks or gaps)");
			text.AppendLine($"On screen · {(MeasuredCover >= 0f ? Pct(MeasuredCover) : "measuring…")} solid cloud · "
				+ $"{(MeasuredAnyCloud >= 0f ? Pct(MeasuredAnyCloud) : "—")} any cloud at all");
			text.AppendLine($"Stack · {active} of {count} band(s) drawing · {sky.CloudShellBottom:0}–{sky.CloudShellTop:0} m "
				+ $"({(sky.CloudShellTop - sky.CloudShellBottom) / 1000f:0.00} km of sky) · deepest band {deepest:0} m");
			float threshold = ThresholdAt(clouds, sky.CloudCover);
			text.AppendLine($"Noise cut · {threshold:0.000} at this cover, keeping about {Pct(FillForThreshold(threshold))} of the field "
				+ $"(which runs 0.33–0.76)");
			text.AppendLine($"Wind · {sky.CloudWindSpeed:0.0} m/s toward {Bearing(sky.CloudWind)} · "
				+ $"sun {Mathf.Asin(Mathf.Clamp(sky.CloudSunDirection.y, -1f, 1f)) * Mathf.Rad2Deg:0.0}° above the horizon");
			text.Append($"Cost · {tier.Steps} steps at {(tier.Resolution * 100f):0}% of the screen"
				+ $"{(BufferSize.x > 0 ? $" ({BufferSize.x}×{BufferSize.y})" : string.Empty)} · detail {tier.Detail:0.00} · "
				+ $"{(tier.Temporal ? $"steadied {tier.TemporalBlend:0.00}" : "no history")} · "
				+ $"shadows {(SkySystem.DrawCloudShadows ? "on" : "off")} · shafts {(SkySystem.DrawGodRays ? "on" : "off")}");
			return text.ToString();
		}

		/// <summary>One band's figures, as a single line for its foldout.</summary>
		public static string Band(SkySystem sky, VolumetricCloudSettings clouds, int index)
		{
			if (sky == null || clouds == null)
			{
				return string.Empty;
			}
			var bands = sky.CloudBands;
			var coverage = sky.CloudBandCoverage;
			if (bands == null || index >= bands.Count || bands[index] == null)
			{
				return string.Empty;
			}
			CloudLayer band = bands[index];
			float cover = coverage != null && index < coverage.Count ? coverage[index] : 0f;
			// A band that follows the condensation level is not sitting where it was authored, so
			// report where it actually is and by how much the weather has moved it.
			var bottoms = sky.CloudBandBottom;
			float floorNow = bottoms != null && index < bottoms.Count && bottoms[index] > 0.001f
				? bottoms[index]
				: band.Bottom;
			float threshold = ThresholdAt(clouds, cover);
			float fill = cover > 0.001f ? FillForThreshold(threshold) : 0f;
			float density = band.Density * clouds.Density;
			// Optical depth is what decides whether a band reads as haze or as a wall: a thin band
			// at a high density and a deep one at a low density look nothing alike on the sliders
			// and much alike in the sky.
			float depth = density * band.Thickness * fill;
			string state = cover <= 0.001f
				? (band.CoverageOnset > 0.001f && sky.CloudCover < band.CoverageOnset
					? $"empty — waits for cover {band.CoverageOnset:0.00}"
					: "empty")
				: density <= 0.001f ? "empty — density 0"
				: fill <= 0.005f ? "empty — cut above the field"
				: "drawing";

			var text = new StringBuilder();
			text.AppendLine($"  {state}");
			text.AppendLine($"  coverage asked {Pct(cover)} → cut {threshold:0.000} → about {Pct(fill)} of the sky in this band");
			float ceilingNow = floorNow + band.Thickness;
			string moved = Mathf.Abs(floorNow - band.Bottom) > 1f
				? $" (authored {band.Bottom:0} m, moved {floorNow - band.Bottom:+0;-0} m by the condensation level)"
				: string.Empty;
			text.AppendLine($"  {floorNow:0} – {ceilingNow:0} m ({band.Thickness:0} m deep, {band.Thickness / 1000f:0.00} km)   "
				+ $"density {density:0.00}   optical depth {depth:0}{moved}");
			text.AppendLine(band.Convection > 0.001f
				? $"  flat base at {floorNow:0} m, tops between about {floorNow + band.Thickness * Mathf.Lerp(1f, 0.3f, band.Convection):0} "
					+ $"and {ceilingNow:0} m (convection {band.Convection:0.00})"
				: $"  level deck: base {floorNow:0} m, top {ceilingNow:0} m (no convection)");
			text.Append($"  masses every {band.NoiseScale:0} m, wisps every {band.DetailScale:0} m"
				+ $"{(band.Stretch > 1.01f ? $", drawn out {band.Stretch:0.0}× downwind" : string.Empty)}   "
				+ $"drifts {sky.CloudWindSpeed * band.WindScale:0.0} m/s"
				+ $"{(band.CarriesRain ? "   carries rain" : string.Empty)}{(band.GrowsStorms ? "   grows storms" : string.Empty)}");
			return text.ToString();
		}

		private static string Pct(float value) => $"{Mathf.Clamp01(value) * 100f:0}%";

		/// <summary>Which way the wind is blowing, in the words a weather report uses.</summary>
		private static string Bearing(Vector2 wind)
		{
			if (wind.sqrMagnitude < 0.0001f)
			{
				return "nowhere";
			}
			float degrees = Mathf.Repeat(Mathf.Atan2(wind.x, wind.y) * Mathf.Rad2Deg, 360f);
			string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
			return $"{points[Mathf.RoundToInt(degrees / 45f) % 8]} ({degrees:0}°)";
		}
	}
}
