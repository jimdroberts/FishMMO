#if UNITY_EDITOR
using System;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The rocks each biome of the biome suite owns (2026-10-25, decision 34 of the biome suite): a biome's own bedrock, its
	/// cliffs, boulders and stones (<see cref="Crust"/>), and the formation-only minerals, ices and deposits only it lays
	/// (<see cref="FormationRocks"/>). <see cref="RockTypes"/> appends both lists to its own, so every rock here is named,
	/// generated and placed exactly as the first sixteen are.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Added, never used yet.</b> No biome names any of these until its own round authors it, so generating them adds new
	/// formation, stone and surface files and changes none that exist. The order is the order they were asked for: the four
	/// lavas a volcanic cave's molten set needs (rhyolite, trachyte, dacite, komatiite) first, then the drafts' order.
	/// </para>
	/// <para>
	/// <b>Shapes on the existing builders.</b> Every shape maps onto a <see cref="FormationKind"/> the generator already has:
	/// towers and spines are <see cref="FormationKind.Pinnacle"/>s, domes, knolls, tumuli and whalebacks low
	/// <see cref="FormationKind.Boulder"/>s with their own exponent and elongation, mushroom rocks and buttes
	/// <see cref="FormationKind.Pedestal"/>s, blade rows <see cref="FormationKind.Penitentes"/>, flow fronts
	/// <see cref="FormationKind.Lobe"/>s, ramparts and ridges <see cref="FormationKind.RubbleRidge"/>s. No shape is called Round,
	/// Slab or Spire (the legacy boulders' names), so a draft's "Slab" is "Slabs" and its "Spire" a "Pinnacle" or "Tower".
	/// </para>
	/// <para>
	/// <b>Initialisation.</b> This class reads nothing of <see cref="RockTypes"/> while it initialises (its helpers are its
	/// own), because <see cref="RockTypes"/>' lists read this class's: each can be touched first without the other seeing a
	/// list that is still null.
	/// </para>
	/// </remarks>
	public static class RegionalRocks
	{
		private static Color Hex(string hex) => SurfaceCatalogue.Hex(hex);

		private static Color[] Hexes(params string[] hex) => Array.ConvertAll(hex, Hex);

		private static Vector2 V(float a, float b) => new Vector2(a, b);

		/// <summary>A rock surface recipe, as <see cref="RockTypes"/>' own (the same parameters in the same order, the same <c>Rock</c> prefix).</summary>
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

		private static SurfaceRecipe Metal(SurfaceRecipe recipe, float metallic)
		{
			recipe.Metallic = metallic;
			return recipe;
		}

		/// <summary>An ice material set-up over a type's own surface, as <see cref="RockTypes"/>' ice types.</summary>
		private static IceMaterialProposal IceLook(string name, float smoothness, float emission)
		{
			return new IceMaterialProposal { Surface = "Rock" + name, Specular = Hex("#252525"), Smoothness = smoothness, EmissionShare = emission };
		}

		private static BeddingStyle Beds(float min, float max, float contrast, float dipMin, float dipMax, float rounding, float crumble, int joints)
			=> new BeddingStyle { Beds = V(min, max), Contrast = contrast, Dip = V(dipMin, dipMax), Rounding = rounding, Crumble = crumble, Joints = joints };

		private static PitStyle Pits(float spacing, float fill, float radius, float depth, float tops = 0f, float sides = 0f)
			=> new PitStyle { Spacing = spacing, Fill = fill, Radius = radius, Depth = depth, Tops = tops, Sides = sides };

		// ── The volcanic cave's molten set, first (conflict C14) ──────

		public static readonly RockType Rhyolite = new RockType
		{
			Name = "Rhyolite",
			Family = RockFamily.Igneous,
			Summary = "Silica-rich lava too stiff to run far: flow-folded bands that it splits along into platy slabs, dome spines squeezed up like toothpaste, knolls rotted to clay and streaked ochre and rose by hot springs.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.55f, 0.85f), Roundness = V(2.4f, 3f), Lumpiness = 0.25f, Facets = 6, FacetDepth = 0.4f, Sharpness = 0.6f,
				Fragments = FragmentForm.Platy,
				Foliation = new FoliationStyle { Wavelength = 0.15f, Relief = 0.02f, Fold = 0.12f, FoldWavelength = 0.8f, Crenulation = 0.3f, Dip = V(20f, 70f) },
				Cleavage = new CleavageStyle { Thickness = V(0.08f, 0.16f), Tilt = V(5f, 25f), EdgeFacets = 6, EdgeBevel = 0.35f },
				Bedding = Beds(4f, 6f, 0.12f, 20f, 45f, 0.3f, 0.015f, 2),
			},
			Shapes = new[]
			{
				// A block splitting along its flow bands.
				S("FlowBanded", FormationKind.Bedded, 2.4f, 1.4f, 5, dip: 30f),
				// A dome spine, 4–12 m in life.
				S("Spine", FormationKind.Pinnacle, 2.6f, 7.0f),
				// A clay-cored knoll, bleached and streaked by alteration.
				S("AlteredKnoll", FormationKind.Boulder, 4.0f, 2.2f, exponent: 2.2f),
				S("Scree", FormationKind.Scree, 2.4f, 0.8f, 14),
			},
			// Rose, cream and lavender-grey with green-grey and ochre flow bands.
			Surface = R("Rhyolite", 0.02f, "#6a5048", "#b08070", "#e0cdb4", 4, SurfaceMotif.Strata, 12, 0.1f, 0.45f, "#7a8a78", "#c8a050", 0.2f, warp: 0.14f,
				speckles: Hexes("#8a8090", "#ece4d8", "#121216"), speckleCount: 2500, speckleSize: 0.0025f),
		};

		public static readonly RockType Trachyte = new RockType
		{
			Name = "Trachyte",
			Family = RockFamily.Igneous,
			Summary = "Alkali lava of sanidine laths aligned by flow: it parts into thin plates and stands in stout columns, grey-pink and glinting.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.3f, Facets = 8, FacetDepth = 0.45f, Sharpness = 0.55f,
				Fragments = FragmentForm.Platy,
				Columns = new ColumnStyle { Diameter = V(0.6f, 0.9f), Gap = 0.025f, TopTilt = V(4f, 18f), Broken = 0.3f, Cup = 0.03f },
				Cleavage = new CleavageStyle { Thickness = V(0.1f, 0.2f), Tilt = V(4f, 16f), EdgeFacets = 7, EdgeBevel = 0.3f },
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.012f, Jointed = true },
			},
			Shapes = new[]
			{
				S("Columns", FormationKind.Columns, 2.6f, 3.0f, 9),
				S("Platy", FormationKind.SlabStack, 2.4f, 0.9f, 5),
				// Rimed rock: a knob under a cap of hoar.
				S("RimeMantle", FormationKind.Knob, 1.8f, 1.4f, cap: "Firn"),
				S("Scree", FormationKind.Scree, 2.4f, 0.8f, 14),
			},
			// Grey-pink with glassy sanidine glints.
			Surface = R("Trachyte", 0.02f, "#5e5658", "#887e82", "#b4aaac", 4, SurfaceMotif.Fractured, 7, 0.06f, 0.45f, "#4a4446", "#c8bec0", 0.22f,
				speckles: Hexes("#efe8e0", "#d8ccc4", "#2a2628"), speckleCount: 4000, speckleSize: 0.0025f),
		};

		public static readonly RockType Dacite = new RockType
		{
			Name = "Dacite",
			Family = RockFamily.Igneous,
			Summary = "Intermediate dome lava: contorted flow bands with black glassy vitrophyre rinds, platy sheeting, spines pushed up from the dome.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.55f, 0.8f), Roundness = V(2f, 2.4f), Lumpiness = 0.2f, Facets = 5, FacetDepth = 0.5f, Sharpness = 0.7f,
				Fragments = FragmentForm.Angular,
				Foliation = new FoliationStyle { Wavelength = 0.2f, Relief = 0.025f, Fold = 0.15f, FoldWavelength = 1f, Crenulation = 0.5f, Dip = V(10f, 80f) },
				Cleavage = new CleavageStyle { Thickness = V(0.1f, 0.2f), Tilt = V(5f, 20f), EdgeFacets = 6, EdgeBevel = 0.3f },
				Bedding = Beds(4f, 6f, 0.1f, 10f, 40f, 0.25f, 0.015f, 2),
			},
			Shapes = new[]
			{
				S("Platy", FormationKind.SlabStack, 3.0f, 1.2f, 5),
				S("Pinnacle", FormationKind.Pinnacle, 4.0f, 9.0f),
				S("Ledges", FormationKind.Ledges, 12f, 4.0f, 5),
				S("FlowBand", FormationKind.Bedded, 6.0f, 2.0f, 5, dip: 35f),
				S("Scree", FormationKind.Scree, 4.0f, 1.3f, 14),
				S("Boulder", FormationKind.Boulder, 2.2f, 1.6f),
			},
			// Rose-grey with white feldspar, black hornblende and vitrophyre bands.
			Surface = R("Dacite", 0.02f, "#6a5e5c", "#9a8e8a", "#c4bab4", 4, SurfaceMotif.Strata, 9, 0.1f, 0.4f, "#2a2622", "#e8e2da", 0.22f, warp: 0.14f,
				speckles: Hexes("#161416", "#e8e2da", "#2a2622"), speckleCount: 5000, speckleSize: 0.0022f),
		};

		public static readonly RockType Komatiite = new RockType
		{
			Name = "Komatiite",
			Family = RockFamily.Igneous,
			Summary = "Ultramafic lava erupted at 1 600 °C: greenish-black, its flow tops criss-crossed by spinifex olivine blades, its crust rafted, toed and ridged as it ran.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.55f, 0.85f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.3f, Facets = 7, FacetDepth = 0.45f, Sharpness = 0.6f,
				Fragments = FragmentForm.Platy,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.01f, Jointed = true },
				Foliation = new FoliationStyle { Wavelength = 0.08f, Relief = 0.015f, Fold = 0.05f, FoldWavelength = 0.4f, Crenulation = 0.9f, Dip = V(30f, 80f) },
				Cleavage = new CleavageStyle { Thickness = V(0.08f, 0.18f), Tilt = V(3f, 12f), EdgeFacets = 6, EdgeBevel = 0.3f },
				Bedding = Beds(3f, 5f, 0.12f, 0f, 6f, 0.3f, 0.015f, 2),
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Faceted, 1.6f, 0.9f),
				// Scarp blocks fallen to the talus.
				S("Fallen", FormationKind.Faceted, 3.0f, 1.6f, elongation: 1.4f),
				// A rafted crust plate stranded by an overflow, its edges upturned.
				S("Raft", FormationKind.SlabStack, 4.0f, 0.8f, 3, dip: 10f),
				// Bulbous pahoehoe toes with a glass skin.
				S("Toe", FormationKind.Lobe, 2.0f, 0.6f),
				S("PressureRidge", FormationKind.RubbleRidge, 8.0f, 1.5f, 44),
				S("DribletSpire", FormationKind.Stalagmite, 1.0f, 3.0f, 3),
				S("Scree", FormationKind.Scree, 2.4f, 0.8f, 14),
			},
			// Greenish-black with criss-cross spinifex blades.
			Surface = R("Komatiite", 0.02f, "#161a16", "#2a3028", "#444c40", 4, SurfaceMotif.Fibres, 20, 0.1f, 0.45f, "#1e221c", "#5a6a52", 0.3f,
				speckles: Hexes("#5a6a52", "#0e100e"), speckleCount: 1500, speckleSize: 0.002f),
		};

		// ── The drafts' order ─────────────────────────────────────────

		public static readonly RockType Dolomite = new RockType
		{
			Name = "Dolomite",
			Family = RockFamily.Sedimentary,
			Summary = "Massive, vertically jointed and far less soluble than limestone: it stands in towers and gendarmes over huge pale screes.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(3f, 4f), Lumpiness = 0.15f, Facets = 5, FacetDepth = 0.4f, Sharpness = 0.7f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(2f, 4f, 0.06f, 0f, 10f, 0.15f, 0.02f, 4),
				Flutes = new FluteStyle { Count = 6, Depth = 0.02f, Reach = 0.25f },
			},
			Shapes = new[]
			{
				S("Tower", FormationKind.Pinnacle, 2.4f, 6.0f),
				S("Gendarme", FormationKind.Pinnacle, 1.6f, 3.5f),
				S("Block", FormationKind.Bedded, 2.2f, 1.4f),
				S("Scree", FormationKind.Scree, 2.4f, 0.8f, 14),
				// A rock glacier's blocky own-rock skin.
				S("RockGlacier", FormationKind.Lobe, 24f, 4.0f),
				// A block under a cap of rime.
				S("Rimed", FormationKind.Knob, 1.6f, 1.2f, cap: "Firn"),
			},
			Surface = R("Dolomite", 0.02f, "#8e887a", "#b8b2a2", "#dcd6c6", 4, SurfaceMotif.Fractured, 7, 0.07f, 0.5f, "#7a7468", "#ece6d6", 0.25f,
				speckles: Hexes("#c8945a", "#2e2c2a", "#f0ece2"), speckleCount: 1500, speckleSize: 0.003f),
		};

		public static readonly RockType Flysch = new RockType
		{
			Name = "Flysch",
			Family = RockFamily.Sedimentary,
			Summary = "Rhythmic turbidite beds, sandstone over shale over sandstone: the sandstone ribs stand, the shale crumbles, so ridges are ribbed and scarps stepped.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.4f, 0.7f), Roundness = V(2.6f, 3.4f), Lumpiness = 0.2f, Facets = 5, FacetDepth = 0.3f, Sharpness = 0.45f,
				Fragments = FragmentForm.Platy,
				Bedding = Beds(6f, 10f, 0.25f, 20f, 45f, 0.35f, 0.025f, 3),
				Cleavage = new CleavageStyle { Thickness = V(0.06f, 0.14f), Tilt = V(15f, 40f), EdgeFacets = 6, EdgeBevel = 0.4f },
			},
			Shapes = new[]
			{
				S("Ribs", FormationKind.Ledges, 6.0f, 1.2f, 6, dip: 35f),
				S("SlabField", FormationKind.SlabStack, 3.0f, 0.5f, 12, dip: 25f),
				S("Tor", FormationKind.Pinnacle, 2.2f, 5.0f),
				S("Scree", FormationKind.Scree, 2.4f, 0.8f, 14),
			},
			Surface = R("Flysch", 0.02f, "#4a4c42", "#70725f", "#9a9c8a", 5, SurfaceMotif.Strata, 12, 0.08f, 0.5f, "#3a3c34", "#a0a290", 0.25f,
				speckles: Hexes("#d8d8d0", "#8a5a34", "#2a2c28"), speckleCount: 1600, speckleSize: 0.003f),
		};

		public static readonly RockType Beachrock = new RockType
		{
			Name = "Beachrock",
			Family = RockFamily.Sedimentary,
			Summary = "Beach sand and shell cemented in the intertidal zone into seaward-dipping benches and pavements, honey-grey and pitted.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(2.8f, 3.6f), Lumpiness = 0.2f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.4f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(2f, 4f, 0.15f, 4f, 10f, 0.4f, 0.015f, 2),
				Pits = Pits(0.3f, 0.4f, 0.4f, 0.04f, tops: 0.7f),
			},
			Shapes = new[]
			{
				S("Pavement", FormationKind.Pavement, 3.2f, 0.4f, 6),
				S("Bench", FormationKind.Ledges, 3.6f, 0.9f, 3, dip: 8f),
				S("Slabs", FormationKind.SlabStack, 2.4f, 0.5f, 3, dip: 8f),
			},
			Surface = R("Beachrock", 0.025f, "#6e665a", "#a09682", "#cfc4ac", 4, SurfaceMotif.Strata, 8, 0.1f, 0.35f, "#5e564a", "#e0d6c0", 0.15f,
				speckles: Hexes("#f0ece0", "#8a7e6a"), speckleCount: 3000, speckleSize: 0.0025f),
		};

		public static readonly RockType Dolerite = new RockType
		{
			Name = "Dolerite",
			Family = RockFamily.Igneous,
			Summary = "A dark intrusion between basalt and gabbro: blocky-columnar joints, weathering to rounded boulders; the spotted bluestone has white feldspar spots.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.4f, 3f), Lumpiness = 0.3f, Facets = 6, FacetDepth = 0.35f, Sharpness = 0.4f,
				Fragments = FragmentForm.Angular,
				Columns = new ColumnStyle { Diameter = V(0.6f, 0.9f), Gap = 0.02f, TopTilt = V(4f, 18f), Broken = 0.3f, Cup = 0.03f },
				Exfoliation = new ExfoliationStyle { Thickness = 0.04f, Peel = 0.2f },
			},
			Shapes = new[]
			{
				// A gully reef's ridge, parallel to the swell.
				S("Ridge", FormationKind.Boulder, 3.0f, 1.4f, elongation: 2.2f),
				// A gully: a block parted along one joint.
				S("Gully", FormationKind.Split, 3.0f, 1.6f),
				S("Pinnacle", FormationKind.Pinnacle, 2.4f, 6.0f),
				S("BoulderField", FormationKind.Scree, 3.0f, 1.0f, 10),
				S("Columns", FormationKind.Columns, 2.4f, 2.6f, 7),
			},
			Surface = R("Dolerite", 0.02f, "#1c221e", "#323a34", "#525a52", 5, SurfaceMotif.Granular, 120, 0.1f, 0.4f, "#141a16", "#6a726a", 0.2f,
				speckles: Hexes("#d8d8d0", "#0e1210"), speckleCount: 2500, speckleSize: 0.003f),
		};

		public static readonly RockType Coralstone = new RockType
		{
			Name = "Coralstone",
			Family = RockFamily.Sedimentary,
			Summary = "Reef limestone grown by coral: porous cream-white, full of fossil heads and branch moulds, built into bommies, microatolls, spurs and storm-thrown blocks.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.5f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.15f,
				Fragments = FragmentForm.Rounded,
				Pits = Pits(0.2f, 0.7f, 0.45f, 0.06f),
				Clasts = new ClastStyle { Size = 0.25f, Fill = 0.4f, Protrusion = 0.2f },
			},
			Shapes = new[]
			{
				S("Bommie", FormationKind.Pinnacle, 3.0f, 5.0f),
				S("Microatoll", FormationKind.CraterRim, 4.5f, 0.9f),
				S("Spur", FormationKind.Boulder, 4.0f, 1.4f, elongation: 2.4f),
				S("StormBlock", FormationKind.Boulder, 3.0f, 1.4f, exponent: 3.6f),
			},
			Surface = R("Coralstone", 0.03f, "#a89c84", "#d2c8b0", "#ece6d4", 5, SurfaceMotif.Granular, 60, 0.12f, 0.5f, "#8e826a", "#f6f2e6", 0.08f,
				speckles: Hexes("#7a6e5a", "#f8f6ee"), speckleCount: 5000, speckleSize: 0.003f),
		};

		public static readonly RockType Suevite = new RockType
		{
			Name = "Suevite",
			Family = RockFamily.Metamorphic,
			Summary = "Impact-melt breccia: shattered basement clasts and black-green glass drawn into flame shapes, in megablocks and the overturned flap of a crater's rim.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.35f, Facets = 9, FacetDepth = 0.5f, Sharpness = 0.6f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.015f, Jointed = false },
				Clasts = new ClastStyle { Size = 0.18f, Fill = 0.55f, Protrusion = 0.22f },
				Bedding = Beds(3f, 5f, 0.12f, 20f, 30f, 0.3f, 0.02f, 2),
			},
			Shapes = new[]
			{
				S("Megablock", FormationKind.Faceted, 12f, 6.0f),
				// The overturned flap: beds dipping 25° outward.
				S("Flap", FormationKind.Ledges, 4.0f, 1.6f, 4, dip: 25f),
				S("Scree", FormationKind.Scree, 2.0f, 0.6f, 12),
				// A glassy ropy melt lobe.
				S("MeltLobe", FormationKind.Lobe, 6.0f, 0.5f, dress: "Obsidian"),
			},
			Surface = R("Suevite", 0.025f, "#4a4640", "#6e6a62", "#9a958a", 4, SurfaceMotif.Fractured, 8, 0.06f, 0.5f, "#2a3a2a", "#b8a8a0", 0.14f,
				speckles: Hexes("#0e1210", "#2a4a2a", "#b49a90"), speckleCount: 4000, speckleSize: 0.004f),
		};

		public static readonly RockType Cryolava = new RockType
		{
			Name = "Cryolava",
			Family = RockFamily.Igneous,
			Summary = "Ammonia-water ice that erupted as a slush and froze: steep lobate flow fronts, cracked tumuli, rafted crust slabs and welded spatter ramparts.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(2f, 2.4f), Lumpiness = 0.35f, Facets = 1, FacetDepth = 0.2f, Sharpness = 0.35f,
				Fragments = FragmentForm.Platy,
				Cleavage = new CleavageStyle { Thickness = V(0.1f, 0.25f), Tilt = V(5f, 20f), EdgeFacets = 6, EdgeBevel = 0.5f },
			},
			Shapes = new[]
			{
				S("FlowFront", FormationKind.Lobe, 8.0f, 2.5f),
				S("Tumulus", FormationKind.Lobe, 10f, 2.0f),
				S("Raft", FormationKind.SlabStack, 6.0f, 1.5f, 3, dip: 15f),
				S("Block", FormationKind.Boulder, 1.5f, 1.0f),
				S("SpatterRampart", FormationKind.RubbleRidge, 8.0f, 1.5f, 40),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
				// A block of fresh ammonia hydrate, cream-grey and bubbly.
				S("HydrateBlock", FormationKind.Boulder, 1.6f, 1.1f, dress: IceSurfaces.AmmoniaHydrate),
			},
			Surface = R("Cryolava", 0.012f, "#4e5a64", "#808c96", "#bcc6cc", 4, SurfaceMotif.Strata, 4, 0.1f, 0.3f, "#45505a", "#d0d8de", 0.3f,
				speckles: Hexes("#e8eef2"), speckleCount: 600, speckleSize: 0.002f),
			Ice = IceLook("Cryolava", 0.3f, 0.03f),
		};

		public static readonly RockType Marl = new RockType
		{
			Name = "Marl",
			Family = RockFamily.Sedimentary,
			Summary = "Blue-grey calcareous mudstone: crumbly and conchoidal between sparse nodular limestone bands, it slumps and rafts down a canyon's walls.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.3f, Facets = 3, FacetDepth = 0.3f, Sharpness = 0.3f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(4f, 7f, 0.18f, 0f, 8f, 0.45f, 0.02f, 2),
				Fracture = new FractureStyle { Dish = 0.03f, Roughness = 0.01f, Jointed = false },
			},
			Shapes = new[]
			{
				S("CanyonRib", FormationKind.Ledges, 4.0f, 0.8f, 4, dip: 6f),
				S("SlumpBlock", FormationKind.Boulder, 3.0f, 2.0f, exponent: 3.2f),
				S("RaftedBlock", FormationKind.SlabStack, 8.0f, 3.0f, 3),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("Marl", 0.02f, "#4e5658", "#7a8486", "#a8b0b0", 4, SurfaceMotif.Strata, 12, 0.1f, 0.35f, "#3e4648", "#c8ccc8", 0.12f,
				speckles: Hexes("#d8d6cc", "#2e3436"), speckleCount: 1200, speckleSize: 0.003f),
		};

		public static readonly RockType Duststone = new RockType
		{
			Name = "Duststone",
			Family = RockFamily.Sedimentary,
			Summary = "Lithified dust: agglutinate crust on an airless world, salt-cemented duricrust on a thin one; curled crust plates, soft clods and thin laminae.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.4f, 0.7f), Roundness = V(2.4f, 2.9f), Lumpiness = 0.25f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.15f,
				Fragments = FragmentForm.Platy,
				Clasts = new ClastStyle { Size = 0.04f, Fill = 0.2f, Protrusion = 0.2f },
				Bedding = Beds(8f, 12f, 0.15f, 0f, 6f, 0.5f, 0.02f, 2),
			},
			Shapes = new[]
			{
				S("Plates", FormationKind.SlabStack, 2.5f, 0.4f, 4, dip: 8f),
				S("Clod", FormationKind.Boulder, 0.9f, 0.6f),
				S("Hummock", FormationKind.Boulder, 6.0f, 1.5f, exponent: 2.2f),
				S("LaminaScarp", FormationKind.Ledges, 3.0f, 2.0f, 8),
				S("Scree", FormationKind.Scree, 1.5f, 0.4f, 12),
			},
			Surface = R("Duststone", 0.01f, "#4e4a44", "#7a746a", "#a8a094", 4, SurfaceMotif.Strata, 14, 0.1f, 0.3f, "#3e3a34", "#bab2a6", 0.08f,
				speckles: Hexes("#262422"), speckleCount: 1500, speckleSize: 0.002f),
		};

		public static readonly RockType Till = new RockType
		{
			Name = "Till",
			Family = RockFamily.Sedimentary,
			Summary = "Glacial boulder clay, unconsolidated: grey below, rusted red above, unsorted stones of every size; it stands in rain-rilled bluffs, slumps, and leaves its boulders as a lag.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.55f, Facets = 1, FacetDepth = 0.2f, Sharpness = 0.05f,
				Fragments = FragmentForm.Rounded,
				Clasts = new ClastStyle { Size = 0.3f, Fill = 0.35f, Protrusion = 0.4f },
				Flutes = new FluteStyle { Count = 14, Depth = 0.05f, Reach = 0.7f },
				Bedding = Beds(1f, 2f, 0.05f, 0f, 4f, 0.7f, 0.04f, 1),
			},
			Shapes = new[]
			{
				S("Kliff", FormationKind.Bedded, 4.0f, 3.0f, 2),
				S("Slump", FormationKind.Boulder, 3.0f, 1.2f, elongation: 1.5f),
				S("Lag", FormationKind.Scree, 3.0f, 0.8f, 8),
				// The erratic it was carrying: a boulder of granite.
				S("Findling", FormationKind.Boulder, 2.0f, 1.4f, exponent: 2.6f, dress: "Granite"),
			},
			Surface = R("Till", 0.03f, "#4e4a40", "#6a6458", "#a8603a", 4, SurfaceMotif.Cobbles, 9, 0.35f, 0.4f, "#5a5448", "#9a9488", 0.08f),
		};

		public static readonly RockType Travertine = new RockType
		{
			Name = "Travertine",
			Family = RockFamily.Sedimentary,
			Summary = "Spring limestone laid down where carbonate water degasses: cream-to-rust bands, porous and vuggy, draped in convex lobes and stepped in rimmed pools; soluble.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.4f, Facets = 1, FacetDepth = 0.2f, Sharpness = 0.2f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(3f, 6f, 0.14f, 0f, 8f, 0.6f, 0.015f, 1),
				Pits = Pits(0.15f, 0.6f, 0.4f, 0.04f),
			},
			Shapes = new[]
			{
				S("Curtain", FormationKind.Flowstone, 3.0f, 3.0f),
				S("Rimstone", FormationKind.Terrace, 3.6f, 0.8f, 5),
				S("Block", FormationKind.Bedded, 2.2f, 1.4f),
				S("Ledges", FormationKind.Ledges, 3.0f, 2.0f, 5),
				// An Abbe chimney: a knobbly spring-deposit chimney 5–20 m, steaming from its top.
				S("AbbeChimney", FormationKind.Cone, 2.6f, 9.0f),
			},
			Surface = R("Travertine", 0.03f, "#8a7650", "#b8a478", "#e2d4b0", 4, SurfaceMotif.Strata, 10, 0.1f, 0.45f, "#a85a2a", "#efe6cc", 0.15f, warp: 0.12f,
				speckles: Hexes("#5a4a30"), speckleCount: 2500, speckleSize: 0.004f),
		};

		public static readonly RockType Greenstone = new RockType
		{
			Name = "Greenstone",
			Family = RockFamily.Metamorphic,
			Summary = "Metabasalt, green with epidote and chlorite in its old gas holes: it takes a glacier's polish and striations, whalebacked on the stoss side and plucked in steps on the lee.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.9f), Roundness = V(2.6f, 3.4f), Lumpiness = 0.2f, Facets = 9, FacetDepth = 0.5f, Sharpness = 0.5f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(3f, 6f, 0.08f, 0f, 10f, 0.3f, 0.015f, 2),
				Fracture = new FractureStyle { Dish = 0.025f, Roughness = 0.01f, Jointed = false },
				Pits = Pits(0.12f, 0.25f, 0.35f, 0.01f),
			},
			Shapes = new[]
			{
				S("Whaleback", FormationKind.Boulder, 3.2f, 1.6f, elongation: 2.4f, exponent: 2.4f),
				S("Plucked", FormationKind.Faceted, 2.4f, 1.8f),
				S("Block", FormationKind.Faceted, 1.8f, 1.2f),
				S("Scree", FormationKind.Scree, 2.4f, 0.9f, 14),
			},
			Surface = R("Greenstone", 0.02f, "#2e3a32", "#56665a", "#869688", 4, SurfaceMotif.Fractured, 6, 0.06f, 0.45f, "#24302a", "#a8b8a4", 0.3f,
				speckles: Hexes("#d8e0d0", "#6a8a5a"), speckleCount: 1800, speckleSize: 0.003f),
		};

		public static readonly RockType Loess = new RockType
		{
			Name = "Loess",
			Family = RockFamily.Sedimentary,
			Summary = "Wind-laid silt, barely consolidated: it stands in vertical walls along its prismatic joints, leaves earth pillars under sod hats, and lines sunken lanes.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.6f, 3.4f), Lumpiness = 0.3f, Facets = 0, Sharpness = 0.3f,
				Fragments = FragmentForm.Rounded,
				Bedding = Beds(2f, 3f, 0.06f, 0f, 3f, 0.6f, 0.03f, 3),
				Columns = new ColumnStyle { Diameter = V(1f, 1.6f), Gap = 0.04f, TopTilt = V(2f, 10f), Broken = 0.15f, Cup = 0.02f },
				Flutes = new FluteStyle { Count = 10, Depth = 0.04f, Reach = 0.6f },
			},
			Shapes = new[]
			{
				S("Bluff", FormationKind.Columns, 4.0f, 5.0f, 7),
				S("Pillar", FormationKind.Chimney, 1.6f, 4.0f),
				S("Bank", FormationKind.Bedded, 3.0f, 1.5f, 2),
			},
			Surface = R("Loess", 0.02f, "#8a7454", "#b49a72", "#d8c29a", 4, SurfaceMotif.Strata, 6, 0.1f, 0.25f, "#7a6448", "#e2d0aa", 0.06f,
				speckles: Hexes("#f0e6d0", "#6a5a40"), speckleCount: 800, speckleSize: 0.003f),
		};

		public static readonly RockType Charnockite = new RockType
		{
			Name = "Charnockite",
			Family = RockFamily.Igneous,
			Summary = "High-grade orthopyroxene granite: massive and greasy olive-green with blue-grey quartz; it weathers to a honey-brown rind and peels in sheets like granite.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.6f, 3.4f), Lumpiness = 0.3f, Facets = 3, FacetDepth = 0.25f, Sharpness = 0.2f,
				Fragments = FragmentForm.Angular,
				Exfoliation = new ExfoliationStyle { Thickness = 0.05f, Peel = 0.3f },
				Bedding = Beds(3f, 5f, 0.08f, 0f, 6f, 0.5f, 0.015f, 3),
			},
			Shapes = new[]
			{
				S("Tor", FormationKind.Tor, 2.4f, 2.6f, 3),
				S("FellBlock", FormationKind.Boulder, 2.2f, 1.4f, exponent: 3.4f),
				S("Ledges", FormationKind.Ledges, 3.0f, 1.6f, 4),
				S("Scree", FormationKind.Scree, 2.4f, 0.8f, 14),
				S("Boulder", FormationKind.Boulder, 1.8f, 1.3f),
				S("Windstone", FormationKind.Ventifact, 1.0f, 0.55f),
			},
			Surface = R("Charnockite", 0.02f, "#3e4230", "#646a4e", "#9a9470", 5, SurfaceMotif.Granular, 140, 0.1f, 0.35f, "#34382a", "#b8a878", 0.28f,
				speckles: Hexes("#7a8a9a", "#c8a46a", "#1e2018"), speckleCount: 9000, speckleSize: 0.002f),
		};

		public static readonly RockType Troctolite = new RockType
		{
			Name = "Troctolite",
			Family = RockFamily.Igneous,
			Summary = "Troutstone: plagioclase spotted with olivine, the lower crust an impact excavates — steep ring massifs, knobby hills, shock-jointed plates.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2f, 2.6f), Lumpiness = 0.3f, Facets = 6, FacetDepth = 0.35f, Sharpness = 0.4f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.01f, Jointed = true },
			},
			Shapes = new[]
			{
				S("Massif", FormationKind.Faceted, 14f, 8.0f),
				S("RookKnob", FormationKind.Knob, 6.0f, 4.0f),
				S("Block", FormationKind.Boulder, 1.6f, 1.1f),
				S("ShockSlab", FormationKind.SlabStack, 4.0f, 1.0f, 4),
				S("Perched", FormationKind.Perched, 3.0f, 2.4f),
				S("Scree", FormationKind.Scree, 2.0f, 0.6f, 12),
			},
			Surface = R("Troctolite", 0.02f, "#6e7064", "#a6a69c", "#d8d6ce", 5, SurfaceMotif.Granular, 120, 0.1f, 0.35f, "#5e6056", "#e4e2da", 0.15f,
				speckles: Hexes("#5c6a34", "#8a5a2c"), speckleCount: 5000, speckleSize: 0.008f),
		};

		public static readonly RockType Laterite = new RockType
		{
			Name = "Laterite",
			Family = RockFamily.Sedimentary,
			Summary = "Tropical ironstone duricrust over soft mottled clay: flat hard caps that undercut into overhangs, vermiform tubes and pisolites, bowal plates.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.45f, Facets = 8, FacetDepth = 0.45f, Sharpness = 0.25f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(3f, 4f, 0.3f, 0f, 4f, 0.4f, 0.03f, 2),
				Fracture = new FractureStyle { Dish = 0.025f, Roughness = 0.01f, Jointed = false },
				Pits = Pits(0.12f, 0.55f, 0.4f, 0.03f),
			},
			Shapes = new[]
			{
				// A breakaway: the cap ledge over its undercut clay face.
				S("Breakaway", FormationKind.Pedestal, 4.0f, 3.0f, 4),
				S("Pavement", FormationKind.Pavement, 3.2f, 0.4f, 6),
				S("Block", FormationKind.Faceted, 1.4f, 0.8f),
			},
			Surface = R("Laterite", 0.03f, "#34160c", "#6a2e18", "#9a4a28", 5, SurfaceMotif.Fractured, 10, 0.08f, 0.5f, "#2a1008", "#c08040", 0.06f,
				speckles: Hexes("#2a1a10", "#c88a4a"), speckleCount: 4000, speckleSize: 0.003f),
		};

		public static readonly RockType Molasse = new RockType
		{
			Name = "Molasse",
			Family = RockFamily.Sedimentary,
			Summary = "Soft grey-green glauconitic sandstone with freshwater-snail bands: it weathers round, ledged, and honeycombed into tafoni at the waterline.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(3f, 3.8f), Lumpiness = 0.3f, Facets = 0, Sharpness = 0.2f,
				Fragments = FragmentForm.Rounded,
				Bedding = Beds(3f, 5f, 0.2f, 0f, 6f, 0.7f, 0.015f, 1),
				Pits = Pits(0.32f, 0.7f, 0.45f, 0.11f, sides: 0.8f),
			},
			Shapes = new[]
			{
				S("Ledges", FormationKind.Ledges, 3.0f, 2.4f, 5),
				S("Tafoni", FormationKind.Boulder, 2.2f, 1.6f),
				S("Bluff", FormationKind.Bedded, 3.2f, 2.4f, 4),
				S("Block", FormationKind.Bedded, 2.2f, 1.4f),
			},
			Surface = R("Molasse", 0.025f, "#5e6252", "#8a8e78", "#b6b8a2", 4, SurfaceMotif.Strata, 9, 0.1f, 0.4f, "#4e5244", "#c8c8b0", 0.1f,
				speckles: Hexes("#3e5a3a", "#e0dccc"), speckleCount: 2500, speckleSize: 0.0025f),
		};

		public static readonly RockType CoralRag = new RockType
		{
			Name = "CoralRag",
			Family = RockFamily.Sedimentary,
			Summary = "A raised fossil reef karstified on land: black-grey razor pinnacles and pits, cream where fresh-broken, red earth in the hollows, mushroom rocks undercut by the tide; soluble.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.55f, Facets = 9, FacetDepth = 0.5f, Sharpness = 0.6f,
				Fragments = FragmentForm.Angular,
				Pits = Pits(0.14f, 0.8f, 0.45f, 0.07f),
				Fracture = new FractureStyle { Dish = 0.025f, Roughness = 0.01f, Jointed = false },
				Flutes = new FluteStyle { Count = 14, Depth = 0.05f, Reach = 0.6f },
				Bedding = Beds(2f, 4f, 0.2f, 0f, 4f, 0.3f, 0.03f, 2),
			},
			Shapes = new[]
			{
				// A champignon: a mushroom rock notched by bioerosion at the tide line.
				S("Champignon", FormationKind.Pedestal, 3.0f, 3.0f, 3),
				// Lapiaz: a knee-high field of razor pinnacles.
				S("Lapiaz", FormationKind.CrystalCluster, 2.0f, 0.6f, 12, habit: CrystalHabit.Jagged),
				S("Notch", FormationKind.Ledges, 3.2f, 1.6f, 3),
				S("Block", FormationKind.Faceted, 1.6f, 1.0f),
			},
			Surface = R("CoralRag", 0.035f, "#2a2a28", "#4a4a46", "#8a867a", 5, SurfaceMotif.Granular, 72, 0.12f, 0.55f, "#1e1e1c", "#e6dcc4", 0.06f,
				speckles: Hexes("#8a3a22", "#ece4cc"), speckleCount: 4000, speckleSize: 0.003f),
		};

		public static readonly RockType Cryokarst = new RockType
		{
			Name = "Cryokarst",
			Family = RockFamily.Sedimentary,
			Summary = "Water ice cemented with organic sediment, dissolved by methane rain: rampart rims, fluted pinnacles and the sheer-sided mesas of a labyrinth.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 1f), Roundness = V(2f, 2.4f), Lumpiness = 0.25f, Facets = 2, FacetDepth = 0.25f, Sharpness = 0.6f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(2f, 4f, 0.1f, 0f, 4f, 0.3f, 0.015f, 3),
				Pits = Pits(0.25f, 0.5f, 0.4f, 0.05f, tops: 0.8f),
				Flutes = new FluteStyle { Count = 14, Depth = 0.06f, Reach = 0.8f },
			},
			Shapes = new[]
			{
				S("Rampart", FormationKind.RubbleRidge, 12f, 2.5f, 60),
				S("FlutedPinnacle", FormationKind.Pinnacle, 1.2f, 4.0f),
				S("LabyrinthMesa", FormationKind.Bedded, 10f, 4.0f, 3),
				S("SolutionBlock", FormationKind.Boulder, 1.5f, 1.0f),
				S("Scree", FormationKind.Scree, 2.0f, 0.7f, 12),
			},
			Surface = R("Cryokarst", 0.015f, "#4e483e", "#8a8272", "#c4bca8", 4, SurfaceMotif.Strata, 6, 0.1f, 0.35f, "#4a3418", "#d4ccb8", 0.25f),
		};

		public static readonly RockType Phonolite = new RockType
		{
			Name = "Phonolite",
			Family = RockFamily.Igneous,
			Summary = "Clinkstone, which rings when struck: fine grey-green with white sanidine laths, platy-jointed into thin slabs, standing in columnar plugs and necks under a pale rind.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(2.4f, 3f), Lumpiness = 0.3f, Facets = 6, FacetDepth = 0.35f, Sharpness = 0.5f,
				Fragments = FragmentForm.Platy,
				Columns = new ColumnStyle { Diameter = V(0.9f, 1.5f), Gap = 0.03f, TopTilt = V(2f, 14f), Broken = 0.25f, Cup = 0.03f },
				Foliation = new FoliationStyle { Wavelength = 0.12f, Relief = 0.02f, FoldWavelength = 2f, Dip = V(0f, 12f) },
				Cleavage = new CleavageStyle { Thickness = V(0.04f, 0.09f), Tilt = V(4f, 12f), EdgeFacets = 6, EdgeBevel = 0.3f },
				Bedding = Beds(4f, 6f, 0.12f, 0f, 8f, 0.4f, 0.015f, 2),
			},
			Shapes = new[]
			{
				S("Plug", FormationKind.Columns, 5.0f, 8.0f, 9),
				S("Organ", FormationKind.Columns, 2.6f, 5.0f, 12),
				S("Clinkslabs", FormationKind.SlabStack, 2.4f, 0.25f, 6, dip: 8f),
				S("Rind", FormationKind.Boulder, 1.8f, 1.3f, exponent: 2.6f),
				S("Ledges", FormationKind.Ledges, 3.5f, 2.0f, 5),
				S("Scree", FormationKind.Scree, 2.4f, 0.8f, 14),
			},
			Surface = R("Phonolite", 0.02f, "#4a5046", "#6e7466", "#9aa090", 5, SurfaceMotif.Fractured, 6, 0.06f, 0.5f, "#3a3e36", "#b0b4a4", 0.25f,
				speckles: Hexes("#e8e8dc", "#c0c0b0", "#222420"), speckleCount: 1800, speckleSize: 0.003f),
		};

		public static readonly RockType RaftIce = new RockType
		{
			Name = "RaftIce",
			Family = RockFamily.Metamorphic,
			Summary = "A dwarf planet's water-ice bedrock: rigid, blocky and jointed like granite, tholin-dusted red on its ledges, rafted and jumbled into montes where nitrogen ice carried it.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 1f), Roundness = V(2f, 2.4f), Lumpiness = 0.15f, Facets = 4, FacetDepth = 0.35f, Sharpness = 0.75f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.01f, Roughness = 0.006f, Jointed = true },
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Faceted, 6.0f, 3.0f),
				S("Montes", FormationKind.Scree, 30f, 18f, 12),
				S("TiltSlab", FormationKind.Standing, 2.0f, 6.0f),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("RaftIce", 0.014f, "#5a4a42", "#9a8a80", "#d8d0c8", 4, SurfaceMotif.Fractured, 6, 0.06f, 0.45f, "#6a3020", "#f2eee6", 0.4f,
				speckles: Hexes("#6a3020"), speckleCount: 600, speckleSize: 0.003f),
		};

		public static readonly RockType Mudstone = new RockType
		{
			Name = "Mudstone",
			Family = RockFamily.Sedimentary,
			Summary = "Non-fissile mud rock: warm grey-brown, conchoidal-blocky, mottled by burrows and rusted where pyrite in them oxidised, manganese-black on its skins.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.3f, Facets = 9, FacetDepth = 0.5f, Sharpness = 0.4f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(3f, 5f, 0.1f, 0f, 6f, 0.4f, 0.02f, 2),
				Fracture = new FractureStyle { Dish = 0.03f, Roughness = 0.01f, Jointed = false },
			},
			Shapes = new[]
			{
				S("SlopeLedge", FormationKind.Ledges, 3.0f, 1.0f, 4, dip: 4f),
				S("Block", FormationKind.Faceted, 2.5f, 2.0f),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("Mudstone", 0.02f, "#4a4038", "#6e6256", "#968a7c", 4, SurfaceMotif.Fractured, 6, 0.06f, 0.4f, "#2a2420", "#a87a4a", 0.12f,
				speckles: Hexes("#8a5a30", "#1e1a18"), speckleCount: 2000, speckleSize: 0.003f),
		};

		public static readonly RockType Redbed = new RockType
		{
			Name = "Redbed",
			Family = RockFamily.Sedimentary,
			Summary = "Thin-bedded red siltstone and mudstone under a white gypsum caprock: buttes and rimrock over undercut red slopes, satin-spar veins and green reduction spots.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.4f, 0.7f), Roundness = V(2f, 2.6f), Lumpiness = 0.3f, Facets = 4, FacetDepth = 0.2f, Sharpness = 0.35f,
				Fragments = FragmentForm.Platy,
				Bedding = Beds(8f, 12f, 0.25f, 0f, 4f, 0.35f, 0.025f, 2),
			},
			Shapes = new[]
			{
				S("Butte", FormationKind.Pedestal, 6.0f, 4.0f, 5),
				S("Rimrock", FormationKind.Ledges, 3.2f, 1.8f, 4),
				S("Slump", FormationKind.Bedded, 2.4f, 1.2f, 3, dip: 25f),
				S("Scree", FormationKind.Scree, 2.2f, 0.7f, 14),
			},
			Surface = R("Redbed", 0.01f, "#5e2618", "#8e4430", "#c8785a", 5, SurfaceMotif.Strata, 90, 0.12f, 0.4f, "#3a1a12", "#ece6da", 0.2f,
				speckles: Hexes("#f0ece4", "#7a8a6a", "#b07a30"), speckleCount: 8000, speckleSize: 0.0015f),
		};

		public static readonly RockType SableIce = new RockType
		{
			Name = "SableIce",
			Family = RockFamily.Metamorphic,
			Summary = "Dirty water ice and rock dust under a radiation-darkened skin: sublimation softens every edge, graben walls show the bright ice beneath.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2f, 2.4f), Lumpiness = 0.35f, Facets = 3, FacetDepth = 0.2f, Sharpness = 0.25f,
				Fragments = FragmentForm.Rounded,
				Bedding = Beds(3f, 5f, 0.12f, 0f, 6f, 0.6f, 0.02f, 2),
			},
			Shapes = new[]
			{
				S("Scarp", FormationKind.Ledges, 20f, 8.0f, 4),
				S("SoftRim", FormationKind.CraterRim, 12f, 1.2f, 4),
				S("Block", FormationKind.Boulder, 3.0f, 2.0f),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("SableIce", 0.02f, "#241d18", "#3e342c", "#6a5e52", 5, SurfaceMotif.Granular, 60, 0.1f, 0.4f, "#1a1410", "#c8ccd0", 0.15f,
				speckles: Hexes("#e8eaee", "#c8ccd0"), speckleCount: 1200, speckleSize: 0.003f),
		};

		public static readonly RockType Norite = new RockType
		{
			Name = "Norite",
			Family = RockFamily.Igneous,
			Summary = "Highland plagioclase and orthopyroxene, salt-and-pepper grey: jointed blocks split by thermal and impact shock, zap-pitted by micrometeorites.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.3f, Facets = 6, FacetDepth = 0.35f, Sharpness = 0.35f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.01f, Jointed = true },
				Pits = Pits(0.4f, 0.2f, 0.25f, 0.015f),
				Bedding = Beds(3f, 5f, 0.08f, 0f, 8f, 0.3f, 0.015f, 3),
			},
			Shapes = new[]
			{
				S("Boulder", FormationKind.Boulder, 1.8f, 1.3f),
				S("Split", FormationKind.Split, 2.2f, 1.5f),
				S("Block", FormationKind.Faceted, 1.6f, 1.0f),
				S("Ledges", FormationKind.Ledges, 4.0f, 2.0f, 4),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("Norite", 0.02f, "#5a5a58", "#8a8884", "#bebcb6", 5, SurfaceMotif.Granular, 120, 0.1f, 0.4f, "#2a2a2a", "#d8d6d0", 0.12f,
				speckles: Hexes("#1e1e1e", "#e2e0da"), speckleCount: 8000, speckleSize: 0.003f),
		};

		public static readonly RockType Variolite = new RockType
		{
			Name = "Variolite",
			Family = RockFamily.Igneous,
			Summary = "Basalt quenched on a channel's chilled margin, studded with pale spherulites that weather out raised: rolled boulders, fault blocks with one fresh face, pocked knobs.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.85f), Roundness = V(2.4f, 3f), Lumpiness = 0.35f, Facets = 5, FacetDepth = 0.3f, Sharpness = 0.3f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.012f, Jointed = true },
				Clasts = new ClastStyle { Size = 0.05f, Fill = 0.45f, Protrusion = 0.3f },
			},
			Shapes = new[]
			{
				S("Boulder", FormationKind.Boulder, 2.4f, 1.6f, exponent: 2.6f),
				S("Block", FormationKind.Faceted, 1.6f, 1.0f),
				S("FaultBlock", FormationKind.Faceted, 6.0f, 3.0f, elongation: 1.6f),
				S("Knob", FormationKind.Knob, 3.5f, 2.4f),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("Variolite", 0.02f, "#2a2c26", "#474a42", "#6a6d62", 5, SurfaceMotif.Granular, 110, 0.1f, 0.4f, "#202220", "#8a8c80", 0.18f,
				speckles: Hexes("#a8aa9c", "#c4c6b8"), speckleCount: 3000, speckleSize: 0.01f),
		};

		public static readonly RockType Greywacke = new RockType
		{
			Name = "Greywacke",
			Family = RockFamily.Sedimentary,
			Summary = "Dark grey-green dense sandstone with a slaty cleavage and white quartz veins, weeping rust: steep-dipping ribs and bluffs, reefs across a river's bed.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.25f, Facets = 6, FacetDepth = 0.4f, Sharpness = 0.6f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(4f, 6f, 0.1f, 30f, 70f, 0.2f, 0.015f, 3),
				Cleavage = new CleavageStyle { Thickness = V(0.06f, 0.14f), Tilt = V(40f, 70f), EdgeFacets = 6, EdgeBevel = 0.3f },
			},
			Shapes = new[]
			{
				S("Ribs", FormationKind.Ledges, 6.0f, 1.4f, 5, dip: 30f),
				S("Bluff", FormationKind.Bedded, 4.0f, 3.0f, 5, dip: 40f),
				S("Slabs", FormationKind.SlabStack, 2.4f, 0.6f, 4, dip: 30f),
				// A lone stack standing in the current.
				S("Midstream", FormationKind.Pinnacle, 3.0f, 6.0f),
				// A bedrock rib across the channel.
				S("Reef", FormationKind.Boulder, 3.0f, 1.4f, elongation: 2.2f),
				S("Scree", FormationKind.Scree, 2.4f, 0.8f, 14),
			},
			Surface = R("Greywacke", 0.02f, "#2a302c", "#464e48", "#6a726a", 4, SurfaceMotif.Cracks, 5, 0.012f, 0.25f, "#d8d8d0", "#f0f0ea", 0.2f,
				speckles: Hexes("#8a5a30"), speckleCount: 600, speckleSize: 0.004f),
		};

		public static readonly RockType Flagrock = new RockType
		{
			Name = "Flagrock",
			Family = RockFamily.Sedimentary,
			Summary = "Laminated lake-bed siltstone that splits into flat flags: vertical joints, stair-stepped ledges, rectangular blocks, sea stacks and wave-cut platforms.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(3f, 3.6f), Lumpiness = 0.1f, Facets = 0, Sharpness = 0.8f,
				Fragments = FragmentForm.Platy,
				Bedding = Beds(10f, 16f, 0.12f, 0f, 6f, 0.15f, 0.01f, 4),
				Cleavage = new CleavageStyle { Thickness = V(0.03f, 0.12f), Tilt = V(0f, 6f), EdgeFacets = 5, EdgeBevel = 0.2f },
			},
			Shapes = new[]
			{
				S("Stack", FormationKind.Pinnacle, 3.0f, 8.0f),
				S("Ledges", FormationKind.SlabStack, 3.0f, 0.8f, 6),
				S("Block", FormationKind.Bedded, 2.0f, 1.2f, 4),
				S("Flags", FormationKind.SlabStack, 1.5f, 0.3f, 5),
				S("Scree", FormationKind.Scree, 2.2f, 0.7f, 20),
			},
			Surface = R("Flagrock", 0.015f, "#2e2a26", "#4c4640", "#6e665c", 4, SurfaceMotif.Strata, 30, 0.1f, 0.5f, "#26221e", "#7a7266", 0.15f,
				speckles: Hexes("#d8d4cc"), speckleCount: 300, speckleSize: 0.004f),
		};

		public static readonly RockType Hornfels = new RockType
		{
			Name = "Hornfels",
			Family = RockFamily.Metamorphic,
			Summary = "Baked in a contact aureole: blue-black, flinty and spotted with cordierite knots, it breaks conchoidally into splinters, combs and needles under a rusty skin.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.3f, 0.6f), Roundness = V(2f, 2.4f), Lumpiness = 0.1f, Facets = 10, FacetDepth = 0.6f, Sharpness = 0.95f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.06f, Ripples = 2, RippleRelief = 0.004f, Roughness = 0.004f, Jointed = false },
			},
			Shapes = new[]
			{
				// A comb: a row of serrated fins.
				S("Comb", FormationKind.Penitentes, 6.0f, 4.0f, 6),
				S("Pinnacle", FormationKind.Pinnacle, 2.0f, 8.0f),
				S("Block", FormationKind.Faceted, 2.0f, 1.5f),
				S("Split", FormationKind.Split, 3.0f, 1.8f),
				S("Scree", FormationKind.Scree, 2.4f, 0.8f, 13),
			},
			Surface = R("Hornfels", 0.012f, "#1e1e26", "#34323e", "#55505e", 4, SurfaceMotif.Fractured, 6, 0.05f, 0.45f, "#16161c", "#6a6070", 0.3f,
				speckles: Hexes("#0e0e12", "#7a4a2a"), speckleCount: 3000, speckleSize: 0.003f),
		};

		public static readonly RockType Platebasalt = new RockType
		{
			Name = "Platebasalt",
			Family = RockFamily.Igneous,
			Summary = "Lowland basalt of a furnace world, sheet-jointed into thin plates: platy slabs and stacks, flow-front ledges and blistered pressure tumuli under a red hematite rind.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.3f, 0.5f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.15f, Facets = 6, FacetDepth = 0.35f, Sharpness = 0.75f,
				Fragments = FragmentForm.Platy,
				Fracture = new FractureStyle { Roughness = 0.006f, Jointed = true },
				Pits = Pits(0.25f, 0.15f, 0.2f, 0.02f),
				Bedding = Beds(5f, 7f, 0.1f, 0f, 4f, 0.2f, 0.01f, 3),
				Cleavage = new CleavageStyle { Thickness = V(0.06f, 0.12f), Tilt = V(1f, 6f), EdgeFacets = 6, EdgeBevel = 0.25f },
			},
			Shapes = new[]
			{
				S("Slabs", FormationKind.SlabStack, 2.0f, 0.3f, 3, dip: 3f),
				S("PlateStack", FormationKind.Bedded, 1.8f, 0.8f, 6),
				S("Block", FormationKind.Faceted, 1.4f, 0.6f),
				S("FlowFront", FormationKind.Ledges, 2.8f, 1.4f, 3),
				S("Blister", FormationKind.Boulder, 1.6f, 0.6f, exponent: 2.2f),
				S("Scree", FormationKind.Scree, 2.4f, 0.9f, 14),
			},
			Surface = R("Platebasalt", 0.015f, "#24201e", "#3a3430", "#5a4e46", 4, SurfaceMotif.Fractured, 8, 0.05f, 0.5f, "#1a1614", "#7a3a24", 0.2f,
				speckles: Hexes("#6a3424", "#d8d2c4"), speckleCount: 1500, speckleSize: 0.002f),
		};

		public static readonly RockType Saltrock = new RockType
		{
			Name = "Saltrock",
			Family = RockFamily.Sedimentary,
			Summary = "Bedded rock salt banded with red-brown clay: rain flutes it into ridged pinnacles, it rises in diapirs, and breaks into angular fragments; it dissolves, but never into a limestone's caves.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.3f, Facets = 4, FacetDepth = 0.3f, Sharpness = 0.6f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(5f, 8f, 0.15f, 0f, 20f, 0.3f, 0.02f, 2),
				Flutes = new FluteStyle { Count = 18, Depth = 0.05f, Reach = 0.7f },
			},
			Shapes = new[]
			{
				S("Bedded", FormationKind.Bedded, 2.4f, 1.6f, 6),
				S("Fluted", FormationKind.Pinnacle, 1.6f, 3.0f),
				S("Diapir", FormationKind.Knob, 8.0f, 4.0f),
				S("Scree", FormationKind.Scree, 2.2f, 0.7f, 14),
			},
			Surface = R("Saltrock", 0.02f, "#8a7a6c", "#c4bcb0", "#ece8e0", 4, SurfaceMotif.Strata, 14, 0.1f, 0.45f, "#8a4a30", "#f4f2ec", 0.3f,
				speckles: Hexes("#ffffff", "#7a4a30"), speckleCount: 3000, speckleSize: 0.002f),
		};

		public static readonly RockType Granodiorite = new RockType
		{
			Name = "Granodiorite",
			Family = RockFamily.Igneous,
			Summary = "Salt-and-pepper plutonic rock with dark rounded enclaves and aplite dykes: joint blocks, exfoliation sheets, pinnacles, block streams and rocking stones.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.85f), Roundness = V(2.4f, 3.2f), Lumpiness = 0.2f, Facets = 5, FacetDepth = 0.35f, Sharpness = 0.6f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.015f, Roughness = 0.01f, Jointed = true },
				Exfoliation = new ExfoliationStyle { Thickness = 0.05f, Peel = 0.25f },
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Faceted, 2.0f, 1.5f),
				S("Sheet", FormationKind.SlabStack, 3.0f, 0.4f, 3),
				S("Pinnacle", FormationKind.Pinnacle, 2.0f, 8.0f),
				S("Talus", FormationKind.Scree, 4.0f, 1.2f, 14),
				S("StreamBlocks", FormationKind.Scree, 4.0f, 1.3f, 10),
				S("Rocker", FormationKind.Perched, 2.8f, 2.6f),
			},
			Surface = R("Granodiorite", 0.02f, "#4e4e4c", "#7e7c76", "#b2aea4", 6, SurfaceMotif.Granular, 150, 0.1f, 0.35f, "#3a3a3c", "#d0ccc4", 0.18f,
				speckles: Hexes("#1e1e1e", "#e0ded6", "#3a3a3c"), speckleCount: 14000, speckleSize: 0.0018f),
		};

		public static readonly RockType Caliche = new RockType
		{
			Name = "Caliche",
			Family = RockFamily.Sedimentary,
			Summary = "Calcrete grown in a dry soil: cream-white nodular hardpan with a laminar rind, standing as benches along wash banks and breaking into plates.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(2.6f, 3.4f), Lumpiness = 0.4f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.3f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(2f, 3f, 0.2f, 0f, 4f, 0.5f, 0.03f, 3),
				Pits = Pits(0.2f, 0.4f, 0.4f, 0.03f),
			},
			Shapes = new[]
			{
				S("Bench", FormationKind.Ledges, 3.0f, 0.8f, 2),
				S("Plate", FormationKind.SlabStack, 2.0f, 0.3f, 3, dip: 6f),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("Caliche", 0.02f, "#a89a84", "#d0c4ae", "#ece4d4", 5, SurfaceMotif.Strata, 10, 0.1f, 0.3f, "#c09a88", "#f6f0e4", 0.08f,
				speckles: Hexes("#e8dcc8", "#b08a78"), speckleCount: 3000, speckleSize: 0.003f),
		};

		public static readonly RockType Hyaloclastite = new RockType
		{
			Name = "Hyaloclastite",
			Family = RockFamily.Igneous,
			Summary = "Lava shattered by quenching under water: black glass shards jigsaw-fitted in amber palagonite, bedded breccia with pillow fragments, wet-sheened.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.9f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.35f, Facets = 10, FacetDepth = 0.55f, Sharpness = 0.55f,
				Fragments = FragmentForm.Angular,
				Clasts = new ClastStyle { Size = 0.12f, Fill = 0.6f, Protrusion = 0.2f },
				Fracture = new FractureStyle { Dish = 0.025f, Roughness = 0.01f, Jointed = false },
				Bedding = Beds(3f, 5f, 0.15f, 5f, 20f, 0.3f, 0.02f, 2),
			},
			Shapes = new[]
			{
				S("BrecciaLedge", FormationKind.Ledges, 3.0f, 1.0f, 4),
				S("Buttress", FormationKind.Faceted, 4.0f, 2.0f),
				S("Scree", FormationKind.Scree, 2.0f, 0.7f, 12),
			},
			Surface = R("Hyaloclastite", 0.025f, "#141414", "#2a2420", "#5a4630", 4, SurfaceMotif.Fractured, 10, 0.06f, 0.5f, "#0a0a0a", "#a87a3a", 0.35f,
				speckles: Hexes("#c08a3a", "#0a0a0c"), speckleCount: 5000, speckleSize: 0.003f),
		};

		public static readonly RockType Eclogite = new RockType
		{
			Name = "Eclogite",
			Family = RockFamily.Metamorphic,
			Summary = "High-pressure rock of green omphacite studded with red garnets under a white mica rind: massive, so it is bared by the wind into domes and blocks.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.4f, 3f), Lumpiness = 0.25f, Facets = 4, FacetDepth = 0.3f, Sharpness = 0.35f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.01f, Jointed = true },
				Pits = Pits(0.18f, 0.2f, 0.3f, 0.012f),
			},
			Shapes = new[]
			{
				S("Knoll", FormationKind.Boulder, 4.0f, 2.2f, exponent: 2.2f),
				S("Block", FormationKind.Faceted, 1.8f, 1.2f),
				S("Boulder", FormationKind.Boulder, 1.8f, 1.3f),
				S("Perched", FormationKind.Perched, 2.6f, 2.2f),
				S("Scree", FormationKind.Scree, 2.2f, 0.7f, 14),
			},
			Surface = R("Eclogite", 0.02f, "#1e2a22", "#38463a", "#5c6a58", 5, SurfaceMotif.Granular, 120, 0.1f, 0.4f, "#141c16", "#d8d8cc", 0.3f,
				speckles: Hexes("#8a1e1e", "#b03a30", "#e0e0d4"), speckleCount: 6000, speckleSize: 0.003f),
		};

		public static readonly RockType Porphyry = new RockType
		{
			Name = "Porphyry",
			Family = RockFamily.Igneous,
			Summary = "Old felsic lava, purple-red with pale feldspar phenocrysts: blocky-jointed, weathering into rounded block stacks like tents on the hills.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.8f, 3.6f), Lumpiness = 0.3f, Facets = 4, FacetDepth = 0.3f, Sharpness = 0.4f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.01f, Roughness = 0.01f, Jointed = true },
			},
			Shapes = new[]
			{
				S("Tent", FormationKind.Tor, 3.0f, 4.0f, 3),
				S("Knob", FormationKind.Boulder, 2.4f, 1.2f, exponent: 3.4f),
				S("BlockField", FormationKind.Scree, 4.0f, 1.0f, 10),
				S("Split", FormationKind.Split, 2.2f, 1.5f),
				S("Scree", FormationKind.Scree, 2.2f, 0.7f, 14),
			},
			Surface = R("Porphyry", 0.02f, "#3e2224", "#643a36", "#8a5a50", 5, SurfaceMotif.Granular, 80, 0.1f, 0.4f, "#2e1a1c", "#a87468", 0.2f,
				speckles: Hexes("#e8d0c8", "#f2e6e0"), speckleCount: 6000, speckleSize: 0.004f),
		};

		public static readonly RockType MarineIce = new RockType
		{
			Name = "MarineIce",
			Family = RockFamily.Metamorphic,
			Summary = "Ocean water refrozen onto the underside of an ice shell: translucent blue-black, veined by brine channels, scalloped and fluted by the currents that melt it.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2f, 2.4f), Lumpiness = 0.3f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.3f,
				Fragments = FragmentForm.Rounded,
				Pits = Pits(0.45f, 0.7f, 0.45f, 0.05f),
				Flutes = new FluteStyle { Count = 12, Depth = 0.06f, Reach = 0.8f },
				Bedding = Beds(2f, 4f, 0.1f, 0f, 6f, 0.6f, 0.015f, 1),
			},
			Shapes = new[]
			{
				S("Scallop", FormationKind.Boulder, 3.0f, 2.0f),
				S("MeltFlute", FormationKind.Pinnacle, 1.6f, 4.0f),
				S("Ledges", FormationKind.Ledges, 3.0f, 1.4f, 3),
				S("Shelf", FormationKind.SlabStack, 4.0f, 0.6f, 2),
				// Ice accreted from below, veined with brine channels.
				S("Accretion", FormationKind.Flowstone, 3.0f, 2.0f, dress: IceSurfaces.Accreted),
			},
			Surface = R("MarineIce", 0.012f, "#1a2a3a", "#3a5a6e", "#7aa0b4", 3, SurfaceMotif.Fractured, 4, 0.03f, 0.3f, "#141e28", "#c8dce6", 0.55f, 0.08f, 0.35f,
				speckles: Hexes("#e0ecf2", "#2a2420"), speckleCount: 800, speckleSize: 0.002f),
			Ice = IceLook("MarineIce", 0.55f, 0.04f),
		};

		public static readonly RockType Alunite = new RockType
		{
			Name = "Alunite",
			Family = RockFamily.Igneous,
			Summary = "Volcanic rock rotted by acid-sulphate steam: chalk-white, buff-pink and lemon, porous and crumbly, seamed with opal and sulphur.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.45f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.15f,
				Fragments = FragmentForm.Rounded,
				Bedding = Beds(3f, 5f, 0.2f, 0f, 8f, 0.6f, 0.03f, 2),
				Pits = Pits(0.15f, 0.5f, 0.4f, 0.03f),
			},
			Shapes = new[]
			{
				S("Ledges", FormationKind.Ledges, 3.2f, 2.0f, 4),
				S("Knoll", FormationKind.Boulder, 6.0f, 3.0f, exponent: 2.4f),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("Alunite", 0.025f, "#a89a86", "#d8ccba", "#f2ece0", 5, SurfaceMotif.Granular, 72, 0.1f, 0.4f, "#d8c040", "#f8f4ec", 0.1f,
				speckles: Hexes("#e0c870", "#c8a0a0"), speckleCount: 3000, speckleSize: 0.003f),
		};

		public static readonly RockType Tessera = new RockType
		{
			Name = "Tessera",
			Family = RockFamily.Metamorphic,
			Summary = "A furnace world's deformed felsic highland crust: fins between parallel fractures crossed by graben, rhomb blocks, comb ridges, acid-honeycombed.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.9f), Roundness = V(2.6f, 3f), Lumpiness = 0.2f, Facets = 6, FacetDepth = 0.45f, Sharpness = 0.85f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Roughness = 0.006f, Jointed = true },
				Pits = Pits(0.18f, 0.35f, 0.35f, 0.05f),
				Bedding = Beds(3f, 5f, 0.1f, 0f, 8f, 0.2f, 0.015f, 3),
			},
			Shapes = new[]
			{
				S("Fin", FormationKind.Boulder, 3.2f, 1.8f, elongation: 2.6f),
				S("RhombBlock", FormationKind.Faceted, 1.8f, 0.9f),
				S("RidgeComb", FormationKind.Penitentes, 4.0f, 2.4f, 5),
				S("GrabenStep", FormationKind.Ledges, 3.4f, 2.0f, 4),
				S("Honeycomb", FormationKind.Boulder, 1.4f, 1.0f),
				S("Scree", FormationKind.Scree, 2.4f, 0.9f, 14),
			},
			Surface = R("Tessera", 0.02f, "#4a423a", "#76695a", "#a89884", 4, SurfaceMotif.Fractured, 7, 0.06f, 0.5f, "#3a332c", "#c8b8a0", 0.2f,
				speckles: Hexes("#d8c88a", "#2a2420"), speckleCount: 2000, speckleSize: 0.003f),
		};

		public static readonly RockType Nenfro = new RockType
		{
			Name = "Nenfro",
			Family = RockFamily.Igneous,
			Summary = "Grey tuff with black lapilli and cream pumice: it carves clean into squared blocks and tomb facades, and weathers to rounded bosses streaked black.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.6f, 3.4f), Lumpiness = 0.35f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.2f,
				Fragments = FragmentForm.Rounded,
				Pits = Pits(0.3f, 0.4f, 0.4f, 0.08f, sides: 0.6f),
				Bedding = Beds(2f, 4f, 0.1f, 0f, 4f, 0.4f, 0.015f, 3),
			},
			Shapes = new[]
			{
				S("Block", FormationKind.Boulder, 1.8f, 1.2f, exponent: 3.8f),
				S("Ledges", FormationKind.Ledges, 3.0f, 2.0f, 4),
				S("Boss", FormationKind.Boulder, 1.6f, 1.2f, exponent: 2.4f),
				S("Facade", FormationKind.Bedded, 4.0f, 3.0f, 2),
			},
			Surface = R("Nenfro", 0.02f, "#2e302e", "#545650", "#808078", 5, SurfaceMotif.Granular, 64, 0.1f, 0.4f, "#1a1c1a", "#e0dccc", 0.08f,
				speckles: Hexes("#141414", "#e8e2d0"), speckleCount: 4000, speckleSize: 0.003f),
		};

		public static readonly RockType Hazestone = new RockType
		{
			Name = "Hazestone",
			Family = RockFamily.Metamorphic,
			Summary = "Ancient water ice under a billion years of haze varnish: wind-grooved hummocks blunt upwind, fused knuckles, tors undercut at the foot by blown sand.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.9f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.12f, Facets = 3, FacetDepth = 0.2f, Sharpness = 0.45f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(4f, 7f, 0.15f, 0f, 3f, 0.5f, 0.01f, 2),
			},
			Shapes = new[]
			{
				S("Hummock", FormationKind.Lobe, 30f, 8.0f),
				S("Knuckle", FormationKind.Tor, 4.0f, 4.0f, 3),
				S("SandTailBlock", FormationKind.Boulder, 2.0f, 1.2f, elongation: 1.6f),
				S("GroovedTor", FormationKind.Ledges, 10f, 8.0f, 6),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("Hazestone", 0.016f, "#7a7266", "#b4aa98", "#e2dac8", 4, SurfaceMotif.Ridges, 12, 0.05f, 0.45f, "#3a2414", "#d8d0c0", 0.25f),
		};

		public static readonly RockType BrineIce = new RockType
		{
			Name = "BrineIce",
			Family = RockFamily.Metamorphic,
			Summary = "An ocean world's shell, frozen from brine and layered with hydrated salts browned by the sky: double ridges, tilted chaos rafts, the cusps of cycloid cracks.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.4f, 0.7f), Roundness = V(2f, 2.4f), Lumpiness = 0.25f, Facets = 4, FacetDepth = 0.3f, Sharpness = 0.55f,
				Fragments = FragmentForm.Platy,
				Cleavage = new CleavageStyle { Thickness = V(0.08f, 0.2f), Tilt = V(5f, 25f), EdgeFacets = 6, EdgeBevel = 0.35f },
			},
			Shapes = new[]
			{
				S("DoubleRidge", FormationKind.RubbleRidge, 14f, 2.6f, 80),
				S("Raft", FormationKind.SlabStack, 12f, 3.0f, 2, dip: 15f),
				S("Cusp", FormationKind.Pinnacle, 3.0f, 5.0f),
				S("Block", FormationKind.Boulder, 1.6f, 1.0f),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("BrineIce", 0.015f, "#6e4a36", "#a08470", "#dcd4c8", 4, SurfaceMotif.Strata, 5, 0.1f, 0.35f, "#5a3a28", "#f0e8d8", 0.35f,
				speckles: Hexes("#f0e8d8"), speckleCount: 600, speckleSize: 0.002f),
			Ice = IceLook("BrineIce", 0.4f, 0.02f),
		};

		public static readonly RockType Porcelanite = new RockType
		{
			Name = "Porcelanite",
			Family = RockFamily.Sedimentary,
			Summary = "Diatom silica hardened to porcelain: pale cream and lavender-grey thin beds with black chert stringers, smooth conchoidal breaks, rusty concretion bands.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(2.6f, 3.2f), Lumpiness = 0.15f, Facets = 10, FacetDepth = 0.55f, Sharpness = 0.8f,
				Fragments = FragmentForm.Platy,
				Bedding = Beds(8f, 12f, 0.12f, 0f, 10f, 0.2f, 0.01f, 3),
				Fracture = new FractureStyle { Dish = 0.05f, Ripples = 1, RippleRelief = 0.003f, Roughness = 0.003f, Jointed = false },
			},
			Shapes = new[]
			{
				S("WallLedge", FormationKind.Ledges, 2.4f, 0.8f, 5, dip: 8f),
				S("Concretion", FormationKind.Boulder, 1.5f, 1.0f, exponent: 2.2f),
				S("Block", FormationKind.Faceted, 2.0f, 1.5f),
				S("Scree", FormationKind.Scree, 1.5f, 0.3f, 14),
			},
			Surface = R("Porcelanite", 0.01f, "#8a8290", "#c4bcb8", "#ece6dc", 4, SurfaceMotif.Strata, 24, 0.1f, 0.35f, "#2a2628", "#f4f0e8", 0.4f,
				speckles: Hexes("#a8683a"), speckleCount: 400, speckleSize: 0.004f),
		};

		public static readonly RockType Serpentinite = new RockType
		{
			Name = "Serpentinite",
			Family = RockFamily.Metamorphic,
			Summary = "Hydrated mantle rock: waxy, scaly and slickensided, mottled like a snake's skin and veined pale; knockers, shingled scale crags, rust-rinded tops.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.2f, 3f), Lumpiness = 0.35f, Facets = 4, FacetDepth = 0.25f, Sharpness = 0.4f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.006f, Jointed = true },
				Cleavage = new CleavageStyle { Thickness = V(0.05f, 0.12f), Tilt = V(20f, 50f), EdgeFacets = 6, EdgeBevel = 0.5f },
			},
			Shapes = new[]
			{
				S("Knocker", FormationKind.Boulder, 2.0f, 1.6f),
				S("ScaleCrag", FormationKind.SlabStack, 2.4f, 2.0f, 6),
				S("Crag", FormationKind.Faceted, 3.0f, 3.5f),
				S("Scree", FormationKind.Scree, 2.2f, 0.7f, 14),
			},
			Surface = R("Serpentinite", 0.02f, "#1e2a22", "#3a4a3a", "#6a7a62", 5, SurfaceMotif.Fractured, 12, 0.08f, 0.45f, "#141c16", "#8a9a80", 0.45f,
				speckles: Hexes("#c8d0b8", "#101010", "#8a6a3a", "#b8c0a8"), speckleCount: 9000, speckleSize: 0.0016f),
		};

		public static readonly RockType Peridotite = new RockType
		{
			Name = "Peridotite",
			Family = RockFamily.Igneous,
			Summary = "The mantle itself: dense olive-black with a scaly fracture, a rust-brown iddingsite rind and serpentine veins; ledges, bluffs and buttressed walls.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.65f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.3f, Facets = 5, FacetDepth = 0.35f, Sharpness = 0.45f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.012f, Jointed = true },
				Bedding = Beds(3f, 5f, 0.1f, 0f, 10f, 0.3f, 0.015f, 3),
			},
			Shapes = new[]
			{
				S("Ledges", FormationKind.Ledges, 3.0f, 1.6f, 4),
				S("Bluff", FormationKind.Bedded, 4.0f, 3.0f, 3),
				S("Buttress", FormationKind.Faceted, 6.0f, 4.0f),
				S("Scree", FormationKind.Scree, 2.2f, 0.7f, 14),
			},
			Surface = R("Peridotite", 0.02f, "#1a2018", "#2e3a28", "#4e5a3e", 4, SurfaceMotif.Fractured, 8, 0.06f, 0.45f, "#121810", "#8a5a30", 0.25f,
				speckles: Hexes("#6a8a5a", "#8a5a30"), speckleCount: 3000, speckleSize: 0.003f),
		};

		public static readonly RockType Radiolarite = new RockType
		{
			Name = "Radiolarite",
			Family = RockFamily.Sedimentary,
			Summary = "Red ribbon chert: hard 3–10 cm beds of radiolarian silica parted by green-grey shale, breaking conchoidally into ribbed scarps.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.4f, 0.7f), Roundness = V(3f, 3.6f), Lumpiness = 0.1f, Facets = 6, FacetDepth = 0.45f, Sharpness = 0.8f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(8f, 14f, 0.22f, 10f, 30f, 0.15f, 0.01f, 3),
				Fracture = new FractureStyle { Dish = 0.04f, Roughness = 0.004f, Jointed = false },
			},
			Shapes = new[]
			{
				S("Ribs", FormationKind.Ledges, 5.0f, 1.0f, 6, dip: 20f),
				S("Scarp", FormationKind.SlabStack, 3.0f, 0.3f, 6, dip: 12f),
				S("Scree", FormationKind.Scree, 2.2f, 0.7f, 12),
			},
			Surface = R("Radiolarite", 0.015f, "#4a1a16", "#7a2a22", "#a24a3a", 4, SurfaceMotif.Strata, 30, 0.1f, 0.55f, "#4a5a48", "#b85a48", 0.35f),
		};

		public static readonly RockType Carstone = new RockType
		{
			Name = "Carstone",
			Family = RockFamily.Sedimentary,
			Summary = "Ferruginous greensand, gingerbread brown and coarse, ribbed with black-purple iron pans and honeycombed into boxwork; it crumbles to rust sand and leaves iron doggers.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.4f, 3f), Lumpiness = 0.4f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.25f,
				Fragments = FragmentForm.Rounded,
				Bedding = Beds(3f, 6f, 0.25f, 0f, 6f, 0.5f, 0.03f, 2),
				Pits = Pits(0.2f, 0.7f, 0.45f, 0.08f, sides: 0.5f),
			},
			Shapes = new[]
			{
				S("ScarpLedge", FormationKind.Ledges, 3.0f, 1.8f, 4),
				S("BoxworkBlock", FormationKind.Boulder, 2.0f, 1.4f),
				S("PanSlab", FormationKind.SlabStack, 2.0f, 0.3f, 3),
				S("Scree", FormationKind.Scree, 2.2f, 0.7f, 10),
				S("Dogger", FormationKind.Boulder, 1.8f, 1.4f, elongation: 0.8f, exponent: 2f),
				S("BoxworkTor", FormationKind.Tor, 3.0f, 4.0f, 3),
			},
			Surface = R("Carstone", 0.03f, "#4a2a14", "#7a4a24", "#a87040", 4, SurfaceMotif.Fractured, 8, 0.06f, 0.5f, "#2a1418", "#c08850", 0.06f,
				speckles: Hexes("#2a1418", "#c89060"), speckleCount: 3000, speckleSize: 0.003f),
		};

		public static readonly RockType SulcusIce = new RockType
		{
			Name = "SulcusIce",
			Family = RockFamily.Metamorphic,
			Summary = "Coarse crystalline water ice of a geyser moon's stripes: paired flank ridges along each fissure, frost-feathered vent cones, house-sized plume-fall blocks, tilted crystal shards.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2f, 2.4f), Lumpiness = 0.2f, Facets = 10, FacetDepth = 0.55f, Sharpness = 0.6f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.02f, Roughness = 0.006f, Jointed = false },
			},
			Shapes = new[]
			{
				S("StripeFlank", FormationKind.RubbleRidge, 24f, 6.0f, 80, dress: IceSurfaces.StripeBlue),
				S("VentCone", FormationKind.Cone, 3.0f, 2.5f),
				S("PlumeBlock", FormationKind.Faceted, 4.0f, 3.0f),
				S("Shard", FormationKind.Standing, 1.2f, 3.0f),
				S("Scree", FormationKind.Scree, 2.0f, 0.5f, 12),
			},
			Surface = R("SulcusIce", 0.012f, "#2f5e6c", "#6e9ea8", "#cfe2e6", 4, SurfaceMotif.Fractured, 5, 0.03f, 0.3f, "#2a5662", "#e8fbff", 0.5f, 0.08f, 0.4f,
				speckles: Hexes("#e8fbff"), speckleCount: 400, speckleSize: 0.002f),
			Ice = IceLook("SulcusIce", 0.5f, 0.04f),
		};

		// ── Formation-only rocks ──────────────────────────────────────

		public static readonly RockType Natrite = new RockType
		{
			Name = "Natrite",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Sodium carbonate salt left by brine welling up through an icy crust: chalk-white mounds with fissured summits, cream sal-ammoniac skins, slim salt spires.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.4f, Facets = 4, FacetDepth = 0.3f, Sharpness = 0.5f,
				Fragments = FragmentForm.Angular,
				Pits = Pits(0.14f, 0.5f, 0.4f, 0.02f),
			},
			Shapes = new[]
			{
				S("FaculaMound", FormationKind.Boulder, 5.0f, 2.0f, exponent: 2.4f),
				S("SaltSpire", FormationKind.Pinnacle, 1.0f, 3.5f),
			},
			Surface = R("Natrite", 0.015f, "#b8b4a8", "#e0ddd2", "#f6f4ee", 5, SurfaceMotif.Granular, 96, 0.1f, 0.4f, "#c8c2b0", "#fbfaf6", 0.25f,
				speckles: Hexes("#e8dcb0", "#ffffff"), speckleCount: 4000, speckleSize: 0.002f),
		};

		public static readonly RockType Phosphorite = new RockType
		{
			Name = "Phosphorite",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Phosphate grown on a starved sea floor: knobbly brown-black knolls and nodules with a green glauconite rind.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.4f), Lumpiness = 0.6f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.2f,
				Fragments = FragmentForm.Rounded,
				Clasts = new ClastStyle { Size = 0.12f, Fill = 0.5f, Protrusion = 0.3f },
			},
			Shapes = new[]
			{
				S("Knob", FormationKind.Knob, 3.0f, 1.6f),
				S("Lump", FormationKind.Boulder, 1.2f, 0.8f),
			},
			Surface = R("Phosphorite", 0.025f, "#1e1a14", "#3a3226", "#5a5040", 5, SurfaceMotif.Granular, 64, 0.1f, 0.45f, "#2a3a22", "#6a7a4a", 0.12f,
				speckles: Hexes("#4a6a3a", "#8a7a5a"), speckleCount: 3000, speckleSize: 0.003f),
		};

		public static readonly RockType Adamant = new RockType
		{
			Name = "Adamant",
			Family = RockFamily.Igneous,
			FormationsOnly = true,
			Summary = "Lodestone of the old tales: black magnetite with a blue temper sheen, breaking in octahedral facets, one crag of it to a scene.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2f), Lumpiness = 0f, Facets = 10, FacetDepth = 0.6f, Sharpness = 1f,
				Fragments = FragmentForm.Angular,
				Fracture = new FractureStyle { Dish = 0.01f, Roughness = 0.003f, Jointed = true },
			},
			Shapes = new[]
			{
				S("Crag", FormationKind.Faceted, 6.0f, 5.0f),
				S("Block", FormationKind.Faceted, 1.6f, 1.2f),
			},
			Surface = Metal(R("Adamant", 0.006f, "#18191c", "#24262a", "#3a3c44", 3, SurfaceMotif.Fractured, 6, 0.05f, 0.4f, "#141518", "#5a5c90", 0.55f, 0.1f, 0.5f), 0.7f),
		};

		public static readonly RockType NitrogenIce = new RockType
		{
			Name = "NitrogenIce",
			Family = RockFamily.Metamorphic,
			FormationsOnly = true,
			Summary = "Soft creeping nitrogen ice: translucent cream, polished, never sharp; it convects into cells, flows in glacier snouts and rims its sublimation pits.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.75f, 1f), Roundness = V(2f, 2.4f), Lumpiness = 0.2f, Facets = 0, Sharpness = 0.1f,
				Fragments = FragmentForm.Rounded,
			},
			Shapes = new[]
			{
				S("PitRim", FormationKind.CraterRim, 10f, 0.8f),
				S("CellDome", FormationKind.Lobe, 30f, 4.0f),
				S("FlowLobe", FormationKind.Lobe, 14f, 2.0f),
				S("Hummock", FormationKind.Boulder, 3.0f, 1.2f, exponent: 2.2f),
			},
			Surface = R("NitrogenIce", 0.01f, "#b8aa98", "#ddd2c2", "#f4eee4", 3, SurfaceMotif.Fractured, 4, 0.03f, 0.2f, "#c4b8a6", "#fbf8f2", 0.6f, 0.08f, 0.35f),
			Ice = IceLook("NitrogenIce", 0.6f, 0.05f),
		};

		public static readonly RockType MethaneIce = new RockType
		{
			Name = "MethaneIce",
			Family = RockFamily.Metamorphic,
			FormationsOnly = true,
			Summary = "Methane ice sublimated into knife-edged blades tens of metres high, pink-white, standing in rows.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.3f, 0.5f), Roundness = V(2f, 2.4f), Lumpiness = 0.15f, Facets = 4, FacetDepth = 0.4f, Sharpness = 0.8f,
				Fragments = FragmentForm.Platy,
			},
			Shapes = new[]
			{
				S("Blade", FormationKind.Standing, 2.0f, 12f),
				S("BladeRow", FormationKind.Penitentes, 4.5f, 3.2f, 9),
				S("Bladelet", FormationKind.Standing, 0.6f, 3.0f),
			},
			Surface = R("MethaneIce", 0.01f, "#b4948e", "#dcc4bc", "#f6ece6", 3, SurfaceMotif.Fractured, 4, 0.03f, 0.25f, "#c0a49c", "#fff6f2", 0.5f, 0.08f, 0.35f),
			Ice = IceLook("MethaneIce", 0.5f, 0.04f),
		};

		public static readonly RockType Moundstone = new RockType
		{
			Name = "Moundstone",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Cold-water coral framestone: grey-buff mud packed with dead coral tubes showing white rings, crusted manganese-black, exposed on a coral mound's flanks.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.45f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.2f,
				Fragments = FragmentForm.Rounded,
				Pits = Pits(0.18f, 0.5f, 0.4f, 0.04f),
			},
			Shapes = new[]
			{
				S("MoundCore", FormationKind.Knob, 4.0f, 3.0f),
				S("Slabs", FormationKind.SlabStack, 3.0f, 0.4f, 3),
			},
			Surface = R("Moundstone", 0.025f, "#4a4438", "#7a7262", "#a49a86", 5, SurfaceMotif.Granular, 72, 0.1f, 0.45f, "#1e1c18", "#ece8dc", 0.1f,
				speckles: Hexes("#f0ece0", "#1e1c18"), speckleCount: 4000, speckleSize: 0.004f),
		};

		public static readonly RockType Carbonatite = new RockType
		{
			Name = "Carbonatite",
			Family = RockFamily.Igneous,
			FormationsOnly = true,
			Summary = "Carbonate lava, runny as water: pale buff-grey levee blocks and slabs with a fizzy pitted skin and a white bloom.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(2.4f, 3f), Lumpiness = 0.25f, Facets = 9, FacetDepth = 0.5f, Sharpness = 0.4f,
				Fragments = FragmentForm.Angular,
				Pits = Pits(0.1f, 0.6f, 0.4f, 0.015f),
				Fracture = new FractureStyle { Dish = 0.025f, Roughness = 0.01f, Jointed = false },
			},
			Shapes = new[]
			{
				S("LeveeBlock", FormationKind.Boulder, 1.2f, 0.6f, exponent: 3.6f),
				S("LeveeSlab", FormationKind.SlabStack, 1.6f, 0.25f, 3),
			},
			Surface = R("Carbonatite", 0.015f, "#7a7064", "#a89e90", "#d4ccc0", 4, SurfaceMotif.Granular, 80, 0.1f, 0.4f, "#6a6256", "#f2eee6", 0.15f,
				speckles: Hexes("#f6f4ee", "#5a5248"), speckleCount: 3000, speckleSize: 0.002f),
		};

		public static readonly RockType Guyotstone = new RockType
		{
			Name = "Guyotstone",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "A drowned reef cap: phosphatised buff limestone with a black manganese rind, bored and pitted, fossil coral heads in its ledges.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.6f, 3.2f), Lumpiness = 0.3f, Facets = 3, FacetDepth = 0.3f, Sharpness = 0.35f,
				Fragments = FragmentForm.Angular,
				Bedding = Beds(2f, 4f, 0.15f, 0f, 6f, 0.4f, 0.02f, 2),
				Pits = Pits(0.16f, 0.5f, 0.35f, 0.04f),
			},
			Shapes = new[]
			{
				S("CapLedge", FormationKind.Ledges, 2.0f, 0.6f, 3),
				S("Slabs", FormationKind.SlabStack, 2.0f, 0.5f, 2),
				S("NotchBlock", FormationKind.Bedded, 1.8f, 1.2f, 3),
			},
			Surface = R("Guyotstone", 0.025f, "#3a3428", "#7a6e56", "#b4a480", 4, SurfaceMotif.Granular, 64, 0.1f, 0.45f, "#16140f", "#d0c4a4", 0.1f,
				speckles: Hexes("#16140f", "#e6dcc0"), speckleCount: 3000, speckleSize: 0.004f),
		};

		public static readonly RockType Anhydrite = new RockType
		{
			Name = "Anhydrite",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Calcium sulphate dropped by a cooler vent: white-lilac chimneys that smoke white, and small fizzing cones.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.5f), Lumpiness = 0.5f, Facets = 3, FacetDepth = 0.3f, Sharpness = 0.3f,
				Fragments = FragmentForm.Angular,
				Pits = Pits(0.2f, 0.4f, 0.4f, 0.03f),
			},
			Shapes = new[]
			{
				S("WhiteSmoker", FormationKind.Cone, 1.5f, 5.0f, 2),
				S("ChampagneCone", FormationKind.Cone, 0.6f, 1.5f),
			},
			Surface = R("Anhydrite", 0.02f, "#9a90a8", "#c8bed4", "#e8e0f0", 5, SurfaceMotif.Granular, 72, 0.1f, 0.4f, "#a89cba", "#f6f2fa", 0.25f,
				speckles: Hexes("#ffffff", "#b8a8c8"), speckleCount: 2000, speckleSize: 0.003f),
		};

		public static readonly RockType VentCarbonate = new RockType
		{
			Name = "VentCarbonate",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Brucite and aragonite of a warm alkaline vent field: porous white towers tens of metres tall, flanged where vent water ponds under ledges.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.5f), Lumpiness = 0.55f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.2f,
				Fragments = FragmentForm.Rounded,
				Pits = Pits(0.2f, 0.6f, 0.4f, 0.04f),
			},
			Shapes = new[]
			{
				S("Tower", FormationKind.Cone, 6.0f, 40f, 3),
			},
			Surface = R("VentCarbonate", 0.02f, "#b8b4a8", "#e0ddd2", "#f6f4ee", 5, SurfaceMotif.Granular, 64, 0.1f, 0.4f, "#c8c4b6", "#fbfaf6", 0.1f,
				speckles: Hexes("#ffffff", "#a8a49a"), speckleCount: 3000, speckleSize: 0.003f),
		};

		public static readonly RockType Leadfrost = new RockType
		{
			Name = "Leadfrost",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Metal frost condensed from a furnace world's air on its cooler summits: lead-grey bladed combs with silver tips.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.25f, Facets = 4, FacetDepth = 0.3f, Sharpness = 0.8f,
				Fragments = FragmentForm.Platy,
				Cleavage = new CleavageStyle { Thickness = V(0.02f, 0.05f), Tilt = V(5f, 20f), EdgeFacets = 6, EdgeBevel = 0.2f },
			},
			Shapes = new[]
			{
				S("FrostComb", FormationKind.CrystalCluster, 1.2f, 1.0f, 9, habit: CrystalHabit.Bladed),
				S("FrostCrown", FormationKind.CrystalCluster, 2.0f, 1.2f, 14, habit: CrystalHabit.Bladed),
			},
			Surface = Metal(R("Leadfrost", 0.004f, "#4a4c52", "#7a7c84", "#d0d0d4", 3, SurfaceMotif.Strata, 60, 0.1f, 0.25f, "#5a5c62", "#e0e0e4", 0.55f, 0.1f, 0.5f), 0.85f),
		};

		public static readonly RockType Duneslab = new RockType
		{
			Name = "Duneslab",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Aeolianite of organic sand: dune beds cemented into tilted slab pavements and ledges that show their sweeping cross-laminae.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.5f, 0.8f), Roundness = V(2.8f, 3.6f), Lumpiness = 0.2f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.4f,
				Fragments = FragmentForm.Platy,
				Bedding = Beds(5f, 8f, 0.18f, 10f, 28f, 0.5f, 0.015f, 2),
			},
			Shapes = new[]
			{
				S("SlabPavement", FormationKind.SlabStack, 6.0f, 0.4f, 3, dip: 8f),
				S("CrossbedLedge", FormationKind.Ledges, 8.0f, 1.5f, 6, dip: 20f),
				S("Stack", FormationKind.SlabStack, 2.0f, 1.0f, 4),
			},
			Surface = R("Duneslab", 0.02f, "#2e2418", "#4a3a26", "#6e5a3e", 4, SurfaceMotif.Strata, 16, 0.1f, 0.4f, "#241c12", "#86704e", 0.12f),
		};

		public static readonly RockType Oysterbank = new RockType
		{
			Name = "Oysterbank",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Giant oyster shells cemented into shelves: curtains cantilevered from a canyon wall, low banks on its terraces.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.9f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.5f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.3f,
				Fragments = FragmentForm.Platy,
				Cleavage = new CleavageStyle { Thickness = V(0.06f, 0.14f), Tilt = V(2f, 12f), EdgeFacets = 7, EdgeBevel = 0.5f },
				Clasts = new ClastStyle { Size = 0.2f, Fill = 0.5f, Protrusion = 0.25f },
			},
			Shapes = new[]
			{
				S("Curtain", FormationKind.SlabStack, 2.0f, 0.6f, 3),
				S("Bank", FormationKind.Boulder, 2.0f, 1.0f, exponent: 2.4f),
			},
			Surface = R("Oysterbank", 0.03f, "#6a665e", "#9c968a", "#cec8bc", 5, SurfaceMotif.Ripples, 14, 0.1f, 0.45f, "#5a564e", "#ece8e0", 0.15f,
				speckles: Hexes("#f4f2ec", "#3a3834"), speckleCount: 3000, speckleSize: 0.004f),
		};

		public static readonly RockType Moraine = new RockType
		{
			Name = "Moraine",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Unconsolidated till packed with rounded boulders: rain carves it into earth pyramids, each column capped by the boulder that shelters it.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2.2f, 2.8f), Lumpiness = 0.5f, Facets = 1, FacetDepth = 0.2f, Sharpness = 0.05f,
				Fragments = FragmentForm.Rounded,
				Clasts = new ClastStyle { Size = 0.25f, Fill = 0.45f, Protrusion = 0.4f },
				Flutes = new FluteStyle { Count = 12, Depth = 0.04f, Reach = 0.8f },
			},
			Shapes = new[]
			{
				S("EarthPyramid", FormationKind.Chimney, 1.2f, 8.0f),
				// A lone erratic of granite carried on the ice.
				S("Erratic", FormationKind.Perched, 4.0f, 3.5f, dress: "Granite"),
			},
			Surface = R("Moraine", 0.03f, "#5a5a54", "#8a8a82", "#b4b2a8", 4, SurfaceMotif.Cobbles, 9, 0.35f, 0.45f, "#6a6a62", "#c4c2b8", 0.08f),
		};

		public static readonly RockType Chemoherm = new RockType
		{
			Name = "Chemoherm",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Carbonate precipitated where methane seeps from the sea floor: knobbly cavernous cream-grey mounds.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.4f), Lumpiness = 0.65f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.15f,
				Fragments = FragmentForm.Rounded,
				Pits = Pits(0.3f, 0.7f, 0.45f, 0.12f, sides: 0.5f),
			},
			Shapes = new[]
			{
				S("Mound", FormationKind.Boulder, 4.0f, 3.0f, exponent: 2.2f),
				S("Block", FormationKind.Boulder, 1.6f, 1.0f, exponent: 3.4f),
			},
			Surface = R("Chemoherm", 0.03f, "#6a665a", "#9c9686", "#c8c2b0", 5, SurfaceMotif.Granular, 64, 0.12f, 0.5f, "#4a463c", "#dcd6c4", 0.1f,
				speckles: Hexes("#3a362e", "#e8e2d0"), speckleCount: 3000, speckleSize: 0.004f),
		};

		public static readonly RockType Mangancrust = new RockType
		{
			Name = "Mangancrust",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Ferromanganese crust grown a few millimetres a million years: black-brown botryoidal metallic skins on knoll tops.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.4f), Lumpiness = 0.6f, Facets = 1, FacetDepth = 0.2f, Sharpness = 0.1f,
				Fragments = FragmentForm.Rounded,
				Clasts = new ClastStyle { Size = 0.08f, Fill = 0.7f, Protrusion = 0.35f },
			},
			Shapes = new[]
			{
				S("Knoll", FormationKind.Boulder, 2.4f, 1.0f, exponent: 2.2f),
			},
			Surface = Metal(R("Mangancrust", 0.02f, "#141210", "#2a2420", "#463a30", 4, SurfaceMotif.Granular, 48, 0.12f, 0.5f, "#0e0c0a", "#5a4a3a", 0.35f), 0.4f),
		};

		public static readonly RockType Permafrost = new RockType
		{
			Name = "Permafrost",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Frozen ground under turf and peat, heaved by its ice: pingos, palsas, polygon rims, solifluction lobes, thaw slumps and earth hummocks.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.7f, 0.95f), Roundness = V(2f, 2.4f), Lumpiness = 0.35f, Facets = 1, FacetDepth = 0.2f, Sharpness = 0.1f,
				Fragments = FragmentForm.Rounded,
				Bedding = Beds(2f, 4f, 0.12f, 0f, 4f, 0.5f, 0.03f, 1),
			},
			Shapes = new[]
			{
				S("Pingo", FormationKind.Knob, 18f, 12f),
				S("Palsa", FormationKind.Lobe, 10f, 2.5f),
				S("Polygons", FormationKind.PolygonRidges, 12f, 0.4f, 5, colliderless: true),
				S("Lobe", FormationKind.Lobe, 8.0f, 1.0f),
				S("ThawSlump", FormationKind.Ledges, 6.0f, 3.0f, 4),
				S("Thufur", FormationKind.Boulder, 1.2f, 0.5f, exponent: 2.2f, colliderless: true),
			},
			Surface = R("Permafrost", 0.03f, "#2e2a20", "#4a4434", "#6e6a52", 5, SurfaceMotif.Clumps, 24, 0.1f, 0.5f, "#3a3a22", "#8a8a5a", 0.08f),
		};

		public static readonly RockType Sarsen = new RockType
		{
			Name = "Sarsen",
			Family = RockFamily.Sedimentary,
			FormationsOnly = true,
			Summary = "Silcrete: hard sugary grey sandstone left lying in the turf like a flock of sheep, rounded, pitted and blotched with lichen.",
			Style = new RockGeometryStyle
			{
				Aspect = V(0.6f, 0.85f), Roundness = V(2.6f, 3.2f), Lumpiness = 0.3f, Facets = 2, FacetDepth = 0.2f, Sharpness = 0.15f,
				Fragments = FragmentForm.Rounded,
				Pits = Pits(0.3f, 0.4f, 0.35f, 0.04f, tops: 0.6f),
			},
			Shapes = new[]
			{
				S("Wether", FormationKind.Boulder, 2.6f, 1.2f, elongation: 1.3f, exponent: 2.6f),
				S("Stone", FormationKind.Standing, 2.2f, 3.6f),
			},
			Surface = R("Sarsen", 0.02f, "#6e6c64", "#9a968a", "#c8c4b6", 5, SurfaceMotif.Granular, 120, 0.1f, 0.35f, "#5e5c54", "#d8d4b8", 0.2f,
				speckles: Hexes("#d8d4b8", "#c8b860"), speckleCount: 3000, speckleSize: 0.006f),
		};

		/// <summary>
		/// The ground family a caller asking for a crust rock's stone would want (<see cref="RockMaterialSpec.GroundFamily"/>);
		/// the stone itself wears its rock type's own material, so this is only a hint.
		/// </summary>
		public static string StoneGround(string rock)
		{
			switch (rock)
			{
				case "Dolomite":
				case "Coralstone":
				case "Travertine":
				case "CoralRag":
				case "Caliche":
				case "Marl":
					return Ground.Limestone;
				case "Komatiite":
				case "Dolerite":
				case "Platebasalt":
				case "Variolite":
				case "Hyaloclastite":
				case "Peridotite":
					return Ground.Basalt;
				case "Suevite":
				case "Norite":
				case "Troctolite":
				case "Duststone":
					return Ground.Regolith;
				case "Cryolava":
				case "RaftIce":
				case "SableIce":
				case "MarineIce":
				case "BrineIce":
				case "SulcusIce":
				case "Cryokarst":
					return Ground.Ice;
				case "Hazestone":
					return Ground.Tholin;
				case "Saltrock":
					return Ground.SaltCrust;
				case "Alunite":
					return Ground.Sulphur;
				case "Nenfro":
					return Ground.Ash;
				case "Beachrock":
				case "Molasse":
				case "Redbed":
				case "Carstone":
				case "Flagrock":
					return Ground.Sandstone;
				case "Till":
				case "Loess":
				case "Laterite":
				case "Mudstone":
					return Ground.Clay;
				default:
					return Ground.Rock;
			}
		}

		/// <summary>
		/// How much moss a biome crust or formation rock's material may grow (as <see cref="MossAmounts.ForRockType"/> for the
		/// first rocks), or null for a name that is none of these. Ice, salt, metal and alien crusts hold none.
		/// </summary>
		public static float? MossAmount(string rock)
		{
			switch (rock)
			{
				case "Travertine":
				case "Molasse":
				case "Nenfro":
				case "Coralstone":
				case "CoralRag":
				case "Loess":
				case "Till":
				case "Moraine":
				case "Sarsen":
				case "Permafrost":
					return 0.8f;
				case "Dolomite":
				case "Flysch":
				case "Beachrock":
				case "Marl":
				case "Greywacke":
				case "Mudstone":
				case "Flagrock":
				case "Laterite":
				case "Redbed":
				case "Carstone":
				case "Caliche":
				case "Phonolite":
				case "Trachyte":
				case "Rhyolite":
				case "Dacite":
				case "Dolerite":
				case "Greenstone":
				case "Granodiorite":
				case "Charnockite":
				case "Porphyry":
				case "Serpentinite":
				case "Eclogite":
				case "Hornfels":
				case "Hyaloclastite":
				case "Peridotite":
				case "Porcelanite":
				case "Radiolarite":
					return 0.6f;
				case "Komatiite":
				case "Suevite":
				case "Norite":
				case "Troctolite":
				case "Variolite":
				case "Duststone":
				case "Platebasalt":
				case "Tessera":
				case "Alunite":
				case "Saltrock":
				case "Cryolava":
				case "Cryokarst":
				case "RaftIce":
				case "SableIce":
				case "MarineIce":
				case "BrineIce":
				case "SulcusIce":
				case "Hazestone":
				case "Natrite":
				case "Phosphorite":
				case "Adamant":
				case "NitrogenIce":
				case "MethaneIce":
				case "Moundstone":
				case "Carbonatite":
				case "Guyotstone":
				case "Anhydrite":
				case "VentCarbonate":
				case "Leadfrost":
				case "Duneslab":
				case "Oysterbank":
				case "Chemoherm":
				case "Mangancrust":
					return 0f;
				default:
					return null;
			}
		}

		// ── The lists RockTypes appends ──────────────────────────────

		/// <summary>The new rocks of the crust, each a biome's own bedrock: they can be cliffs, river boulders and stones.</summary>
		public static readonly RockType[] Crust =
		{
			Rhyolite, Trachyte, Dacite, Komatiite,
			Dolomite, Flysch, Beachrock, Dolerite, Coralstone, Suevite, Cryolava, Marl, Duststone, Till, Travertine, Greenstone, Loess,
			Charnockite, Troctolite, Laterite, Molasse, CoralRag, Cryokarst, Phonolite, RaftIce, Mudstone, Redbed, SableIce, Norite,
			Variolite, Greywacke, Flagrock, Hornfels, Platebasalt, Saltrock, Granodiorite, Caliche, Hyaloclastite, Eclogite, Porphyry,
			MarineIce, Alunite, Tessera, Nenfro, Hazestone, BrineIce, Porcelanite, Serpentinite, Peridotite, Radiolarite, Carstone,
			SulcusIce,
		};

		/// <summary>The new formation-only rocks: minerals, ices and deposits made only as scattered formations.</summary>
		public static readonly RockType[] FormationRocks =
		{
			Natrite, Phosphorite, Adamant, NitrogenIce, MethaneIce, Moundstone, Carbonatite, Guyotstone, Anhydrite, VentCarbonate,
			Leadfrost, Duneslab, Oysterbank, Moraine, Chemoherm, Mangancrust, Permafrost, Sarsen,
		};
	}
}
#endif
