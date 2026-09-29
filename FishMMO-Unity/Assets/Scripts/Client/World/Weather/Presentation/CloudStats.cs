using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Everything the cloud renderer knows about the sky it is drawing, in numbers: the air it was
	/// worked out from, where that air puts each regime of cloud, what the whole sky costs, and how
	/// much of the screen it actually ended up on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A slider tells you what you added to the air. It does not tell you what the air did with it:
	/// a few kelvin of cold can move the freezing level a kilometre, a little humidity can bring a
	/// base down under a lid that was holding the sky clear. These figures close that gap — the air
	/// in real units, the heights it condenses, freezes and stops at, and what each regime of cloud
	/// is doing with that.
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
		public static float ThresholdAt(float coverage)
		{
			float cover = Mathf.Clamp01(coverage);
			float curve = cover * cover;
			return Mathf.Lerp(SkySystem.CloudCutClear, SkySystem.CloudCutFull, cover) - SkySystem.CloudCutBend * curve * curve;
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
		public static void Sky(SkySystem sky, List<Figure> into)
		{
			into.Clear();
			if (sky == null)
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
			WeatherDriver.Synoptic air = sky.CloudViewerAir;
			AirColumn column = sky.CloudViewerColumn;

			// The air overhead, at sea level, in the units a forecast uses.
			into.Add(new Figure("Air", "Temperature", $"{column.SurfaceKelvin - 273.15f:0.0} °C"));
			into.Add(new Figure("Air", "Dew point", $"{column.DewPointKelvin - 273.15f:0.0} °C", Tone.Plain, $"Relative humidity {Pct(column.RelativeHumidity)}."));
			into.Add(new Figure("Air", "Pressure", air.Pressure >= 0.25f ? $"high {air.Pressure:+0.00}" : air.Pressure <= -0.25f ? $"low {air.Pressure:+0.00;-0.00}" : $"{air.Pressure:+0.00;-0.00}"));
			into.Add(new Figure("Air", "Lapse", $"{column.EnvironmentLapse * 1000f:0.0} K/km", Tone.Plain,
				$"The air cools this fast with height. Saturated air {column.MoistLapse * 1000f:0.0}, dry {column.DryLapse * 1000f:0.0}: between the two the air is conditionally unstable."));
			into.Add(new Figure("Air", "Wind", $"{sky.CloudWindSpeed:0.0} m/s toward {Bearing(sky.CloudWind)}"));
			into.Add(new Figure("Air", "Air climbs", $"{sky.CloudClimbHeight:0} m", Tone.Plain, "Ground lower than this the air goes over; higher, it flows round."));

			// Where the air puts things.
			bool free = column.Deep;
			into.Add(new Figure("Column", "Cloud base", $"{column.Base:0} m", Tone.Plain, "Where rising air reaches its dew point: the flat underside of every low cloud."));
			into.Add(new Figure("Column", "Tops", $"{column.Top:0} m", Tone.Plain, "Where an ordinary cloud here stops: a deck's top in settled air, a heap's in unsettled."));
			into.Add(new Figure("Column", "Towers to", free ? $"{column.TowerCeiling:0} m" : "no deep convection",
				free ? Tone.Good : Tone.Dim, free ? $"A lifted parcel rises on its own from {column.FreeConvection:0} m. CAPE {column.Cape:0} J/kg, updraughts {column.Updraft:0} m/s." : float.IsInfinity(column.FreeConvection) || column.FreeConvection >= Mathf.Min(column.Cap, column.Tropopause)
					? "A lifted parcel never gets warmer than the air around it below the lid."
					: $"A lifted parcel rises on its own from {column.FreeConvection:0} m but gains too little by it ({column.Cape:0} J/kg) to build a tower."));
			into.Add(new Figure("Column", "Lid", column.Cap < column.Tropopause - 1f ? $"{column.Cap:0} m" : "none", Tone.Plain, "The inversion a high puts on the sky: nothing ordinary grows through it."));
			into.Add(new Figure("Column", "Freezing level", $"{column.Freezing:0} m"));
			into.Add(new Figure("Column", "Ice level", $"{column.IceLevel:0} m", Tone.Plain, "Where every drop freezes: cirrus lives above it."));
			into.Add(new Figure("Column", "Tropopause", $"{column.Tropopause:0} m", Tone.Plain, "The top of the weather."));
			into.Add(new Figure("Column", "Convection", Pct(column.Vigour), column.Vigour <= 0.01f ? Tone.Dim : Tone.Plain, "How heaped and bubbly the low cloud is: 0 a flat deck, 1 boiling towers."));
			bool crossing = sky.CloudTowerAtCamera > 0.3f;
			into.Add(new Figure("Column", "Tower overhead", crossing ? $"{Pct(sky.CloudTowerAtCamera)} — crossing now" : Pct(sky.CloudTowerAtCamera), crossing ? Tone.Warn : Tone.Plain));

			// What the cloud does to the light, and what the wind does to the cloud.
			into.Add(new Figure("Light", "Cloud here", sky.CloudOverheadDepth > 0.05f ? $"τ {sky.CloudOverheadDepth:0}" : "none", Tone.Plain,
				"The optical depth of an ordinary low cloud over this air: the column's adiabatic water from its base to its top."));
			into.Add(new Figure("Light", "Sky gets through", Pct(sky.CloudDiffuseOverhead), sky.CloudDiffuseOverhead < 0.1f ? Tone.Warn : Tone.Plain,
				"All of it through the gaps; through the cloud, by diffusion, 1/(1 + ¾(1 − g)τ) — about 15% under a cumulus, a few per cent under a storm: dark grey, never Beer's black."));
			into.Add(new Figure("Light", "Wind shear", $"{sky.CloudLeanShear * 1000f:0.0} m/s per km", Tone.Plain,
				$"How fast the wind grows with height here. A heap leans about {Mathf.Rad2Deg * Mathf.Atan(sky.CloudLeanShear * Mathf.Max(100f, column.Top - column.Base) / (2f * Mathf.Max(CloudClimate.HeapUpdraught, column.Updraft))):0}° downwind; the drift of each layer is the world's own wind, which does not change with the weather."));

			// What ended up on the screen.
			into.Add(new Figure("On screen", "Solid cloud", MeasuredCover >= 0f ? Pct(MeasuredCover) : "measuring…", MeasuredCover >= 0f ? Tone.Plain : Tone.Dim));
			into.Add(new Figure("On screen", "Any cloud", MeasuredAnyCloud >= 0f ? Pct(MeasuredAnyCloud) : "—", MeasuredAnyCloud >= 0f ? Tone.Plain : Tone.Dim));
			into.Add(new Figure("On screen", "Precipitation", Pct(sky.CloudPrecipitation), sky.CloudPrecipitation <= 0.005f ? Tone.Dim : Tone.Plain));
			into.Add(new Figure("On screen", "Shell", $"{sky.CloudShellBottom:0}–{sky.CloudShellTop:0} m"));

			into.Add(new Figure("Cost", "Steps", tier.Steps.ToString()));
			into.Add(new Figure("Cost", "Buffer", BufferSize.x > 0 ? $"{(tier.Resolution * 100f):0}% · {BufferSize.x}×{BufferSize.y}" : $"{(tier.Resolution * 100f):0}%"));
			into.Add(new Figure("Cost", "Detail", tier.Detail.ToString("0.00")));
			into.Add(new Figure("Cost", "History", tier.Temporal ? $"steadied {tier.TemporalBlend:0.00}" : "off", tier.Temporal ? Tone.Plain : Tone.Warn));
			into.Add(new Figure("Cost", "Shadows", SkySystem.DrawCloudShadows ? "on" : "off", SkySystem.DrawCloudShadows ? Tone.Plain : Tone.Warn));
			into.Add(new Figure("Cost", "Shafts", SkySystem.DrawGodRays ? "on" : "off", SkySystem.DrawGodRays ? Tone.Plain : Tone.Warn));
		}

		/// <summary>One regime's figures, for its foldout.</summary>
		public static void Band(SkySystem sky, int index, List<Figure> into)
		{
			into.Clear();
			IReadOnlyList<CloudBand> bands = sky != null ? sky.CloudBands : null;
			if (bands == null || index < 0 || index >= bands.Count)
			{
				return;
			}
			CloudBand band = bands[index];
			const string Group = "This regime";
			if (!band.Present)
			{
				into.Add(new Figure(Group, "State", band.Regime == CloudRegime.Ice ? "none — the ice level is above the tropopause" : "none — no room for it", Tone.Dim));
				return;
			}
			float threshold = ThresholdAt(band.Coverage);
			float fill = band.Coverage > 0.001f ? FillForThreshold(threshold) : 0f;
			bool drawing = band.MaxCoverage > 0.001f;
			into.Add(new Figure(Group, "State", band.Coverage > 0.001f ? "overhead" : drawing ? "elsewhere in view" : "none in view", drawing ? Tone.Good : Tone.Dim));
			into.Add(new Figure(Group, "Cover here", Pct(band.Coverage), band.Coverage > 0.001f ? Tone.Plain : Tone.Dim, $"The air asks for {Pct(band.Coverage)} overhead, which cuts the noise at {threshold:0.000} and keeps {Pct(fill)} of it. The most anywhere in view: {Pct(band.MaxCoverage)}."));
			into.Add(new Figure(Group, "Shell", $"{band.Bottom:0}–{band.Top:0} m"));
			if (band.Column)
			{
				into.Add(new Figure(Group, "Opacity", $"{band.Extinction * Mathf.Pow(300f, 2f / 3f) * 1000f:0.0}/km at 300 m", Tone.Plain,
					"Extinction rises as the two-thirds power of the height above the base: grey and thin at the base, dense and bright at the crown."));
			}
			else
			{
				into.Add(new Figure(Group, "Opacity", $"{band.Extinction * 1000f:0.00}/km", Tone.Plain, $"Optical depth through it: {band.Extinction * (band.Top - band.Bottom):0.0}."));
			}
			into.Add(new Figure(Group, "Drops", band.DropletDiameter > 0f ? $"{band.DropletDiameter:0} µm" : "ice", Tone.Plain,
				band.DropletDiameter > 0f
					? $"Their mean diameter, from the water and how many drops share it: they throw light forward with a mean cosine of {band.Asymmetry:0.00}, a spike within a degree of the sun."
					: $"Ice crystals scatter more broadly than drops: mean cosine {band.Asymmetry:0.00}."));
			into.Add(new Figure(Group, "Drift", $"{sky.CloudScales.SteeringWind * band.WindScale:0.0} m/s", Tone.Plain,
				$"{band.WindScale:0.00}× the column: the world's wind at this height against its wind at the base."));
			into.Add(new Figure(Group, "Masses every", $"{band.NoiseScale:0} m", Tone.Plain, band.Stretch > 1.01f ? $"Drawn out {band.Stretch:0}× along the wind by its fall streaks: ice falling through the shear trails behind the head it fell from." : null));
			into.Add(new Figure(Group, "Wisps every", $"{band.DetailScale:0} m", Tone.Plain, $"Mixing threshold {band.DetailStrength:0.00}: the share of dry air an edge takes before its drops are gone. Drier air frays an edge more."));
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
