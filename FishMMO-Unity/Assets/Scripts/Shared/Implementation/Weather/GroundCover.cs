using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Weather
{
	/// <summary>The ground at a point as one of <see cref="GroundCover"/>'s generations holds it: its cover, and how deep its snow lies past the blanket (metres).</summary>
	public struct CoverTrack
	{
		public WeatherCover Cover;
		public float Depth;

		public static CoverTrack Lerp(in CoverTrack a, in CoverTrack b, float t)
		{
			return new CoverTrack
			{
				Cover = new WeatherCover
				{
					Snow = a.Cover.Snow + (b.Cover.Snow - a.Cover.Snow) * t,
					Wet = a.Cover.Wet + (b.Cover.Wet - a.Cover.Wet) * t,
					Ash = a.Cover.Ash + (b.Cover.Ash - a.Cover.Ash) * t,
					Sand = a.Cover.Sand + (b.Cover.Sand - a.Cover.Sand) * t,
				},
				Depth = a.Depth + (b.Depth - a.Depth) * t,
			};
		}

		public bool SameAs(in CoverTrack o)
		{
			return Cover.Snow == o.Cover.Snow && Cover.Wet == o.Cover.Wet && Cover.Ash == o.Cover.Ash && Cover.Sand == o.Cover.Sand && Depth == o.Depth;
		}
	}

	/// <summary>How deep snow can lie past the blanket, and how many world hours of the heaviest fall build it. None on the server.</summary>
	public struct DeepSnow
	{
		public float MaxMetres;
		public float Hours;
	}

	/// <summary>
	/// What lies on the ground — snow, water, ash, sand, and how deep the snow is — as a pure function of the place and
	/// the moment of world time: the same on the server and on every client, whenever each joined, wherever each has
	/// looked, and after any set of the clock. Set the clock, and the ground is the ground of the new moment at once.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>It had a history.</b> The server integrated its scene figure from whenever it started, and sent it; each
	/// client seeded its map from that figure, filled ground coming into view with it, nudged toward each snapshot, and
	/// integrated rows at moments of its own. Two players looking at one field saw two grounds, and a set of the clock
	/// back kept the ground of the future.
	/// </para>
	/// <para>
	/// <b>Now: a fixed look-back.</b> The ground is integrated over a window that ends at the moment, on a grid of
	/// <see cref="StepSeconds"/> steps fixed from the epoch, each under the weather of its own step (the scene's open
	/// air at its centre, <see cref="Forcing"/>, and every storm's footprint at the point: the scene's own,
	/// <see cref="StormSchedule"/>, and those an admin started). Integrating from a fixed start would make the start a
	/// seam, so there are two GENERATIONS, each starting every <see cref="GenerationSeconds"/> from the ground the
	/// climate alone would leave (<see cref="Prior"/>) and running for two; the younger one is faded in over the older
	/// one's second half. A generation forgets everything before its start: wet ground dries in under an hour, ash and
	/// sand clear in one, and snow in a thaw goes in a few, so all of it is long settled by the time its generation
	/// shows. Cold snow is the only thing with a longer memory, and that is what the prior is for: where it is cold
	/// enough for snow to stay, a generation starts white.
	/// </para>
	/// <para>
	/// Between two steps the ground is the blend of the steps either side, so it changes smoothly, and it is the same
	/// blend everywhere.
	/// </para>
	/// </remarks>
	public static class GroundCover
	{
		/// <summary>One step of the ground's grid, world seconds.</summary>
		public const double StepSeconds = WeatherCover.StepSeconds;
		/// <summary>How long a generation runs before the next starts; each is shown for two of these.</summary>
		public const double GenerationSeconds = 3.0 * 3600.0;
		/// <summary>Steps in a generation.</summary>
		public const int StepsPerGeneration = 180;
		/// <summary>How far back the ground remembers: the older generation's start, at the most.</summary>
		public const double MemorySeconds = 2.0 * GenerationSeconds;

		public static long StepOf(double seconds) => (long)Math.Floor(seconds / StepSeconds);
		public static long GenerationOf(double seconds) => (long)Math.Floor(seconds / GenerationSeconds);
		public static long StartStep(long generation) => generation * StepsPerGeneration;

		/// <summary>How much of the younger generation shows at a moment: none as it starts, all of it as the next starts.</summary>
		public static float YoungWeight(double seconds)
		{
			float x = (float)Math.Max(0.0, Math.Min(1.0, (seconds - GenerationOf(seconds) * GenerationSeconds) / GenerationSeconds));
			return x * x * (3f - 2f * x);
		}

		/// <summary>How far between its step and the next a moment is, 0..1.</summary>
		public static float StepFraction(double seconds)
		{
			return (float)Math.Max(0.0, Math.Min(1.0, (seconds - StepOf(seconds) * StepSeconds) / StepSeconds));
		}

		/// <summary>
		/// The weather one step of the grid is integrated under, for the whole scene: the open air over the scene's
		/// middle at that step's moment, and the storms alive then. A storm's own weather at a point is its kind's in
		/// that air (<see cref="FrameAt"/>).
		/// </summary>
		/// <remarks>
		/// One reading of the open air serves the scene: the field turns over across tens of kilometres and a scene is
		/// a few. It was read at the middle of each client's map, which moves with the camera, so the ground depended
		/// on where its player stood.
		/// </remarks>
		public sealed class Forcing
		{
			public long Step;
			public double Seconds;
			public WeatherSample Around;
			public float Temperature;
			public float Sunlight;
			public bool HasWeather;
			public StormFrames Storms;
			public readonly List<StormCell> Cells = new List<StormCell>();
		}

		/// <summary>The weather of one step (cached per timeline; see <see cref="Forcing"/>).</summary>
		public static Forcing ForcingAt(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, long step)
		{
			Cache cache = CacheFor(timeline, settings);
			if (cache.Steps.TryGetValue(step, out Forcing forcing))
			{
				return forcing;
			}
			if (cache.Steps.Count > MaxCachedSteps)
			{
				Trim(cache, step);
			}
			double seconds = step * StepSeconds;
			forcing = new Forcing { Step = step, Seconds = seconds };
			if (timeline != null && timeline.SceneMode == WeatherSceneMode.Own)
			{
				Vector2 c = timeline.Area.width > 0f ? timeline.Area.center : Vector2.zero;
				forcing.Around = WeatherField.SampleAtSeconds(timeline, settings, scene, new Vector3(c.x, 0f, c.y), seconds);
				forcing.Temperature = forcing.Around.Temperature;
				forcing.HasWeather = true;
				forcing.Storms = new StormFrames(forcing.Around);
				StormSchedule.CellsBetween(timeline, settings, scene, seconds, seconds, forcing.Cells);
				for (int i = 0; i < timeline.Cells.Count; i++)
				{
					StormCell cell = timeline.Cells[i];
					if (!StormSchedule.IsScheduled(cell.ID) && cell.BirthSeconds <= seconds && cell.DeathSeconds > seconds)
					{
						forcing.Cells.Add(cell);
					}
				}
			}
			else
			{
				forcing.Around = new WeatherSample { Frame = WeatherFrame.Clear, Background = WeatherFrame.Clear };
			}
			forcing.Sunlight = SceneTime.IsDaylight(settings, seconds / 3600.0) ? 1f : 0f;
			cache.Steps[step] = forcing;
			return forcing;
		}

		/// <summary>
		/// Counts the times what the ground is worked out from changed under a timeline — another scene's settings, a
		/// new timeline, the air or the storms an admin set — so a map holding ground worked out before knows to work
		/// it out again.
		/// </summary>
		public static int VersionOf(WeatherTimeline timeline, WorldSceneSettings settings) => CacheFor(timeline, settings).Version;

		/// <summary>The weather standing over one point at a step: the open air's own, and what each storm reaching it makes in that air.</summary>
		public static WeatherFrame FrameAt(Forcing forcing, Vector3 position) => FrameAt(forcing, position, 0f);

		/// <summary>The same at a place whose climate stands <paramref name="temperatureOffset"/> from the area's middle (<see cref="BandAt"/>).</summary>
		public static WeatherFrame FrameAt(Forcing forcing, Vector3 position, float temperatureOffset)
		{
			if (!forcing.HasWeather || forcing.Cells.Count == 0)
			{
				return OpenFrame(forcing, temperatureOffset);
			}
			var accumulator = new WeatherAccumulator();
			accumulator.Add(forcing.Around.Background, 1f);
			bool any = false;
			for (int i = 0; i < forcing.Cells.Count; i++)
			{
				StormCell cell = forcing.Cells[i];
				float weight = cell.InfluenceAtSeconds(position, forcing.Seconds);
				if (weight <= 0f)
				{
					continue;
				}
				accumulator.Add(forcing.Storms.Of(cell.Kind), weight);
				any = true;
			}
			if (!any)
			{
				// Exactly the open air's, so a point no storm reaches has the shared ground to the last bit.
				return OpenFrame(forcing, temperatureOffset);
			}
			WeatherFrame frame = accumulator.Resolve();
			return Typed(frame, forcing.Temperature + temperatureOffset);
		}

		public static WeatherFrame OpenFrame(Forcing forcing) => OpenFrame(forcing, 0f);

		/// <summary>The open air's frame at a place <paramref name="temperatureOffset"/> warmer or colder than the area's middle.</summary>
		public static WeatherFrame OpenFrame(Forcing forcing, float temperatureOffset) => Typed(forcing.Around.Background, forcing.Temperature + temperatureOffset);

		private static WeatherFrame Typed(WeatherFrame frame, float temperature)
		{
			frame.DeriveSurfaceRates();
			frame.RetypeForTemperature(temperature);
			frame.DeriveSurfaceRates();
			return frame;
		}

		public static bool Touched(Forcing forcing, Vector3 position)
		{
			for (int i = 0; i < forcing.Cells.Count; i++)
			{
				if (forcing.Cells[i].InfluenceAtSeconds(position, forcing.Seconds) > 0f)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>One step of a track under the weather standing over its point.</summary>
		public static CoverTrack Advance(in CoverTrack track, in WeatherFrame frame, Forcing forcing, in DeepSnow deep) => Advance(track, frame, forcing, deep, 0f);

		/// <summary>The same at a place <paramref name="temperatureOffset"/> from the area's middle: its snow melts and its puddles dry by its own warmth.</summary>
		public static CoverTrack Advance(in CoverTrack track, in WeatherFrame frame, Forcing forcing, in DeepSnow deep, float temperatureOffset)
		{
			float temperature = forcing.Temperature + temperatureOffset;
			CoverTrack next = track;
			next.Cover.Integrate(frame, temperature, (float)StepSeconds, forcing.Sunlight);
			if (deep.MaxMetres > 0f)
			{
				next.Depth = WeatherCover.AdvanceDeepSnow(track.Depth, next.Cover.Snow, frame, temperature, (float)StepSeconds,
					forcing.Sunlight, deep.MaxMetres, deep.Hours);
			}
			return next;
		}

		public static CoverTrack Prior(Forcing atStart) => Prior(atStart, 0f);

		/// <summary>The ground a generation starts from at a place <paramref name="temperatureOffset"/> from the area's middle.</summary>
		public static CoverTrack Prior(Forcing atStart, float temperatureOffset)
		{
			var track = new CoverTrack();
			if (atStart.HasWeather && atStart.Around.Planet.HasAir)
			{
				track.Cover.Snow = Mathf.Clamp01(Mathf.InverseLerp(-0.1f, -0.3f, atStart.Temperature + temperatureOffset));
			}
			return track;
		}

		// ── A place's own climate against the area's ───────────────────

		/// <summary>The temperature step places are grouped by, scale units: half a kelvin.</summary>
		public const float BandScale = (float)(0.5 / ClimateModel.KelvinPerUnit);

		/// <summary>Bands either side of the area's middle: ±32 K, past which a place is held at the last.</summary>
		public const int BandReach = 64;

		/// <summary>How many bands there are, the middle's included.</summary>
		public const int BandCount = 2 * BandReach + 1;

		/// <summary>
		/// How much warmer or colder the climate of a place is than the middle of the area the ground's forcing is read at,
		/// on the climate scale, to the nearest band (<see cref="BandScale"/>).
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>One weather, many climates (2026-10-15).</b> The ground's forcing is read once, at the area's middle, so every
		/// texel of the cover map snowed and melted by the middle's temperature: a ridge three hundred metres up, the far
		/// end of a scene four degrees of latitude away, a cold inland basin and a mild coast all whitened together. The
		/// weather over them is one air and is still read once; each place's own climate is its offset from the middle's,
		/// static (altitude, latitude, how inland), added to the middle's moment.
		/// </para>
		/// <para>
		/// Banded so the cover map can keep one shared ground per band rather than one per texel, and so the server's
		/// points and the client's map, which both take it, agree to the band. Main thread: it reads the terrain.
		/// </para>
		/// </remarks>
		public static int BandAt(WorldSceneSettings settings, Vector3 position, float middleKelvin)
		{
			if (settings == null || middleKelvin <= 0f)
			{
				return BandReach;
			}
			float here = BiomeSampler.Read(position, settings, 0f, 0f).Climate.KelvinOrScale;
			return BandOf((here - middleKelvin) / (float)ClimateModel.KelvinPerUnit);
		}

		/// <summary>The climate at the middle of the area the forcing is read at, K: what <see cref="BandAt"/> measures from. 0 with no area.</summary>
		public static float MiddleKelvin(WeatherTimeline timeline, WorldSceneSettings settings)
		{
			if (timeline == null || settings == null)
			{
				return 0f;
			}
			Vector2 c = timeline.Area.width > 0f ? timeline.Area.center : Vector2.zero;
			return BiomeSampler.Read(new Vector3(c.x, 0f, c.y), settings, 0f, 0f).Climate.KelvinOrScale;
		}

		/// <summary>The band of an offset on the climate scale.</summary>
		public static int BandOf(float offset)
		{
			return Mathf.Clamp(Mathf.RoundToInt(offset / BandScale), -BandReach, BandReach) + BandReach;
		}

		/// <summary>The offset a band stands for, climate scale.</summary>
		public static float BandOffset(int band) => (band - BandReach) * BandScale;

		// ── The forcing cache ──────────────────────────────────────────

		private const int MaxCachedSteps = 2 * StepsPerGeneration + 64;

		private sealed class Cache
		{
			public WorldSceneSettings Settings;
			public uint Generation;
			public uint Seed;
			public Rect Area;
			public bool Director;
			public WeatherSceneMode Mode;
			public float Latitude, Longitude;
			public WorldBody Body;
			public AirOffsetEntry Air;
			public long SentCells;
			public int Version;
			public readonly Dictionary<long, Forcing> Steps = new Dictionary<long, Forcing>();
		}

		private static readonly ConditionalWeakTable<WeatherTimeline, Cache> caches = new ConditionalWeakTable<WeatherTimeline, Cache>();
		private static readonly Cache none = new Cache();
		private static readonly List<long> trimScratch = new List<long>();

		private static Cache CacheFor(WeatherTimeline timeline, WorldSceneSettings settings)
		{
			if (timeline == null)
			{
				return none;
			}
			Cache cache = caches.GetValue(timeline, _ => new Cache());
			long sent = SentFingerprint(timeline);
			AirOffsetEntry air = timeline.Air;
			if (cache.Settings != settings || cache.Generation != timeline.Generation || cache.Seed != timeline.Seed || cache.Area != timeline.Area
				|| cache.Director != timeline.Director || cache.Mode != timeline.SceneMode || cache.Latitude != timeline.LatitudeDegrees
				|| cache.Longitude != timeline.LongitudeDegrees || cache.Body != timeline.BodyOverride || cache.SentCells != sent
				|| !SameAir(cache.Air, air))
			{
				cache.Steps.Clear();
				cache.Settings = settings;
				cache.Generation = timeline.Generation;
				cache.Seed = timeline.Seed;
				cache.Area = timeline.Area;
				cache.Director = timeline.Director;
				cache.Mode = timeline.SceneMode;
				cache.Latitude = timeline.LatitudeDegrees;
				cache.Longitude = timeline.LongitudeDegrees;
				cache.Body = timeline.BodyOverride;
				cache.SentCells = sent;
				cache.Air = air;
				cache.Version++;
			}
			return cache;
		}

		/// <summary>The storms an admin or a script started, as one number: any edit to them changes it.</summary>
		private static long SentFingerprint(WeatherTimeline timeline)
		{
			unchecked
			{
				long h = 17;
				for (int i = 0; i < timeline.Cells.Count; i++)
				{
					StormCell c = timeline.Cells[i];
					if (StormSchedule.IsScheduled(c.ID))
					{
						continue;
					}
					h = h * 31 + c.ID;
					h = h * 31 + BitConverter.DoubleToInt64Bits(c.BirthSeconds);
					h = h * 31 + BitConverter.DoubleToInt64Bits(c.DecaySeconds);
					h = h * 31 + BitConverter.DoubleToInt64Bits(c.DeathSeconds);
					h = h * 31 + BitConverter.DoubleToInt64Bits(c.MotionSeconds);
					h = h * 31 + c.OriginX.GetHashCode();
					h = h * 31 + c.OriginZ.GetHashCode();
					h = h * 31 + c.VelocityX.GetHashCode();
					h = h * 31 + c.VelocityZ.GetHashCode();
					h = h * 31 + (long)c.Kind;
				}
				return h;
			}
		}

		/// <summary>Forgets the steps furthest from <paramref name="around"/>, keeping the memory either side of it.</summary>
		private static void Trim(Cache cache, long around)
		{
			trimScratch.Clear();
			foreach (long step in cache.Steps.Keys)
			{
				if (step < around - 2 * StepsPerGeneration - 8 || step > around + 8)
				{
					trimScratch.Add(step);
				}
			}
			foreach (long step in trimScratch)
			{
				cache.Steps.Remove(step);
			}
			if (cache.Steps.Count > MaxCachedSteps)
			{
				cache.Steps.Clear();
			}
		}

		private static bool SameAir(in AirOffsetEntry a, in AirOffsetEntry b)
		{
			return a.StartSeconds == b.StartSeconds && a.EndSeconds == b.EndSeconds
				&& a.From.Temperature == b.From.Temperature && a.From.Humidity == b.From.Humidity && a.From.Pressure == b.From.Pressure
				&& a.From.Instability == b.From.Instability && a.From.Wind == b.From.Wind && a.From.Gravity == b.From.Gravity
				&& a.To.Temperature == b.To.Temperature && a.To.Humidity == b.To.Humidity && a.To.Pressure == b.To.Pressure
				&& a.To.Instability == b.To.Instability && a.To.Wind == b.To.Wind && a.To.Gravity == b.To.Gravity;
		}
	}

	/// <summary>
	/// The ground at a fixed set of points, kept up to the moment it is asked for (<see cref="GroundCover"/>): stepped on
	/// from where it was as the world clock moves on, worked out again from its look-back when the clock is set, when
	/// what it is worked out from changes, or when it is asked for a moment it has not reached by stepping.
	/// </summary>
	public sealed class CoverPoints
	{
		private Vector3[] points = Array.Empty<Vector3>();
		// Each point's own climate against the area's middle (GroundCover.BandAt), as an offset on the climate scale.
		private float[] offsets = Array.Empty<float>();
		private WorldSceneSettings offsetsFor;
		private Rect offsetsArea;
		private CoverTrack[] oldAt = Array.Empty<CoverTrack>(), oldNext = Array.Empty<CoverTrack>();
		private CoverTrack[] youngAt = Array.Empty<CoverTrack>(), youngNext = Array.Empty<CoverTrack>();
		private long step = long.MinValue;
		private long generation;
		private int version = -1;
		private WeatherTimeline timeline;
		private DeepSnow deep;

		/// <summary>The points, in world space. Changing them works the ground out again.</summary>
		public void SetPoints(IReadOnlyList<Vector3> where)
		{
			bool same = where.Count == points.Length;
			for (int i = 0; same && i < points.Length; i++)
			{
				same = points[i] == where[i];
			}
			if (same)
			{
				return;
			}
			points = new Vector3[where.Count];
			for (int i = 0; i < points.Length; i++)
			{
				points[i] = where[i];
			}
			oldAt = new CoverTrack[points.Length];
			oldNext = new CoverTrack[points.Length];
			youngAt = new CoverTrack[points.Length];
			youngNext = new CoverTrack[points.Length];
			offsets = new float[points.Length];
			offsetsFor = null;
			step = long.MinValue;
		}

		/// <summary>Each point's band against the area's middle, worked out again when the scene or its area changes.</summary>
		private bool RefreshOffsets(WeatherTimeline timeline, WorldSceneSettings settings)
		{
			Rect area = timeline != null ? timeline.Area : default;
			if (ReferenceEquals(offsetsFor, settings) && area == offsetsArea && offsets.Length == points.Length)
			{
				return false;
			}
			float middle = GroundCover.MiddleKelvin(timeline, settings);
			for (int i = 0; i < points.Length; i++)
			{
				offsets[i] = GroundCover.BandOffset(GroundCover.BandAt(settings, points[i], middle));
			}
			offsetsFor = settings;
			offsetsArea = area;
			return true;
		}

		/// <summary>Brings the ground at every point to <paramref name="seconds"/> of world time.</summary>
		public void Update(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, double seconds, DeepSnow deep = default)
		{
			long g = GroundCover.StepOf(seconds);
			long j = GroundCover.GenerationOf(seconds);
			int v = GroundCover.VersionOf(timeline, settings);
			bool moved = RefreshOffsets(timeline, settings);
			bool rebuild = moved || step == long.MinValue || v != version || !ReferenceEquals(timeline, this.timeline)
				|| deep.MaxMetres != this.deep.MaxMetres || deep.Hours != this.deep.Hours
				|| g < step || j < generation || g - step > GroundCover.StepsPerGeneration;
			this.timeline = timeline;
			this.deep = deep;
			version = v;
			if (rebuild)
			{
				Rebuild(timeline, settings, scene, g, j);
				return;
			}
			while (step < g)
			{
				StepForward(timeline, settings, scene);
			}
		}

		/// <summary>The ground at point <paramref name="index"/> at <paramref name="seconds"/> (which <see cref="Update"/> brought it to).</summary>
		public CoverTrack At(int index, double seconds)
		{
			if (index < 0 || index >= points.Length || step == long.MinValue)
			{
				return default;
			}
			float f = GroundCover.StepFraction(seconds);
			CoverTrack older = CoverTrack.Lerp(oldAt[index], oldNext[index], f);
			CoverTrack younger = CoverTrack.Lerp(youngAt[index], youngNext[index], f);
			return CoverTrack.Lerp(older, younger, GroundCover.YoungWeight(seconds));
		}

		/// <summary>The mean cover over every point at <paramref name="seconds"/>.</summary>
		public WeatherCover Mean(double seconds)
		{
			WeatherCover mean = default;
			if (points.Length == 0)
			{
				return mean;
			}
			for (int i = 0; i < points.Length; i++)
			{
				WeatherCover c = At(i, seconds).Cover;
				mean.Snow += c.Snow;
				mean.Wet += c.Wet;
				mean.Ash += c.Ash;
				mean.Sand += c.Sand;
			}
			float n = points.Length;
			mean.Snow /= n;
			mean.Wet /= n;
			mean.Ash /= n;
			mean.Sand /= n;
			return mean;
		}

		private void Rebuild(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, long g, long j)
		{
			long youngStart = GroundCover.StartStep(j);
			long oldStart = youngStart - GroundCover.StepsPerGeneration;
			GroundCover.Forcing first = GroundCover.ForcingAt(timeline, settings, scene, oldStart);
			for (int i = 0; i < points.Length; i++)
			{
				oldNext[i] = GroundCover.Prior(first, offsets[i]);
			}
			for (long k = oldStart + 1; k <= g + 1; k++)
			{
				GroundCover.Forcing forcing = GroundCover.ForcingAt(timeline, settings, scene, k);
				for (int i = 0; i < points.Length; i++)
				{
					if (k == youngStart)
					{
						youngNext[i] = GroundCover.Prior(forcing, offsets[i]);
					}
					if (k == g + 1)
					{
						oldAt[i] = oldNext[i];
						youngAt[i] = youngNext[i];
					}
					WeatherFrame frame = GroundCover.FrameAt(forcing, points[i], offsets[i]);
					oldNext[i] = GroundCover.Advance(oldNext[i], frame, forcing, deep, offsets[i]);
					if (k > youngStart)
					{
						youngNext[i] = GroundCover.Advance(youngNext[i], frame, forcing, deep, offsets[i]);
					}
				}
			}
			step = g;
			generation = j;
		}

		private void StepForward(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene)
		{
			long next = step + 1;
			GroundCover.Forcing ahead = GroundCover.ForcingAt(timeline, settings, scene, next + 1);
			if (next == GroundCover.StartStep(generation + 1))
			{
				// A new generation starts: the young one becomes the old, and the new one starts from the climate's ground.
				GroundCover.Forcing atStart = GroundCover.ForcingAt(timeline, settings, scene, next);
				for (int i = 0; i < points.Length; i++)
				{
					CoverTrack start = GroundCover.Prior(atStart, offsets[i]);
					WeatherFrame frame = GroundCover.FrameAt(ahead, points[i], offsets[i]);
					oldAt[i] = youngNext[i];
					oldNext[i] = GroundCover.Advance(oldAt[i], frame, ahead, deep, offsets[i]);
					youngAt[i] = start;
					youngNext[i] = GroundCover.Advance(start, frame, ahead, deep, offsets[i]);
				}
				generation++;
			}
			else
			{
				for (int i = 0; i < points.Length; i++)
				{
					WeatherFrame frame = GroundCover.FrameAt(ahead, points[i], offsets[i]);
					oldAt[i] = oldNext[i];
					oldNext[i] = GroundCover.Advance(oldAt[i], frame, ahead, deep, offsets[i]);
					youngAt[i] = youngNext[i];
					youngNext[i] = GroundCover.Advance(youngAt[i], frame, ahead, deep, offsets[i]);
				}
			}
			step = next;
		}
	}
}
