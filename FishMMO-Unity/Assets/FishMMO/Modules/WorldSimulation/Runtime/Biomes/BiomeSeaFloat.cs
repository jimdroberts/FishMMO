using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// What drifts on a biome's open sea (<see cref="BiomeTemplate.SeaFloats"/>, 2026-10-25, phase I2p, decisions 42, 60, 68, 69, 80):
	/// sargassum windrows, by-the-wind sailors, drift logs crusted with goose barnacles, mangrove propagule rafts, a lost fishing
	/// corkline, growlers calved off the bergs, pumice rafts off a sea-floor eruption, pancake and brash ice. Append-only (stored by
	/// value). Drawn by the client on the GPU (SeaFloatSystem, FishMMO/Sea Floats) riding the sea's own wave sum, carried
	/// downwind on the shared clock; nothing is cut into the scene and the server never knows of them.
	/// </summary>
	/// <remarks>
	/// Until 2026-10-25 no sea carried anything; an empty list (every biome's default, and what the authoring writes until a biome's
	/// file names its own with <c>.FloatsAtSea(...)</c>) keeps it so, and draws nothing at all.
	/// </remarks>
	public enum BiomeSeaFloatKind : byte
	{
		/// <summary>Sargassum: golden-brown weed buoyed by its berry-like floats, gathered by the wind into long windrows 20–60 m apart.</summary>
		Sargassum = 0,
		/// <summary>By-the-wind sailors (Velella): small blue discs each carrying a stiff clear sail, scattered in drifts.</summary>
		Velella = 1,
		/// <summary>A drift log far from any shore, its end bearded with goose barnacles.</summary>
		GooseLog = 2,
		/// <summary>A raft of mangrove propagules: green pencil-like seedlings floating together off a mangrove coast.</summary>
		PropaguleRaft = 3,
		/// <summary>A lost corkline: a fishing net's head rope, its cork floats strung out over thirty metres of sea.</summary>
		Corkline = 4,
		/// <summary>A growler: a lump of glacier ice the size of a cart, most of it under the water. Cold seas only.</summary>
		Growler = 5,
		/// <summary>A pumice raft: grey froth-stone from a sea-floor eruption, drifting in streaks.</summary>
		PumiceRaft = 6,
		/// <summary>Pancake ice: round plates of new sea ice with raised rims where they jostle. Freezing seas only.</summary>
		PancakeIce = 7,
		/// <summary>Brash ice: the broken rubble of floes and bergs, chunks under two metres. Freezing seas only.</summary>
		BrashIce = 8,
		/// <summary>A kelp raft (2026-10-25, I2i-b): a detached bull-kelp mat of whip stipes, bulbs and blades drifting off a cool kelp coast.</summary>
		KelpRaft = 9,
	}

	/// <summary>One thing a biome's open sea carries and how thickly (<see cref="BiomeTemplate.SeaFloats"/>).</summary>
	[Serializable]
	public struct BiomeSeaFloat
	{
		[Tooltip("What drifts: sargassum windrows, by-the-wind sailors, barnacled drift logs, propagule rafts, a lost corkline, growlers, pumice rafts, pancake or brash ice, kelp rafts.")]
		public BiomeSeaFloatKind Kind;
		[Tooltip("How thickly, as a share of the kind's own density at sea (1 its natural density, 0 none). Kinds do not compete: each drifts on its own lattice.")]
		[Min(0f)] public float Weight;

		public BiomeSeaFloat(BiomeSeaFloatKind kind, float weight)
		{
			Kind = kind;
			Weight = weight;
		}

		/// <summary>The entry as the authoring marks and reports it: "Kind×weight".</summary>
		public override string ToString() => Kind + "×" + Weight.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>How one kind of float lies and drifts on the sea (<see cref="BiomeSeaFloats.Traits"/>).</summary>
	public struct BiomeSeaFloatTraits
	{
		/// <summary>The lattice's pitch along the drift (downwind) and across it, metres: at most one float per lattice point.</summary>
		public Vector2 Spacing;
		/// <summary>The chance a lattice point holds a float, at weight 1 and full patch cover.</summary>
		public float Chance;
		/// <summary>The float's size (its mesh's one-metre unit times this), metres, min..max.</summary>
		public Vector2 Size;
		/// <summary>Metres a second it drifts downwind (a constant share of the climatological wind: weed slow, a sailor faster).</summary>
		public float DriftMetresPerSecond;
		/// <summary>Windrows: one row to each band this many metres wide across the wind (0: no rows, spread over the sea).</summary>
		public float RowPitch;
		/// <summary>A windrow's half width, metres.</summary>
		public float RowHalfWidth;
		/// <summary>The patches it lies in: lattice points to a patch cell along and across (≥ 1).</summary>
		public int PatchCells;
		/// <summary>The share of the sea its patches cover (1: everywhere).</summary>
		public float PatchShare;
		/// <summary>Metres from the camera it is drawn to (the faraway small things are not drawn at all).</summary>
		public float DrawMetres;
		/// <summary>A rigid body that rides the sea tilted as one (a log, a berg): the wave sum at its middle. Else it bends to the sea vertex by vertex (a weed mat).</summary>
		public bool Rigid;
		/// <summary>Only on a sea this cold or colder (the scene's painted climate, −1 coldest .. 1 hottest; 0 = 0 °C); above 1 never gated.</summary>
		public float ColdestOnly;
		/// <summary>The least water under it, metres, and the least distance from the shore: a float is never drawn in the surf.</summary>
		public float MinDepth;
	}

	/// <summary>
	/// The sea-float rules that do not depend on a template asset (pure): each kind's lattice and drift, the drift split the GPU
	/// lattice runs on, and the hash and patch noise both sides share (FishSeaFloats.compute mirrors them line for line).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A pure function of the shared clock.</b> Every float has an identity: a point (k, j) of its kind's lattice in the frame of
	/// the scene's climatological wind (k along the drift, j across). At world time t it lies at u = k·s + D(t) along the drift, with
	/// D(t) = rate·t, and its look (whether it is there, its size, turn, jitter) is hashed from (k, j) alone. Two players see the same
	/// weed in the same place; a rejoin lands on it; nothing is integrated frame by frame.
	/// </para>
	/// <para>
	/// <b>No precision lost to a large clock.</b> D(t) is metres since the clock began: millions of them. The CPU splits it in double
	/// into whole lattice steps and a remainder under one pitch (<see cref="SplitDrift"/>); the GPU's lattice slots sit at
	/// (slot·s + remainder) and a slot's identity is slot − steps, an integer. Floats carry only scene-sized numbers, and as the
	/// remainder runs past a pitch the next step takes over with exactly the same positions: no pop.
	/// </para>
	/// </remarks>
	public static class BiomeSeaFloats
	{
		/// <summary>An empty list: no sea carries anything (every biome's default). A fresh list each call.</summary>
		public static List<BiomeSeaFloat> None() => new List<BiomeSeaFloat>();

		/// <summary>How many kinds there are (the enum's count): slots in the GPU's instance buffer.</summary>
		public const int KindCount = 10;

		/// <summary>The farthest any float is drawn, metres: inside the sea's own amplitude fade (_WaveFadeStart, 900 m by default), where the sea it rides is whole.</summary>
		public const float MaxDrawMetres = 800f;

		/// <summary>One degree Celsius on the painted climate's scale (0 = 0 °C, 1 = 33.1 °C).</summary>
		private const float Degree = 1f / 33.1f;

		/// <summary>How a kind lies and drifts.</summary>
		public static BiomeSeaFloatTraits Traits(BiomeSeaFloatKind kind)
		{
			switch (kind)
			{
				case BiomeSeaFloatKind.Sargassum:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(1.6f, 1.6f), Chance = 0.8f, Size = new Vector2(0.7f, 1.8f), DriftMetresPerSecond = 0.12f,
						RowPitch = 40f, RowHalfWidth = 2.2f, PatchCells = 18, PatchShare = 0.7f, DrawMetres = 450f, MinDepth = 3f, ColdestOnly = 2f };
				case BiomeSeaFloatKind.Velella:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(1.2f, 1.2f), Chance = 0.35f, Size = new Vector2(0.05f, 0.08f), DriftMetresPerSecond = 0.25f,
						PatchCells = 40, PatchShare = 0.35f, DrawMetres = 60f, MinDepth = 2f, ColdestOnly = 2f };
				case BiomeSeaFloatKind.GooseLog:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(70f, 70f), Chance = 0.25f, Size = new Vector2(2.5f, 7f), DriftMetresPerSecond = 0.05f,
						PatchCells = 1, PatchShare = 1f, DrawMetres = 650f, Rigid = true, MinDepth = 4f, ColdestOnly = 2f };
				case BiomeSeaFloatKind.PropaguleRaft:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(3f, 3f), Chance = 0.4f, Size = new Vector2(0.8f, 1.5f), DriftMetresPerSecond = 0.08f,
						PatchCells = 16, PatchShare = 0.3f, DrawMetres = 150f, MinDepth = 1f, ColdestOnly = 2f };
				case BiomeSeaFloatKind.Corkline:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(160f, 160f), Chance = 0.2f, Size = new Vector2(0.9f, 1.1f), DriftMetresPerSecond = 0.03f,
						PatchCells = 1, PatchShare = 1f, DrawMetres = 500f, MinDepth = 6f, ColdestOnly = 2f };
				case BiomeSeaFloatKind.Growler:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(45f, 45f), Chance = 0.3f, Size = new Vector2(2f, 6f), DriftMetresPerSecond = 0.06f,
						PatchCells = 6, PatchShare = 0.6f, DrawMetres = 750f, Rigid = true, MinDepth = 5f, ColdestOnly = 5f * Degree };
				case BiomeSeaFloatKind.PumiceRaft:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(1.2f, 1.2f), Chance = 0.75f, Size = new Vector2(0.5f, 1.4f), DriftMetresPerSecond = 0.15f,
						RowPitch = 60f, RowHalfWidth = 5f, PatchCells = 30, PatchShare = 0.6f, DrawMetres = 400f, MinDepth = 2f, ColdestOnly = 2f };
				case BiomeSeaFloatKind.PancakeIce:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(2.4f, 2.4f), Chance = 0.85f, Size = new Vector2(0.8f, 2.2f), DriftMetresPerSecond = 0.04f,
						PatchCells = 24, PatchShare = 0.8f, DrawMetres = 350f, Rigid = true, MinDepth = 1f, ColdestOnly = -1f * Degree };
				case BiomeSeaFloatKind.BrashIce:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(1.8f, 1.8f), Chance = 0.6f, Size = new Vector2(0.3f, 1.4f), DriftMetresPerSecond = 0.05f,
						PatchCells = 14, PatchShare = 0.55f, DrawMetres = 250f, Rigid = true, MinDepth = 1f, ColdestOnly = 1f * Degree };
				// Sparse mats 2–10 m on cool seas (the draft's t ≤ 0.3: about 10 °C), bending to the swell as a weed mat does.
				case BiomeSeaFloatKind.KelpRaft:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(55f, 55f), Chance = 0.3f, Size = new Vector2(2f, 10f), DriftMetresPerSecond = 0.07f,
						PatchCells = 4, PatchShare = 0.6f, DrawMetres = 600f, MinDepth = 6f, ColdestOnly = 0.3f };
				default:
					return new BiomeSeaFloatTraits { Spacing = new Vector2(10f, 10f), Chance = 0f, Size = Vector2.one, PatchCells = 1, PatchShare = 1f, ColdestOnly = 2f };
			}
		}

		/// <summary>Whether a kind may lie on a sea at this temperature (the painted climate's, −1 .. 1): the ice only where it is cold enough.</summary>
		public static bool LivesAt(BiomeSeaFloatKind kind, float temperature) => temperature <= Traits(kind).ColdestOnly;

		/// <summary>Whether any entry of a list floats anything (a kind with a weight over zero).</summary>
		public static bool Any(IReadOnlyList<BiomeSeaFloat> floats)
		{
			if (floats == null)
			{
				return false;
			}
			foreach (BiomeSeaFloat f in floats)
			{
				if (f.Weight > 0f && (int)f.Kind < KindCount)
				{
					return true;
				}
			}
			return false;
		}

		// ── The drift ─────────────────────────────────────────────────

		/// <summary>Metres a kind has drifted downwind by a moment of the shared clock: rate × seconds, in double.</summary>
		public static double Drifted(BiomeSeaFloatKind kind, double seconds) => Traits(kind).DriftMetresPerSecond * seconds;

		/// <summary>
		/// The drift split into whole lattice steps along the drift and a remainder in [0, pitch): drift = steps·pitch + remainder.
		/// The steps are carried as a wrapping 32-bit integer (a float's identity is its slot less them, modulo 2^32, so the wrap
		/// moves nothing); only the remainder, under one pitch, is a float.
		/// </summary>
		public static void SplitDrift(double drift, float pitch, out int steps, out float remainder)
		{
			double p = Math.Max(1e-3, pitch);
			double whole = Math.Floor(drift / p);
			double rest = drift - whole * p;
			// A remainder rounded up to the pitch is the next step's zero.
			if (rest >= p)
			{
				whole += 1.0;
				rest -= p;
			}
			if (rest < 0.0)
			{
				rest = 0.0;
			}
			unchecked
			{
				steps = (int)(long)whole;
			}
			remainder = (float)rest;
		}

		/// <summary>The unit vector across the drift (90° anticlockwise from it): the lattice's j axis.</summary>
		public static Vector2 Across(Vector2 drift) => new Vector2(-drift.y, drift.x);

		/// <summary>
		/// The lattice slot range (first slot and count, along the drift then across it) whose points can fall in a world square
		/// [x0, x0 + side) × [z0, z0 + side), for slots at (slot·spacing.x + remainder) along <paramref name="drift"/> and
		/// (slot·spacing.y) across it.
		/// </summary>
		public static void SlotRange(Vector2 drift, Vector2 spacing, float remainder, float x0, float z0, float side, out int firstU, out int firstV, out int countU, out int countV)
		{
			Vector2 across = Across(drift);
			float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
			for (int c = 0; c < 4; c++)
			{
				var corner = new Vector2(x0 + ((c & 1) != 0 ? side : 0f), z0 + ((c & 2) != 0 ? side : 0f));
				float u = Vector2.Dot(corner, drift), v = Vector2.Dot(corner, across);
				uMin = Mathf.Min(uMin, u);
				uMax = Mathf.Max(uMax, u);
				vMin = Mathf.Min(vMin, v);
				vMax = Mathf.Max(vMax, v);
			}
			firstU = Mathf.FloorToInt((uMin - remainder) / spacing.x);
			firstV = Mathf.FloorToInt(vMin / spacing.y);
			countU = Mathf.FloorToInt((uMax - remainder) / spacing.x) - firstU + 1;
			countV = Mathf.FloorToInt(vMax / spacing.y) - firstV + 1;
		}

		/// <summary>The world (x, z) of a lattice slot: slot·spacing + remainder along the drift, slot·spacing across.</summary>
		public static Vector2 SlotPosition(Vector2 drift, Vector2 spacing, float remainder, int slotU, int slotV)
		{
			float u = slotU * spacing.x + remainder;
			float v = slotV * spacing.y;
			return drift * u + Across(drift) * v;
		}

		/// <summary>The identity of the float in a slot: its along-drift index is the slot's less the steps drifted (wrapping).</summary>
		public static int IdentityAlong(int slotU, int steps)
		{
			unchecked
			{
				return slotU - steps;
			}
		}

		// ── Hash and noise (FishSeaFloats.compute: the same integer arithmetic) ──

		/// <summary>lowbias32.</summary>
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

		/// <summary>A float's own hash: from the scene, its kind and its identity (k, j).</summary>
		public static uint Hash(uint seed, int kind, int k, int j)
		{
			unchecked
			{
				return Mix(seed ^ Mix((uint)kind * 0x9E3779B1u ^ Mix((uint)k * 0x85EBCA6Bu ^ Mix((uint)j * 0xC2B2AE35u + 0x27D4EB2Fu))));
			}
		}

		/// <summary>0 .. 1 from a hash.</summary>
		public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

		/// <summary>Floor division for integers of either sign.</summary>
		public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

		/// <summary>
		/// Smooth value noise on the lattice of identities, 0 .. 1, one feature to <paramref name="cells"/> points along and across:
		/// integer cells, so it moves with the floats and never loses precision however far they have drifted.
		/// </summary>
		public static float Patch(uint seed, int kind, int k, int j, int cells)
		{
			cells = Math.Max(1, cells);
			int ck = FloorDiv(k, cells), cj = FloorDiv(j, cells);
			float fk = (k - ck * cells) / (float)cells, fj = (j - cj * cells) / (float)cells;
			fk = fk * fk * (3f - 2f * fk);
			fj = fj * fj * (3f - 2f * fj);
			uint s = seed ^ 0x5EAF1047u;
			float a = Unit(Hash(s, kind, ck, cj)), b = Unit(Hash(s, kind, ck + 1, cj));
			float c = Unit(Hash(s, kind, ck, cj + 1)), d = Unit(Hash(s, kind, ck + 1, cj + 1));
			return Mathf.Lerp(Mathf.Lerp(a, b, fk), Mathf.Lerp(c, d, fk), fj);
		}

		/// <summary>
		/// The share of a lattice point a float holds before its own roll (0 .. 1): its kind's chance × the entry's weight × the patch
		/// cover, and for a windrow kind only within its row's half width of the row's middle (the row's place in its band hashed per
		/// band, its width breathing along it).
		/// </summary>
		public static float Cover(in BiomeSeaFloatTraits traits, uint seed, int kind, int k, int j, float weight)
		{
			float patch = traits.PatchShare >= 1f ? 1f
				: Smooth01((Patch(seed, kind, k, j, traits.PatchCells) - (1f - traits.PatchShare)) / 0.12f + 0.5f);
			float cover = traits.Chance * weight * patch;
			if (traits.RowPitch > 0f)
			{
				int band = Math.Max(1, Mathf.RoundToInt(traits.RowPitch / traits.Spacing.y));
				int r = FloorDiv(j, band);
				float middle = (0.2f + 0.6f * Unit(Hash(seed ^ 0x0A0Bu, kind, 0, r))) * band;
				float off = Mathf.Abs((j - r * band) - middle) * traits.Spacing.y;
				float width = traits.RowHalfWidth * (0.5f + Patch(seed ^ 0x77u, kind, k, r, 12));
				cover *= Mathf.Clamp01((width - off) / Mathf.Max(0.3f, traits.Spacing.y) + 0.5f);
			}
			return Mathf.Clamp01(cover);
		}

		private static float Smooth01(float x)
		{
			x = Mathf.Clamp01(x);
			return x * x * (3f - 2f * x);
		}

		/// <summary>
		/// Whether the float of identity (k, j) is there, and where its middle lies, given only its identity and the drift in double:
		/// the reference the split lattice must equal (tests). Jitter within its lattice cell up to 0.35 of a pitch.
		/// </summary>
		public static bool Reference(BiomeSeaFloatKind kind, uint seed, int k, int j, float weight, Vector2 drift, double drifted, out Vector2 position)
		{
			BiomeSeaFloatTraits traits = Traits(kind);
			uint h = Hash(seed, (int)kind, k, j);
			bool there = Unit(h) < Cover(traits, seed, (int)kind, k, j, weight);
			double u = (double)k * traits.Spacing.x + drifted + (Unit(Mix(h ^ 0x1u)) - 0.5) * 0.7 * traits.Spacing.x;
			double v = (double)j * traits.Spacing.y + (Unit(Mix(h ^ 0x2u)) - 0.5) * 0.7 * traits.Spacing.y;
			Vector2 across = Across(drift);
			position = new Vector2((float)(drift.x * u + across.x * v), (float)(drift.y * u + across.y * v));
			return there;
		}

		/// <summary>
		/// The same float found as the GPU finds it: from its slot, the steps and the remainder (<see cref="SplitDrift"/>), in floats.
		/// </summary>
		public static bool FromSlot(BiomeSeaFloatKind kind, uint seed, int slotU, int j, float weight, Vector2 drift, int steps, float remainder, out int identity, out Vector2 position)
		{
			BiomeSeaFloatTraits traits = Traits(kind);
			identity = IdentityAlong(slotU, steps);
			uint h = Hash(seed, (int)kind, identity, j);
			bool there = Unit(h) < Cover(traits, seed, (int)kind, identity, j, weight);
			float u = slotU * traits.Spacing.x + remainder + (Unit(Mix(h ^ 0x1u)) - 0.5f) * 0.7f * traits.Spacing.x;
			float v = j * traits.Spacing.y + (Unit(Mix(h ^ 0x2u)) - 0.5f) * 0.7f * traits.Spacing.y;
			position = drift * u + Across(drift) * v;
			return there;
		}
	}
}
