using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What lives on and in a cave's still water as every client draws it (2026-10-26, phase I2n-c; decision 86 (i) and its leeches,
	/// decision 97): marsh-gas BEADS rising in trails from the drowned muck and ringing out on the surface, and LEECHES swimming in it,
	/// toward whoever wades in. Pure; FishCaveWaterLife.shader mirrors it number for number (CaveWaterLife draws it).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The beads are the shared clock's.</b> A pool's vents are hashed from its surface's position (to the centimetre: every client
	/// loads the same cave-life spot), each releasing a burst of beads every <see cref="Periods"/> seconds (each period a whole number
	/// of cycles in the motion clock's <see cref="Wrap"/>, so a trail never jumps when it wraps), a burst kept or skipped by its own
	/// hash: every player sees the same bead break the surface at the same moment.
	/// </para>
	/// <para>
	/// <b>The leeches are each viewer's.</b> At rest a leech's slow circuit of its pool is the clock's as the beads are; how far it has
	/// turned toward a wader is a presentation each client eases on its own (<see cref="Attract"/>): nothing on the wire, no damage,
	/// nothing anyone else sees moves (decision 97: viewer-reactive effects only as per-viewer presentation).
	/// </para>
	/// </remarks>
	public static class SceneCaveWater
	{
		/// <summary>The motion clock's wrap, seconds (every rate here a whole number of cycles in it).</summary>
		public const float Wrap = 10000f;
		/// <summary>A vent's burst periods, seconds (each divides <see cref="Wrap"/>; each longer than a burst's life at the deepest pool).</summary>
		public static readonly float[] Periods = { 12.5f, 16f, 20f, 25f, 40f, 50f };
		/// <summary>The most vents a pool has, beads in a burst, leeches in a pool.</summary>
		public const int MaxVents = 6, BeadsPerBurst = 4, MaxLeeches = 6;
		/// <summary>Seconds between a burst's beads, metres a second a bead rises at least (and up to <see cref="BeadSpeedSpread"/> more).</summary>
		public const float BeadSpacing = 0.35f, BeadSpeed = 0.24f, BeadSpeedSpread = 0.1f;
		/// <summary>How long a bead's ring shows once it breaks the surface, and how fast it spreads (s, m/s).</summary>
		public const float RingSeconds = 1.2f, RingSpeed = 0.3f;
		/// <summary>The deepest water a bead rises through, metres (a pool's depth is clamped to it; a burst's life stays under its period).</summary>
		public const float MostDepth = 2f;

		// ── Hashes (the shader's, bit for bit) ───────────────────────

		/// <summary>lowbias32.</summary>
		public static uint Mix(uint x)
		{
			unchecked
			{
				x ^= x >> 16;
				x *= 0x7FEB352Du;
				x ^= x >> 15;
				x *= 0x846CA68Bu;
				x ^= x >> 16;
				return x;
			}
		}

		public static uint Hash(uint seed, uint a, uint b) => Mix(unchecked(seed ^ Mix(unchecked(a * 0x9E3779B1u ^ (b + 0x632BE5ABu)))));

		/// <summary>A hash's 0..1 (its top 24 bits: exact in a float, as the shader's).</summary>
		public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

		/// <summary>A pool's seed from its surface's position (to the centimetre).</summary>
		public static uint SeedOf(Vector3 surface)
		{
			unchecked
			{
				uint x = (uint)Mathf.RoundToInt(surface.x * 100f), y = (uint)Mathf.RoundToInt(surface.y * 100f), z = (uint)Mathf.RoundToInt(surface.z * 100f);
				return Mix(x * 0x8DA6B343u ^ Mix(z * 0xD8163841u ^ Mix(y * 0xCB1AB31Fu)));
			}
		}

		// ── How many ─────────────────────────────────────────────────

		/// <summary>A pool's vents at a biome's bead weight: about one a 3 m² of water, at least one where it beads, at most <see cref="MaxVents"/>.</summary>
		public static int VentsOf(float bubbles, float radius) => !(bubbles > 0f) ? 0 : Mathf.Clamp(Mathf.RoundToInt(bubbles * Mathf.PI * radius * radius / 3f), 1, MaxVents);

		/// <summary>A pool's leeches at a biome's leech weight: about one a 2 m² of water, at least one where any swim, at most <see cref="MaxLeeches"/>.</summary>
		public static int LeechesOf(float leeches, float radius) => !(leeches > 0f) ? 0 : Mathf.Clamp(Mathf.RoundToInt(leeches * Mathf.PI * radius * radius / 2f), 1, MaxLeeches);

		// ── The beads ────────────────────────────────────────────────

		/// <summary>Where a vent stands in its pool, as an offset from the surface's middle (within 0.85 of its radius).</summary>
		public static Vector2 VentOffset(uint seed, int vent, float radius)
		{
			float a = 6.2831853f * Unit(Hash(seed, (uint)vent, 1u));
			float r = 0.85f * radius * Mathf.Sqrt(Unit(Hash(seed, (uint)vent, 2u)));
			return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
		}

		/// <summary>A vent's burst period, seconds (one of <see cref="Periods"/>).</summary>
		public static float PeriodOf(uint seed, int vent) => Periods[(int)(Hash(seed, (uint)vent, 3u) % (uint)Periods.Length)];

		/// <summary>
		/// One bead of a vent at a moment of the wrapped clock <paramref name="t"/> (0..<see cref="Wrap"/>): false when none is out
		/// (between bursts, a skipped burst, or its ring gone); else how far over the bed it has risen (<paramref name="rise"/>, metres,
		/// at most <paramref name="depth"/>) and, once it has broken the surface, its ring's age (<paramref name="ringAge"/>, seconds; −1
		/// while it rises). A burst is kept at the pool's bead weight (<paramref name="bubbles"/>, at most every burst).
		/// </summary>
		public static bool Bead(float t, uint seed, int vent, int bead, float depth, float bubbles, out float rise, out float ringAge)
		{
			rise = 0f;
			ringAge = -1f;
			float period = PeriodOf(seed, vent);
			float cycle = Mathf.Floor(t / period);
			float into = t - cycle * period;
			uint burst = Hash(seed, (uint)vent * 64u + 7u, (uint)cycle);
			if (Unit(burst) >= Mathf.Clamp01(0.35f + 0.5f * bubbles))
			{
				return false;
			}
			float d = Mathf.Clamp(depth, 0.05f, MostDepth);
			float speed = BeadSpeed + BeadSpeedSpread * Unit(Hash(burst, (uint)bead, 4u));
			float age = into - (bead * BeadSpacing + 0.2f * Unit(Hash(burst, (uint)bead, 5u)));
			if (age < 0f)
			{
				return false;
			}
			float up = d / speed;
			if (age < up)
			{
				rise = age * speed;
				return true;
			}
			ringAge = age - up;
			rise = d;
			return ringAge < RingSeconds;
		}

		// ── The leeches ──────────────────────────────────────────────

		/// <summary>
		/// A leech at rest at a moment of the wrapped clock: a slow circuit of its pool (a whole number of turns in <see cref="Wrap"/>, 30–90
		/// a wrap, either way round), at its own share of the radius and its own depth in the water, a little rise and fall on it.
		/// </summary>
		public static Vector3 LeechHome(float t, uint seed, int leech, Vector3 surface, float radius, float depth)
		{
			uint h = Hash(seed, (uint)leech + 101u, 11u);
			float turns = 30f + Mathf.Floor(61f * Unit(Hash(h, 1u, 0u)));
			float sense = Unit(Hash(h, 2u, 0u)) < 0.5f ? -1f : 1f;
			float phase = Unit(Hash(h, 3u, 0u));
			float a = 6.2831853f * (phase + sense * turns * (t / Wrap));
			float r = radius * (0.3f + 0.55f * Unit(Hash(h, 4u, 0u)));
			float d = Mathf.Clamp(depth, 0.1f, MostDepth);
			float y = surface.y - d * (0.35f + 0.45f * Unit(Hash(h, 5u, 0u))) + 0.04f * Mathf.Sin(6.2831853f * (t / 40f + phase));
			return new Vector3(surface.x + Mathf.Cos(a) * r, y, surface.z + Mathf.Sin(a) * r);
		}

		/// <summary>
		/// Where a drawn leech is: from its home (<see cref="LeechHome"/>) toward its place about a wader's feet as the viewer's pull
		/// <paramref name="attract"/> (0..1, <see cref="Attract"/>) grows, each leech late by its own share (the near ones first).
		/// </summary>
		public static Vector3 LeechAt(Vector3 home, Vector3 feet, float attract, uint seed, int leech, Vector3 surface, float depth)
		{
			uint h = Hash(seed, (uint)leech + 101u, 12u);
			float late = 0.35f * Unit(h);
			float pull = Mathf.Clamp01((attract - late) / 0.5f);
			pull = pull * pull * (3f - 2f * pull);
			float a = 6.2831853f * Unit(Hash(h, 1u, 0u)), r = 0.1f + 0.18f * Unit(Hash(h, 2u, 0u));
			float bed = surface.y - Mathf.Clamp(depth, 0.1f, MostDepth);
			float y = Mathf.Clamp(feet.y + 0.08f + 0.3f * Unit(Hash(h, 3u, 0u)), bed + 0.03f, surface.y - 0.04f);
			var target = new Vector3(feet.x + Mathf.Cos(a) * r, y, feet.z + Mathf.Sin(a) * r);
			return Vector3.Lerp(home, target, pull);
		}

		/// <summary>The seconds a viewer's pull takes to come in full while someone wades, and to die away after.</summary>
		public const float AttractSeconds = 6f, ReleaseSeconds = 3f;

		/// <summary>A viewer's pull a frame on (its own presentation, eased: in over <see cref="AttractSeconds"/>, out over <see cref="ReleaseSeconds"/>).</summary>
		public static float Attract(float previous, bool wading, float dt)
			=> Mathf.Clamp01(previous + (wading ? dt / AttractSeconds : -dt / ReleaseSeconds));

		/// <summary>Whether feet stand in a pool: within its rim (and a stride past it) and under its surface, over its bed.</summary>
		public static bool Wades(Vector3 feet, Vector3 surface, float radius, float depth)
		{
			float dx = feet.x - surface.x, dz = feet.z - surface.z, reach = radius + 0.75f;
			float bed = surface.y - Mathf.Clamp(depth, 0.1f, MostDepth);
			return dx * dx + dz * dz <= reach * reach && feet.y < surface.y - 0.05f && feet.y > bed - 0.6f;
		}
	}
}
