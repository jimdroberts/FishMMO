using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>Which Unity terrain channel a spawn rule writes to.</summary>
	/// <remarks>
	/// Collision follows the channel and the prefab, never a flag: detail layers have no colliders
	/// at all, which is what makes them cheap enough to scatter by the hundred thousand, and a tree
	/// instance collides exactly when its prefab carries a collider. A large prop that should be
	/// seen from far off but walked through is a tree-channel rule whose prefab has none.
	/// </remarks>
	public enum PrefabSpawnChannel
	{
		/// <summary>Grass, flowers, pebbles, twigs: instanced meshes, no colliders, drawn to the detail distance.</summary>
		DetailLayer = 0,
		/// <summary>Trees, boulders, large props: LOD'd, billboarded, colliding when the prefab has a collider.</summary>
		TreeInstance = 1,
	}

	/// <summary>How a detail-layer rule fills its ground.</summary>
	/// <remarks>
	/// <para>
	/// <b>Scattered</b> is point sampling: each detail cell either gets the rule or not, by a draw
	/// against <see cref="PrefabSpawnRule.densityPer100m2"/>. Right for things that are counted —
	/// flowers, stones, twigs, a fern here and there — and wrong for ground cover: at the 8–30 per
	/// 100 m² a lawn would need to stay affordable, five to twenty cells in a hundred get a tuft and
	/// the rest are bare, which reads as speckle, and the hard <see cref="PrefabSpawnRule.minTextureWeight"/>
	/// cut leaves a bald line wherever two textures blend.
	/// </para>
	/// <para>
	/// <b>Carpet</b> is continuous coverage: every cell of the rule's ground gets a share of the cell,
	/// rising smoothly with the texture's weight and swelling and thinning in world-space swathes, so
	/// grass is a turf with thicker and thinner patches rather than a scatter of tufts. Unity's
	/// coverage mode then draws as many instances in a cell as that share times the prototype's
	/// density (and the quality preset's density scale), so how much it costs to draw is a draw
	/// setting, not a property of the bake.
	/// </para>
	/// </remarks>
	public enum DetailPlacement
	{
		/// <summary>Point sampling by density: flowers, debris, stones.</summary>
		Scattered = 0,
		/// <summary>Continuous coverage: grass, moss, low ground cover.</summary>
		Carpet = 1,
	}

	/// <summary>
	/// Where and how densely a set of prefabs is scattered wherever its texture layer dominates.
	/// Field names match the WorldEditor asset layout so exported biome templates load unchanged.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Carpet coverage is its own field.</b> <see cref="carpetCoverage"/> is a 0–1 share of a
	/// detail cell rather than a reuse of <see cref="detailInstancesPerSpawn"/>: that field is a byte
	/// floored at 64 (a quarter cell) because it is what one accepted Scattered sample writes, and a
	/// sparse carpet — desert tufts — needs to go well below a quarter. Each field means one thing.
	/// </para>
	/// <para>
	/// <b>Ground sink.</b> <see cref="sinkRange"/> and <see cref="sinkSlopeFactor"/> push an instance
	/// into the ground so it reads as bedded rather than set down: a boulder resting on its lowest
	/// point floats everywhere else, and on a slope its downhill edge stands proud by the footprint
	/// radius times the slope's tangent. Tree instances are lowered by the scatter itself; a detail
	/// layer has no per-instance height in Unity, so for details the values are data the vegetation
	/// material reads (the generator writes them there), never applied by the scatter.
	/// </para>
	/// <para>
	/// <b>Groups.</b> Nothing in nature is scattered evenly: flowers come in patches, stones in
	/// piles, trees in groves. With <see cref="clusterMetres"/> set, a point-sampled rule's chance is
	/// multiplied by a world-space group field whose average is 1 — so the rule's density is still
	/// what it says, only gathered — and a tree's size leans larger toward a group's heart. A carpet
	/// ignores it; its swathes are <see cref="carpetClumpMetres"/>.
	/// </para>
	/// <para>
	/// <b>Forests.</b> Groups are tens of metres; woods are hundreds. A tree rule with
	/// <see cref="forestMetres"/> set stands in stands: a scene-wide field (one for every forest
	/// rule, whatever its biome) is cut at the rule's <see cref="forestCover"/>, so that share of
	/// the ground is wood with a feathered edge and the rest is clearing and open ground holding
	/// only <see cref="forestOpen"/>'s lone trees. Rules share the field, so a biome's species stand
	/// in the same woods and a sparse neighbour's copses are the hearts of a dense one's forest. The
	/// density is still the average; a forest rule's spacing is shared with every other forest rule
	/// and scales with each tree's crown, so species do not stand inside one another.
	/// </para>
	/// <para>
	/// <b>The spec fingerprint.</b> A rule the Biome Art authoring tool created carries a hidden hash
	/// of the values it wrote. While the rule still hashes to it, nobody has tuned it, and a later run
	/// may bring it up to date with the spec; once anything differs it is somebody's work and the tool
	/// never touches it again. A rule made by hand has no fingerprint at all.
	/// </para>
	/// </remarks>
	[Serializable]
	public class PrefabSpawnRule
	{
		[SerializeField]
		private string stableGuid = Guid.NewGuid().ToString("N");
		[Tooltip("Enable or disable this spawn rule without removing configuration.")]
		public bool enableSpawning = true;

		[Header("Identification")]
		public string ruleName = "Untitled Rule";
		[Tooltip("List of prefabs to spawn. One will be randomly selected per spawn location for variation.")]
		public GameObject[] prefabs = new GameObject[0];

		[Header("Spawn Channel")]
		[Tooltip("Detail layers: instanced, no colliders, faded out at the detail distance. Tree instances: LOD'd and billboarded, and they collide when the prefab has a collider.")]
		public PrefabSpawnChannel spawnChannel = PrefabSpawnChannel.DetailLayer;
		[Tooltip("Detail layers only. VertexLit draws the mesh as it is; Grass bends it in the terrain's wind.")]
		public DetailRenderMode detailRenderMode = DetailRenderMode.VertexLit;

		[Header("Detail Placement")]
		[Tooltip("Detail layers only. Scattered: each detail cell gets the rule or not by a density draw — flowers, stones, debris. Carpet: every cell of the rule's ground gets a continuous share of coverage that rises with the texture's weight and swells and thins in swathes — grass, moss, low cover. Tree instances are always scattered.")]
		public DetailPlacement detailPlacement = DetailPlacement.Scattered;
		[Tooltip("Carpet only. Share of a detail cell covered where everything favours the rule (its ground at full weight, its biome alone, the thickest part of a swathe). 1 = the whole cell. Unity draws coverage × the prototype's density, thinned by the quality preset's density scale.")]
		[Range(0f, 1f)] public float carpetCoverage = 0.85f;
		[Tooltip("Carpet only. Size in metres of the thicker and thinner swathes the coverage swells and thins in. Larger is broader, calmer patches; smaller is busier.")]
		[Range(2f, 200f)] public float carpetClumpMetres = 16f;
		[Tooltip("Carpet only. Coverage at the thinnest part of a swathe, as a share of the thickest. 1 = no swathes at all (even turf); 0.5 = thinning to half; 0 = bare between clumps.")]
		[Range(0f, 1f)] public float carpetClumpFloor = 0.45f;
		[Tooltip("Carpet only. Width of the soft edge around Min Texture Weight: coverage rises smoothly from none at minTextureWeight − width/2 (never below weight 0) to full at minTextureWeight + width/2, so grass thins across a texture blend instead of stopping at a line.")]
		[Range(0.01f, 1f)] public float carpetWeightRamp = 0.25f;

		[Header("Density & Limits")]
		[Tooltip("Scattered only. Expected prefab count per 100 square meters of dominant texture.")]
		[Range(0f, 50f)] public float densityPer100m2 = 0.5f;
		[Tooltip("Scattered only (a carpet is not counted per instance). Cap per terrain tile, on top of the tile's own budget. 0 = no cap of the rule's own — right for grass, which at one-metre detail cells covers far more than 10,000 cells of a tile; keep a cap for trees.")]
		[Min(0)] public int maxPerChunk = 10000;
		[Tooltip("Scattered only. Minimum spacing in meters between spawned prefabs. Forest rules: between two trees of average crown and width, kept from every forest rule's trees (the mean of the two spacings) and scaled per tree by its own crown and width.")]
		[Range(0f, 50f)] public float minSpacing = 2f;

		[Header("Texture Weight")]
		[Tooltip("Scattered: the target texture's alphamap weight must reach this for a cell to be considered. Carpet: the middle of the soft edge (see Carpet Weight Ramp).")]
		[Range(0f, 1f)] public float minTextureWeight = 0.45f;
		[Tooltip("Optional multiplier applied to the computed spawn probability (Scattered) or coverage (Carpet, capped at a full cell).")]
		[Range(0.1f, 5f)] public float spawnProbabilityMultiplier = 1f;

		[Header("Height Constraint")]
		public bool useHeightConstraint = false;
		public MinMaxRange heightRange = new MinMaxRange(0f, 1f);
		[Range(0.001f, 0.2f)] public float heightFalloff = 0.05f;

		[Header("Slope Constraint")]
		public bool useSlopeConstraint = false;
		public MinMaxRange slopeRange = new MinMaxRange(0f, 45f);
		[Range(1f, 20f)] public float slopeFalloff = 5f;

		[Header("Randomization")]
		[Tooltip("Enable independent width and height scaling for more natural tree variation.")]
		public bool useNonUniformScaling = false;
		[Tooltip("Random scale range applied uniformly to width and height (when non-uniform scaling is disabled).")]
		public Vector2 uniformScaleRange = new Vector2(1f, 1f);
		[Tooltip("Random scale range for tree width (only used when non-uniform scaling is enabled).")]
		public Vector2 widthScaleRange = new Vector2(0.8f, 1.2f);
		[Tooltip("Random scale range for tree height (only used when non-uniform scaling is enabled).")]
		public Vector2 heightScaleRange = new Vector2(0.8f, 1.2f);
		public Vector2 yRotationRange = new Vector2(0f, 360f);
		public bool alignToTerrainNormal = true;
		[Tooltip("Seed offset applied on top of the global prefab seed to keep results deterministic per rule.")]
		public int seedOffset = 0;

		[Header("Grouping")]
		[Tooltip("Scattered rules and trees only. Radius in metres of the groups instances gather in — a patch of flowers, a rock pile, a grove. 0 = no groups: an even scatter. The rule's density is the average over the ground, so groups move instances together rather than adding any.")]
		[Range(0f, 60f)] public float clusterMetres = 0f;
		[Tooltip("How many instances (detail cells, for a detail rule) an average group holds. Sets how far apart group centres stand from the rule's density, so a sparse rule still gathers into real groups rather than groups of one, and widens the group to fit that many at the rule's spacing. 0 = use Cluster Spacing instead.")]
		[Range(0f, 100f)] public float clusterSize = 0f;
		[Tooltip("Only when Cluster Size is 0. Distance between group centres, as a multiple of the group radius. Larger is fewer, denser groups with more bare ground between them.")]
		[Range(2f, 10f)] public float clusterSpacing = 4f;
		[Tooltip("Share of the density scattered evenly between groups, so a few strays stand apart. 1 = no groups at all.")]
		[Range(0f, 1f)] public float clusterBackground = 0.2f;
		[Tooltip("Trees only (Unity sizes details itself). How much larger instances stand toward a group's heart and smaller at its fringe, as a share of the scale range.")]
		[Range(0f, 1f)] public float clusterScaleBias = 0.5f;

		[Header("Forest")]
		[Tooltip("Trees only. Size in metres of the stands the rule's trees gather into — woods and copses with clearings between, from a scene-wide field every forest rule shares, so the species of a biome (and of its neighbours) stand in the same woods. 0 = no stands.")]
		[Range(0f, 4000f)] public float forestMetres = 0f;
		[Tooltip("Share of the rule's ground under stands. The rest is clearings and open ground, which only lone trees reach (Forest Open). The rule's density stays the average over all of it, so the stands are that much denser.")]
		[Range(0.01f, 1f)] public float forestCover = 0.6f;
		[Tooltip("Width of a stand's feathered edge, as a share of the field's spread: 0 is a hard line, 0.3 a woodland edge thinning over tens of metres, 1 a gradual fade.")]
		[Range(0f, 1f)] public float forestEdge = 0.3f;
		[Tooltip("Density in the open, between stands, as a share of the density inside one: the lone trees in a field.")]
		[Range(0f, 1f)] public float forestOpen = 0.05f;
		[Tooltip("How strongly each forest rule (and each species of a rule with several) gathers into its own patches inside the shared stands, instead of mixing tree by tree. 0 = evenly mixed.")]
		[Range(0f, 1f)] public float forestMix = 0.5f;
		[Tooltip("How much taller and narrower trees grow inside a stand, and shorter and broader in the open and at its edge, as a share of the scale ranges.")]
		[Range(0f, 1f)] public float forestScaleBias = 0.3f;

		[Header("Ground Sink")]
		[Tooltip("Metres each instance is pushed into the ground, drawn per instance between min and max, so it reads as bedded rather than set down. Trees: applied by the scatter. Details: carried to the vegetation material, which applies it (Unity has no per-instance detail height).")]
		public Vector2 sinkRange = Vector2.zero;
		[Tooltip("Extra sink on a slope, as a share of footprint radius × tan(slope): at 1 the downhill edge of the footprint meets the ground. The radius is the prefab's horizontal bounds times the instance's width scale. Slopes past 60° count as 60°.")]
		[Range(0f, 1f)] public float sinkSlopeFactor = 0f;

		[Header("Depth")]
		[Tooltip("Rules on a biome's submerged (lakebed) layer only. Metres below mean sea level the rule grows between: x the shallowest, y the deepest; 0 for either end leaves that end open. The rule thins out over the outer fifth of the range at each closed end, as light fades for kelp and seagrass. (0, 0) = no depth band; the shore gate still keeps it under water at every tide.")]
		public Vector2 depthRange = Vector2.zero;

		[SerializeField, HideInInspector]
		private string specFingerprint = string.Empty;

		[Header("Detail Rendering")]
		[Tooltip("Controls how widely detail prototypes spread noise across the terrain patch.")]
		[Range(0.05f, 5f)] public float detailNoiseSpread = 0.5f;
		[Tooltip("Tint applied when the detail patch is considered healthy.")]
		public Color detailHealthyColor = new Color(0.9f, 0.95f, 0.9f, 1f);
		[Tooltip("Tint applied when the detail patch is considered dry.")]
		public Color detailDryColor = new Color(0.75f, 0.7f, 0.55f, 1f);
		[Tooltip("Scattered only. How much of a detail cell an accepted sample covers, out of 255 (a full cell). Terrain detail density and quality settings thin it from there without a re-bake.")]
		[Range(64, 255)] public int detailInstancesPerSpawn = 64;

		public string StableGuid => stableGuid;

		/// <summary>
		/// The authoring tool's hash of the values it last wrote; empty for a rule made by hand.
		/// See the class remarks.
		/// </summary>
		public string SpecFingerprint
		{
			get => specFingerprint ?? string.Empty;
			set => specFingerprint = value ?? string.Empty;
		}

		/// <summary>True when the rule fills a detail layer as a continuous carpet; tree rules never do.</summary>
		public bool IsCarpet => spawnChannel == PrefabSpawnChannel.DetailLayer && detailPlacement == DetailPlacement.Carpet;

		/// <summary>True when a point-sampled rule gathers into groups (<see cref="clusterMetres"/>); a carpet has its own swathes instead.</summary>
		public bool IsClustered => !IsCarpet && clusterMetres > 0f && clusterBackground < 1f;

		/// <summary>True when a tree rule stands in woods and copses (<see cref="forestMetres"/>); detail rules never do.</summary>
		public bool IsForested => spawnChannel == PrefabSpawnChannel.TreeInstance && forestMetres > 0f;

		public bool HasValidPrefabs()
		{
			if (prefabs == null)
			{
				return false;
			}
			for (int i = 0; i < prefabs.Length; i++)
			{
				if (prefabs[i] != null)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>One of the rule's prefabs, drawn with the given RNG; null when it has none.</summary>
		public GameObject GetRandomPrefab(DeterministicRNG rng)
		{
			if (prefabs == null || prefabs.Length == 0)
			{
				return null;
			}
			var valid = new List<GameObject>(prefabs.Length);
			for (int i = 0; i < prefabs.Length; i++)
			{
				if (prefabs[i] != null)
				{
					valid.Add(prefabs[i]);
				}
			}
			if (valid.Count == 0)
			{
				return null;
			}
			return valid.Count == 1 ? valid[0] : valid[rng.Next(valid.Count)];
		}

		public void EnsureStableGuid()
		{
			if (string.IsNullOrEmpty(stableGuid))
			{
				stableGuid = Guid.NewGuid().ToString("N");
			}
		}
	}
}
