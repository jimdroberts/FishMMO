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
	/// Decisions 10 and 13 of the biome suite (2026-10-15, 2026-10-16): the water and ice biomes, the rock a cave is cut in
	/// and the paving of each biome's streets moved onto the biome template, a cave's formations are its shell's rock, and
	/// the canyon walls stand in the rock their biome takes, at the cut and again on a repaint. Every value the template now
	/// carries is what the code decided before (a water biome by its name, a cave's shell by its form), so authoring changes
	/// no scene; the cobble table reaches the shader in the layout it reads; and no cut-time code picks a biome by its name
	/// any more.
	/// </summary>
	public class BiomeTemplateRolesTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		// ── Water role ────────────────────────────────────────────────

		/// <summary>
		/// The biomes the cut found by name before the role (SceneBiomeField, IceSheetSurface, IcePlacer), keyed as the registry
		/// looked them up: the name normalised to lowercase letters.
		/// </summary>
		private static readonly Dictionary<string, BiomeWaterRole> OldNames = new Dictionary<string, BiomeWaterRole>
		{
			[BiomeClimateVariant.Normalize("Lake")] = BiomeWaterRole.Lake,
			[BiomeClimateVariant.Normalize("Methane Lake")] = BiomeWaterRole.MethaneLake,
			[BiomeClimateVariant.Normalize("River")] = BiomeWaterRole.River,
			[BiomeClimateVariant.Normalize("Estuary")] = BiomeWaterRole.Estuary,
			[BiomeClimateVariant.Normalize("Salt Flat")] = BiomeWaterRole.SaltFlat,
			[BiomeClimateVariant.Normalize("Ice Sheet")] = BiomeWaterRole.IceSheet,
		};

		/// <summary>Every biome asset's spec water role is the role its name played (None for every other biome).</summary>
		[Test]
		public void SpecWaterRole_IsWhatTheNameGave_ForEveryBiomeAsset()
		{
			int checkedBiomes = 0;
			var wrong = new List<string>();
			foreach (string name in BiomeAssetNames())
			{
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				Assert.That(entry, Is.Not.Null, $"{name} has a spec entry");
				BiomeWaterRole old = OldNames.TryGetValue(BiomeClimateVariant.Normalize(name), out BiomeWaterRole role) ? role : BiomeWaterRole.None;
				if (entry.WaterRole != old)
				{
					wrong.Add($"{name}: spec {entry.WaterRole}, its name gave {old}");
				}
				checkedBiomes++;
			}
			Assert.IsEmpty(wrong, string.Join("; ", wrong));
			Assert.That(checkedBiomes, Is.EqualTo(BiomeArtSpec.Entries.Length), "every spec biome has its asset");
			Assert.That(checkedBiomes, Is.EqualTo(83));
		}

		/// <summary>Each role is held by exactly one spec biome, the one the cut's name fallback names.</summary>
		[Test]
		public void EachWaterRole_IsHeldByOneSpecBiome_TheOneItsFallbackNames()
		{
			foreach (BiomeWaterRole role in (BiomeWaterRole[])Enum.GetValues(typeof(BiomeWaterRole)))
			{
				if (role == BiomeWaterRole.None)
				{
					Assert.That(BiomeTemplateArt.LegacyWaterBiomeName(role), Is.Null);
					continue;
				}
				var holders = new List<string>();
				foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
				{
					if (entry.WaterRole == role)
					{
						holders.Add(entry.Biome);
					}
				}
				Assert.That(holders, Is.EqualTo(new[] { BiomeTemplateArt.LegacyWaterBiomeName(role) }), role.ToString());
				Assert.That(OldNames[BiomeClimateVariant.Normalize(holders[0])], Is.EqualTo(role));
			}
		}

		// ── Cave shells and their formations (decision 13, 2026-10-16) ──

		/// <summary>The rock CaveShaper.RockOf cut a cave in before 2026-10-16 (copied literally), the ground's being the biome's rule there.</summary>
		private static string OldShell(CaveForm form, Func<string> groundRock)
			=> form == CaveForm.LavaTube ? "Basalt" : form == CaveForm.IceCave ? CliffRocks.Ice : groundRock();

		/// <summary>The rock PointOfInterestCaveDressing.SetFor gave each cave form's formations before 2026-10-15 (copied literally).</summary>
		private static string OldFormationRock(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.IceCave: return "WaterIce";
				case CaveForm.LavaTube: return "Basalt";
				case CaveForm.SeaCave: return "Limestone";
				case CaveForm.Mine: return "Quartzite";
				default: return "Limestone";
			}
		}

		/// <summary>The geology a cave can stand in: every lithology's rock, and none known.</summary>
		private static List<string> GeologySamples()
		{
			var samples = new List<string> { null };
			foreach (Lithology lithology in PlanetGeology.Lithologies)
			{
				samples.Add(lithology.RockTypeName);
			}
			return samples;
		}

		/// <summary>
		/// Every biome's spec cave shells are every form's old shell, in the forms' order (Ground where it was the ground's
		/// rock), and resolved on every geology they are the rock the shaper cut the cave in: authoring changes no shell.
		/// </summary>
		[Test]
		public void SpecCaveShells_CutWhatTheShaperCut_ForEveryBiomeAndGeology()
		{
			var forms = (CaveForm[])Enum.GetValues(typeof(CaveForm));
			Assert.That(forms.Length, Is.EqualTo(6));
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			List<string> geologies = GeologySamples();
			int checkedShells = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				List<BiomeCaveShell> shells = entry.CaveShells();
				Assert.That(shells.Count, Is.EqualTo(forms.Length), entry.Biome);
				for (int i = 0; i < forms.Length; i++)
				{
					CaveForm form = forms[i];
					Assert.That((CaveForm)shells[i].Form, Is.EqualTo(form), entry.Biome);
					Assert.That(shells[i].Rock, Is.EqualTo(entry.CaveShellFor(form)));
					Assert.That(shells[i].Rock, Is.EqualTo(BiomeArtSpec.DefaultCaveShell(form)), $"{entry.Biome} {form}: no biome names its own yet");
					foreach (string geology in geologies)
					{
						Func<string> ground = () => CliffRocks.RockFor(entry, geology);
						Assert.That(CaveShaper.ResolveShell(shells[i].Rock, ground), Is.EqualTo(OldShell(form, ground)), $"{entry.Biome} {form} on {geology ?? "unknown"}");
						checkedShells++;
					}
				}
			}
			Assert.That(checkedShells, Is.EqualTo(83 * 6 * geologies.Count));
			Assert.That(BiomeArtSpec.DefaultCaveShell(CaveForm.LavaTube), Is.EqualTo("Basalt"));
			Assert.That(BiomeArtSpec.DefaultCaveShell(CaveForm.IceCave), Is.EqualTo(CliffRocks.Ice));
			foreach (CaveForm form in new[] { CaveForm.Cave, CaveForm.Grotto, CaveForm.SeaCave, CaveForm.Mine })
			{
				Assert.That(BiomeArtSpec.DefaultCaveShell(form), Is.EqualTo(BiomeCaveShell.Ground), form.ToString());
			}
			// An empty shell (a hand-cleared entry reaching the resolver) is the ground's, as Ground is.
			Assert.That(CaveShaper.ResolveShell(null, () => "Shale"), Is.EqualTo("Shale"));
			Assert.That(CaveShaper.ResolveShell("Obsidian", () => "Shale"), Is.EqualTo("Obsidian"));
		}

		/// <summary>A biome's file can give one form its own shell; the others keep the form's.</summary>
		[Test]
		public void SpecCaveShell_OverridesOneFormOnly()
		{
			BiomeArtSpec.Entry entry = new BiomeArtSpec.Entry().CaveShell(CaveForm.LavaTube, "Scoria").CaveShell(CaveForm.LavaTube, "Obsidian");
			Assert.That(entry.CaveShellFor(CaveForm.LavaTube), Is.EqualTo("Obsidian"), "the last word stands");
			Assert.That(entry.CaveShellFor(CaveForm.IceCave), Is.EqualTo(CliffRocks.Ice));
			Assert.That(entry.CaveShellFor(CaveForm.Mine), Is.EqualTo(BiomeCaveShell.Ground));
			Assert.That(entry.CaveShells().FindAll(r => r.Form == BiomeCaveForm.LavaTube).Count, Is.EqualTo(1));
		}

		/// <summary>
		/// A cave's formations are its shell's rock (2026-10-16), for every biome, geology and form: the rock the dressing
		/// is handed is the shell's (glacier ice's formations being water ice's), and the fallen rock and spoil are drawn in
		/// that very rock wherever it is a crust rock.
		/// </summary>
		[Test]
		public void CaveFormations_AreTheShellsRock_ForEveryBiomeAndGeology()
		{
			Assert.That(PointOfInterestCaveDressing.FormationRock(CliffRocks.Ice), Is.EqualTo("WaterIce"));
			List<string> geologies = GeologySamples();
			var crust = new HashSet<string>(Array.ConvertAll(RockTypes.Crust, t => t.Name));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach (CaveForm form in (CaveForm[])Enum.GetValues(typeof(CaveForm)))
				{
					foreach (string geology in geologies)
					{
						string shell = CaveShaper.ResolveShell(entry.CaveShellFor(form), () => CliffRocks.RockFor(entry, geology));
						string rock = PointOfInterestCaveDressing.FormationRock(shell);
						Assert.That(rock, Is.EqualTo(shell == CliffRocks.Ice ? "WaterIce" : shell), $"{entry.Biome} {form} on {geology ?? "unknown"}");
						foreach (string shape in PointOfInterestCaveDressing.FormationShapes(form))
						{
							(string artRock, string artShape) = PointOfInterestCaveDressing.FormationArt(rock, shape, form);
							Assert.That(RockTypes.TryGet(artRock, out RockType type) && RockTypes.TryShape(in type, artShape, out _), Is.True,
								$"{entry.Biome} {form} on {geology}: {shape} is drawn as {artRock} {artShape}, which has no art");
							bool rubble = shape == "Block" || shape == "Slabs" || shape == "Scree";
							if (rubble && crust.Contains(rock))
							{
								Assert.That(artRock, Is.EqualTo(rock), $"{entry.Biome} {form} on {geology}: the {shape} is not the shell's {rock}");
							}
							if (artRock != rock)
							{
								Assert.That(artRock, Is.EqualTo(PointOfInterestCaveDressing.StandInRock(form)), "only the form's old rock stands in");
								Assert.That(artShape, Is.EqualTo(shape), "a stand-in keeps the shape");
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Where the shell is the rock a form's formations were before (a limestone cave, every lava tube and ice cave, a
		/// quartzite mine), every formation is drawn exactly as before; and the old rock has every shape its form places, so a
		/// stand-in never lacks art.
		/// </summary>
		[Test]
		public void CaveFormations_InTheFormsOldRock_AreAsBefore()
		{
			foreach (CaveForm form in (CaveForm[])Enum.GetValues(typeof(CaveForm)))
			{
				string old = OldFormationRock(form);
				Assert.That(PointOfInterestCaveDressing.StandInRock(form), Is.EqualTo(old), form.ToString());
				List<string> shapes = PointOfInterestCaveDressing.FormationShapes(form);
				Assert.That(shapes, Is.Not.Empty, form.ToString());
				foreach (string shape in shapes)
				{
					Assert.That(PointOfInterestCaveDressing.FormationArt(old, shape, form), Is.EqualTo((old, shape)), $"{form} {shape}");
				}
			}
			// The shells the shaper always cut lava tubes and ice caves in give their formations' old rock.
			Assert.That(PointOfInterestCaveDressing.FormationRock(BiomeArtSpec.DefaultCaveShell(CaveForm.LavaTube)), Is.EqualTo(OldFormationRock(CaveForm.LavaTube)));
			Assert.That(PointOfInterestCaveDressing.FormationRock(BiomeArtSpec.DefaultCaveShell(CaveForm.IceCave)), Is.EqualTo(OldFormationRock(CaveForm.IceCave)));
		}

		/// <summary>Sample caves: what a cave cut in each kind of rock is dressed with.</summary>
		[Test]
		public void CaveFormations_Samples()
		{
			// A sandstone cave: sandstone blocks fallen from its roof; calcite dripstone (limestone's sinter-dressed art) on its floor.
			Assert.That(PointOfInterestCaveDressing.FormationArt("Sandstone", "Block", CaveForm.Cave), Is.EqualTo(("Sandstone", "Block")));
			Assert.That(PointOfInterestCaveDressing.FormationArt("Sandstone", "Stalagmite", CaveForm.Cave), Is.EqualTo(("Limestone", "Stalagmite")));
			Assert.That(PointOfInterestCaveDressing.FormationArt("Sandstone", "Gours", CaveForm.Grotto), Is.EqualTo(("Limestone", "Gours")));
			// A granite cave's fallen rock is split granite; a granite mine's spoil too.
			Assert.That(PointOfInterestCaveDressing.FormationArt("Granite", "Block", CaveForm.SeaCave), Is.EqualTo(("Granite", "Split")));
			Assert.That(PointOfInterestCaveDressing.FormationArt("Granite", "Scree", CaveForm.Mine), Is.EqualTo(("Granite", "Split")));
			// A slate mine's spoil is slate scree; an andesite mine's its talus.
			Assert.That(PointOfInterestCaveDressing.FormationArt("Slate", "Scree", CaveForm.Mine), Is.EqualTo(("Slate", "Scree")));
			Assert.That(PointOfInterestCaveDressing.FormationArt("Andesite", "Scree", CaveForm.Mine), Is.EqualTo(("Andesite", "Talus")));
			// A lava tube a biome cuts in obsidian: obsidian chunks for its lining's slabs, basalt dribble where obsidian has none.
			Assert.That(PointOfInterestCaveDressing.FormationArt("Obsidian", "Slabs", CaveForm.LavaTube), Is.EqualTo(("Obsidian", "Chunk")));
			Assert.That(PointOfInterestCaveDressing.FormationArt("Obsidian", "Dribble", CaveForm.LavaTube), Is.EqualTo(("Basalt", "Dribble")));
			// A rock no art knows (a hand-typed shell) stands in whole.
			Assert.That(PointOfInterestCaveDressing.FormationArt("Moonstone", "Block", CaveForm.Cave), Is.EqualTo(("Limestone", "Block")));
		}

		/// <summary>
		/// The cave shaper decides no rock of its own any more (only through the template's shell and its one logged fallback),
		/// and the dressing's formations are handed the very rock the shaper builds the walls in.
		/// </summary>
		[Test]
		public void CaveShaper_TakesItsShellFromTheTemplate_AndTheDressingTheShaperRock()
		{
			string shaper = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "PointsOfInterest/Shapers/CaveShaper.cs")));
			Assert.That(shaper, Does.Contain("BiomeTemplateArt.CaveShell(biome, form)"));
			Assert.That(shaper, Does.Contain("string rock = ShellOf(context.Request, site, system);"), "the shaper builds its walls from the shell");
			Assert.That(shaper, Does.Not.Contain("\"Basalt\""), "a rock decided in the shaper");
			Assert.That(shaper, Does.Not.Contain("CliffRocks.Ice"), "a rock decided in the shaper");
			Assert.That(shaper, Does.Not.Contain("CaveForm.LavaTube ?"), "a shell decided by form in the shaper");
			string dressing = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "PointsOfInterest/Features/PointOfInterestCaveDressing.cs")));
			Assert.That(dressing, Does.Contain("set.Rock = FormationRock(CaveShaper.ShellOf(context.Request, context.Record, system));"));
			Assert.That(dressing, Does.Not.Contain("BiomeTemplateArt.CaveRock"), "the old per-form formation rock");
			// The only cut-time reader of the spec's default shell is the template reader's fallback.
			string art = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "Terrain/BiomeTemplateArt.cs")));
			Assert.That(art, Does.Contain("Fallback(biome, \"Cave Shells\""));
		}

		/// <summary>The template's cave form is the cave shaper's, member for member and number for number (it is cast across).</summary>
		[Test]
		public void BiomeCaveForm_MirrorsCaveForm()
		{
			string[] names = Enum.GetNames(typeof(CaveForm));
			Assert.That(Enum.GetNames(typeof(BiomeCaveForm)), Is.EqualTo(names));
			foreach (string name in names)
			{
				Assert.That(Convert.ToInt32(Enum.Parse(typeof(BiomeCaveForm), name)), Is.EqualTo(Convert.ToInt32(Enum.Parse(typeof(CaveForm), name))), name);
			}
		}

		// ── Cobbles ───────────────────────────────────────────────────

		/// <summary>
		/// A per-layer slice map reaches the shader four layers to a vector, layer i at [i >> 2][i &amp; 3], always eight
		/// vectors, −1 past the map; no map at all (a scene cut before the cobbles had one) is −1 everywhere.
		/// </summary>
		[Test]
		public void CobbleTable_PacksFourLayersToAVector()
		{
			var map = new float[10];
			for (int i = 0; i < map.Length; i++)
			{
				map[i] = 100 + i;
			}
			Vector4[] packed = ScenePathSurfaceBinder.Pack(map);
			Assert.That(packed.Length, Is.EqualTo(ScenePathSurfaceBinder.MapVectors));
			Assert.That(packed.Length * 4, Is.EqualTo(32), "a slot for every one of the arrays' 32 layers");
			for (int layer = 0; layer < 32; layer++)
			{
				float expected = layer < map.Length ? 100 + layer : -1f;
				Assert.That(packed[layer >> 2][layer & 3], Is.EqualTo(expected), $"layer {layer}");
			}
			foreach (Vector4 v in ScenePathSurfaceBinder.Pack(null))
			{
				Assert.That(v, Is.EqualTo(new Vector4(-1f, -1f, -1f, -1f)));
			}
		}

		/// <summary>
		/// The binder pushes the cobble table the shader reads, the shader reads it by the ground layer as it reads the earth
		/// and road tables, and a layer without one falls back on the scene's one cobble slice as before.
		/// </summary>
		[Test]
		public void CobbleTable_BinderAndShaderAgree()
		{
			string binder = File.ReadAllText("Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/Terrain/TerrainArrayBinder.cs");
			Assert.That(binder, Does.Contain("Shader.PropertyToID(\"_FishPathStoneFor\")"));
			Assert.That(binder, Does.Contain("ScenePathSurfaceBinder.StoneMapOf(gameObject.scene)"));
			string shader = File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishWeatherTerrainArrayInput.hlsl");
			Assert.That(shader, Does.Contain("float4 _FishPathStoneFor[8];"));
			Assert.That(shader, Does.Contain("_FishPathStoneFor[layer >> 2][layer & 3]"));
			Assert.That(shader, Does.Contain("stoneSlice = stoneSlice > -0.5 ? stoneSlice : (_FishPathLayers.z > -0.5 ? _FishPathLayers.z : roadSlice);"));
			string generator = File.ReadAllText(Path.Combine(WorldFolder, "SceneGenerator.cs"));
			Assert.That(generator, Does.Contain("palette.PathLayerMap(SceneTerrainPalette.SlotPathStone, coverage)"), "the cut writes each layer's own biome's cobbles");
		}

		// ── Canyon walls ──────────────────────────────────────────────

		/// <summary>Each wall cell keeps its own rock, indexed in the order first met; a cell that is not wall has none.</summary>
		[Test]
		public void CanyonWallCells_KeepEachCellsOwnRock()
		{
			// 4 x 2 cells: the west half sandstone, the east half the biome's own granite, one cell not wall.
			bool[] wall = { true, true, true, true, true, false, true, true };
			int[] cells = CanyonWalls.CellRocks(wall, 4, 2, (x, z) => x < 2 ? "Sandstone" : x == 3 && z == 1 ? null : "Granite", out List<string> rocks);
			Assert.That(rocks, Is.EqualTo(new[] { "Sandstone", "Granite" }), "null counts as the default rock, sandstone");
			Assert.That(cells, Is.EqualTo(new[] { 0, 0, 1, 1, 0, -1, 1, 0 }));
			Assert.That(CanyonWalls.DefaultRock, Is.EqualTo("Sandstone"), "what an unknown rock stood for before");
		}

		/// <summary>The cut hands the canyon walls the biomes' rock rule, and the paint the same biome field.</summary>
		[Test]
		public void CanyonWalls_AreGivenTheBiomesRockRule()
		{
			string generator = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "SceneGenerator.cs")));
			Assert.That(generator, Does.Contain("BiomeRocks(wallBiomes)"));
			Assert.That(generator, Does.Contain("field: wallBiomes"));
			Assert.That(generator, Does.Contain("BiomeTemplateArt.RockRule(field.Biomes[b])"));
		}

		/// <summary>The wall record's runs give back every cell's index, gaps and row ends included.</summary>
		[Test]
		public void CanyonWallRecord_RoundTrips()
		{
			var random = new System.Random(1234);
			const int cellsX = 37, cellsZ = 23;
			var cells = new int[cellsX * cellsZ];
			for (int c = 0; c < cells.Length; c++)
			{
				// Bands of wall with runs of one geology rock, broken by gaps and by a change of rock.
				cells[c] = random.NextDouble() < 0.35 ? -1 : (c / 11) % 3;
			}
			int[] runs = CanyonWalls.EncodeCells(cells);
			Assert.That(runs.Length % 3, Is.EqualTo(0));
			Assert.That(CanyonWalls.DecodeCells(runs, cells.Length), Is.EqualTo(cells));
			Assert.That(CanyonWalls.EncodeCells(new[] { -1, -1 }), Is.Empty, "no wall, no runs");
			Assert.That(CanyonWalls.DecodeCells(new int[0], 3), Is.EqualTo(new[] { -1, -1, -1 }));
			Assert.That(CanyonWalls.DecodeCells(new[] { 1, 9, 0 }, 4), Is.EqualTo(new[] { -1, 0, 0, 0 }), "a run past the grid is cut at its edge");
			Assert.That(CanyonWalls.EncodeCells(new[] { 0, 0, 1, -1, 1 }), Is.EqualTo(new[] { 0, 2, 0, 2, 1, 1, 4, 1, 1 }));
		}

		/// <summary>
		/// A cell's geology is kept as the geology named it, unknown included (not read as the default), and the rock a wall
		/// cell wears is the biome rule's at the cell's centre on the scene's grid, the geology's without one, sandstone
		/// where nothing names one: the same for a cut (from the geology grid) and a repaint (from the record).
		/// </summary>
		[Test]
		public void CanyonWallRock_IsTheRuleAtTheCellsCentre()
		{
			bool[] wall = { true, true, false, true };
			int[] geology = CanyonWalls.GeologyCells(wall, 2, 2, (x, z) => x == 0 && z == 0 ? "Shale" : x == 1 && z == 1 ? "Shale" : null, out List<string> rocks);
			Assert.That(rocks, Is.EqualTo(new[] { "Shale", null }));
			Assert.That(geology, Is.EqualTo(new[] { 0, 1, -1, 0 }));

			var plan = new TerrainTilePlan { CountX = 2, CountZ = 1, TileMetres = 64f, Resolution = 33 };
			float seenEast = float.NaN, seenNorth = float.NaN;
			string seenGeology = "unset";
			string rock = CanyonWalls.WallRock(plan, 5, 7, "Shale", (east, north, g) =>
			{
				seenEast = east;
				seenNorth = north;
				seenGeology = g;
				return "Granite";
			});
			Assert.That(rock, Is.EqualTo("Granite"));
			Assert.That(seenGeology, Is.EqualTo("Shale"));
			Assert.That(seenEast, Is.EqualTo(0.5f * (SceneHeightField.EastOf(plan, 5) + SceneHeightField.EastOf(plan, 6))));
			Assert.That(seenNorth, Is.EqualTo(0.5f * (SceneHeightField.NorthOf(plan, 7) + SceneHeightField.NorthOf(plan, 8))));
			Assert.That(seenEast, Is.EqualTo(-64f + 5.5f * 2f).Within(1e-4f), "2 m samples from the scene's west edge");
			Assert.That(CanyonWalls.WallRock(plan, 0, 0, "Shale", (e, n, g) => null), Is.EqualTo("Shale"), "no rule there: the geology's");
			Assert.That(CanyonWalls.WallRock(plan, 0, 0, null, (e, n, g) => null), Is.EqualTo(CanyonWalls.DefaultRock));
			Assert.That(CanyonWalls.WallRock(plan, 0, 0, null, null), Is.EqualTo(CanyonWalls.DefaultRock));
		}

		/// <summary>
		/// A chunk's vertices are wired the same whatever rocks its cells wear, and its triangles are the same set, only
		/// grouped by rock: so a repaint regroups the cut's meshes in place into exactly the sub-meshes a cut with the new rocks
		/// builds, and the colliders (all the triangles) stay as they were.
		/// </summary>
		[Test]
		public void CanyonWallChunks_RegroupByRock_WithTheSameVerticesAndTriangles()
		{
			const int cellsX = 12, cellsZ = 9, chunk = 8, k = 2;
			var wall = new bool[cellsX * cellsZ];
			for (int c = 0; c < wall.Length; c++)
			{
				int x = c % cellsX, z = c / cellsX;
				wall[c] = Math.Abs(x - z) <= 2 || x == 10;
			}
			int[] cutRocks = CanyonWalls.CellRocks(wall, cellsX, cellsZ, (x, z) => "Sandstone", out List<string> cutNames);
			int[] newRocks = CanyonWalls.CellRocks(wall, cellsX, cellsZ, (x, z) => x < 5 ? "Granite" : z < 4 ? "Shale" : "Sandstone", out List<string> newNames);
			Assert.That(newNames.Count, Is.EqualTo(3));
			for (int cz0 = 0; cz0 < cellsZ; cz0 += chunk)
			{
				for (int cx0 = 0; cx0 < cellsX; cx0 += chunk)
				{
					List<long> cutKeys = CanyonWalls.ChunkTopology(wall, cutRocks, cutNames.Count, cellsX, cellsZ, cx0, cz0, chunk, k, out List<int>[] cutTriangles, out List<int> cutChunkRocks);
					List<long> newKeys = CanyonWalls.ChunkTopology(wall, newRocks, newNames.Count, cellsX, cellsZ, cx0, cz0, chunk, k, out List<int>[] newTriangles, out List<int> newChunkRocks);
					Assert.That(newKeys, Is.EqualTo(cutKeys), $"chunk {cx0},{cz0}: the vertices");
					Assert.That(cutChunkRocks.Count, Is.EqualTo(cutKeys.Count > 0 ? 1 : 0));
					var cut = Triangles(cutTriangles, cutChunkRocks);
					var regrouped = Triangles(newTriangles, newChunkRocks);
					cut.Sort(StringComparer.Ordinal);
					regrouped.Sort(StringComparer.Ordinal);
					Assert.That(regrouped, Is.EqualTo(cut), $"chunk {cx0},{cz0}: the triangles");
					// Each sub-mesh holds its own rock's cells' faces: 2·k² triangles a cell.
					foreach (int r in newChunkRocks)
					{
						int cellsOfRock = 0;
						for (int cz = cz0; cz < Math.Min(cellsZ, cz0 + chunk); cz++)
						{
							for (int cx = cx0; cx < Math.Min(cellsX, cx0 + chunk); cx++)
							{
								cellsOfRock += wall[cz * cellsX + cx] && newRocks[cz * cellsX + cx] == r ? 1 : 0;
							}
						}
						Assert.That(newTriangles[r].Count, Is.EqualTo(cellsOfRock * 2 * k * k * 3), $"rock {newNames[r]}");
					}
				}
			}
		}

		private static List<string> Triangles(List<int>[] lists, List<int> rocks)
		{
			var triangles = new List<string>();
			foreach (int r in rocks)
			{
				for (int i = 0; i < lists[r].Count; i += 3)
				{
					triangles.Add($"{lists[r][i]},{lists[r][i + 1]},{lists[r][i + 2]}");
				}
			}
			return triangles;
		}

		/// <summary>
		/// The cut keeps the walls' cells beside its terrain, and a repaint dresses the walls from them with its own biome field,
		/// before the NavMesh and the prop bakes, and only when the cut did not just build them from that field; a scene with
		/// walls but no record says once that it needs a re-cut.
		/// </summary>
		[Test]
		public void Repaint_RedressesTheCanyonWalls_FromTheKeptCells()
		{
			string generator = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "SceneGenerator.cs")));
			Assert.That(generator, Does.Contain("BiomeRocks(wallBiomes), CanyonWalls.RecordPath(terrainFolder, request.SceneName));"));
			Assert.That(generator, Does.Contain("bool wallsBuiltFromField = field != null;"));
			Assert.That(generator, Does.Contain("if (!wallsBuiltFromField)"));
			Assert.That(generator, Does.Contain("CanyonWalls.Redress(scene, plan, record, BiomeRocks(field))"));
			Assert.That(generator, Does.Contain("was cut before its walls' cells were kept"));
			string repaint = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "Terrain/BiomeRepaintTool.cs")));
			int paint = repaint.IndexOf("SceneGenerator.PaintBiomes(", StringComparison.Ordinal);
			int bake = repaint.IndexOf("SceneGenerator.BakePropsAndNavMesh(", StringComparison.Ordinal);
			Assert.That(paint, Is.GreaterThan(0));
			Assert.That(bake, Is.GreaterThan(paint), "the walls are dressed before the NavMesh is baked from their colliders");
			string walls = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "Terrain/CanyonWalls.cs")));
			Assert.That(walls, Does.Contain("int[] cellRock = WallRocks(wall, cellGeology, geologyRocks, field.Plan, cellsX, cellsZ, rockAt, out List<string> rocks);"), "the cut");
			Assert.That(walls, Does.Contain("int[] cellRock = WallRocks(wall, cellGeology, geologyRocks, record.Plan, cellsX, cellsZ, rockAt, out List<string> rocks);"), "the repaint");
			Assert.That(walls, Does.Contain("List<long> keys = ChunkTopology("), "one wiring for both");
		}

		// ── No selection by name ──────────────────────────────────────

		/// <summary>A biome looked up by a literal name or a name constant, or a biome's (or a template's) name compared with a literal.</summary>
		private static readonly Regex ByName = new Regex(
			@"BiomeRegistry\.(Get|Contains|TryGet)\(\s*(""|[A-Za-z_.]*BiomeName)"
			+ @"|\b([A-Za-z_]*[Bb]iome[A-Za-z_]*|[A-Za-z_]*[Tt]emplate)\??\.(name|ResolvedDisplayName|DisplayName|Key)\s*(==|!=)\s*"""
			+ @"|""\s*(==|!=)\s*([A-Za-z_]*[Bb]iome[A-Za-z_]*|[A-Za-z_]*[Tt]emplate)\??\.(name|ResolvedDisplayName|DisplayName|Key)\b",
			RegexOptions.CultureInvariant);

		/// <summary>
		/// No cut-time code picks a biome for a part by its name (2026-10-15: the water role replaced the last of them): the
		/// scene generator, the terrain painters and placers, hydrology and every point-of-interest builder. Naming code may
		/// read names; BiomeTemplateArt holds the one logged fallback (the old name of a role no template holds yet).
		/// </summary>
		[Test]
		public void CutTimeCode_NeverSelectsABiomeByName()
		{
			// The pattern catches what the code did before.
			foreach (string old in new[]
			{
				"lakeBiome = BiomeRegistry.Get(\"Lake\");",
				"&& BiomeRegistry.Contains(BiomeName);",
				"&& BiomeRegistry.Contains(IceSheetSurface.BiomeName);",
				"if (biome.name == \"Ice Sheet\")",
				"if (template.ResolvedDisplayName == \"River\")",
			})
			{
				Assert.That(ByName.IsMatch(old), Is.True, "control: " + old);
			}
			Assert.That(ByName.IsMatch("if (found != null && found.name == \"HullWaterLid\")"), Is.False, "not a biome");

			var files = new List<string> { Path.Combine(WorldFolder, "SceneGenerator.cs"), Path.Combine(WorldFolder, "SceneGeneration.cs") };
			files.AddRange(Directory.GetFiles(Path.Combine(WorldFolder, "Terrain"), "*.cs", SearchOption.AllDirectories));
			files.AddRange(Directory.GetFiles(Path.Combine(WorldFolder, "PointsOfInterest"), "*.cs", SearchOption.AllDirectories));
			Assert.That(files.Count, Is.GreaterThan(50), "the scan found the cut's sources");
			var found = new List<string>();
			foreach (string file in files)
			{
				string name = file.Replace('\\', '/');
				if (name.EndsWith("/Terrain/BiomeTemplateArt.cs", StringComparison.Ordinal) || Path.GetFileName(name).Contains("Naming"))
				{
					continue;
				}
				foreach (string line in CodeOnly(File.ReadAllText(file)).Split('\n'))
				{
					if (ByName.IsMatch(line))
					{
						found.Add($"{Path.GetFileName(file)}: {line.Trim()}");
					}
				}
			}
			Assert.IsEmpty(found, "cut-time code selecting a biome by its name: " + string.Join(" | ", found));
		}

		// ── Helpers ───────────────────────────────────────────────────

		/// <summary>The biome assets' names (m_Name), the climate profile that shares their folder left out.</summary>
		private static IEnumerable<string> BiomeAssetNames()
		{
			var names = new List<string>();
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				Match m = Regex.Match(text, "^  m_Name: ?(.*)$", RegexOptions.Multiline);
				names.Add(m.Groups[1].Value.Trim().Trim('\r'));
			}
			names.Sort(StringComparer.Ordinal);
			return names;
		}

		/// <summary>The source with comment lines dropped, so a scan sees code and not prose.</summary>
		private static string CodeOnly(string source)
		{
			var code = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}
	}
}
