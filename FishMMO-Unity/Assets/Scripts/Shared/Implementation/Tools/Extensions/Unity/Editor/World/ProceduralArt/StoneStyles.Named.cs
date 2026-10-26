#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The named stone sets the biome drafts ask for beyond each rock's own (2026-10-26, phase I6f of the biome suite, decisions
	/// 6 and 34): maerl and coral rubble, river gravels, ruin rubble and fallen dressed blocks, chert, furrow and strand stones,
	/// haze knots, ice cobbles, striated glacial stones, tezontle, duneslab. Each is a stone material of decision 6's machinery
	/// (I6b): <c>Boulder_{Name}_{Round,Slab,Spire}</c>, <c>Detail_Rocks_{Name}</c>, <c>Detail_Pebbles_{Name}</c>, its own material
	/// over its rock's surface (<see cref="NamedStones"/>), and three variant meshes a detail (<see cref="RockVariantCounts"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Added, never used yet.</b> No spec names them until a biome's round authors its rules, so generating them adds files
	/// and changes none that exist. The boulder slots keep their roles (Slab the flattest, Spire the tallest, sizes near the
	/// nominal), from the habit helper of StoneStyles.Regional.cs; the small rocks and pebbles are each draft's own.
	/// </para>
	/// <para>
	/// <b>Served by sets that already exist</b> (no set here): Pebbles_Till and the Findling boulders (Till's set), Rocks_Dolerite,
	/// Rocks_Coralstone, Rocks_Greywacke, Pebbles/Boulder_CoralRag, Rocks_Komatiite, Rocks_Trachyte, Radiolarite's stones,
	/// Greenstone's, Molasse's, Pebbles_Caliche (each crust rock's own, StoneStyles.Regional.cs), Rocks_Quartzite and
	/// Pebbles_Sandstone (decision 6's). A draft's own colours for them (Sandstone's grey-green iron-rinded chips, maerl's pink)
	/// are texture variants: phase I9.
	/// </para>
	/// </remarks>
	public static partial class StoneStyles
	{
		/// <summary>
		/// Every named set: its name, the rock type whose surface its material wears (its own name when it is named for the rock:
		/// it then wears that rock's own material), and the ground family a caller asking for one would want.
		/// </summary>
		public static readonly (string Name, string Surface, string Ground)[] NamedStones =
		{
			("Maerl", "Rhyolite", Ground.Coral),
			("CoralRubble", "Coralstone", Ground.Coral),
			("Rhine", "Conglomerate", Ground.Pebbles),
			("Imbricated", "Greywacke", Ground.Pebbles),
			("Rubble", "Limestone", Ground.Limestone),
			("Chert", "Porcelanite", Ground.Limestone),
			("BlockFall", "Sandstone", Ground.Sandstone),
			("Furrow", "Limestone", Ground.Limestone),
			("Strand", "Gneiss", Ground.Pebbles),
			("HazeKnot", "Duneslab", Ground.Tholin),
			("IceCobble", "WaterIce", Ground.Ice),
			("Striated", "Granite", Ground.Rock),
			("Tezontle", "Scoria", Ground.Cinder),
			("Duneslab", "Duneslab", Ground.TholinDune),
		};

		/// <summary>The named sets' names, in <see cref="NamedStones"/>' order.</summary>
		public static readonly string[] NamedStoneNames = Array.ConvertAll(NamedStones, n => n.Name);

		/// <summary>The rock type whose surface a named set's material wears, or null for a name that is not one.</summary>
		public static string NamedStoneSurface(string name)
		{
			foreach ((string named, string surface, string _) in NamedStones)
			{
				if (named == name)
				{
					return surface;
				}
			}
			return null;
		}

		/// <summary>
		/// A detail group's pieces for one of the meshes it cycles (<see cref="RockVariantCounts.StoneDetailsOf"/>): variant 0
		/// is the group itself, so the mesh it always had keeps its bytes; another starts the group at another piece and draws
		/// every piece's randomness under its own name, so the same habits lie in another arrangement and other forms.
		/// </summary>
		public static StonePiece[] DetailVariant(StonePiece[] pieces, int variant)
		{
			if (variant <= 0 || pieces == null || pieces.Length == 0)
			{
				return pieces;
			}
			var turned = new StonePiece[pieces.Length];
			for (int k = 0; k < pieces.Length; k++)
			{
				StonePiece piece = pieces[(k + variant) % pieces.Length];
				piece.Shape = piece.Shape.Named($"{piece.Shape.Name}/v{variant}");
				turned[k] = piece;
			}
			return turned;
		}

		/// <summary>A named set: a habit's boulders (their roles kept) at its own proportions and cut, and the draft's own small rocks and pebbles.</summary>
		private static StoneSet Own(string material, string summary, StoneHabit habit, float round, float slab, float spire, int k, StonePiece[] rocks, StonePiece[] pebbles)
		{
			StoneSet h = Habit(material, summary, habit, round, slab, spire, 0.5f, 0.5f, k);
			return Named(material, summary, h.Round, h.Slab, h.Spire, rocks, pebbles);
		}

		/// <summary>Every named set, in <see cref="NamedStones"/>' order.</summary>
		private static IEnumerable<StoneSet> NamedSets()
		{
			// Coastal Water: free-living coralline algae, branched pink nodules; heaps of it as the boulders.
			yield return Own("Maerl", "Maerl: free-living coralline algae — branched pink nodules 2–6 cm, the dead ones bleached white.",
				StoneHabit.Knobbly, 0.7f, 0.3f, 1.5f, 2,
				new[] { P(S(0.3f, 0.16f, 0.26f).Knobbly(9, 0.4f, 0.3f).Noise(0.2f, 0.05f), RockResolution), P(S(0.12f, 0.07f, 0.11f).Cored(0.5f).Branched(3, 0.9f, 0.25f, 0.3f)) },
				new[] { P(S(0.05f, 0.035f, 0.045f).Cored(0.45f).Branched(4, 1f, 0.3f, 0.35f), 3), P(S(0.04f, 0.03f, 0.04f).Knobbly(5, 0.6f, 0.3f)), P(S(0.06f, 0.02f, 0.022f).Knobbly(3, 0.6f, 0.25f)) });

			// Coral Reef: broken branching coral, bleached: rods, forks and knobs 3–15 cm.
			yield return Own("CoralRubble", "Coral rubble: rods, forks and knobs of broken branching coral 3–15 cm, bleached.",
				StoneHabit.Pitted, 0.75f, 0.35f, 1.7f, 1,
				new[] { P(S(0.3f, 0.12f, 0.26f).Cored(0.4f).Branched(3, 1f, 0.22f, 0.15f).Noise(0.15f, 0.05f), RockResolution), P(S(0.16f, 0.05f, 0.05f).Knobbly(4, 0.5f, 0.12f)) },
				new[] { P(S(0.15f, 0.04f, 0.04f).Knobbly(4, 0.6f, 0.14f), 3), P(S(0.1f, 0.04f, 0.09f).Cored(0.45f).Branched(2, 1f, 0.28f, 0.1f)), P(S(0.06f, 0.05f, 0.06f).Knobbly(5, 0.7f, 0.2f)) });

			// River: the Rhine's gravel, well rounded and of every colour (the conglomerate's cobbles' surface).
			yield return Own("Rhine", "Rhine gravel: well-rounded multicoloured pebbles of quartz, basalt, red sandstone and greywacke, now and then a banded agate.",
				StoneHabit.Massive, 0.8f, 0.36f, 1.6f, 0,
				new[] { P(S(0.34f, 0.2f, 0.28f).Noise(0.08f, 0f), RockResolution), P(S(0.14f, 0.09f, 0.12f).Noise(0.06f, 0f)) },
				new[] { P(S(0.1f, 0.08f, 0.09f).Noise(0.05f, 0f)), P(S(0.11f, 0.045f, 0.09f).Noise(0.05f, 0f)), P(S(0.12f, 0.05f, 0.06f).Noise(0.05f, 0f)), P(S(0.08f, 0.06f, 0.07f).Bedded(2f, 0.08f).Noise(0.04f, 0f)) });

			// River: a riffle's flat cobbles, the ones the current shingles upstream in rows.
			yield return Own("Imbricated", "Imbricated riffle cobbles: flat greywacke discs 0.1–0.3 m that the current shingles upstream in rows.",
				StoneHabit.Bedded, 0.62f, 0.26f, 1.6f, 1,
				new[] { P(S(0.3f, 0.08f, 0.24f).Boxy(2.2f, 2f).Noise(0.06f, 0f), RockResolution), P(S(0.18f, 0.05f, 0.14f).Boxy(2.2f, 2f).Noise(0.05f, 0f)) },
				new[] { P(S(0.12f, 0.035f, 0.1f).Boxy(2.2f, 2f).Noise(0.05f, 0f), 3), P(S(0.1f, 0.03f, 0.075f).Noise(0.05f, 0f)), P(S(0.13f, 0.04f, 0.08f).Boxy(2.4f, 2f).Noise(0.05f, 0f)) });

			// Jungle Ruins: a fallen building's masonry — squared veneer blocks, column drums, stucco lumps.
			yield return Own("Rubble", "Collapsed masonry: squared limestone veneer blocks, column drums and lumps of stucco.",
				StoneHabit.Blocky, 0.78f, 0.34f, 2.0f, 2,
				new[] { P(S(0.36f, 0.22f, 0.3f).Boxy(6f, 6f).Cut(2, 0.2f, 0.02f).Noise(0.05f, 0.05f), RockResolution), P(S(0.2f, 0.14f, 0.2f).Prism(12, 0f).Soft(0.02f).Noise(0.05f, 0.05f)) },
				new[] { P(S(0.12f, 0.07f, 0.1f).Boxy(6f, 6f).Cut(2, 0.25f, 0.02f), 3), P(S(0.1f, 0.06f, 0.09f).Boxy(2.4f, 2.2f).Soft(0.2f)), P(S(0.08f, 0.07f, 0.08f).Prism(10, 0f).Soft(0.02f)) });

			// Jungle Ruins and Cave: chert nodules out of the limestone, waxy, white-rinded, and their flakes.
			yield return Own("Chert", "Chert: waxy honey-grey nodules with a white cortex, river-rounded ones, and conchoidal flakes.",
				StoneHabit.Glassy, 0.72f, 0.3f, 1.8f, 1,
				new[] { P(S(0.3f, 0.2f, 0.24f).Knobbly(5, 0.5f, 0.25f).Conchoidal(2, 0.35f, 0.8f), RockResolution), P(S(0.14f, 0.035f, 0.1f).Conchoidal(2, 0.35f, 0.9f)) },
				new[] { P(S(0.1f, 0.07f, 0.08f).Knobbly(4, 0.6f, 0.28f), 3), P(S(0.09f, 0.022f, 0.07f).Conchoidal(2, 0.35f, 0.9f)), P(S(0.09f, 0.06f, 0.075f).Noise(0.1f, 0.02f)) });

			// Jungle Temple: dressed sandstone fallen from the galleries — squared, mitred, with lifting-peg holes.
			yield return Own("BlockFall", "Fallen dressed blocks: squared sandstone 0.6–1.6 m with mitred edges and the holes of the lifting pegs.",
				StoneHabit.Blocky, 0.66f, 0.3f, 2.1f, 0,
				new[] { P(S(0.36f, 0.22f, 0.3f).Boxy(7f, 7f).Cut(3, 0.2f, 0.02f).Pitted(1, 0.4f, 0.15f), RockResolution), P(S(0.15f, 0.1f, 0.12f).Boxy(5f, 5f).Cut(3, 0.4f, 0.02f)) },
				new[] { P(S(0.12f, 0.07f, 0.1f).Boxy(6f, 6f).Cut(3, 0.4f, 0.02f), 3), P(S(0.1f, 0.05f, 0.08f).Boxy(5f, 5f).Cut(2, 0.45f, 0.02f)), P(S(0.08f, 0.06f, 0.07f).Boxy(4f, 4f).Cut(4, 0.4f, 0.02f)) });

			// Lake: limestone cobbles on the shelf, etched in meandering furrows by the blue-green algae crusting them.
			yield return Own("Furrow", "Furrow stones: sub-round limestone cobbles etched with meandering grooves by the algae that crust them.",
				StoneHabit.Soft, 0.76f, 0.34f, 1.7f, 0,
				new[] { P(S(0.34f, 0.2f, 0.28f).Boxy(2.4f, 2.2f).Bedded(2.5f, 0.14f, 40f).Meandering(1.4f).Noise(0.2f, 0.05f), RockResolution), P(S(0.15f, 0.09f, 0.12f).Bedded(1.5f, 0.12f, 30f).Meandering(1.2f)) },
				new[] { P(S(0.12f, 0.08f, 0.1f).Bedded(2f, 0.14f, 35f).Meandering(1.3f), 3), P(S(0.1f, 0.07f, 0.085f).Noise(0.15f, 0.02f)), P(S(0.09f, 0.06f, 0.075f).Bedded(1.5f, 0.12f, 20f).Meandering(1f)) });

			// Lake: the strand's skimming stones, wave-polished flat discs of the erratics.
			yield return Own("Strand", "Strand stones: flat wave-polished discs of grey, pink and green erratics — skimming stones.",
				StoneHabit.Massive, 0.6f, 0.25f, 1.5f, 1,
				new[] { P(S(0.34f, 0.1f, 0.28f).Noise(0.04f, 0f), RockResolution), P(S(0.16f, 0.05f, 0.13f).Noise(0.04f, 0f)) },
				new[] { P(S(0.1f, 0.025f, 0.085f).Noise(0.03f, 0f)), P(S(0.09f, 0.03f, 0.07f).Noise(0.03f, 0f)), P(S(0.11f, 0.03f, 0.08f).Noise(0.03f, 0f)), P(S(0.075f, 0.022f, 0.065f).Noise(0.03f, 0f)) });

			// Tholin Plain: the haze's cemented organics as knotted rods, fused doublets and dimpled rings, never spheres.
			yield return Own("HazeKnot", "Haze knots: organic concretions as knotted rods, fused doublets and dimpled rings 2–10 cm, matte brown-black.",
				StoneHabit.Knobbly, 0.66f, 0.3f, 1.6f, 0,
				new[] { P(S(0.36f, 0.1f, 0.12f).Knobbly(6, 0.5f, 0.25f), RockResolution), P(S(0.16f, 0.08f, 0.09f).Knobbly(2, 1.2f, 0.4f)) },
				new[] { P(S(0.1f, 0.025f, 0.03f).Knobbly(4, 0.6f, 0.3f), 3), P(S(0.08f, 0.03f, 0.08f).Pitted(2, 0.5f, 0.6f)), P(S(0.07f, 0.035f, 0.04f).Knobbly(2, 1.2f, 0.4f)) });

			// Methane Lake and Tholin Plain: Huygens' cobbles — water ice rounded and flattened by flowing methane.
			yield return Own("IceCobble", "Ice cobbles: rounded, flattened water-ice cobbles 3–25 cm, rolled by methane rivers.",
				StoneHabit.Icy, 0.7f, 0.3f, 1.5f, 0,
				new[] { P(S(0.3f, 0.14f, 0.24f).Noise(0.12f, 0.01f), RockResolution), P(S(0.14f, 0.07f, 0.11f).Noise(0.1f, 0f)) },
				new[] { P(S(0.12f, 0.055f, 0.1f).Noise(0.1f, 0.01f), 3), P(S(0.08f, 0.04f, 0.065f).Noise(0.1f, 0f)), P(S(0.05f, 0.03f, 0.045f).Noise(0.08f, 0f)) });

			// Valley (and Glacier's striated boulders): stones the ice dragged — flat-iron shapes, faceted, scored with striae.
			yield return Own("Striated", "Striated glacial stones: sub-rounded flat-iron cobbles, faceted by the ice and scored with parallel striae.",
				StoneHabit.Massive, 0.7f, 0.32f, 1.6f, 2,
				new[] { P(S(0.36f, 0.18f, 0.24f).Boxy(2.4f, 2.6f).Cut(4, 0.35f, 0.05f).Bedded(4f, 0.05f, 5f).Noise(0.15f, 0.05f), RockResolution), P(S(0.15f, 0.08f, 0.11f).Cut(3, 0.35f, 0.05f).Bedded(3f, 0.06f, 5f)) },
				new[] { P(S(0.12f, 0.06f, 0.08f).Cut(3, 0.35f, 0.04f).Bedded(3f, 0.06f, 5f), 3), P(S(0.1f, 0.05f, 0.075f).Cut(3, 0.4f, 0.04f)), P(S(0.09f, 0.05f, 0.06f).Boxy(2.4f, 2.4f).Bedded(2f, 0.06f, 5f)) });

			// Volcanic Cave: tezontle — oxblood frothy scoria quarried in chunks, glass-rinded, light enough to float.
			yield return Own("Tezontle", "Tezontle: oxblood-red frothy scoria in chunks 3–25 cm, with black glassy rinds, so light it nearly floats.",
				StoneHabit.Blocky, 0.75f, 0.35f, 1.9f, 1,
				new[] { P(S(0.3f, 0.2f, 0.26f).Cut(3, 0.4f, 0.04f).Pitted(6, 0.35f, 0.22f).Noise(0.4f, 0.3f), RockResolution), P(S(0.14f, 0.09f, 0.12f).Pitted(3, 0.5f, 0.2f)) },
				new[] { P(S(0.1f, 0.07f, 0.09f).Pitted(4, 0.5f, 0.22f), 3), P(S(0.08f, 0.05f, 0.07f).Cut(3, 0.4f, 0.04f).Pitted(2, 0.6f, 0.2f)), P(S(0.06f, 0.04f, 0.05f).Pitted(2, 0.7f, 0.2f)) });

			// Tholin Plain: the dune sea's own stone, cemented organic sand in thin cross-laminated flags (its own rock's material).
			yield return Own("Duneslab", "Duneslab: cemented organic dune-rock in thin flaggy slabs 5–60 cm, margins rounded, edges cross-laminated.",
				StoneHabit.Bedded, 0.5f, 0.18f, 1.5f, 2,
				new[] { P(S(0.4f, 0.07f, 0.3f).Boxy(3f, 4f).Bedded(3f, 0.05f, 25f).Soft(0.03f), RockResolution), P(S(0.18f, 0.04f, 0.14f).Boxy(3f, 4f).Bedded(2f, 0.05f, 25f)) },
				new[] { P(S(0.12f, 0.02f, 0.09f).Boxy(3f, 4f).Soft(0.02f), 3), P(S(0.1f, 0.018f, 0.08f).Boxy(3f, 4f).Cut(2, 0.4f, 0.02f)), P(S(0.08f, 0.02f, 0.06f).Boxy(3f, 3f).Soft(0.02f)) });
		}
	}
}
#endif
