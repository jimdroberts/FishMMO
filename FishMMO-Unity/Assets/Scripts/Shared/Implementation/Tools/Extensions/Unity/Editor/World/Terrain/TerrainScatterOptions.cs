#if UNITY_EDITOR
using System;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How <see cref="TerrainScatter"/> samples, how much it may place, and how the terrain draws
	/// what it placed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Budgets are per tile</b>, and a generated tile is a kilometre square, so the defaults are
	/// read as "per square kilometre". They are safety valves, not art direction: a rule that hits
	/// one is reported, never silently thinned, and the fix is the rule's density.
	/// </para>
	/// <para>
	/// <b>The draw settings are the terrain's own defaults.</b> Quality presets can override every
	/// one of them through <c>QualitySettings.terrainQualityOverrides</c>, which is where a low-end
	/// machine should lose grass — not here, in data that is committed with the scene.
	/// </para>
	/// </remarks>
	public sealed class TerrainScatterOptions
	{
		/// <summary>
		/// The planet-normalised height (0–1) of a world position (x, y, z), which is what every
		/// rule's <c>heightRange</c> is authored against.
		/// </summary>
		/// <remarks>
		/// The coordinator passes the biome system's own normalisation, so a rule that keeps pines
		/// above 0.6 means the same altitude in every scene on the planet. Null falls back to the
		/// scene's own lowest-to-highest ground, which is reported: in that frame "high" means
		/// "high for this scene", and the same rule would put pines on a beach in a flat scene.
		/// </remarks>
		public Func<float, float, float, float> NormalizedHeight;

		/// <summary>
		/// The prefabs a rule places, given the palette entry that brought it in; null uses the rule's own
		/// <see cref="PrefabSpawnRule.prefabs"/>.
		/// </summary>
		/// <remarks>
		/// The seam a LOCAL scene's prefab overrides come through (<see cref="LocalArtScope.ScatterPrefabs"/>),
		/// which is null for every scene outside Assets/LOCAL, so committed terrain data only ever gets
		/// the rules' own prefabs as prototypes. The resolved array is used for everything: validation,
		/// prototypes, and the per-prefab noise seed (by index, so a swapped prefab keeps its slot's seed).
		/// </remarks>
		public Func<SceneTerrainPalette.Entry, PrefabSpawnRule, GameObject[]> Prefabs;

		/// <summary>How the detail map is read. Coverage by default; see remarks.</summary>
		/// <remarks>
		/// <para>
		/// <b>Coverage, not instance count.</b> In coverage mode a detail cell stores how much of
		/// its area is covered, and the number of instances drawn there is the prototype's density
		/// times the terrain's (and the quality preset's) density scale. That is the only mode in
		/// which grass can be thinned per machine without re-baking the scene, and the only one in
		/// which the detail resolution is a precision choice rather than a density one: halving it
		/// in instance-count mode quarters the grass. Instance count is kept selectable for
		/// comparison, and what is written adapts to it through <c>maxDetailScatterPerRes</c>.
		/// </para>
		/// </remarks>
		public DetailScatterMode DetailScatterMode = DetailScatterMode.CoverageMode;

		/// <summary>
		/// Detail map cells per side of a tile. Zero derives it from <see cref="DetailCellMetres"/>.
		/// Rounded up to a whole number of patches and clamped to Unity's 4048.
		/// </summary>
		public int DetailResolution = 0;

		/// <summary>Target size of one detail cell, in metres, when <see cref="DetailResolution"/> is zero.</summary>
		/// <remarks>A metre: fine enough that a path or a cliff foot reads in the grass, coarse enough that a 1 km tile's map is a megabyte per prototype.</remarks>
		public float DetailCellMetres = 1f;

		/// <summary>Detail cells per rendered patch side (Unity's 8–128).</summary>
		/// <remarks>
		/// <para>
		/// 16 cells of a metre (was 32 until 2026-10-02). Unity culls details a whole patch at a time at
		/// the draw distance, which read as grass popping in square chunks; the vegetation shader now
		/// dissolves details over a band that must end a patch diagonal inside the draw distance
		/// (the client's VegetationDistanceFade), so the patch size decides how much of the draw
		/// distance is drawn at full density: 120 − 16·√2 ≈ 97 m with 16 m patches, against
		/// 120 − 32·√2 ≈ 75 m with 32 m ones, which gave away almost half the detail area to the fade.
		/// </para>
		/// <para>
		/// The cost is draw calls: an instanced detail draw is per patch per prototype, and a 120 m
		/// circle holds ~177 patches of 16 m (≈90 in a typical frustum) against ~44 of 32 m — with the
		/// three to six detail prototypes a biome scatters, a few hundred small instanced draws instead
		/// of about a hundred, cheap on the desktop GPUs the client targets. 8 m patches would halve the
		/// diagonal again for four times the draws and buy only 11 m. Existing scenes keep their old
		/// patch size until they are repainted.
		/// </para>
		/// </remarks>
		public int DetailResolutionPerPatch = 16;

		/// <summary>
		/// Size of a tree sampling cell, in metres. One candidate position per cell, jittered inside
		/// it, so the cell is also the closest two trees of one rule can stand without spacing.
		/// </summary>
		/// <remarks>
		/// Deliberately independent of the alphamap resolution, so re-painting a scene at a finer
		/// splat does not move every tree.
		/// </remarks>
		public float TreeCellMetres = 2f;

		/// <summary>Most detail cells one tile may cover, summed over every detail rule.</summary>
		/// <remarks>
		/// A detail "spawn" is one covered cell, so at the default metre cells this is 60% of a 1 km
		/// tile's ground. What it costs to draw is set by the draw distance and density, not this.
		/// </remarks>
		public int MaxDetailSpawnsPerTile = 600_000;

		/// <summary>Most tree instances one tile may hold, summed over every tree rule.</summary>
		/// <remarks>One per 50 m² over a kilometre square: dense forest, well inside what the terrain's tree renderer draws.</remarks>
		public int MaxTreesPerTile = 20_000;

		/// <summary>
		/// The smallest share of a tile's budget any biome on it is given, however little of the
		/// tile it covers, so a sliver of forest is not starved by the meadow around it.
		/// </summary>
		public float MinimumBiomeBudgetShare = 0.05f;

		/// <summary>True to write the draw settings below onto every tile.</summary>
		public bool ApplyDrawSettings = true;

		/// <summary>Metres to which detail objects are drawn.</summary>
		public float DetailObjectDistance = 120f;

		/// <summary>The terrain's detail density scale, 0–1 (prototypes opt in with <c>useDensityScaling</c>).</summary>
		public float DetailObjectDensity = 1f;

		/// <summary>Metres to which trees are drawn at all.</summary>
		public float TreeDistance = 1500f;

		/// <summary>Metres beyond which trees with billboards draw as billboards.</summary>
		public float TreeBillboardDistance = 200f;

		/// <summary>Metres over which a tree cross-fades from mesh to billboard.</summary>
		public float TreeCrossFadeLength = 40f;

		/// <summary>
		/// True when the scene's low ground holds a sea at world y = 0: plants are then kept on their side
		/// of the water line (<see cref="LandFadeStartMetres"/> … <see cref="AquaticFadeStartMetres"/>).
		/// </summary>
		/// <remarks>
		/// The splat and biome gates alone do not do it. A biome without a submerged layer keeps its grass
		/// layer down into the sea, and the biome map's cells are far coarser than a beach, so grass and
		/// trees grew under water and down to the waterline; a submerged layer's channel, blended at the
		/// alphamap's resolution, carried seaweed up onto the sand.
		/// </remarks>
		public bool HasLiquidWater;

		/// <summary>
		/// Land plants (every rule not on a submerged layer, trees included) fade in from this height above
		/// mean sea level to <see cref="LandFullMetres"/>: above the highest tide (WaterEnvironment's
		/// MaximumTideMetres, 2.5 m) and its swash, where salt and waves keep a beach bare.
		/// </summary>
		public float LandFadeStartMetres = 2.5f;
		public float LandFullMetres = 4f;

		/// <summary>
		/// Aquatic plants (rules on a biome's submerged layer: seaweed, kelp, seagrass) fade in from this
		/// depth below mean sea level to <see cref="AquaticFullMetres"/>, the lowest tide: under water at
		/// any tide, never stranded on a beach.
		/// </summary>
		public float AquaticFadeStartMetres = -1f;
		public float AquaticFullMetres = -2.5f;
	}
}
#endif
