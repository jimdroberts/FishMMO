using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The storms a scene's own weather makes, as a pure function of the world's seed, the scene and the moment: where
	/// each is born, what kind, which way it goes, how long it lives and when the air stops feeding it. The server and
	/// every client work out the same storms for any moment — now, an hour ago, or after an admin sets the clock back —
	/// so none of them is sent, a player who joins knows the storms that already passed (the ground they wetted, the
	/// lightning they threw), and setting the clock back brings back the storms of that time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Slots.</b> World time is cut into <see cref="SlotSeconds"/> slots fixed from the epoch. A slot's hash decides
	/// whether it tries for a storm at all (in proportion to the scene's area), the moment in it, and up to
	/// <see cref="Attempts"/> places; a place keeps the storm in proportion to how ready its air is then
	/// (<see cref="StormPhysics.LikelihoodAt"/>), and the kind, the heading off the prevailing wind, the pace and the
	/// size come from that air and more of the hash. This is the director's rule — it rolled the same dice from a
	/// running generator, so its storms were whatever that generator had reached.
	/// </para>
	/// <para>
	/// <b>The cap without a history.</b> A storm is kept only if fewer than <see cref="MaxCells"/> earlier CANDIDATES
	/// are alive at its birth, counted by their full natural lives. Counting candidates, not kept storms, is what keeps
	/// it pure: whether a storm is kept never depends on whether an earlier one was, so nothing has to be replayed from
	/// the epoch. Kept storms only ever live as long as their candidates or less, so never more than the cap are alive.
	/// </para>
	/// <para>
	/// <b>Dying back.</b> A kept storm's path is walked every <see cref="RetireStepSeconds"/> of its life: the first
	/// moment it has left the scene, or stands in air that cannot feed its kind, it starts to fade there, as the
	/// director retired it. Worked out once per storm, when it is first needed.
	/// </para>
	/// <para>
	/// <b>What it is a function of.</b> The timeline's seed, area, director switch, latitude and the air added to it
	/// (<see cref="WeatherTimeline.Air"/>), the scene's settings, and the world clock — every one of them the same on
	/// the server and on every client. An admin's change to the air is applied to every moment the entry covers, so
	/// it re-decides the storms everywhere alike. Storms an admin or a script starts are not this schedule's: they are
	/// sent, as before, with ids below <see cref="FirstID"/>.
	/// </para>
	/// </remarks>
	public static class StormSchedule
	{
		/// <summary>The length of a slot of world time, seconds: at most one storm is born in each.</summary>
		public const double SlotSeconds = 60.0;
		/// <summary>The first id of a scheduled storm; those below are sent ones (an admin's, a script's).</summary>
		public const ushort FirstID = 0x8000;
		/// <summary>The most scheduled storms alive in a scene at once.</summary>
		public const int MaxCells = 12;
		/// <summary>A scene smaller than this, km², makes no storms of its own: there is no room for one to come and go.</summary>
		public const float MinimumSquareKm = 2f;
		/// <summary>How many storms a km² holds in air that is fully ready for them, at a storm's <see cref="ReferenceLifeSeconds"/>.</summary>
		public const float StormsPerSquareKm = 0.5f;
		/// <summary>The life a slot's chance is reckoned against: half an hour, a middling storm.</summary>
		public const float ReferenceLifeSeconds = 1800f;
		/// <summary>Places a slot tries before it gives up.</summary>
		public const int Attempts = 8;
		/// <summary>How often along its life a storm's air is asked whether it still feeds it, world seconds.</summary>
		public const double RetireStepSeconds = 120.0;
		/// <summary>
		/// The longest a storm lives (<see cref="StormPhysics.Dimensions"/>: a haboob, three and a half hours at most, a
		/// quarter over). Longer lives are cut to it, because the cap looks back this far for the storms still alive.
		/// </summary>
		public const double LongestLifeSeconds = 15750.0;

		/// <summary>Whether a cell is one of the schedule's (never sent) rather than one an admin or a script started.</summary>
		public static bool IsScheduled(ushort id) => id >= FirstID;

		/// <summary>Whether this scene makes storms of its own: its director is on, its weather is its own, and it is big enough.</summary>
		public static bool Runs(WeatherTimeline timeline)
		{
			return timeline != null && timeline.Director && timeline.SceneMode == WeatherSceneMode.Own
				&& timeline.Area.width > 0f && timeline.Area.height > 0f
				&& timeline.Area.width * timeline.Area.height / 1_000_000f >= MinimumSquareKm;
		}

		/// <summary>
		/// The scheduled storms whose lives overlap [<paramref name="from"/>, <paramref name="to"/>] world seconds,
		/// added to <paramref name="into"/> (which is not cleared).
		/// </summary>
		public static void CellsBetween(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, double from, double to, List<StormCell> into)
		{
			if (!Runs(timeline) || !(to >= from))
			{
				return;
			}
			Cache cache = CacheFor(timeline, settings, scene);
			long first = (long)Math.Floor((from - LongestLifeSeconds) / SlotSeconds) - 1;
			long last = (long)Math.Floor(to / SlotSeconds);
			for (long k = first; k <= last; k++)
			{
				if (!TryKept(cache, timeline, settings, scene, k, out StormCell cell))
				{
					continue;
				}
				if (cell.BirthSeconds <= to && cell.DeathSeconds > from)
				{
					into.Add(cell);
				}
			}
		}

		/// <summary>
		/// Brings the scheduled storms in <see cref="WeatherTimeline.Cells"/> to those alive about
		/// <paramref name="worldSeconds"/>, so everything that reads the timeline's cells — the field, the sky, the
		/// lightning — has them. Sent cells are left alone. True when the list changed; <paramref name="born"/> gets the
		/// ones that were not there before.
		/// </summary>
		public static bool Present(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, double worldSeconds, List<StormCell> born = null)
		{
			if (timeline == null)
			{
				return false;
			}
			List<StormCell> alive = presentScratch;
			alive.Clear();
			// A slot either side: a storm a moment from birth is already in the list when it starts, on every machine.
			CellsBetween(timeline, settings, scene, worldSeconds - SlotSeconds, worldSeconds + SlotSeconds, alive);
			bool changed = false;
			for (int i = timeline.Cells.Count - 1; i >= 0; i--)
			{
				StormCell cell = timeline.Cells[i];
				if (!IsScheduled(cell.ID))
				{
					continue;
				}
				int at = IndexOf(alive, cell.ID);
				if (at < 0 || !Same(alive[at], cell))
				{
					timeline.Cells.RemoveAt(i);
					changed = true;
				}
			}
			for (int i = 0; i < alive.Count; i++)
			{
				if (!timeline.TryGetCell(alive[i].ID, out _))
				{
					timeline.Cells.Add(alive[i]);
					born?.Add(alive[i]);
					changed = true;
				}
			}
			return changed;
		}

		/// <summary>
		/// A new storm's life in world time, from its birth: it grows, holds and fades with the world's clock, held or
		/// raced. The schedule's storms and the ones an admin starts are made by this one rule.
		/// </summary>
		public static StormCell NewCell(ushort id, uint seed, StormKind kind, Vector2 at, float radius, float extent, Vector2 velocity,
			float lifetimeSeconds, double birthSeconds)
		{
			float matureSeconds = Mathf.Clamp(lifetimeSeconds * 0.15f, 20f, 180f);
			float decaySeconds = Mathf.Clamp(lifetimeSeconds * 0.2f, 20f, 240f);
			float holdSeconds = Mathf.Max(0f, lifetimeSeconds - matureSeconds - decaySeconds);
			StormCellShape shape = StormPhysics.ShapeOf(kind);
			var cell = new StormCell
			{
				ID = id,
				Kind = kind,
				Seed = seed,
				OriginX = at.x,
				OriginZ = at.y,
				VelocityX = velocity.x,
				VelocityZ = velocity.y,
				RadiusMeters = Mathf.Max(10f, radius),
				/* The shape comes from the KIND, not from whoever asked for the cell. A squall line is a line because
				 * of what it is, so an admin command, an ECA action and the schedule all make the same shape for the
				 * same kind of storm. */
				Shape = shape,
				ExtentMeters = Mathf.Max(0f, extent),
				PeakIntensity = 1f,
				// A wall wanders less than a shower: it is held in shape by the air pushing it.
				MeanderMeters = Mathf.Max(10f, radius) * (shape == StormCellShape.Front ? 0.04f : 0.15f),
				MotionSeconds = birthSeconds,
				BirthSeconds = birthSeconds,
				MatureSeconds = birthSeconds + matureSeconds,
			};
			cell.DecaySeconds = cell.MatureSeconds + holdSeconds;
			cell.DeathSeconds = cell.DecaySeconds + decaySeconds;
			return cell;
		}

		/// <summary>
		/// Starts a storm's fade at <paramref name="startSeconds"/>, over <paramref name="fadeSeconds"/>, keeping its
		/// envelope continuous (a still-growing storm fades from where it is). False if it was already fading by then.
		/// </summary>
		public static bool Retire(ref StormCell cell, double startSeconds, float fadeSeconds)
		{
			if (cell.DecaySeconds <= startSeconds)
			{
				return false;
			}
			if (cell.MatureSeconds > startSeconds)
			{
				cell.MatureSeconds = startSeconds;
			}
			cell.DecaySeconds = startSeconds;
			cell.DeathSeconds = startSeconds + Mathf.Max(1f, fadeSeconds);
			return true;
		}

		// ── Inside ─────────────────────────────────────────────────────

		/// <summary>One slot's storm: whether it found air for one, its life as born, and once worked out, whether it is kept and its real life.</summary>
		private sealed class Candidate
		{
			public bool Exists;
			public StormCell Cell;
			/// <summary>When it dies if nothing cuts it short: what the cap counts by. Its walk only ever shortens its life, so the cap stays a cap.</summary>
			public double NaturalDeath;
			public bool Decided;
			public bool Kept;
			public bool Walked;
		}

		/// <summary>A timeline's slots, worked out, with what they were worked out from.</summary>
		private sealed class Cache
		{
			public WorldSceneSettings Settings;
			public uint Seed;
			public Rect Area;
			public bool Director;
			public WeatherSceneMode Mode;
			public float Latitude, Longitude;
			public WorldBody Body;
			public AirOffsetEntry Air;
			public readonly Dictionary<long, Candidate> Slots = new Dictionary<long, Candidate>();
		}

		private static readonly ConditionalWeakTable<WeatherTimeline, Cache> caches = new ConditionalWeakTable<WeatherTimeline, Cache>();
		private static readonly List<StormCell> presentScratch = new List<StormCell>();

		/// <summary>Slots are forgotten past this many; the oldest go first.</summary>
		private const int MaxSlots = 8192;

		private static Cache CacheFor(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene)
		{
			Cache cache = caches.GetValue(timeline, _ => new Cache());
			if (cache.Settings != settings || cache.Seed != timeline.Seed || cache.Area != timeline.Area || cache.Director != timeline.Director
				|| cache.Mode != timeline.SceneMode || cache.Latitude != timeline.LatitudeDegrees || cache.Longitude != timeline.LongitudeDegrees
				|| cache.Body != timeline.BodyOverride || !SameAir(cache.Air, timeline.Air))
			{
				cache.Slots.Clear();
				cache.Settings = settings;
				cache.Seed = timeline.Seed;
				cache.Area = timeline.Area;
				cache.Director = timeline.Director;
				cache.Mode = timeline.SceneMode;
				cache.Latitude = timeline.LatitudeDegrees;
				cache.Longitude = timeline.LongitudeDegrees;
				cache.Body = timeline.BodyOverride;
				cache.Air = timeline.Air;
			}
			else if (cache.Slots.Count > MaxSlots)
			{
				cache.Slots.Clear();
			}
			return cache;
		}

		/// <summary>Whether slot <paramref name="k"/>'s storm is kept, and its real life if it is.</summary>
		private static bool TryKept(Cache cache, WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, long k, out StormCell cell)
		{
			Candidate candidate = Get(cache, timeline, settings, scene, k);
			cell = candidate.Cell;
			if (!candidate.Exists)
			{
				return false;
			}
			if (!candidate.Decided)
			{
				candidate.Kept = OlderAlive(cache, timeline, settings, scene, k, candidate.Cell.BirthSeconds) < MaxCells;
				candidate.Decided = true;
			}
			if (!candidate.Kept)
			{
				return false;
			}
			if (!candidate.Walked)
			{
				Walk(timeline, settings, scene, ref candidate.Cell);
				candidate.Walked = true;
			}
			cell = candidate.Cell;
			return true;
		}

		/// <summary>How many earlier slots' candidates are alive at <paramref name="birth"/>, by their natural lives.</summary>
		private static int OlderAlive(Cache cache, WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, long k, double birth)
		{
			int alive = 0;
			long first = k - (long)Math.Ceiling(LongestLifeSeconds / SlotSeconds) - 1;
			for (long j = first; j < k; j++)
			{
				Candidate older = Get(cache, timeline, settings, scene, j);
				if (older.Exists && older.Cell.BirthSeconds <= birth && birth < older.NaturalDeath)
				{
					alive++;
				}
			}
			return alive;
		}

		private static Candidate Get(Cache cache, WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, long k)
		{
			if (cache.Slots.TryGetValue(k, out Candidate candidate))
			{
				return candidate;
			}
			candidate = new Candidate();
			cache.Slots[k] = candidate;
			Rect area = timeline.Area;
			float squareKm = area.width * area.height / 1_000_000f;
			uint h = Hash(Hash(timeline.Seed, 0x5707A11u), unchecked((uint)k ^ (uint)(k >> 32)));
			float chance = Mathf.Clamp01(squareKm * StormsPerSquareKm * (float)SlotSeconds / ReferenceLifeSeconds);
			if (Unit(h) >= chance)
			{
				return candidate;
			}
			double birth = (k + (double)Unit(Hash(h, 1))) * SlotSeconds;
			for (uint attempt = 0; attempt < Attempts; attempt++)
			{
				var p = new Vector3(area.xMin + Unit(Hash(h, 10 + 2 * attempt)) * area.width, 0f, area.yMin + Unit(Hash(h, 11 + 2 * attempt)) * area.height);
				WeatherSample sample = WeatherField.OpenAtSeconds(timeline, settings, scene, p, birth);
				StormPhysics.Likelihood likely = StormPhysics.LikelihoodAt(sample.OpenAir, sample.OpenColumn, sample.Ground, timeline.LatitudeDegrees);
				if (Unit(Hash(h, 30 + attempt)) >= Mathf.Clamp01(likely.Total))
				{
					continue;
				}
				StormKind kind = likely.Pick(Unit(Hash(h, 40)));
				PrevailingWind(timeline, settings, scene, area, birth, h, out float windHeading, out float windSpeed);
				float heading = (windHeading + Mathf.Lerp(-35f, 35f, Unit(Hash(h, 41)))) * Mathf.Deg2Rad;
				/* Carried by the wind that is actually blowing. A storm moves with the air it is in, so a stiff day drives
				 * weather across a scene in minutes and a still one leaves it hanging about. Kept below the wind itself: a
				 * storm lags its steering flow. A vent does not travel: the eruption stays put and its plume is what the
				 * wind carries. */
				float carried = Mathf.Clamp(windSpeed * 0.55f, 1.5f, 14f);
				float speed = kind == StormKind.Eruption ? 0f : carried * Mathf.Lerp(0.75f, 1.25f, Unit(Hash(h, 42)));
				StormPhysics.Dimensions(kind, sample.OpenColumn, Unit(Hash(h, 43)), Unit(Hash(h, 44)), out float radius, out float extent, out float lifetime);
				lifetime = Mathf.Min(lifetime, (float)LongestLifeSeconds);
				ushort id = unchecked((ushort)(FirstID | (k & 0x7FFF)));
				candidate.Cell = NewCell(id, Hash(h, 45), kind, new Vector2(p.x, p.z), radius, extent,
					new Vector2(Mathf.Sin(heading), Mathf.Cos(heading)) * speed, lifetime, birth);
				candidate.NaturalDeath = candidate.Cell.DeathSeconds;
				candidate.Exists = true;
				break;
			}
			return candidate;
		}

		/// <summary>
		/// The prevailing wind over the scene's middle at a moment, as a heading (degrees) and a speed: the open air's,
		/// never the air with storms in it. Air too still to have a direction gives the slot's own heading and a light air.
		/// </summary>
		private static void PrevailingWind(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, Rect area, double seconds, uint h,
			out float headingDegrees, out float speed)
		{
			var centre = new Vector3(area.center.x, 0f, area.center.y);
			Vector2 wind = WeatherField.OpenAtSeconds(timeline, settings, scene, centre, seconds).OpenAir.Wind;
			if (wind.sqrMagnitude < 1e-4f)
			{
				headingDegrees = Unit(Hash(h, 46)) * 360f;
				speed = 4f;
				return;
			}
			headingDegrees = Mathf.Repeat(Mathf.Atan2(wind.x, wind.y) * Mathf.Rad2Deg, 360f);
			speed = wind.magnitude;
		}

		/// <summary>
		/// Walks a kept storm's life: the first moment it has drifted out of the scene (by its reach, so a front with
		/// most of its wall still over the scene stays), or stands in air that cannot feed its kind, it fades there.
		/// </summary>
		private static void Walk(WeatherTimeline timeline, WorldSceneSettings settings, Scene scene, ref StormCell cell)
		{
			Rect area = timeline.Area;
			float reach = cell.ReachMeters;
			var grown = new Rect(area.xMin - reach, area.yMin - reach, area.width + reach * 2f, area.height + reach * 2f);
			for (double s = cell.BirthSeconds + RetireStepSeconds; s < cell.DecaySeconds; s += RetireStepSeconds)
			{
				Vector2 centre = cell.CentreAtSeconds(s);
				if (!grown.Contains(centre))
				{
					Retire(ref cell, s, 30f);
					return;
				}
				WeatherSample there = WeatherField.OpenAtSeconds(timeline, settings, scene, new Vector3(centre.x, 0f, centre.y), s);
				StormPhysics.Likelihood likely = StormPhysics.LikelihoodAt(there.OpenAir, there.OpenColumn, there.Ground, timeline.LatitudeDegrees);
				if (likely.Of(cell.Kind) < 0.01f)
				{
					Retire(ref cell, s, 120f);
					return;
				}
			}
		}

		private static int IndexOf(List<StormCell> cells, ushort id)
		{
			for (int i = 0; i < cells.Count; i++)
			{
				if (cells[i].ID == id)
				{
					return i;
				}
			}
			return -1;
		}

		private static bool Same(in StormCell a, in StormCell b)
		{
			return a.ID == b.ID && a.Seed == b.Seed && a.Kind == b.Kind && a.BirthSeconds == b.BirthSeconds && a.DeathSeconds == b.DeathSeconds
				&& a.DecaySeconds == b.DecaySeconds && a.OriginX == b.OriginX && a.OriginZ == b.OriginZ;
		}

		private static bool SameAir(in AirOffsetEntry a, in AirOffsetEntry b)
		{
			return a.StartSeconds == b.StartSeconds && a.EndSeconds == b.EndSeconds && SameOffsets(a.From, b.From) && SameOffsets(a.To, b.To);
		}

		private static bool SameOffsets(in AirOffsets a, in AirOffsets b)
		{
			return a.Temperature == b.Temperature && a.Humidity == b.Humidity && a.Pressure == b.Pressure && a.Instability == b.Instability
				&& a.Wind == b.Wind && a.Gravity == b.Gravity;
		}

		/// <summary>A well-mixed hash of two words: the schedule's only source of chance.</summary>
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

		/// <summary>A hash as a number in [0, 1).</summary>
		public static float Unit(uint h) => (h & 0xFFFFFF) / (float)0x1000000;
	}
}
