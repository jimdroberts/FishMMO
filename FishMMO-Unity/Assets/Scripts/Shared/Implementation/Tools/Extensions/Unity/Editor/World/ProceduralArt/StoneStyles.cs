#if UNITY_EDITOR
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// One stone material's shapes: its three boulders (the Round, Slab and Spire slots of
	/// <c>Boulder_{material}_{slot}</c>), the group a <c>Detail_Rocks_{material}</c> draws and the group a
	/// <c>Detail_Pebbles_{material}</c> draws.
	/// </summary>
	public struct StoneSet
	{
		public string Material;
		/// <summary>One line: what of its rock the shapes show.</summary>
		public string Summary;
		public StoneShape Round, Slab, Spire;
		/// <summary>The small rock and its companion, the first in the middle.</summary>
		public StonePiece[] Rocks;
		/// <summary>The pebbles, the first in the middle.</summary>
		public StonePiece[] Pebbles;

		/// <summary>The boulder of a slot (<see cref="StoneStyles.Round"/>, <see cref="StoneStyles.Slab"/>, <see cref="StoneStyles.Spire"/>).</summary>
		public StoneShape Boulder(string slot) => slot == StoneStyles.Slab ? Slab : slot == StoneStyles.Spire ? Spire : Round;
	}

	/// <summary>
	/// Every stone material's shapes, each from how its rock breaks and weathers (2026-10-16, decision 6 of the biome
	/// suite): basalt as angular columnar chips, column drums and broken columns; granite as rounded corestones,
	/// onion-skin sheets and grus; sandstone as flat cross-bedded slabs with rounded edges; limestone pitted and
	/// karren-fluted; shale and slate as thin flakes and plates; river cobbles as smooth flattened ellipsoids; flint as
	/// knobbly nodules and knapped flakes; pumice and scoria as vesicular lumps; quartzite angular and glassy; manganese
	/// nodules as knobbly spheres; coral as brain heads, plates, branching heads and broken-branch rubble; ice as rounded
	/// growlers, floe slabs and shards.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Names stay; meshes become the material's.</b> A material keeps every prefab name it had (terrain data and the
	/// biome templates reference them), and each slot's mesh is now its own (<c>Boulder_{material}_{slot}_LOD{n}</c>,
	/// <c>Rocks_Small_{material}</c>, <c>Pebbles_{material}</c>; ProceduralArtCatalogue.Rocks.cs). A boulder slot keeps
	/// its role — Round the ordinary lump, Slab the flattest (fall ledges, lip stones and overhang slabs are
	/// <c>Boulder_{rock}_Slab</c> stretched to a box, so every Slab is a slab), Spire the tallest — and roughly its old
	/// size, so a scatter rule's scale range still fits.
	/// </para>
	/// <para>
	/// <b>Variants.</b> Boulders: three per material, one a slot. A detail prefab is one mesh drawn as one channel of the
	/// GPU detail scatter (at most eight a terrain), so its variants are pieces of that mesh: two stones in a small rock
	/// (the stone and a spall off it), three or four pebbles each of another habit. The scatter turns, sizes and
	/// stretches every instance by its own hash (DetailScatterMath), so no two groups lie alike.
	/// </para>
	/// <para>
	/// <b>What a grid can draw.</b> A boulder's top level is ten cells to a cube face, a small rock's three and a
	/// pebble's two or three, so the marks are sized to them: beds no finer than three to a boulder's height, a dozen
	/// flutes at most, pits a cell or more across. Finer banding is the texture's (every rock surface's strata run up
	/// the side charts). A pebble shows its rock by its habit — flake, disc, chip, nodule, rod — not by its marks.
	/// </para>
	/// </remarks>
	public static class StoneStyles
	{
		public const string Round = "Round", Slab = "Slab", Spire = "Spire";

		/// <summary>A small rock's grid: 108 triangles; its companion's 48.</summary>
		public const int RockResolution = 3, CompanionResolution = 2;

		private static Dictionary<string, StoneSet> sets;

		/// <summary>True when the material has shapes of its own (every material the catalogue lists should).</summary>
		public static bool Has(string material) => Sets.ContainsKey(material ?? string.Empty);

		/// <summary>Every material with shapes, in the order they are listed.</summary>
		public static IEnumerable<string> Materials => Sets.Keys;

		/// <summary>
		/// A material's shapes; a material listed without any (one added to the catalogue before its shapes were written)
		/// takes granite's, seeded under its own name so it is still a stone of its own.
		/// </summary>
		public static StoneSet For(string material)
		{
			if (Sets.TryGetValue(material ?? string.Empty, out StoneSet set))
			{
				return set;
			}
			StoneSet grey = Sets["Grey"];
			return Named(material, "Granite's shapes, until this material has its own.", grey.Round, grey.Slab, grey.Spire, grey.Rocks, grey.Pebbles);
		}

		private static Dictionary<string, StoneSet> Sets
		{
			get
			{
				if (sets == null)
				{
					var all = new Dictionary<string, StoneSet>();
					foreach (StoneSet set in Build())
					{
						all[set.Material] = set;
					}
					sets = all;
				}
				return sets;
			}
		}

		private static StoneShape S(float x, float y, float z) => StoneShape.Of(string.Empty, x, y, z);

		private static StonePiece P(StoneShape shape, int resolution = CompanionResolution) => new StonePiece(shape, resolution);

		/// <summary>A set with every shape named for its material and place, so each draws its own randomness.</summary>
		private static StoneSet Named(string material, string summary, StoneShape round, StoneShape slab, StoneShape spire, StonePiece[] rocks, StonePiece[] pebbles)
		{
			var set = new StoneSet
			{
				Material = material,
				Summary = summary,
				Round = round.Named($"Stone/{material}/{Round}"),
				Slab = slab.Named($"Stone/{material}/{Slab}"),
				Spire = spire.Named($"Stone/{material}/{Spire}"),
				Rocks = (StonePiece[])rocks.Clone(),
				Pebbles = (StonePiece[])pebbles.Clone(),
			};
			for (int k = 0; k < set.Rocks.Length; k++)
			{
				set.Rocks[k].Shape = set.Rocks[k].Shape.Named($"Stone/{material}/Rocks/{k}");
			}
			for (int k = 0; k < set.Pebbles.Length; k++)
			{
				set.Pebbles[k].Shape = set.Pebbles[k].Shape.Named($"Stone/{material}/Pebbles/{k}");
			}
			return set;
		}

		/// <summary>
		/// Every set. The slots' nominal sizes (the old shared boulders'): Round 1.6 × 1.2 × 1.44 m, Slab 2.64 × 0.92 ×
		/// 1.98, Spire 1.05 × 2.24 × 0.98; a small rock 0.35 × 0.21 × 0.3, a pebble 0.12 × 0.07 × 0.1.
		/// </summary>
		private static IEnumerable<StoneSet> Build()
		{
			// ── The legacy four ───────────────────────────────────────

			yield return Named("Grey", "Granite: rounded corestones weathered from its joint blocks, onion-skin sheets, subangular grus.",
				S(1.6f, 1.2f, 1.45f).Boxy(2.7f, 2.5f).Cut(4, 0.2f, 0.14f).Noise(0.45f, 0.15f),
				S(2.6f, 0.85f, 2f).Boxy(2.6f, 3.2f).Bedded(2f, 0.05f).Cut(3, 0.25f, 0.12f, -0.1f, 0.4f).Noise(0.35f, 0.1f),
				S(1.1f, 2.2f, 1f).Boxy(3.6f, 3.2f).Cut(5, 0.35f, 0.1f, -0.15f, 0.8f).Noise(0.4f, 0.15f),
				new[]
				{
					P(S(0.36f, 0.24f, 0.31f).Boxy(2.6f, 2.4f).Cut(4, 0.35f, 0.07f).Noise(0.5f, 0.2f), RockResolution),
					P(S(0.15f, 0.09f, 0.12f).Boxy(2.4f, 2.2f).Cut(3, 0.4f, 0.05f)),
				},
				new[]
				{
					P(S(0.12f, 0.075f, 0.1f).Boxy(2.3f, 2.2f).Cut(3, 0.3f, 0.06f).Noise(0.4f, 0.15f), 3),
					P(S(0.1f, 0.065f, 0.085f).Boxy(2.2f, 2f)),
					P(S(0.09f, 0.06f, 0.07f).Cut(4, 0.45f, 0.05f)),
				});

			yield return Named("Sandstone", "Flat slabs parted along their beds, cross-bedded, every edge worn round.",
				S(1.7f, 1f, 1.4f).Boxy(3.2f, 3f).Bedded(2.5f, 0.06f).Cut(3, 0.3f, 0.15f, -0.1f, 0.3f).Noise(0.3f, 0.08f),
				S(2.7f, 0.7f, 2f).Boxy(4f, 5f).Bedded(2f, 0.05f, 18f).Cut(4, 0.35f, 0.16f, -0.1f, 0.2f).Noise(0.25f, 0.05f),
				S(1.1f, 2.2f, 1f).Boxy(3f, 2.6f).Bedded(3f, 0.09f).Tapered(0.25f).Noise(0.3f, 0.1f),
				new[]
				{
					P(S(0.38f, 0.16f, 0.3f).Boxy(3.5f, 4f).Bedded(1f, 0.04f, 15f).Soft(0.15f), RockResolution),
					P(S(0.16f, 0.06f, 0.12f).Boxy(3.5f, 4f).Soft(0.12f)),
				},
				new[]
				{
					P(S(0.13f, 0.05f, 0.1f).Boxy(2.6f, 3f), 3),
					P(S(0.1f, 0.04f, 0.08f).Boxy(3f, 3f).Cut(2, 0.3f, 0.1f, -0.1f, 0.2f)),
					P(S(0.09f, 0.045f, 0.07f).Boxy(2.4f, 2.5f)),
				});

			yield return Named("Basalt", "Columnar jointing: blocky boulders with conchoidal breaks, column drums, broken columns, angular chips.",
				S(1.6f, 1.15f, 1.4f).Boxy(2.4f, 2.4f).Cut(9, 0.55f, 0.04f).Conchoidal(2, 0.308f, 0.9f).Noise(0.2f, 0.25f),
				S(2.4f, 0.9f, 2.1f).Prism(6, 0.25f).Soft(0.05f).Conchoidal(1, 0.15f, 1.6f).Noise(0.15f, 0.2f),
				S(0.95f, 2.3f, 0.9f).Prism(6, 0.22f).Cut(1, 0.4f, 0.04f, 0.5f, 0.9f).Conchoidal(1, 0.252f, 0.7f).Noise(0.12f, 0.2f),
				new[]
				{
					P(S(0.36f, 0.22f, 0.3f).Prism(5, 0.3f).Cut(2, 0.5f, 0.03f).Noise(0.15f, 0.2f), RockResolution),
					P(S(0.15f, 0.08f, 0.11f).Cut(6, 0.6f, 0.02f)),
				},
				new[]
				{
					P(S(0.12f, 0.07f, 0.1f).Cut(7, 0.6f, 0.02f).Noise(0.1f, 0.2f), 3),
					P(S(0.1f, 0.06f, 0.08f).Prism(5, 0.3f).Soft(0.02f)),
					P(S(0.09f, 0.055f, 0.075f).Cut(6, 0.55f, 0.02f)),
				});

			yield return Named("Limestone", "Pale blocks that dissolve: solution pits and pans, karren flutes down from their tops, grike-cut sides.",
				S(1.6f, 1.15f, 1.45f).Boxy(3f, 2.6f).Pitted(7, 0.32f, 0.126f).Fluted(8, 0.126f).Cut(3, 0.3f, 0.12f, -0.1f, 0.4f).Noise(0.3f, 0.1f),
				S(2.6f, 0.85f, 2f).Boxy(5f, 4f).Pitted(4, 0.4f, 0.108f).Cut(4, 0.3f, 0.06f, -0.15f, 0.15f).Noise(0.2f, 0.08f),
				S(1f, 2.3f, 0.95f).Boxy(2.4f, 2.2f).Fluted(9, 0.18f).Pitted(5, 0.3f, 0.108f).Tapered(0.35f).Noise(0.3f, 0.1f),
				new[]
				{
					P(S(0.36f, 0.25f, 0.31f).Boxy(3f, 2.6f).Pitted(4, 0.5f, 0.144f).Cut(3, 0.35f, 0.08f), RockResolution),
					P(S(0.15f, 0.1f, 0.12f).Boxy(2.8f, 2.4f).Pitted(2, 0.6f, 0.144f)),
				},
				new[]
				{
					P(S(0.12f, 0.08f, 0.1f).Boxy(2.8f, 2.4f).Pitted(3, 0.6f, 0.18f), 3),
					P(S(0.1f, 0.07f, 0.085f).Boxy(3f, 2.6f).Cut(3, 0.3f, 0.08f)),
					P(S(0.085f, 0.06f, 0.07f).Boxy(2.4f, 2.2f).Pitted(2, 0.7f, 0.18f)),
				});

			// ── The sea's and the ice's ───────────────────────────────

			yield return Named("Nodule", "Manganese nodules: knobbly botryoidal spheres and discs grown a millimetre a million years.",
				S(1.3f, 1f, 1.25f).Knobbly(24, 0.25f, 0.1f).Noise(0.1f, 0.03f, 0.5f),
				S(2f, 0.8f, 1.8f).Knobbly(22, 0.27f, 0.1f).Noise(0.1f, 0.03f, 0.5f),
				S(1f, 1.8f, 0.95f).Knobbly(22, 0.27f, 0.11f).Noise(0.12f, 0.03f, 0.5f),
				new[]
				{
					P(S(0.3f, 0.24f, 0.29f).Knobbly(7, 0.6f, 0.238f), RockResolution),
					P(S(0.14f, 0.11f, 0.13f).Knobbly(4, 0.8f, 0.255f)),
				},
				new[]
				{
					P(S(0.11f, 0.09f, 0.105f).Knobbly(6, 0.65f, 0.255f), 3),
					P(S(0.09f, 0.07f, 0.085f).Knobbly(4, 0.8f, 0.272f)),
					P(S(0.08f, 0.06f, 0.075f).Knobbly(4, 0.8f, 0.255f)),
				});

			yield return Named("Coral", "Dead coral: brain heads, table plates, branching heads, and rubble of broken branches.",
				S(1.5f, 0.95f, 1.45f).Bedded(3.5f, 0.22f, 60f).Meandering(1.6f).Noise(0.2f, 0.05f),
				S(2.4f, 0.55f, 2.2f).Boxy(2.2f, 3f).Fluted(14, 0.108f).Knobbly(10, 0.18f, 0.068f).Noise(0.2f, 0.05f),
				S(1.4f, 2.6f, 1.4f).Cored(0.4f).Branched(6, 1f, 0.2f, 0.45f).Noise(0.15f, 0.05f),
				new[]
				{
					P(S(0.4f, 0.12f, 0.13f).Knobbly(6, 0.5f, 0.102f), RockResolution),
					P(S(0.14f, 0.09f, 0.13f).Bedded(1.5f, 0.12f, 60f).Meandering(1.2f)),
				},
				new[]
				{
					P(S(0.16f, 0.05f, 0.05f).Knobbly(4, 0.6f, 0.136f), 3),
					P(S(0.12f, 0.045f, 0.05f)),
					P(S(0.08f, 0.06f, 0.075f).Knobbly(3, 0.8f, 0.17f)),
				});

			yield return Named("Ice", "Ice: rounded melt-scalloped growlers, flat floe slabs, angular shards; Titan's rounded water-ice cobbles.",
				S(1.6f, 1.1f, 1.45f).Boxy(2.1f, 2f).Conchoidal(5, 0.168f, 1f).Soft(0.1f).Noise(0.25f, 0.03f),
				S(2.6f, 0.7f, 2f).Boxy(5f, 6f).Cut(5, 0.35f, 0.03f, -0.1f, 0.15f).Noise(0.1f, 0.05f),
				S(0.9f, 2.3f, 0.7f).Boxy(3f, 2.6f).Cut(9, 0.65f, 0.01f, -0.3f, 0.8f).Tapered(0.15f, 0.25f).Noise(0.05f, 0.05f),
				new[]
				{
					P(S(0.36f, 0.22f, 0.24f).Cut(6, 0.6f, 0.02f).Noise(0.05f, 0.05f), RockResolution),
					P(S(0.15f, 0.1f, 0.13f).Soft(0.1f).Noise(0.2f, 0f)),
				},
				new[]
				{
					P(S(0.12f, 0.08f, 0.11f).Noise(0.2f, 0.02f), 3),
					P(S(0.1f, 0.05f, 0.07f).Cut(5, 0.55f, 0.02f)),
					P(S(0.08f, 0.06f, 0.075f).Noise(0.15f, 0f)),
				});

			// ── The rest of the crust (2026-10-16), each wearing its rock type's own material ──

			yield return Named("Andesite", "Blocky and subangular with platy flow jointing: rough angular blocks and plates.",
				S(1.6f, 1.1f, 1.4f).Boxy(3f, 2.8f).Cut(6, 0.4f, 0.06f, -0.15f, 0.6f).Bedded(2.5f, 0.05f, 8f).Noise(0.25f, 0.25f),
				S(2.6f, 0.7f, 2f).Boxy(4f, 5f).Bedded(2f, 0.06f, 8f).Cut(5, 0.4f, 0.05f, -0.1f, 0.3f).Noise(0.2f, 0.25f),
				S(1.05f, 2.1f, 0.95f).Boxy(3.2f, 3f).Cut(7, 0.5f, 0.04f, -0.15f, 0.8f).Bedded(3f, 0.04f, 8f).Noise(0.25f, 0.25f),
				new[]
				{
					P(S(0.36f, 0.2f, 0.3f).Boxy(3f, 3f).Cut(5, 0.45f, 0.05f), RockResolution),
					P(S(0.15f, 0.07f, 0.12f).Cut(5, 0.5f, 0.04f)),
				},
				new[]
				{
					P(S(0.12f, 0.06f, 0.1f).Boxy(3f, 3f).Cut(4, 0.4f, 0.05f), 3),
					P(S(0.1f, 0.05f, 0.085f).Boxy(3f, 3.5f).Cut(3, 0.45f, 0.04f)),
					P(S(0.09f, 0.05f, 0.07f).Cut(5, 0.45f, 0.04f)),
				});

			yield return Named("Obsidian", "Volcanic glass: chunks and blades scalloped by conchoidal scars with razor edges; rounded Apache tears.",
				S(1.5f, 1.1f, 1.35f).Boxy(2.2f, 2.2f).Conchoidal(7, 0.4f, 0.75f).Cut(4, 0.5f, 0.015f).Noise(0.1f, 0.05f),
				S(2.4f, 0.55f, 1.8f).Boxy(3f, 3f).Conchoidal(6, 0.4f, 0.7f).Cut(3, 0.45f, 0.015f, -0.1f, 0.2f).Noise(0.08f, 0.05f),
				S(0.85f, 2.3f, 0.45f).Boxy(3f, 2.6f).Conchoidal(6, 0.4f, 0.6f).Cut(6, 0.6f, 0.012f, -0.3f, 0.8f).Tapered(0.15f, 0.2f).Noise(0.05f, 0.05f),
				new[]
				{
					P(S(0.36f, 0.2f, 0.26f).Conchoidal(4, 0.4f, 0.8f).Cut(2, 0.5f, 0.015f), RockResolution),
					P(S(0.16f, 0.05f, 0.07f).Conchoidal(2, 0.4f, 0.8f).Tapered(0.3f)),
				},
				new[]
				{
					P(S(0.12f, 0.06f, 0.08f).Conchoidal(3, 0.4f, 0.8f).Cut(2, 0.5f, 0.015f), 3),
					P(S(0.07f, 0.06f, 0.065f).Soft(0.1f).Noise(0.1f, 0f)),
					P(S(0.1f, 0.03f, 0.07f).Conchoidal(2, 0.35f, 0.9f)),
				});

			yield return Named("Pumice", "Glass froth: light rounded lumps pitted all over with vesicles.",
				S(1.5f, 1.1f, 1.35f).Boxy(2.1f, 2f).Pitted(18, 0.3f, 0.144f).Noise(0.45f, 0.1f, 0.8f),
				S(2.4f, 0.8f, 1.9f).Boxy(2.6f, 2.6f).Pitted(16, 0.32f, 0.144f).Noise(0.4f, 0.1f, 0.8f),
				S(1f, 2f, 0.95f).Boxy(2.2f, 2f).Pitted(16, 0.3f, 0.144f).Tapered(0.2f).Noise(0.45f, 0.1f, 0.8f),
				new[]
				{
					P(S(0.34f, 0.24f, 0.3f).Pitted(6, 0.55f, 0.18f).Noise(0.4f, 0.1f, 0.8f), RockResolution),
					P(S(0.15f, 0.11f, 0.13f).Pitted(3, 0.7f, 0.18f)),
				},
				new[]
				{
					P(S(0.12f, 0.09f, 0.11f).Pitted(4, 0.6f, 0.18f), 3),
					P(S(0.1f, 0.07f, 0.085f).Pitted(2, 0.7f, 0.18f)),
					P(S(0.08f, 0.065f, 0.075f).Noise(0.4f, 0.1f)),
				});

			yield return Named("Tuff", "Soft compacted ash: rounded blocks hollowed into tafoni, cones under hard caps.",
				S(1.6f, 1.15f, 1.45f).Boxy(2.8f, 2.6f).Pitted(6, 0.45f, 0.216f).Soft(0.15f).Noise(0.35f, 0.08f),
				S(2.6f, 0.85f, 2f).Boxy(3.5f, 3.5f).Pitted(5, 0.45f, 0.18f).Soft(0.15f).Noise(0.3f, 0.08f),
				S(1.1f, 2.2f, 1.05f).Boxy(2.2f, 2f).Tapered(0.55f).Pitted(4, 0.4f, 0.144f).Noise(0.3f, 0.08f),
				new[]
				{
					P(S(0.35f, 0.24f, 0.31f).Boxy(2.6f, 2.4f).Pitted(3, 0.6f, 0.18f), RockResolution),
					P(S(0.15f, 0.1f, 0.13f).Boxy(2.4f, 2.2f)),
				},
				new[]
				{
					P(S(0.12f, 0.08f, 0.1f).Boxy(2.4f, 2.2f).Pitted(2, 0.7f, 0.18f), 3),
					P(S(0.1f, 0.07f, 0.085f).Boxy(2.4f, 2.2f)),
					P(S(0.085f, 0.06f, 0.07f).Boxy(2.2f, 2f)),
				});

			yield return Named("Shale", "Fissile mud rock: platy blocks of stacked layers with ragged outlines, and thin crumbly flakes.",
				S(1.6f, 0.8f, 1.4f).Boxy(4f, 6f).Bedded(3f, 0.08f).Cut(6, 0.4f, 0.03f, -0.1f, 0.25f).Noise(0.15f, 0.25f),
				S(2.6f, 0.5f, 2f).Boxy(5f, 6f).Bedded(2f, 0.07f).Cut(6, 0.45f, 0.02f, -0.1f, 0.2f).Noise(0.12f, 0.2f),
				S(1.2f, 1.9f, 0.55f).Boxy(5f, 5f).Bedded(3f, 0.08f, 75f).Cut(5, 0.35f, 0.03f, -0.2f, 0.4f).Noise(0.15f, 0.2f),
				new[]
				{
					P(S(0.38f, 0.07f, 0.3f).Boxy(5f, 8f).Cut(6, 0.5f, 0.015f, -0.1f, 0.2f).Noise(0.1f, 0.25f), RockResolution),
					P(S(0.16f, 0.035f, 0.12f).Boxy(5f, 8f).Cut(5, 0.5f, 0.015f, -0.1f, 0.2f)),
				},
				new[]
				{
					P(S(0.13f, 0.025f, 0.1f).Boxy(4f, 8f).Cut(5, 0.5f, 0.015f, -0.1f, 0.2f)),
					P(S(0.11f, 0.02f, 0.08f).Boxy(4f, 8f).Cut(5, 0.5f, 0.015f, -0.1f, 0.2f)),
					P(S(0.1f, 0.025f, 0.07f).Boxy(4f, 8f).Cut(4, 0.5f, 0.015f, -0.1f, 0.2f)),
					P(S(0.08f, 0.02f, 0.06f).Boxy(4f, 8f).Cut(4, 0.5f, 0.015f, -0.1f, 0.2f)),
				});

			yield return Named("Conglomerate", "Cemented gravel: rounded lumps with their cobbles standing out as knobs, and the cobbles weathered free.",
				S(1.6f, 1.15f, 1.45f).Boxy(2.6f, 2.4f).Knobbly(18, 0.2f, 0.14f).Noise(0.35f, 0.1f),
				S(2.6f, 0.85f, 2f).Boxy(3.5f, 4f).Knobbly(18, 0.2f, 0.14f).Bedded(1.5f, 0.05f).Noise(0.3f, 0.08f),
				S(1.05f, 2.2f, 1f).Boxy(2.8f, 2.6f).Knobbly(18, 0.2f, 0.14f).Tapered(0.2f).Noise(0.35f, 0.1f),
				new[]
				{
					P(S(0.36f, 0.24f, 0.31f).Knobbly(5, 0.5f, 0.204f), RockResolution),
					P(S(0.12f, 0.08f, 0.1f).Noise(0.1f, 0.02f)),
				},
				new[]
				{
					P(S(0.12f, 0.08f, 0.1f).Knobbly(4, 0.6f, 0.204f), 3),
					P(S(0.09f, 0.065f, 0.08f).Noise(0.12f, 0.02f)),
					P(S(0.08f, 0.05f, 0.065f).Noise(0.12f, 0.02f)),
				});

			yield return Named("Chalk", "Soft white limestone: blocks and stacks with every edge rounded off, faint thick beds.",
				S(1.6f, 1.15f, 1.45f).Boxy(3f, 2.8f).Cut(3, 0.3f, 0.2f, -0.1f, 0.5f).Bedded(2f, 0.03f).Noise(0.25f, 0.05f),
				S(2.6f, 0.85f, 2f).Boxy(4f, 4f).Cut(3, 0.3f, 0.2f, -0.1f, 0.3f).Noise(0.2f, 0.05f),
				S(1.05f, 2.2f, 1f).Boxy(3f, 2.6f).Cut(3, 0.3f, 0.2f, -0.15f, 0.3f).Tapered(0.15f).Noise(0.25f, 0.05f),
				new[]
				{
					P(S(0.36f, 0.24f, 0.31f).Boxy(2.8f, 2.6f).Cut(2, 0.3f, 0.15f), RockResolution),
					P(S(0.15f, 0.1f, 0.12f).Boxy(2.6f, 2.4f).Soft(0.12f)),
				},
				new[]
				{
					P(S(0.12f, 0.08f, 0.1f).Boxy(2.6f, 2.4f).Soft(0.15f), 3),
					P(S(0.1f, 0.07f, 0.085f).Boxy(2.6f, 2.4f).Soft(0.12f)),
					P(S(0.085f, 0.06f, 0.07f).Boxy(2.4f, 2.2f).Cut(2, 0.3f, 0.12f)),
				});

			yield return Named("Slate", "Slaty cleavage: thin flat plates and flags with straight sharp edges, cleaved blocks, standing slabs.",
				S(1.5f, 1f, 1.2f).Boxy(6f, 6f).Bedded(3f, 0.06f, 72f).Cut(5, 0.3f, 0.015f, -0.15f, 0.3f).Noise(0.08f, 0.1f),
				S(2.7f, 0.4f, 2f).Boxy(7f, 6f).Cut(5, 0.4f, 0.01f, -0.05f, 0.1f).Noise(0.05f, 0.08f),
				S(1f, 2.2f, 0.32f).Boxy(8f, 8f).Cut(4, 0.35f, 0.012f, -0.1f, 0.5f).Noise(0.05f, 0.08f),
				new[]
				{
					P(S(0.38f, 0.06f, 0.28f).Boxy(8f, 10f).Cut(5, 0.45f, 0.01f, -0.05f, 0.1f), RockResolution),
					P(S(0.16f, 0.03f, 0.12f).Boxy(8f, 10f).Cut(4, 0.45f, 0.01f, -0.05f, 0.1f)),
				},
				new[]
				{
					P(S(0.13f, 0.022f, 0.1f).Boxy(8f, 10f).Cut(4, 0.45f, 0.01f, -0.05f, 0.1f)),
					P(S(0.11f, 0.02f, 0.075f).Boxy(8f, 10f).Cut(4, 0.45f, 0.01f, -0.05f, 0.1f)),
					P(S(0.1f, 0.018f, 0.065f).Boxy(8f, 10f).Cut(3, 0.45f, 0.01f, -0.05f, 0.1f)),
					P(S(0.075f, 0.016f, 0.06f).Boxy(8f, 10f).Cut(3, 0.45f, 0.01f, -0.05f, 0.1f)),
				});

			yield return Named("Schist", "Mica-rich and foliated: lenticular lumps and flaggy plates with crinkled ribs along the foliation.",
				S(1.8f, 0.95f, 1.2f).Boxy(2.4f, 2.6f).Bedded(3f, 0.06f, 25f).Cut(3, 0.25f, 0.08f, -0.1f, 0.3f).Noise(0.3f, 0.35f),
				S(2.6f, 0.55f, 1.8f).Boxy(3.5f, 6f).Bedded(2f, 0.06f, 10f).Cut(4, 0.35f, 0.04f, -0.1f, 0.2f).Noise(0.25f, 0.35f),
				S(1.1f, 2f, 0.6f).Boxy(3f, 3.5f).Bedded(3f, 0.07f, 70f).Tapered(0.2f).Noise(0.3f, 0.35f),
				new[]
				{
					P(S(0.4f, 0.12f, 0.24f).Boxy(2.4f, 3f).Noise(0.3f, 0.3f), RockResolution),
					P(S(0.17f, 0.05f, 0.1f).Boxy(2.4f, 3f).Noise(0.3f, 0.3f)),
				},
				new[]
				{
					P(S(0.14f, 0.045f, 0.08f).Boxy(2.2f, 2.6f), 3),
					P(S(0.11f, 0.035f, 0.07f).Boxy(2.2f, 2.6f)),
					P(S(0.1f, 0.03f, 0.06f).Cut(2, 0.3f, 0.05f, -0.1f, 0.2f)),
				});

			yield return Named("Gneiss", "Banded and massive: rounded blocks with broad dipping bands on every face.",
				S(1.6f, 1.15f, 1.4f).Boxy(3f, 2.8f).Bedded(3f, 0.04f, 30f).Cut(3, 0.25f, 0.12f, -0.1f, 0.4f).Noise(0.4f, 0.15f),
				S(2.6f, 0.8f, 2f).Boxy(3.5f, 4f).Bedded(2f, 0.05f, 15f).Cut(3, 0.3f, 0.12f, -0.1f, 0.3f).Noise(0.35f, 0.12f),
				S(1.05f, 2.2f, 1f).Boxy(3.5f, 3f).Bedded(3f, 0.05f, 60f).Cut(4, 0.35f, 0.1f, -0.15f, 0.8f).Noise(0.35f, 0.15f),
				new[]
				{
					P(S(0.36f, 0.22f, 0.3f).Boxy(2.8f, 2.6f).Cut(3, 0.3f, 0.1f), RockResolution),
					P(S(0.15f, 0.09f, 0.12f).Boxy(2.6f, 2.4f)),
				},
				new[]
				{
					P(S(0.12f, 0.06f, 0.1f).Boxy(2.4f, 2.4f), 3),
					P(S(0.1f, 0.055f, 0.085f).Boxy(2.6f, 2.6f).Cut(2, 0.3f, 0.08f)),
					P(S(0.09f, 0.05f, 0.075f).Boxy(2.2f, 2.2f)),
				});

			yield return Named("Marble", "Sugary and soluble: boulders rounded at every edge with rain-etched grooves and pans; broken pieces crisp rhombic blocks.",
				S(1.55f, 1.15f, 1.4f).Boxy(3.4f, 3f).Cut(4, 0.3f, 0.2f, -0.1f, 0.5f).Fluted(6, 0.08f).Pitted(4, 0.4f, 0.1f).Noise(0.15f, 0.05f),
				S(2.6f, 0.8f, 2f).Boxy(4.5f, 4f).Cut(4, 0.3f, 0.2f, -0.1f, 0.3f).Pitted(4, 0.45f, 0.1f).Noise(0.12f, 0.04f),
				S(1f, 2.2f, 1f).Boxy(3.4f, 3f).Cut(3, 0.3f, 0.2f, -0.1f, 0.4f).Fluted(7, 0.09f).Noise(0.15f, 0.05f),
				new[]
				{
					P(S(0.35f, 0.22f, 0.3f).Boxy(4f, 4f).Cut(5, 0.45f, 0.04f, -0.2f, 0.6f).Noise(0.1f, 0.05f), RockResolution),
					P(S(0.15f, 0.09f, 0.12f).Boxy(4f, 4f).Cut(4, 0.45f, 0.04f)),
				},
				new[]
				{
					P(S(0.12f, 0.07f, 0.1f).Boxy(4f, 4f).Cut(4, 0.45f, 0.04f), 3),
					P(S(0.1f, 0.06f, 0.085f).Boxy(4f, 4f).Cut(3, 0.45f, 0.04f)),
					P(S(0.085f, 0.05f, 0.07f).Boxy(2.8f, 2.6f).Soft(0.15f)),
				});

			yield return Named("Quartzite", "Fused quartz: angular blocks and chips with sharp, faintly conchoidal faces.",
				S(1.6f, 1.15f, 1.4f).Boxy(2.2f, 2.2f).Cut(10, 0.6f, 0.02f).Conchoidal(2, 0.21f, 1f).Noise(0.1f, 0.2f),
				S(2.6f, 0.8f, 2f).Boxy(4f, 5f).Cut(6, 0.45f, 0.02f, -0.15f, 0.35f).Conchoidal(1, 0.168f, 1.2f).Noise(0.08f, 0.15f),
				S(1.05f, 2.2f, 0.95f).Boxy(2.6f, 2.6f).Cut(8, 0.6f, 0.02f, -0.2f, 0.8f).Noise(0.08f, 0.15f),
				new[]
				{
					P(S(0.36f, 0.22f, 0.3f).Cut(7, 0.55f, 0.02f).Conchoidal(1, 0.21f, 1f).Noise(0.08f, 0.15f), RockResolution),
					P(S(0.15f, 0.08f, 0.11f).Cut(6, 0.6f, 0.015f)),
				},
				new[]
				{
					P(S(0.12f, 0.07f, 0.1f).Boxy(2.6f, 2.6f).Cut(7, 0.55f, 0.02f), 3),
					P(S(0.1f, 0.06f, 0.08f).Cut(6, 0.6f, 0.015f)),
					P(S(0.09f, 0.05f, 0.075f).Boxy(3f, 3f).Cut(5, 0.55f, 0.015f)),
				});

			yield return Named("Scoria", "Gas-blown clinker: irregular spiny vesicular lumps, spatter clots, twisted spindle bombs.",
				S(1.5f, 1.05f, 1.35f).Boxy(2.2f, 2.2f).Pitted(14, 0.3f, 0.18f).Knobbly(8, 0.25f, 0.17f).Cut(2, 0.3f, 0.06f).Noise(0.6f, 0.6f, 1f),
				S(2.4f, 0.65f, 1.9f).Pitted(12, 0.32f, 0.18f).Knobbly(8, 0.3f, 0.204f).Noise(0.6f, 0.6f, 1f),
				S(1.05f, 2f, 0.95f).Knobbly(10, 0.3f, 0.204f).Pitted(10, 0.3f, 0.18f).Tapered(0.3f, 0.15f).Noise(0.6f, 0.6f, 1f),
				new[]
				{
					P(S(0.34f, 0.22f, 0.3f).Knobbly(5, 0.5f, 0.238f).Pitted(4, 0.5f, 0.18f).Noise(0.6f, 0.6f, 1f), RockResolution),
					P(S(0.2f, 0.08f, 0.09f).Noise(0.3f, 0.3f)),
				},
				new[]
				{
					P(S(0.11f, 0.08f, 0.1f).Knobbly(4, 0.6f, 0.255f).Noise(0.6f, 0.6f), 3),
					P(S(0.09f, 0.06f, 0.08f).Pitted(2, 0.7f, 0.216f).Noise(0.6f, 0.6f)),
					P(S(0.12f, 0.05f, 0.055f).Noise(0.3f, 0.3f)),
				});

			yield return Named("Breccia", "Shock-shattered rock welded back together: very angular irregular blocks and elongated shards.",
				S(1.6f, 1.15f, 1.4f).Boxy(2.4f, 2.4f).Cut(10, 0.6f, 0.025f).Knobbly(6, 0.18f, 0.085f).Noise(0.2f, 0.3f),
				S(2.5f, 0.8f, 2f).Boxy(3f, 3f).Cut(8, 0.55f, 0.025f, -0.1f, 0.4f).Noise(0.2f, 0.3f),
				S(1.05f, 2.1f, 0.95f).Cut(8, 0.6f, 0.025f, -0.2f, 0.6f).Noise(0.2f, 0.3f),
				new[]
				{
					P(S(0.36f, 0.22f, 0.3f).Cut(8, 0.6f, 0.02f).Noise(0.2f, 0.3f), RockResolution),
					P(S(0.17f, 0.07f, 0.09f).Cut(6, 0.65f, 0.015f)),
				},
				new[]
				{
					P(S(0.13f, 0.06f, 0.07f).Cut(7, 0.65f, 0.02f), 3),
					P(S(0.11f, 0.05f, 0.06f).Cut(6, 0.65f, 0.015f)),
					P(S(0.09f, 0.05f, 0.05f).Cut(6, 0.65f, 0.015f)),
				});

			// ── Stones of no one rock type (2026-10-16), each with a material of its own ──

			yield return Named("Flint", "Flint from the chalk: knobbly nodules, tabular sheets and barrel-shaped paramoudras; knapped flakes.",
				S(1.4f, 1f, 1.2f).Knobbly(7, 0.5f, 0.374f).Conchoidal(1, 0.4f, 1f).Noise(0.3f, 0.05f),
				S(2.3f, 0.6f, 1.7f).Knobbly(8, 0.45f, 0.255f).Noise(0.3f, 0.05f),
				S(1f, 2f, 0.95f).Boxy(2f, 2.4f).Knobbly(8, 0.45f, 0.255f).Noise(0.3f, 0.05f),
				new[]
				{
					P(S(0.38f, 0.15f, 0.17f).Knobbly(5, 0.55f, 0.306f), RockResolution),
					P(S(0.12f, 0.03f, 0.09f).Conchoidal(2, 0.35f, 0.9f).Cut(2, 0.5f, 0.01f, -0.1f, 0.2f)),
				},
				new[]
				{
					P(S(0.12f, 0.08f, 0.09f).Knobbly(4, 0.7f, 0.34f), 3),
					P(S(0.09f, 0.025f, 0.065f).Conchoidal(2, 0.35f, 0.9f).Cut(2, 0.5f, 0.01f, -0.1f, 0.2f)),
					P(S(0.09f, 0.06f, 0.075f).Noise(0.15f, 0.02f)),
				});

			yield return Named("Cobble", "River and beach cobbles: smooth flattened ellipsoids worn round by the water — discs, blades, rods and spheres.",
				S(1.6f, 0.95f, 1.3f).Noise(0.08f, 0f),
				S(2.6f, 0.75f, 2f).Boxy(2.2f, 2f).Noise(0.06f, 0f),
				S(1f, 2f, 0.85f).Noise(0.08f, 0f),
				new[]
				{
					P(S(0.36f, 0.18f, 0.27f).Noise(0.06f, 0f), RockResolution),
					P(S(0.15f, 0.07f, 0.11f).Noise(0.05f, 0f)),
				},
				new[]
				{
					P(S(0.12f, 0.04f, 0.11f).Noise(0.05f, 0f)),
					P(S(0.13f, 0.04f, 0.07f).Noise(0.05f, 0f)),
					P(S(0.12f, 0.05f, 0.055f).Noise(0.05f, 0f)),
					P(S(0.08f, 0.07f, 0.075f).Noise(0.05f, 0f)),
				});
		}
	}
}
#endif
