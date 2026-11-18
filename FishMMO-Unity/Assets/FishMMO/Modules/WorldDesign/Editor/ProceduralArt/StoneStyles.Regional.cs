#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The stones of each biome's own crust rock (<see cref="RegionalRocks.Crust"/>, 2026-10-25): boulders, small rocks and
	/// pebbles of the rock's own habit, so a biome that lays its rock lays its own stones (decision 6 of the biome suite).
	/// </summary>
	public static partial class StoneStyles
	{
		/// <summary>How a rock breaks into stones: the marks its boulders, rocks and pebbles carry.</summary>
		private enum StoneHabit
		{
			/// <summary>Jointed and angular: many fracture planes, crisp arrises (andesite, dolomite, porphyry).</summary>
			Blocky,
			/// <summary>Parted along beds or cleavage into plates and flakes (flagstone, red beds, platy lavas).</summary>
			Platy,
			/// <summary>Bedded slabs with worn edges (beachrock, molasse).</summary>
			Bedded,
			/// <summary>Massive and rounded: few joints, weathered corners (granodiorite, charnockite, eclogite).</summary>
			Massive,
			/// <summary>Dissolved or vuggy: pits, pans and flutes (travertine, coral rag, laterite's pisolites).</summary>
			Pitted,
			/// <summary>Glassy or flinty: conchoidal scars and razor edges (hornfels, hyaloclastite, porcelanite).</summary>
			Glassy,
			/// <summary>Clasts or spherules standing out as knobs (suevite, variolite).</summary>
			Knobbly,
			/// <summary>Columnar jointing: prism drums and chips (dolerite, trachyte).</summary>
			Columnar,
			/// <summary>Ice: melt-rounded lumps, floe plates, shards.</summary>
			Icy,
			/// <summary>Soft and crumbly: every edge rounded off (marl, loess, nenfro).</summary>
			Soft,
		}

		/// <summary>
		/// A set of a habit at its own proportions: <paramref name="round"/>, <paramref name="slab"/>, <paramref name="spire"/>,
		/// <paramref name="rock"/> and <paramref name="pebble"/> are the heights over the geometric mean of length and breadth
		/// (the measure's flatness) the Round, Slab and Spire boulders, the small rock and the pebbles are built at, and
		/// <paramref name="k"/> (0–2) picks how heavily the habit's marks are cut, so two rocks of one habit still differ.
		/// </summary>
		private static StoneSet Habit(string material, string summary, StoneHabit habit, float round, float slab, float spire, float rock, float pebble, int k)
		{
			// The slots' footprints are the old nominal ones; only the heights follow the rock.
			StoneShape r = S(1.6f, round * 1.523f, 1.45f);
			StoneShape l = S(2.6f, slab * 2.28f, 2f);
			StoneShape t = S(1.05f, spire * 1.014f, 0.98f);
			StoneShape a = S(0.36f, rock * 0.329f, 0.3f);
			StoneShape b = S(0.15f, rock * 0.42f * 0.329f, 0.12f);
			StoneShape p1 = S(0.12f, pebble * 0.1095f, 0.1f);
			StoneShape p2 = S(0.1f, pebble * 0.85f * 0.0922f, 0.085f);
			StoneShape p3 = S(0.09f, pebble * 0.75f * 0.0822f, 0.075f);
			float m = 1f + 0.25f * k;
			switch (habit)
			{
				case StoneHabit.Blocky:
					r = r.Boxy(2.8f + 0.2f * k, 2.6f).Cut(5 + 2 * k, 0.4f, 0.05f, -0.15f, 0.6f).Noise(0.25f, 0.2f);
					l = l.Boxy(4f, 5f).Cut(4 + k, 0.4f, 0.05f, -0.1f, 0.3f).Noise(0.2f, 0.2f);
					t = t.Boxy(3f, 3f).Cut(6 + k, 0.5f, 0.04f, -0.15f, 0.8f).Noise(0.25f, 0.2f);
					a = a.Boxy(3f, 3f).Cut(4 + k, 0.45f, 0.05f);
					b = b.Cut(4 + k, 0.5f, 0.04f);
					p1 = p1.Boxy(3f, 3f).Cut(3 + k, 0.4f, 0.05f);
					p2 = p2.Cut(3 + k, 0.45f, 0.04f);
					p3 = p3.Boxy(2.6f, 2.6f).Cut(4, 0.45f, 0.04f);
					break;
				case StoneHabit.Platy:
					r = r.Boxy(4f, 5f).Bedded(2f + k, 0.06f, 10f * k).Cut(5, 0.4f, 0.03f, -0.1f, 0.25f).Noise(0.15f, 0.2f);
					l = l.Boxy(5f, 6f).Bedded(2f, 0.06f).Cut(5, 0.45f, 0.02f, -0.1f, 0.2f).Noise(0.12f, 0.15f);
					t = t.Boxy(5f, 5f).Bedded(3f, 0.07f, 70f).Cut(4 + k, 0.35f, 0.03f, -0.2f, 0.4f).Noise(0.15f, 0.2f);
					a = a.Boxy(5f, 7f).Cut(5, 0.5f, 0.015f, -0.1f, 0.2f).Noise(0.1f, 0.2f);
					b = b.Boxy(5f, 7f).Cut(4, 0.5f, 0.015f, -0.1f, 0.2f);
					p1 = p1.Boxy(4f, 7f).Cut(4 + k, 0.5f, 0.015f, -0.1f, 0.2f);
					p2 = p2.Boxy(4f, 7f).Cut(4, 0.5f, 0.015f, -0.1f, 0.2f);
					p3 = p3.Boxy(4f, 7f).Cut(3, 0.5f, 0.015f, -0.1f, 0.2f);
					break;
				case StoneHabit.Bedded:
					r = r.Boxy(3.2f, 3f).Bedded(2.5f + 0.5f * k, 0.06f, 8f * k).Cut(3, 0.3f, 0.15f, -0.1f, 0.3f).Noise(0.3f, 0.08f);
					l = l.Boxy(4f, 5f).Bedded(2f, 0.05f, 15f).Cut(4, 0.35f, 0.16f, -0.1f, 0.2f).Noise(0.25f, 0.05f);
					t = t.Boxy(3f, 2.6f).Bedded(3f, 0.08f).Tapered(0.2f + 0.05f * k).Noise(0.3f, 0.1f);
					a = a.Boxy(3.5f, 4f).Bedded(1f, 0.04f, 12f).Soft(0.12f);
					b = b.Boxy(3.5f, 4f).Soft(0.1f);
					p1 = p1.Boxy(2.6f, 3f);
					p2 = p2.Boxy(3f, 3f).Cut(2, 0.3f, 0.1f, -0.1f, 0.2f);
					p3 = p3.Boxy(2.4f, 2.5f);
					break;
				case StoneHabit.Massive:
					r = r.Boxy(2.6f + 0.2f * k, 2.4f).Cut(3 + k, 0.25f, 0.12f, -0.1f, 0.4f).Noise(0.4f, 0.12f);
					l = l.Boxy(3f, 3.5f).Bedded(2f, 0.04f).Cut(3, 0.3f, 0.12f, -0.1f, 0.3f).Noise(0.35f, 0.1f);
					t = t.Boxy(3.2f, 3f).Cut(4 + k, 0.35f, 0.1f, -0.15f, 0.8f).Noise(0.35f, 0.12f);
					a = a.Boxy(2.6f, 2.4f).Cut(3 + k, 0.35f, 0.07f).Noise(0.45f, 0.15f);
					b = b.Boxy(2.4f, 2.2f).Cut(3, 0.4f, 0.05f);
					p1 = p1.Boxy(2.3f, 2.2f).Cut(3, 0.3f, 0.06f).Noise(0.4f, 0.12f);
					p2 = p2.Boxy(2.2f, 2f);
					p3 = p3.Cut(3 + k, 0.4f, 0.05f);
					break;
				case StoneHabit.Pitted:
					r = r.Boxy(2.8f, 2.4f).Pitted(5 + 3 * k, 0.32f, 0.11f * m).Fluted(4 + 2 * k, 0.1f).Noise(0.3f, 0.1f);
					l = l.Boxy(4f, 4f).Pitted(4 + 2 * k, 0.4f, 0.1f).Cut(3, 0.3f, 0.06f, -0.15f, 0.15f).Noise(0.2f, 0.08f);
					t = t.Boxy(2.4f, 2.2f).Pitted(4 + k, 0.3f, 0.1f).Fluted(6 + k, 0.14f).Tapered(0.3f).Noise(0.3f, 0.1f);
					a = a.Boxy(2.8f, 2.6f).Pitted(3 + k, 0.5f, 0.15f).Cut(2 + k, 0.35f, 0.08f);
					b = b.Boxy(2.6f, 2.4f).Pitted(2, 0.6f, 0.15f);
					p1 = p1.Boxy(2.6f, 2.4f).Pitted(2 + k, 0.6f, 0.18f);
					p2 = p2.Boxy(2.8f, 2.6f).Cut(2, 0.3f, 0.08f);
					p3 = p3.Pitted(2, 0.7f, 0.18f);
					break;
				case StoneHabit.Glassy:
					r = r.Boxy(2.2f, 2.2f).Conchoidal(4 + k, 0.35f, 0.8f).Cut(4 + k, 0.5f, 0.015f).Noise(0.1f, 0.05f);
					l = l.Boxy(3f, 3f).Conchoidal(4, 0.35f, 0.75f).Cut(3, 0.45f, 0.015f, -0.1f, 0.2f).Noise(0.08f, 0.05f);
					t = t.Boxy(3f, 2.6f).Conchoidal(4, 0.35f, 0.6f).Cut(5 + k, 0.6f, 0.012f, -0.3f, 0.8f).Tapered(0.15f, 0.15f).Noise(0.05f, 0.05f);
					a = a.Conchoidal(3 + k, 0.4f, 0.8f).Cut(2, 0.5f, 0.015f);
					b = b.Conchoidal(2, 0.4f, 0.8f).Tapered(0.25f);
					p1 = p1.Conchoidal(3, 0.4f, 0.8f).Cut(2, 0.5f, 0.015f);
					p2 = p2.Conchoidal(2, 0.35f, 0.9f);
					p3 = p3.Cut(3 + k, 0.5f, 0.015f);
					break;
				case StoneHabit.Knobbly:
					r = r.Boxy(2.6f, 2.4f).Knobbly(12 + 4 * k, 0.2f, 0.12f).Cut(2 + k, 0.3f, 0.06f).Noise(0.3f, 0.12f);
					l = l.Boxy(3.5f, 4f).Knobbly(14, 0.2f, 0.12f).Bedded(1.5f, 0.04f).Noise(0.25f, 0.1f);
					t = t.Boxy(2.8f, 2.6f).Knobbly(14, 0.2f, 0.12f).Tapered(0.2f).Noise(0.3f, 0.12f);
					a = a.Knobbly(4 + k, 0.5f, 0.18f).Cut(2 + k, 0.4f, 0.04f);
					b = b.Cut(4, 0.5f, 0.03f);
					p1 = p1.Knobbly(3 + k, 0.6f, 0.2f);
					p2 = p2.Noise(0.15f, 0.05f);
					p3 = p3.Cut(4, 0.5f, 0.03f);
					break;
				case StoneHabit.Columnar:
					r = r.Boxy(2.4f, 2.4f).Cut(7 + k, 0.5f, 0.04f).Conchoidal(1 + k, 0.3f, 0.9f).Noise(0.2f, 0.2f);
					l = l.Prism(5 + k, 0.25f).Soft(0.05f).Conchoidal(1, 0.15f, 1.6f).Noise(0.15f, 0.2f);
					t = t.Prism(6, 0.22f).Cut(1, 0.4f, 0.04f, 0.5f, 0.9f).Conchoidal(1, 0.25f, 0.7f).Noise(0.12f, 0.2f);
					a = a.Prism(5 + k, 0.3f).Cut(2, 0.5f, 0.03f).Noise(0.15f, 0.2f);
					b = b.Cut(6, 0.6f, 0.02f);
					p1 = p1.Cut(6 + k, 0.6f, 0.02f);
					p2 = p2.Prism(5, 0.3f).Soft(0.02f);
					p3 = p3.Cut(5, 0.55f, 0.02f);
					break;
				case StoneHabit.Icy:
					r = r.Boxy(2.1f, 2f).Conchoidal(3 + k, 0.17f, 1f).Soft(0.1f).Noise(0.25f, 0.03f);
					l = l.Boxy(5f, 6f).Cut(4 + k, 0.35f, 0.03f, -0.1f, 0.15f).Noise(0.1f, 0.05f);
					t = t.Boxy(3f, 2.6f).Cut(6 + k, 0.6f, 0.01f, -0.3f, 0.8f).Tapered(0.15f, 0.2f).Noise(0.05f, 0.05f);
					a = a.Cut(5 + k, 0.6f, 0.02f).Noise(0.05f, 0.05f);
					b = b.Soft(0.1f).Noise(0.2f, 0f);
					p1 = p1.Noise(0.2f, 0.02f);
					p2 = p2.Cut(4 + k, 0.55f, 0.02f);
					p3 = p3.Noise(0.15f, 0f);
					break;
				default: // Soft
					r = r.Boxy(2.8f + 0.2f * k, 2.6f).Cut(2 + k, 0.3f, 0.2f, -0.1f, 0.5f).Soft(0.15f).Noise(0.3f, 0.05f);
					l = l.Boxy(3.5f, 3.5f).Cut(3, 0.3f, 0.2f, -0.1f, 0.3f).Soft(0.15f).Noise(0.25f, 0.05f);
					t = t.Boxy(2.6f, 2.4f).Tapered(0.3f + 0.1f * k).Soft(0.15f).Noise(0.3f, 0.06f);
					a = a.Boxy(2.6f, 2.4f).Soft(0.15f).Noise(0.3f, 0.05f);
					b = b.Boxy(2.4f, 2.2f).Soft(0.12f);
					p1 = p1.Boxy(2.4f, 2.2f).Soft(0.15f);
					p2 = p2.Boxy(2.4f, 2.2f).Soft(0.12f);
					p3 = p3.Boxy(2.2f, 2f).Cut(2, 0.3f, 0.12f);
					break;
			}
			return Named(material, summary, r, l, t,
				new[] { P(a, RockResolution), P(b) },
				new[] { P(p1, 3), P(p2), P(p3) });
		}

		/// <summary>Every biome crust rock's stones, in <see cref="RegionalRocks.Crust"/>'s order.</summary>
		private static IEnumerable<StoneSet> RegionalSets()
		{
			yield return Habit("Rhyolite", "Flow-banded lava: blocks and slabs split along the bands, platy chips.", StoneHabit.Bedded, 0.7f, 0.32f, 1.7f, 0.42f, 0.45f, 2);
			yield return Habit("Trachyte", "Platy-jointed columns: stout prism drums and thin plates.", StoneHabit.Columnar, 0.66f, 0.32f, 1.9f, 0.5f, 0.52f, 1);
			yield return Habit("Dacite", "Dome lava: angular platy blocks with glassy rinds.", StoneHabit.Blocky, 0.75f, 0.36f, 2.2f, 0.6f, 0.6f, 1);
			yield return Habit("Komatiite", "Spinifex lava: platy angular chips with a glass rind.", StoneHabit.Blocky, 0.56f, 0.24f, 1.6f, 0.34f, 0.38f, 2);
			yield return Habit("Dolomite", "Massive jointed blocks, barely pitted: big pale angular scree.", StoneHabit.Blocky, 0.86f, 0.4f, 2.45f, 0.78f, 0.74f, 0);
			yield return Habit("Flysch", "Thin sandstone ribs and crumbly shale: platy slabs and flakes.", StoneHabit.Platy, 0.6f, 0.24f, 2.1f, 0.36f, 0.38f, 1);
			yield return Habit("Beachrock", "Cemented beach sand: flat bench slabs with worn edges.", StoneHabit.Bedded, 0.58f, 0.26f, 1.75f, 0.38f, 0.36f, 0);
			yield return Habit("Dolerite", "Blocky-columnar intrusion: rounded joint blocks and prism chips.", StoneHabit.Columnar, 0.8f, 0.4f, 2.3f, 0.66f, 0.62f, 0);
			yield return Habit("Coralstone", "Reef limestone: porous lumps pocked by solution and coral moulds.", StoneHabit.Pitted, 0.82f, 0.4f, 1.8f, 0.8f, 0.8f, 0);
			yield return Habit("Suevite", "Impact-melt breccia: angular blocks with glass and clasts standing out.", StoneHabit.Knobbly, 0.68f, 0.4f, 2.0f, 0.72f, 0.58f, 1);
			yield return Habit("Cryolava", "Frozen ammonia-water lava: rounded chill-rind lumps and flat crust plates.", StoneHabit.Icy, 0.6f, 0.24f, 1.9f, 0.45f, 0.45f, 2);
			yield return Habit("Marl", "Crumbly calcareous mudstone: soft rounded lumps.", StoneHabit.Soft, 0.7f, 0.32f, 1.6f, 0.6f, 0.55f, 0);
			yield return Habit("Duststone", "Lithified dust: soft flat clods and curled crust flakes.", StoneHabit.Soft, 0.48f, 0.2f, 1.7f, 0.36f, 0.3f, 1);
			yield return Habit("Till", "Boulder clay's stones: subrounded, faceted by the ice.", StoneHabit.Soft, 0.84f, 0.42f, 2.0f, 0.82f, 0.72f, 2);
			yield return Habit("Travertine", "Spring limestone: vuggy banded lumps and draped slabs.", StoneHabit.Pitted, 0.64f, 0.3f, 1.6f, 0.5f, 0.55f, 2);
			yield return Habit("Greenstone", "Glacier-polished metabasalt: blocky, stepped where plucked.", StoneHabit.Blocky, 0.68f, 0.33f, 1.5f, 0.62f, 0.52f, 2);
			yield return Habit("Loess", "Silt that barely holds together: soft lumps and its loess-doll concretions.", StoneHabit.Soft, 0.62f, 0.36f, 2.4f, 0.7f, 0.82f, 1);
			yield return Habit("Charnockite", "Massive greasy granite: rounded corestones under a honey rind.", StoneHabit.Massive, 0.8f, 0.38f, 2.1f, 0.7f, 0.65f, 0);
			yield return Habit("Troctolite", "Spotted lower crust: angular shock-jointed blocks.", StoneHabit.Blocky, 0.72f, 0.42f, 1.95f, 0.84f, 0.84f, 1);
			yield return Habit("Laterite", "Ironstone duricrust: pitted vermiform slabs and round pisolites.", StoneHabit.Pitted, 0.58f, 0.26f, 2.0f, 0.44f, 0.76f, 0);
			yield return Habit("Molasse", "Soft glauconitic sandstone: rounded bedded blocks.", StoneHabit.Bedded, 0.74f, 0.38f, 2.0f, 0.56f, 0.58f, 1);
			yield return Habit("CoralRag", "Karstified fossil reef: jagged pitted lumps with razor edges.", StoneHabit.Pitted, 0.9f, 0.42f, 2.45f, 0.86f, 0.62f, 1);
			yield return Habit("Cryokarst", "Organic-cemented ice dissolved by methane: pitted fluted blocks.", StoneHabit.Pitted, 0.74f, 0.36f, 2.25f, 0.66f, 0.66f, 1);
			yield return Habit("Phonolite", "Clinkstone: thin ringing plates and slabs.", StoneHabit.Platy, 0.5f, 0.18f, 1.8f, 0.3f, 0.3f, 0);
			yield return Habit("RaftIce", "Rigid water-ice bedrock: big angular jointed blocks.", StoneHabit.Blocky, 0.96f, 0.42f, 2.0f, 0.9f, 0.92f, 1);
			yield return Habit("Mudstone", "Non-fissile mud rock: conchoidal blocky lumps.", StoneHabit.Blocky, 0.8f, 0.3f, 1.5f, 0.66f, 0.66f, 0);
			yield return Habit("Redbed", "Thin red beds: crumbly flakes and platy chips.", StoneHabit.Platy, 0.66f, 0.22f, 1.6f, 0.26f, 0.24f, 2);
			yield return Habit("SableIce", "Dark dirty ice: sublimation-softened lumps.", StoneHabit.Icy, 0.82f, 0.38f, 1.5f, 0.76f, 0.76f, 1);
			yield return Habit("Norite", "Salt-and-pepper highland rock: rounded jointed blocks.", StoneHabit.Massive, 0.66f, 0.3f, 1.8f, 0.55f, 0.6f, 1);
			yield return Habit("Variolite", "Spherulitic basalt: rolled boulders studded with raised pale knobs.", StoneHabit.Knobbly, 0.76f, 0.34f, 1.6f, 0.62f, 0.72f, 0);
			yield return Habit("Greywacke", "Cleaved dense sandstone: wedge-shaped angular blocks.", StoneHabit.Blocky, 0.55f, 0.22f, 2.3f, 0.5f, 0.42f, 1);
			yield return Habit("Flagrock", "Flagstone: flat flags with straight sharp edges.", StoneHabit.Platy, 0.45f, 0.14f, 2.2f, 0.2f, 0.18f, 0);
			yield return Habit("Hornfels", "Flinty contact rock: conchoidal splinters and needles.", StoneHabit.Glassy, 0.6f, 0.28f, 2.5f, 0.4f, 0.34f, 0);
			yield return Habit("Platebasalt", "Sheet-jointed basalt: thin plates.", StoneHabit.Platy, 0.42f, 0.16f, 1.45f, 0.22f, 0.24f, 1);
			yield return Habit("Saltrock", "Bedded rock salt: angular clay-banded fragments.", StoneHabit.Blocky, 0.62f, 0.36f, 2.5f, 0.72f, 0.56f, 1);
			yield return Habit("Granodiorite", "Massive plutonic rock: joint blocks rounded at the corners.", StoneHabit.Massive, 0.88f, 0.36f, 2.4f, 0.8f, 0.76f, 2);
			yield return Habit("Caliche", "Calcrete hardpan: flat laminar plates and cream nodules.", StoneHabit.Soft, 0.54f, 0.18f, 2.1f, 0.3f, 0.62f, 2);
			yield return Habit("Hyaloclastite", "Quenched glassy breccia: sharp-edged lumps.", StoneHabit.Glassy, 0.78f, 0.38f, 1.7f, 0.7f, 0.7f, 1);
			yield return Habit("Eclogite", "Dense garnet rock: massive rounded blocks.", StoneHabit.Massive, 0.74f, 0.42f, 1.55f, 0.84f, 0.86f, 1);
			yield return Habit("Porphyry", "Blocky felsic lava: rounded joint blocks.", StoneHabit.Blocky, 0.92f, 0.38f, 1.8f, 0.86f, 0.86f, 2);
			yield return Habit("MarineIce", "Refrozen ocean ice: scalloped melt-rounded lumps.", StoneHabit.Icy, 0.72f, 0.3f, 1.7f, 0.6f, 0.6f, 0);
			yield return Habit("Alunite", "Acid-rotted lava: crumbly porous lumps.", StoneHabit.Soft, 0.74f, 0.28f, 2.3f, 0.5f, 0.42f, 2);
			yield return Habit("Tessera", "Deformed highland crust: sharp rhomb blocks.", StoneHabit.Blocky, 0.5f, 0.28f, 1.75f, 0.44f, 0.54f, 0);
			yield return Habit("Nenfro", "Grey tuff: soft rounded bosses and squared lumps.", StoneHabit.Soft, 0.92f, 0.4f, 2.2f, 0.9f, 0.9f, 1);
			yield return Habit("Hazestone", "Varnished ancient ice: massive wind-rounded blocks.", StoneHabit.Massive, 0.6f, 0.26f, 1.75f, 0.5f, 0.5f, 2);
			yield return Habit("BrineIce", "Salt-layered shell ice: platy rafts and plates.", StoneHabit.Platy, 0.55f, 0.2f, 1.4f, 0.32f, 0.34f, 2);
			yield return Habit("Porcelanite", "Porcelain silica: thin conchoidal plates.", StoneHabit.Glassy, 0.5f, 0.2f, 1.9f, 0.3f, 0.28f, 1);
			yield return Habit("Serpentinite", "Scaly slickensided rock: curved angular lumps.", StoneHabit.Blocky, 0.64f, 0.38f, 1.7f, 0.5f, 0.66f, 2);
			yield return Habit("Peridotite", "Dense mantle rock: blocky with a scaly fracture.", StoneHabit.Blocky, 0.62f, 0.3f, 1.8f, 0.74f, 0.58f, 1);
			yield return Habit("Radiolarite", "Ribbon chert: conchoidal splintered blocks.", StoneHabit.Glassy, 0.62f, 0.24f, 2.05f, 0.38f, 0.32f, 2);
			yield return Habit("Carstone", "Iron-pan greensand: boxwork-pitted lumps and round doggers.", StoneHabit.Pitted, 0.7f, 0.4f, 1.8f, 0.6f, 0.86f, 0);
			yield return Habit("SulcusIce", "Coarse crystalline ice: angular glassy shards.", StoneHabit.Glassy, 0.66f, 0.34f, 2.3f, 0.55f, 0.55f, 2);
			// Then the named sets the drafts ask for beyond each rock's own (2026-10-26, StoneStyles.Named.cs).
			foreach (StoneSet set in NamedSets())
			{
				yield return set;
			}
		}
	}
}
#endif
