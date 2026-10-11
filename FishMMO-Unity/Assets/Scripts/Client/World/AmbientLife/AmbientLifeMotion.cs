using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>What the ambient life asks of the world: the ground, the water over it and the country it is.</summary>
	public interface IAmbientWorld
	{
		/// <summary>
		/// The ground's height at a world (x, z); false where nothing is loaded there yet. Positive infinity (and
		/// true) past the scene's edge, where there is no ground.
		/// </summary>
		bool TryGround(float x, float z, out float y);

		/// <summary>
		/// The still water surface over (x, z) — the sea at its mean level (never the tide's, so placement does not
		/// depend on the hour), a lake's or a river's — or negative infinity where there is none. Whether the ground
		/// stands above it is the caller's to compare.
		/// </summary>
		float WaterAt(float x, float z, out bool sea);

		/// <summary>
		/// The water's surface over (x, z) as it is now — the sea with its tide (on the shared clock, so the same for
		/// everyone at the same moment) — for what sits on it; negative infinity where there is none.
		/// </summary>
		float LiveWaterAt(float x, float z);

		/// <summary>The country (from the biome) and the painted climate's temperature (-1..1) at a world position.</summary>
		void Habitat(Vector3 position, out AmbientHabitat habitat, out float temperature);

		/// <summary>
		/// The grass standing at (x, z), metres: what would hide an animal on the ground. 0 where there is none or it is not
		/// known yet, so a world without grass needs nothing here.
		/// </summary>
		float CoverAt(float x, float z) => 0f;
	}

	/// <summary>One group of ambient creatures: a flock, a pair of birds, a raptor, a colony of bats, a family of rats.</summary>
	public struct AmbientGroup
	{
		/// <summary>The kind's index in the catalogue.</summary>
		public int Kind;
		/// <summary>Everything else about the group and its members is drawn from this.</summary>
		public uint Seed;
		/// <summary>Its home: x, z, and the surface there (the ground, or the water for a group living over it).</summary>
		public Vector3 Home;
		/// <summary>How far from home it ranges, metres.</summary>
		public float Radius;
		/// <summary>Flocks, raptors and bats: metres above the surface it flies at.</summary>
		public float Altitude;
		/// <summary>Seconds one episode of its routine lasts.</summary>
		public double Episode;
		/// <summary>Members.</summary>
		public int Count;
		/// <summary>Body length, metres (each member varies about it).</summary>
		public float Length;
		/// <summary>
		/// 0..1: drawn while the moment's activity share (AmbientGating.Activity) is above this, so the same groups
		/// come out first for everyone as the light changes.
		/// </summary>
		public float Rank;
		/// <summary>Where this group's members' fright states start in their cell's array.</summary>
		public int StateFirst;
	}

	/// <summary>One member's fright: when it took flight or bolted, from where, and where it went.</summary>
	public struct AmbientMemberState
	{
		public const byte None = 0, ToPerch = 1, ToGround = 2, Gone = 3, ToTrunk = 4, ToWater = 5;

		/// <summary>What it fled to (None: not frightened).</summary>
		public byte Refuge;
		/// <summary>The episode it took fright in (birds return to their routine at the next one).</summary>
		public long Episode;
		/// <summary>World seconds it took fright at.</summary>
		public double At;
		/// <summary>World seconds it started coming back (ground animals), or 0.</summary>
		public double Return;
		public Vector3 From;
		public Vector3 To;
		/// <summary>The way it fled (horizontal, normalised).</summary>
		public Vector3 Flee;
		/// <summary>Tree height for a squirrel's climb.</summary>
		public float Height;
	}

	/// <summary>Who the animals are afraid of this frame: the camera, the player, other characters near.</summary>
	public sealed class AmbientThreats
	{
		public const int Max = 16;
		public readonly Vector3[] Positions = new Vector3[Max];
		public readonly float[] Speeds = new float[Max];
		public int Count;

		public void Clear() => Count = 0;

		public void Add(Vector3 position, float speed)
		{
			if (Count < Max)
			{
				Positions[Count] = position;
				Speeds[Count] = speed;
				Count++;
			}
		}

		/// <summary>The first threat close enough to frighten an animal of a kind at a place, if any.</summary>
		public bool Frightens(AmbientCreatureKind kind, Vector3 animal, out Vector3 threat)
		{
			for (int i = 0; i < Count; i++)
			{
				if (AmbientGating.ShouldFlush(kind, animal, Positions[i], Speeds[i], false))
				{
					threat = Positions[i];
					return true;
				}
			}
			threat = default;
			return false;
		}

		/// <summary>Squared horizontal distance to the nearest threat (infinity with none).</summary>
		public float NearestSq(Vector3 p)
		{
			float best = float.PositiveInfinity;
			for (int i = 0; i < Count; i++)
			{
				float dx = Positions[i].x - p.x, dz = Positions[i].z - p.z;
				best = Mathf.Min(best, dx * dx + dz * dz);
			}
			return best;
		}
	}

	/// <summary>One animal drawn this frame.</summary>
	public struct AmbientInstance
	{
		public Matrix4x4 Matrix;
		/// <summary>FishAmbientLife.hlsl's <c>_Anim</c>: phase, stroke strength, fold (birds) or hop (walkers), head turn or pose.</summary>
		public Vector4 Anim;
		/// <summary>FishAmbientLife.hlsl's <c>_Tint</c>: colour, and how much is drawn.</summary>
		public Vector4 Tint;
	}

	/// <summary>
	/// Where the ambient creatures live and what each is doing at a moment: placement per cell from the scene, the
	/// cell, the kind and the ground; routines as pure functions of the shared world clock, so every player sees the
	/// same flock wheel over the same field and land in the same oak. Only a fright is this client's own: who
	/// startles a bird depends on who is standing near it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Routines are episodes.</b> Time is cut into episodes of a length fixed per group (per member for birds
	/// alone and ground animals); each episode's choice — a perch, a patch of ground, a thermal, whether a flock lands
	/// — is hashed from the episode's index, so where an animal is at any moment needs no memory of where it was. The
	/// move from one episode's spot to the next takes the end of the episode, so motion is continuous.
	/// </para>
	/// <para>
	/// <b>Frights overlay the routine.</b> A bird flushed from its perch flies to a refuge (a tree away from the
	/// threat, a patch of ground further off, or away out of sight) and rejoins its routine from there at the next
	/// episode; a flushed flock bursts up and rejoins its loop; a ground animal bolts, goes to ground and comes back
	/// once the coast has been clear for a while.
	/// </para>
	/// </remarks>
	public static class AmbientLifeMotion
	{
		/// <summary>Seconds a flock takes to settle from its loop to the ground, or to climb back.</summary>
		public const float FlockTransition = 7f;
		/// <summary>Seconds a ground animal stays hidden after bolting, at least.</summary>
		public const float HideSeconds = 15f;

		/// <summary>The share of a squirrel's rests spent clinging to a trunk rather than on the ground.</summary>
		public const float TrunkRestShare = 0.45f;
		/// <summary>Seconds a ground animal takes to vanish into cover, and to reappear.</summary>
		public const float FadeSeconds = 0.4f;

		// ── Hashing and clocks ────────────────────────────────────────

		private static uint Mix(uint x) => SeaLifePlacement.Mix(x);
		private static float Unit(uint h) => SeaLifePlacement.Unit(h);

		/// <summary>A member's own seed.</summary>
		public static uint MemberSeed(uint groupSeed, int member) => Mix(groupSeed ^ ((uint)member * 0x9E3779B9u) ^ 0xA3B1E5u);

		/// <summary>An episode's own hash.</summary>
		public static uint EpisodeSeed(uint seed, long episode) => Mix(seed ^ Mix((uint)episode * 0x85EBCA6Bu ^ (uint)(episode >> 32) * 0xC2B2AE35u));

		/// <summary>The episode a time falls in, and when it started (episodes of a fixed length, offset per seed).</summary>
		public static long EpisodeAt(double seconds, double length, float offset, out double start)
		{
			double e = seconds / Math.Max(0.5, length) + offset;
			long index = (long)Math.Floor(e);
			start = (index - offset) * length;
			return index;
		}

		/// <summary>An angle (0..2π) turning at a rate (radians a second) from a phase, worked in double precision (world seconds are large).</summary>
		public static float Angle(double seconds, double rate, float phase)
		{
			double a = seconds * rate + phase;
			a -= Math.Floor(a / (2.0 * Math.PI)) * (2.0 * Math.PI);
			return (float)a;
		}

		private static Vector3 Disk(uint h, float radius)
		{
			float a = Unit(h) * Mathf.PI * 2f;
			float r = Mathf.Sqrt(Unit(Mix(h ^ 0x51u))) * radius;
			return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
		}

		/// <summary>The ground at (x, z) when it is dry land, loaded and inside the scene.</summary>
		public static bool DryGround(IAmbientWorld world, float x, float z, out float y)
		{
			if (!world.TryGround(x, z, out y) || float.IsInfinity(y))
			{
				return false;
			}
			return y > world.WaterAt(x, z, out _) + 0.05f;
		}

		/// <summary>The surface at (x, z): the water where it stands over the ground, else the ground; false past the scene or unloaded.</summary>
		private static bool Surface(IAmbientWorld world, float x, float z, out float y, out bool wet)
		{
			wet = false;
			if (!world.TryGround(x, z, out y) || float.IsInfinity(y))
			{
				return false;
			}
			float water = world.WaterAt(x, z, out _);
			if (water > y)
			{
				y = water;
				wet = true;
			}
			return true;
		}

		// ── Placement ─────────────────────────────────────────────────

		/// <summary>
		/// A kind's groups in one cell of the placement grid, appended to <paramref name="groups"/>; false when the
		/// ground under the cell is not loaded yet (nothing is appended, and the cell should be asked again).
		/// </summary>
		/// <param name="density">The family's density multiplier (settings).</param>
		/// <param name="perches">The tree perches in the cell (x, crown top y, z, tree height).</param>
		public static bool TryPlace(AmbientCreatureKind kind, int kindIndex, float density, uint sceneSeed, int cellX, int cellZ, float cellMetres,
			IAmbientWorld world, List<Vector4> perches, List<AmbientGroup> groups)
		{
			if (kind == null || !kind.Enabled || kind.GroupsPerKm2 <= 0f || density <= 0f)
			{
				return true;
			}
			int before = groups.Count;
			float area = cellMetres * cellMetres * 1e-6f;
			float perKm2 = kind.GroupsPerKm2 * density;
			int candidates = Mathf.CeilToInt(perKm2 * area * Mathf.Max(1f, kind.PreferWeight));
			for (int i = 0; i < candidates; i++)
			{
				uint seed = SeaLifePlacement.Hash(sceneSeed ^ 0xA1B1E000u, cellX * 4099 + kindIndex, cellZ, i);
				var home = new Vector3((cellX + Unit(Mix(seed ^ 0x1u))) * cellMetres, 0f, (cellZ + Unit(Mix(seed ^ 0x2u))) * cellMetres);
				// A squirrel lives at the foot of a tree.
				if (kind.NeedsTrees && perches != null && perches.Count > 0)
				{
					Vector4 tree = perches[(int)(Unit(Mix(seed ^ 0x3u)) * perches.Count) % perches.Count];
					home = new Vector3(tree.x, 0f, tree.z) + Disk(Mix(seed ^ 0x4u), 3f);
				}
				if (!world.TryGround(home.x, home.z, out float ground))
				{
					groups.RemoveRange(before, groups.Count - before);
					return false;
				}
				if (float.IsInfinity(ground))
				{
					continue;
				}
				float water = world.WaterAt(home.x, home.z, out bool seaHere);
				bool wet = water > ground + 0.05f;
				bool walker = kind.Behaviour == AmbientBehaviour.Scurry || kind.Behaviour == AmbientBehaviour.Perch;
				if (walker && wet)
				{
					continue;
				}
				if (kind.Behaviour == AmbientBehaviour.Paddle && (!wet || seaHere || water - ground < 0.3f))
				{
					continue;
				}
				// Shore crabs keep to the beach: the band the tide works over, and a little above it.
				if (kind.NeedsSea && kind.Behaviour == AmbientBehaviour.Scurry && !(seaHere && ground - water < 2.5f))
				{
					continue;
				}
				bool seaNear = kind.NeedsSea && (seaHere && wet || Near(world, home, kind.Behaviour == AmbientBehaviour.Scurry ? 20f : 160f, true));
				bool freshNear = kind.NeedsFreshWater && (kind.Behaviour == AmbientBehaviour.Paddle || Near(world, home, 10f, false));
				bool treesNear = perches != null && perches.Count > 0;
				world.Habitat(new Vector3(home.x, ground, home.z), out AmbientHabitat habitat, out float temperature);
				float weight = AmbientGating.HabitatWeight(kind, habitat, temperature, seaNear, freshNear, treesNear);
				// Candidate i lives here with chance (expected count - i), so the count is the expectation on average.
				float expected = perKm2 * area * weight;
				if (Unit(Mix(seed ^ 0x5u)) >= Mathf.Clamp01(expected - i))
				{
					continue;
				}
				groups.Add(new AmbientGroup
				{
					Kind = kindIndex,
					Seed = seed,
					Home = new Vector3(home.x, wet ? water : ground, home.z),
					Radius = Mathf.Lerp(kind.Roam.x, kind.Roam.y, Unit(Mix(seed ^ 0x6u))),
					Altitude = Mathf.Lerp(kind.Altitude.x, kind.Altitude.y, Unit(Mix(seed ^ 0x7u))),
					Episode = Mathf.Lerp(kind.Episode.x, kind.Episode.y, Unit(Mix(seed ^ 0x8u))),
					Count = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(kind.GroupSize.x, kind.GroupSize.y + 0.999f, Unit(Mix(seed ^ 0x9u))) - 0.5f)),
					Length = Mathf.Lerp(kind.Length.x, kind.Length.y, Unit(Mix(seed ^ 0xAu))),
					Rank = Unit(Mix(seed ^ 0xBu)),
				});
			}
			return true;
		}

		/// <summary>True when the sea (or, for <paramref name="sea"/> false, a lake or river) stands over the ground somewhere on a ring round a place.</summary>
		private static bool Near(IAmbientWorld world, Vector3 at, float radius, bool sea)
		{
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.PI * 0.25f;
				float x = at.x + Mathf.Cos(a) * radius, z = at.z + Mathf.Sin(a) * radius;
				if (!world.TryGround(x, z, out float y) || float.IsInfinity(y))
				{
					continue;
				}
				float water = world.WaterAt(x, z, out bool isSea);
				if (water > y && isSea == sea)
				{
					return true;
				}
			}
			return false;
		}

		// ── Evaluation ────────────────────────────────────────────────

		/// <summary>Everything the evaluation of one group needs besides the group.</summary>
		public struct Frame
		{
			public double Seconds;
			public AmbientConditions Conditions;
			public IAmbientWorld World;
			/// <summary>Tree perches in the group's cell (x, crown top, z, height).</summary>
			public List<Vector4> Perches;
			/// <summary>The cell's fright states, indexed from the group's <see cref="AmbientGroup.StateFirst"/>.</summary>
			public AmbientMemberState[] States;
			/// <summary>Who frightens them this frame (none: nothing takes fright, as for a scene-view draw).</summary>
			public AmbientThreats Threats;
			/// <summary>How much of the group is drawn (the activity's edge), 0..1.</summary>
			public float Visible;
		}

		/// <summary>Every member of a group at the frame's moment, appended to <paramref name="into"/>.</summary>
		public static void Evaluate(in AmbientGroup group, AmbientCreatureKind kind, ref Frame frame, List<AmbientInstance> into)
		{
			switch (kind.Behaviour)
			{
				case AmbientBehaviour.Flock:
					Flock(group, kind, ref frame, into);
					break;
				case AmbientBehaviour.Perch:
				case AmbientBehaviour.Paddle:
					for (int m = 0; m < group.Count; m++)
					{
						Bird(group, kind, m, ref frame, into);
					}
					break;
				case AmbientBehaviour.Soar:
					Soar(group, kind, ref frame, into);
					break;
				case AmbientBehaviour.Hawk:
					Bats(group, kind, ref frame, into);
					break;
				default:
					for (int m = 0; m < group.Count; m++)
					{
						Critter(group, kind, m, ref frame, into);
					}
					break;
			}
		}

		private static void Emit(List<AmbientInstance> into, AmbientCreatureKind kind, uint own, Vector3 position, Quaternion rotation, float length, Vector4 anim, float visible)
		{
			if (visible <= 0.002f)
			{
				return;
			}
			Color tint = Color.white * Mathf.Lerp(0.9f, 1.1f, Unit(Mix(own ^ 0x77u)));
			if (kind.Palette != null && kind.Palette.Length > 0)
			{
				tint = kind.Palette[(int)(Unit(Mix(own ^ 0x78u)) * kind.Palette.Length) % kind.Palette.Length];
			}
			Color lin = tint.linear;
			into.Add(new AmbientInstance
			{
				Matrix = Matrix4x4.TRS(position, rotation, Vector3.one * length),
				Anim = anim,
				Tint = new Vector4(lin.r, lin.g, lin.b, Mathf.Clamp01(visible)),
			});
		}

		private static Quaternion Facing(Vector3 forward, Vector3 up)
		{
			if (forward.sqrMagnitude < 1e-8f)
			{
				forward = Vector3.forward;
			}
			return Quaternion.LookRotation(forward, up);
		}

		/// <summary>A bird's rotation in flight: nose along its velocity, rolled into its turn.</summary>
		private static Quaternion Flight(Vector3 velocity, float bankDegrees)
		{
			if (velocity.sqrMagnitude < 1e-8f)
			{
				velocity = Vector3.forward;
			}
			Vector3 forward = velocity.normalized;
			// Climbing or diving no steeper than 50°: a bird's body leads its flight path, not its velocity's every kink.
			forward.y = Mathf.Clamp(forward.y, -0.75f, 0.75f);
			return Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, -bankDegrees);
		}

		/// <summary>Wing beats with glides between: <paramref name="duty"/> of each cycle flapping (1 = steady flapping).</summary>
		private static float FlapStrength(double seconds, float period, float duty, float offset)
		{
			if (duty >= 0.999f)
			{
				return 1f;
			}
			float c = SeaLifePlacement.Cycle(seconds, period, offset);
			// Soft edges a twentieth of the cycle wide: wings sweep into and out of the glide.
			return Mathf.Clamp01(Mathf.Min(c / 0.05f, (duty - c) / 0.05f));
		}

		/// <summary>Flap rhythm per body: bounding finches, gliding gulls, rowing crows, soaring raptors.</summary>
		private static void Rhythm(AmbientShape shape, out float period, out float duty)
		{
			switch (shape)
			{
				case AmbientShape.Songbird: period = 0.9f; duty = 0.6f; break;     // bounding flight: a burst of beats, wings shut, a burst
				case AmbientShape.Gull: period = 4.5f; duty = 0.6f; break;
				case AmbientShape.Crow: period = 6f; duty = 0.85f; break;
				case AmbientShape.Raptor: period = 30f; duty = 0.1f; break;
				case AmbientShape.Vulture: period = 45f; duty = 0.05f; break;
				case AmbientShape.Bat: period = 1.3f; duty = 0.85f; break;
				default: period = 2f; duty = 0.9f; break;
			}
		}

		// ── Flocks ────────────────────────────────────────────────────

		/// <summary>Where a flock's centre is on its loop at a time: a slow wandering figure over its home, up and down a little.</summary>
		public static Vector3 FlockCentre(in AmbientGroup g, AmbientCreatureKind kind, double seconds)
		{
			float r = Mathf.Max(5f, g.Radius);
			double w = kind.Speed / (r * 1.25);
			float p1 = Unit(Mix(g.Seed ^ 0x11u)) * 6.28f, p2 = Unit(Mix(g.Seed ^ 0x12u)) * 6.28f, p3 = Unit(Mix(g.Seed ^ 0x13u)) * 6.28f;
			float sense = Unit(Mix(g.Seed ^ 0x14u)) < 0.5f ? -1f : 1f;
			float t1 = Angle(seconds, w * sense, p1), t2 = Angle(seconds, w * 2.3 * sense, p2), t3 = Angle(seconds, w * 1.7, p3), t4 = Angle(seconds, w * 0.6, p2);
			float x = r * Mathf.Cos(t1) + 0.35f * r * Mathf.Cos(t2);
			float z = r * 0.7f * Mathf.Sin(t1) + 0.3f * r * Mathf.Sin(t3);
			float yaw = Unit(Mix(g.Seed ^ 0x15u)) * Mathf.PI * 2f;
			float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
			return new Vector3(g.Home.x + c * x - s * z, g.Home.y + g.Altitude * (1f + 0.3f * Mathf.Sin(t4)), g.Home.z + s * x + c * z);
		}

		/// <summary>A member's place in its flock at a time: a fixed spot in a flattened, breathing ellipsoid, wobbling about it.</summary>
		private static Vector3 FlockMember(in AmbientGroup g, AmbientCreatureKind kind, int m, double seconds, Vector3 centre, Vector3 heading)
		{
			uint own = MemberSeed(g.Seed, m);
			float u = Unit(own) * 2f - 1f, v = Unit(Mix(own ^ 0x1u)) * 2f - 1f, w = Unit(Mix(own ^ 0x2u)) * 2f - 1f;
			var place = new Vector3(u, v, w);
			if (place.sqrMagnitude > 1f)
			{
				place.Normalize();
			}
			// A flock breathes: it spreads and bunches over tens of seconds, and a wave runs through it front to back.
			float breathe = 1f + 0.3f * Mathf.Sin(Angle(seconds, 0.21, Unit(Mix(g.Seed ^ 0x16u)) * 6.28f));
			float spread = kind.Spread * breathe;
			float a1 = Angle(seconds, 0.8 + 0.5 * Unit(Mix(own ^ 0x3u)), Unit(Mix(own ^ 0x4u)) * 6.28f);
			float a2 = Angle(seconds, 0.55 + 0.4 * Unit(Mix(own ^ 0x5u)), Unit(Mix(own ^ 0x6u)) * 6.28f);
			float wave = Mathf.Sin(Angle(seconds, 0.9, 0f) - w * 2.5f);
			var side = new Vector3(heading.z, 0f, -heading.x);
			Vector3 local = new Vector3(place.x * spread + 0.25f * spread * Mathf.Sin(a1),
				place.y * spread * 0.35f + 0.2f * spread * wave + 0.15f * spread * Mathf.Sin(a2),
				place.z * spread * 1.3f + 0.2f * spread * Mathf.Cos(a2));
			return centre + side * local.x + Vector3.up * local.y + heading * local.z;
		}

		private static void Flock(in AmbientGroup g, AmbientCreatureKind kind, ref Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			IAmbientWorld world = f.World;
			long episode = EpisodeAt(t, g.Episode, Unit(Mix(g.Seed ^ 0x20u)), out double start);
			float tau = (float)(t - start), length = (float)g.Episode;
			uint eh = EpisodeSeed(g.Seed, episode);
			float shelter = AmbientGating.Shelter(f.Conditions);
			bool gull = kind.Shape == AmbientShape.Gull;
			bool sheltering = shelter > 0.5f;

			// The loop: the centre now and a moment on, kept above the ground under it.
			Vector3 centre = FlockCentre(g, kind, t);
			Vector3 next = FlockCentre(g, kind, t + 0.4);
			Vector3 later = FlockCentre(g, kind, t + 0.8);
			float floorUnder = Surface(world, centre.x, centre.z, out float under, out _) ? under : g.Home.y;
			float lift = Mathf.Max(0f, floorUnder + g.Altitude * 0.5f + kind.Spread - centre.y);
			centre.y += lift;
			next.y += lift;
			Vector3 velocity = (next - centre) / 0.4f;
			var heading = new Vector3(velocity.x, 0f, velocity.z);
			heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.forward;
			var nextHeading = new Vector3(later.x - next.x, 0f, later.z - next.z);
			float turn = nextHeading.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(heading, nextHeading.normalized, Vector3.up) / 0.4f : 0f;
			float bank = Mathf.Clamp(turn * 0.9f, -45f, 45f);

			// This episode: aloft the whole time, or down for a while on the ground, the water or in trees.
			bool lands = sheltering || Unit(eh) < (gull ? 0.5f : 0.4f);
			bool toTrees = false;
			Vector3 spot = default;
			bool spotWet = false;
			int perchCentre = -1;
			if (lands)
			{
				bool hasTrees = !gull && f.Perches != null && f.Perches.Count > 0;
				if (hasTrees && (sheltering || Unit(Mix(eh ^ 0x1u)) < 0.4f))
				{
					toTrees = true;
					perchCentre = (int)(Unit(Mix(eh ^ 0x2u)) * f.Perches.Count) % f.Perches.Count;
					Vector4 p = f.Perches[perchCentre];
					spot = new Vector3(p.x, p.y, p.z);
				}
				else if (sheltering)
				{
					return; // sitting out the rain under cover somewhere: none to see
				}
				else
				{
					lands = false;
					for (int attempt = 0; attempt < 3 && !lands; attempt++)
					{
						Vector3 at = g.Home + Disk(Mix(eh ^ (0x3u + (uint)attempt)), g.Radius * 0.6f);
						if (!Surface(world, at.x, at.z, out float y, out bool wet) || (wet && !gull))
						{
							continue;
						}
						spot = new Vector3(at.x, y, at.z);
						spotWet = wet;
						lands = true;
					}
				}
			}

			// A fright: the whole flock goes up together when anyone comes near where it has settled.
			ref AmbientMemberState state = ref f.States[g.StateFirst];
			if (state.Refuge != AmbientMemberState.None && state.Episode != episode)
			{
				state = default;
			}
			bool flushed = state.Refuge != AmbientMemberState.None;
			if (lands && !flushed && tau > FlockTransition * 0.6f && tau < length - FlockTransition && f.Threats != null
				&& f.Threats.Frightens(kind, spot, out Vector3 threat))
			{
				state.Refuge = AmbientMemberState.Gone;
				state.Episode = episode;
				state.At = t;
				state.Flee = AmbientGating.FleeDirection(spot, threat, eh);
				flushed = true;
			}

			Rhythm(kind.Shape, out float period, out float duty);
			for (int m = 0; m < g.Count; m++)
			{
				uint own = MemberSeed(g.Seed, m);
				float len = g.Length * Mathf.Lerp(0.88f, 1.12f, Unit(Mix(own ^ 0x7u)));
				Vector3 aloft = FlockMember(g, kind, m, t, centre, heading);
				float stagger = Unit(Mix(own ^ 0x8u)) * 1.5f;
				float flap = FlapStrength(t, period * Mathf.Lerp(0.85f, 1.15f, Unit(Mix(own ^ 0x9u))), duty, Unit(Mix(own ^ 0xAu)));
				float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, kind.StrokeHz * Mathf.Lerp(0.9f, 1.1f, Unit(Mix(own ^ 0xBu)))), Unit(Mix(own ^ 0xCu)));
				Quaternion flying = Flight(velocity + new Vector3(0f, 0.15f * Mathf.Sin(Angle(t, 0.7, Unit(own) * 6.28f)), 0f), bank + 6f * Mathf.Sin(Angle(t, 0.5, Unit(Mix(own ^ 0xDu)) * 6.28f)));
				if (!lands)
				{
					Emit(into, kind, own, aloft, flying, len, new Vector4(phase, flap, 0f, 0f), f.Visible);
					continue;
				}

				// Where this member sits once down: scattered over the patch (all facing much the same way), riding
				// the water as it is now (the tide moves it), or on the crowns of the trees round the chosen one.
				Vector3 down;
				bool perched = false;
				float yaw = Unit(Mix(eh ^ 0x40u)) * 360f + (Unit(Mix(own ^ 0xEu)) - 0.5f) * 70f;
				if (toTrees)
				{
					Vector4 p = PerchNear(f.Perches, perchCentre, m, 40f);
					float h = Mathf.Max(2f, p.w);
					Vector3 jitter = Disk(Mix(own ^ 0xFu), h * 0.12f);
					down = new Vector3(p.x + jitter.x, p.y - Unit(Mix(own ^ 0x10u)) * h * 0.12f, p.z + jitter.z);
					perched = true;
				}
				else
				{
					Vector3 at = spot + Disk(Mix(own ^ 0x11u), kind.Spread * 0.7f);
					if (!Surface(world, at.x, at.z, out float y, out bool wet) || (wet && !gull))
					{
						at = spot;
						y = spot.y;
						wet = spotWet;
					}
					if (wet)
					{
						float live = world.LiveWaterAt(at.x, at.z);
						down = new Vector3(at.x, float.IsInfinity(live) ? y : live, at.z) + Vector3.up * (0.015f * Mathf.Sin(Angle(t, 1.7, Unit(own) * 6.28f)));
					}
					else
					{
						down = new Vector3(at.x, y, at.z) + Forage(own, t, out _, out _);
					}
				}

				float settle;
				if (flushed)
				{
					// Up and away from the threat at once (a flock goes within half a second of the first bird), then
					// back to the loop.
					float dt = Mathf.Max(0f, (float)(t - state.At) - stagger * 0.25f);
					down += state.Flee * (kind.FleeSpeed * 0.7f * dt) + Vector3.up * (2.5f * dt);
					settle = 1f - Mathf.SmoothStep(0f, 1f, dt / 6f);
				}
				else if (tau < FlockTransition + stagger)
				{
					settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((tau - stagger) / FlockTransition));
				}
				else if (tau > length - FlockTransition - 1.5f + stagger)
				{
					settle = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((tau - (length - FlockTransition - 1.5f + stagger)) / FlockTransition));
				}
				else
				{
					settle = 1f;
				}

				if (settle >= 0.999f)
				{
					Quaternion sitting = Quaternion.Euler(0f, yaw, 0f);
					float peck = perched ? 0f : Peck(own, t);
					float head = HeadTurn(own, t);
					Emit(into, kind, own, down, sitting * Quaternion.Euler(peck * 40f, 0f, 0f), len, new Vector4(0f, 0f, 1f, head), f.Visible);
					continue;
				}
				// Swooping between the loop and the ground: down in a curve, wings beating, folding at the last.
				Vector3 path = Vector3.Lerp(aloft, down, settle) + Vector3.up * (Mathf.Sin(settle * Mathf.PI) * 2f);
				Vector3 toward = (flushed ? aloft - down : down - aloft);
				Quaternion rotation = Flight(Vector3.Lerp(velocity, toward, 0.6f), 0f);
				Emit(into, kind, own, path, rotation, len, new Vector4(phase, 1f, settle > 0.93f ? (settle - 0.93f) / 0.07f : 0f, 0f), f.Visible);
			}
		}

		/// <summary>The perch for a flock's <paramref name="member"/>: among the trees within reach of the chosen one, spread round them.</summary>
		private static Vector4 PerchNear(List<Vector4> perches, int centre, int member, float reach)
		{
			Vector4 c = perches[centre];
			int seen = 0;
			int pick = member % 5;
			for (int k = 0; k < perches.Count; k++)
			{
				Vector4 p = perches[(centre + k) % perches.Count];
				float dx = p.x - c.x, dz = p.z - c.z;
				if (dx * dx + dz * dz > reach * reach)
				{
					continue;
				}
				if (seen == pick)
				{
					return p;
				}
				seen++;
			}
			return c;
		}

		// ── Small behaviours ──────────────────────────────────────────

		/// <summary>
		/// A foraging bird's little hops about its spot: every second or two it hops a few centimetres to a new place
		/// (a small arc), and between hops it pecks. Returns the offset from the spot.
		/// </summary>
		private static Vector3 Forage(uint own, double seconds, out float yaw, out bool hopping)
		{
			double period = 1.2 + 1.3 * Unit(Mix(own ^ 0x60u));
			double e = seconds / period + Unit(Mix(own ^ 0x61u));
			long k = (long)Math.Floor(e);
			float u = (float)(e - k);
			Vector3 now = Disk(EpisodeSeed(own, k), 0.45f), before = Disk(EpisodeSeed(own, k - 1), 0.45f);
			float hop = Mathf.Clamp01(u / 0.18f);
			hopping = hop < 1f;
			Vector3 at = Vector3.Lerp(before, now, hop);
			at.y = 4f * hop * (1f - hop) * 0.05f;
			Vector3 d = now - before;
			yaw = d.sqrMagnitude > 1e-6f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : 0f;
			return at;
		}

		/// <summary>0..1: how far down a foraging bird's head is in its pecking (quick jabs in bursts).</summary>
		private static float Peck(uint own, double seconds)
		{
			float burst = SeaLifePlacement.Cycle(seconds, 2.2 + Unit(Mix(own ^ 0x62u)), Unit(Mix(own ^ 0x63u)));
			if (burst > 0.45f)
			{
				return 0f;
			}
			float jab = SeaLifePlacement.Cycle(seconds, 0.32, Unit(Mix(own ^ 0x64u)));
			return Mathf.Pow(Mathf.Sin(jab * Mathf.PI), 4f);
		}

		/// <summary>A sitting bird's head turning to look about: a new direction every second or two, snapped to quickly (radians).</summary>
		private static float HeadTurn(uint own, double seconds)
		{
			double period = 0.9 + 1.4 * Unit(Mix(own ^ 0x65u));
			double e = seconds / period + Unit(Mix(own ^ 0x66u));
			long k = (long)Math.Floor(e);
			float u = (float)(e - k);
			float now = (Unit(EpisodeSeed(own ^ 0x67u, k)) - 0.5f) * 1.6f, before = (Unit(EpisodeSeed(own ^ 0x67u, k - 1)) - 0.5f) * 1.6f;
			return Mathf.Lerp(before, now, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.12f)));
		}

		// ── Birds alone, and ducks ────────────────────────────────────

		/// <summary>
		/// Where a lone bird (or a duck) is in an episode: a tree perch near home or a patch of ground (a patch of
		/// still water for a duck). False when it has nowhere to be (sheltering from the rain with no tree to hand).
		/// </summary>
		private static bool BirdSpot(in AmbientGroup g, AmbientCreatureKind kind, uint own, long episode, ref Frame f, float shelter, out Vector3 spot, out bool tree)
		{
			uint eh = EpisodeSeed(own, episode);
			IAmbientWorld world = f.World;
			tree = false;
			spot = g.Home;
			if (kind.Behaviour == AmbientBehaviour.Paddle)
			{
				for (int attempt = 0; attempt < 3; attempt++)
				{
					Vector3 at = g.Home + Disk(Mix(eh ^ (uint)attempt), g.Radius);
					if (world.TryGround(at.x, at.z, out float y) && !float.IsInfinity(y))
					{
						float water = world.WaterAt(at.x, at.z, out bool sea);
						if (!sea && water > y + 0.2f)
						{
							spot = new Vector3(at.x, water, at.z);
							return true;
						}
					}
				}
				return true; // stays at home on the water
			}
			List<Vector4> perches = f.Perches;
			/* A bird up in a crown 10–30 m over the path is not seen, and one in the grass barely: the open ground and the
			 * flights between are where the eye finds them (Jim, 2026-10-10: "still rarely seeing birds"), so most episodes
			 * are on the ground and they are short, so a bird is often on the wing. */
			float treeShare = kind.Shape == AmbientShape.Crow ? 0.3f : 0.35f;
			if (perches != null && perches.Count > 0 && (shelter > 0.5f || Unit(eh) < treeShare))
			{
				int first = (int)(Unit(Mix(eh ^ 0x1u)) * perches.Count) % perches.Count;
				float reach = g.Radius * 1.6f + 10f;
				for (int k = 0; k < perches.Count; k++)
				{
					Vector4 p = perches[(first + k) % perches.Count];
					float dx = p.x - g.Home.x, dz = p.z - g.Home.z;
					if (dx * dx + dz * dz > reach * reach)
					{
						continue;
					}
					// The crown's top, or out on its side and a little down (a broadleaf's outer twigs).
					float h = Mathf.Max(2f, p.w);
					Vector3 side = Unit(Mix(eh ^ 0x2u)) < 0.5f ? Vector3.zero : Disk(Mix(eh ^ 0x3u), h * 0.14f);
					spot = new Vector3(p.x + side.x, p.y - (side.sqrMagnitude > 0f ? h * (0.04f + 0.12f * Unit(Mix(eh ^ 0x4u))) : 0f), p.z + side.z);
					tree = true;
					return true;
				}
			}
			if (shelter > 0.5f)
			{
				return false;
			}
			// On the ground: the most open of a few places (a songbird in tall grass is not seen; see CritterSpot).
			bool onGround = false;
			float bestCover = float.MaxValue;
			for (int attempt = 0; attempt < SpotChoices; attempt++)
			{
				Vector3 at = g.Home + Disk(Mix(eh ^ (0x10u + (uint)attempt)), g.Radius);
				if (DryGround(world, at.x, at.z, out float y))
				{
					float cover = world.CoverAt(at.x, at.z);
					if (!onGround || cover < bestCover - 0.02f)
					{
						onGround = true;
						bestCover = cover;
						spot = new Vector3(at.x, y, at.z);
					}
				}
			}
			if (!onGround)
			{
				spot = g.Home;
			}
			return true;
		}

		/// <summary>Flight time between two spots: at the kind's speed, at least a second, at most most of an episode.</summary>
		private static float FlightSeconds(AmbientCreatureKind kind, Vector3 a, Vector3 b, float episode)
		{
			float d = Vector3.Distance(a, b);
			bool paddle = kind.Behaviour == AmbientBehaviour.Paddle;
			return Mathf.Clamp(d / Mathf.Max(0.05f, kind.Speed) + (paddle ? 0f : 0.6f), 1f, episode * (paddle ? 0.85f : 0.45f));
		}

		/// <summary>A point along a hop of flight from one spot to another: up and over in an arc (undulating for a songbird), u 0..1.</summary>
		private static Vector3 Arc(AmbientCreatureKind kind, Vector3 a, Vector3 b, float u, out Vector3 velocity)
		{
			float d = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
			float rise = 1.2f + 0.12f * d;
			float e = Mathf.SmoothStep(0f, 1f, u);
			Vector3 p = Vector3.Lerp(a, b, e) + Vector3.up * (4f * u * (1f - u) * rise);
			if (kind.Shape == AmbientShape.Songbird)
			{
				// Bounding flight: a dip with each closed-wing glide (none at take-off and landing).
				p.y += 0.25f * Mathf.Sin(u * d * 1.4f) * Mathf.Min(1f, d * 0.1f) * 4f * u * (1f - u);
			}
			float u2 = Mathf.Min(1f, u + 0.02f);
			Vector3 q = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, u2)) + Vector3.up * (4f * u2 * (1f - u2) * rise);
			velocity = (q - p) / 0.02f;
			return p;
		}

		private static void Bird(in AmbientGroup g, AmbientCreatureKind kind, int m, ref Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			uint own = MemberSeed(g.Seed, m);
			bool paddle = kind.Behaviour == AmbientBehaviour.Paddle;
			double length = g.Episode * Mathf.Lerp(0.8f, 1.2f, Unit(Mix(own ^ 0x30u)));
			long episode = EpisodeAt(t, length, Unit(Mix(own ^ 0x31u)), out double start);
			float tau = (float)(t - start), len = (float)length;
			float shelter = AmbientGating.Shelter(f.Conditions);
			float size = g.Length * Mathf.Lerp(0.88f, 1.12f, Unit(Mix(own ^ 0x32u)));
			if (!BirdSpot(g, kind, own, episode, ref f, shelter, out Vector3 spot, out bool tree))
			{
				return;
			}
			bool haveNext = BirdSpot(g, kind, own, episode + 1, ref f, shelter, out Vector3 next, out bool nextTree);
			// A pair keeps together: the second bird a little off the first, on the same tree or patch.
			if (m > 0 && !paddle)
			{
				Vector3 off = Disk(Mix(own ^ 0x33u), tree ? 0.5f : 1.5f);
				spot += new Vector3(off.x, 0f, off.z);
				next += new Vector3(off.x, 0f, off.z);
				if (!tree && f.World.TryGround(spot.x, spot.z, out float gy) && !float.IsInfinity(gy))
				{
					spot.y = gy;
				}
				if (!nextTree && f.World.TryGround(next.x, next.z, out float ny) && !float.IsInfinity(ny))
				{
					next.y = ny;
				}
			}

			ref AmbientMemberState state = ref f.States[g.StateFirst + m];
			if (state.Refuge != AmbientMemberState.None && state.Episode != episode)
			{
				state = default;
			}
			// Where the bird sits out this episode: its spot, or where it fled to.
			Vector3 seat = state.Refuge != AmbientMemberState.None ? state.To : spot;
			bool seatTree = state.Refuge == AmbientMemberState.None ? tree : state.Refuge == AmbientMemberState.ToPerch;
			float flight = haveNext ? FlightSeconds(kind, seat, next, len) : 0f;
			float leave = haveNext ? len - flight : float.PositiveInfinity;

			Rhythm(kind.Shape, out float period, out float duty);
			float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, kind.StrokeHz * Mathf.Lerp(0.9f, 1.1f, Unit(Mix(own ^ 0x34u)))), Unit(Mix(own ^ 0x35u)));
			if (tau >= leave)
			{
				// On its way to the next spot: a short flight, or a paddle across the water.
				float u = Mathf.Clamp01((tau - leave) / Mathf.Max(0.1f, flight));
				if (paddle)
				{
					Vector3 swim = Vector3.Lerp(seat, next, Mathf.SmoothStep(0f, 1f, u));
					Emit(into, kind, own, swim + Vector3.up * Bob(own, t), Facing(next - seat, Vector3.up), size, new Vector4(phase, 0f, 1f, 0f), f.Visible);
					return;
				}
				Vector3 at = Arc(kind, seat, next, u, out Vector3 velocity);
				float fold = u > 0.9f ? (u - 0.9f) / 0.1f : 0f;
				float flap = Mathf.Max(FlapStrength(t, period, duty, Unit(own)), u < 0.15f || u > 0.85f ? 1f : 0f);
				Emit(into, kind, own, at, Flight(velocity, 0f), size, new Vector4(phase, flap, fold, 0f), f.Visible);
				return;
			}

			// A fright, once: off to a refuge, where it sits until it is time to move on.
			if (state.Refuge == AmbientMemberState.None && f.Threats != null && f.Threats.Frightens(kind, seat, out Vector3 threat))
			{
				state.Episode = episode;
				state.At = t;
				state.From = seat;
				state.Flee = AmbientGating.FleeDirection(seat, threat, EpisodeSeed(own, episode));
				state.Refuge = Refuge(g, kind, ref f, seat, threat, state.Flee, out state.To);
				seat = state.To;
			}
			if (state.Refuge != AmbientMemberState.None)
			{
				float away = (float)(t - state.At);
				float fleeTime = Mathf.Max(1f, Vector3.Distance(state.From, state.To) / Mathf.Max(1f, kind.FleeSpeed));
				if (away < fleeTime)
				{
					float u = away / fleeTime;
					Vector3 at = Arc(kind, state.From, state.To, u, out Vector3 velocity);
					float visible = state.Refuge == AmbientMemberState.Gone ? f.Visible * (1f - Mathf.SmoothStep(0f, 1f, (u - 0.75f) / 0.25f)) : f.Visible;
					float fold = state.Refuge != AmbientMemberState.Gone && u > 0.9f ? (u - 0.9f) / 0.1f : 0f;
					Emit(into, kind, own, at, Flight(velocity, 0f), size, new Vector4(phase, 1f, fold, 0f), visible);
					return;
				}
				if (state.Refuge == AmbientMemberState.Gone)
				{
					return; // flown off out of sight; back next episode
				}
				seatTree = state.Refuge == AmbientMemberState.ToPerch;
				paddle = paddle && state.Refuge == AmbientMemberState.ToWater;
			}

			// Sitting: on a branch, looking about; on the ground, hopping and pecking; on the water, bobbing and
			// now and then up-ending to feed.
			float head = HeadTurn(own, t);
			float yaw = Unit(EpisodeSeed(own ^ 0x36u, episode)) * 360f;
			if (paddle)
			{
				float drift = Mathf.Sin(Angle(t, 0.13, Unit(own) * 6.28f)) * 0.4f;
				Vector3 at = seat + new Vector3(drift, Bob(own, t), drift * 0.6f);
				float dabble = Dabble(own, t);
				Quaternion r = Quaternion.Euler(dabble * 75f, yaw + 25f * Mathf.Sin(Angle(t, 0.09, Unit(Mix(own ^ 0x37u)) * 6.28f)), 0f);
				Emit(into, kind, own, at, r, size, new Vector4(phase, 0f, 1f, dabble > 0.1f ? 0f : head * 0.6f), f.Visible);
				return;
			}
			if (seatTree)
			{
				Emit(into, kind, own, seat, Quaternion.Euler(0f, yaw, 0f), size, new Vector4(0f, 0f, 1f, head), f.Visible);
				return;
			}
			Vector3 hop = Forage(own, t, out float hopYaw, out bool hopping);
			Vector3 ground = seat + new Vector3(hop.x, 0f, hop.z);
			if (f.World.TryGround(ground.x, ground.z, out float gy2) && !float.IsInfinity(gy2))
			{
				ground.y = gy2;
			}
			ground.y += hop.y;
			float peck = hopping ? 0f : Peck(own, t);
			Emit(into, kind, own, ground, Quaternion.Euler(peck * 45f, yaw + hopYaw * 0.3f, 0f), size, new Vector4(0f, 0f, 1f, peck > 0.05f ? 0f : head), f.Visible);
		}

		/// <summary>A duck's bob on the water, metres.</summary>
		private static float Bob(uint own, double seconds) => 0.012f * Mathf.Sin(Angle(seconds, 2.1 + Unit(Mix(own ^ 0x38u)), Unit(own) * 6.28f));

		/// <summary>0..1: how far a dabbling duck is up-ended, feeding, for a few seconds now and then.</summary>
		private static float Dabble(uint own, double seconds)
		{
			float c = SeaLifePlacement.Cycle(seconds, 14.0 + 10.0 * Unit(Mix(own ^ 0x39u)), Unit(Mix(own ^ 0x3Au)));
			return c < 0.15f ? Mathf.Sin(c / 0.15f * Mathf.PI) : 0f;
		}

		/// <summary>
		/// Where a frightened bird goes: the tree in reach furthest from the threat (and well clear of it); else a
		/// patch of ground further along its line of flight; else away out of sight. Ducks go further out on the water
		/// or fly off.
		/// </summary>
		private static byte Refuge(in AmbientGroup g, AmbientCreatureKind kind, ref Frame f, Vector3 from, Vector3 threat, Vector3 flee, out Vector3 to)
		{
			IAmbientWorld world = f.World;
			float clear = AmbientGating.FlightDistance(kind, 0f) * 1.8f;
			if (kind.Behaviour == AmbientBehaviour.Paddle)
			{
				Vector3 at = from + flee * (25f + 15f * Unit(Mix(g.Seed ^ 0x3Bu)));
				if (world.TryGround(at.x, at.z, out float y) && !float.IsInfinity(y))
				{
					float water = world.WaterAt(at.x, at.z, out bool sea);
					if (!sea && water > y + 0.2f)
					{
						to = new Vector3(at.x, water, at.z);
						return AmbientMemberState.ToWater;
					}
				}
				to = from + flee * 60f + Vector3.up * 14f;
				return AmbientMemberState.Gone;
			}
			List<Vector4> perches = f.Perches;
			if (perches != null)
			{
				float best = -1f;
				int bestIndex = -1;
				for (int k = 0; k < perches.Count; k++)
				{
					Vector4 p = perches[k];
					float dx = p.x - from.x, dz = p.z - from.z;
					if (dx * dx + dz * dz > 45f * 45f)
					{
						continue;
					}
					float tx = p.x - threat.x, tz = p.z - threat.z;
					float fromThreat = tx * tx + tz * tz;
					if (fromThreat > clear * clear && fromThreat > best)
					{
						best = fromThreat;
						bestIndex = k;
					}
				}
				if (bestIndex >= 0)
				{
					Vector4 p = perches[bestIndex];
					to = new Vector3(p.x, p.y, p.z);
					return AmbientMemberState.ToPerch;
				}
			}
			Vector3 ground = from + flee * Mathf.Max(clear, 18f + 12f * Unit(Mix(g.Seed ^ 0x3Cu)));
			if (DryGround(world, ground.x, ground.z, out float gy))
			{
				to = new Vector3(ground.x, gy, ground.z);
				return AmbientMemberState.ToGround;
			}
			to = from + flee * 45f + Vector3.up * 15f;
			return AmbientMemberState.Gone;
		}

		// ── Raptors ───────────────────────────────────────────────────

		/// <summary>Where a soaring bird is in a thermal: circling its centre, climbing as the episode goes on.</summary>
		private static Vector3 Thermal(in AmbientGroup g, AmbientCreatureKind kind, int m, long episode, double seconds, double start, ref Frame f, out float sense)
		{
			uint eh = EpisodeSeed(g.Seed, episode);
			Vector3 centre = g.Home + Disk(eh, g.Radius);
			float ground = Surface(f.World, centre.x, centre.z, out float y, out _) ? y : g.Home.y;
			float radius = Mathf.Lerp(18f, 40f, Unit(Mix(eh ^ 0x1u)));
			sense = Unit(Mix(eh ^ 0x2u)) < 0.5f ? -1f : 1f;
			double rate = kind.Speed / radius * sense;
			float angle = Angle(seconds, rate, Unit(Mix(eh ^ 0x3u)) * 6.28f + m * 6.2831853f / Mathf.Max(1, g.Count));
			// A kettle of vultures stacks up the column; the climb is a few metres a minute's worth of a lap each.
			float climb = (float)((seconds - start) / g.Episode) * 40f;
			float height = Mathf.Max(g.Home.y, ground) + g.Altitude + climb + m * 6f;
			return new Vector3(centre.x + Mathf.Cos(angle) * radius * (1f + 0.15f * m), height, centre.z + Mathf.Sin(angle) * radius * (1f + 0.15f * m));
		}

		/// <summary>A soaring bird's place at a time: circling in this episode's thermal, then gliding into the next one's.</summary>
		private static Vector3 SoarPosition(in AmbientGroup g, AmbientCreatureKind kind, int m, long episode, double start, float glide, double at, ref Frame f, out float bank)
		{
			Vector3 circling = Thermal(g, kind, m, episode, at, start, ref f, out float sense);
			float u = Mathf.Clamp01(((float)(at - start) - ((float)g.Episode - glide)) / glide);
			bank = 24f * sense * (1f - u);
			if (u <= 0f)
			{
				return circling;
			}
			// Out of the top of one thermal and a long straight glide down into the next.
			Vector3 entering = Thermal(g, kind, m, episode + 1, at, start + g.Episode, ref f, out _);
			return Vector3.Lerp(circling, entering, Mathf.SmoothStep(0f, 1f, u));
		}

		private static void Soar(in AmbientGroup g, AmbientCreatureKind kind, ref Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			long episode = EpisodeAt(t, g.Episode, Unit(Mix(g.Seed ^ 0x50u)), out double start);
			float glide = Mathf.Min(35f, (float)g.Episode * 0.3f);
			Rhythm(kind.Shape, out float period, out float duty);
			for (int m = 0; m < g.Count; m++)
			{
				uint own = MemberSeed(g.Seed, m);
				float size = g.Length * Mathf.Lerp(0.92f, 1.08f, Unit(Mix(own ^ 0x51u)));
				Vector3 now = SoarPosition(g, kind, m, episode, start, glide, t, ref f, out float roll);
				Vector3 soon = SoarPosition(g, kind, m, episode, start, glide, t + 0.3, ref f, out _);
				float flap = FlapStrength(t, period, duty, Unit(Mix(own ^ 0x52u)));
				float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, kind.StrokeHz), Unit(Mix(own ^ 0x53u)));
				Emit(into, kind, own, now, Flight((soon - now) / 0.3f, roll), size, new Vector4(phase, flap, 0f, 0f), f.Visible);
			}
		}

		// ── Bats ──────────────────────────────────────────────────────

		private static void Bats(in AmbientGroup g, AmbientCreatureKind kind, ref Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			Rhythm(kind.Shape, out float period, out float duty);
			for (int m = 0; m < g.Count; m++)
			{
				uint own = MemberSeed(g.Seed, m);
				// Half the colony works the tree line (round a crown), the rest the open air over home (and its water).
				Vector3 beat = g.Home + Disk(Mix(own ^ 0x70u), kind.Spread);
				float band = Mathf.Lerp(kind.Altitude.x, kind.Altitude.y, Unit(Mix(own ^ 0x71u)));
				if (f.Perches != null && f.Perches.Count > 0 && Unit(Mix(own ^ 0x72u)) < 0.5f)
				{
					Vector4 p = f.Perches[(int)(Unit(Mix(own ^ 0x73u)) * f.Perches.Count) % f.Perches.Count];
					float dx = p.x - g.Home.x, dz = p.z - g.Home.z;
					if (dx * dx + dz * dz < 60f * 60f)
					{
						beat = new Vector3(p.x, 0f, p.z);
						band = Mathf.Max(2f, p.w * 0.7f);
					}
				}
				float r = g.Radius * Mathf.Lerp(0.6f, 1f, Unit(Mix(own ^ 0x74u)));
				double w = kind.Speed / (r * 0.9);
				float p1 = Unit(Mix(own ^ 0x75u)) * 6.28f, p2 = Unit(Mix(own ^ 0x76u)) * 6.28f, p3 = Unit(Mix(own ^ 0x77u)) * 6.28f;
				Vector3 Position(double at)
				{
					// Three incommensurate turns make a path that never quite repeats; the fastest is the jinking after insects.
					float x = r * (0.6f * Mathf.Sin(Angle(at, w, p1)) + 0.25f * Mathf.Sin(Angle(at, w * 2.7, p2)) + 0.12f * Mathf.Sin(Angle(at, w * 6.1, p3)));
					float z = r * (0.6f * Mathf.Cos(Angle(at, w * 0.93, p2)) + 0.25f * Mathf.Cos(Angle(at, w * 2.3, p3)) + 0.12f * Mathf.Cos(Angle(at, w * 5.3, p1)));
					float y = band + 1.2f * Mathf.Sin(Angle(at, w * 1.3, p3)) + 0.5f * Mathf.Sin(Angle(at, w * 4.7, p1));
					return new Vector3(beat.x + x, y, beat.z + z);
				}
				Vector3 now = Position(t);
				Vector3 soon = Position(t + 0.05);
				Vector3 after = Position(t + 0.1);
				if (!Surface(f.World, now.x, now.z, out float surface, out _))
				{
					continue;
				}
				float lift = surface + 1.2f;
				now.y += surface;
				soon.y += surface;
				after.y += surface;
				if (now.y < lift)
				{
					float d = lift - now.y;
					now.y += d;
					soon.y += d;
					after.y += d;
				}
				Vector3 v = (soon - now) / 0.05f;
				Vector3 v2 = (after - soon) / 0.05f;
				float bank = Mathf.Clamp(Vector3.SignedAngle(new Vector3(v.x, 0f, v.z), new Vector3(v2.x, 0f, v2.z), Vector3.up) * 2f, -60f, 60f);
				float flap = FlapStrength(t, period, duty, Unit(Mix(own ^ 0x78u)));
				float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, kind.StrokeHz * Mathf.Lerp(0.85f, 1.15f, Unit(Mix(own ^ 0x79u)))), Unit(Mix(own ^ 0x7Au)));
				Emit(into, kind, own, now, Flight(v, bank), g.Length * Mathf.Lerp(0.9f, 1.1f, Unit(Mix(own ^ 0x7Bu))), new Vector4(phase, flap, 0f, 0f), f.Visible);
			}
		}

		// ── Ground animals ────────────────────────────────────────────

		/// <summary>How a walker moves between spots: whether it bounds (rabbits, squirrels, frogs) and how it dashes.</summary>
		private static bool Bounding(AmbientShape shape) => shape == AmbientShape.Rabbit || shape == AmbientShape.Squirrel || shape == AmbientShape.Frog;

		/// <summary>How many places a ground animal weighs for its next spot, keeping the most open (least grass).</summary>
		private const int SpotChoices = 4;

		/// <summary>
		/// A ground animal's spot in an episode: somewhere dry within its run of its own home spot (a crab's may be wet sand),
		/// the most open of a few: in 0.5 m grass a mouse or a rat is not seen at all, so they forage on paths, sand, rock
		/// and short turf where there is any (Jim, 2026-10-10). With no grass anywhere this is the first dry place, as before.
		/// </summary>
		private static Vector3 CritterSpot(in AmbientGroup g, AmbientCreatureKind kind, uint own, long episode, Vector3 home, IAmbientWorld world)
		{
			uint eh = EpisodeSeed(own, episode);
			bool found = false;
			Vector3 best = home;
			float bestCover = float.MaxValue;
			for (int attempt = 0; attempt < SpotChoices; attempt++)
			{
				Vector3 at = home + Disk(Mix(eh ^ (uint)attempt), g.Radius * (attempt < 2 ? 1f : 1.6f));
				if (world.TryGround(at.x, at.z, out float y) && !float.IsInfinity(y) && y > world.WaterAt(at.x, at.z, out _) - 0.02f)
				{
					float cover = world.CoverAt(at.x, at.z);
					if (!found || cover < bestCover - 0.02f)
					{
						found = true;
						bestCover = cover;
						best = new Vector3(at.x, y, at.z);
					}
				}
			}
			return best;
		}

		private static void Critter(in AmbientGroup g, AmbientCreatureKind kind, int m, ref Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			IAmbientWorld world = f.World;
			uint own = MemberSeed(g.Seed, m);
			Vector3 home = g.Home + Disk(Mix(own ^ 0x80u), kind.Spread);
			if (!world.TryGround(home.x, home.z, out float hy) || float.IsInfinity(hy))
			{
				return;
			}
			home.y = hy;
			float size = g.Length * Mathf.Lerp(0.85f, 1.15f, Unit(Mix(own ^ 0x81u)));
			bool crab = kind.Shape == AmbientShape.Crab;
			bool bounds = Bounding(kind.Shape);
			ref AmbientMemberState state = ref f.States[g.StateFirst + m];

			double length = g.Episode * Mathf.Lerp(0.75f, 1.25f, Unit(Mix(own ^ 0x82u)));
			long episode = EpisodeAt(t, length, Unit(Mix(own ^ 0x83u)), out double start);
			float tau = (float)(t - start), len = (float)length;
			Vector3 spot = CritterSpot(g, kind, own, episode, home, world);
			Vector3 next = CritterSpot(g, kind, own, episode + 1, home, world);
			float dash = kind.Shape == AmbientShape.Lizard ? 3f : 1f;
			float move = Mathf.Clamp(Vector3.Distance(spot, next) / (kind.Speed * dash), 0.3f, len * 0.6f);
			float gaitHz = kind.StrokeHz * (kind.Shape == AmbientShape.Lizard ? 1.6f : 1f);

			// Where it would be by its routine, and whether it is on the move.
			Vector3 at;
			Vector3 forward;
			float stride;
			if (tau > len - move)
			{
				float u = Mathf.Clamp01((tau - (len - move)) / move);
				// A rat scurries in two dashes with a freeze between; the rest go in one.
				float e = kind.Shape == AmbientShape.Rat ? (u < 0.5f ? Mathf.SmoothStep(0f, 0.5f, u * 2f) : 0.5f + Mathf.SmoothStep(0f, 0.5f, Mathf.Clamp01((u - 0.6f) / 0.4f)))
					: Mathf.SmoothStep(0f, 1f, u);
				at = Vector3.Lerp(spot, next, e);
				forward = next - spot;
				stride = kind.Shape == AmbientShape.Rat && u > 0.5f && u < 0.6f ? 0f : 1f;
			}
			else
			{
				at = spot;
				float yaw = Unit(EpisodeSeed(own ^ 0x84u, episode)) * 6.2831853f + 0.4f * Mathf.Sin(Angle(t, 0.15, Unit(own) * 6.28f));
				forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
				stride = 0f;
			}

			float visible = f.Visible;
			float alert = 0f;
			// A fright: bolt, then go to ground (a squirrel runs up its tree) until the coast has been clear a while.
			if (state.Refuge == AmbientMemberState.None && f.Threats != null && f.Threats.Frightens(kind, at, out Vector3 threat))
			{
				state.Refuge = kind.Shape == AmbientShape.Squirrel ? AmbientMemberState.ToTrunk : AmbientMemberState.Gone;
				state.At = t;
				state.Return = 0.0;
				state.From = at;
				state.Flee = AmbientGating.FleeDirection(at, threat, EpisodeSeed(own, (long)Math.Floor(t)));
				state.To = at + state.Flee * kind.FleeSpeed * 2f;
				state.Height = 0f;
				if (state.Refuge == AmbientMemberState.ToTrunk)
				{
					if (NearestTree(f.Perches, at, 15f, out Vector4 tree))
					{
						// Round to the far side of the trunk from the threat, as squirrels do, clear of the bark.
						state.To = new Vector3(tree.x, 0f, tree.z) + state.Flee * Mathf.Clamp(tree.w * 0.025f, 0.2f, 0.5f);
						state.Height = tree.w;
					}
					else
					{
						state.Refuge = AmbientMemberState.Gone;
					}
				}
			}
			if (state.Refuge != AmbientMemberState.None)
			{
				float away = (float)(t - state.At);
				float clear = AmbientGating.FlightDistance(kind, 0f) * 2.2f;
				bool coastClear = f.Threats == null || f.Threats.NearestSq(home) > clear * clear;
				if (state.Return <= 0.0 && away > HideSeconds && coastClear)
				{
					state.Return = t;
				}
				float back = state.Return > 0.0 ? (float)(t - state.Return) : -1f;
				if (back >= 2f * FadeSeconds)
				{
					state = default; // back to its routine
				}
				else if (back >= FadeSeconds)
				{
					// Reappearing at its routine spot.
					visible *= (back - FadeSeconds) / FadeSeconds;
				}
				else
				{
					float run = Vector3.Distance(state.From, state.To) / Mathf.Max(0.3f, kind.FleeSpeed);
					Vector3 pos;
					Vector3 dir = state.Flee;
					float runStride = 1f;
					Quaternion? climbing = null;
					if (state.Refuge == AmbientMemberState.ToTrunk)
					{
						// Across to the trunk, then straight up it to where the branches start.
						Vector3 foot = state.To;
						if (away < run)
						{
							pos = Vector3.Lerp(state.From, foot, away / run);
							dir = foot - state.From;
						}
						else
						{
							float climb = Mathf.Min(1f, (away - run) / 1.6f) * Mathf.Max(1.5f, state.Height * 0.45f);
							pos = foot;
							if (world.TryGround(foot.x, foot.z, out float fy) && !float.IsInfinity(fy))
							{
								pos.y = fy;
							}
							pos.y += climb;
							runStride = away - run < 1.6f ? 1f : 0f;
							// Head up the trunk, back to the world, belly to the bark.
							climbing = Quaternion.LookRotation(Vector3.up, new Vector3(state.Flee.x, 0f, state.Flee.z));
						}
					}
					else
					{
						float u = Mathf.Min(1f, away / run);
						pos = Vector3.Lerp(state.From, state.To, u);
						// A rabbit jinks as it runs.
						if (kind.Shape == AmbientShape.Rabbit)
						{
							pos += new Vector3(-dir.z, 0f, dir.x) * (0.6f * Mathf.Sin(away * 7f) * (1f - u));
						}
						if (away >= run)
						{
							float gone = (away - run) / FadeSeconds;
							if (gone >= 1f)
							{
								return; // gone to ground
							}
							visible *= 1f - gone;
						}
					}
					if (!climbing.HasValue)
					{
						if (!world.TryGround(pos.x, pos.z, out float py) || float.IsInfinity(py))
						{
							return;
						}
						pos.y = py;
					}
					if (back >= 0f)
					{
						visible *= 1f - back / FadeSeconds; // vanishing from where it hid
					}
					float fleePhase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, gaitHz * 1.8f), Unit(own));
					Quaternion r = climbing ?? Ground(world, pos, crab ? new Vector3(dir.z, 0f, -dir.x) : dir, size);
					Emit(into, kind, own, pos, r, size, new Vector4(fleePhase, runStride, bounds ? 1f : 0f, 0f), visible);
					return;
				}
			}

			// A squirrel spends some of its rests on a trunk a couple of metres up, where it is seen (Jim, 2026-10-10).
			if (kind.Shape == AmbientShape.Squirrel && stride <= 0f && Unit(EpisodeSeed(own ^ 0x88u, episode)) < TrunkRestShare
				&& NearestTree(f.Perches, home, 12f, out Vector4 trunk))
			{
				Vector3 round = Disk(Mix(EpisodeSeed(own ^ 0x89u, episode)), 1f);
				Vector3 outward = new Vector3(round.x, 0f, round.z).sqrMagnitude > 1e-6f ? new Vector3(round.x, 0f, round.z).normalized : Vector3.forward;
				Vector3 bark = new Vector3(trunk.x, 0f, trunk.z) + outward * Mathf.Clamp(trunk.w * 0.025f, 0.2f, 0.5f);
				if (world.TryGround(bark.x, bark.z, out float by) && !float.IsInfinity(by))
				{
					bark.y = by + Mathf.Lerp(1.2f, Mathf.Clamp(trunk.w * 0.3f, 1.5f, 4f), Unit(Mix(own ^ 0x8Au)));
					// Head up the trunk, belly to the bark, as when it flees up one.
					Quaternion cling = Quaternion.LookRotation(Vector3.up, outward);
					float sway = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, gaitHz * 0.35f), Unit(Mix(own ^ 0x87u)));
					Emit(into, kind, own, bark, cling, size, new Vector4(sway, 0f, 1f, 0f), visible);
					return;
				}
			}

			if (!world.TryGround(at.x, at.z, out float y) || float.IsInfinity(y))
			{
				return;
			}
			at.y = y;
			if (stride <= 0f)
			{
				// At rest: a rabbit sits up to look round, rats and mice rear to sniff the air, a lizard does push-ups, the
				// rest sniff about. Upright half the time now (was under a third): a creature standing up is the one seen.
				float c = SeaLifePlacement.Cycle(t, 5.0 + 4.0 * Unit(Mix(own ^ 0x85u)), Unit(Mix(own ^ 0x86u)));
				alert = c < 0.5f ? Mathf.Sin(c / 0.5f * Mathf.PI) : 0f;
			}
			float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, gaitHz * (stride > 0f ? 1f : 0.35f)), Unit(Mix(own ^ 0x87u)));
			Vector3 heading = crab ? new Vector3(forward.z, 0f, -forward.x) : forward;
			Quaternion rotation = Ground(world, at, heading, size);
			if (alert > 0f)
			{
				if (kind.Shape == AmbientShape.Rabbit)
				{
					rotation *= Quaternion.Euler(-38f * alert, 0f, 0f);
				}
				else if (kind.Shape == AmbientShape.Rat || kind.Shape == AmbientShape.Mouse)
				{
					rotation *= Quaternion.Euler(-30f * alert, 0f, 0f);
				}
			}
			Emit(into, kind, own, at, rotation, size, new Vector4(phase, stride, bounds ? 1f : 0f, alert), visible);
		}

		/// <summary>A walker's rotation: heading along the ground, its back tilted with the slope under it.</summary>
		private static Quaternion Ground(IAmbientWorld world, Vector3 at, Vector3 heading, float size)
		{
			heading.y = 0f;
			if (heading.sqrMagnitude < 1e-8f)
			{
				heading = Vector3.forward;
			}
			heading.Normalize();
			float step = Mathf.Max(0.15f, size * 0.6f);
			Vector3 up = Vector3.up;
			if (world.TryGround(at.x + step, at.z, out float hx) && world.TryGround(at.x, at.z + step, out float hz) && !float.IsInfinity(hx) && !float.IsInfinity(hz))
			{
				up = Vector3.Cross(new Vector3(0f, hz - at.y, step), new Vector3(step, hx - at.y, 0f)).normalized;
				up = Vector3.Slerp(Vector3.up, up, 0.8f);
			}
			Vector3 forward = Vector3.ProjectOnPlane(heading, up);
			return Facing(forward, up);
		}

		/// <summary>The tree nearest a place within reach.</summary>
		private static bool NearestTree(List<Vector4> perches, Vector3 at, float reach, out Vector4 tree)
		{
			tree = default;
			if (perches == null)
			{
				return false;
			}
			float best = reach * reach;
			bool any = false;
			foreach (Vector4 p in perches)
			{
				float dx = p.x - at.x, dz = p.z - at.z;
				float d = dx * dx + dz * dz;
				if (d < best)
				{
					best = d;
					tree = p;
					any = true;
				}
			}
			return any;
		}
	}
}
