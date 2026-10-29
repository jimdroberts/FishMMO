using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2i-b of the biome suite (2026-10-25, decision 34): every floating kind the biome drafts name is a kind of I2i's
	/// baked-prop scatter (a detail mesh, a habit, a prefab), the Underwater Canyon's kelp raft a kind of I2p's sea floats, and
	/// every boardwalk style the drafts name a style of I2i's boardwalk pieces. Every default is what was cut before: no biome
	/// names a new kind or a style, and a planked walk lays exactly as I2i's.
	/// </summary>
	public class BiomeFloatKindsAndBoardwalkStylesTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string TemplateSource = "Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs";

		/// <summary>The inland floating kinds the drafts' APPROVED sections and the infrastructure map name, by the name they are appended under.</summary>
		private static readonly string[] DraftFloats =
		{
			"Tasselweed", "SacredLotus", "WaterChestnut", "Watercress", "AmericanLotus", "WaterClover", "WaterLettuce", "WaterSnowflake", "Ottelia",
			"Azolla", "FloatingHeart", "WaterSoldier", "WaterFern", "Bladderwort", "Spongeplant", "Pennywort", "BergyBits", "SaltRaft",
			"AmphibiousBistort", "Starwort", "WaterCrowfoot", "Pondweed", "BlueLotus", "FrothRaft", "PropaguleRaft", "TanninFoam", "Gutweed",
		};

		/// <summary>The boardwalk styles the drafts name (Farmland, Plains, Valley, Mangrove; Wetlands' reed-mat deck is the planked walk in its palette).</summary>
		private static readonly string[] DraftStyles = { "Passerella", "LandingWalk", "Bohlensteg", "PoleWalk" };

		// ── Defaults: nothing changes ─────────────────────────────────

		[Test]
		public void Defaults_NoBiomeNamesAStyle_AndTheTemplateDefaultIsPlanked()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				// A "no biome names it yet" pin: the authored rounds name their own floats and styles on purpose.
				if (BiomeAuthoredRounds.Contains(entry.Biome))
				{
					continue;
				}
				Assert.That(entry.BoardwalkStyle, Is.EqualTo(BiomeBoardwalkStyle.Planked), $"{entry.Biome} names no style yet");
				Assert.That(entry.FloatingPlants(), Is.Empty, $"{entry.Biome} floats nothing yet");
				Assert.That(entry.SeaFloats().Exists(f => f.Kind == BiomeSeaFloatKind.KelpRaft), Is.False, $"{entry.Biome} floats no kelp raft yet");
			}
			int templates = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				templates++;
				Match style = Regex.Match(text, @"^  boardwalkStyle: (\d+)", RegexOptions.Multiline);
				Assert.That(!style.Success || style.Groups[1].Value == "0" || BiomeAuthoredRounds.Contains(Path.GetFileNameWithoutExtension(path)), Is.True,
					$"{Path.GetFileName(path)} names a boardwalk style");
			}
			Assert.That(templates, Is.EqualTo(83));
			StringAssert.Contains("private BiomeBoardwalkStyle boardwalkStyle = BiomeBoardwalkStyle.Planked;", File.ReadAllText(TemplateSource));
		}

		[Test]
		public void Kinds_AreAppendedOnly()
		{
			Assert.That((byte)BiomeFloatingPlantKind.Duckweed, Is.EqualTo(0));
			Assert.That((byte)BiomeFloatingPlantKind.Frogbit, Is.EqualTo(3));
			for (int i = 0; i < DraftFloats.Length; i++)
			{
				Assert.That(Enum.TryParse(DraftFloats[i], out BiomeFloatingPlantKind kind), Is.True, DraftFloats[i]);
				Assert.That((int)kind, Is.EqualTo(4 + i), $"{DraftFloats[i]} appended in the map's order");
			}
			Assert.That(Enum.GetValues(typeof(BiomeFloatingPlantKind)).Length, Is.EqualTo(4 + DraftFloats.Length));
			Assert.That((int)BiomeSeaFloatKind.BrashIce, Is.EqualTo(8));
			Assert.That((int)BiomeSeaFloatKind.KelpRaft, Is.EqualTo(9));
			Assert.That(Enum.GetValues(typeof(BiomeSeaFloatKind)).Length, Is.EqualTo(BiomeSeaFloats.KindCount));
			Assert.That((int)BiomeBoardwalkStyle.Planked, Is.Zero);
			for (int i = 0; i < DraftStyles.Length; i++)
			{
				Assert.That(Enum.TryParse(DraftStyles[i], out BiomeBoardwalkStyle style), Is.True, DraftStyles[i]);
				Assert.That((int)style, Is.EqualTo(1 + i));
			}
			// The DetailKinds that draw them follow Frogbit in the same order (appended to the enum's end).
			Assert.That((int)DetailKind.Tasselweed, Is.EqualTo((int)DetailKind.Frogbit + 1));
			Assert.That((int)DetailKind.Gutweed - (int)DetailKind.Tasselweed, Is.EqualTo(DraftFloats.Length - 1));
		}

		// ── Every draft-named float resolves ──────────────────────────

		[Test]
		public void EveryDraftFloat_HasAHabit_ADetail_AndAPrefab()
		{
			var details = new Dictionary<string, DetailSpec>();
			foreach (DetailSpec spec in ProceduralArtCatalogue.Details)
			{
				details[ProceduralArtCatalogue.DetailPrefab(spec.Name)] = spec;
			}
			foreach (string name in DraftFloats)
			{
				Enum.TryParse(name, out BiomeFloatingPlantKind kind);
				Assert.That(BiomeFloatingPlants.TryHabit(kind, out BiomeFloatingPlants.Habit habit), Is.True, name);
				Assert.That(habit.Peak, Is.InRange(0.05f, 1f), name);
				string prefab = ProceduralArtCatalogue.FloatingPrefab(kind);
				Assert.That(prefab, Is.EqualTo(ProceduralArtCatalogue.DetailPrefab("Pond" + name)), name);
				Assert.That(details.TryGetValue(prefab, out DetailSpec detail), Is.True, $"{name}: {prefab} is a catalogued detail");
				Assert.That(detail.Plant.Kind.ToString(), Is.EqualTo(name), $"{name} is drawn by its own DetailKind");
				Assert.That(FloatingPlantMeshes.IsFloating(detail.Plant.Kind), Is.True, name);
				Assert.That(SeaFloorMeshes.IsAquatic(detail.Plant.Kind), Is.False, $"{name}: on the water, not under the sea");
				Assert.That(detail.Plant.Lean, Is.Zero, $"{name} does not lean");
				Vector2 scale = BiomeFloatingPlants.ScaleRange(kind);
				Assert.That(scale.x, Is.GreaterThan(0f).And.LessThanOrEqualTo(scale.y), name);
				Vector2 reach = FloatingPlantMeshes.Reach(in detail.Plant);
				Assert.That(reach.x, Is.GreaterThanOrEqualTo(0f), name);
				Assert.That(reach.y, Is.GreaterThan(0f), name);
				foreach (string prefix in new[] { "Detail_Grass", "Detail_Reeds", "Detail_Flowers", "Detail_Debris" })
				{
					Assert.That(prefab.StartsWith(prefix, StringComparison.Ordinal), Is.False, $"{prefab} must not read as a {prefix} kind");
				}
				// It grows somewhere: some water a sheltered metre or more out from the shore, at some depth, carries it.
				float best = 0f;
				foreach (float depth in new[] { 0.1f, 0.3f, 0.6f, 1f, 1.5f, 2.5f, 4f })
				{
					foreach (float shore in new[] { 0.5f, 2f, 5f, 15f, 40f })
					{
						best = Mathf.Max(best, BiomeFloatingPlants.Cover(kind, depth, shore, BiomeFloatingPlants.Shelter(1f, shore)));
					}
				}
				Assert.That(best, Is.GreaterThan(0.2f), $"{name} covers some water");
				Assert.That(BiomeFloatingPlants.Cover(kind, 0.05f, 5f, 1f), Is.Zero, $"{name}: nothing on a wet bank");
			}
			// The details were appended after the first four, which keep their places.
			int frogbit = Array.FindIndex(ProceduralArtCatalogue.Details, d => d.Name == "PondFrogbit");
			Assert.That(ProceduralArtCatalogue.Details[frogbit + 1].Name, Is.EqualTo("PondTasselweed"));
			Assert.That(ProceduralArtCatalogue.Details.Length, Is.EqualTo(frogbit + 1 + DraftFloats.Length), "the floating details are the catalogue's last");
		}

		[Test]
		public void Habits_FollowTheirPlants()
		{
			// Rooted kinds hold a band of depth.
			foreach (BiomeFloatingPlantKind rooted in new[] { BiomeFloatingPlantKind.SacredLotus, BiomeFloatingPlantKind.WaterChestnut, BiomeFloatingPlantKind.Ottelia,
				BiomeFloatingPlantKind.Pondweed, BiomeFloatingPlantKind.BlueLotus })
			{
				Assert.That(BiomeFloatingPlants.Cover(rooted, 1f, 20f, 1f), Is.GreaterThan(0.5f), $"{rooted} in a metre of water");
				Assert.That(BiomeFloatingPlants.Cover(rooted, 6f, 20f, 1f), Is.Zero, $"{rooted}: too deep for its stalks");
			}
			// Free floaters pile up in shelter near the shore and thin over open water.
			foreach (BiomeFloatingPlantKind free in new[] { BiomeFloatingPlantKind.Azolla, BiomeFloatingPlantKind.WaterLettuce, BiomeFloatingPlantKind.WaterFern,
				BiomeFloatingPlantKind.FrothRaft, BiomeFloatingPlantKind.TanninFoam })
			{
				float near = BiomeFloatingPlants.Cover(free, 2f, 1f, BiomeFloatingPlants.Shelter(0.3f, 1f));
				float far = BiomeFloatingPlants.Cover(free, 2f, 60f, BiomeFloatingPlants.Shelter(0.3f, 60f));
				Assert.That(near, Is.GreaterThan(far), $"{free} thickest by the shore");
				Assert.That(far, Is.Zero, $"{free} gone over open water");
			}
			Assert.That(BiomeFloatingPlants.Cover(BiomeFloatingPlantKind.BergyBits, 0.3f, 30f, 1f), Is.Zero, "a bergy bit grounds in the shallows");
			Assert.That(BiomeFloatingPlants.Cover(BiomeFloatingPlantKind.BergyBits, 5f, 30f, 1f), Is.GreaterThan(0f), "and floats over deep water");
			Assert.That(BiomeFloatingPlants.Cover(BiomeFloatingPlantKind.Pennywort, 1f, 12f, 1f), Is.Zero, "pennywort keeps by the bank");
			// The first four's rules are their own (unchanged); they have no table habit.
			Assert.That(BiomeFloatingPlants.TryHabit(BiomeFloatingPlantKind.Duckweed, out _), Is.False);
			Assert.That(BiomeFloatingPlants.TryHabit((BiomeFloatingPlantKind)200, out _), Is.False);
			Assert.That(BiomeFloatingPlants.Cover((BiomeFloatingPlantKind)200, 1f, 5f, 1f), Is.Zero);
		}

		[Test]
		public void KelpRaft_IsASeaFloat()
		{
			BiomeSeaFloatTraits t = BiomeSeaFloats.Traits(BiomeSeaFloatKind.KelpRaft);
			Assert.That(t.Chance, Is.GreaterThan(0f));
			Assert.That(t.DrawMetres, Is.InRange(1f, BiomeSeaFloats.MaxDrawMetres));
			Assert.That(t.Rigid, Is.False, "a weed mat bends to the swell");
			Assert.That(BiomeSeaFloats.LivesAt(BiomeSeaFloatKind.KelpRaft, 0.2f), Is.True);
			Assert.That(BiomeSeaFloats.LivesAt(BiomeSeaFloatKind.KelpRaft, 0.6f), Is.False, "cool seas only (the draft's t ≤ 0.3)");
			Assert.That(BiomeSeaFloats.Any(new List<BiomeSeaFloat> { new BiomeSeaFloat(BiomeSeaFloatKind.KelpRaft, 1f) }), Is.True);
			string meshes = File.ReadAllText("Assets/Scripts/Client/World/SeaFloats/SeaFloatMeshes.cs");
			int brash = meshes.IndexOf("b.Sub = (int)BiomeSeaFloatKind.BrashIce;", StringComparison.Ordinal);
			int kelp = meshes.IndexOf("b.Sub = (int)BiomeSeaFloatKind.KelpRaft;", StringComparison.Ordinal);
			Assert.That(kelp, Is.GreaterThan(brash).And.GreaterThan(0), "built after every older kind, so their seeds are unchanged");
		}

		// ── Boardwalk styles ──────────────────────────────────────────

		[Test]
		public void EveryDraftStyle_HasItsPieces_AndAShape()
		{
			StructurePiece span = StructurePieces.Find("BoardwalkSpan"), bent = StructurePieces.Find("BoardwalkBent");
			Assert.That(span.Variants, Is.EqualTo(StructurePieces.BoardwalkVariants));
			Assert.That(bent.Variants, Is.EqualTo(StructurePieces.BoardwalkVariants));
			var seen = new HashSet<int>();
			foreach (string name in DraftStyles)
			{
				Enum.TryParse(name, out BiomeBoardwalkStyle style);
				int variant = StructurePieces.BoardwalkStyleVariant(style);
				Assert.That(variant, Is.InRange(2, StructurePieces.BoardwalkVariants - 1), name);
				Assert.That(seen.Add(variant), Is.True, $"{name} has a variant of its own");
				StructurePieces.BoardwalkStyleShape shape = StructurePieces.BoardwalkShape(style, true);
				Assert.That(shape.Span, Is.EqualTo(variant), name);
				Assert.That(shape.Bent, Is.EqualTo(variant), name);
				Assert.That(shape.Braced, Is.False, name);
				Assert.That(shape.Width, Is.InRange(0.4f, 1.6f), $"{name} is built at its own width");
				Assert.That(shape.Rise, Is.InRange(0.15f, 0.9f), name);
				Assert.That(shape.BentClear, Is.LessThan(shape.Rise), $"{name}: a bent stands under its deck");
				Assert.That(StructurePieces.BoardwalkShape(style, false).Span, Is.EqualTo(variant), $"{name}: one span for every class of way");
			}
		}

		[Test]
		public void PlankedStyle_IsI2isWalk_Exactly()
		{
			StructurePieces.BoardwalkStyleShape road = StructurePieces.BoardwalkShape(BiomeBoardwalkStyle.Planked, false);
			StructurePieces.BoardwalkStyleShape path = StructurePieces.BoardwalkShape(BiomeBoardwalkStyle.Planked, true);
			Assert.That(road.Span, Is.Zero);
			Assert.That(path.Span, Is.EqualTo(1), "a footway's span is railed on one side");
			Assert.That(road.Bent, Is.Zero);
			Assert.That(road.Braced, Is.True);
			Assert.That(road.Rise, Is.EqualTo(PathBoardwalks.DeckRiseMetres));
			Assert.That(road.Ramp, Is.EqualTo(PathBoardwalks.RampMetres));
			Assert.That(road.Width, Is.Zero, "scaled across to the way");
			Assert.That(road.BentClear, Is.EqualTo(PathBoardwalks.BentClearMetres));
			Assert.That(road.BentDepth, Is.EqualTo(StructurePieces.BoardwalkBentDepth));
			Assert.That(StructurePieces.BoardwalkStyleVariant(BiomeBoardwalkStyle.Planked), Is.Zero);

			// The deck: the defaults and the planked shape's numbers give the same line, bit for bit.
			ScenePath way = Way(41);
			float Ground(float x, float z) => 2f + 0.4f * Mathf.Sin(x * 0.3f) + 0.02f * x;
			List<PathBoardwalks.Station> before = PathBoardwalks.Stations(way, 0, 40, Ground);
			List<PathBoardwalks.Station> after = PathBoardwalks.Stations(way, 0, 40, Ground, StructurePieces.BoardwalkModule, true, true, road.Rise, road.Ramp, road.Width);
			Assert.That(after.Count, Is.EqualTo(before.Count));
			for (int k = 0; k < before.Count; k++)
			{
				Assert.That(after[k].Deck, Is.EqualTo(before[k].Deck), $"station {k}");
				Assert.That(PathBoardwalks.BentScale(after[k], in road), Is.EqualTo(PathBoardwalks.BentScale(before[k])), $"bent {k}");
			}
			for (float clear = 0f; clear < 3f; clear += 0.07f)
			{
				Assert.That(StructurePieces.BoardwalkBentScale(clear, StructurePieces.BoardwalkBentDepth), Is.EqualTo(StructurePieces.BoardwalkBentScale(clear)));
			}
		}

		[Test]
		public void StyledDeck_StandsAtItsOwnHeight_AndItsBentsReachTheGround()
		{
			ScenePath way = Way(41);
			float Ground(float x, float z) => 1f + 0.1f * Mathf.Sin(x * 0.2f);
			foreach (string name in DraftStyles)
			{
				Enum.TryParse(name, out BiomeBoardwalkStyle style);
				StructurePieces.BoardwalkStyleShape shape = StructurePieces.BoardwalkShape(style, true);
				List<PathBoardwalks.Station> stations = PathBoardwalks.Stations(way, 0, 40, Ground, StructurePieces.BoardwalkModule, true, true, shape.Rise, shape.Ramp, shape.Width);
				Assert.That(stations[0].Deck, Is.EqualTo(stations[0].Ground + PathBoardwalks.MinClearMetres).Within(1e-4f), $"{name} ramps down at its start");
				for (int k = 2; k <= stations.Count - 3; k++)
				{
					float clear = stations[k].Deck - stations[k].Ground;
					Assert.That(clear, Is.GreaterThanOrEqualTo(shape.Rise - 1e-4f), $"{name} station {k}");
					Assert.That(clear, Is.LessThan(PathBoardwalks.DeckRiseMetres + 0.3f), $"{name} station {k}");
					float scale = PathBoardwalks.BentScale(stations[k], in shape);
					Assert.That(scale, Is.GreaterThan(0f), $"{name}: a bent under station {k}");
					Assert.That(scale * shape.BentDepth, Is.EqualTo(clear + StructurePieces.BoardwalkPileSink).Within(1e-4f), $"{name}: its feet driven into the ground");
				}
			}
		}

		/// <summary>
		/// Wetlands' reed-mat walk (the draft's check): a deck's planks are its piece's Floor, so a palette that builds the Timber
		/// style's Floor in reed lays every planked deck (and the passerella's, the landing walk's, the Bohlensteg's) in reed mats;
		/// the pole-walk's poles are woodwork and stay timber.
		/// </summary>
		[Test]
		public void ReedMatDeck_IsThePalettesTimberFloor()
		{
			var palette = new BiomeStructurePalette
			{
				Surfaces = new List<BiomeStructureSurface>
				{
					new BiomeStructureSurface(BiomeStructureStyle.Timber, BiomeStructureRole.Floor, BiomeStructureMaterial.Reed, BiomeStructurePalette.KitTint(BiomeStructureMaterial.Reed)),
				},
			};
			StructurePiece span = StructurePieces.Find("BoardwalkSpan");
			for (int variant = 0; variant < StructurePieces.BoardwalkVariants; variant++)
			{
				string[][] names = StructurePalettes.MaterialNames(span, variant, StructureFinish.None, palette, null, out bool differs);
				bool reed = Array.Exists(names[0], n => n.StartsWith(StructurePieces.Prefix + "Reed", StringComparison.Ordinal));
				bool poles = variant == StructurePieces.BoardwalkStyleVariant(BiomeBoardwalkStyle.PoleWalk);
				Assert.That(differs, Is.EqualTo(!poles), $"v{variant}");
				Assert.That(reed, Is.EqualTo(!poles), $"v{variant}: {string.Join(", ", names[0])}");
			}
		}

		// ── Wiring ────────────────────────────────────────────────────

		[Test]
		public void Wiring_StyleIsAuthored_Read_AndLaid()
		{
			var entry = new BiomeArtSpec.Entry { Biome = "Test" };
			Assert.That(entry.BoardwalkStyle, Is.EqualTo(BiomeBoardwalkStyle.Planked));
			entry.RaisesBoardwalks(BiomeBoardwalks.Paths, BiomeBoardwalkStyle.Bohlensteg);
			Assert.That(entry.Boardwalks, Is.EqualTo(BiomeBoardwalks.Paths));
			Assert.That(entry.BoardwalkStyle, Is.EqualTo(BiomeBoardwalkStyle.Bohlensteg));

			string authoring = File.ReadAllText(World + "/ProceduralArt/BiomeArtAuthoring.cs");
			StringAssert.Contains("AuthorValue(who, biome, BiomeTemplate.Authored.BoardwalkStyle,", authoring);
			string reader = File.ReadAllText(World + "/Terrain/BiomeTemplateArt.cs");
			StringAssert.Contains("public static BiomeBoardwalkStyle BoardwalkStyle(BiomeTemplate biome)", reader);
			string walks = File.ReadAllText(World + "/PointsOfInterest/Paths/PathBoardwalks.cs");
			StringAssert.Contains("BiomeTemplateArt.BoardwalkStyle(biome)", walks);
			StringAssert.Contains("StructurePieces.BoardwalkShape(style, footway)", walks);
			// A settlement's streets lay the plank walk (their Lay call names no style).
			string streets = File.ReadAllText(World + "/PointsOfInterest/PropsFeature.cs");
			Assert.That(streets.Contains("BoardwalkStyle"), Is.False, "streets stay planked");
		}

		/// <summary>A straight way along +x, two metres a point, a metre each side.</summary>
		private static ScenePath Way(int points)
		{
			var path = new ScenePath
			{
				Id = 4,
				Class = ScenePathClass.Footpath,
				Points = new Vector3[points],
				HalfWidth = new float[points],
				Wear = new float[points],
				Surface = new byte[points],
				Flags = new byte[points],
			};
			for (int i = 0; i < points; i++)
			{
				path.Points[i] = new Vector3(2f * i, 0f, 0f);
				path.HalfWidth[i] = 1f;
			}
			return path;
		}
	}
}
