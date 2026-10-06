using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>What the placement asks of the world: the sea floor, and what kind of sea is over it.</summary>
	public interface ISeaGround
	{
		/// <summary>
		/// The sea floor's height at a world (x, z); false where nothing is loaded there yet. Positive infinity
		/// (and true) where there is no ground at all, past the scene's edge: no water there.
		/// </summary>
		bool TryGround(float x, float z, out float y);

		/// <summary>The biome (asset name, or null) and the climate's temperature (-1 .. 1) at a world position.</summary>
		void Habitat(Vector3 position, out string biome, out float temperature);
	}

	/// <summary>One group of sea creatures: a school, a reef fish's patch, a cruising shark, a crab colony.</summary>
	public struct SeaGroup
	{
		/// <summary>The kind's index in the settings.</summary>
		public int Kind;
		/// <summary>Everything else about the group and its members is drawn from this.</summary>
		public uint Seed;
		/// <summary>The middle of its loop: x, z, and the depth it swims at (y, world).</summary>
		public Vector3 Home;
		/// <summary>The loop's radius, metres; the second radius is <see cref="Radius"/> × <see cref="Aspect"/>.</summary>
		public float Radius;
		public float Aspect;
		/// <summary>The loop's turn on the map, radians, and its sense (+1 anticlockwise, -1 clockwise).</summary>
		public float Yaw;
		public float Sense;
		/// <summary>Seconds a lap takes.</summary>
		public double Period;
		/// <summary>Members.</summary>
		public int Count;
		/// <summary>The kind's length range, sampled per member.</summary>
		public float Length;
	}

	/// <summary>One animal drawn this frame.</summary>
	public struct SeaInstance
	{
		public Matrix4x4 Matrix;
		/// <summary>FishSeaLife.hlsl's <c>_Anim</c>: phase, strength, turn.</summary>
		public Vector4 Anim;
		/// <summary>FishSeaLife.hlsl's <c>_Tint</c>: colour, and how much is drawn.</summary>
		public Vector4 Tint;
	}

	/// <summary>
	/// Where the sea's background creatures live and where each one is at a moment: pure functions of the
	/// scene, its ground, a cell of the placement grid and the shared world clock, so every player's sea
	/// holds the same schools in the same places doing the same thing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Placement is per cell.</b> The scene is cut into a grid (<see cref="SeaLifeSettings.CellMetres"/>);
	/// each cell decides its own groups from its coordinates, the kind and the scene's name, hashed, and from
	/// the ground and the climate under them. A cell is the same whenever and from wherever it is first seen,
	/// so streaming them in as the camera moves changes nothing. A cell whose ground is not loaded yet is not
	/// decided (<see cref="TryPlace"/> is false) until it is.
	/// </para>
	/// <para>
	/// <b>Motion is a loop.</b> Every group swims a closed path about its home — an ellipse with a little of
	/// its second and third harmonics, so it is not a racetrack — at its kind's speed, so where it is is a
	/// function of the clock folded by its lap time, worked out in double precision (world seconds are large).
	/// Every point of the loop was checked for water when the group was placed, so no school swims through a
	/// sandbank; on the way the group keeps its clearance over the floor and stays under the low-tide surface.
	/// </para>
	/// <para>
	/// <b>Members</b> sit at fixed offsets in their group's frame (a school's ellipsoid, a reef patch), each
	/// with its own slow wobble and stroke phase, all from the group's seed.
	/// </para>
	/// </remarks>
	public static class SeaLifePlacement
	{
		/// <summary>
		/// How far below its mean surface the sea falls at the lowest tide, metres (WaterEnvironment's
		/// DefaultMaximumTideMetres; FishMMO.Water is not a reference of the client assembly). The plants keep
		/// under it (they cannot move); the animals keep under the surface as it is at the moment instead.
		/// </summary>
		public const float LowTideMetres = 2.5f;

		/// <summary>Metres an animal keeps under the surface it is given (<see cref="Evaluate"/>).</summary>
		public const float SurfaceClearance = 0.3f;

		/// <summary>Points round a loop checked for water when a group is placed.</summary>
		public const int LoopChecks = 12;

		// ── Hashing ───────────────────────────────────────────────────

		/// <summary>lowbias32: a well-mixed 32-bit hash.</summary>
		public static uint Mix(uint x)
		{
			unchecked
			{
				x ^= x >> 16;
				x *= 0x7feb352du;
				x ^= x >> 15;
				x *= 0x846ca68bu;
				x ^= x >> 16;
				return x;
			}
		}

		public static uint Hash(uint seed, int a, int b, int c)
		{
			unchecked
			{
				return Mix(seed ^ Mix((uint)a * 0x9E3779B1u ^ Mix((uint)b * 0x85EBCA6Bu ^ Mix((uint)c * 0xC2B2AE35u + 0x27D4EB2Fu))));
			}
		}

		/// <summary>0 .. 1 from a hash.</summary>
		public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

		/// <summary>A stable seed for a scene from its name (FNV-1a: the same on every machine, unlike GetHashCode).</summary>
		public static uint SceneSeed(string sceneName)
		{
			unchecked
			{
				uint h = 2166136261u;
				if (sceneName != null)
				{
					foreach (char ch in sceneName)
					{
						h = (h ^ ch) * 16777619u;
					}
				}
				return Mix(h);
			}
		}

		// ── Placement ─────────────────────────────────────────────────

		/// <summary>
		/// A kind's groups in one cell of the placement grid, appended to <paramref name="groups"/>; false when
		/// the ground under the cell is not loaded yet (nothing is appended, and the cell should be asked again).
		/// </summary>
		/// <param name="meanLevel">The sea's mean surface, world y (tide left out, so placement never depends on the hour).</param>
		public static bool TryPlace(SeaCreatureKind kind, int kindIndex, uint sceneSeed, int cellX, int cellZ, float cellMetres, float meanLevel,
			ISeaGround ground, List<SeaGroup> groups)
		{
			if (kind == null || !kind.Enabled || kind.GroupsPerKm2 <= 0f)
			{
				return true;
			}
			float area = cellMetres * cellMetres * 1e-6f;
			float most = kind.GroupsPerKm2 * area * Mathf.Max(1f, kind.Prefers != null && kind.Prefers.Length > 0 ? kind.PreferWeight : 1f);
			int candidates = Mathf.CeilToInt(most);
			for (int i = 0; i < candidates; i++)
			{
				uint seed = Hash(sceneSeed, cellX * 4099 + kindIndex, cellZ, i);
				var home = new Vector3((cellX + Unit(Mix(seed ^ 0x1u))) * cellMetres, 0f, (cellZ + Unit(Mix(seed ^ 0x2u))) * cellMetres);
				if (!ground.TryGround(home.x, home.z, out float floor))
				{
					return false;
				}
				float water = meanLevel - floor;
				if (water < kind.MinWater)
				{
					continue;
				}
				ground.Habitat(new Vector3(home.x, floor, home.z), out string biome, out float temperature);
				float weight = HabitatWeight(kind, biome, temperature);
				// Candidate i lives here with chance (expected count - i), so the count is the expectation on average.
				float expected = kind.GroupsPerKm2 * area * weight;
				if (Unit(Mix(seed ^ 0x3u)) >= Mathf.Clamp01(expected - i))
				{
					continue;
				}
				if (!TryShape(kind, kindIndex, seed, home, floor, meanLevel, ground, out SeaGroup group, out bool loaded))
				{
					if (!loaded)
					{
						return false;
					}
					continue;
				}
				groups.Add(group);
			}
			return true;
		}

		/// <summary>How much at home a kind is at a place: 0 (not at all) .. its preferred weight.</summary>
		public static float HabitatWeight(SeaCreatureKind kind, string biome, float temperature)
		{
			// Soft edges on the temperature band, a tenth wide; an end at the scale's end is open.
			float warm = kind.Temperature.x <= -1f ? 1f : Mathf.Clamp01((temperature - kind.Temperature.x) / 0.1f + 0.5f);
			float cool = kind.Temperature.y >= 1f ? 1f : Mathf.Clamp01((kind.Temperature.y - temperature) / 0.1f + 0.5f);
			float climate = Mathf.Min(warm, cool);
			bool preferred = false;
			if (biome != null && kind.Prefers != null)
			{
				foreach (string name in kind.Prefers)
				{
					if (string.Equals(name, biome, StringComparison.OrdinalIgnoreCase))
					{
						preferred = true;
						break;
					}
				}
			}
			float place = preferred ? Mathf.Max(1f, kind.PreferWeight) : kind.ElsewhereWeight;
			// A preferred biome is home whatever the thermometer says (a reef is warm by being a reef).
			return preferred ? place : place * climate;
		}

		/// <summary>
		/// The group's loop and depth, checked for water all the way round; false when there is no room for it
		/// (<paramref name="loaded"/> true) or the ground on its loop is not loaded yet (false).
		/// </summary>
		private static bool TryShape(SeaCreatureKind kind, int kindIndex, uint seed, Vector3 home, float floor, float meanLevel, ISeaGround ground,
			out SeaGroup group, out bool loaded)
		{
			loaded = true;
			group = default;
			float water = meanLevel - floor;
			bool onFloor = kind.Behaviour == SeaCreatureBehaviour.Hover || kind.Behaviour == SeaCreatureBehaviour.Crawl;
			if (onFloor && (water < kind.Depth.x || water > kind.Depth.y))
			{
				return false;
			}
			// Its band's shallow end, below the mean surface: the tide moves the surface over it, and an animal the
			// low water leaves no room for is simply not drawn until it rises (Evaluate).
			float ceiling = meanLevel - Mathf.Max(0.5f, kind.Depth.x);
			float y;
			if (onFloor)
			{
				y = floor;
			}
			else
			{
				// A depth in its band, kept off the floor and under the low-tide surface.
				float depth = Mathf.Lerp(kind.Depth.x, kind.Depth.y, Unit(Mix(seed ^ 0x4u)));
				y = Mathf.Min(meanLevel - depth, ceiling);
				y = Mathf.Max(y, floor + kind.FloorClearance.y);
				if (y > ceiling)
				{
					return false; // too shallow for it to swim here at all
				}
			}

			float radius = Mathf.Lerp(kind.Roam.x, kind.Roam.y, Unit(Mix(seed ^ 0x5u)));
			group = new SeaGroup
			{
				Kind = kindIndex,
				Seed = seed,
				Home = new Vector3(home.x, y, home.z),
				Radius = radius,
				Aspect = Mathf.Lerp(0.45f, 0.9f, Unit(Mix(seed ^ 0x6u))),
				Yaw = Unit(Mix(seed ^ 0x7u)) * Mathf.PI * 2f,
				Sense = Unit(Mix(seed ^ 0x8u)) < 0.5f ? -1f : 1f,
				Count = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(kind.GroupSize.x, kind.GroupSize.y + 0.999f, Unit(Mix(seed ^ 0x9u))) - 0.5f)),
				Length = Mathf.Lerp(kind.Length.x, kind.Length.y, Unit(Mix(seed ^ 0xAu))),
			};
			// Round the loop, with the group's own spread: twice, the second time half the size, then give up.
			for (int attempt = 0; attempt < 2; attempt++)
			{
				group.Period = LapSeconds(group, kind);
				bool fits = true;
				for (int k = 0; k < LoopChecks && fits; k++)
				{
					Vector3 at = LoopPoint(group, k / (float)LoopChecks);
					if (!ground.TryGround(at.x, at.z, out float there))
					{
						loaded = false;
						return false;
					}
					float deep = meanLevel - there;
					fits = deep >= kind.MinWater && (!onFloor || deep <= kind.Depth.y * 1.25f);
				}
				if (fits)
				{
					return true;
				}
				group.Radius *= 0.5f;
			}
			return false;
		}

		/// <summary>Seconds a lap of the group's loop takes at its kind's speed.</summary>
		public static double LapSeconds(in SeaGroup group, SeaCreatureKind kind)
		{
			// Ramanujan's ellipse perimeter, near enough for a loop with a little of its harmonics.
			double a = group.Radius, b = group.Radius * group.Aspect;
			double h = (a - b) * (a - b) / Math.Max(1e-6, (a + b) * (a + b));
			double perimeter = Math.PI * (a + b) * (1.0 + 3.0 * h / (10.0 + Math.Sqrt(4.0 - 3.0 * h))) * 1.1;
			return Math.Max(4.0, perimeter / Math.Max(0.01, kind.Speed));
		}

		/// <summary>The loop's point at a share of the lap (0 .. 1), before depth: x, z, and the home's y.</summary>
		public static Vector3 LoopPoint(in SeaGroup group, float lap)
		{
			float theta = lap * Mathf.PI * 2f * group.Sense;
			float phase = Unit(Mix(group.Seed ^ 0xBu)) * Mathf.PI * 2f;
			float a = group.Radius, b = group.Radius * group.Aspect;
			float x = a * Mathf.Cos(theta) + 0.18f * a * Mathf.Cos(2f * theta + phase);
			float z = b * Mathf.Sin(theta) + 0.12f * a * Mathf.Sin(3f * theta + phase * 1.7f);
			float c = Mathf.Cos(group.Yaw), s = Mathf.Sin(group.Yaw);
			return new Vector3(group.Home.x + c * x - s * z, group.Home.y, group.Home.z + s * x + c * z);
		}

		/// <summary>The share of its lap a group is at, at a world time: the clock folded by the lap time, in double precision.</summary>
		public static float Lap(in SeaGroup group, double seconds)
		{
			double laps = seconds / group.Period + Unit(Mix(group.Seed ^ 0xCu));
			return (float)(laps - Math.Floor(laps));
		}

		/// <summary>A periodic phase in cycles (0 .. 1) at a world time, for a period in seconds and an offset.</summary>
		public static float Cycle(double seconds, double period, float offset)
		{
			double c = seconds / Math.Max(1e-3, period) + offset;
			return (float)(c - Math.Floor(c));
		}

		// ── Evaluation ────────────────────────────────────────────────

		/// <summary>
		/// Every member of a group at a world time, appended to <paramref name="into"/>. Ground is read for
		/// floor clearance (the centre's, or each member's for kinds that live on the bed).
		/// </summary>
		/// <param name="visible">How much of the group the distance leaves drawn (FishSeaLife's dither), 0 .. 1.</param>
		/// <param name="surface">
		/// The sea's surface now, world y, less what its waves' troughs need (SeaLifeSystem): the tide is on the
		/// shared clock, so this is the same for every player at the same moment.
		/// </param>
		public static void Evaluate(in SeaGroup group, SeaCreatureKind kind, double seconds, float surface, ISeaGround ground, float visible,
			List<SeaInstance> into)
		{
			float ceiling = surface - SurfaceClearance;
			float lap = Lap(group, seconds);
			float step = (float)Math.Min(0.02, 1.0 / Math.Max(1.0, group.Period));
			Vector3 centre = LoopPoint(group, lap);
			Vector3 ahead = LoopPoint(group, lap + step);
			Vector3 further = LoopPoint(group, lap + 2f * step);
			Vector3 heading = ahead - centre;
			heading.y = 0f;
			if (heading.sqrMagnitude < 1e-8f)
			{
				heading = new Vector3(Mathf.Cos(group.Yaw), 0f, Mathf.Sin(group.Yaw));
			}
			heading.Normalize();
			Vector3 next = further - ahead;
			next.y = 0f;
			// How hard it is turning: the heading's change over the step, signed, as -1 .. 1.
			float turn = next.sqrMagnitude > 1e-8f ? Mathf.Clamp(Vector3.SignedAngle(heading, next.normalized, Vector3.up) / 12f, -1f, 1f) : 0f;

			// Rises and dips a little round the lap, kept off the floor and under the surface.
			float bob = Mathf.Sin(lap * Mathf.PI * 4f + Unit(Mix(group.Seed ^ 0xDu)) * 6.28f) * Mathf.Min(2f, group.Radius * 0.08f);
			centre.y += bob;
			if (kind.Behaviour != SeaCreatureBehaviour.Hover && kind.Behaviour != SeaCreatureBehaviour.Crawl && ground.TryGround(centre.x, centre.z, out float floor)
				&& !float.IsInfinity(floor))
			{
				centre.y = Mathf.Max(centre.y, floor + kind.FloorClearance.x + kind.Spread * 0.3f);
			}
			centre.y = Mathf.Min(centre.y, ceiling - kind.Spread * 0.3f);

			var side = new Vector3(heading.z, 0f, -heading.x);
			bool palette = kind.Palette != null && kind.Palette.Length > 0;
			for (int m = 0; m < group.Count; m++)
			{
				uint own = Mix(group.Seed ^ ((uint)m * 0x9E3779B9u) ^ 0x5EA11FEu);
				float r0 = Unit(own), r1 = Unit(Mix(own ^ 0x1u)), r2 = Unit(Mix(own ^ 0x2u)), r3 = Unit(Mix(own ^ 0x3u));
				float length = group.Length * Mathf.Lerp(0.85f, 1.15f, r3);
				Vector3 position;
				Vector3 forward = heading;
				Vector3 up = Vector3.up;
				float strength = 1f;
				float memberTurn = turn;

				switch (kind.Behaviour)
				{
					case SeaCreatureBehaviour.Hover:
					case SeaCreatureBehaviour.Crawl:
					{
						// Each its own small orbit about a spot in the patch, on (or just over) the bed.
						bool crawl = kind.Behaviour == SeaCreatureBehaviour.Crawl;
						float angle = r0 * Mathf.PI * 2f;
						float distance = Mathf.Sqrt(r1) * kind.Spread;
						Vector3 spot = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
						float orbit = (crawl ? 0.4f : 0.6f) + r2 * (crawl ? 0.8f : 1.6f);
						double period = 2.0 * Math.PI * orbit / Math.Max(0.02, kind.Speed * Mathf.Lerp(0.6f, 1.3f, r3));
						float c = Cycle(seconds, period, r0) * Mathf.PI * 2f * (r1 < 0.5f ? 1f : -1f);
						// A reef fish's orbit is lumpy: it pauses and darts.
						float wobble = crawl ? 0f : 0.35f * Mathf.Sin(c * 3f + r2 * 6.28f);
						position = spot + new Vector3(Mathf.Cos(c), 0f, Mathf.Sin(c)) * (orbit * (1f + wobble));
						var tangent = new Vector3(-Mathf.Sin(c), 0f, Mathf.Cos(c)) * (r1 < 0.5f ? 1f : -1f);
						forward = tangent.normalized;
						if (crawl)
						{
							// Crabs walk sideways.
							forward = new Vector3(tangent.z, 0f, -tangent.x).normalized;
						}
						float bed = ground.TryGround(position.x, position.z, out float under) && !float.IsInfinity(under) ? under : centre.y;
						position.y = crawl ? bed : bed + Mathf.Lerp(kind.FloorClearance.x, kind.FloorClearance.y, r2) + 0.15f * Mathf.Sin(c * 2f);
						if (position.y > ceiling - (crawl ? 0.05f : length * 0.5f))
						{
							continue; // its spot is too shallow for it at low tide: not drawn rather than out of the water
						}
						memberTurn = (r1 < 0.5f ? 1f : -1f) * 0.6f;
						break;
					}
					case SeaCreatureBehaviour.Drift:
					{
						// Hanging in the water about the group's slow drift, each bobbing to its own pulse.
						float angle = r0 * Mathf.PI * 2f;
						float distance = Mathf.Sqrt(r1) * kind.Spread;
						float pulse = Cycle(seconds, 1.0 / Math.Max(0.05, kind.StrokeHz * Mathf.Lerp(0.8f, 1.2f, r2)), r3);
						float rise = 0.12f * length * Mathf.Sin(pulse * Mathf.PI * 2f - 1f);
						float drift = Cycle(seconds, 40.0 + 40.0 * r2, r0) * Mathf.PI * 2f;
						position = centre + new Vector3(Mathf.Cos(angle) * distance + 0.6f * Mathf.Cos(drift), (r2 - 0.5f) * kind.Spread * 0.6f + rise,
							Mathf.Sin(angle) * distance + 0.6f * Mathf.Sin(drift));
						// Bell up, tilted a little toward where it is drifting.
						up = (Vector3.up + new Vector3(Mathf.Cos(drift), 0f, Mathf.Sin(drift)) * 0.25f).normalized;
						forward = Vector3.Cross(up, new Vector3(Mathf.Sin(drift), 0f, -Mathf.Cos(drift))).normalized;
						float top = ceiling - length;
						if (ground.TryGround(position.x, position.z, out float bed) && !float.IsInfinity(bed))
						{
							if (bed + 0.3f + length > top)
							{
								continue;
							}
							position.y = Mathf.Max(position.y, bed + Mathf.Min(kind.FloorClearance.x, top - bed - length));
						}
						position.y = Mathf.Min(position.y, top);
						memberTurn = 0f;
						break;
					}
					default:
					{
						// A school: members at fixed places in the school's frame, longer than it is wide,
						// flatter than both, each wobbling slowly about its place; a cruiser: one or two
						// abreast.
						bool school = kind.Behaviour == SeaCreatureBehaviour.School;
						float u = r0 * 2f - 1f, v = r1 * 2f - 1f, w = r2 * 2f - 1f;
						var place = new Vector3(u, v * 0.45f, w * 1.6f) * (school ? kind.Spread : 0f);
						if (!school && m > 0)
						{
							place = new Vector3((m % 2 == 0 ? -1f : 1f) * kind.Spread * (0.5f + 0.5f * r0), (r1 - 0.5f) * kind.Spread * 0.3f, -kind.Spread * r2);
						}
						double wobblePeriod = 5.0 + 6.0 * r3;
						float a1 = Cycle(seconds, wobblePeriod, r0) * Mathf.PI * 2f;
						float a2 = Cycle(seconds, wobblePeriod * 1.37, r1) * Mathf.PI * 2f;
						Vector3 wobble = new Vector3(Mathf.Sin(a1), 0.5f * Mathf.Sin(a2), Mathf.Cos(a2)) * (school ? kind.Spread * 0.15f : length * 0.3f);
						Vector3 offset = side * (place.x + wobble.x) + Vector3.up * (place.y + wobble.y) + heading * (place.z + wobble.z);
						position = centre + offset;
						// Each fish a little off the school's heading, swinging as it wobbles.
						float yaw = 0.18f * Mathf.Cos(a1);
						forward = (heading * Mathf.Cos(yaw) + side * Mathf.Sin(yaw)).normalized;
						forward.y = 0.12f * Mathf.Cos(a2);
						forward.Normalize();
						strength = 1f + 0.25f * Mathf.Sin(a1 * 2f);
						// Between its own patch of floor and the low-tide surface; where the water is too thin
						// for it, not drawn rather than in the sand or out of the sea.
						float roof = ceiling - length * 0.3f;
						if (ground.TryGround(position.x, position.z, out float below) && !float.IsInfinity(below))
						{
							float floorTop = below + length * 0.4f + 0.1f;
							if (floorTop > roof)
							{
								continue;
							}
							position.y = Mathf.Clamp(position.y, floorTop, roof);
						}
						else
						{
							position.y = Mathf.Min(position.y, roof);
						}
						break;
					}
				}

				float stroke = Cycle(seconds, 1.0 / Math.Max(0.01, kind.StrokeHz * Mathf.Lerp(0.85f, 1.15f, r2)), r1);
				Color colour = palette ? kind.Palette[(int)(r3 * kind.Palette.Length) % kind.Palette.Length] : Color.white * Mathf.Lerp(0.9f, 1.1f, r0);
				var rotation = Quaternion.LookRotation(forward, up);
				into.Add(new SeaInstance
				{
					Matrix = Matrix4x4.TRS(position, rotation, Vector3.one * length),
					Anim = new Vector4(stroke, strength, memberTurn, 0f),
					Tint = new Vector4(colour.linear.r, colour.linear.g, colour.linear.b, visible),
				});
			}
		}
	}
}
