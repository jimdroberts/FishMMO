#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>A rock material: a ground family's textures on a rock.</summary>
	public struct RockMaterialSpec
	{
		public string Name;
		public string GroundFamily;
	}

	/// <summary>A detail prefab: one detail plant or rock mesh with its material.</summary>
	public struct DetailSpec
	{
		public string Name;
		public DetailPlant Plant;
		/// <summary>Healthy and dry tint multipliers the material carries (Unity's detail convention).</summary>
		public Color Healthy, Dry;
		/// <summary>Sinks into deep snow (low plants), 0..1.</summary>
		public float SnowBury;
		/// <summary>
		/// Casts shadows. Off for grass, flowers, reeds and litter: thousands of thin blades in every
		/// shadow cascade cost more than the shading they add, and their shade is lost in the ground's
		/// own texture. On for the few plants tall and solid enough to throw a readable shadow.
		/// </summary>
		/// <remarks>Written to the prefab's renderer, which is what the instanced detail renderer reads.</remarks>
		public bool CastsShadows;
	}

	/// <summary>
	/// Every asset the procedural art generator makes, by its stable name and path.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Names are the contract with everything else: biome templates reference these assets, and a
	/// LOCAL override is a file of the same name (textures by file name under
	/// <c>Assets/LOCAL/Biomes/Textures/</c>, terrain layers under <c>Assets/LOCAL/Biomes/TerrainLayers/</c>).
	/// So they are plain, human-guessable words — <c>Ground_Grass_Albedo</c>, not a hash — and once
	/// referenced they do not change (a generated asset's GUID is derived from its path).
	/// </para>
	/// <para>
	/// Everything lives under <see cref="Root"/>, one folder per kind of asset, so it is obvious at
	/// a glance what is generated and what was made by hand.
	/// </para>
	/// <para>
	/// <b>Two kinds of generated file, neither committed.</b> The heavy ones — every texture and every
	/// mesh — are the <i>payload</i> (<see cref="PayloadRoot"/>); the small ones — terrain layers,
	/// materials, prefabs — are the <i>wrappers</i> biomes, scenes and terrain data point at. Since
	/// 2026-10-02 the whole of <see cref="Root"/> is gitignored build output: every file's GUID is a
	/// function of its path and every object ID a function of what the object is
	/// (<see cref="ProceduralArtPayload"/>, <see cref="ProceduralArtFileIds"/>), so each machine
	/// regenerates byte-for-byte what every other has, and committed references resolve everywhere.
	/// Paths are therefore part of the contract: renaming a generated asset re-addresses it.
	/// </para>
	/// </remarks>
	public static partial class ProceduralArtCatalogue
	{
		public const string Root = "Assets/Prefabs/Shared/Biomes/Generated";

		/// <summary>The gitignored payload: generated textures and meshes, and their ledger. Rebuilt per machine.</summary>
		public const string PayloadRoot = Root + "/Payload";
		public const string TexturesFolder = PayloadRoot + "/Textures";
		public const string MeshesFolder = PayloadRoot + "/Meshes";

		/// <summary>Wrappers: what everything else references. Generated and gitignored like the payload.</summary>
		public const string TerrainLayersFolder = Root + "/TerrainLayers";
		public const string MaterialsFolder = Root + "/Materials";
		public const string PrefabsFolder = Root + "/Prefabs";

		public const int GroundSize = 1024;
		public const int BarkSize = 512;
		public const int AtlasSize = 1024;
		public const int BillboardHeight = 512;

		/// <summary>The seed every generated asset derives its own from.</summary>
		public const int DefaultSeed = 20261002;

		public const string AtlasName = "Foliage_Atlas";

		// ── Paths ─────────────────────────────────────────────────────

		public static string GroundLayerName(string family) => "Ground_" + family;
		public static string GroundTexture(string family, string map) => $"{TexturesFolder}/Ground_{family}_{map}.png";
		public static string GroundLayerPath(string family) => $"{TerrainLayersFolder}/{GroundLayerName(family)}.terrainlayer";
		public static string BarkTexture(string family, string map) => $"{TexturesFolder}/Bark_{family}_{map}.png";
		public static string AtlasPath => $"{TexturesFolder}/{AtlasName}.png";
		public static string BillboardTexture(string tree) => $"{TexturesFolder}/Billboard_{tree}_Albedo.png";
		public static string MeshPath(string name) => $"{MeshesFolder}/{name}.asset";
		public static string MaterialPath(string name) => $"{MaterialsFolder}/{name}.mat";
		public static string PrefabPath(string name) => $"{PrefabsFolder}/{name}.prefab";

		public static string BarkMaterial(string family) => "Bark_" + family;
		public static string RockMaterial(string name) => "Rock_" + name;
		public static string LeavesMaterial(string tree) => "Leaves_" + tree;
		public static string BillboardMaterial(string tree) => "Billboard_" + tree;
		public static string BoulderMesh(string shape, int lod) => $"Boulder_{shape}_LOD{lod}";
		public const string SmallRocksMesh = "Rocks_Small";
		public const string PebblesMesh = "Pebbles";

		public static string DetailPrefab(string name) => "Detail_" + name;
		public static string TreePrefab(string name) => "Tree_" + name;
		public static string TreeDecorPrefab(string name) => "Tree_" + name + "_Decor";
		public static string BoulderPrefab(string material, string shape) => $"Boulder_{material}_{shape}";
		public static string SmallRocksPrefab(string material) => $"Detail_Rocks_{material}";
		public static string PebblesPrefab(string material) => $"Detail_Pebbles_{material}";

		// ── Rocks ─────────────────────────────────────────────────────

		public static readonly RockMaterialSpec[] RockMaterials =
		{
			new RockMaterialSpec { Name = "Grey", GroundFamily = Ground.Rock },
			new RockMaterialSpec { Name = "Sandstone", GroundFamily = Ground.Sandstone },
			new RockMaterialSpec { Name = "Basalt", GroundFamily = Ground.Basalt },
			new RockMaterialSpec { Name = "Limestone", GroundFamily = Ground.Limestone },
		};

		/// <summary>Boulder shapes, each built at three levels of detail for the tree channel.</summary>
		public static readonly RockShape[] BoulderShapes =
		{
			new RockShape { Name = "Round", Size = 1.6f, Proportions = new Vector3(1f, 0.75f, 0.9f), Lumpiness = 0.8f, Facets = 5, FacetDepth = 0.4f },
			new RockShape { Name = "Slab", Size = 2.2f, Proportions = new Vector3(1.2f, 0.42f, 0.9f), Lumpiness = 0.6f, Facets = 6, FacetDepth = 0.55f },
			new RockShape { Name = "Spire", Size = 1.4f, Proportions = new Vector3(0.75f, 1.6f, 0.7f), Lumpiness = 0.7f, Facets = 7, FacetDepth = 0.5f },
		};

		/// <summary>Grid cells per cube face at each boulder level: 1200, 300 and 48 triangles.</summary>
		public static readonly int[] BoulderResolution = { 10, 5, 2 };

		public static readonly RockShape SmallRock = new RockShape { Name = "SmallRock", Size = 0.35f, Proportions = new Vector3(1f, 0.6f, 0.85f), Lumpiness = 0.8f, Facets = 4, FacetDepth = 0.45f };
		public static readonly RockShape Pebble = new RockShape { Name = "Pebble", Size = 0.12f, Proportions = new Vector3(1f, 0.55f, 0.8f), Lumpiness = 0.5f, Facets = 2, FacetDepth = 0.3f };

		// ── Detail plants ─────────────────────────────────────────────

		private static Color H(string hex) => SurfaceCatalogue.Hex(hex);
		private static readonly Color HealthyTint = new Color(0.9f, 0.95f, 0.9f, 1f);
		private static readonly Color DryTint = new Color(0.75f, 0.7f, 0.55f, 1f);

		private static DetailSpec D(string name, DetailKind kind, int count, float height, float width, float radius, string a, string b,
			float lean = 0.3f, int segments = 3, Color[] accents = null, float snowBury = 0.8f, Color? healthy = null, Color? dry = null)
		{
			return new DetailSpec
			{
				Name = name,
				Plant = new DetailPlant
				{
					Name = name,
					Kind = kind,
					Count = count,
					Height = height,
					Width = width,
					Radius = radius,
					ColourA = H(a),
					ColourB = H(b),
					Accents = accents,
					Lean = lean,
					Segments = segments,
				},
				Healthy = healthy ?? HealthyTint,
				Dry = dry ?? DryTint,
				SnowBury = snowBury,
				CastsShadows = kind == DetailKind.Fern || kind == DetailKind.Shrub || kind == DetailKind.DryShrub || kind == DetailKind.BarrelCactus,
			};
		}

		public static readonly DetailSpec[] Details =
		{
			D("GrassLush", DetailKind.Grass, 18, 0.45f, 0.035f, 0.45f, "#4c7a2c", "#78a040"),
			D("GrassDry", DetailKind.Grass, 16, 0.5f, 0.03f, 0.45f, "#a08a4c", "#cdb672", healthy: new Color(1f, 0.97f, 0.9f), dry: new Color(0.85f, 0.78f, 0.62f)),
			D("GrassTall", DetailKind.Grass, 20, 0.9f, 0.035f, 0.4f, "#557f30", "#86a848", lean: 0.4f),
			D("GrassTuft", DetailKind.Grass, 10, 0.2f, 0.03f, 0.15f, "#6e7a3c", "#9a9a58", lean: 0.5f, segments: 2),
			D("Reeds", DetailKind.Reeds, 14, 1.4f, 0.025f, 0.2f, "#5e7034", "#8a9050", lean: 0.12f),
			D("Fern", DetailKind.Fern, 7, 0.6f, 0.28f, 0.03f, "#355e22", "#4f7a2e", segments: 4),
			D("FlowersMeadow", DetailKind.Flowers, 7, 0.35f, 0.07f, 0.35f, "#4c7a2c", "#669036", accents: new[] { H("#f4f2ea"), H("#f2d040"), H("#9b6fc8"), H("#e07a9a") }),
			D("FlowersWarm", DetailKind.Flowers, 6, 0.3f, 0.06f, 0.3f, "#6a7a36", "#8a9448", accents: new[] { H("#f0b030"), H("#e05a2a"), H("#f4e070") }),
			D("ShrubSmall", DetailKind.Shrub, 10, 0.6f, 0.45f, 0.4f, "#3f6526", "#5e8034", snowBury: 0.4f),
			D("ShrubDry", DetailKind.DryShrub, 8, 0.7f, 0.6f, 0.45f, "#6a5a44", "#8a7458", snowBury: 0.3f),
			D("DebrisForest", DetailKind.Debris, 10, 0f, 0.25f, 0.6f, "#6a4a2a", "#9a6a3a", snowBury: 1f),
			D("Kelp", DetailKind.Kelp, 4, 3f, 0.12f, 0.3f, "#4a5a22", "#6a6a2e", segments: 8, snowBury: 0f),
			D("Coral", DetailKind.Coral, 5, 0.5f, 0.04f, 0.2f, "#d06a5a", "#e8a060",
				accents: new[] { H("#d06a5a"), H("#e8a060"), H("#c070c0"), H("#6ac0c8"), H("#f0e080") }, snowBury: 0f),
			D("CactusBarrel", DetailKind.BarrelCactus, 1, 0.45f, 0f, 0.25f, "#3e7238", "#5a8a48", snowBury: 0f),
		};

		/// <summary>
		/// How far a detail plant is pushed into the ground, in metres, when no scatter rule in the spec
		/// scatters it: the spec's rules carry the sink (<see cref="BiomeArtSpec.Scatter.Sink"/>, which
		/// BiomeArtGenerator.DetailSinkRange reads first), and this is the fallback, applied as 0.5–1×
		/// of it per instance by the vegetation shader's <c>_GroundSink</c>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A terrain detail stands exactly on the interpolated height surface, and the heightmap between
		/// samples is a plane while the mesh's base is a ring of stems at one height: on any slope or
		/// bump half the base floats, and a fern reads as set down on the ground rather than growing out
		/// of it. Unity's detail layers have no per-instance height offset, so the shader translates the
		/// whole instance down — translates, never squashes, so blade proportions and the wind's pinned
		/// base are untouched.
		/// </para>
		/// <para>
		/// Sized to each kind's base: grass and tufts 0.03 (the blades root in a ~3 cm thatch), flowers
		/// 0.02 (thin stems, short plants — more would swallow them), ferns 0.06 and shrubs 0.07 (a
		/// crown of fronds or stems spreading from a woody base wider than a cell's height error), dry
		/// shrubs 0.06, reeds 0.05 (in mud or water), debris 0.02 (flat litter; just enough to bed it),
		/// kelp and coral 0.05 (on a seabed of silt), the barrel cactus 0.05.
		/// No slope term: the shader has no ground normal for an instance (a terrain detail's instance
		/// matrix carries only position, yaw and scale unless the prototype aligns to the ground, and
		/// then the plant already leans into the slope), and one taken from the plant's own up axis
		/// would sink every upright plant the same on flat and steep ground alike.
		/// </para>
		/// </remarks>
		public static float DetailSink(in DetailSpec spec)
		{
			switch (spec.Plant.Kind)
			{
				case DetailKind.Grass: return 0.03f;
				case DetailKind.Flowers: return 0.02f;
				case DetailKind.Fern: return 0.06f;
				case DetailKind.Shrub: return 0.07f;
				case DetailKind.DryShrub: return 0.06f;
				case DetailKind.Reeds: return 0.05f;
				case DetailKind.Debris: return 0.02f;
				case DetailKind.Kelp: return 0.05f;
				case DetailKind.Coral: return 0.05f;
				case DetailKind.BarrelCactus: return 0.05f;
				default: return 0.03f;
			}
		}

		/// <summary>The detail spec with this name.</summary>
		public static bool TryDetail(string name, out DetailSpec spec)
		{
			foreach (DetailSpec d in Details)
			{
				if (d.Name == name)
				{
					spec = d;
					return true;
				}
			}
			spec = default;
			return false;
		}

		// ── Trees ─────────────────────────────────────────────────────

		public static readonly TreeSpecies[] Trees =
		{
			new TreeSpecies { Name = "Spruce", Form = TreeForm.Conifer, Height = 14f, TrunkRadius = 0.22f, CrownWidth = 0.22f, CrownBase = 0.12f, Branches = 16,
				BarkFamily = Bark.Brown, LeafA = H("#24452a"), LeafB = H("#35593a"), LeafCell = FoliageCell.NeedleSpray, LeafSize = 1.2f },
			new TreeSpecies { Name = "Pine", Form = TreeForm.Conifer, Height = 16f, TrunkRadius = 0.25f, CrownWidth = 0.16f, CrownBase = 0.45f, Branches = 12,
				BarkFamily = Bark.Pine, LeafA = H("#2e5230"), LeafB = H("#46683a"), LeafCell = FoliageCell.NeedleSpray, LeafSize = 1.3f },
			new TreeSpecies { Name = "Oak", Form = TreeForm.Broadleaf, Height = 11f, TrunkRadius = 0.35f, CrownWidth = 0.45f, CrownBase = 0.32f, Branches = 6,
				BarkFamily = Bark.Brown, LeafA = H("#3d6426"), LeafB = H("#5a7e30"), LeafCell = FoliageCell.BroadLeaves, LeafSize = 1.6f, Deciduous = true },
			new TreeSpecies { Name = "Birch", Form = TreeForm.Broadleaf, Height = 12f, TrunkRadius = 0.16f, CrownWidth = 0.25f, CrownBase = 0.4f, Branches = 5,
				BarkFamily = Bark.Birch, LeafA = H("#5a8a30"), LeafB = H("#7aa040"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 1.1f, Deciduous = true },
			new TreeSpecies { Name = "Jungle", Form = TreeForm.Broadleaf, Height = 20f, TrunkRadius = 0.45f, CrownWidth = 0.35f, CrownBase = 0.6f, Branches = 7,
				BarkFamily = Bark.Brown, LeafA = H("#24501e"), LeafB = H("#3a6a26"), LeafCell = FoliageCell.BroadLeaves, LeafSize = 2f },
			new TreeSpecies { Name = "Dead", Form = TreeForm.Dead, Height = 9f, TrunkRadius = 0.25f, CrownWidth = 0.35f, CrownBase = 0.35f, Branches = 5,
				BarkFamily = Bark.Dead, LeafA = H("#6a5e4e"), LeafB = H("#8a7a66"), LeafCell = FoliageCell.Twigs, LeafSize = 1f },
			new TreeSpecies { Name = "Palm", Form = TreeForm.Palm, Height = 10f, TrunkRadius = 0.2f, CrownWidth = 0.35f, CrownBase = 0.9f, Branches = 10,
				BarkFamily = Bark.Palm, LeafA = H("#3e6a26"), LeafB = H("#5a8030"), LeafCell = FoliageCell.PalmFrond, LeafSize = 1f },
			new TreeSpecies { Name = "Saguaro", Form = TreeForm.Cactus, Height = 6f, TrunkRadius = 0.3f, CrownWidth = 0.2f, CrownBase = 0f, Branches = 2,
				BarkFamily = Bark.Cactus, LeafA = Color.white, LeafB = Color.white, LeafCell = FoliageCell.Solid, LeafSize = 0f },
			new TreeSpecies { Name = "Acacia", Form = TreeForm.Umbrella, Height = 6f, TrunkRadius = 0.18f, CrownWidth = 0.6f, CrownBase = 0.8f, Branches = 3,
				BarkFamily = Bark.Brown, LeafA = H("#55702a"), LeafB = H("#748a38"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 1.5f },
			new TreeSpecies { Name = "Bamboo", Form = TreeForm.Bamboo, Height = 9f, TrunkRadius = 0.05f, CrownWidth = 0.3f, CrownBase = 0.45f, Branches = 10,
				BarkFamily = Bark.Bamboo, LeafA = H("#4a7a2a"), LeafB = H("#6a9638"), LeafCell = FoliageCell.BambooLeaves, LeafSize = 0.9f },
		};

		public static bool TryTree(string name, out TreeSpecies species)
		{
			foreach (TreeSpecies t in Trees)
			{
				if (t.Name == name)
				{
					species = t;
					return true;
				}
			}
			species = default;
			return false;
		}

		/// <summary>Screen-relative heights at which a tree steps to its next level; the last one culls.</summary>
		/// <remarks>
		/// <para>
		/// For a 15 m tree under a 60° vertical field of view (screen share = h / (2·d·tan 30°)):
		/// LOD0 → LOD1 at 0.25 ≈ 52 m, LOD1 → the billboard at 0.08 ≈ 160 m, culled at 0.002 ≈ 4.3 km.
		/// The billboard used to start at 0.1 (≈130 m), close enough that its flatness showed; at 160 m a
		/// 15 m tree is ~90 px tall at 1080p, a little below the 512 px picture's own resolution.
		/// </para>
		/// <para>
		/// The cull height is set far past the terrain's tree distance on purpose: a terrain tree is
		/// dropped at <c>Terrain.treeDistance</c> (1.5 km by default) whatever its LODs say, which used to
		/// pop. The vegetation shader dissolves tree materials over a band ending just inside that
		/// distance instead (<c>_FishVegetationFade.zw</c>, set by the client's VegetationDistanceFade),
		/// so the LOD cull only has to stay out of its way. Every transition cross-fades (LODGroup
		/// CrossFade, animated), and every pass of the shader dithers on <c>LOD_FADE_CROSSFADE</c>.
		/// </para>
		/// </remarks>
		public static readonly float[] TreeLodHeights = { 0.25f, 0.08f, 0.002f };

		// ── What the generator writes ────────────────────────────────

		/// <summary>
		/// True for a detail spec whose material wears the cactus bark's textures rather than the foliage
		/// atlas (the barrel cactus).
		/// </summary>
		/// <remarks>
		/// Every detail has a material of its own, the barrel cactus included: it used to share the
		/// cactus TREE's bark material, but a detail fades out before its terrain patch is culled and a
		/// tree fades out at the tree distance (<c>_DistanceFade</c> on the vegetation shader), so one
		/// material cannot serve both.
		/// </remarks>
		public static bool WearsBark(in DetailSpec spec) => spec.Plant.Kind == DetailKind.BarrelCactus;

		/// <summary>True for a tree species with a leaf material (a cactus has none).</summary>
		public static bool HasLeaves(in TreeSpecies species) => species.Form != TreeForm.Cactus;

		/// <summary>
		/// Every payload file the generator writes, by project path: the gitignored textures and
		/// meshes. A path missing from disk is what makes the payload stale.
		/// </summary>
		public static IEnumerable<string> PayloadPaths()
		{
			foreach (SurfaceRecipe r in SurfaceCatalogue.GroundRecipes)
			{
				yield return GroundTexture(r.Name, "Albedo");
				yield return GroundTexture(r.Name, "Normal");
				yield return GroundTexture(r.Name, "Mask");
			}
			foreach (SurfaceRecipe r in SurfaceCatalogue.BarkRecipes)
			{
				yield return BarkTexture(r.Name, "Albedo");
				yield return BarkTexture(r.Name, "Normal");
			}
			yield return AtlasPath;
			foreach (RockShape shape in BoulderShapes)
			{
				for (int lod = 0; lod < BoulderResolution.Length; lod++)
				{
					yield return MeshPath(BoulderMesh(shape.Name, lod));
				}
			}
			yield return MeshPath(SmallRocksMesh);
			yield return MeshPath(PebblesMesh);
			foreach (DetailSpec d in Details)
			{
				yield return MeshPath(DetailPrefab(d.Name));
			}
			foreach (TreeSpecies t in Trees)
			{
				yield return BillboardTexture(t.Name);
				yield return MeshPath(TreePrefab(t.Name) + "_LOD0");
				yield return MeshPath(TreePrefab(t.Name) + "_LOD1");
				yield return MeshPath(TreePrefab(t.Name) + "_Billboard");
			}
			foreach (string path in RockPayloadPaths())
			{
				yield return path; // Rock formations, ice and cliff pieces (ProceduralArtCatalogue.Rocks.cs).
			}
		}

		/// <summary>Every wrapper the generator writes, by project path: terrain layers, materials and prefabs.</summary>
		public static IEnumerable<string> WrapperPaths()
		{
			foreach (SurfaceRecipe r in SurfaceCatalogue.GroundRecipes)
			{
				yield return GroundLayerPath(r.Name);
			}
			foreach (SurfaceRecipe r in SurfaceCatalogue.BarkRecipes)
			{
				yield return MaterialPath(BarkMaterial(r.Name));
			}
			foreach (RockMaterialSpec m in RockMaterials)
			{
				yield return MaterialPath(RockMaterial(m.Name));
			}
			foreach (DetailSpec d in Details)
			{
				yield return MaterialPath(DetailPrefab(d.Name));
			}
			foreach (TreeSpecies t in Trees)
			{
				if (HasLeaves(in t))
				{
					yield return MaterialPath(LeavesMaterial(t.Name));
				}
				yield return MaterialPath(BillboardMaterial(t.Name));
			}
			foreach (string material in RockMaterialNames())
			{
				yield return MaterialPath(material);
			}
			foreach (string prefab in AllPrefabNames())
			{
				yield return PrefabPath(prefab);
			}
		}

		/// <summary>Every prefab name the generator writes, for the spec table's checks.</summary>
		public static IEnumerable<string> AllPrefabNames()
		{
			foreach (DetailSpec d in Details)
			{
				yield return DetailPrefab(d.Name);
			}
			foreach (TreeSpecies t in Trees)
			{
				yield return TreePrefab(t.Name);
				yield return TreeDecorPrefab(t.Name);
			}
			foreach (RockMaterialSpec m in RockMaterials)
			{
				foreach (RockShape s in BoulderShapes)
				{
					yield return BoulderPrefab(m.Name, s.Name);
				}
				yield return SmallRocksPrefab(m.Name);
				yield return PebblesPrefab(m.Name);
			}
			foreach (string prefab in RockPrefabNames())
			{
				yield return prefab;
			}
		}
	}
}
#endif
