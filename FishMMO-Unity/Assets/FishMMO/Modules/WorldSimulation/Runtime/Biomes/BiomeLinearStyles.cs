using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// One named linear style (<see cref="BiomeLinearStyles"/>, 2026-10-26, phase I2f-b): the drafts' own names for the hedges, walls,
	/// fences, marker lines and channels they ask for (a Clawdd, a Kershef wall, a SergeLine, a qanat Gallery), each a
	/// <see cref="BiomeLinearStyle"/> on the I2f model with its form, height, spacing, breaks, side and material set, and the plants it
	/// is grown of by species name.
	/// </summary>
	public readonly struct BiomeNamedLinearStyle
	{
		/// <summary>The name a biome's file gives it (<c>new Entry.Line("Clawdd")</c>).</summary>
		public readonly string Name;
		/// <summary>What it is, for designers.</summary>
		public readonly string Description;
		/// <summary>A hedge's bushes or a row's trees by species name (the spec's own names), when the style is grown of its own; else empty.</summary>
		public readonly string[] Species;
		/// <summary>A hedge's standard trees by species name; else empty.</summary>
		public readonly string[] Standards;
		private readonly Recipe recipe;

		internal BiomeNamedLinearStyle(string name, string description, Recipe recipe, string[] species, string[] standards)
		{
			Name = name;
			Description = description;
			this.recipe = recipe;
			Species = species ?? Array.Empty<string>();
			Standards = standards ?? Array.Empty<string>();
		}

		/// <summary>The place it is laid at unless a biome's file says otherwise.</summary>
		public BiomeLinearPlace Place => recipe.Where;

		/// <summary>Its form.</summary>
		public BiomeLinearForm Form => recipe.Form;

		/// <summary>
		/// The style itself, fresh (its own lists), laid at <paramref name="where"/> (its own place when null) on
		/// <paramref name="density"/> of the places it may go.
		/// </summary>
		public BiomeLinearStyle Make(BiomeLinearPlace? where = null, float density = 1f) => recipe.Make(where ?? recipe.Where, density);

		/// <summary>What a named style fixes, beside its form and place (every other value the form's own).</summary>
		internal sealed class Recipe
		{
			public BiomeLinearForm Form;
			public BiomeLinearPlace Where;
			public float Height, Spacing, GateWidth, Breaks, Reach;
			public BiomeLinearSides Sides;
			public BiomeLinearWays Ways;
			public bool OwnMaterial;
			public BiomeStructureMaterial Material;
			public Color Tint = Color.white;
			public string Vine;

			public BiomeLinearStyle Make(BiomeLinearPlace where, float density)
			{
				var style = new BiomeLinearStyle(Form, where, density)
				{
					Height = Height,
					Spacing = Spacing,
					GateWidth = GateWidth,
					Breaks = Breaks,
					Reach = Reach,
					Sides = Sides,
					Ways = Ways,
					OwnMaterial = OwnMaterial,
					Material = OwnMaterial ? Material : BiomeStructureMaterial.Timber,
					Tint = OwnMaterial ? Tint : Color.white,
					Vine = Vine ?? string.Empty,
				};
				return style;
			}
		}
	}

	/// <summary>
	/// The drafts' named linear styles (2026-10-26, phase I2f-b; decisions 46, 49, 64, 67, 70, 77, 81, 89, 93, 96 and the styles the
	/// earlier rounds named on the I2f forms), as data: each is a <see cref="BiomeLinearStyle"/> preset a biome's file lays by name
	/// (<c>.Lines(new Entry.Line("Kershef"), new Entry.Line("Gallery"))</c>). Pure; nothing here is laid until a biome names it, so
	/// no scene changes.
	/// </summary>
	/// <remarks>
	/// The tints are the stone, brick, earth or timber a style is laid in where it names its own material (white keeps the
	/// surface's own); the plants are the drafts' species, by the names the spec gives them.
	/// </remarks>
	public static class BiomeLinearStyles
	{
		private static readonly List<BiomeNamedLinearStyle> all = new List<BiomeNamedLinearStyle>();
		private static readonly Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>Every named style, in table order.</summary>
		public static IReadOnlyList<BiomeNamedLinearStyle> All => all;

		/// <summary>The named style called <paramref name="name"/> (case as written); false when there is none.</summary>
		public static bool TryGet(string name, out BiomeNamedLinearStyle style)
		{
			if (!string.IsNullOrEmpty(name) && byName.TryGetValue(name, out int i))
			{
				style = all[i];
				return true;
			}
			style = default;
			return false;
		}

		/// <summary>True for a name in the table.</summary>
		public static bool Has(string name) => !string.IsNullOrEmpty(name) && byName.ContainsKey(name);

		private static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1f);

		private static void Add(string name, string description, BiomeLinearForm form, BiomeLinearPlace where, float height = 0f, float spacing = 0f,
			float breaks = 0f, BiomeLinearSides sides = BiomeLinearSides.Both, float reach = 0f, BiomeStructureMaterial? material = null, Color? tint = null,
			string[] species = null, string[] standards = null, float gate = 0f, BiomeLinearWays ways = BiomeLinearWays.All)
		{
			var recipe = new BiomeNamedLinearStyle.Recipe
			{
				Form = form,
				Where = where,
				Height = height,
				Spacing = spacing,
				GateWidth = gate,
				Breaks = breaks,
				Reach = reach,
				Sides = sides,
				Ways = ways,
				OwnMaterial = material.HasValue,
				Material = material ?? BiomeStructureMaterial.Timber,
				Tint = tint ?? Color.white,
			};
			byName.Add(name, all.Count);
			all.Add(new BiomeNamedLinearStyle(name, description, recipe, species, standards));
		}

		static BiomeLinearStyles()
		{
			const BiomeLinearPlace Fields = BiomeLinearPlace.FieldEdges, Lanes = BiomeLinearPlace.Lanes, Sites = BiomeLinearPlace.SiteBounds,
				Folds = BiomeLinearPlace.Folds, Wind = BiomeLinearPlace.Windbreaks, Frontier = BiomeLinearPlace.Frontier;
			const BiomeStructureMaterial Fieldstone = BiomeStructureMaterial.Fieldstone, Adobe = BiomeStructureMaterial.Adobe;

			// ── Hedges ──
			Add("ChristsThorn", "A tall shaggy hedge of Christ's thorn round the fields.", BiomeLinearForm.Wild, Fields, height: 2.4f, species: new[] { "ChristsThorn" });
			Add("Chanoki", "Tea bushes clipped into a low, rounded hedge along the field rows.", BiomeLinearForm.Clipped, Fields, height: 1f, species: new[] { "Chanoki" });
			Add("Myrtle", "A clipped myrtle hedge along a garden's walks.", BiomeLinearForm.Clipped, Sites, height: 1.2f, species: new[] { "Myrtle" });
			Add("ThornBamboo", "A living fence of thorny bamboo clumps round a village.", BiomeLinearForm.Wild, Sites, height: 4f, species: new[] { "ThornBambooClump" });
			Add("FiqueRow", "Fique rosettes set in a line 1.5 m apart along field bounds and road edges.", BiomeLinearForm.Wild, Fields, height: 1.8f, spacing: 1.5f,
				species: new[] { "Detail_Fique" });
			Add("DeadHedge", "The old thorn hedges standing dead and silver, a third gapped, hollow pollards along them.", BiomeLinearForm.Dead, Fields, height: 2f,
				breaks: 0.3f, species: new[] { "Thornwrack" }, standards: new[] { "Deadwood_PollardBoll" });

			// ── Earth banks ──
			Add("Clawdd", "An earth bank 1.5 m faced with rubble, a hedge grown along its turfed top.", BiomeLinearForm.EarthBank, Fields, height: 1.5f);
			Add("Tungardur", "A turf-and-stone homefield dyke 1.2 m, grassed on top, round fields and farms.", BiomeLinearForm.EarthBank, Fields, height: 1.2f);
			Add("SodWall", "Walls of cut sod round a yard.", BiomeLinearForm.EarthBank, Sites, height: 1.4f);
			Add("Kuaiwi", "Low stone-faced field banks, 0.6 m high, of dark lava stone.", BiomeLinearForm.EarthBank, Fields, height: 0.6f, material: Fieldstone,
				tint: Rgb(0.30f, 0.29f, 0.29f));
			Add("IyaEarthBank", "An earth bank round the fields of the forested hills.", BiomeLinearForm.EarthBank, Fields, height: 1.2f);

			// ── Walls ──
			Add("TuffRubble", "Drystone field walls of warm tuff rubble.", BiomeLinearForm.Drystone, Fields, material: Fieldstone, tint: Rgb(0.72f, 0.62f, 0.48f));
			Add("SlateStack", "Dry walls 1.2 m of flat slate courses.", BiomeLinearForm.Drystone, Fields, height: 1.2f, material: BiomeStructureMaterial.Slate);
			Add("MoraineDyke", "A drystone boundary and fold of grey moraine boulders.", BiomeLinearForm.Drystone, Folds, height: 1.1f, breaks: 0.15f, material: Fieldstone,
				tint: Rgb(0.55f, 0.56f, 0.58f));
			Add("Albarrada", "A drystone lot wall of pale limestone.", BiomeLinearForm.Drystone, Sites, height: 1.4f, material: Fieldstone, tint: Rgb(0.80f, 0.76f, 0.68f));
			Add("HillDyke", "A tall drystone hill dyke of dark whin.", BiomeLinearForm.Drystone, Fields, height: 1.5f, material: Fieldstone, tint: Rgb(0.42f, 0.44f, 0.46f));
			Add("ZimbabweWall", "A battered wall of coursed granite blocks, 1.5–3 m, round a royal enclosure.", BiomeLinearForm.Drystone, Sites, height: 2.5f,
				material: Fieldstone, tint: Rgb(0.66f, 0.63f, 0.58f));
			Add("Shilei", "A wall of piled field stone round a village.", BiomeLinearForm.Drystone, Sites, height: 1.6f, material: Fieldstone, tint: Rgb(0.60f, 0.58f, 0.55f));
			Add("Nozura", "A battered wall of big rough stones piled without mortar.", BiomeLinearForm.Drystone, Sites, height: 2.2f, material: Fieldstone,
				tint: Rgb(0.55f, 0.55f, 0.52f));
			Add("LateriteEnclosure", "A wall of red laterite blocks round a compound.", BiomeLinearForm.Mortared, Sites, height: 1.8f, material: Adobe,
				tint: Rgb(0.62f, 0.30f, 0.18f));
			Add("Kershef", "A wall of salt-crust lumps bedded in grey mud round a date garden.", BiomeLinearForm.Mortared, Sites, height: 2.2f, material: Adobe,
				tint: Rgb(0.66f, 0.63f, 0.57f));
			Add("GenoardWall", "A royal park's tall rendered wall.", BiomeLinearForm.Mortared, Sites, height: 3f, material: BiomeStructureMaterial.Stone,
				tint: Rgb(0.85f, 0.78f, 0.65f));
			Add("Brolo", "A brick garden wall 2.4 m under a tile cap, buttressed every 4 m.", BiomeLinearForm.Mortared, Sites, height: 2.4f, material: Adobe,
				tint: Rgb(0.66f, 0.33f, 0.24f));
			Add("SanseongWall", "A mountain fortress's stone curtain following the ridges.", BiomeLinearForm.Mortared, Sites, height: 4f, material: BiomeStructureMaterial.Stone,
				tint: Rgb(0.70f, 0.70f, 0.68f));
			Add("Plattenzaun", "Slabs of serpentine or granite set upright on edge, 0.8–1.2 m.", BiomeLinearForm.UprightSlabs, Fields, height: 1f);
			Add("FlagFence", "Caithness flagstones set on edge round the fields.", BiomeLinearForm.UprightSlabs, Fields, height: 1.2f, material: BiomeStructureMaterial.Slate);
			Add("BrickBarrier", "A ruined fired-brick barrier wall with an outer ditch and square brick forts, broken every 40–200 m; one to a scene.",
				BiomeLinearForm.BrickBarrier, Frontier, height: 3.5f, breaks: 0.1f, material: Adobe, tint: Rgb(0.62f, 0.27f, 0.18f));
			Add("SnowWall", "Curved walls of wind-slab blocks 1.5–2 m on the windward arc of a camp.", BiomeLinearForm.SnowWall, Wind, height: 1.7f,
				material: BiomeStructureMaterial.IceBlock, tint: Rgb(0.93f, 0.95f, 0.98f));

			// ── Fences ──
			Add("Takegaki", "A split-culm bamboo fence.", BiomeLinearForm.Picket, Sites, height: 1.6f, material: BiomeStructureMaterial.Bamboo);
			Add("Stackete", "Pointed split pickets round gardens and orchards.", BiomeLinearForm.Picket, Sites);
			Add("SandFence", "Lines of brushwood hurdles holding the sand.", BiomeLinearForm.Wattle, Fields, height: 1f, breaks: 0.2f);
			Add("PalmRibFence", "A 2 m screen of palm ribs on a palm-trunk rail, holding the dunes off the gardens.", BiomeLinearForm.PalmRib, Fields, height: 2f,
				material: BiomeStructureMaterial.Timber, tint: Rgb(0.70f, 0.58f, 0.40f));
			Add("FrondScreen", "A lower screen of palm fronds round garden plots.", BiomeLinearForm.PalmRib, Sites, height: 1.5f, breaks: 0.1f,
				material: BiomeStructureMaterial.Timber, tint: Rgb(0.62f, 0.54f, 0.36f));
			Add("OcotilloFence", "A living fence of ocotillo canes planted close round a courtyard group.", BiomeLinearForm.PalmRib, Sites, height: 1.8f,
				material: BiomeStructureMaterial.Timber, tint: Rgb(0.45f, 0.40f, 0.34f));
			Add("RibFence", "Saguaro ribs lashed upright on a rail.", BiomeLinearForm.PalmRib, Sites, height: 1.4f, material: BiomeStructureMaterial.Timber,
				tint: Rgb(0.78f, 0.72f, 0.60f));
			Add("PaintedPalisade", "A palisade of posts painted in bands.", BiomeLinearForm.Palisade, Sites, height: 2.5f, material: BiomeStructureMaterial.Timber,
				tint: Rgb(0.62f, 0.26f, 0.18f));
			Add("HandRope", "Iron-shod ash stakes every 4–6 m carrying a sagging hemp rope up ways steeper than 15°.", BiomeLinearForm.HandRope, Lanes,
				sides: BiomeLinearSides.One);
			Add("BoughBreak", "A fir-bough windbreak 1.2–1.8 m on stakes, on the windward arc of a camp.", BiomeLinearForm.BoughBreak, Wind, height: 1.5f,
				material: BiomeStructureMaterial.Thatch, tint: Rgb(0.20f, 0.30f, 0.22f));

			// ── Clearance ──
			Add("ClearanceRow", "Field stones heaped along a field's edge.", BiomeLinearForm.Heaped, Fields);

			// ── Markers ──
			Add("WandLine", "Birch wands with saffron rags every 12–18 m across the ice, a cairn at each end.", BiomeLinearForm.WandLine, Lanes, spacing: 15f,
				sides: BiomeLinearSides.One);
			Add("SergeLine", "Carved larch serge posts every 35–50 m along the ways, white horsehair tied; every fifth a fat-lamp hook.", BiomeLinearForm.SergePost, Lanes,
				spacing: 42f, sides: BiomeLinearSides.One);
			Add("TamgaPole", "Spruce poles every 40–60 m along the winter roads, a clan sign cut in each, red cloth; every sixth a bark-torch bracket.",
				BiomeLinearForm.TamgaPole, Lanes, spacing: 50f, sides: BiomeLinearSides.One);
			Add("CairnLine", "Waist-high platy cairns every 30 m along a road, each capped by a bronze token.", BiomeLinearForm.Cairn, Lanes, spacing: 30f,
				sides: BiomeLinearSides.One);
			Add("AhuLine", "Lava-stone cairns 0.8–1.5 m every 40–60 m along the trails.", BiomeLinearForm.Ahu, Lanes, spacing: 50f, sides: BiomeLinearSides.One);

			// ── Channels ──
			Add("Bisse", "A long contour leat hung across the cliffs in bracketed flumes, a water-hammer at its head.", BiomeLinearForm.Bisse, Fields, reach: 3000f);
			Add("Roggia", "Straight brick-lined channels on the land's grid, sluices every 150–300 m, partitions where they split.", BiomeLinearForm.Roggia, Fields);
			Add("GreatCanal", "One great canal a scene off a river weir, 10–20 m wide and 3 m deep, laterals to the other settlements' fields.",
				BiomeLinearForm.GreatCanal, Fields, reach: 4000f);
			Add("Gallery", "A qanat: underground from a scarp's foot, shaft mouths every 15–20 m, daylighting at an arched mouth into a lined channel.",
				BiomeLinearForm.Gallery, Fields, reach: 2000f);
		}
	}
}
