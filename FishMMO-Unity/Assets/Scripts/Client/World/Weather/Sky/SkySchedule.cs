using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>A lightning strike: when, where it hits, how bright.</summary>
	public struct LightningStrike
	{
		public double Time;
		public Vector3 Ground;
		public float CloudHeight;
		public uint Seed;
		public float Intensity;
	}

	/// <summary>A meteor: when, where it starts, where it heads, how long it lasts.</summary>
	public struct Meteor
	{
		public double Time;
		public Vector3 Direction;
		public Vector3 Heading;
		public float Length;
		public float Duration;
		public float Brightness;
	}

	/// <summary>
	/// When lightning strikes and meteors fall. Both are pure functions of the world time and the
	/// weather or sky, so every client sees the same strike at the same moment without messages.
	/// Lightning is visual only.
	/// </summary>
	public static class SkySchedule
	{
		/// <summary>
		/// How often a rate of 1 strikes, per second of world time. A strike every eight seconds, so
		/// a thunderstorm gives about five a minute and the heaviest rain about two and a half —
		/// which is a strong storm. It was 0.5, twenty-one a minute, three times the busiest real
		/// storm and closer to a strobe than to weather.
		/// </summary>
		public const float StrikesPerSecondAtFullRate = 0.12f;

		/// <summary>
		/// Scales every lightning rate. 1 in the game. A test bed running its clock at a hundred and
		/// eighty times real time sets it to the reciprocal, or a storm there is sixty flashes a
		/// second — true to the world clock, and unwatchable.
		/// </summary>
		public static float RateScale = 1f;

		public const double LightningSlotSeconds = 0.2;
		public const double MeteorSlotSeconds = 0.25;

		public static uint Hash(uint a, uint b)
		{
			unchecked
			{
				uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
				h ^= h >> 15;
				h *= 0x2C1B3C6Du;
				h ^= h >> 12;
				h *= 0x297A2D39u;
				h ^= h >> 15;
				return h;
			}
		}

		public static float Unit(uint h) => (h & 0xFFFFFF) / (float)0x1000000;

		/// <summary>
		/// The scene's own thunder — the field's storms, heavy rain — is laid on a grid of patches this
		/// wide fixed in the world, each striking at the rate the weather has over it. It struck round
		/// each viewer at the viewer's own rate, so every player saw the same bolt somewhere else, and one
		/// standing in dry air saw none of the storm next door.
		/// </summary>
		public const float ScenePatchMeters = 1000f;

		/// <summary>
		/// How far from the viewer the scene's strikes are looked for: as far as a flash still lights the
		/// sky (LightningPresenter fades it out by about five kilometres) and thunder is still heard.
		/// </summary>
		public const float SceneReachMeters = 4000f;

		/// <summary>
		/// The ground a rate's strikes were spread over when they struck round the viewer, 250 m to
		/// 3 km out: a patch strikes its own share of that, so a storm overhead flashes as often as it did.
		/// </summary>
		private const float ReferenceArea = Mathf.PI * (3000f * 3000f - 250f * 250f);

		/// <summary>
		/// Strikes in [from, to) world seconds: from storm cells (in the cell) and from the scene's own
		/// lightning (in the patches round the viewer). Each strike is where and when it is for every
		/// player; the viewer only decides which of them are near enough to see.
		/// </summary>
		/// <param name="around">
		/// The weather at the viewer, which stands in for the weather everywhere when there is no
		/// <see cref="LightningAir"/> to read it where the strikes are (the tests, a bare timeline).
		/// </param>
		public static void Lightning(WeatherTimeline timeline, uint tick, double from, double to, Vector3 viewer, List<LightningStrike> into, in WeatherSample around)
		{
			Lightning(timeline, tick, from, to, viewer, into, around, null, null);
		}

		/// <summary>The same, reusing a caller's per-kind storm weather.</summary>
		public static void Lightning(WeatherTimeline timeline, uint tick, double from, double to, Vector3 viewer, List<LightningStrike> into, in WeatherSample around, StormFrames storms)
		{
			Lightning(timeline, tick, from, to, viewer, into, around, storms, null);
		}

		/// <summary>
		/// The same, with the weather read where each strike is (<paramref name="air"/>): what the game
		/// does, so whether a strike happens never depends on who is looking.
		/// </summary>
		/// <param name="tick">Unused: every strike is read at the tick of its own moment.</param>
		public static void Lightning(WeatherTimeline timeline, uint tick, double from, double to, Vector3 viewer, List<LightningStrike> into, in WeatherSample around,
			StormFrames storms, LightningAir air)
		{
			if (timeline == null || to <= from)
			{
				return;
			}
			long first = (long)Math.Floor(from / LightningSlotSeconds);
			long last = (long)Math.Floor(to / LightningSlotSeconds);
			float perRate = StrikesPerSecondAtFullRate * Mathf.Max(0f, RateScale) * (float)LightningSlotSeconds;

			/* The scene's own lightning, patch by patch round the viewer. Whether a patch strikes in a
			 * slot, and where in it, is a hash of the patch and the slot; the rate it is held to is the
			 * weather over that patch (LightningAir.SceneRate), which every client reads the same. */
			float uniform = Mathf.Clamp01(around.Background[WeatherChannel.LightningRate]);
			if (air != null || uniform > 0.001f)
			{
				float share = ScenePatchMeters * ScenePatchMeters / ReferenceArea;
				int minI = Mathf.FloorToInt((viewer.x - SceneReachMeters) / ScenePatchMeters);
				int maxI = Mathf.FloorToInt((viewer.x + SceneReachMeters) / ScenePatchMeters);
				int minJ = Mathf.FloorToInt((viewer.z - SceneReachMeters) / ScenePatchMeters);
				int maxJ = Mathf.FloorToInt((viewer.z + SceneReachMeters) / ScenePatchMeters);
				float reachSq = SceneReachMeters * SceneReachMeters;
				for (int i = minI; i <= maxI; i++)
				{
					for (int j = minJ; j <= maxJ; j++)
					{
						// A patch with no part within reach strikes nothing this viewer would see.
						float nearX = Mathf.Clamp(viewer.x, i * ScenePatchMeters, (i + 1) * ScenePatchMeters) - viewer.x;
						float nearZ = Mathf.Clamp(viewer.z, j * ScenePatchMeters, (j + 1) * ScenePatchMeters) - viewer.z;
						if (nearX * nearX + nearZ * nearZ > reachSq)
						{
							continue;
						}
						uint patch = Hash(timeline.Seed, PatchKey(i, j));
						for (long slot = first; slot <= last; slot++)
						{
							uint h = Hash(patch, (uint)slot);
							// The cheap test first: most slots strike nowhere even at the full rate.
							if (Unit(h) >= perRate * share)
							{
								continue;
							}
							double time = (slot + Unit(Hash(h, 1))) * LightningSlotSeconds;
							if (time < from || time >= to)
							{
								continue;
							}
							float rate = uniform;
							if (air != null && !air.SceneRate(timeline, i, j, time, out rate))
							{
								// Not read yet (LightningAir spreads its reads over frames): this viewer misses it.
								continue;
							}
							if (Unit(h) >= Mathf.Clamp01(rate) * perRate * share)
							{
								continue;
							}
							var point = new Vector2((i + Unit(Hash(h, 2))) * ScenePatchMeters, (j + Unit(Hash(h, 3))) * ScenePatchMeters);
							if ((point - new Vector2(viewer.x, viewer.z)).sqrMagnitude > reachSq)
							{
								continue;
							}
							into.Add(new LightningStrike
							{
								Time = time,
								Ground = new Vector3(point.x, GroundAt(point), point.y),
								CloudHeight = Mathf.Lerp(900f, 1500f, Unit(Hash(h, 4))),
								Seed = h,
								Intensity = Mathf.Lerp(0.6f, 1f, Unit(Hash(h, 5))),
							});
						}
					}
				}
			}

			if (timeline.SceneMode != WeatherSceneMode.Own)
			{
				return;
			}
			if (air == null)
			{
				storms ??= new StormFrames(around);
			}
			for (int c = 0; c < timeline.Cells.Count; c++)
			{
				StormCell cell = timeline.Cells[c];
				/* How hard its kind of storm thunders in the CELL's own air (StormCellAir), not in the
				 * viewer's: that decided whether a storm thundered at all by where each player stood. */
				WeatherFrame peak;
				if (air != null)
				{
					StormCellAir.Air own = StormCellAir.Of(timeline, air.Settings, air.Scene, cell);
					if (own == null)
					{
						continue;
					}
					peak = own.Frames.Of(cell.Kind);
				}
				else
				{
					peak = storms.Of(cell.Kind);
				}
				float kindRate = peak[WeatherChannel.LightningRate] * cell.PeakIntensity;
				if (kindRate <= 0.001f)
				{
					continue;
				}
				for (long slot = first; slot <= last; slot++)
				{
					uint h = Hash(cell.Seed ^ ((uint)cell.ID << 16), (uint)slot);
					if (Unit(h) >= kindRate * perRate)
					{
						continue;
					}
					double time = (slot + Unit(Hash(h, 1))) * LightningSlotSeconds;
					if (time < from || time >= to)
					{
						continue;
					}
					// The cell as it is at the strike's own moment of world time: its strength and where it stands then.
					if (Unit(h) >= kindRate * cell.EnvelopeAtSeconds(time) * perRate)
					{
						continue;
					}
					Vector2 centre = cell.CentreAtSeconds(time);
					/* Placed by the cell's own SHAPE. A disc of strikes around the centre put a
					 * squall line's whole display at one point and struck a hurricane in its eye. */
					Vector2 point = centre + cell.PointInside(Unit(Hash(h, 2)), Unit(Hash(h, 3)));
					into.Add(new LightningStrike
					{
						Time = time,
						// On the ground under it, not two metres under whoever was looking.
						Ground = new Vector3(point.x, GroundAt(point), point.y),
						CloudHeight = Mathf.Lerp(900f, 1500f, Unit(Hash(h, 4))),
						Seed = h,
						Intensity = Mathf.Lerp(0.7f, 1f, Unit(Hash(h, 5))),
					});
				}
			}
		}

		/// <summary>The scene patch's own number, for its hash.</summary>
		/// <remarks>
		/// Mixed through <see cref="Hash"/>, not i·A ^ j·B: with both multipliers odd that gave a patch and
		/// its mirror through the origin the same key — (1, −3) and (−1, 3) struck at the same moments —
		/// 1688 collisions in a 101-patch square (caught by SkyTests). This has none in a 601-patch one.
		/// </remarks>
		public static uint PatchKey(int i, int j) => Hash(unchecked((uint)i ^ 0x51ED270Bu), unchecked((uint)j));

		/// <summary>
		/// The (fractional) tick a moment of world time falls on, by the timeline's own anchor — the
		/// inverse of <see cref="WeatherTimeline.WorldSecondsAt"/>, so every client reads the same tick.
		/// </summary>
		public static double TickAt(WeatherTimeline timeline, double worldSeconds)
		{
			// The inverse of what the timeline reads (WeatherTimeline.WorldSecondsAt): the world clock's, pace and all.
			if (WorldClock.Shared.HasAnchor)
			{
				return Math.Max(0.0, WorldClock.Shared.TickAt(worldSeconds));
			}
			double delta = timeline.TickDelta > 0.0 ? timeline.TickDelta : 1.0 / 30.0;
			return Math.Max(0.0, timeline.WorldSecondsTick + (worldSeconds - timeline.WorldSecondsAtTick) / delta);
		}

		/// <summary>The ground a strike lands on: the terrain, or the sea over it.</summary>
		private static float GroundAt(Vector2 point) => VortexPresenter.GroundAt(point, out _);

		/// <summary>The jagged path of a bolt, from the cloud down to the ground, with a few branches.</summary>
		public static void BoltPath(in LightningStrike strike, List<Vector3> trunk, List<List<Vector3>> branches)
		{
			trunk.Clear();
			branches.Clear();
			const int segments = 18;
			Vector3 top = strike.Ground + Vector3.up * strike.CloudHeight;
			Vector3 point = top;
			for (int i = 0; i <= segments; i++)
			{
				float t = i / (float)segments;
				Vector3 straight = Vector3.Lerp(top, strike.Ground, t);
				uint h = Hash(strike.Seed, (uint)(i + 100));
				float wobble = strike.CloudHeight * 0.05f * (1f - t * 0.6f);
				point = i == 0 || i == segments ? straight : straight + new Vector3((Unit(h) - 0.5f) * wobble, 0f, (Unit(Hash(h, 1)) - 0.5f) * wobble);
				trunk.Add(point);
				if (i > 2 && i < segments - 3 && Unit(Hash(h, 2)) < 0.22f)
				{
					var branch = new List<Vector3> { point };
					Vector3 b = point;
					Vector3 drift = new Vector3(Unit(Hash(h, 3)) - 0.5f, -0.6f, Unit(Hash(h, 4)) - 0.5f).normalized * (strike.CloudHeight * 0.06f);
					for (int k = 0; k < 4; k++)
					{
						b += drift + new Vector3((Unit(Hash(h, (uint)(10 + k))) - 0.5f) * wobble * 0.5f, 0f, 0f);
						branch.Add(b);
					}
					branches.Add(branch);
				}
			}
		}

		/// <summary>
		/// Meteors in [from, to) world seconds for a sky: sporadic ones anywhere, shower members
		/// streaking away from their radiant. Whether the sky is dark enough to see them is the drawing's
		/// business, not this: the schedule is the same for everyone.
		/// </summary>
		/// <param name="limit">
		/// The most added from this window: those ranked first by a hash of their own, so a lower tier
		/// shows a subset of what a higher one does, and every client with one budget the same ones. It
		/// took the first in time, counting whatever the list already held.
		/// </param>
		public static void Meteors(CelestialState state, double from, double to, int limit, List<Meteor> into)
		{
			if (state == null || state.System == null || state.MeteorRate <= 0f || to <= from)
			{
				return;
			}
			float perSlot = state.MeteorRate / 3600f * (float)MeteorSlotSeconds * 6f; // several per slot on a visible hemisphere
			long first = (long)Math.Floor(from / MeteorSlotSeconds);
			long last = (long)Math.Floor(to / MeteorSlotSeconds);
			var showers = state.System.MeteorShowers;
			double day = state.Hours / CelestialMath.HomeSolarDayHours(state.System);
			double dayOfYear = day - Math.Floor(day / state.System.DaysPerYear) * state.System.DaysPerYear;
			if (limit <= 0)
			{
				return;
			}
			ranked.Clear();
			for (long slot = first; slot <= last; slot++)
			{
				uint h = Hash(0xA5A5u, (uint)slot);
				if (Unit(h) >= perSlot)
				{
					continue;
				}
				double time = (slot + Unit(Hash(h, 1))) * MeteorSlotSeconds;
				if (time < from || time >= to)
				{
					continue;
				}
				float alt = Mathf.Lerp(15f, 80f, Unit(Hash(h, 2)));
				float az = Unit(Hash(h, 3)) * 360f;
				Vector3 start = CelestialState.SceneDirection(alt, az, 0.0);
				Vector3 heading;
				// Pick a shower by its share of the rate; otherwise sporadic.
				MeteorShower chosen = null;
				float roll = Unit(Hash(h, 4)) * state.MeteorRate;
				foreach (MeteorShower shower in showers)
				{
					if (shower == null) continue;
					roll -= shower.RateOn(dayOfYear, state.System.DaysPerYear);
					if (roll <= 0f)
					{
						chosen = shower;
						break;
					}
				}
				if (chosen != null)
				{
					Vector3 radiant = state.EquatorialDirection(chosen.RadiantRightAscension * Mathf.Deg2Rad, chosen.RadiantDeclination * Mathf.Deg2Rad);
					heading = (start - radiant * Vector3.Dot(start, radiant)).normalized;
					if (heading.sqrMagnitude < 0.5f) heading = Vector3.down;
				}
				else
				{
					heading = new Vector3(Unit(Hash(h, 5)) - 0.5f, -0.6f, Unit(Hash(h, 6)) - 0.5f).normalized;
				}
				ranked.Add((Hash(h, 10), new Meteor
				{
					Time = time,
					Direction = start,
					Heading = heading,
					Length = Mathf.Lerp(3f, 12f, Unit(Hash(h, 7))) * Mathf.Deg2Rad,
					Duration = Mathf.Lerp(0.3f, 0.9f, Unit(Hash(h, 8))),
					Brightness = Mathf.Lerp(0.4f, 1f, Unit(Hash(h, 9))),
				}));
			}
			if (ranked.Count > limit)
			{
				// The budget's share by rank, then back in time order (ties, which a 32-bit hash all but
				// never makes, by time, so the cut never depends on the sort).
				ranked.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.meteor.Time.CompareTo(b.meteor.Time));
				ranked.RemoveRange(limit, ranked.Count - limit);
				ranked.Sort((a, b) => a.meteor.Time.CompareTo(b.meteor.Time));
			}
			foreach (var entry in ranked)
			{
				into.Add(entry.meteor);
			}
		}

		[ThreadStatic] private static List<(uint rank, Meteor meteor)> rankedScratch;
		private static List<(uint rank, Meteor meteor)> ranked => rankedScratch ??= new List<(uint rank, Meteor meteor)>();
	}

	/// <summary>
	/// The weather lightning is decided by, read where the strikes are: the scene's own thunder over
	/// each patch (<see cref="SkySchedule.ScenePatchMeters"/>), and each storm cell's own air
	/// (<see cref="StormCellAir"/>). Every read is a pure function of the timeline, the scene, the place
	/// and the moment, so every client decides every strike the same.
	/// </summary>
	public sealed class LightningAir
	{
		/// <summary>
		/// How long a patch's rate is held. The field drifts over hours; and a patch is only read when a
		/// slot's hash could strike there at all, a few times a minute round a viewer.
		/// </summary>
		public const double HoldSeconds = 30.0;

		/// <summary>The most patches read afresh in one frame (a whole weather sample each): a teleport into a storm costs no hitch.</summary>
		public const int FreshReadsPerFrame = 8;

		public WorldSceneSettings Settings { get; private set; }
		public Scene Scene { get; private set; }

		private WeatherTimeline timeline;
		private readonly Dictionary<long, (long window, float rate)> rates = new Dictionary<long, (long, float)>();
		private int reads;
		private int readsFrame = -1;

		/// <summary>Reads the weather of this scene from now on; a different one forgets what was read.</summary>
		public void Bind(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene)
		{
			if (timeline != this.timeline || settings != Settings || scene != Scene)
			{
				rates.Clear();
			}
			this.timeline = timeline;
			Settings = settings;
			Scene = scene;
		}

		public void Clear()
		{
			rates.Clear();
			timeline = null;
		}

		/// <summary>
		/// The storm-free lightning rate over a patch at a moment, 0..1: the weather sampled at the
		/// patch's middle, at the start of the hold window the moment is in. False when this frame has
		/// read all it may — the strike is then missed by this viewer alone, as one out of sight is.
		/// </summary>
		public bool SceneRate(WeatherTimeline timeline, int i, int j, double worldSeconds, out float rate)
		{
			uint patch = SkySchedule.PatchKey(i, j);
			// Each patch changes its rate at a moment of its own, so a viewer's few dozen are not all read at once.
			double offset = SkySchedule.Unit(SkySchedule.Hash(patch, 0x51A7u)) * HoldSeconds;
			long window = (long)Math.Floor((worldSeconds + offset) / HoldSeconds);
			long key = ((long)i << 32) | (uint)j;
			if (rates.TryGetValue(key, out var held) && held.window == window)
			{
				rate = held.rate;
				return true;
			}
			if (readsFrame != Time.frameCount)
			{
				readsFrame = Time.frameCount;
				reads = 0;
			}
			if (reads >= FreshReadsPerFrame)
			{
				rate = 0f;
				return false;
			}
			reads++;
			// Read at the moment the window began: the same number whoever reads it, and whenever.
			double start = window * HoldSeconds - offset;
			uint tick = (uint)Math.Floor(SkySchedule.TickAt(timeline, start));
			var middle = new Vector2((i + 0.5f) * SkySchedule.ScenePatchMeters, (j + 0.5f) * SkySchedule.ScenePatchMeters);
			float ground = VortexPresenter.GroundAt(middle, out _);
			WeatherSample sample = WeatherField.Sample(timeline, Settings, Scene, new Vector3(middle.x, ground, middle.y), tick);
			rate = Mathf.Clamp01(sample.Background[WeatherChannel.LightningRate]);
			if (rates.Count > 1024)
			{
				rates.Clear();
			}
			rates[key] = (window, rate);
			return true;
		}
	}

	/// <summary>
	/// The air a storm cell grows in, read once where and when the cell is mature: what everything drawn
	/// of the cell takes its own character and pace from — a tornado's form, how hard it thunders.
	/// </summary>
	/// <remarks>
	/// They were worked out in the air round the VIEWER, so two players in different air saw two
	/// different funnels on one storm, and a funnel changed shape and speed as its watcher walked. This
	/// is a pure function of the timeline, the scene and the cell, read at one fixed tick of its life:
	/// the same on every client, whenever it joined, and constant for the cell's life.
	/// </remarks>
	public static class StormCellAir
	{
		public sealed class Air
		{
			/// <summary>The world seconds it was read at (<see cref="ReferenceSeconds"/>).</summary>
			public double Seconds;
			/// <summary>Where the cell stood then, world x/z.</summary>
			public Vector2 Centre;
			/// <summary>The ground there, or the sea over it.</summary>
			public float Ground;
			public bool Water;
			/// <summary>The weather there and then.</summary>
			public WeatherSample Sample;
			/// <summary>What each kind of storm makes in that air.</summary>
			public StormFrames Frames;

			internal StormKind Kind;
			internal AirOffsets Offsets;
			internal WeatherTimeline Timeline;
			internal WorldSceneSettings Settings;
		}

		private static readonly Dictionary<ushort, Air> known = new Dictionary<ushort, Air>();
		private static readonly List<ushort> gone = new List<ushort>();
		private static int sweptFrame = -1;

		/// <summary>The world seconds a cell's air is read at: when it matures, kept inside its life.</summary>
		public static double ReferenceSeconds(in StormCell cell)
		{
			double first = cell.BirthSeconds + 0.05;
			double last = cell.DeathSeconds - 0.05 > first ? cell.DeathSeconds - 0.05 : first;
			return Math.Min(Math.Max(cell.MatureSeconds, first), last);
		}

		/// <summary>The cell's air, read on first asking and kept while the cell lives unchanged; null without a timeline.</summary>
		public static Air Of(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, in StormCell cell)
		{
			if (timeline == null)
			{
				return null;
			}
			Sweep(timeline);
			double seconds = ReferenceSeconds(cell);
			Vector2 centre = cell.CentreAtSeconds(seconds);
			AirOffsets offsets = timeline.Air.AtSeconds(seconds);
			// Kept while what it was read from stands: an edit that moves the cell, or the air an admin
			// adds, reads it again — on every client alike.
			if (known.TryGetValue(cell.ID, out Air air) && air.Timeline == timeline && air.Settings == settings && air.Seconds == seconds
				&& air.Kind == cell.Kind && air.Centre == centre && Same(air.Offsets, offsets))
			{
				return air;
			}
			float ground = VortexPresenter.GroundAt(centre, out bool water);
			WeatherSample sample = WeatherField.SampleAtSeconds(timeline, settings, scene, new Vector3(centre.x, ground, centre.y), seconds);
			air = new Air
			{
				Seconds = seconds,
				Centre = centre,
				Ground = ground,
				Water = water,
				Sample = sample,
				Frames = new StormFrames(sample),
				Kind = cell.Kind,
				Offsets = offsets,
				Timeline = timeline,
				Settings = settings,
			};
			known[cell.ID] = air;
			return air;
		}

		/// <summary>Forgets the cells the timeline no longer has, once a frame.</summary>
		private static void Sweep(WeatherTimeline timeline)
		{
			if (sweptFrame == Time.frameCount)
			{
				return;
			}
			sweptFrame = Time.frameCount;
			gone.Clear();
			foreach (KeyValuePair<ushort, Air> pair in known)
			{
				if (pair.Value.Timeline != timeline || !timeline.TryGetCell(pair.Key, out _))
				{
					gone.Add(pair.Key);
				}
			}
			foreach (ushort id in gone)
			{
				known.Remove(id);
			}
		}

		private static bool Same(in AirOffsets a, in AirOffsets b) =>
			a.Temperature == b.Temperature && a.Humidity == b.Humidity && a.Pressure == b.Pressure
			&& a.Instability == b.Instability && a.Wind == b.Wind && a.Gravity == b.Gravity;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnPlay()
		{
			known.Clear();
			sweptFrame = -1;
		}
	}
}
