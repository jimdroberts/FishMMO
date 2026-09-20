using System.Collections.Generic;
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

		/// <summary>How a figure should read at a glance.</summary>
		public enum Tone
		{
			Plain,
			/// <summary>Working as intended and worth noticing.</summary>
			Good,
			/// <summary>Something is off, or switched off.</summary>
			Warn,
			/// <summary>Not applicable just now.</summary>
			Dim,
		}

		/// <summary>
		/// One figure: which group it belongs to, what it is, and what it reads.
		/// </summary>
		/// <remarks>
		/// Figures and not a block of text. These used to be returned as one string of clauses joined
		/// by dots, ten lines for the sky and five more for every band, and whoever showed them could
		/// only show them as that: a paragraph to be read for a number. A figure has a name, so it has
		/// a place, and the number is found by where it is.
		/// </remarks>
		public readonly struct Figure
		{
			public readonly string Group;
			public readonly string Label;
			public readonly string Value;
			public readonly Tone Tone;
			/// <summary>More than fits beside the label: shown on hover.</summary>
			public readonly string Note;

			public Figure(string group, string label, string value, Tone tone = Tone.Plain, string note = null)
			{
				Group = group;
				Label = label;
				Value = value;
				Tone = tone;
				Note = note;
			}
		}

		/// <summary>The whole-sky figures. Always the same figures in the same order, whatever they read.</summary>
		public static void Sky(SkySystem sky, VolumetricCloudSettings clouds, List<Figure> into)
		{
			into.Clear();
			if (sky == null || clouds == null)
			{
				into.Add(new Figure("Clouds", "State", "no sky system", Tone.Warn));
				return;
			}
			if (!SkySystem.CloudsReady)
			{
				into.Add(new Figure("Clouds", "State", "not baked", Tone.Warn, "Weather Tools → Bake cloud noise."));
				return;
			}
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

			// What the weather asked for.
			into.Add(new Figure("Forecast", "Cover", Pct(sky.CloudCover)));
			into.Add(new Figure("Forecast", "Precipitation", Pct(sky.CloudPrecipitation), sky.CloudPrecipitation <= 0.005f ? Tone.Dim : Tone.Plain));
			into.Add(new Figure("Forecast", "Storm", Pct(sky.CloudStorm), sky.CloudStorm <= 0.005f ? Tone.Dim : Tone.Plain));
			// The formations: how far the bank or gap the camera stands under has moved the cover
			// from the sky's own figure. Zero with the sliders or a pinned preset, which have none.
			float meso = sky.CloudMesoscaleAtCamera;
			bool formed = Mathf.Abs(meso) > 0.0005f;
			into.Add(new Figure("Forecast", "Here", formed ? $"{(meso >= 0f ? "in a bank" : "in a gap")} {meso * 100f:+0;-0}%" : "no formations",
				formed ? Tone.Plain : Tone.Dim,
				formed
					? $"Cover here against the sky's mean. Masses every ~{WeatherDriver.MesoscaleMetres / 1000f:0} km, up to ±{WeatherDriver.MesoscaleAmplitude * 100f:0}%."
					: "The channels or a preset decide the weather, and they have no banks or gaps."));

			// What ended up on the screen.
			into.Add(new Figure("On screen", "Solid cloud", MeasuredCover >= 0f ? Pct(MeasuredCover) : "measuring…", MeasuredCover >= 0f ? Tone.Plain : Tone.Dim));
			into.Add(new Figure("On screen", "Any cloud", MeasuredAnyCloud >= 0f ? Pct(MeasuredAnyCloud) : "—", MeasuredAnyCloud >= 0f ? Tone.Plain : Tone.Dim));
			float threshold = ThresholdAt(clouds, sky.CloudCover);
			into.Add(new Figure("On screen", "Noise cut", threshold.ToString("0.000"), Tone.Plain, "Where the noise is cut at this cover. The field runs 0.33–0.76."));
			into.Add(new Figure("On screen", "Field kept", Pct(FillForThreshold(threshold))));

			// The stack.
			into.Add(new Figure("Stack", "Bands drawing", $"{active} of {count}", active == 0 ? Tone.Dim : Tone.Plain));
			into.Add(new Figure("Stack", "Shell", $"{sky.CloudShellBottom:0}–{sky.CloudShellTop:0} m"));
			into.Add(new Figure("Stack", "Sky depth", $"{(sky.CloudShellTop - sky.CloudShellBottom) / 1000f:0.00} km"));
			into.Add(new Figure("Stack", "Deepest band", $"{deepest:0} m", deepest <= 0f ? Tone.Dim : Tone.Plain));

			// The columns: how far up the cloud of this sky gets. The same curve the shader uses.
			float ColumnTop(float type)
			{
				float low = Mathf.Lerp(0.07f, 0.30f, Mathf.Clamp01(type * 2f));
				return Mathf.Lerp(low, 1f, Mathf.Clamp01(type * 2f - 1f));
			}
			for (int i = 0; i < count; i++)
			{
				if (bands[i] == null || !bands[i].Column)
				{
					continue;
				}
				var bottoms = sky.CloudBandBottom;
				float floor = bottoms != null && i < bottoms.Count && bottoms[i] > 0.001f ? bottoms[i] : bands[i].Bottom;
				float depth = bands[i].Thickness;
				bool towers = sky.CloudTowerGain >= 0.01f;
				into.Add(new Figure("Columns", "Base", $"{floor:0} m"));
				into.Add(new Figure("Columns", "Most cloud to", $"{floor + depth * ColumnTop(sky.CloudColumnType):0} m", Tone.Plain, $"Column type {sky.CloudColumnType:0.00}."));
				into.Add(new Figure("Columns", "Towers to", towers ? $"{floor + depth * ColumnTop(Mathf.Clamp01(sky.CloudColumnType + sky.CloudTowerGain)):0} m" : "none",
					towers ? Tone.Plain : Tone.Dim, towers ? null : "The field is not deciding the weather, so nothing grows a tower."));
				// A tower is a few kilometres across and the bed's default clock carries the sky at a
				// kilometre and a half a second: one crossing the camera is there and gone in two
				// seconds, which looks like cloud swelling up and shrinking away. This says when.
				bool crossing = sky.CloudTowerAtCamera > 0.3f;
				into.Add(new Figure("Columns", "Tower overhead", crossing ? $"{Pct(sky.CloudTowerAtCamera)} — crossing now" : Pct(sky.CloudTowerAtCamera),
					crossing ? Tone.Warn : Tone.Plain));
				into.Add(new Figure("Columns", "Air climbs", $"{sky.CloudClimbHeight:0} m", Tone.Plain, "Ground lower than this the cloud goes over; higher, it goes round."));
				break;
			}

			into.Add(new Figure("Air & light", "Wind", $"{sky.CloudWindSpeed:0.0} m/s"));
			into.Add(new Figure("Air & light", "Toward", Bearing(sky.CloudWind)));
			into.Add(new Figure("Air & light", "Sun", $"{Mathf.Asin(Mathf.Clamp(sky.CloudSunDirection.y, -1f, 1f)) * Mathf.Rad2Deg:0.0}° up"));

			into.Add(new Figure("Cost", "Steps", tier.Steps.ToString()));
			into.Add(new Figure("Cost", "Buffer", BufferSize.x > 0 ? $"{(tier.Resolution * 100f):0}% · {BufferSize.x}×{BufferSize.y}" : $"{(tier.Resolution * 100f):0}%"));
			into.Add(new Figure("Cost", "Detail", tier.Detail.ToString("0.00")));
			into.Add(new Figure("Cost", "History", tier.Temporal ? $"steadied {tier.TemporalBlend:0.00}" : "off", tier.Temporal ? Tone.Plain : Tone.Warn));
			into.Add(new Figure("Cost", "Shadows", SkySystem.DrawCloudShadows ? "on" : "off", SkySystem.DrawCloudShadows ? Tone.Plain : Tone.Warn));
			into.Add(new Figure("Cost", "Shafts", SkySystem.DrawGodRays ? "on" : "off", SkySystem.DrawGodRays ? Tone.Plain : Tone.Warn));
		}

		/// <summary>One band's figures, for its foldout.</summary>
		public static void Band(SkySystem sky, VolumetricCloudSettings clouds, int index, List<Figure> into)
		{
			into.Clear();
			if (sky == null || clouds == null)
			{
				return;
			}
			var bands = sky.CloudBands;
			var coverage = sky.CloudBandCoverage;
			if (bands == null || index >= bands.Count || bands[index] == null)
			{
				return;
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
			bool drawing = cover > 0.001f && density > 0.001f && fill > 0.005f;
			string state = cover <= 0.001f
				? (band.CoverageOnset > 0.001f && sky.CloudCover < band.CoverageOnset
					? $"empty — waits for cover {band.CoverageOnset:0.00}"
					: "empty")
				: density <= 0.001f ? "empty — density 0"
				: fill <= 0.005f ? "empty — cut above the field"
				: "drawing";
			float ceilingNow = floorNow + band.Thickness;
			bool moved = Mathf.Abs(floorNow - band.Bottom) > 1f;
			const string Group = "This band";

			into.Add(new Figure(Group, "State", state, drawing ? Tone.Good : Tone.Dim));
			into.Add(new Figure(Group, "Sky filled", Pct(fill), drawing ? Tone.Plain : Tone.Dim, $"Coverage asked {Pct(cover)}, which cuts the noise at {threshold:0.000}."));
			into.Add(new Figure(Group, "Floor", $"{floorNow:0} m", Tone.Plain,
				moved ? $"Authored at {band.Bottom:0} m; moved {floorNow - band.Bottom:+0;-0} m by the condensation level." : null));
			into.Add(new Figure(Group, "Ceiling", $"{ceilingNow:0} m"));
			into.Add(new Figure(Group, "Depth", $"{band.Thickness:0} m"));
			into.Add(new Figure(Group, "Optical depth", depth.ToString("0"), Tone.Plain, $"Density {density:0.00} through the band's depth, over the share of sky it fills. Whether it reads as haze or as a wall."));
			into.Add(new Figure(Group, "Tops", band.Convection > 0.001f
				? $"{floorNow + band.Thickness * Mathf.Lerp(1f, 0.3f, band.Convection):0}–{ceilingNow:0} m"
				: "level deck", band.Convection > 0.001f ? Tone.Plain : Tone.Dim,
				band.Convection > 0.001f ? $"Flat base, lumpy tops (convection {band.Convection:0.00})." : "No convection: a flat top as well as a flat base."));
			into.Add(new Figure(Group, "Drift", $"{sky.CloudWindSpeed * band.WindScale:0.0} m/s"));
			into.Add(new Figure(Group, "Masses every", $"{band.NoiseScale:0} m", Tone.Plain,
				band.Stretch > 1.01f ? $"Drawn out {band.Stretch:0.0}× downwind." : null));
			into.Add(new Figure(Group, "Wisps every", $"{band.DetailScale:0} m"));
			into.Add(new Figure(Group, "Carries rain", band.CarriesRain ? "yes" : "no", band.CarriesRain ? Tone.Plain : Tone.Dim));
			into.Add(new Figure(Group, "Grows storms", band.GrowsStorms ? "yes" : "no", band.GrowsStorms ? Tone.Plain : Tone.Dim));
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
