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

		// ── Optional, for details added from 2026-10-10 on ──
		// The spec table's helpers (BiomeArtSpec.Plants, PlantSink) know the legacy details by name; a detail added
		// since carries its own values here instead, and the helpers read them only for names absent from their
		// tables, so every legacy rule writes exactly what it did (its fingerprint unchanged). Zero = not given.

		/// <summary>Group radius in metres the spec gathers this detail into (BiomeArtSpec.Plants' "metres"); 0 = the helper's default.</summary>
		public float GroupMetres;
		/// <summary>Cells an average group holds (BiomeArtSpec.Plants' "size"); 0 = the helper's default.</summary>
		public float GroupSize;
		/// <summary>
		/// Metres into the ground, min..max per instance: the spec's sink for a rule scattering this detail
		/// (BiomeArtSpec.PlantSink) and, its max, the material's fallback (<see cref="ProceduralArtCatalogue.DetailSink"/>).
		/// Zero = by name or kind as before.
		/// </summary>
		public Vector2 Sink;
	}

	/// <summary>
	/// What the generator needs to know of a detail kind besides its mesh: written once per kind by the file that
	/// builds it (<see cref="ProceduralArtCatalogue"/>'s FloraKindTraits / SeaKindTraits hooks), read wherever a
	/// legacy kind used to be named in a switch.
	/// </summary>
	/// <remarks>
	/// Only consulted for kinds the legacy switches do not name, so the legacy kinds' materials and prefabs are what
	/// they were. Every field's zero is the default a kind got before it had traits.
	/// </remarks>
	public struct DetailKindTraits
	{
		/// <summary>The prefab's renderer casts shadows (see <see cref="DetailSpec.CastsShadows"/>).</summary>
		public bool CastsShadows;
		/// <summary>The material's fallback ground sink, metres (<see cref="ProceduralArtCatalogue.DetailSink"/>); 0 = 0.03.</summary>
		public float Sink;
		/// <summary>The <see cref="Bark"/> family the material wears instead of the foliage atlas (a cactus's pads); null = the atlas.</summary>
		public string BarkFamily;
		/// <summary>The material's wind sway (<c>_WindSway</c>, BiomeArtGenerator); 0 = the 0.2 every non-grass detail gets.</summary>
		public float Sway;
		/// <summary>The healthy/dry patch size, metres (<c>_TintPatchMetres</c>, BiomeArtGenerator.DetailTintPatchMetres); 0 = its default.</summary>
		public float TintPatchMetres;
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

		private static RockMaterialSpec[] allRockMaterials;

		/// <summary>
		/// Every small-rock material the generator makes boulders, rocks and pebbles in: the four legacy ones
		/// (<see cref="RockMaterials"/>, which keep meaning "the legacy materials" — each stands for a rock type,
		/// RockTypes.ForLegacyMaterial) and those added since, each listed in the file of the package that owns it:
		/// the sea's manganese nodules and coral rubble (SeaRockMaterials, ProceduralArtCatalogue.Sea.cs) and ice
		/// cobbles (IceRockMaterials, ProceduralArtCatalogue.Rocks.cs). A newer material wears its ground family's
		/// textures (it is not one of RockArtNames' legacy-on-rock-surface four).
		/// </summary>
		/// <remarks>Built on first use for the same reason as <see cref="Details"/>: its parts are fields of other files.</remarks>
		public static RockMaterialSpec[] AllRockMaterials
		{
			get
			{
				if (allRockMaterials == null)
				{
					var all = new List<RockMaterialSpec>(RockMaterials);
					all.AddRange(SeaRockMaterials);
					all.AddRange(IceRockMaterials);
					allRockMaterials = all.ToArray();
				}
				return allRockMaterials;
			}
		}

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
		/// <summary>Healthy and dry alike for sea plants: no drought browns a kelp forest.</summary>
		private static readonly Color SeaTint = new Color(0.95f, 0.95f, 0.95f, 1f);

		private static DetailSpec D(string name, DetailKind kind, int count, float height, float width, float radius, string a, string b,
			float lean = 0.3f, int segments = 3, Color[] accents = null, float snowBury = 0.8f, Color? healthy = null, Color? dry = null,
			int variant = 0, float groupMetres = 0f, float groupSize = 0f, Vector2 sink = default)
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
					Variant = variant,
				},
				Healthy = healthy ?? HealthyTint,
				Dry = dry ?? DryTint,
				SnowBury = snowBury,
				CastsShadows = CastsShadows(kind),
				GroupMetres = groupMetres,
				GroupSize = groupSize,
				Sink = sink,
			};
		}

		/// <summary>
		/// Whether a detail kind's prefab casts shadows: the legacy kinds as they always did (the few tall and solid
		/// enough to throw a readable shadow), every newer kind as its owner's traits say.
		/// </summary>
		public static bool CastsShadows(DetailKind kind)
		{
			switch (kind)
			{
				case DetailKind.Fern:
				case DetailKind.Shrub:
				case DetailKind.DryShrub:
				case DetailKind.BarrelCactus:
				case DetailKind.BrainCoral:
				case DetailKind.TableCoral:
				case DetailKind.Sponge:
					return true;
				case DetailKind.Grass:
				case DetailKind.Reeds:
				case DetailKind.Flowers:
				case DetailKind.Debris:
				case DetailKind.Kelp:
				case DetailKind.Coral:
				case DetailKind.Seaweed:
				case DetailKind.SeaFan:
				case DetailKind.Anemone:
				case DetailKind.Urchin:
				case DetailKind.Starfish:
				case DetailKind.Shells:
				case DetailKind.TubeWorms:
					return false;
				default:
					return Traits(kind).CastsShadows;
			}
		}

		/// <summary>
		/// A detail kind's traits as the file that builds it declares them (all zero for a kind nobody declares,
		/// the legacy kinds included: their values live in the legacy switches, which are consulted first).
		/// </summary>
		/// <remarks>
		/// Each owner fills its own kinds in its own file, so no package edits a shared switch: the land flora in
		/// ProceduralArtCatalogue.Flora.cs (FloraKindTraits), the sea floor in ProceduralArtCatalogue.Sea.cs
		/// (SeaKindTraits). Both are partial methods — pure switches, reading no static field, because this is called
		/// while the type's static fields are still being initialised (from <see cref="D"/>, for CoreDetails).
		/// </remarks>
		public static DetailKindTraits Traits(DetailKind kind)
		{
			var traits = new DetailKindTraits();
			FloraKindTraits(kind, ref traits);
			SeaKindTraits(kind, ref traits);
			return traits;
		}

		/// <summary>The land flora's kinds' traits (ProceduralArtCatalogue.Flora.cs). Sets only the kinds it owns.</summary>
		static partial void FloraKindTraits(DetailKind kind, ref DetailKindTraits traits);

		/// <summary>The sea floor's newer kinds' traits (ProceduralArtCatalogue.Sea.cs). Sets only the kinds it owns.</summary>
		static partial void SeaKindTraits(DetailKind kind, ref DetailKindTraits traits);

		private static DetailSpec[] details;

		/// <summary>
		/// Every detail prefab the generator makes: the legacy ones (<see cref="CoreDetails"/>, first and in their old
		/// order), then the land flora's (FloraDetails, ProceduralArtCatalogue.Flora.cs) and the sea floor's
		/// (SeaDetails, ProceduralArtCatalogue.Sea.cs) — one list per package, so none edits another's.
		/// </summary>
		/// <remarks>
		/// <b>Built on first use, not in a field initialiser.</b> The C# compiler runs a partial class's static field
		/// initialisers file by file in an order the language leaves unspecified, so a field here that concatenated
		/// arrays declared in the other files could read them as null, and one of those built with <see cref="D"/>
		/// could read the tints above as zero. A property is first read after the type's initialiser has run every
		/// file's fields, and FloraDetails / SeaDetails are methods for the same reason. (BiomeArtSpec.Entries notes
		/// the same trap.) The array is cached, so callers see one array as before.
		/// </remarks>
		public static DetailSpec[] Details
		{
			get
			{
				if (details == null)
				{
					var all = new List<DetailSpec>(CoreDetails);
					all.AddRange(FloraDetails());
					all.AddRange(SeaDetails());
					details = all.ToArray();
				}
				return details;
			}
		}

		/// <summary>The details that were in the catalogue before the vegetation expansion, in their old order.</summary>
		/// <remarks>
		/// A field, initialised with the rest of this file in textual order, so it may read <see cref="HealthyTint"/>
		/// and the other tints above it. The other parts' detail lists are built by methods for the reason in
		/// <see cref="Details"/>.
		/// </remarks>
		private static readonly DetailSpec[] CoreDetails =
		{
			D("GrassLush", DetailKind.Grass, 18, 0.45f, 0.035f, 0.45f, "#4c7a2c", "#78a040"),
			D("GrassDry", DetailKind.Grass, 16, 0.5f, 0.03f, 0.45f, "#a08a4c", "#cdb672", healthy: new Color(1f, 0.97f, 0.9f), dry: new Color(0.85f, 0.78f, 0.62f)),
			D("GrassTall", DetailKind.Grass, 18, 0.9f, 0.035f, 0.4f, "#557f30", "#86a848", lean: 0.4f),
			D("GrassTuft", DetailKind.Grass, 10, 0.2f, 0.03f, 0.15f, "#6e7a3c", "#9a9a58", lean: 0.5f, segments: 2),
			D("Reeds", DetailKind.Reeds, 14, 1.4f, 0.025f, 0.2f, "#5e7034", "#8a9050", lean: 0.12f),
			D("Fern", DetailKind.Fern, 7, 0.6f, 0.28f, 0.03f, "#355e22", "#4f7a2e", segments: 4),
			D("FlowersMeadow", DetailKind.Flowers, 7, 0.35f, 0.07f, 0.35f, "#4c7a2c", "#669036", accents: new[] { H("#f4f2ea"), H("#f2d040"), H("#9b6fc8"), H("#e07a9a") }),
			D("FlowersWarm", DetailKind.Flowers, 6, 0.3f, 0.06f, 0.3f, "#6a7a36", "#8a9448", accents: new[] { H("#f0b030"), H("#e05a2a"), H("#f4e070") }),
			D("ShrubSmall", DetailKind.Shrub, 10, 0.6f, 0.45f, 0.4f, "#3f6526", "#5e8034", snowBury: 0.4f),
			D("ShrubDry", DetailKind.DryShrub, 8, 0.7f, 0.6f, 0.45f, "#6a5a44", "#8a7458", snowBury: 0.3f),
			D("DebrisForest", DetailKind.Debris, 10, 0f, 0.25f, 0.6f, "#6a4a2a", "#9a6a3a", snowBury: 1f),
			D("Kelp", DetailKind.Kelp, 4, 4.5f, 0.1f, 0.3f, "#5a4e1e", "#8a7428", segments: 10, snowBury: 0f, healthy: SeaTint, dry: SeaTint),
			D("Coral", DetailKind.Coral, 5, 0.5f, 0.04f, 0.2f, "#d06a5a", "#e8a060",
				accents: new[] { H("#d06a5a"), H("#e8a060"), H("#c070c0"), H("#6ac0c8"), H("#f0e080") }, snowBury: 0f),
			// The rest of the sea floor (SeaFloorMeshes).
			D("Seaweed", DetailKind.Seaweed, 6, 0.8f, 0.045f, 0.15f, "#4a3e14", "#8a6a22", lean: 0.35f, segments: 6, snowBury: 0f, healthy: SeaTint, dry: SeaTint),
			D("BrainCoral", DetailKind.BrainCoral, 1, 0.28f, 0f, 0.45f, "#b8a070", "#8c7a4a",
				accents: new[] { H("#b8a878"), H("#a8b070"), H("#c09a70"), H("#9a8a9a") }, snowBury: 0f),
			D("TableCoral", DetailKind.TableCoral, 1, 0.4f, 0f, 0.7f, "#9ab0a0", "#6a7a6a",
				accents: new[] { H("#a0c0b0"), H("#c0b090"), H("#b090a0"), H("#90a8c0") }, snowBury: 0f),
			D("SeaFan", DetailKind.SeaFan, 1, 0.9f, 0.012f, 0.5f, "#8a3a8a", "#c06a3a",
				accents: new[] { H("#8a3a8a"), H("#c0603a"), H("#d0b040"), H("#c03a4a") }, snowBury: 0f),
			D("Sponge", DetailKind.Sponge, 3, 0.5f, 0.1f, 0.25f, "#c08030", "#a04a2a",
				accents: new[] { H("#d09030"), H("#b04a6a"), H("#7a4a9a"), H("#c0c040"), H("#c06030") }, snowBury: 0f),
			D("Anemone", DetailKind.Anemone, 1, 0.12f, 0.006f, 0.15f, "#a05a4a", "#c08070",
				accents: new[] { H("#e070a0"), H("#70c090"), H("#b080e0"), H("#f0a050") }, snowBury: 0f),
			D("Urchin", DetailKind.Urchin, 2, 0.08f, 0.004f, 0.22f, "#2a1a2e", "#4a2a4a", snowBury: 0f),
			D("Starfish", DetailKind.Starfish, 2, 0.02f, 0f, 0.3f, "#d06030", "#b03a3a",
				accents: new[] { H("#e07030"), H("#c03a40"), H("#8a4a9a"), H("#e0a040") }, snowBury: 0f),
			D("Shells", DetailKind.Shells, 4, 0.03f, 0f, 0.35f, "#e8dcc0", "#c0a080",
				accents: new[] { H("#eee2c8"), H("#d8b898"), H("#e0b0a8"), H("#b89870") }, snowBury: 0f),
			D("TubeWorms", DetailKind.TubeWorms, 10, 0.7f, 0.015f, 0.25f, "#e8e4d8", "#d0c8b0", accents: new[] { H("#c01c1c") }, snowBury: 0f),
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
			if (spec.Sink.y > 0f)
			{
				return spec.Sink.y; // a newer detail's own (no legacy detail sets it)
			}
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
				case DetailKind.BrainCoral: return 0.06f;
				case DetailKind.TableCoral: return 0.05f;
				case DetailKind.SeaFan: return 0.04f;
				case DetailKind.Sponge: return 0.05f;
				case DetailKind.TubeWorms: return 0.05f;
				case DetailKind.Anemone: return 0.02f;
				case DetailKind.Urchin: return 0.015f;
				case DetailKind.Starfish: return 0.004f;
				case DetailKind.Shells: return 0.006f;
				default:
					float own = Traits(spec.Plant.Kind).Sink;
					return own > 0f ? own : 0.03f;
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

		/// <summary>Every tree species, at the size of a mature tree of its kind: one unit is one metre.</summary>
		/// <remarks>
		/// <para>
		/// <b>Real sizes.</b> These were 6–20 m, about half to two thirds of the real trees, which made a
		/// pine wood read as a plantation of saplings beside a 1.8 m character. Each is now a mature tree
		/// as it grows in a stand, the scatter's ±15–20% per instance (<see cref="BiomeArtSpec"/>) giving
		/// the spread round it:
		/// </para>
		/// <list type="bullet">
		/// <item><b>Spruce</b> (Norway spruce): 28 m, trunk 0.38 m radius (≈0.75 m across at the foot),
		/// a narrow spire 3.1 m in radius (0.11 of the height) from low down (0.18): spruce keeps its
		/// lower boughs, even in a stand. 28 whorls, about one every 0.8 m of crown.</item>
		/// <item><b>Pine</b> (Scots pine): 30 m, trunk 0.4 m, its crown high on a bare bole (from 0.55) and
		/// 3.9 m in radius (0.13): a stand-grown pine sheds its lower branches.</item>
		/// <item><b>Oak</b> (pedunculate oak): 24 m, trunk 0.6 m, a broad crown 7.2 m in radius (0.3) from
		/// a third of the way up; an open-grown oak spreads wider still, which the open-ground width lean
		/// gives it.</item>
		/// <item><b>Birch</b> (silver birch): 20 m, a slender 0.18 m trunk, a narrow crown 3.2 m in radius.</item>
		/// <item><b>Jungle</b> (a rainforest canopy tree): 36 m, a 0.7 m trunk (the generator has no
		/// buttresses, so the trunk is wide at the foot instead), a crown 9 m in radius high on the bole (0.6).</item>
		/// <item><b>Dead</b> (a snag): 14 m, 0.3 m.</item>
		/// <item><b>Palm</b> (coconut): 16 m, a 0.2 m stem, fronds 4.8 m long (0.3 of the height) and 1.2 m wide.</item>
		/// <item><b>Saguaro</b>: 10 m, a 0.3 m column with three arms.</item>
		/// <item><b>Acacia</b> (umbrella thorn): 9 m, its flat crown 5.4 m in radius — wider than the tree is tall.</item>
		/// <item><b>Bamboo</b>: a clump of twelve 13 m culms of 0.065 m radius on a footing 1 m across.</item>
		/// </list>
		/// <para>
		/// <b>Leaf cards grow with the crown.</b> A crown is cards hung on limbs, so a crown twice as wide
		/// with the old cards and limbs would be a quarter as full: the broadleaves get more limbs and
		/// larger cards (oak 2.2 m, jungle 2.6 m), keeping card area at about two and a half times the
		/// crown's surface as before. Conifer sprays are sized from the crown already. The billboard's
		/// texture is a fixed pixel height whatever the tree's, and the level-of-detail switches are
		/// screen heights (<see cref="TreeLodHeights"/>), so a taller tree simply changes level further off.
		/// </para>
		/// </remarks>
		public static readonly TreeSpecies[] Trees =
		{
			new TreeSpecies { Name = "Spruce", Form = TreeForm.Conifer, Height = 28f, TrunkRadius = 0.38f, CrownWidth = 0.11f, CrownBase = 0.18f, Branches = 28,
				BarkFamily = Bark.Brown, LeafA = H("#24452a"), LeafB = H("#35593a"), LeafCell = FoliageCell.NeedleSpray, LeafSize = 1.6f },
			new TreeSpecies { Name = "Pine", Form = TreeForm.Pine, Height = 30f, TrunkRadius = 0.4f, CrownWidth = 0.13f, CrownBase = 0.55f, Branches = 18,
				BarkFamily = Bark.Pine, LeafA = H("#2e5230"), LeafB = H("#46683a"), LeafCell = FoliageCell.NeedleSpray, LeafSize = 1.8f },
			new TreeSpecies { Name = "Oak", Form = TreeForm.Broadleaf, Height = 24f, TrunkRadius = 0.6f, CrownWidth = 0.3f, CrownBase = 0.3f, Branches = 8,
				BarkFamily = Bark.Brown, LeafA = H("#3d6426"), LeafB = H("#5a7e30"), LeafCell = FoliageCell.BroadLeaves, LeafSize = 2.2f, Deciduous = true },
			new TreeSpecies { Name = "Birch", Form = TreeForm.Broadleaf, Height = 20f, TrunkRadius = 0.18f, CrownWidth = 0.16f, CrownBase = 0.4f, Branches = 6,
				BarkFamily = Bark.Birch, LeafA = H("#5a8a30"), LeafB = H("#7aa040"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 1.5f, Deciduous = true },
			new TreeSpecies { Name = "Jungle", Form = TreeForm.Broadleaf, Height = 36f, TrunkRadius = 0.7f, CrownWidth = 0.25f, CrownBase = 0.6f, Branches = 8,
				BarkFamily = Bark.Brown, LeafA = H("#24501e"), LeafB = H("#3a6a26"), LeafCell = FoliageCell.BroadLeaves, LeafSize = 2.6f },
			new TreeSpecies { Name = "Dead", Form = TreeForm.Dead, Height = 14f, TrunkRadius = 0.3f, CrownWidth = 0.25f, CrownBase = 0.4f, Branches = 6,
				BarkFamily = Bark.Dead, LeafA = H("#6a5e4e"), LeafB = H("#8a7a66"), LeafCell = FoliageCell.Twigs, LeafSize = 1.2f },
			new TreeSpecies { Name = "Palm", Form = TreeForm.Palm, Height = 16f, TrunkRadius = 0.2f, CrownWidth = 0.3f, CrownBase = 0.9f, Branches = 14,
				BarkFamily = Bark.Palm, LeafA = H("#3e6a26"), LeafB = H("#5a8030"), LeafCell = FoliageCell.PalmFrond, LeafSize = 1.2f },
			new TreeSpecies { Name = "Saguaro", Form = TreeForm.Cactus, Height = 10f, TrunkRadius = 0.3f, CrownWidth = 0.2f, CrownBase = 0f, Branches = 3,
				BarkFamily = Bark.Cactus, LeafA = Color.white, LeafB = Color.white, LeafCell = FoliageCell.Solid, LeafSize = 0f },
			new TreeSpecies { Name = "Acacia", Form = TreeForm.Umbrella, Height = 9f, TrunkRadius = 0.25f, CrownWidth = 0.6f, CrownBase = 0.8f, Branches = 3,
				BarkFamily = Bark.Brown, LeafA = H("#55702a"), LeafB = H("#748a38"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 2.2f },
			new TreeSpecies { Name = "Bamboo", Form = TreeForm.Bamboo, Height = 13f, TrunkRadius = 0.065f, CrownWidth = 0.3f, CrownBase = 0.45f, Branches = 12,
				BarkFamily = Bark.Bamboo, LeafA = H("#4a7a2a"), LeafB = H("#6a9638"), LeafCell = FoliageCell.BambooLeaves, LeafSize = 1.4f },

			// ── The vegetation expansion (2026-10-10): a species for every biome that had none of its own ──
			// Appended, so no older species' order (or anything keyed by it) moves. Sizes are mature trees of each real
			// species as they grow in their habitat; the biomes each stands in are in the design's per-biome table.

			// European larch: a deciduous conifer, the open spire of a spruce but sparser, softer and lighter green,
			// gold in autumn and bare in winter. Taiga, alpine meadow and valley treelines, upper mountain slopes.
			new TreeSpecies { Name = "Larch", Form = TreeForm.Conifer, Height = 30f, TrunkRadius = 0.4f, CrownWidth = 0.12f, CrownBase = 0.3f, Branches = 22,
				BarkFamily = Bark.Pine, LeafA = H("#7a9a4a"), LeafB = H("#9ab45a"), LeafCell = FoliageCell.NeedleSpray, LeafSize = 1.5f, Deciduous = true },
			// European beech: a tall smooth grey bole under a dense dome that shades out the floor. Forests and woodland.
			new TreeSpecies { Name = "Beech", Form = TreeForm.Broadleaf, Height = 32f, TrunkRadius = 0.55f, CrownWidth = 0.27f, CrownBase = 0.3f, Branches = 8,
				BarkFamily = Bark.Smooth, LeafA = H("#3e6a28"), LeafB = H("#5a8a34"), LeafCell = FoliageCell.BroadLeaves, LeafSize = 2.6f, Deciduous = true },
			// Common alder: a narrow oval crown of dark leaves on wet ground — riverbanks, lakeshores, fen carr.
			new TreeSpecies { Name = "Alder", Form = TreeForm.Broadleaf, Height = 20f, TrunkRadius = 0.3f, CrownWidth = 0.2f, CrownBase = 0.3f, Branches = 7,
				BarkFamily = Bark.Brown, LeafA = H("#2e5224"), LeafB = H("#44682c"), LeafCell = FoliageCell.BroadLeaves, LeafSize = 1.7f, Deciduous = true },
			// Olive: squat and wide on a short, fluted, half-hollow trunk, its small leaves silver-grey. Mediterranean
			// hills and karst, scrubland, palace gardens.
			new TreeSpecies { Name = "Olive", Form = TreeForm.Broadleaf, Height = 9f, TrunkRadius = 0.45f, CrownWidth = 0.45f, CrownBase = 0.35f, Branches = 7,
				BarkFamily = Bark.Olive, LeafA = H("#7a8a6a"), LeafB = H("#a0aa90"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 1.3f, Gnarl = 0.6f },
			// African baobab: a vast bottle trunk, swollen a half again about a third of its height, under a short
			// crown of stubby limbs, leafless through the dry season. The savanna.
			new TreeSpecies { Name = "Baobab", Form = TreeForm.Broadleaf, Height = 20f, TrunkRadius = 2.2f, CrownWidth = 0.45f, CrownBase = 0.72f, Branches = 8,
				BarkFamily = Bark.Smooth, LeafA = H("#4e7a2c"), LeafB = H("#6a9038"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 1.8f, Deciduous = true, TrunkSwell = 0.5f },
			// Date palm: a straight stem of diamond leaf bases and a crown of many stiff glaucous fronds. Oases, wet
			// desert hollows, palace gardens (the coconut grows on tropical shores, not at desert springs).
			new TreeSpecies { Name = "DatePalm", Form = TreeForm.Palm, Height = 20f, TrunkRadius = 0.35f, CrownWidth = 0.24f, CrownBase = 0.8f, Branches = 28,
				BarkFamily = Bark.DatePalm, LeafA = H("#6a8a6a"), LeafB = H("#8aa080"), LeafCell = FoliageCell.PalmFrond, LeafSize = 1f, Lean = 0.02f, FrondLift = 1.05f },
			// Joshua tree: a shaggy trunk forking into angular limbs, each tipped by a rosette of dagger leaves over a
			// skirt of dead ones. The cool high desert (Mojave).
			new TreeSpecies { Name = "JoshuaTree", Form = TreeForm.Rosette, Height = 9f, TrunkRadius = 0.4f, CrownWidth = 0.35f, CrownBase = 0.35f, Branches = 3,
				BarkFamily = Bark.Fibrous, LeafA = H("#6a7a3a"), LeafB = H("#8a9a4a"), LeafCell = FoliageCell.BambooLeaves, LeafSize = 0.8f },
			// Red mangrove: a dense, glossy, low crown stood on arching prop roots in the tidal mud.
			new TreeSpecies { Name = "Mangrove", Form = TreeForm.Broadleaf, Height = 12f, TrunkRadius = 0.25f, CrownWidth = 0.32f, CrownBase = 0.45f, Branches = 6,
				BarkFamily = Bark.Brown, LeafA = H("#2a5222"), LeafB = H("#3a6a2a"), LeafCell = FoliageCell.BroadLeaves, LeafSize = 1.5f,
				PropRootCount = 12, PropRootTop = 0.3f, PropRootRing = 2.5f },
			// Bald cypress: a deciduous conifer of southern swamps, its foot swollen into fluted buttresses, its crown
			// flat-topped and feathery, rust in autumn, hung with grey Spanish moss all year.
			new TreeSpecies { Name = "BaldCypress", Form = TreeForm.Pine, Height = 30f, TrunkRadius = 0.6f, CrownWidth = 0.2f, CrownBase = 0.4f, Branches = 18,
				BarkFamily = Bark.Fibrous, LeafA = H("#5a7a3a"), LeafB = H("#7a9a4a"), LeafCell = FoliageCell.NeedleSpray, LeafSize = 1.6f, Deciduous = true,
				ButtressCount = 9, ButtressHeight = 0.07f, ButtressReach = 1.3f, Hanging = 0.5f, HangingColour = H("#8a8e80") },
			// Kapok (silk-cotton tree): the rainforest's emergent, a smooth bare bole rising through the canopy to an
			// umbrella crown at three quarters of its height, braced at the foot by tall plank buttresses.
			new TreeSpecies { Name = "Kapok", Form = TreeForm.Broadleaf, Height = 55f, TrunkRadius = 1.2f, CrownWidth = 0.26f, CrownBase = 0.72f, Branches = 7,
				BarkFamily = Bark.Smooth, LeafA = H("#2e5a24"), LeafB = H("#4a7a30"), LeafCell = FoliageCell.BroadLeaves, LeafSize = 3.8f,
				ButtressCount = 6, ButtressHeight = 0.08f, ButtressReach = 4f },
			// Tree fern: a slender fibrous stem and a shuttlecock of long arching fronds. Jungle understory, bamboo
			// forest, warm wet karst and mountain slopes.
			new TreeSpecies { Name = "TreeFern", Form = TreeForm.Palm, Height = 7f, TrunkRadius = 0.15f, CrownWidth = 0.4f, CrownBase = 0.8f, Branches = 12,
				BarkFamily = Bark.Fibrous, LeafA = H("#3e6e26"), LeafB = H("#5a8a34"), LeafCell = FoliageCell.Frond, LeafSize = 0.9f, Lean = 0.03f, FrondLift = 0.7f },
			// Pinyon pine: low, rounded and bushy, crowned nearly to the ground with short grey-green needles. High
			// desert and dry ranges, badland caprock.
			new TreeSpecies { Name = "Pinyon", Form = TreeForm.Pine, Height = 9f, TrunkRadius = 0.25f, CrownWidth = 0.35f, CrownBase = 0.15f, Branches = 14,
				BarkFamily = Bark.Pine, LeafA = H("#3a5a3a"), LeafB = H("#5a7448"), LeafCell = FoliageCell.NeedleSpray, LeafSize = 1.2f },

			// Quaking aspen: slender and pale-barked, a narrow crown of round leaves that turns gold. Taiga after fire,
			// valleys, mountain slopes.
			new TreeSpecies { Name = "Aspen", Form = TreeForm.Broadleaf, Height = 20f, TrunkRadius = 0.2f, CrownWidth = 0.15f, CrownBase = 0.5f, Branches = 6,
				BarkFamily = Bark.Aspen, LeafA = H("#6a9a3a"), LeafB = H("#8ab04a"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 1.4f, Deciduous = true },
			// Weeping willow: a broad crown whose shoots fall in curtains of narrow leaves nearly to the ground. Rivers,
			// lakes, wetlands, swamps.
			new TreeSpecies { Name = "WeepingWillow", Form = TreeForm.Broadleaf, Height = 20f, TrunkRadius = 0.5f, CrownWidth = 0.32f, CrownBase = 0.3f, Branches = 6,
				BarkFamily = Bark.Brown, LeafA = H("#6a8a3a"), LeafB = H("#8aa24a"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 2.1f, Deciduous = true, Weeping = 0.4f },
			// Black poplar / cottonwood: tall, broad and irregular on a deeply fissured trunk. River corridors, valleys,
			// prairie creeks, farmland.
			new TreeSpecies { Name = "Poplar", Form = TreeForm.Broadleaf, Height = 28f, TrunkRadius = 0.6f, CrownWidth = 0.28f, CrownBase = 0.35f, Branches = 8,
				BarkFamily = Bark.Brown, LeafA = H("#4a7a2c"), LeafB = H("#6a9438"), LeafCell = FoliageCell.BroadLeaves, LeafSize = 2.4f, Deciduous = true },
			// Italian cypress: a dark green flame of a column. Palace avenues, karst, scrubland.
			new TreeSpecies { Name = "Cypress", Form = TreeForm.Columnar, Height = 20f, TrunkRadius = 0.3f, CrownWidth = 0.065f, CrownBase = 0.03f, Branches = 24,
				BarkFamily = Bark.Fibrous, LeafA = H("#1e3a1e"), LeafB = H("#2a4a26"), LeafCell = FoliageCell.NeedleSpray, LeafSize = 1f },
			// Banana: a soft pseudostem under a few huge paddle leaves torn by the wind. Jungle gaps and ruins, the
			// landward edge of mangroves.
			new TreeSpecies { Name = "Banana", Form = TreeForm.Palm, Height = 5f, TrunkRadius = 0.12f, CrownWidth = 0.42f, CrownBase = 0.6f, Branches = 8,
				BarkFamily = Bark.Fibrous, LeafA = H("#3a7a2a"), LeafB = H("#5a9a34"), LeafCell = FoliageCell.Paddle, LeafSize = 0.6f, Lean = 0.03f, FrondLift = 1.5f },
			// Great Basin bristlecone pine: squat, wind-twisted, its trunk mostly dead wood wrapped in living bark
			// strips and half its boughs bare, bottle-brush tufts of dark needles on the rest. Dry ranges above the trees.
			new TreeSpecies { Name = "Bristlecone", Form = TreeForm.Pine, Height = 10f, TrunkRadius = 0.6f, CrownWidth = 0.32f, CrownBase = 0.2f, Branches = 14,
				BarkFamily = Bark.Dead, LeafA = H("#24402a"), LeafB = H("#36543a"), LeafCell = FoliageCell.NeedleSpray, LeafSize = 1f, Gnarl = 0.95f },
			// Honey mesquite: low and spreading on several stems, its feathery leaves light green. Scrubland, desert
			// washes, badlands.
			new TreeSpecies { Name = "Mesquite", Form = TreeForm.Umbrella, Height = 7f, TrunkRadius = 0.2f, CrownWidth = 0.55f, CrownBase = 0.5f, Branches = 4,
				BarkFamily = Bark.Brown, LeafA = H("#6a8a3a"), LeafB = H("#8aa24a"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 2.2f },

			// Lombardy poplar: a tall narrow column of small leaves — the farmland windbreak and valley avenue tree.
			new TreeSpecies { Name = "LombardyPoplar", Form = TreeForm.Columnar, Height = 25f, TrunkRadius = 0.45f, CrownWidth = 0.09f, CrownBase = 0.04f, Branches = 24,
				BarkFamily = Bark.Brown, LeafA = H("#4a7a2c"), LeafB = H("#6a9438"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 1f, Deciduous = true },
			// Holm oak: a dense dark evergreen dome of small leathery leaves. Karst, scrubland, palace gardens.
			new TreeSpecies { Name = "HolmOak", Form = TreeForm.Broadleaf, Height = 18f, TrunkRadius = 0.5f, CrownWidth = 0.33f, CrownBase = 0.25f, Branches = 8,
				BarkFamily = Bark.Brown, LeafA = H("#2a4422"), LeafB = H("#3e5a2c"), LeafCell = FoliageCell.SmallLeaves, LeafSize = 2f },
			// Dragon tree: a stout grey trunk under a dense umbrella of forking limbs, every tip a rosette of
			// blue-green swords. Dry volcanic slopes, scrubland.
			new TreeSpecies { Name = "DragonTree", Form = TreeForm.Rosette, Height = 10f, TrunkRadius = 0.5f, CrownWidth = 0.45f, CrownBase = 0.42f, Branches = 6,
				BarkFamily = Bark.Smooth, LeafA = H("#4a6a4a"), LeafB = H("#6a8a68"), LeafCell = FoliageCell.BambooLeaves, LeafSize = 1.3f },
			// Screw pine (Pandanus): a slender branching trunk on a cone of prop roots, every branch a spiral rosette
			// of long strap leaves. Tropical beaches behind the strand.
			new TreeSpecies { Name = "Pandanus", Form = TreeForm.Rosette, Height = 8f, TrunkRadius = 0.18f, CrownWidth = 0.4f, CrownBase = 0.45f, Branches = 3,
				BarkFamily = Bark.Palm, LeafA = H("#4a7a3a"), LeafB = H("#6a9a48"), LeafCell = FoliageCell.BambooLeaves, LeafSize = 1.6f,
				PropRootCount = 7, PropRootTop = 0.12f, PropRootRing = 1f },
			// Nipa palm: no trunk above the mud — its long erect fronds rise straight from a creeping stem. The landward
			// mangrove and tidal creeks.
			new TreeSpecies { Name = "Nipa", Form = TreeForm.Palm, Height = 7f, TrunkRadius = 0.25f, CrownWidth = 0.36f, CrownBase = 0.05f, Branches = 11,
				BarkFamily = Bark.Palm, LeafA = H("#4a7a2a"), LeafB = H("#6a8e38"), LeafCell = FoliageCell.PalmFrond, LeafSize = 1.1f, Lean = 0.02f, FrondLift = 2.8f },
		};

		/// <summary>
		/// How far a species' crown reaches from its trunk, metres, at scale 1: what a stand's spacing is
		/// sized from (<see cref="BiomeArtSpec"/>).
		/// </summary>
		/// <remarks>
		/// The crown width is a radius over the height for every form the generator grows round a single
		/// stem (TreeMeshes: a conifer's lowest whorl, a broadleaf's or an umbrella's crown radius, a palm's
		/// frond length). A bamboo's crown is its clump — culm feet spread over a quarter of the crown
		/// width times the height — plus one leaf spray; a cactus's is its arms, about three trunk radii. A rosette
		/// tree's limbs are aimed at the crown width too (TreeMeshes' Rosette), and a columnar tree's whorls reach it.
		/// A tree on prop roots stands over a ring of them (<see cref="TreeSpecies.PropRootRing"/>): its footprint is
		/// the wider of the crown and that ring, not their sum — the roots stand under the crown, and a mangrove's
		/// closed canopy would open into gaps if its trees were spaced by both.
		/// </remarks>
		public static float CrownRadius(in TreeSpecies species)
		{
			switch (species.Form)
			{
				case TreeForm.Bamboo: return species.CrownWidth * species.Height * 0.25f + species.LeafSize;
				case TreeForm.Cactus: return species.TrunkRadius * 3f;
				default: return Mathf.Max(species.CrownWidth * species.Height, species.PropRootCount > 0 ? species.PropRootRing : 0f);
			}
		}

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
		/// Screen shares scale with the tree, so a 28 m spruce steps at ≈ 97 m and ≈ 300 m: the trees
		/// grew to their real sizes (2026-10-04) and change level proportionally further off, at the same
		/// size on screen, which is what the shares are for.
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
		public static bool WearsBark(in DetailSpec spec) => DetailBark(in spec) != null;

		/// <summary>
		/// The <see cref="Bark"/> family a detail's material wears instead of the foliage atlas, or null: the barrel
		/// cactus the cactus bark, as always; a newer kind whatever its traits name (<see cref="DetailKindTraits.BarkFamily"/>).
		/// </summary>
		public static string DetailBark(in DetailSpec spec) => spec.Plant.Kind == DetailKind.BarrelCactus ? Bark.Cactus : Traits(spec.Plant.Kind).BarkFamily;

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
			foreach (string path in BushPayloadPaths())
			{
				yield return path; // Shrubs (ProceduralArtCatalogue.Bushes.cs).
			}
			foreach (string path in DeadwoodPayloadPaths())
			{
				yield return path; // Logs, stumps, driftwood (ProceduralArtCatalogue.Flora.cs).
			}
			foreach (string path in StructurePayloadPaths())
			{
				yield return path; // The structure kit (ProceduralArtCatalogue.Structures.cs).
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
			foreach (RockMaterialSpec m in AllRockMaterials)
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
			foreach (string path in BushWrapperPaths())
			{
				yield return path;
			}
			foreach (string path in DeadwoodWrapperPaths())
			{
				yield return path;
			}
			foreach (string path in StructureWrapperPaths())
			{
				yield return path;
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
			}
			foreach (RockMaterialSpec m in AllRockMaterials)
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
			foreach (string prefab in BushPrefabNames())
			{
				yield return prefab;
			}
			foreach (string prefab in DeadwoodPrefabNames())
			{
				yield return prefab;
			}
			foreach (string prefab in StructurePrefabNames())
			{
				yield return prefab;
			}
		}
	}
}
#endif
