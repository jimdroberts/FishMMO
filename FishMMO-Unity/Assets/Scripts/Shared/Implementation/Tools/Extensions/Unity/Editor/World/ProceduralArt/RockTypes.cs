#if UNITY_EDITOR
using System;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The three ways a rock is made, which is most of what decides how it breaks.</summary>
	public enum RockFamily
	{
		/// <summary>Cooled from melt: massive or jointed, glassy, or blown full of gas holes.</summary>
		Igneous,
		/// <summary>Laid down in beds: breaks along its bedding and weathers back bed by bed.</summary>
		Sedimentary,
		/// <summary>Recrystallised under heat and pressure: cleaved, foliated or sugary.</summary>
		Metamorphic,
	}

	/// <summary>Which builder in <see cref="RockFormations"/> makes a shape.</summary>
	public enum FormationKind
	{
		/// <summary>One rock from an implicit field: corestones, lumps, foliated and pitted boulders.</summary>
		Boulder,
		/// <summary>Rounded joint blocks stacked on each other: a granite tor.</summary>
		Tor,
		/// <summary>A big rounded boulder balanced on a low one: a logan (rocking) stone.</summary>
		Perched,
		/// <summary>A corestone parted along one vertical joint, the halves leaning apart.</summary>
		Split,
		/// <summary>Flat-lying beds of differing hardness: ledges where hard beds stand proud.</summary>
		Bedded,
		/// <summary>Beds stepping back as they rise: a staircase cliff in miniature.</summary>
		Ledges,
		/// <summary>A hard cap bed on a neck of soft beds: a pedestal or mushroom rock.</summary>
		Pedestal,
		/// <summary>Karst clints parted by grikes, their tops pocked with solution pans.</summary>
		Pavement,
		/// <summary>A tapering spire scored by vertical solution flutes.</summary>
		Pinnacle,
		/// <summary>A tall eroded cone of soft rock under a hard capstone: a fairy chimney.</summary>
		Chimney,
		/// <summary>Flat fracture faces meeting at sharp edges, conchoidal or jointed.</summary>
		Faceted,
		/// <summary>Thin plates parted along cleavage or fissility, stacked and shingled.</summary>
		SlabStack,
		/// <summary>One cleaved slab standing on end.</summary>
		Standing,
		/// <summary>A cluster of polygonal columns of columnar jointing.</summary>
		Columns,
		/// <summary>Broken columns lying where they fell.</summary>
		FallenColumns,
		/// <summary>A heap of fragments: talus at the foot of an outcrop.</summary>
		Scree,

		// ── Mineral, ice and alien formations (2026-10-10), built by CrystalFormations ──

		/// <summary>Crystals grown from a common matrix, in the shape's <see cref="FormationShape.Habit"/>: selenite blades, sulphur needles, pyrite cubes, ice prisms, halite pinnacles.</summary>
		CrystalCluster,
		/// <summary>A vent cone with a crater or an orifice at its top: spatter cone, hornito, fumarole, geyser cone, ice vent, a black smoker's chimney (with <see cref="FormationShape.Count"/> flanges).</summary>
		Cone,
		/// <summary>Rimmed pools stepping down a slope, each a lobe with a raised lip: sinter and travertine terraces, cave gours, salt terraces.</summary>
		Terrace,
		/// <summary>A small crater's raised rim and apron, its bowl floor flush with the ground, ejecta blocks on the rim.</summary>
		CraterRim,
		/// <summary>A patch of salt-crust polygons whose edges have buckled up into a network of low ridges.</summary>
		PolygonRidges,
		/// <summary>A fluted termite mound with turrets round its main spire.</summary>
		Mound,
		/// <summary>Dripstone: a cluster of stalagmites on a shared flowstone foot (lava dribble spires, ice stalagmites).</summary>
		Stalagmite,
		/// <summary>A flowstone mound draped with ribs and rippled with small rims.</summary>
		Flowstone,
		/// <summary>A conical knob from the implicit field; with a <see cref="FormationShape.Cap"/>, a second body capping its top (a frost cap).</summary>
		Knob,
		/// <summary>A wind-cut stone: three faces polished flat by blown sand, meeting at sharp keels (a dreikanter).</summary>
		Ventifact,
		/// <summary>Wind-carved snow ridges, aligned, each with a steep undercut prow into the wind.</summary>
		Sastrugi,
		/// <summary>A field of tall blades of snow or ice in rows, leaning toward the noon sun, on a shared foot.</summary>
		Penitentes,
		/// <summary>A lobate flow front: a low tongue with a steep rounded edge and pressure ridges on its top.</summary>
		Lobe,
		/// <summary>A glacier table: a boulder (the <see cref="FormationShape.Cap"/>) on the ice pedestal its shade preserved.</summary>
		Table,
		/// <summary>Broken slabs heaped into a ridge on the ground (a grounded pressure ridge, from <see cref="IceMeshes"/>).</summary>
		RubbleRidge,
	}

	/// <summary>How the crystals of a <see cref="FormationKind.CrystalCluster"/> grow.</summary>
	public enum CrystalHabit
	{
		/// <summary>Six-sided columns with flat or shallow-pointed ends: ice, quartz-like prisms.</summary>
		Prismatic,
		/// <summary>Flattened blades with chisel tips: selenite gypsum.</summary>
		Bladed,
		/// <summary>Thin needles in radiating sprays: sulphur.</summary>
		Acicular,
		/// <summary>Interlocking cubes: pyrite, halite.</summary>
		Cubic,
		/// <summary>Irregular steep spires from a rough crust: halite pinnacles etched by rain.</summary>
		Jagged,
		/// <summary>Thin six-sided plates standing every way: hoar frost, frost flowers.</summary>
		Plates,
	}

	/// <summary>What a scree fragment of this rock looks like.</summary>
	public enum FragmentForm
	{
		/// <summary>Blocky, sharp-edged: most hard rocks.</summary>
		Angular,
		/// <summary>Thin plates: slate, shale, flaggy schist.</summary>
		Platy,
		/// <summary>Soft or vesicular rock that breaks into lumps: pumice, tuff, chalk.</summary>
		Rounded,
	}

	/// <summary>Bedding: how many beds a block shows and how differently they weather.</summary>
	[Serializable]
	public struct BeddingStyle
	{
		/// <summary>Bed count range for a block (a shape's Count overrides).</summary>
		public Vector2 Beds;
		/// <summary>How far the softest bed weathers back behind the hardest, as a fraction of its own thickness (about 0.1–0.25).</summary>
		public float Contrast;
		/// <summary>Dip of the beds in degrees, min..max.</summary>
		public Vector2 Dip;
		/// <summary>0 square-edged … 1 soft beds fully rounded.</summary>
		public float Rounding;
		/// <summary>Ragged outline of each bed, as a fraction of the block's size.</summary>
		public float Crumble;
		/// <summary>Vertical joints that square off the block's outline.</summary>
		public int Joints;
	}

	/// <summary>Foliation: layering by mineral segregation, standing out as ribs where it weathers.</summary>
	[Serializable]
	public struct FoliationStyle
	{
		/// <summary>Distance between bands, metres.</summary>
		public float Wavelength;
		/// <summary>How far a resistant band stands proud, metres. Zero turns foliation off.</summary>
		public float Relief;
		/// <summary>Amplitude of folding, metres.</summary>
		public float Fold;
		/// <summary>Distance between fold crests, metres.</summary>
		public float FoldWavelength;
		/// <summary>0 straight … 1 crinkled (crenulation of the bands at a small scale).</summary>
		public float Crenulation;
		/// <summary>Dip of the foliation in degrees, min..max.</summary>
		public Vector2 Dip;
	}

	/// <summary>Cleavage or fissility: the rock parts into plates of a thickness.</summary>
	[Serializable]
	public struct CleavageStyle
	{
		/// <summary>Plate thickness range, metres.</summary>
		public Vector2 Thickness;
		/// <summary>Tilt of the plates from horizontal, degrees.</summary>
		public Vector2 Tilt;
		/// <summary>How many fracture faces make each plate's edge.</summary>
		public int EdgeFacets;
		/// <summary>0 square broken edges … 1 edges bevelled at all angles.</summary>
		public float EdgeBevel;
	}

	/// <summary>Exfoliation: onion-skin sheets peeling off a rounded rock.</summary>
	[Serializable]
	public struct ExfoliationStyle
	{
		/// <summary>Thickness of one sheet, metres. Zero turns it off.</summary>
		public float Thickness;
		/// <summary>Fraction of the surface where the outer sheet has already gone.</summary>
		public float Peel;
	}

	/// <summary>Fracture: what a broken face looks like.</summary>
	[Serializable]
	public struct FractureStyle
	{
		/// <summary>How deeply a fracture face is scooped, as a fraction of its width (conchoidal scars).</summary>
		public float Dish;
		/// <summary>Concentric ripple rings round the point of impact.</summary>
		public int Ripples;
		/// <summary>Ripple height as a fraction of the face's width.</summary>
		public float RippleRelief;
		/// <summary>Uneven relief on a face, metres.</summary>
		public float Roughness;
		/// <summary>Faces follow three joint sets (blocky) rather than falling every way (conchoidal).</summary>
		public bool Jointed;
	}

	/// <summary>Pits: gas vesicles, tafoni honeycomb, solution pans, weathering pits.</summary>
	[Serializable]
	public struct PitStyle
	{
		/// <summary>Distance between candidate pit sites, metres. Zero turns pits off.</summary>
		public float Spacing;
		/// <summary>Fraction of sites that hold a pit.</summary>
		public float Fill;
		/// <summary>Pit radius as a fraction of the spacing.</summary>
		public float Radius;
		/// <summary>Pit depth, metres.</summary>
		public float Depth;
		/// <summary>0 pits everywhere … 1 only on upward-facing surfaces (solution pans, gnammas).</summary>
		public float Tops;
		/// <summary>0 pits everywhere … 1 only on steep sides (tafoni).</summary>
		public float Sides;
	}

	/// <summary>Flutes: vertical solution grooves running down from a top edge (rillenkarren).</summary>
	[Serializable]
	public struct FluteStyle
	{
		/// <summary>Grooves round the rock. Zero turns flutes off.</summary>
		public int Count;
		/// <summary>Groove depth, metres.</summary>
		public float Depth;
		/// <summary>How far down from the top they run, as a fraction of the height.</summary>
		public float Reach;
	}

	/// <summary>Clasts: pebbles and cobbles cemented in a finer matrix, standing out as it wears.</summary>
	[Serializable]
	public struct ClastStyle
	{
		/// <summary>Typical clast diameter, metres. Zero turns clasts off.</summary>
		public float Size;
		/// <summary>Fraction of sites holding a clast.</summary>
		public float Fill;
		/// <summary>How far a clast stands out of the matrix, as a fraction of its radius.</summary>
		public float Protrusion;
	}

	/// <summary>Columnar jointing: polygonal columns from a lava flow cooling inwards.</summary>
	[Serializable]
	public struct ColumnStyle
	{
		/// <summary>Column diameter range, metres. Zero means the rock has none.</summary>
		public Vector2 Diameter;
		/// <summary>Open joint between columns, metres.</summary>
		public float Gap;
		/// <summary>Tilt of a column's broken top, degrees.</summary>
		public Vector2 TopTilt;
		/// <summary>Fraction of columns whose top is a broken wedge rather than a cross joint.</summary>
		public float Broken;
		/// <summary>Depth of the cup (or dome) of a cross joint, metres.</summary>
		public float Cup;
	}

	/// <summary>Everything about one rock type's geometry.</summary>
	[Serializable]
	public struct RockGeometryStyle
	{
		/// <summary>Footprint depth ÷ width, min..max across variants.</summary>
		public Vector2 Aspect;
		/// <summary>Superellipsoid exponent, min..max: 2 an ellipsoid, 3–4 a rounded box.</summary>
		public Vector2 Roundness;
		/// <summary>Relative depth of the lumpy noise, 0..1.</summary>
		public float Lumpiness;
		/// <summary>Fracture planes cutting the rock (planes on a boulder, faces on a faceted rock).</summary>
		public int Facets;
		/// <summary>How far the planes cut in, 0 barely … 1 deep.</summary>
		public float FacetDepth;
		/// <summary>0 chipped and soft-edged … 1 crisp-edged.</summary>
		public float Sharpness;
		public FragmentForm Fragments;

		public BeddingStyle Bedding;
		public FoliationStyle Foliation;
		public CleavageStyle Cleavage;
		public ExfoliationStyle Exfoliation;
		public FractureStyle Fracture;
		public PitStyle Pits;
		public FluteStyle Flutes;
		public ClastStyle Clasts;
		public ColumnStyle Columns;
	}

	/// <summary>One shape a rock type is made in.</summary>
	[Serializable]
	public struct FormationShape
	{
		/// <summary>The shape's stable name; never Round, Slab or Spire, which belong to the legacy boulders.</summary>
		public string Name;
		public FormationKind Kind;
		/// <summary>Longest horizontal extent, metres. Every level of detail is fitted to it exactly.</summary>
		public float Size;
		/// <summary>Height from the lowest buried point to the top, metres.</summary>
		public float Height;
		/// <summary>Parts: beds, blocks, plates, columns, fragments. Zero lets the builder choose.</summary>
		public int Count;
		/// <summary>Dip of the structure in degrees; negative uses the style's range.</summary>
		public float Dip;
		/// <summary>Stretch of the long axis: 1 equant, 2.5 a blade. Zero means 1.</summary>
		public float Elongation;
		/// <summary>Superellipsoid exponent override; zero uses the style's range.</summary>
		public float Exponent;
		/// <summary>A crystal cluster's habit (<see cref="FormationKind.CrystalCluster"/> only).</summary>
		public CrystalHabit Habit;
		/// <summary>
		/// The surface the shape wears instead of its type's: a <see cref="RockTypes"/> name or an
		/// <see cref="IceSurfaces"/> recipe name (<see cref="RockArtNames.SurfaceMaterial"/>). Null wears the type's.
		/// </summary>
		public string Dress;
		/// <summary>
		/// The surface of a second body, submesh 1 (a glacier table's boulder, a knob's frost cap), named as for
		/// <see cref="Dress"/>. Null when the shape is one body.
		/// </summary>
		public string Cap;
		/// <summary>Flat ground cover a player walks over (sastrugi, salt polygons, frost flowers): its prefab gets no collider.</summary>
		public bool Colliderless;
	}

	/// <summary>A rock type: how it breaks, the shapes it comes in, and its surface.</summary>
	[Serializable]
	public struct RockType
	{
		/// <summary>The stable name: Granite, Basalt … Every derived asset is named for it.</summary>
		public string Name;
		public RockFamily Family;
		/// <summary>
		/// The <see cref="ProceduralArtCatalogue.RockMaterials"/> name this type stands for, or null.
		/// The legacy materials keep their names; they map onto these types, not the other way.
		/// </summary>
		public string LegacyMaterial;
		/// <summary>One line: how this rock weathers and breaks, which is what its geometry shows.</summary>
		public string Summary;
		public RockGeometryStyle Style;
		public FormationShape[] Shapes;
		/// <summary>Its surface, tiling every <see cref="RockMeshes.TextureMetres"/> as rock UVs do.</summary>
		public SurfaceRecipe Surface;
		/// <summary>
		/// A mineral, ice or alien rock made only as scattered formations: never a cliff, a river boulder or a
		/// biome's bedrock (<see cref="CliffRocks.IsCliffRock"/>). False for the sixteen rocks of the crust.
		/// </summary>
		public bool FormationsOnly;
		/// <summary>
		/// For snow and ice: its material is set up as ice (<see cref="IceSurfaces"/>' Specular workflow) over its
		/// own surface textures. <see cref="IceMaterialProposal.Surface"/> null for rock.
		/// </summary>
		public IceMaterialProposal Ice;
	}

	/// <summary>
	/// Sixteen rock types, grouped igneous, sedimentary and metamorphic, each with geometry that
	/// breaks the way the real rock breaks.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Geometry first, texture second.</b> A geologist recognises a rock across a valley by its
	/// form long before its colour: granite weathers to rounded corestones and tors because its
	/// joints are widely spaced and spheroidal weathering eats corners first; sandstone and
	/// limestone step back bed by bed because each bed has its own cement; slate parts into thin
	/// plates along a cleavage that has nothing to do with its bedding; basalt cracks into columns
	/// as a flow cools inward; obsidian, being a glass, breaks in curved conchoidal scars with
	/// edges like a blade. So each type here carries a <see cref="RockGeometryStyle"/> whose
	/// structure parameters drive <see cref="RockFormations"/>, and shapes chosen from what that
	/// rock actually forms in the field.
	/// </para>
	/// <para>
	/// <b>Surfaces from existing motifs only.</b> Each <see cref="SurfaceRecipe"/> uses the motifs
	/// <see cref="SurfaceSynth"/> already has: <see cref="SurfaceMotif.Strata"/> for bedded and
	/// foliated rocks (its bands run along v, and every rock UV here runs v up the rock, so the
	/// texture's bands lie along the geometry's beds), <see cref="SurfaceMotif.Granular"/> with
	/// speckles for crystalline and vesicular rocks, <see cref="SurfaceMotif.Cobbles"/> for
	/// conglomerate, <see cref="SurfaceMotif.Cracks"/> for marble's veins. Colours are picked from
	/// daylight photographs and authored in sRGB, the three ramp stops being the shadowed,
	/// typical and sunlit-dry tones, as in <see cref="SurfaceCatalogue"/>. Speckles stamp raised
	/// dots, so pumice's "holes" read as dark grains in the albedo rather than as relief; the
	/// geometry's pits carry the relief.
	/// </para>
	/// <para>
	/// <b>Foliation: texture and ribs agree.</b> A foliated rock's v is its folded, irregularly
	/// stretched layer coordinate (<see cref="RockFormations"/>), the same one its relief ribs are
	/// cut from, and its Strata recipe has <c>MotifCells = TextureMetres / Foliation.Wavelength</c>,
	/// so texture bands and ribs share spacing, folds, crinkles and thickness variation. They agree
	/// to within the small wobble Strata adds of its own (about a third of a band), which a recipe
	/// cannot turn off. The texture bands are kept low in contrast: the ribs carry the banding.
	/// Conglomerate cannot be aligned the same way: its texture cobbles are 2D cells in UV space and
	/// its geometric cobbles 3D cells in the rock, so the painted outlines are kept faint and the
	/// cobbles themselves are geometry.
	/// </para>
	/// <para>
	/// <b>Names.</b> Four types stand for the legacy rock materials (<see cref="RockType.LegacyMaterial"/>):
	/// Grey is granite, and Sandstone, Basalt and Limestone are themselves. No shape is called
	/// Round, Slab or Spire, so no name built from a type and a shape can collide with a legacy
	/// <c>Boulder_{material}_{shape}</c> prefab. Recipe names carry a <c>Rock</c> prefix so a
	/// rock surface never shares a name, and so a seed, with a ground family.
	/// </para>
	/// </remarks>
	public static class RockTypes
	{
		/// <summary>Seeded variants of every shape, so a field of boulders does not repeat.</summary>
		public const int VariantCount = 4;

		private static Color Hex(string hex) => SurfaceCatalogue.Hex(hex);

		private static Color[] Hexes(params string[] hex) => Array.ConvertAll(hex, Hex);

		/// <summary>A rock surface recipe, parameters in the same order as <see cref="SurfaceCatalogue"/>'s.</summary>
		private static SurfaceRecipe R(string name, float relief, string dark, string mid, string light,
			int baseCells, SurfaceMotif motif, int motifCells, float motifSize, float motifWeight, string motifDark, string motifLight,
			float smoothness, float smoothVar = 0.1f, float occlusion = 0.75f, int octaves = 5, float persistence = 0.5f, float warp = 0.05f,
			Color[] speckles = null, int speckleCount = 0, float speckleSize = 0.002f)
		{
			return new SurfaceRecipe
			{
				Name = "Rock" + name,
				TileMetres = RockMeshes.TextureMetres,
				ReliefMetres = relief,
				Dark = Hex(dark),
				Mid = Hex(mid),
				Light = Hex(light),
				BaseCells = baseCells,
				Octaves = octaves,
				Persistence = persistence,
				Warp = warp,
				Motif = motif,
				MotifCells = motifCells,
				MotifCount = 0,
				MotifSize = motifSize,
				MotifWeight = motifWeight,
				MotifDark = Hex(motifDark),
				MotifLight = Hex(motifLight),
				Vertical = false,
				Speckles = speckles,
				SpeckleCount = speckleCount,
				SpeckleSize = speckleSize,
				Smoothness = smoothness,
				SmoothnessVariation = smoothVar,
				Metallic = 0f,
				Occlusion = occlusion,
				NormalScale = 1f,
			};
		}

		private static FormationShape S(string name, FormationKind kind, float size, float height, int count = 0, float dip = -1f, float elongation = 0f, float exponent = 0f,
			CrystalHabit habit = CrystalHabit.Prismatic, string dress = null, string cap = null, bool colliderless = false)
		{
			return new FormationShape
			{
				Name = name, Kind = kind, Size = size, Height = height, Count = count, Dip = dip, Elongation = elongation, Exponent = exponent,
				Habit = habit, Dress = dress, Cap = cap, Colliderless = colliderless,
			};
		}

		/// <summary>A recipe with a metallic lustre (the mask's R channel), which <see cref="R"/> leaves at zero.</summary>
		private static SurfaceRecipe Metal(SurfaceRecipe recipe, float metallic)
		{
			recipe.Metallic = metallic;
			return recipe;
		}

		/// <summary>An ice material set-up over a type's own surface: ice's 1.8 % reflectance (#252525), as <see cref="IceSurfaces.Materials"/>.</summary>
		private static IceMaterialProposal IceLook(string name, float smoothness, float emission)
		{
			return new IceMaterialProposal { Surface = "Rock" + name, Specular = Hex("#252525"), Smoothness = smoothness, EmissionShare = emission };
		}

		private static Vector2 V(float a, float b) => new Vector2(a, b);

		// ── Igneous ───────────────────────────────────────────────────

		public static readonly RockType Granite = new RockType
		{
			Name = "Granite",
			Family = RockFamily.Igneous,
			LegacyMaterial = "Grey",
			Summary = "Widely jointed and coarse: corners weather first, leaving rounded corestones that stack into tors and shed onion-skin sheets.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.6f, 3.4f), Lumpiness = 0.35f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.1f,
				Fragments = FragmentForm.Angular,
				Exfoliation = new ExfoliationStyle { Thickness = 0.06f, Peel = 0.35f },
				Pits = new PitStyle { Spacing = 0.5f, Fill = 0.3f, Radius = 0.35f, Depth = 0.045f, Tops = 1f },
			},
			Shapes = new[]
			{
				S("Corestone", FormationKind.Boulder, 1.8f, 1.3f),
				S("Tor", FormationKind.Tor, 2.6f, 3.0f, 3),
				S("Perched", FormationKind.Perched, 2.6f, 2.3f),
				S("Split", FormationKind.Split, 2.2f, 1.5f),
			},
			// Weathered grey granite (Dartmoor, the Sierra): black biotite, white and pink feldspar, glassy quartz.
			Surface = R("Granite", 0.02f, "#5a5852", "#8a867e", "#b9b4aa", 6, SurfaceMotif.Granular, 160, 0.1f, 0.35f, "#4a4844", "#c8c4bc", 0.18f,
				speckles: Hexes("#1c1b1a", "#e9e6df", "#b8907c", "#9c9a96"), speckleCount: 14000, speckleSize: 0.0018f),
		};

		public static readonly RockType Basalt = new RockType
		{
			Name = "Basalt",
			Family = RockFamily.Igneous,
			LegacyMaterial = "Basalt",
			Summary = "A lava flow contracts as it cools inward and cracks into five- to seven-sided columns; broken pieces are sharp blocks.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.75f, 1f), Roundness = V(2.4f, 3f), Lumpiness = 0.25f, Facets = 8, FacetDepth = 0.5f, Sharpness = 0.7f,
				Fragments = FragmentForm.Angular,
				Columns = new ColumnStyle { Diameter = V(0.45f, 0.6f), Gap = 0.02f, TopTilt = V(4f, 22f), Broken = 0.35f, Cup = 0.04f },
				Fracture = new FractureStyle { Dish = 0.03f, Roughness = 0.008f, Jointed = true },
			},
			Shapes = new[]
			{
				S("Columns", FormationKind.Columns, 2.6f, 2.6f, 9),
				S("Causeway", FormationKind.Columns, 4f, 1.1f, 20),
				S("Fallen", FormationKind.FallenColumns, 2.8f, 1.2f, 4),
				S("Block", FormationKind.Faceted, 1.6f, 0.65f),
				// Flow on flow: the stacked units of a rille's rim or a tube's benches, each a ledge.
				S("Ledges", FormationKind.Ledges, 3.2f, 2.2f, 5),
				S("Scree", FormationKind.Scree, 2.6f, 1.0f, 14),
				// Venera's platy slabs: thin crust plates of a flow top, lying almost flat.
				S("Slabs", FormationKind.SlabStack, 2.0f, 0.3f, 3, dip: 3f),
				// A tessera fin: a narrow upstanding blade between parallel fractures.
				S("Fin", FormationKind.Boulder, 3.0f, 1.6f, elongation: 2.4f),
				// Lava dribble spires: drips of a cooling tube's roof, piled up from its floor.
				S("Dribble", FormationKind.Stalagmite, 0.8f, 1.6f, 3),
				S("Ventifact", FormationKind.Ventifact, 1.0f, 0.55f),
			},
			// Weathered columnar basalt: charcoal with a faint warm cast, olivine grains, vesicles.
			Surface = R("Basalt", 0.02f, "#26262a", "#3a3a3c", "#58585a", 4, SurfaceMotif.Fractured, 8, 0.05f, 0.6f, "#18181a", "#4a4a4c", 0.22f,
				speckles: Hexes("#5d6b34", "#121214"), speckleCount: 1500, speckleSize: 0.0015f),
		};

		public static readonly RockType Andesite = new RockType
		{
			Name = "Andesite",
			Family = RockFamily.Igneous,
			LegacyMaterial = null,
			Summary = "Blocky and subangular, often with platy jointing parallel to its flow; talus slopes of rough angular blocks.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.6f), Lumpiness = 0.45f, Facets = 14, FacetDepth = 0.45f, Sharpness = 0.5f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.035f, Roughness = 0.03f, Jointed = false },
				Cleavage = new CleavageStyle { Thickness = V(0.12f, 0.22f), Tilt = V(5f, 20f), EdgeFacets = 7, EdgeBevel = 0.35f },
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Faceted, 1.8f, 1.3f),
				S("Platy", FormationKind.SlabStack, 2.2f, 1.0f, 4),
				S("Talus", FormationKind.Scree, 2.6f, 1.0f, 14),
			},
			// Porphyritic grey andesite: white plagioclase laths and black hornblende in a grey groundmass.
			Surface = R("Andesite", 0.025f, "#4c4945", "#6e6a64", "#948f87", 4, SurfaceMotif.Fractured, 7, 0.06f, 0.5f, "#3c3a36", "#a29d94", 0.2f,
				speckles: Hexes("#d9d5cc", "#1d1b19"), speckleCount: 6000, speckleSize: 0.0022f),
		};

		public static readonly RockType Obsidian = new RockType
		{
			Name = "Obsidian",
			Family = RockFamily.Igneous,
			LegacyMaterial = null,
			Summary = "Volcanic glass: no grain to follow, so it breaks in curved conchoidal scars with rippled faces and razor edges.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.95f), Roundness = V(2f, 2f), Lumpiness = 0f, Facets = 11, FacetDepth = 0.7f, Sharpness = 1f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.11f, Ripples = 4, RippleRelief = 0.012f, Roughness = 0f, Jointed = false },
			},
			Shapes = new[]
			{
				S("Chunk", FormationKind.Faceted, 1.4f, 1.0f),
				S("Shard", FormationKind.Faceted, 1.6f, 0.55f, elongation: 1.5f),
				S("Scree", FormationKind.Scree, 2.0f, 0.8f, 12),
			},
			// Black glass with faint grey flow banding; glossy.
			Surface = R("Obsidian", 0.004f, "#08080a", "#121216", "#24242a", 3, SurfaceMotif.Strata, 18, 0.1f, 0.15f, "#060608", "#2c2c34", 0.85f, 0.05f, 0.3f),
		};

		public static readonly RockType Pumice = new RockType
		{
			Name = "Pumice",
			Family = RockFamily.Igneous,
			LegacyMaterial = null,
			Summary = "Froth of glass blown full of gas: light, rounded by any transport, and pitted all over with vesicles.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.75f, 0.95f), Roundness = V(2f, 2.3f), Lumpiness = 0.6f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0f,
				Fragments = FragmentForm.Rounded,
				Pits = new PitStyle { Spacing = 0.17f, Fill = 0.7f, Radius = 0.45f, Depth = 0.05f },
			},
			Shapes = new[]
			{
				S("Lump", FormationKind.Boulder, 1.2f, 0.85f),
				S("Raft", FormationKind.Boulder, 1.6f, 0.7f, exponent: 2.8f),
			},
			// Pale grey-cream pumice; the dark speckles are vesicle shadows.
			Surface = R("Pumice", 0.03f, "#9f9b90", "#c4c0b4", "#e0ddd2", 6, SurfaceMotif.Granular, 96, 0.1f, 0.7f, "#7e7a70", "#ece9e0", 0.05f, 0.05f,
				speckles: Hexes("#5e5a52", "#7a766c"), speckleCount: 9000, speckleSize: 0.0025f),
		};

		public static readonly RockType Tuff = new RockType
		{
			Name = "Tuff",
			Family = RockFamily.Igneous,
			LegacyMaterial = null,
			Summary = "Soft compacted ash: rain carves it into fairy chimneys under hard caps, and salt hollows it into tafoni honeycomb.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.75f, 1f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.5f, Facets = 3, FacetDepth = 0.3f, Sharpness = 0.1f,
				Fragments = FragmentForm.Rounded,
				Pits = new PitStyle { Spacing = 0.36f, Fill = 0.8f, Radius = 0.48f, Depth = 0.22f, Sides = 0.85f },
				Foliation = new FoliationStyle { Wavelength = 0.4f, Relief = 0.025f, Fold = 0.03f, FoldWavelength = 2f, Dip = V(0f, 6f) },
				Flutes = new FluteStyle { Count = 16, Depth = 0.045f, Reach = 0.8f },
			},
			Shapes = new[]
			{
				S("Chimney", FormationKind.Chimney, 2.0f, 4.5f),
				S("Tafoni", FormationKind.Boulder, 2.0f, 1.5f),
			},
			// Cappadocian tuff: pale buff with dark lithic fragments and white pumice lapilli.
			Surface = R("Tuff", 0.025f, "#a8926e", "#d2bf98", "#ebdcbc", 5, SurfaceMotif.Granular, 64, 0.1f, 0.4f, "#9a8462", "#f0e4c8", 0.06f, 0.05f,
				speckles: Hexes("#5a544c", "#8a7c6a", "#f2ece0"), speckleCount: 3000, speckleSize: 0.003f),
		};

		// ── Sedimentary ──────────────────────────────────────────────

		public static readonly RockType Sandstone = new RockType
		{
			Name = "Sandstone",
			Family = RockFamily.Sedimentary,
			LegacyMaterial = "Sandstone",
			Summary = "Beds of differing cement weather back at different rates: hard beds stand out as ledges, soft ones recess; hard caps make pedestals.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.55f, 0.85f), Roundness = V(3f, 4f), Lumpiness = 0.3f, Facets = 0, Sharpness = 0.4f,
				Fragments = FragmentForm.Angular,
				Bedding = new BeddingStyle { Beds = V(3f, 5f), Contrast = 0.22f, Dip = V(0f, 4f), Rounding = 0.55f, Crumble = 0.012f, Joints = 2 },
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Bedded, 2.4f, 1.6f),
				S("Ledges", FormationKind.Ledges, 3.0f, 2.4f, 6),
				S("Tilted", FormationKind.Bedded, 2.6f, 1.7f, 5, dip: 24f),
				S("Pedestal", FormationKind.Pedestal, 2.2f, 2.6f, 6),
				// A cannonball concretion: grains cemented round a nucleus, harder than the sandstone that weathered from it.
				S("Concretion", FormationKind.Boulder, 1.6f, 1.4f, elongation: 0.72f, exponent: 2f),
			},
			// The ground family's red-orange sandstone, so the legacy material maps on unchanged.
			Surface = R("Sandstone", 0.03f, "#8a4a2a", "#b86a3e", "#d89a64", 4, SurfaceMotif.Strata, 10, 0.1f, 0.6f, "#7a3c22", "#e0b080", 0.1f, 0.05f, 0.8f),
		};

		public static readonly RockType Limestone = new RockType
		{
			Name = "Limestone",
			Family = RockFamily.Sedimentary,
			LegacyMaterial = "Limestone",
			Summary = "Dissolves rather than crumbles: solution pans on its tops, flutes down its edges, clints and grikes along its joints, fluted pinnacles.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.6f, 3.4f), Lumpiness = 0.3f, Facets = 0, Sharpness = 0.3f,
				Fragments = FragmentForm.Angular,
				Bedding = new BeddingStyle { Beds = V(2f, 3f), Contrast = 0.15f, Dip = V(0f, 6f), Rounding = 0.5f, Crumble = 0.01f, Joints = 2 },
				Pits = new PitStyle { Spacing = 0.45f, Fill = 0.65f, Radius = 0.45f, Depth = 0.12f, Tops = 1f },
				Flutes = new FluteStyle { Count = 12, Depth = 0.07f, Reach = 0.5f },
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Bedded, 2.2f, 1.4f),
				S("Pinnacle", FormationKind.Pinnacle, 1.5f, 2.8f),
				S("Pavement", FormationKind.Pavement, 3.2f, 0.55f, 6),
				// Speleothems: calcite laid down film by film from dripping and flowing cave water, banded cream and
				// orange as sinter is (the same chemistry, a precipitate in laminae), so they wear sinter's surface.
				S("Stalagmite", FormationKind.Stalagmite, 1.2f, 2.6f, 3, dress: "Sinter"),
				S("Flowstone", FormationKind.Flowstone, 3.0f, 1.6f, dress: "Sinter"),
				S("Gours", FormationKind.Terrace, 3.6f, 0.5f, 5, dress: "Sinter"),
			},
			// The ground family's pale grey limestone, with fossil-shell fragments.
			Surface = R("Limestone", 0.03f, "#9a968a", "#bcb8aa", "#dcd8ca", 4, SurfaceMotif.Fractured, 5, 0.08f, 0.5f, "#8a8676", "#d0ccbc", 0.2f,
				speckles: Hexes("#ebe7da", "#8f8a7c"), speckleCount: 1200, speckleSize: 0.003f),
		};

		public static readonly RockType Shale = new RockType
		{
			Name = "Shale",
			Family = RockFamily.Sedimentary,
			LegacyMaterial = null,
			Summary = "Fissile mud rock: splits into thin plates, crumbles back between harder beds, and sheds slopes of platy chips.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(3f, 3.5f), Lumpiness = 0.3f, Facets = 0, Sharpness = 0.6f,
				Fragments = FragmentForm.Platy,
				Bedding = new BeddingStyle { Beds = V(8f, 12f), Contrast = 0.25f, Dip = V(0f, 8f), Rounding = 0.3f, Crumble = 0.02f, Joints = 3 },
				Cleavage = new CleavageStyle { Thickness = V(0.03f, 0.06f), Tilt = V(3f, 14f), EdgeFacets = 6, EdgeBevel = 0.5f },
			},
			Shapes = new[]
			{
				S("Stack", FormationKind.SlabStack, 1.8f, 0.55f, 8),
				S("Ledges", FormationKind.Ledges, 2.4f, 1.4f, 10),
				S("Scree", FormationKind.Scree, 2.4f, 0.9f, 30),
			},
			// Dark grey shale, a little brown where it weathers.
			Surface = R("Shale", 0.02f, "#2c2b29", "#46443f", "#625f58", 4, SurfaceMotif.Strata, 40, 0.1f, 0.7f, "#22211f", "#6e6a62", 0.15f),
		};

		public static readonly RockType Conglomerate = new RockType
		{
			Name = "Conglomerate",
			Family = RockFamily.Sedimentary,
			LegacyMaterial = null,
			Summary = "Rounded pebbles and cobbles cemented in sand: the matrix wears back and leaves the clasts standing out as knobs.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.3f, 3f), Lumpiness = 0.35f, Facets = 3, FacetDepth = 0.35f, Sharpness = 0.2f,
				Fragments = FragmentForm.Rounded,
				Clasts = new ClastStyle { Size = 0.3f, Fill = 0.7f, Protrusion = 0.35f },
			},
			Shapes = new[]
			{
				S("Boulder", FormationKind.Boulder, 1.8f, 1.3f),
				S("Block", FormationKind.Boulder, 2.2f, 1.4f, exponent: 3.6f),
			},
			// Brown sandy matrix full of grey and cream river cobbles.
			Surface = R("Conglomerate", 0.04f, "#6a563f", "#8c7356", "#ad9475", 4, SurfaceMotif.Cobbles, 7, 0.4f, 0.45f, "#8a8070", "#b0a690", 0.15f),
		};

		public static readonly RockType Chalk = new RockType
		{
			Name = "Chalk",
			Family = RockFamily.Sedimentary,
			LegacyMaterial = null,
			Summary = "Soft white limestone: faint thick beds, every edge rounded, with black flint nodules in bands.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.8f, 3.6f), Lumpiness = 0.2f, Facets = 2, FacetDepth = 0.25f, Sharpness = 0f,
				Fragments = FragmentForm.Rounded,
				Bedding = new BeddingStyle { Beds = V(3f, 5f), Contrast = 0.12f, Dip = V(0f, 3f), Rounding = 0.5f, Crumble = 0.008f, Joints = 1 },
				Foliation = new FoliationStyle { Wavelength = 0.3f, Relief = 0.012f, FoldWavelength = 3f, Dip = V(0f, 3f) },
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Bedded, 2.2f, 1.4f),
				S("Lump", FormationKind.Boulder, 1.4f, 0.9f),
				// Lost City's carbonate towers: white brucite and calcite chimneys where warm alkaline vent fluid meets sea
				// water, built like a smoker's (a narrow vent, flanges where fluid ponds under ledges), 2–60 m in life.
				S("Pinnacle", FormationKind.Cone, 2.6f, 8.0f, 2),
			},
			// Chalk cliff white with grey-black flint.
			Surface = R("Chalk", 0.01f, "#b9b6aa", "#dddace", "#f3f1e9", 4, SurfaceMotif.Strata, 6, 0.1f, 0.2f, "#c8c4b6", "#f6f4ee", 0.08f, 0.05f, 0.6f,
				speckles: Hexes("#2a2a2c", "#4a4a4e"), speckleCount: 120, speckleSize: 0.008f),
		};

		// ── Metamorphic ──────────────────────────────────────────────

		public static readonly RockType Slate = new RockType
		{
			Name = "Slate",
			Family = RockFamily.Metamorphic,
			LegacyMaterial = null,
			Summary = "Slaty cleavage parts it into thin flat plates with straight sharp edges, stacked, standing, or heaped as scree.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(3f, 3f), Lumpiness = 0f, Facets = 0, Sharpness = 1f,
				Fragments = FragmentForm.Platy,
				Cleavage = new CleavageStyle { Thickness = V(0.05f, 0.1f), Tilt = V(8f, 30f), EdgeFacets = 6, EdgeBevel = 0.25f },
				Fracture = new FractureStyle { Roughness = 0.004f, Jointed = true },
			},
			Shapes = new[]
			{
				S("Stack", FormationKind.SlabStack, 2.0f, 0.8f, 5),
				S("Upright", FormationKind.Standing, 1.3f, 2.0f),
				S("Scree", FormationKind.Scree, 2.2f, 0.84f, 28),
			},
			// Blue-grey slate with fine cleavage lines and the odd brassy pyrite cube.
			Surface = R("Slate", 0.008f, "#2c2f33", "#43474d", "#5d6268", 4, SurfaceMotif.Strata, 60, 0.1f, 0.5f, "#24272b", "#6a7076", 0.3f,
				speckles: Hexes("#b8a060"), speckleCount: 60, speckleSize: 0.0015f),
		};

		public static readonly RockType Schist = new RockType
		{
			Name = "Schist",
			Family = RockFamily.Metamorphic,
			LegacyMaterial = null,
			Summary = "Mica-rich and foliated: crinkled ribs of quartz stand out along the foliation, and it splits into flaggy plates.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.35f, Facets = 4, FacetDepth = 0.4f, Sharpness = 0.3f,
				Fragments = FragmentForm.Platy,
				Foliation = new FoliationStyle { Wavelength = 0.2f, Relief = 0.05f, Fold = 0.08f, FoldWavelength = 0.9f, Crenulation = 0.8f, Dip = V(20f, 60f) },
				Cleavage = new CleavageStyle { Thickness = V(0.06f, 0.12f), Tilt = V(10f, 25f), EdgeFacets = 7, EdgeBevel = 0.4f },
			},
			Shapes = new[]
			{
				S("Lump", FormationKind.Boulder, 1.8f, 1.1f),
				S("Ridge", FormationKind.Boulder, 2.6f, 1.2f, elongation: 1.8f),
				S("Flags", FormationKind.SlabStack, 2.0f, 0.7f, 4),
			},
			// Silvery grey-green mica schist with garnets.
			Surface = R("Schist", 0.02f, "#4a4a42", "#6d6d62", "#93938a", 4, SurfaceMotif.Strata, 10, 0.1f, 0.4f, "#57574e", "#8a8a7e", 0.38f, warp: 0.12f,
				speckles: Hexes("#6e2a26", "#d0d0c4"), speckleCount: 2500, speckleSize: 0.0025f),
		};

		public static readonly RockType Gneiss = new RockType
		{
			Name = "Gneiss",
			Family = RockFamily.Metamorphic,
			LegacyMaterial = null,
			Summary = "Coarse light and dark bands, often folded; massive like granite, so it weathers to rounded blocks banded on every face.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.4f, 3f), Lumpiness = 0.3f, Facets = 3, FacetDepth = 0.3f, Sharpness = 0.2f,
				Fragments = FragmentForm.Angular,
				Foliation = new FoliationStyle { Wavelength = 0.25f, Relief = 0.04f, Fold = 0.35f, FoldWavelength = 1.2f, Dip = V(10f, 70f) },
				Exfoliation = new ExfoliationStyle { Thickness = 0.04f, Peel = 0.15f },
			},
			Shapes = new[]
			{
				S("Boulder", FormationKind.Boulder, 1.9f, 1.4f),
				S("Block", FormationKind.Boulder, 2.2f, 1.5f, exponent: 3.6f),
			},
			// Banded grey gneiss: dark biotite-hornblende layers between pale quartz-feldspar ones.
			Surface = R("Gneiss", 0.025f, "#3b3936", "#77736b", "#b4aea3", 4, SurfaceMotif.Strata, 8, 0.1f, 0.45f, "#4e4b46", "#a39d92", 0.2f, warp: 0.15f,
				speckles: Hexes("#e2dcd0", "#c0988a", "#1e1c1a"), speckleCount: 5000, speckleSize: 0.002f),
		};

		public static readonly RockType Marble = new RockType
		{
			Name = "Marble",
			Family = RockFamily.Metamorphic,
			LegacyMaterial = null,
			Summary = "Recrystallised limestone: sugary and soluble, so its edges round off and rain etches shallow grooves and pans.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.9f), Roundness = V(2.8f, 3.4f), Lumpiness = 0.15f, Facets = 4, FacetDepth = 0.35f, Sharpness = 0.15f,
				Fragments = FragmentForm.Angular,
				Flutes = new FluteStyle { Count = 18, Depth = 0.02f, Reach = 0.35f },
				Pits = new PitStyle { Spacing = 0.3f, Fill = 0.3f, Radius = 0.35f, Depth = 0.025f, Tops = 1f },
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Boulder, 1.8f, 1.3f, exponent: 3.4f),
				S("Boulder", FormationKind.Boulder, 1.6f, 1.2f, exponent: 2.4f),
			},
			// White marble with soft grey veins; a little glossier than limestone.
			Surface = R("Marble", 0.008f, "#b5b3ad", "#d8d6d0", "#efede8", 4, SurfaceMotif.Cracks, 4, 0.012f, 0.15f, "#8c8c8a", "#a8a8a6", 0.45f),
		};

		public static readonly RockType Quartzite = new RockType
		{
			Name = "Quartzite",
			Family = RockFamily.Metamorphic,
			LegacyMaterial = null,
			Summary = "Fused quartz, harder than steel: breaks across its grains into angular blocks with sharp, faintly conchoidal faces.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2f, 2f), Lumpiness = 0f, Facets = 9, FacetDepth = 0.6f, Sharpness = 1f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.035f, Ripples = 2, RippleRelief = 0.004f, Roughness = 0.006f, Jointed = true },
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Faceted, 1.6f, 1.1f),
				S("Wedge", FormationKind.Faceted, 1.8f, 1.0f, elongation: 1.7f),
				S("Scree", FormationKind.Scree, 2.2f, 0.8f, 14),
			},
			// Pale pinkish-white quartzite, sugary and faintly lustrous.
			Surface = R("Quartzite", 0.015f, "#a99c90", "#cfc4b8", "#e9e2d8", 4, SurfaceMotif.Fractured, 6, 0.06f, 0.45f, "#9a8c80", "#f0ebe4", 0.4f,
				speckles: Hexes("#c49a8a"), speckleCount: 800, speckleSize: 0.0025f),
		};

		// ── Mineral, ice and alien formations (2026-10-10) ───────────
		//
		// Rocks that are made only as scattered formations, never as cliffs (FormationsOnly): evaporites and vent
		// precipitates, volcanic clinker, impact rocks, a dead moon's lag, a termite's clay, and snow and ice. Each is
		// what a lifeless, alien or frozen biome has in place of plants (vegetation design §2h).

		public static readonly RockType Halite = new RockType
		{
			Name = "Halite",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Rock salt: a drying pan's crust cracks into polygons whose edges buckle up into ridges as it grows; rain etches rough crust into jagged pinnacles; hot brine springs build rimmed terraces.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.5f, Facets = 8, FacetDepth = 0.5f, Sharpness = 0.7f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.02f, Jointed = false },
				Pits = new PitStyle { Spacing = 0.12f, Fill = 0.6f, Radius = 0.4f, Depth = 0.02f },
			},
			Shapes = new[]
			{
				// Uyuni and Bonneville: polygons 2–3 m across, ridges 5–15 cm high.
				S("Polygons", FormationKind.PolygonRidges, 5.0f, 0.22f, 5, colliderless: true),
				// Badwater's Devil's Golf Course: spires 0.3–1 m of salt and mud.
				S("Pinnacles", FormationKind.CrystalCluster, 1.6f, 0.8f, 12, habit: CrystalHabit.Jagged),
				// Dallol's terraces round its hot brine springs.
				S("Terrace", FormationKind.Terrace, 3.6f, 1.0f, 4),
			},
			// Salt-pan white, greyed and browned by blown mud; glassy cubic grains.
			Surface = R("Halite", 0.02f, "#a8a49a", "#d4d0c6", "#f2f0ea", 5, SurfaceMotif.Granular, 96, 0.1f, 0.45f, "#bcb8ae", "#fbfaf6", 0.25f, 0.1f, 0.6f,
				speckles: Hexes("#ffffff", "#8a7f6c", "#c9c2b2"), speckleCount: 6000, speckleSize: 0.002f),
		};

		public static readonly RockType Gypsum = new RockType
		{
			Name = "Gypsum",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Selenite: soft gypsum growing from brine and cave water as clear-to-milky blades with chisel tips, a metre or more long where it grew undisturbed.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.35f, Facets = 3, FacetDepth = 0.3f, Sharpness = 0.6f,
				Fragments = FragmentForm.Platy,
				Cleavage = new CleavageStyle { Thickness = V(0.03f, 0.06f), Tilt = V(5f, 20f), EdgeFacets = 6, EdgeBevel = 0.3f },
			},
			Shapes = new[]
			{
				S("Crystal", FormationKind.CrystalCluster, 1.4f, 1.3f, 9, habit: CrystalHabit.Bladed),
			},
			// Milky selenite: pearly white with faint fibrous striations along the blades.
			Surface = R("Gypsum", 0.006f, "#a9a69c", "#d8d6cc", "#f2f1ea", 3, SurfaceMotif.Fibres, 24, 0.1f, 0.25f, "#c4c1b6", "#fbfaf5", 0.6f, 0.08f, 0.4f),
		};

		public static readonly RockType Sulphur = new RockType
		{
			Name = "Sulphur",
			Family = RockFamily.Igneous,
			FormationsOnly = true,
			Summary = "Native sulphur sublimed from fumarole gas: crusts round vents, hollow chimneys and cones, sprays of needle crystals; yellow, greenish where wet, orange where hot.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.5f), Lumpiness = 0.55f, Facets = 4, FacetDepth = 0.3f, Sharpness = 0.3f,
				Fragments = FragmentForm.Rounded,
				Pits = new PitStyle { Spacing = 0.15f, Fill = 0.4f, Radius = 0.4f, Depth = 0.025f },
			},
			Shapes = new[]
			{
				// Dallol and Io: a vent pillar 0.5–3 m, open at its top.
				S("Chimney", FormationKind.Cone, 0.9f, 1.8f),
				S("Crystals", FormationKind.CrystalCluster, 0.9f, 0.6f, 26, habit: CrystalHabit.Acicular),
				// A fumarole's cone, 1–3 m, crusted in sulphur round its throat.
				S("Cone", FormationKind.Cone, 2.4f, 1.4f),
			},
			// Fumarole sulphur: lemon yellow, with green and orange where the vent is wet or hot.
			Surface = R("Sulphur", 0.02f, "#b08c18", "#d8bc30", "#f2e266", 5, SurfaceMotif.Granular, 72, 0.1f, 0.45f, "#a07c12", "#f8ee90", 0.3f, 0.1f, 0.65f,
				speckles: Hexes("#f6f0a0", "#8a9a30", "#c0701c"), speckleCount: 2500, speckleSize: 0.003f),
		};

		public static readonly RockType Sinter = new RockType
		{
			Name = "Sinter",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Siliceous sinter: opal laid down by boiling water a film at a time — nodular geyserite cones round geyser vents, scalloped rimmed terraces where the run-off spreads, banded orange and brown by microbial mats where it is still warm.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.45f, Facets = 0, Sharpness = 0.2f,
				Fragments = FragmentForm.Rounded,
				Pits = new PitStyle { Spacing = 0.2f, Fill = 0.5f, Radius = 0.4f, Depth = 0.03f },
			},
			Shapes = new[]
			{
				// Yellowstone's geyser cones, 1–4 m.
				S("Cone", FormationKind.Cone, 2.2f, 1.6f),
				S("Terrace", FormationKind.Terrace, 4.0f, 1.1f, 5),
			},
			// Grey-white sinter with orange and brown bands of microbial mat.
			Surface = R("Sinter", 0.02f, "#8f877a", "#c4bdb0", "#e8e4dc", 4, SurfaceMotif.Strata, 9, 0.1f, 0.35f, "#a0582a", "#d8b08a", 0.2f, 0.1f, 0.65f,
				speckles: Hexes("#f4f2ec", "#7a4a2a"), speckleCount: 1500, speckleSize: 0.0025f),
		};

		public static readonly RockType Scoria = new RockType
		{
			Name = "Scoria",
			Family = RockFamily.Igneous,
			FormationsOnly = true,
			Summary = "Gas-blown basaltic clinker: spatter cones of welded clots round a vent, spindle bombs twisted in flight, rough vesicular lumps; red where steam oxidised it, black where it did not.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.4f), Lumpiness = 0.7f, Facets = 3, FacetDepth = 0.25f, Sharpness = 0.1f,
				Fragments = FragmentForm.Rounded,
				Pits = new PitStyle { Spacing = 0.14f, Fill = 0.75f, Radius = 0.45f, Depth = 0.045f },
			},
			Shapes = new[]
			{
				// A spatter cone or hornito, 2–6 m, round its vent.
				S("Cone", FormationKind.Cone, 5.0f, 3.2f),
				S("Bomb", FormationKind.Boulder, 1.2f, 0.6f, elongation: 2.2f, exponent: 2f),
				S("Lump", FormationKind.Boulder, 1.0f, 0.7f),
			},
			// Oxidised red-brown scoria with black clinker and vesicle shadows.
			Surface = R("Scoria", 0.03f, "#2a1a16", "#5a2c20", "#8a4630", 6, SurfaceMotif.Granular, 80, 0.1f, 0.6f, "#1e1210", "#9a5236", 0.08f, 0.05f, 0.8f,
				speckles: Hexes("#160f0e", "#a4542e"), speckleCount: 9000, speckleSize: 0.0025f),
		};

		public static readonly RockType Breccia = new RockType
		{
			Name = "Breccia",
			Family = RockFamily.Metamorphic,
			FormationsOnly = true,
			Summary = "Impact breccia: angular clasts of shattered rock welded in a fine glassy matrix by the shock — the ejecta blocks round a fresh crater and its raised, block-strewn rim.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.35f, Facets = 10, FacetDepth = 0.5f, Sharpness = 0.7f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.025f, Roughness = 0.015f, Jointed = false },
				Clasts = new ClastStyle { Size = 0.22f, Fill = 0.6f, Protrusion = 0.25f },
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Faceted, 1.6f, 1.0f),
				S("Boulder", FormationKind.Boulder, 1.8f, 1.2f),
				// A crater 2–40 m: the rim crest about a twenty-fifth of its diameter high, ejecta blocks on it.
				S("CraterRim", FormationKind.CraterRim, 14f, 1.1f, 7),
			},
			// Grey shocked rock: angular light and dark clasts in a darker matrix.
			Surface = R("Breccia", 0.025f, "#4a4844", "#6e6b66", "#94918b", 4, SurfaceMotif.Fractured, 9, 0.06f, 0.55f, "#3a3936", "#b2aea6", 0.12f,
				speckles: Hexes("#c8c4bc", "#2a2926"), speckleCount: 4000, speckleSize: 0.003f),
		};

		public static readonly RockType Anorthosite = new RockType
		{
			Name = "Anorthosite",
			Family = RockFamily.Igneous,
			FormationsOnly = true,
			Summary = "Lunar highland rock, almost all plagioclase: bright grey-white and coarse, shock-crushed, worn to rounded blocks by micrometeorite rain.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.4f, 3f), Lumpiness = 0.3f, Facets = 4, FacetDepth = 0.3f, Sharpness = 0.2f,
				Fragments = FragmentForm.Angular,
				Pits = new PitStyle { Spacing = 0.35f, Fill = 0.25f, Radius = 0.3f, Depth = 0.02f },
			},
			Shapes = new[]
			{
				S("Boulder", FormationKind.Boulder, 1.8f, 1.2f),
			},
			// Bright plagioclase grey-white with sparse dark pyroxene grains.
			Surface = R("Anorthosite", 0.02f, "#8f8d88", "#bdbbb5", "#e0ded8", 5, SurfaceMotif.Granular, 140, 0.1f, 0.35f, "#7e7c78", "#ecebe6", 0.15f,
				speckles: Hexes("#f4f2ee", "#5a5854"), speckleCount: 7000, speckleSize: 0.002f),
		};

		public static readonly RockType Sulphide = new RockType
		{
			Name = "Sulphide",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Massive sulphide of a black smoker: 350 °C vent fluid meets cold sea water and drops iron, copper and zinc sulphides as a knobbly chimney, with flanges where hot water ponds under ledges; black, rusty where it oxidises.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.5f), Lumpiness = 0.7f, Facets = 3, FacetDepth = 0.3f, Sharpness = 0.3f,
				Fragments = FragmentForm.Angular,
				Pits = new PitStyle { Spacing = 0.25f, Fill = 0.5f, Radius = 0.4f, Depth = 0.04f },
			},
			Shapes = new[]
			{
				// East Pacific Rise smokers: 3–15 m, three flanges.
				S("Smoker", FormationKind.Cone, 2.6f, 9.0f, 3),
			},
			// Black sulphide crust with rust, brass and sulphur-yellow staining.
			Surface = R("Sulphide", 0.03f, "#141210", "#2a2420", "#4a3a2c", 5, SurfaceMotif.Granular, 64, 0.1f, 0.5f, "#0e0c0a", "#5a4430", 0.2f, 0.1f, 0.8f,
				speckles: Hexes("#7a3a18", "#a08a40", "#b0a040"), speckleCount: 3000, speckleSize: 0.003f),
		};

		public static readonly RockType Pyrite = new RockType
		{
			Name = "Pyrite",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Brassy iron sulphide in striated interlocking cubes — on Venus's highlands the radar-bright metal frost that condenses where the air is cool enough.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.75f, 0.95f), Roundness = V(2.4f, 3f), Lumpiness = 0.25f, Facets = 6, FacetDepth = 0.4f, Sharpness = 0.9f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Roughness = 0.004f, Jointed = true },
			},
			Shapes = new[]
			{
				S("Cubes", FormationKind.CrystalCluster, 1.2f, 0.8f, 10, habit: CrystalHabit.Cubic),
			},
			// Brass-yellow metal, finely striated on every cube face, tarnished brown in patches.
			Surface = Metal(R("Pyrite", 0.004f, "#6a5a2a", "#a08a46", "#d2bc72", 3, SurfaceMotif.Strata, 60, 0.1f, 0.25f, "#7a6a36", "#e0cc84", 0.55f, 0.1f, 0.5f,
				speckles: Hexes("#5a4a3a"), speckleCount: 400, speckleSize: 0.004f), 0.85f),
		};

		public static readonly RockType DarkLag = new RockType
		{
			Name = "DarkLag",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Sublimation lag: on a cold icy moon the ice of a hill sublimates away and leaves its dark dust behind, so hills erode into dark conical knobs, and the vapour refreezes on their cold tops as bright frost.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.75f, 1f), Roundness = V(2f, 2.4f), Lumpiness = 0.45f, Facets = 3, FacetDepth = 0.25f, Sharpness = 0.15f,
				Fragments = FragmentForm.Rounded,
			},
			Shapes = new[]
			{
				// Callisto's knobs, a frost cap on each.
				S("FrostKnob", FormationKind.Knob, 6.0f, 8.0f, cap: IceSurfaces.BubblyWhite),
			},
			// Dark sputtered lag, brown-black with a few paler grains.
			Surface = R("DarkLag", 0.025f, "#1e1c1a", "#34302c", "#4e4842", 6, SurfaceMotif.Granular, 64, 0.1f, 0.5f, "#161412", "#5a524a", 0.05f, 0.05f, 0.8f,
				speckles: Hexes("#6a625a", "#141210"), speckleCount: 3000, speckleSize: 0.0025f),
		};

		public static readonly RockType Termitaria = new RockType
		{
			Name = "Termitaria",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "A termite mound: lateritic clay cemented grain by grain with saliva into fluted spires and buttresses round the shafts that ventilate the nest; hard as brick and as red as the soil it was carried from.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.6f), Lumpiness = 0.5f, Facets = 0, Sharpness = 0.2f,
				Fragments = FragmentForm.Rounded,
				Pits = new PitStyle { Spacing = 0.18f, Fill = 0.35f, Radius = 0.35f, Depth = 0.02f },
				Flutes = new FluteStyle { Count = 9, Depth = 0.12f, Reach = 0.9f },
			},
			Shapes = new[]
			{
				// Macrotermes and cathedral termites: 1.5–4 m.
				S("Cathedral", FormationKind.Mound, 2.2f, 3.2f),
			},
			// Red-brown laterite, darker where damp, paler where sun-baked.
			Surface = R("Termitaria", 0.02f, "#6a3a20", "#9a5a32", "#c08050", 5, SurfaceMotif.Granular, 64, 0.1f, 0.5f, "#5a2e18", "#d0a070", 0.06f, 0.05f,
				speckles: Hexes("#5a2e18", "#d0a070"), speckleCount: 4000, speckleSize: 0.0025f),
		};

		public static readonly RockType Firn = new RockType
		{
			Name = "Firn",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Wind-packed snow and firn: wind scours it into sastrugi, sharp-prowed ridges pointing into the wind; under a high sun in dry cold air it sublimates into rows of leaning blades, penitentes; on an icy world's plains it rims sublimation pits.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.4f), Lumpiness = 0.3f, Facets = 0, Sharpness = 0.4f,
				Fragments = FragmentForm.Rounded,
			},
			Shapes = new[]
			{
				// Sastrugi 0.1–1 m high, aligned with the wind.
				S("Sastrugi", FormationKind.Sastrugi, 4.0f, 0.6f, 4, colliderless: true),
				// Penitentes 1–5 m, in rows.
				S("Penitentes", FormationKind.Penitentes, 4.5f, 3.0f, 9),
				// The rim of a sublimation pit in nitrogen ice, as a crater rim without ejecta.
				S("PitRim", FormationKind.CraterRim, 10f, 0.8f),
			},
			// Wind crust: blue-white snow, rippled, with sparkling grains.
			Surface = R("Firn", 0.015f, "#b6c2cc", "#dfe7ee", "#f8fbfd", 4, SurfaceMotif.Ripples, 10, 0.1f, 0.35f, "#c4d0da", "#ffffff", 0.3f, 0.1f, 0.45f,
				speckles: Hexes("#ffffff"), speckleCount: 800, speckleSize: 0.0015f),
			Ice = IceLook("Firn", 0.32f, 0.03f),
		};

		public static readonly RockType WaterIce = new RockType
		{
			Name = "WaterIce",
			Family = RockFamily.Metamorphic,
			FormationsOnly = true,
			Summary = "Water ice, dense and bubbly: dripstone in ice caves, hoar crystals grown from cold air, vent cones and lobate flows on icy moons, pedestals under a glacier's boulders, ridges of broken slabs where floes are pressed together.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.6f), Lumpiness = 0.25f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.3f,
				Fragments = FragmentForm.Rounded,
			},
			Shapes = new[]
			{
				S("Stalagmite", FormationKind.Stalagmite, 1.0f, 2.2f, 3),
				S("Crystal", FormationKind.CrystalCluster, 1.2f, 1.1f, 10, habit: CrystalHabit.Prismatic),
				// Frost flowers on new sea ice, and hoar: a few centimetres of feathery plates.
				S("FrostFlowers", FormationKind.CrystalCluster, 0.45f, 0.14f, 24, habit: CrystalHabit.Plates, colliderless: true),
				// Fresh frost and cryolava on an icy moon are bubbly white, not glacial blue.
				S("Cone", FormationKind.Cone, 4.0f, 2.6f, dress: IceSurfaces.BubblyWhite),
				S("Lobe", FormationKind.Lobe, 6.0f, 1.0f, dress: IceSurfaces.BubblyWhite),
				S("GlacierTable", FormationKind.Table, 2.8f, 2.4f, cap: "Granite"),
				// A dirt cone: ice under a few centimetres of meltwater gravel, which shaded it as the glacier melted down.
				S("DirtCone", FormationKind.Knob, 2.6f, 1.8f, dress: "Conglomerate"),
				// Sea ice pressed into a ridge on a frozen-through shelf or an ice moon's lineae.
				S("Ridge", FormationKind.RubbleRidge, 12f, 1.8f, 120, dress: IceSurfaces.SeaIce),
			},
			// Bubbly water ice: pale blue-white, faint fracture planes, white bubble trains.
			Surface = R("WaterIce", 0.012f, "#6f9ab4", "#a9c9dc", "#e2eff6", 3, SurfaceMotif.Fractured, 4, 0.03f, 0.25f, "#7fa8c0", "#f2f8fb", 0.8f, 0.08f, 0.35f,
				speckles: Hexes("#ffffff", "#d6e6f0"), speckleCount: 900, speckleSize: 0.0015f),
			Ice = IceLook("WaterIce", 0.85f, 0.05f),
		};

		/// <summary>The sixteen rocks of the crust: igneous, then sedimentary, then metamorphic. Every one can be a cliff.</summary>
		public static readonly RockType[] Crust =
		{
			Granite, Basalt, Andesite, Obsidian, Pumice, Tuff,
			Sandstone, Limestone, Shale, Conglomerate, Chalk,
			Slate, Schist, Gneiss, Marble, Quartzite,
		};

		/// <summary>The rocks made only as formations (<see cref="RockType.FormationsOnly"/>), after the crust in <see cref="All"/>.</summary>
		public static readonly RockType[] FormationRocks =
		{
			Halite, Gypsum, Sulphur, Sinter, Scoria, Breccia, Anorthosite, Sulphide, Pyrite, DarkLag, Termitaria, Firn, WaterIce,
		};

		/// <summary>Every rock type: the crust (igneous, sedimentary, metamorphic), then the formation-only rocks.</summary>
		public static readonly RockType[] All = Concat(Crust, FormationRocks);

		private static RockType[] Concat(RockType[] a, RockType[] b)
		{
			var all = new RockType[a.Length + b.Length];
			Array.Copy(a, all, a.Length);
			Array.Copy(b, 0, all, a.Length, b.Length);
			return all;
		}

		/// <summary>The type with this name.</summary>
		public static bool TryGet(string name, out RockType type)
		{
			foreach (RockType t in All)
			{
				if (t.Name == name)
				{
					type = t;
					return true;
				}
			}
			type = default;
			return false;
		}

		/// <summary>The type a legacy rock material (Grey, Sandstone, Basalt, Limestone) stands for.</summary>
		public static bool ForLegacyMaterial(string material, out RockType type)
		{
			foreach (RockType t in All)
			{
				if (t.LegacyMaterial == material)
				{
					type = t;
					return true;
				}
			}
			type = default;
			return false;
		}

		/// <summary>The shape of a type with this name.</summary>
		public static bool TryShape(in RockType type, string shape, out FormationShape result)
		{
			if (type.Shapes != null)
			{
				foreach (FormationShape s in type.Shapes)
				{
					if (s.Name == shape)
					{
						result = s;
						return true;
					}
				}
			}
			result = default;
			return false;
		}
	}
}
#endif
