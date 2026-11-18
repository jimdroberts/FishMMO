#if UNITY_EDITOR
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How many seeded variants of each rock's formations the generator makes, and how many meshes each stone material's
	/// small rocks and pebbles cycle through in the GPU detail scatter (2026-10-26, phase I6f of the biome suite, decision
	/// 34). One table each, a row a rock or stone, so a biome round or the owner changes a count by editing one row.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Formations.</b> Every rock without a row keeps <see cref="RockTypes.VariantCount"/> (four), which is every rock
	/// there was before the biome suite's own rocks (RegionalRocks.cs), so their art is unchanged. Each of those rocks has a
	/// row: four for a crust rock of the worlds players live on, two for a formation-only rock (a mineral, ice or deposit
	/// scattered in one biome) and for a crust rock of the off-world and deep-floor biomes only. Four variants of 342 new
	/// shapes at three levels were about 4 100 meshes (about 290 MiB); these rows make it about 3 300 (about 235 MiB).
	/// </para>
	/// <para>
	/// <b>Why a count of 1, 2 or 4.</b> A cave's dressing draws a formation's variant in 0..3 (<see cref="RockTypes.VariantCount"/>)
	/// before it knows which rock's art stands in, and the name wraps that draw round the rock's count
	/// (<see cref="RockArtNames.FormationPrefab"/>), so the draws after it are the same whatever the count. A count that
	/// divides four keeps every variant equally likely; three would draw its first twice as often. No count is more than four.
	/// </para>
	/// <para>
	/// <b>Stone details.</b> A detail prefab (<c>Detail_Rocks_{material}</c>, <c>Detail_Pebbles_{material}</c>) draws its root
	/// mesh; with more than one variant it also carries bare-mesh children <c>Variant1</c>, <c>Variant2</c>
	/// (<see cref="ProceduralArtCatalogue.DetailVariantChild"/>), and the GPU detail scatter picks one per instance by its
	/// hash (DetailScatterMath.Variant, FishDetailScatter.compute). Every stone made before the biome suite's has no row
	/// and keeps one mesh, so its scenes draw exactly as before; the suite's own stones (each crust rock's, and the named
	/// sets of StoneStyles.Named.cs) cycle three. The CPU chunk renderer, and Unity's own detail drawing, draw the root.
	/// </para>
	/// </remarks>
	public static class RockVariantCounts
	{
		/// <summary>A formation's count with no row: every rock made before the biome suite's.</summary>
		public const int DefaultFormations = RockTypes.VariantCount;

		/// <summary>A stone detail's mesh count with no row: one, the root mesh, as every stone has drawn so far.</summary>
		public const int DefaultStoneDetails = 1;

		/// <summary>The most meshes a stone detail cycles (the root and two children): the GPU scatter's slices per type.</summary>
		public const int MaxStoneDetails = 3;

		private const string Formation = "formation-only", OffWorld = "off-world biome only", DeepFloor = "deep floor only", Lived = "lived-on world";

		/// <summary>The formation variants of the biome suite's rocks, in RegionalRocks' order, with why.</summary>
		public static readonly (string Rock, int Variants, string Why)[] Formations =
		{
			// The crust rocks.
			("Rhyolite", 4, Lived), ("Trachyte", 4, Lived), ("Dacite", 4, Lived), ("Komatiite", 4, Lived),
			("Dolomite", 4, Lived), ("Flysch", 4, Lived), ("Beachrock", 4, Lived), ("Dolerite", 4, Lived), ("Coralstone", 4, Lived),
			("Suevite", 4, Lived), ("Cryolava", 2, OffWorld), ("Marl", 2, DeepFloor), ("Duststone", 2, OffWorld), ("Till", 4, Lived),
			("Travertine", 4, Lived), ("Greenstone", 4, Lived), ("Loess", 4, Lived), ("Charnockite", 4, Lived), ("Troctolite", 2, OffWorld),
			("Laterite", 4, Lived), ("Molasse", 4, Lived), ("CoralRag", 4, Lived), ("Cryokarst", 2, OffWorld), ("Phonolite", 4, Lived),
			("RaftIce", 2, OffWorld), ("Mudstone", 4, Lived), ("Redbed", 4, Lived), ("SableIce", 2, OffWorld), ("Norite", 2, OffWorld),
			("Variolite", 2, OffWorld), ("Greywacke", 4, Lived), ("Flagrock", 4, Lived), ("Hornfels", 4, Lived), ("Platebasalt", 2, OffWorld),
			("Saltrock", 4, Lived), ("Granodiorite", 4, Lived), ("Caliche", 4, Lived), ("Hyaloclastite", 4, Lived), ("Eclogite", 4, Lived),
			("Porphyry", 4, Lived), ("MarineIce", 2, OffWorld), ("Alunite", 2, OffWorld), ("Tessera", 2, OffWorld), ("Nenfro", 4, Lived),
			("Hazestone", 2, OffWorld), ("BrineIce", 2, OffWorld), ("Porcelanite", 4, Lived), ("Serpentinite", 4, Lived),
			("Peridotite", 2, DeepFloor), ("Radiolarite", 2, DeepFloor), ("Carstone", 4, Lived), ("SulcusIce", 2, OffWorld),
			// The formation-only rocks.
			("Natrite", 2, Formation), ("Phosphorite", 2, Formation), ("Adamant", 2, Formation), ("NitrogenIce", 2, Formation),
			("MethaneIce", 2, Formation), ("Moundstone", 2, Formation), ("Carbonatite", 2, Formation), ("Guyotstone", 2, Formation),
			("Anhydrite", 2, Formation), ("VentCarbonate", 2, Formation), ("Leadfrost", 2, Formation), ("Duneslab", 2, Formation),
			("Oysterbank", 2, Formation), ("Moraine", 2, Formation), ("Chemoherm", 2, Formation), ("Mangancrust", 2, Formation),
			("Permafrost", 2, Formation), ("Sarsen", 2, Formation),
		};

		/// <summary>How many meshes a stone material's small rocks and pebbles cycle (no row: <see cref="DefaultStoneDetails"/>).</summary>
		public static readonly (string Stone, int Meshes)[] StoneDetails = BuildStoneDetails();

		private static Dictionary<string, int> formations, stones;

		/// <summary>The formation variants a rock has: its row, else <see cref="DefaultFormations"/>; always 1..<see cref="RockTypes.VariantCount"/>.</summary>
		public static int Of(string rock)
		{
			if (formations == null)
			{
				var all = new Dictionary<string, int>();
				foreach ((string name, int variants, string _) in Formations)
				{
					all[name] = variants;
				}
				formations = all;
			}
			int n = rock != null && formations.TryGetValue(rock, out int v) ? v : DefaultFormations;
			return n < 1 ? 1 : n > RockTypes.VariantCount ? RockTypes.VariantCount : n;
		}

		/// <summary>A drawn variant wrapped round the rock's count: always a variant the generator makes.</summary>
		public static int Wrap(string rock, int variant)
		{
			int n = Of(rock);
			int v = variant % n;
			return v < 0 ? v + n : v;
		}

		/// <summary>The meshes a stone material's small rocks and pebbles cycle: its row, else one; always 1..<see cref="MaxStoneDetails"/>.</summary>
		public static int StoneDetailsOf(string stone)
		{
			if (stones == null)
			{
				var all = new Dictionary<string, int>();
				foreach ((string name, int meshes) in StoneDetails)
				{
					all[name] = meshes;
				}
				stones = all;
			}
			int n = stone != null && stones.TryGetValue(stone, out int v) ? v : DefaultStoneDetails;
			return n < 1 ? 1 : n > MaxStoneDetails ? MaxStoneDetails : n;
		}

		/// <summary>
		/// Three meshes for every crust rock's own stone (Formations' crust rows) and every named set (StoneStyles.Named.cs);
		/// a row added below the loop overrides them.
		/// </summary>
		private static (string, int)[] BuildStoneDetails()
		{
			var rows = new List<(string, int)>();
			foreach ((string rock, int _, string why) in Formations)
			{
				if (why != Formation)
				{
					rows.Add((rock, MaxStoneDetails));
				}
			}
			foreach (string named in StoneStyles.NamedStoneNames)
			{
				rows.Add((named, MaxStoneDetails));
			}
			return rows.ToArray();
		}
	}
}
#endif
