using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The kinds of small, dense cover the run-time cave cover scatters on a carved cave's rock (2026-10-20): the cave's
	/// equivalent of the surface's blade grass and detail scatter. Their order is the order of the art's submeshes
	/// (<see cref="CaveCoverTypes.SubmeshOf"/>) and of the bits of a cave's mask (<see cref="CaveCoverSite"/>): append only.
	/// </summary>
	public enum CaveCoverType : byte
	{
		// ── Floors ──
		/// <summary>Moss cushions where it is damp and some daylight reaches.</summary>
		MossCushion = 0,
		/// <summary>Luminous moss: low tufts whose tips glow, where the cave's glow gathers.</summary>
		GlowMoss = 1,
		/// <summary>A spread of pebbles where silt and grit settle (a mine's spoil).</summary>
		Gravel = 2,
		/// <summary>Fine grit and chips: the one cover feet do not wear away.</summary>
		Grit = 3,
		/// <summary>A clump of small pale mushrooms on rich, damp ground.</summary>
		Mushrooms = 4,
		/// <summary>Tiny luminous caps in tufts.</summary>
		GlowCaps = 5,
		/// <summary>Bone and skull fragments under the halls' roosts and lairs.</summary>
		Bones = 6,
		/// <summary>A crust of guano under roosts.</summary>
		Guano = 7,
		/// <summary>Cave pearls in a wet hollow's shallow cup.</summary>
		CavePearls = 8,
		/// <summary>A sheen of mud and standing water in a wet hollow.</summary>
		MudSheen = 9,
		/// <summary>Twigs, dead leaves and root ends blown in at a mouth.</summary>
		RootLitter = 10,
		/// <summary>Hoar frost and ice needles (ice caves, cold caves).</summary>
		FrostCrystals = 11,
		/// <summary>Black basalt grit and clinker (lava tubes).</summary>
		BasaltGrit = 12,
		/// <summary>Yellow sulphur and white mineral crusts (lava tubes).</summary>
		SulphurCrust = 13,
		/// <summary>Faint ember glints in the cracks of a lava tube's floor.</summary>
		EmberGlints = 14,
		// ── Walls ──
		/// <summary>Lichen and moss crusts where the daylight reaches.</summary>
		Lichen = 15,
		/// <summary>Tiny glowing fungus specks.</summary>
		GlowSpecks = 16,
		/// <summary>Rows of small bracket fungi standing out of a wall.</summary>
		BracketFungus = 17,
		/// <summary>Mineral crusts and glints that catch a lamp (a fantasy sparkle).</summary>
		MineralGlints = 18,
		/// <summary>A wet sheen of seeping water down a wall.</summary>
		DripSheen = 19,
		// ── Roofs ──
		/// <summary>Soda straws: thin hollow stalactites where water drips.</summary>
		SodaStraws = 20,
		/// <summary>Roots hanging through the roof near a mouth or under shallow cover.</summary>
		HangingRoots = 21,
		/// <summary>Glow-worm threads beaded with light.</summary>
		GlowThreads = 22,
		// ── Mines ──
		/// <summary>Timber offcuts and splinters.</summary>
		TimberOffcuts = 23,
		/// <summary>Iron nails, spikes and broken tool bits.</summary>
		IronBits = 24,
		/// <summary>Ore crumbs that glint.</summary>
		OreCrumbs = 25,
		/// <summary>Candle stubs left on the floor and ledges, still burning.</summary>
		CandleStubs = 26,
	}

	/// <summary>How a cover type stands on the rock (the compute builds its rotation by it).</summary>
	public enum CaveCoverMount : byte
	{
		/// <summary>Its +Y along the rock's outward normal, turned about it at random: crusts, moss, gravel.</summary>
		Surface = 0,
		/// <summary>Its +Y up (leaned a little toward the normal), turned at random: mushrooms, candles; on a roof a hanging thing whose mesh hangs below its origin.</summary>
		Upright = 1,
		/// <summary>Its +Y along the normal and its +Z down the slope: seeps and bracket rows on a wall.</summary>
		Downhill = 2,
	}

	/// <summary>Which of the cave's glow colours a type's glowing part takes (<see cref="CaveCoverSite"/>).</summary>
	public enum CaveCoverGlowSlot : byte
	{
		None = 0,
		/// <summary>The cave's leading fungus hue.</summary>
		Fungus = 1,
		/// <summary>The cave's accent fungus hue.</summary>
		Accent = 2,
		/// <summary>The cave's crystal hue.</summary>
		Crystal = 3,
		/// <summary>A candle's flame (fixed warm).</summary>
		Flame = 4,
		/// <summary>An ember's glow (fixed deep orange).</summary>
		Ember = 5,
	}

	/// <summary>What one point of a cave's rock is like to live on (the cave mesh's habitat channels), each 0..1.</summary>
	public struct CaveCoverHabitat
	{
		public float Wet, Daylight, Sediment, Trodden, Organic, Glow;

		public CaveCoverHabitat(float wet, float daylight, float sediment, float trodden, float organic, float glow)
		{
			Wet = wet;
			Daylight = daylight;
			Sediment = sediment;
			Trodden = trodden;
			Organic = organic;
			Glow = glow;
		}
	}

	/// <summary>One cover type: where it grows, how thick, how big and how it stands.</summary>
	public struct CaveCoverTypeInfo
	{
		public CaveCoverType Type;
		/// <summary>Instances per m² of rock at full habitat in the nearest ring (each instance is a clump or spread, not one item).</summary>
		public float Density;
		/// <summary>Weights on floors, walls and roofs (<see cref="CaveCoverMath.ClassWeights"/>).</summary>
		public Vector3 Class;
		/// <summary>The habitat response: saturate(Bias + Wet·x + Daylight·y + Sediment·z + Trodden·w + Organic·Rich.x + Glow·Rich.y).</summary>
		public float Bias;
		public Vector4 Gain;
		public Vector2 Rich;
		/// <summary>What is left on a fully trodden way (0 worn away, 1 untouched).</summary>
		public float TroddenKeep;
		/// <summary>Uniform scale range of an instance.</summary>
		public Vector2 Scale;
		public CaveCoverMount Mount;
		public CaveCoverGlowSlot Glow;
		/// <summary>Metres it is set into the rock (so a clump never floats over a hollow).</summary>
		public float Sink;
		/// <summary>Its distances (rings, levels, reach) as a share of the settings' (big things reach further, grit less).</summary>
		public float Reach;
		/// <summary>Its look in the shader: smoothness, how much the wet raises it, colour jitter, shimmer of its glow.</summary>
		public float Smoothness, WetGloss, Jitter, Shimmer;
	}

	/// <summary>
	/// The cave cover's types (<see cref="CaveCoverType"/>) and the forms of cave they belong to. The run-time renderer reads the
	/// table for its densities and looks; the art generator builds one clump mesh a type and level in this order
	/// (<see cref="SubmeshOf"/>); the cut's species lookup picks a cave's types by its form and biome.
	/// </summary>
	public static class CaveCoverTypes
	{
		/// <summary>Levels of detail a type is drawn at (the art's submeshes per type).</summary>
		public const int Levels = 3;

		/// <summary>How many types there are.</summary>
		public const int Count = 27;

		/// <summary>The art's submesh of a type at a level.</summary>
		public static int SubmeshOf(CaveCoverType type, int level) => (int)type * Levels + level;

		/// <summary>A type's bit in a cave's mask.</summary>
		public static uint Bit(CaveCoverType type) => 1u << (int)type;

		/// <summary>The natural limestone cave's own cover (Cave, Grotto, SeaCave): also the default where a cave carries no choice.</summary>
		public static readonly uint Natural = Mask(CaveCoverType.MossCushion, CaveCoverType.GlowMoss, CaveCoverType.Gravel, CaveCoverType.Grit,
			CaveCoverType.Mushrooms, CaveCoverType.GlowCaps, CaveCoverType.Bones, CaveCoverType.Guano, CaveCoverType.CavePearls, CaveCoverType.MudSheen,
			CaveCoverType.RootLitter, CaveCoverType.Lichen, CaveCoverType.GlowSpecks, CaveCoverType.BracketFungus, CaveCoverType.MineralGlints,
			CaveCoverType.DripSheen, CaveCoverType.SodaStraws, CaveCoverType.HangingRoots, CaveCoverType.GlowThreads);

		/// <summary>An ice cave's: frost and ice grit, sparkle, seeps, and at its mouth litter and lichen. Nothing lives deeper in.</summary>
		public static readonly uint Ice = Mask(CaveCoverType.FrostCrystals, CaveCoverType.Grit, CaveCoverType.MineralGlints, CaveCoverType.DripSheen,
			CaveCoverType.RootLitter, CaveCoverType.Lichen);

		/// <summary>A lava tube's: basalt grit, sulphur and mineral crusts, ember glints, sparkle; litter, lichen and a few bones by the mouth.</summary>
		public static readonly uint Lava = Mask(CaveCoverType.BasaltGrit, CaveCoverType.SulphurCrust, CaveCoverType.EmberGlints, CaveCoverType.MineralGlints,
			CaveCoverType.RootLitter, CaveCoverType.Lichen, CaveCoverType.Bones);

		/// <summary>A mine's: spoil and grit, timber, iron, ore crumbs and candle stubs; fungi on rotting timber, seeps, roots at the adit.</summary>
		public static readonly uint Mine = Mask(CaveCoverType.Gravel, CaveCoverType.Grit, CaveCoverType.TimberOffcuts, CaveCoverType.IronBits,
			CaveCoverType.OreCrumbs, CaveCoverType.CandleStubs, CaveCoverType.Mushrooms, CaveCoverType.GlowMoss, CaveCoverType.DripSheen,
			CaveCoverType.Bones, CaveCoverType.RootLitter, CaveCoverType.HangingRoots, CaveCoverType.MineralGlints);

		/// <summary>The types that live (fungi, moss, glow-worms): none in an ice cave or a lava tube.</summary>
		public static readonly uint Living = Mask(CaveCoverType.MossCushion, CaveCoverType.GlowMoss, CaveCoverType.Mushrooms, CaveCoverType.GlowCaps,
			CaveCoverType.GlowSpecks, CaveCoverType.BracketFungus, CaveCoverType.GlowThreads);

		public static uint Mask(params CaveCoverType[] types)
		{
			uint mask = 0u;
			foreach (CaveCoverType t in types)
			{
				mask |= Bit(t);
			}
			return mask;
		}

		/// <summary>A type's row.</summary>
		public static ref readonly CaveCoverTypeInfo Info(CaveCoverType type) => ref All[(int)type];

		private static CaveCoverTypeInfo Row(CaveCoverType type, float density, Vector3 cls, float bias, Vector4 gain, Vector2 rich, float troddenKeep,
			Vector2 scale, CaveCoverMount mount, CaveCoverGlowSlot glow, float sink, float reach, float smoothness, float wetGloss, float jitter, float shimmer)
			=> new CaveCoverTypeInfo
			{
				Type = type, Density = density, Class = cls, Bias = bias, Gain = gain, Rich = rich, TroddenKeep = troddenKeep, Scale = scale, Mount = mount,
				Glow = glow, Sink = sink, Reach = reach, Smoothness = smoothness, WetGloss = wetGloss, Jitter = jitter, Shimmer = shimmer,
			};

		// Gains: (wet, daylight, sediment, trodden), rich: (organic, glow). Class: (floor, wall, roof).
		/// <summary>Every type, in <see cref="CaveCoverType"/> order.</summary>
		public static readonly CaveCoverTypeInfo[] All =
		{
			Row(CaveCoverType.MossCushion,   1.2f,  new Vector3(1f, 0.5f, 0f),    -0.35f, new Vector4(0.9f, 1.1f, 0.1f, 0f),   new Vector2(0.3f, 0f),    0.1f,  new Vector2(0.7f, 1.3f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.03f, 1f,    0.15f, 0.35f, 0.18f, 0f),
			Row(CaveCoverType.GlowMoss,      1.5f,  new Vector3(1f, 0.6f, 0.2f),  -0.1f,  new Vector4(0.4f, -0.4f, 0f, 0f),   new Vector2(0f, 1.6f),    0.15f, new Vector2(0.7f, 1.3f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.Fungus,  0.03f, 1f,    0.2f,  0.3f,  0.12f, 0.35f),
			Row(CaveCoverType.Gravel,        1.0f,  new Vector3(1f, 0.1f, 0f),    0.15f,  new Vector4(-0.1f, 0f, 0.9f, 0f),   new Vector2(0f, 0f),      0.35f, new Vector2(0.7f, 1.4f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.02f, 1f,    0.2f,  0.45f, 0.12f, 0f),
			Row(CaveCoverType.Grit,          2.0f,  new Vector3(1f, 0.05f, 0f),   0.35f,  new Vector4(0f, 0f, 0.6f, 0.3f),    new Vector2(0f, 0f),      1f,    new Vector2(0.8f, 1.3f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.01f, 0.6f,  0.15f, 0.4f,  0.1f,  0f),
			Row(CaveCoverType.Mushrooms,     0.35f, new Vector3(1f, 0.1f, 0f),    -0.3f,  new Vector4(0.5f, -0.2f, 0f, 0f),   new Vector2(1f, 0.2f),    0.05f, new Vector2(0.7f, 1.5f),  CaveCoverMount.Upright,  CaveCoverGlowSlot.None,    0.02f, 1.2f,  0.3f,  0.3f,  0.2f,  0f),
			Row(CaveCoverType.GlowCaps,      0.6f,  new Vector3(1f, 0.25f, 0f),   -0.25f, new Vector4(0.3f, -0.3f, 0f, 0f),   new Vector2(0.4f, 1.4f),  0.05f, new Vector2(0.7f, 1.6f),  CaveCoverMount.Upright,  CaveCoverGlowSlot.Accent,  0.02f, 1.2f,  0.35f, 0.25f, 0.15f, 0.5f),
			Row(CaveCoverType.Bones,         0.08f, new Vector3(1f, 0f, 0f),      -0.15f, new Vector4(0f, 0f, 0.2f, 0f),      new Vector2(0.7f, 0f),    0.3f,  new Vector2(0.8f, 1.2f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.02f, 1.2f,  0.25f, 0.2f,  0.1f,  0f),
			Row(CaveCoverType.Guano,         0.8f,  new Vector3(1f, 0.1f, 0f),    -0.35f, new Vector4(0f, 0f, 0f, 0f),        new Vector2(1.2f, 0f),    0.2f,  new Vector2(0.8f, 1.5f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.01f, 1f,    0.1f,  0.3f,  0.1f,  0f),
			Row(CaveCoverType.CavePearls,    0.6f,  new Vector3(1f, 0f, 0f),      -0.6f,  new Vector4(0.8f, 0f, 0.6f, 0f),    new Vector2(0f, 0f),      0.1f,  new Vector2(0.7f, 1.3f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.02f, 0.8f,  0.75f, 0.2f,  0.08f, 0f),
			Row(CaveCoverType.MudSheen,      0.3f,  new Vector3(1f, 0f, 0f),      -0.55f, new Vector4(0.9f, 0f, 0.5f, 0f),    new Vector2(0f, 0f),      0.6f,  new Vector2(0.7f, 1.6f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0f,    1.4f,  0.85f, 0.1f,  0.08f, 0f),
			Row(CaveCoverType.RootLitter,    0.8f,  new Vector3(1f, 0.1f, 0f),    -0.25f, new Vector4(0f, 1.0f, 0f, 0f),      new Vector2(0.6f, 0f),    0.3f,  new Vector2(0.7f, 1.3f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.01f, 1f,    0.1f,  0.3f,  0.2f,  0f),
			Row(CaveCoverType.FrostCrystals, 1.2f,  new Vector3(1f, 0.6f, 0.4f),  0.4f,   new Vector4(0.3f, -0.2f, 0f, 0f),   new Vector2(0f, 0.3f),    0.2f,  new Vector2(0.7f, 1.4f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.Crystal, 0.01f, 1f,    0.85f, 0f,    0.06f, 0.25f),
			Row(CaveCoverType.BasaltGrit,    1.5f,  new Vector3(1f, 0.1f, 0f),    0.5f,   new Vector4(0f, 0f, 0.5f, 0f),      new Vector2(0f, 0f),      1f,    new Vector2(0.8f, 1.3f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.01f, 0.7f,  0.25f, 0.3f,  0.08f, 0f),
			Row(CaveCoverType.SulphurCrust,  0.6f,  new Vector3(0.6f, 1f, 0.3f),  -0.1f,  new Vector4(-0.3f, 0f, 0.3f, 0f),   new Vector2(0f, 0.6f),    0.2f,  new Vector2(0.7f, 1.5f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.01f, 1f,    0.3f,  0f,    0.15f, 0f),
			Row(CaveCoverType.EmberGlints,   0.5f,  new Vector3(1f, 0.5f, 0f),    -0.2f,  new Vector4(-0.5f, 0f, 0f, 0f),     new Vector2(0f, 1.2f),    0.3f,  new Vector2(0.7f, 1.4f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.Ember,   0.01f, 1f,    0.4f,  0f,    0.1f,  0.6f),
			Row(CaveCoverType.Lichen,        1.2f,  new Vector3(0.2f, 1f, 0.4f),  -0.1f,  new Vector4(0.2f, 1.3f, 0f, 0f),    new Vector2(0f, 0f),      1f,    new Vector2(0.7f, 1.5f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.005f, 1f,   0.1f,  0.3f,  0.2f,  0f),
			Row(CaveCoverType.GlowSpecks,    1.5f,  new Vector3(0.1f, 1f, 0.6f),  -0.1f,  new Vector4(0.2f, -0.3f, 0f, 0f),   new Vector2(0f, 1.5f),    1f,    new Vector2(0.7f, 1.4f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.Fungus,  0.005f, 0.9f, 0.3f,  0.2f,  0.1f,  0.6f),
			Row(CaveCoverType.BracketFungus, 0.25f, new Vector3(0f, 1f, 0f),      -0.3f,  new Vector4(0.3f, 0f, 0f, 0f),      new Vector2(0.6f, 0.8f),  1f,    new Vector2(0.7f, 1.4f),  CaveCoverMount.Downhill, CaveCoverGlowSlot.Accent,  0.03f, 1.2f,  0.3f,  0.3f,  0.15f, 0.4f),
			Row(CaveCoverType.MineralGlints, 0.8f,  new Vector3(0.2f, 1f, 0.5f),  0.15f,  new Vector4(0f, -0.1f, 0f, 0f),     new Vector2(0f, 0.5f),    1f,    new Vector2(0.7f, 1.4f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.Crystal, 0.005f, 1f,   0.9f,  0f,    0.08f, 1f),
			Row(CaveCoverType.DripSheen,     0.4f,  new Vector3(0f, 1f, 0.2f),    -0.4f,  new Vector4(1.2f, 0f, 0f, 0f),      new Vector2(0f, 0f),      1f,    new Vector2(0.7f, 1.5f),  CaveCoverMount.Downhill, CaveCoverGlowSlot.None,    0f,    1.4f,  0.9f,  0f,    0.05f, 0f),
			Row(CaveCoverType.SodaStraws,    1.0f,  new Vector3(0f, 0f, 1f),      -0.25f, new Vector4(1.0f, -0.2f, 0f, 0f),   new Vector2(0f, 0f),      1f,    new Vector2(0.7f, 1.5f),  CaveCoverMount.Upright,  CaveCoverGlowSlot.None,    0.02f, 1.2f,  0.7f,  0.2f,  0.08f, 0f),
			Row(CaveCoverType.HangingRoots,  0.6f,  new Vector3(0f, 0.15f, 1f),   -0.35f, new Vector4(0f, 1.2f, 0f, 0f),      new Vector2(0.5f, 0f),    1f,    new Vector2(0.7f, 1.5f),  CaveCoverMount.Upright,  CaveCoverGlowSlot.None,    0.03f, 1.3f,  0.15f, 0.3f,  0.15f, 0f),
			Row(CaveCoverType.GlowThreads,   0.8f,  new Vector3(0f, 0f, 1f),      -0.25f, new Vector4(0.3f, -0.3f, 0f, 0f),   new Vector2(0f, 1.2f),    1f,    new Vector2(0.7f, 1.6f),  CaveCoverMount.Upright,  CaveCoverGlowSlot.Fungus,  0.02f, 1.3f,  0.6f,  0f,    0.08f, 0.8f),
			Row(CaveCoverType.TimberOffcuts, 0.3f,  new Vector3(1f, 0f, 0f),      0.25f,  new Vector4(0f, 0f, 0f, 0.2f),      new Vector2(0.5f, 0f),    0.4f,  new Vector2(0.8f, 1.3f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.02f, 1.1f,  0.15f, 0.25f, 0.2f,  0f),
			Row(CaveCoverType.IronBits,      0.25f, new Vector3(1f, 0f, 0f),      0.3f,   new Vector4(0f, 0f, 0f, 0.3f),      new Vector2(0f, 0f),      0.6f,  new Vector2(0.8f, 1.3f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.01f, 0.8f,  0.45f, 0.2f,  0.12f, 0f),
			Row(CaveCoverType.OreCrumbs,     0.8f,  new Vector3(1f, 0.3f, 0f),    0.2f,   new Vector4(0f, 0f, 0.4f, 0f),      new Vector2(0f, 0f),      0.5f,  new Vector2(0.8f, 1.3f),  CaveCoverMount.Surface,  CaveCoverGlowSlot.None,    0.01f, 0.8f,  0.7f,  0.1f,  0.1f,  0f),
			Row(CaveCoverType.CandleStubs,   0.04f, new Vector3(1f, 0f, 0f),      0.4f,   new Vector4(0f, 0f, 0f, -0.2f),     new Vector2(0f, 0f),      0f,    new Vector2(0.8f, 1.3f),  CaveCoverMount.Upright,  CaveCoverGlowSlot.Flame,   0.01f, 1.4f,  0.3f,  0f,    0.1f,  0.7f),
		};
	}

	/// <summary>
	/// The cave cover's pure arithmetic: the hashes, the habitat a vertex carries, a type's density there, the rings and the
	/// levels. FishCaveCover.compute mirrors every function here line for line (change both together); the tests pin them.
	/// </summary>
	public static class CaveCoverMath
	{
		/// <summary>The habitat a mesh read with no habitat channels stands for: a modest damp, silted, faintly glowing cave.</summary>
		public static readonly CaveCoverHabitat Unknown = new CaveCoverHabitat(0.35f, 0f, 0.35f, 0f, 0.15f, 0.2f);

		/// <summary>The most candidates one triangle offers one type (FishCaveCover.compute COVER_MAX_PER_TRIANGLE).</summary>
		public const int MaxPerTriangle = 128;

		/// <summary>The share of the keep over which a thinned candidate dissolves (FishCaveCover.compute COVER_THIN_BAND).</summary>
		public const float FadeBand = 0.08f;

		// ── Hashes (the scatter's mix: FishDetailScatter.compute ScatterMix) ──

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

		/// <summary>A triangle's key: its world centroid to the centimetre, so the same rock gives the same cover on every client.</summary>
		public static uint TriangleKey(Vector3 centroid)
		{
			unchecked
			{
				int x = Mathf.FloorToInt(centroid.x * 100f), y = Mathf.FloorToInt(centroid.y * 100f), z = Mathf.FloorToInt(centroid.z * 100f);
				uint h = Mix((uint)x * 0x9E3779B1u ^ Mix((uint)y + 0x632BE5ABu));
				return Mix(h ^ Mix((uint)z * 0x85EBCA77u));
			}
		}

		/// <summary>A candidate's hash: the triangle, the type's salt, the candidate's index.</summary>
		public static uint Candidate(uint triangle, uint salt, uint k)
		{
			unchecked
			{
				return Mix(Mix(triangle ^ salt * 0xC2B2AE3Du) ^ (k + 1u) * 0x27D4EB2Fu);
			}
		}

		/// <summary>A unit value of a hash's stream, [0, 1).</summary>
		public static float Unit(uint hash, uint stream)
		{
			unchecked
			{
				return (Mix(hash ^ (stream + 1u) * 0x165667B1u) >> 8) * (1f / 16777216f);
			}
		}

		/// <summary>A type's salt.</summary>
		public static uint Salt(CaveCoverType type) => Mix(0xC0FE0000u + (uint)type);

		/// <summary>A point on a triangle from two units: barycentric (1 − u − v, u, v), folded into the triangle.</summary>
		public static Vector3 Barycentric(float u, float v)
		{
			if (u + v > 1f)
			{
				u = 1f - u;
				v = 1f - v;
			}
			return new Vector3(1f - u - v, u, v);
		}

		// ── Habitat ──

		/// <summary>Whether a vertex colour is the mesher's old plain white: no habitat written.</summary>
		public static bool IsUnknown(Color colour) => colour.r > 0.99f && colour.g > 0.99f && colour.b > 0.99f && colour.a > 0.99f;

		/// <summary>
		/// The habitat a vertex carries (COLOR = wet, daylight, sediment, trodden; TEXCOORD3 = organic, glow). An old mesh's white
		/// colour is <see cref="Unknown"/> (all six: neither half was written); a colour with no TEXCOORD3 has no organic or glow.
		/// </summary>
		public static CaveCoverHabitat HabitatOf(Color colour, Vector2 uv3, bool hasUv3)
		{
			if (IsUnknown(colour))
			{
				return Unknown;
			}
			return new CaveCoverHabitat(colour.r, colour.g, colour.b, colour.a, hasUv3 ? Mathf.Clamp01(uv3.x) : 0f, hasUv3 ? Mathf.Clamp01(uv3.y) : 0f);
		}

		/// <summary>
		/// How much floor, wall and roof a face is by its outward (into the void) normal's y: floor above ~0.55, roof below ~−0.5,
		/// smooth between; the three add to one.
		/// </summary>
		public static Vector3 ClassWeights(float ny)
		{
			float floor = SmoothStep(0.35f, 0.7f, ny);
			float roof = SmoothStep(-0.35f, -0.7f, ny);
			return new Vector3(floor, Mathf.Max(0f, 1f - floor - roof), roof);
		}

		/// <summary>Hermite smoothstep as HLSL's (edges may be reversed).</summary>
		public static float SmoothStep(float a, float b, float x)
		{
			float t = Mathf.Clamp01((x - a) / (b - a));
			return t * t * (3f - 2f * t);
		}

		/// <summary>A type's habitat weight at a point, 0..1 (before its class).</summary>
		public static float HabitatWeight(in CaveCoverTypeInfo t, in CaveCoverHabitat h)
		{
			float w = t.Bias + t.Gain.x * h.Wet + t.Gain.y * h.Daylight + t.Gain.z * h.Sediment + t.Gain.w * h.Trodden + t.Rich.x * h.Organic + t.Rich.y * h.Glow;
			w = Mathf.Clamp01(w);
			return w * Mathf.Lerp(1f, t.TroddenKeep, h.Trodden);
		}

		/// <summary>A type's instances per m² at a point in the nearest ring: its density × habitat × class × the scale.</summary>
		public static float Density(in CaveCoverTypeInfo t, in CaveCoverHabitat h, float normalY, float scale)
		{
			Vector3 c = ClassWeights(normalY);
			float cls = c.x * t.Class.x + c.y * t.Class.y + c.z * t.Class.z;
			return t.Density * HabitatWeight(in t, in h) * cls * scale;
		}

		// ── Distance ──

		/// <summary>
		/// The share kept at a distance: piecewise linear through the rings (metres, keep), whole before the first, nothing past the
		/// last. Rings must rise in metres. The same at every candidate of a triangle at one distance, and falling with distance, so
		/// the far rings' instances are a subset of the near ones'.
		/// </summary>
		public static float Keep(float d, Vector2[] rings, int count)
		{
			if (count <= 0 || d <= rings[0].x)
			{
				return count <= 0 ? 0f : rings[0].y;
			}
			for (int i = 1; i < count; i++)
			{
				if (d <= rings[i].x)
				{
					float t = (d - rings[i - 1].x) / Mathf.Max(1e-4f, rings[i].x - rings[i - 1].x);
					return Mathf.Lerp(rings[i - 1].y, rings[i].y, t);
				}
			}
			return 0f;
		}

		/// <summary>
		/// Whether candidate k (with its coin) of a triangle exists: the triangle offers n = area × density candidates, the whole
		/// ones always and the last by its coin, so the expected count is exactly n. Nothing here depends on the camera.
		/// </summary>
		public static bool Exists(float n, int k, float coin) => k + coin < n;

		/// <summary>
		/// The thinning with distance, as the detail scatter's (FishDetailScatter.compute ThinFade): a candidate whose hash is under
		/// the keep is drawn, fading out (dithered) over the last <see cref="FadeBand"/> of it; returns the share faded out, 1 gone.
		/// The same candidate drops at the same keep from every camera, so the far rings' instances are a subset of the near ones'.
		/// </summary>
		public static float ThinFade(float keep, float hash) => keep >= 1f ? 0f : Mathf.Clamp01(1f - (keep - hash) / FadeBand);

		/// <summary>The level of detail a candidate draws at: 0 within lod1 × reach, 1 within lod2 × reach, else 2.</summary>
		public static int LevelOf(float d, float reach, float lod1, float lod2) => d < lod1 * reach ? 0 : d < lod2 * reach ? 1 : 2;

		/// <summary>How many candidates a triangle offers a type: its area × density rounded up, capped at <see cref="MaxPerTriangle"/>.</summary>
		public static int Candidates(float area, float density) => Mathf.Min(MaxPerTriangle, Mathf.CeilToInt(Mathf.Max(0f, area * density)));
	}
}
