using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The air over the whole visible sky, place by place: what the clouds everywhere in view are
	/// worked out from.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The sky used to be told about the air at one place — where the camera stood — and drew every
	/// cloud in view from that one reading, bent by a straight line fitted across it. An unstable,
	/// humid air mass thirty kilometres off drew exactly the same kind of cloud as the dry settled
	/// air overhead. This samples the same air the weather is worked out from, over 128 km round the
	/// viewer at 4 km a texel — the systems it follows are a hundred and more across — and hands the
	/// shader, for each place: how much low cloud, where its base is, how high an ordinary cloud and
	/// a tower can climb, how vigorous the convection is, and how much middle and high cloud.
	/// </para>
	/// <para>
	/// Read at sea level. The shader already lifts the cloud field over high ground as the air rides
	/// over it; reading the air at the ground as well would lift it twice.
	/// </para>
	/// <para>
	/// Built a slice at a time and cross-faded from the previous map as it lands, so the sky never
	/// steps. The field it samples drifts a texel in minutes of world time.
	/// </para>
	/// <para>
	/// <b>Paced by the world clock, not by the frame.</b> Each texel is the whole light physics path —
	/// the synoptic field's four lookups and two lattices, the place's climate, an air column with its
	/// exponentials — some microseconds of main thread apiece (counted, not measured). It used to probe
	/// 96 a frame whatever the clock did, a whole map every eleven frames: right for the World Sim bed,
	/// whose clock runs at 180× and carries the air a twelfth of a texel between maps, but in the game,
	/// whose clock runs at 4×, the air moves about nine metres — a fifth of a percent of a texel —
	/// between one map and the next, and re-deriving it cost some half a millisecond of every frame.
	/// It is now built over <see cref="WorldSecondsPerMap"/>
	/// of world time, no slower than <see cref="RealSecondsPerMap"/> of real time and no faster than
	/// <see cref="TexelsPerFrame"/> a frame (<see cref="SliceFor"/>). Kept on the CPU: this is the
	/// server's own weather physics, and a second copy in a shader would be one more thing to keep
	/// in step with it for a map that now costs a few texels a frame.
	/// </para>
	/// </remarks>
	public sealed class CloudAirMap
	{
		public const int Resolution = 32;
		public const float SizeMeters = 128000f;
		/// <summary>The most texels probed in a frame: a whole map in eleven frames, when the clock is fast.</summary>
		public const int TexelsPerFrame = 96;

		/// <summary>
		/// World seconds a whole map is built over. The air is carried along at
		/// <see cref="WeatherDriver.AdvectionSpeed"/>, 4–12 m/s: 240 m at most over a build, six hundredths
		/// of a 4 km texel — well inside what the texture filter already blurs across, and the cross-fade
		/// spreads it over the build besides.
		/// </summary>
		public const float WorldSecondsPerMap = 20f;

		/// <summary>
		/// The longest, in real seconds, a whole map may take however slowly the clock runs — a paused
		/// clock included — so that an offset changed without asking for a rebuild (the World Sim bed's
		/// sliders) still shows within a few seconds, eased in by the cross-fade.
		/// </summary>
		public const float RealSecondsPerMap = 3f;

		/// <summary>
		/// Texels to probe this frame, for a frame that moved the world clock by
		/// <paramref name="worldSeconds"/> and took <paramref name="realSeconds"/>: enough to finish a map
		/// in <see cref="WorldSecondsPerMap"/> of world time or <see cref="RealSecondsPerMap"/> of real time,
		/// whichever comes first, at least one and never more than <see cref="TexelsPerFrame"/>.
		/// </summary>
		public static int SliceFor(double worldSeconds, float realSeconds)
		{
			double world = double.IsNaN(worldSeconds) || double.IsInfinity(worldSeconds) ? 0.0 : System.Math.Abs(worldSeconds);
			float real = float.IsNaN(realSeconds) ? 0f : Mathf.Max(0f, realSeconds);
			double share = System.Math.Max(world / WorldSecondsPerMap, real / RealSecondsPerMap);
			double texels = System.Math.Ceiling(Resolution * Resolution * System.Math.Min(share, 1.0));
			return Mathf.Clamp((int)texels, 1, TexelsPerFrame);
		}

		public static readonly int AirAId = Shader.PropertyToID("_FishCloudAirA");
		public static readonly int AirBId = Shader.PropertyToID("_FishCloudAirB");
		public static readonly int PrevAirAId = Shader.PropertyToID("_FishCloudAirPrevA");
		public static readonly int PrevAirBId = Shader.PropertyToID("_FishCloudAirPrevB");
		public static readonly int RectId = Shader.PropertyToID("_FishCloudAirRect");
		public static readonly int PrevRectId = Shader.PropertyToID("_FishCloudAirPrevRect");

		/// <summary>What the map is worked out from this frame.</summary>
		public struct Inputs
		{
			public WeatherTimeline Timeline;
			public WorldSceneSettings Settings;
			public PlanetAir Planet;
			public AirOffsets Offsets;
			public AtmosphereKind Atmosphere;
			public WindBelts Belts;
			public double WorldSeconds;
			public float Season01;
			public float LocalTime01;
			public float DaylightShare;
			public bool HasDaylight;
			public uint Tick;
		}

		private Texture2D a, b, prevA, prevB;
		private readonly Color[] buildA = new Color[Resolution * Resolution];
		private readonly Color[] buildB = new Color[Resolution * Resolution];
		private Vector2 corner, prevCorner, buildCorner;
		private int cursor = -1;
		private float blend = 1f;
		private float buildSeconds;
		private float lastBuildSeconds = 0.25f;
		private double lastWorldSeconds = double.NaN;
		private CloudClimate.MapExtremes extremes, prevExtremes, building;

		/// <summary>True once a map has been published.</summary>
		public bool Valid { get; private set; }

		/// <summary>The extremes of the maps being shown, previous and current together.</summary>
		public CloudClimate.MapExtremes Extremes
		{
			get
			{
				if (!prevExtremes.Valid || blend >= 1f)
				{
					return extremes;
				}
				return new CloudClimate.MapExtremes
				{
					Valid = true,
					LowestBase = Mathf.Min(extremes.LowestBase, prevExtremes.LowestBase),
					HighestTower = Mathf.Max(extremes.HighestTower, prevExtremes.HighestTower),
					ThinnestDeck = Mathf.Min(extremes.ThinnestDeck, prevExtremes.ThinnestDeck),
					MaxLow = Mathf.Max(extremes.MaxLow, prevExtremes.MaxLow),
					MaxMid = Mathf.Max(extremes.MaxMid, prevExtremes.MaxMid),
					MaxHigh = Mathf.Max(extremes.MaxHigh, prevExtremes.MaxHigh),
				};
			}
		}

		/// <summary>
		/// The air at sea level over a place, and its column: the light physics path the map is made
		/// of. Storms are left out — they are drawn from the weather map — and so are volumes, which
		/// are the ground's business, not the sky's.
		/// </summary>
		public static void Probe(in Inputs inputs, Vector2 place, out WeatherDriver.Synoptic air, out AirColumn column)
		{
			WeatherTimeline timeline = inputs.Timeline;
			WorldSceneSettings settings = inputs.Settings;
			var position = new Vector3(place.x, 0f, place.y);
			air = WeatherField.OpenAirAt(timeline, place, inputs.WorldSeconds, inputs.Season01, inputs.LocalTime01, inputs.Belts);
			float waterLine = settings != null && settings.Climate != null ? settings.Climate.WaterSurfaceHeight : ClimateModel.DefaultWaterSurfaceHeight;
			// At the place, so a generated scene's sky reads the latitude and moisture its ground was painted from.
			ClimateSample climate = settings != null ? settings.SampleClimateAt(position, waterLine) : default;
			air = WeatherDriver.OverPlace(air, climate.Humidity);
			air = WeatherDriver.InClimate(air, climate.Temperature);
			if (inputs.HasDaylight)
			{
				air = WeatherDriver.UnderSun(air, inputs.DaylightShare);
			}
			air = inputs.Offsets.Apply(air);
			float temperature = Mathf.Clamp(climate.Temperature + air.Temperature * 0.35f, -1f, 1f);
			column = AirColumn.Of(inputs.Planet, inputs.Planet.SurfaceKelvin(temperature), air.Humidity, air.Pressure, air.Instability);
		}

		/// <summary>Advances the build by a slice, publishing a finished map and easing it in.</summary>
		public void Update(in Inputs inputs, Vector3 viewer, float deltaTime)
		{
			Ensure();
			float texel = SizeMeters / Resolution;
			double worldStep = double.IsNaN(lastWorldSeconds) ? 0.0 : inputs.WorldSeconds - lastWorldSeconds;
			lastWorldSeconds = inputs.WorldSeconds;
			if (!Valid)
			{
				// The first map is built whole: the sky has nothing to show until it exists.
				Begin(viewer, texel);
				while (cursor >= 0)
				{
					Step(inputs, texel, Resolution * Resolution);
				}
				blend = 1f;
				Publish();
				return;
			}
			if (cursor < 0)
			{
				Begin(viewer, texel);
			}
			buildSeconds += Mathf.Max(0f, deltaTime);
			Step(inputs, texel, SliceFor(worldStep, deltaTime));
			// Fade toward the newest map over as long as a map takes to build, so the next one lands
			// just as this one has finished arriving.
			blend = Mathf.Clamp01(blend + Mathf.Max(0f, deltaTime) / Mathf.Max(0.05f, lastBuildSeconds));
			Publish();
		}

		private void Begin(Vector3 viewer, float texel)
		{
			// Snapped, so the map does not crawl as the camera moves.
			buildCorner = new Vector2(
				Mathf.Round((viewer.x - SizeMeters * 0.5f) / texel) * texel,
				Mathf.Round((viewer.z - SizeMeters * 0.5f) / texel) * texel);
			cursor = 0;
			buildSeconds = 0f;
			building = new CloudClimate.MapExtremes
			{
				Valid = true,
				LowestBase = float.MaxValue,
				HighestTower = 0f,
				ThinnestDeck = float.MaxValue,
			};
		}

		/// <summary>What the probes cost the main thread, for the profiler.</summary>
		private static readonly Unity.Profiling.ProfilerMarker StepMarker = new Unity.Profiling.ProfilerMarker("CloudAirMap.Step");

		private void Step(in Inputs inputs, float texel, int count)
		{
			using var marker = StepMarker.Auto();
			int total = Resolution * Resolution;
			for (int n = 0; n < count && cursor >= 0 && cursor < total; n++, cursor++)
			{
				int x = cursor % Resolution, y = cursor / Resolution;
				var place = new Vector2(buildCorner.x + (x + 0.5f) * texel, buildCorner.y + (y + 0.5f) * texel);
				Probe(inputs, place, out WeatherDriver.Synoptic air, out AirColumn column);
				float low = WeatherDriver.CloudCoverUnder(CloudClimate.LowCover(air, column), inputs.Atmosphere);
				float mid = WeatherDriver.CloudCoverUnder(CloudClimate.MidCover(air, column), inputs.Atmosphere);
				float high = WeatherDriver.CloudCoverUnder(CloudClimate.HighCover(air, column), inputs.Atmosphere);
				buildA[cursor] = new Color(low, column.Base, column.Top, column.TowerCeiling);
				buildB[cursor] = new Color(column.Vigour, mid, high, column.Freezing);

				building.LowestBase = Mathf.Min(building.LowestBase, column.Base);
				building.HighestTower = Mathf.Max(building.HighestTower, column.TowerCeiling);
				if (low > 0.02f)
				{
					building.ThinnestDeck = Mathf.Min(building.ThinnestDeck, column.Top - column.Base);
				}
				building.MaxLow = Mathf.Max(building.MaxLow, low);
				building.MaxMid = Mathf.Max(building.MaxMid, mid);
				building.MaxHigh = Mathf.Max(building.MaxHigh, high);
			}
			if (cursor >= total)
			{
				Finish();
			}
		}

		private void Finish()
		{
			cursor = -1;
			lastBuildSeconds = Mathf.Max(0.05f, buildSeconds);
			// What was current becomes what the sky fades away from.
			(prevA, a) = (a, prevA);
			(prevB, b) = (b, prevB);
			prevCorner = corner;
			prevExtremes = extremes;
			corner = buildCorner;
			if (building.ThinnestDeck == float.MaxValue)
			{
				building.ThinnestDeck = 150f;
			}
			extremes = building;
			a.SetPixels(buildA);
			a.Apply(false, false);
			b.SetPixels(buildB);
			b.Apply(false, false);
			if (!Valid)
			{
				// Nothing to fade from yet: the previous map is this one.
				prevA.SetPixels(buildA);
				prevA.Apply(false, false);
				prevB.SetPixels(buildB);
				prevB.Apply(false, false);
				prevCorner = corner;
				prevExtremes = extremes;
			}
			blend = 0f;
			Valid = true;
		}

		private void Publish()
		{
			if (!Valid)
			{
				return;
			}
			Shader.SetGlobalTexture(AirAId, a);
			Shader.SetGlobalTexture(AirBId, b);
			Shader.SetGlobalTexture(PrevAirAId, prevA);
			Shader.SetGlobalTexture(PrevAirBId, prevB);
			Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, SizeMeters, 1f));
			Shader.SetGlobalVector(PrevRectId, new Vector4(prevCorner.x, prevCorner.y, SizeMeters, blend));
		}

		/// <summary>
		/// Builds the next map whole, from the air as it is then, instead of a slice at a time from
		/// the air as it was. For an air that changed all at once: the sky should show it at once, not
		/// keep drawing the old air for as long as the slices take to come round.
		/// </summary>
		public void Invalidate()
		{
			Valid = false;
			cursor = -1;
		}

		/// <summary>Tells the shader there is no map, so it reads the air nowhere.</summary>
		public static void Unpublish()
		{
			Shader.SetGlobalVector(RectId, Vector4.zero);
		}

		private void Ensure()
		{
			if (a != null)
			{
				return;
			}
			a = Make("Cloud Air A");
			b = Make("Cloud Air B");
			prevA = Make("Cloud Air A (previous)");
			prevB = Make("Cloud Air B (previous)");
		}

		private static Texture2D Make(string name)
		{
			return new Texture2D(Resolution, Resolution, TextureFormat.RGBAHalf, false, true)
			{
				name = name,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
		}

		public void Dispose()
		{
			Destroy(ref a);
			Destroy(ref b);
			Destroy(ref prevA);
			Destroy(ref prevB);
			Valid = false;
			cursor = -1;
		}

		private static void Destroy(ref Texture2D texture)
		{
			if (texture == null)
			{
				return;
			}
			if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
			texture = null;
		}
	}
}
