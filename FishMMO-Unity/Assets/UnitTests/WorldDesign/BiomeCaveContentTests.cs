using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The biome suite's cave content (2026-10-16, phase I6c): each biome's cave crystals (a catalogue of kinds, each its own
	/// habit and material, sound, budgeted, deterministic and distinct; selenite exactly the old crystal), the new way-light kinds
	/// (kit pieces that give off their light), the caves' and mines' lights moved onto the template (every biome's default the
	/// cave dressing's old rule), and the structure palette's leftovers from I6a (decks are floors, a turf roof's ridge its trim,
	/// hay and bone never a part).
	/// </summary>
	[TestFixture]
	public class BiomeCaveContentTests
	{
		private const int Seed = ProceduralArtCatalogue.DefaultSeed;

		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private static string Code(string path) => File.ReadAllText(Path.Combine(World, path));

		private static IEnumerable<BiomeCaveCrystalKind> AllKinds => (BiomeCaveCrystalKind[])Enum.GetValues(typeof(BiomeCaveCrystalKind));

		// ── The crystal catalogue ─────────────────────────────────────

		[Test]
		public void CrystalKinds_AreAppendOnly_AndTheCatalogueIsInKindOrder()
		{
			string[] names =
			{
				"Selenite", "Amethyst", "ClearQuartz", "SmokyQuartz", "GreenFluorite", "FrostCrystal", "Obsidian", "Amberlight", "Wyrdstone", "Sunstone",
				// 2026-10-26 (NEW-CRYSTALS): every kind the biome drafts name, appended.
				"Phillipsite", "Lodeheart", "FireIce", "Hadalite", "Gwindel", "PinkFluorite", "Starfrost", "Stibnite", "Rhodochrosite", "Stormglass", "FireOpal",
				"Perilight", "Twinquartz", "Jadeite", "Hotarubi", "Aragonite", "Pearlspar", "Galena", "Chalcopyrite", "Wyrmglass", "BlueJohn", "Helictite",
				"Echospar", "Cassiterite", "Bellspar", "Apatite", "Lagoonglass", "Tektite", "Starglass", "SalAmmoniac", "Eitrglass", "Dogtooth", "Stellamaris",
				"Malachite", "Sunglass", "Ilmenite", "Phaseglass", "Vivianite", "Ebbspar", "Celestine", "Fogpearl", "Cinnabar", "Farrstone", "Realgar",
				"Orpiment", "Dalistone", "IcelandSpar", "Stilbite", "Solarsteinn", "CupHoar", "Nagamani", "Skyglass", "Turquoise", "Gueststar", "Garnet",
				"Staurolite", "Heartgarnet", "Hoarfeather", "Cryocalcite", "Chudfire", "Hydrohalite", "Hyalite", "Nightpearl", "ScythianEmerald", "Hornice",
				"Jadeice", "Swellglass", "Chrysolite", "MansionPearl", "WatermelonTourmaline", "Leafglass", "Specularite", "Glyphjade", "Zircon", "Monsoonglass",
				"Smithsonite", "Mistjade", "CavePearl", "Nixglass", "Thenardite", "OlivineBomb", "Magecstone", "Marcasite", "Nuru", "Simetite", "Fatastone",
				"Waxspar", "Adamant", "Orphanstone", "Cristobalite", "BrimstoneAmber", "Dracontite", "Hydrophane", "Moonstone", "NitrogenSpar", "Tearglass",
				"DesertRose", "Alum", "Oraclestone", "Siderite", "GalernaStone", "Wispglass", "Stillglass", "BariteRose", "SatinSpar", "Sunspoke", "Antozonite",
				"Septentrion", "Balas", "Levanite", "Sapphire", "Shamirspar", "Agate", "RhinegoldSeam", "Kyanite", "Haarglass", "Lajvard", "Shabchiragh",
				"Wollastonite", "Pyrrhotite", "Sibylleaf", "Halite", "Sylvite", "Embersalt", "Reefgold", "Emerald", "Rainglass", "Goblinfire", "Peridot",
				"Sunshell", "Lapastone", "Analcime", "Rutilquartz", "MidwinterSpar", "Dioptase", "WolfsEye", "Covellite", "Anhydrite", "Gledstone",
				"NativeSulphur", "Bluebrim", "Leadfeather", "Pyrite", "Cloudheart", "Mireopal", "Aetite", "BollasEye", "Hauyne", "Leucite", "Saeculite",
				"TrapicheEmerald", "Mistberyl", "Quicksilver", "Hazeamber", "Epsomite", "Sakhrastone", "Labradorite", "Aurorite", "Ikaite", "Tremorspar",
				"Demantoid", "Sphene", "Laurinite", "SulphurNeedles", "Smokestone", "Hearthglass", "Hieratite", "Forgeheart", "Goethite", "Weepglass",
				"Carnelian", "Abzulite", "FlintGeode",
			};
			Assert.That(Enum.GetNames(typeof(BiomeCaveCrystalKind)), Is.EqualTo(names));
			for (int i = 0; i < names.Length; i++)
			{
				Assert.That((int)(BiomeCaveCrystalKind)Enum.Parse(typeof(BiomeCaveCrystalKind), names[i]), Is.EqualTo(i), names[i]);
			}
			CaveMineral[] all = ProceduralArtCatalogue.CaveMinerals;
			Assert.That(all.Length, Is.EqualTo(names.Length));
			for (int i = 0; i < all.Length; i++)
			{
				Assert.That((int)all[i].Kind, Is.EqualTo(i), "CaveMineralOf indexes the table by kind");
				Assert.That(all[i].Description, Is.Not.Empty);
				Assert.That(all[i].LightHue, Is.InRange(-1, 3), $"{all[i].Kind}: SiteLightPool reads two bits of hue");
				Assert.That(all[i].Glows, Is.EqualTo(BiomeCaveCrystals.Glows(all[i].Kind)), $"{all[i].Kind}: the runtime's glow rule is the catalogue's");
				if (all[i].Glows)
				{
					Assert.That(all[i].Lit, Is.True, $"{all[i].Kind} glows, so it carries a light");
					Assert.That(all[i].Hue.EmitterHue, Is.EqualTo(all[i].LightHue), $"{all[i].Kind}: its glow and its light are one hue");
					Assert.That(all[i].Hue.Light.maxColorComponent, Is.GreaterThan(0f));
				}
				else if (!all[i].Formation)
				{
					Assert.That(all[i].Hue.Emission.maxColorComponent, Is.EqualTo(0f), $"{all[i].Kind} only glitters under a lamp");
					Assert.That(all[i].Hue.EmitterHue, Is.EqualTo(-1));
					Assert.That(all[i].Lit, Is.False, $"{all[i].Kind} brings no light of its own");
				}
			}
			// The formations' crystals are the rock art's own: gypsum's (selenite) and water ice's (frost crystal).
			foreach (CaveMineral m in all.Where(m => m.Formation))
			{
				Assert.That(RockTypes.TryGet(m.FormationRock, out RockType type), Is.True, m.FormationRock);
				Assert.That(RockTypes.TryShape(in type, m.FormationShape, out _), Is.True, $"{m.FormationRock} {m.FormationShape}");
			}
			Assert.That(all.Where(m => m.Formation).Select(m => m.Kind), Is.EqualTo(new[] { BiomeCaveCrystalKind.Selenite, BiomeCaveCrystalKind.FrostCrystal, BiomeCaveCrystalKind.SulphurNeedles }));
			// Each own-built kind is its own habit and colour: no two alike.
			var looks = new HashSet<string>();
			foreach (CaveMineral m in all.Where(m => !m.Formation))
			{
				Assert.That(looks.Add($"{m.Habit} {m.Hue.Albedo}"), Is.True, $"{m.Kind} looks like another kind");
				Assert.That(m.Hue.Name, Is.EqualTo(m.Kind.ToString()));
			}
			Assert.That(all.Where(m => !m.Formation).Select(m => m.Habit).Distinct().Count(), Is.GreaterThanOrEqualTo(6), "prism clusters, geodes, blades, druses, nodules, cubes, shards");
		}

		[Test]
		public void CrystalHabits_AreSound_Budgeted_Seated_AndTheSameEveryTime()
		{
			foreach (CaveMineralHabit habit in ProceduralArtCatalogue.CaveMineralHabits())
			{
				for (int v = 0; v < ProceduralArtCatalogue.CaveMineralVariants; v++)
				{
					int previous = int.MaxValue;
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						string name = $"{habit} v{v} LOD{lod}";
						MeshBuilder m = CaveGlowMeshes.BuildMineral(habit, v, lod, Seed);
						List<string> problems = m.Validate(true);
						Assert.That(problems, Is.Empty, $"{name}: {(problems.Count > 0 ? problems[0] : "")}");
						Assert.That(m.TriangleCount, Is.GreaterThan(0), name);
						Assert.That(m.TriangleCount, Is.LessThanOrEqualTo(previous), $"{name} has more triangles than the level before it");
						// Budgeted like the glow art's crystals (a cluster is 300–1000 triangles at its finest; a prop's ceiling is 10 000).
						Assert.That(m.TriangleCount, Is.LessThanOrEqualTo(3000), $"{name}: {m.TriangleCount} triangles");
						previous = m.TriangleCount;
						Assert.That(m.Submeshes.Count, Is.EqualTo(2), $"{name}: rock and crystal");
						Assert.That(m.Submeshes.All(s => s.Count > 0), Is.True, $"{name}: an empty submesh");
						Bounds b = m.Bounds;
						Assert.That(b.max.y, Is.InRange(0.15f, 2.2f), $"{name} stands up from its foot");
						Assert.That(b.min.y, Is.InRange(-1f, 0f), $"{name} is seated a little below its foot");
						Assert.That(Mathf.Max(b.size.x, b.size.z), Is.InRange(0.3f, 2.6f), $"{name} footprint");
						foreach (Vector2 uv in m.UVs)
						{
							Assert.That(float.IsNaN(uv.x) || float.IsNaN(uv.y), Is.False, $"{name}: a texture coordinate is not a number");
						}
						// The crystal's u is a glow level inside the ramp.
						foreach (int i in m.Submeshes[1])
						{
							Assert.That(m.UVs[i].x, Is.InRange(CaveGlowMeshes.Glow(0f) - 1e-4f, CaveGlowMeshes.Glow(1f) + 1e-4f), name);
						}
					}
					MeshBuilder a = CaveGlowMeshes.BuildMineral(habit, v, 0, Seed), again = CaveGlowMeshes.BuildMineral(habit, v, 0, Seed);
					Assert.That(again.Positions, Is.EqualTo(a.Positions), $"{habit} v{v} is the same every time");
					MeshBuilder other = CaveGlowMeshes.BuildMineral(habit, v, 0, Seed + 1);
					Assert.That(other.Positions, Is.Not.EqualTo(a.Positions), $"{habit} v{v} follows the seed");
				}
				// Its variants are distinct shapes.
				var shapes = new HashSet<string>();
				for (int v = 0; v < ProceduralArtCatalogue.CaveMineralVariants; v++)
				{
					MeshBuilder m = CaveGlowMeshes.BuildMineral(habit, v, 0, Seed);
					Assert.That(shapes.Add($"{m.VertexCount} {m.Bounds.size}"), Is.True, $"{habit} v{v} is another variant's shape");
				}
			}
			// And no two habits build the same shape.
			var habits = new HashSet<string>();
			foreach (CaveMineralHabit habit in ProceduralArtCatalogue.CaveMineralHabits())
			{
				MeshBuilder m = CaveGlowMeshes.BuildMineral(habit, 0, 0, Seed);
				Assert.That(habits.Add($"{m.VertexCount} {m.Bounds.size}"), Is.True, $"{habit} builds another habit's shape");
			}
		}

		[Test]
		public void CrystalFiles_AreListed_WhereTheGeneratorLooks()
		{
			var payload = new HashSet<string>(ProceduralArtCatalogue.PayloadPaths());
			var wrappers = new HashSet<string>(ProceduralArtCatalogue.WrapperPaths());
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			foreach (CaveMineralHabit habit in ProceduralArtCatalogue.CaveMineralHabits())
			{
				for (int v = 0; v < ProceduralArtCatalogue.CaveMineralVariants; v++)
				{
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						Assert.That(payload, Does.Contain(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.CaveMineralMesh(habit, v, lod))));
					}
				}
			}
			foreach (CaveMineral m in ProceduralArtCatalogue.CaveMinerals)
			{
				for (int v = 0; v < ProceduralArtCatalogue.CaveMineralVariants; v++)
				{
					string name = CaveArt.MineralPrefabName(m.Kind, v);
					if (m.Formation)
					{
						Assert.That(name, Is.EqualTo(RockArtNames.FormationPrefab(m.FormationRock, m.FormationShape, v)), "a formation's crystal is the rock art's prefab");
						continue;
					}
					Assert.That(prefabs, Does.Contain(name));
					Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.PrefabPath(name)));
				}
				if (!m.Formation)
				{
					Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.MaterialPath(ProceduralArtCatalogue.CaveMineralMaterial(m.Kind))));
				}
			}
			Assert.That(ProceduralArtCatalogue.PrefabPath(CaveArt.MineralPrefabName(BiomeCaveCrystalKind.Amethyst, 1)),
				Is.EqualTo("Assets/Prefabs/Shared/Biomes/Generated/Prefabs/Cave/Cave_Mineral_Amethyst_1.prefab"), "paths are a contract");
			Assert.That(ProceduralArtFamilies.FamiliesOfStep("Structure: cave crystals"), Is.EqualTo(new[] { ArtFamily.Structures }));
		}

		[Test]
		public void Selenite_IsTheOldCrystal_EveryBiomesDefault_AndPicksAreByWeight()
		{
			// The chambers' crystal before 2026-10-16: gypsum's Crystal formation, its variant drawn from the formations' four.
			Assert.That(ProceduralArtCatalogue.CaveMineralVariants, Is.EqualTo(RockTypes.VariantCount));
			for (int v = 0; v < RockTypes.VariantCount; v++)
			{
				Assert.That(CaveArt.MineralPrefabName(BiomeCaveCrystalKind.Selenite, v), Is.EqualTo(RockArtNames.FormationPrefab("Gypsum", "Crystal", v)));
				Assert.That(CaveArt.MineralPrefabName(BiomeCaveCrystalKind.FrostCrystal, v), Is.EqualTo(RockArtNames.FormationPrefab("WaterIce", "Crystal", v)));
			}
			CaveMineral selenite = ProceduralArtCatalogue.CaveMineralOf(BiomeCaveCrystalKind.Selenite);
			Assert.That(selenite.Lit, Is.True, "a cave light beside it, as always");
			Assert.That(selenite.LightHue, Is.EqualTo(-1), "of a hue by chance, as always");
			Assert.That(BiomeCaveCrystals.Default().Select(c => c.ToString()), Is.EqualTo(new[] { "Selenite×1" }));
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				List<BiomeCaveCrystal> crystals = entry.CaveCrystals();
				if (BiomeAuthoredRounds.Contains(entry.Biome))
				{
					// An authored round names its own crystals (legacy equality only; BiomeAuthoredRoundTests pins them): its caves still grow one.
					Assert.That(BiomeCaveCrystals.TryPick(crystals, 0.5f, out _), Is.True, $"{entry.Biome}: its caves grow a crystal");
					continue;
				}
				Assert.That(crystals.Select(c => c.ToString()), Is.EqualTo(new[] { "Selenite×1" }), $"{entry.Biome}: no biome round has named its own crystals yet");
				Assert.That(BiomeCaveCrystals.NeedsRoll(crystals), Is.False, $"{entry.Biome}: one kind, no draw, so every cave's draws stand as they were");
				Assert.That(BiomeCaveCrystals.TryPick(crystals, 0f, out BiomeCaveCrystalKind kind) && kind == BiomeCaveCrystalKind.Selenite, Is.True);
			}
			// Picks by weight; no weight never; glowing-only picks only glowing kinds.
			var mix = new List<BiomeCaveCrystal>
			{
				new BiomeCaveCrystal(BiomeCaveCrystalKind.SmokyQuartz, 0.7f),
				new BiomeCaveCrystal(BiomeCaveCrystalKind.Obsidian, 0f),
				new BiomeCaveCrystal(BiomeCaveCrystalKind.Amberlight, 0.3f),
			};
			Assert.That(BiomeCaveCrystals.NeedsRoll(mix), Is.True);
			Assert.That(BiomeCaveCrystals.NeedsRoll(mix, glowingOnly: true), Is.False);
			var counts = new Dictionary<BiomeCaveCrystalKind, int>();
			for (int i = 0; i < 1000; i++)
			{
				Assert.That(BiomeCaveCrystals.TryPick(mix, (i + 0.5f) / 1000f, out BiomeCaveCrystalKind k), Is.True);
				counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1;
			}
			Assert.That(counts[BiomeCaveCrystalKind.SmokyQuartz], Is.EqualTo(700));
			Assert.That(counts[BiomeCaveCrystalKind.Amberlight], Is.EqualTo(300));
			Assert.That(counts.ContainsKey(BiomeCaveCrystalKind.Obsidian), Is.False);
			Assert.That(BiomeCaveCrystals.TryPick(mix, 0.1f, out BiomeCaveCrystalKind glow, glowingOnly: true) && glow == BiomeCaveCrystalKind.Amberlight, Is.True);
			Assert.That(BiomeCaveCrystals.TryPick(new List<BiomeCaveCrystal>(), 0.5f, out _), Is.False, "none grow");
			Assert.That(BiomeCaveCrystals.TryPick(null, 0.5f, out _), Is.False);
			// A crystal post carries the heaviest glowing kind, else the heaviest.
			Assert.That(BiomeCaveCrystals.PostCrystal(mix), Is.EqualTo(BiomeCaveCrystalKind.Amberlight));
			Assert.That(BiomeCaveCrystals.PostCrystal(new List<BiomeCaveCrystal> { new BiomeCaveCrystal(BiomeCaveCrystalKind.Amethyst, 1f), new BiomeCaveCrystal(BiomeCaveCrystalKind.GreenFluorite, 2f) }),
				Is.EqualTo(BiomeCaveCrystalKind.GreenFluorite));
			Assert.That(BiomeCaveCrystals.PostCrystal(BiomeCaveCrystals.Default()), Is.EqualTo(BiomeCaveCrystalKind.Selenite));
			Assert.That(BiomeCaveCrystals.PostCrystal(null), Is.EqualTo(BiomeCaveCrystalKind.Selenite));
		}

		// ── Way lights ────────────────────────────────────────────────

		/// <summary>The light each kind's piece gives off (what SiteLightPool lights it with).</summary>
		private static readonly Dictionary<BiomeWayLightKind, SceneEmitterKind> LightOf = new Dictionary<BiomeWayLightKind, SceneEmitterKind>
		{
			[BiomeWayLightKind.LanternPost] = SceneEmitterKind.Lamp,
			[BiomeWayLightKind.TorchPost] = SceneEmitterKind.Torch,
			[BiomeWayLightKind.Brazier] = SceneEmitterKind.Fire,
			[BiomeWayLightKind.LanternStand] = SceneEmitterKind.Lamp,
			[BiomeWayLightKind.WallTorch] = SceneEmitterKind.Torch,
			[BiomeWayLightKind.HangingLantern] = SceneEmitterKind.Lamp,
			[BiomeWayLightKind.WallLantern] = SceneEmitterKind.Lamp,
			[BiomeWayLightKind.Cresset] = SceneEmitterKind.Fire,
			[BiomeWayLightKind.CrookLantern] = SceneEmitterKind.Lamp,
			// The cold glows (2026-10-18, I6g): faerie, crystal and rune light.
			[BiomeWayLightKind.Glowjar] = SceneEmitterKind.Glow,
			[BiomeWayLightKind.CrystalPost] = SceneEmitterKind.Glow,
			[BiomeWayLightKind.RuneStone] = SceneEmitterKind.Glow,
			[BiomeWayLightKind.HangingBracket] = SceneEmitterKind.Lamp,
		};

		[Test]
		public void WayLightKinds_AreAppendOnly_KitPieces_ThatGiveOffTheirLight()
		{
			string[] names = { "LanternPost", "TorchPost", "Brazier", "LanternStand", "WallTorch", "HangingLantern", "WallLantern", "Cresset", "CrookLantern", "Glowjar", "CrystalPost", "RuneStone", "HangingBracket" };
			// The first thirteen keep their names and numbers; the suite's lights (2026-10-26) follow them (WayLightSuiteTests).
			Assert.That(Enum.GetNames(typeof(BiomeWayLightKind)).Take(names.Length), Is.EqualTo(names));
			foreach (BiomeWayLightKind light in (BiomeWayLightKind[])Enum.GetValues(typeof(BiomeWayLightKind)))
			{
				if (light >= BiomeWayLights.FirstSuiteLight)
				{
					continue;
				}
				Assert.That((int)light, Is.EqualTo(Array.IndexOf(names, light.ToString())), $"{light} keeps its number");
				Assert.That(BiomeWayLights.EmitterOf(light), Is.EqualTo(LightOf[light]), $"{light}'s table row names its emitter");
				string id = BiomeWayLights.PieceId(light);
				Assert.That(BiomeWayLights.PieceQuery(light), Is.EqualTo("=" + id));
				StructurePiece piece = StructureKit.Get(id);
				Assert.That(piece, Is.Not.Null, $"{light} is the kit's piece {id}");
				Assert.That(piece.HasTag("light"), Is.True, $"{id} is a light");
				for (int v = 0; v < piece.Variants; v++)
				{
					IReadOnlyList<(Vector3 at, SceneEmitterKind kind)> emitters = StructurePieces.EmittersOf(piece, v);
					Assert.That(emitters.Count(e => e.kind == LightOf[light]), Is.GreaterThanOrEqualTo(1), $"{id} v{v} gives off its {LightOf[light]}");
				}
				// How it is set: the wall pieces stand out of a wall along −z (and are walked past), the hanging lantern hangs.
				BiomeWayLightMount mount = BiomeWayLights.MountOf(light);
				if (mount == BiomeWayLightMount.Wall)
				{
					Assert.That(piece.Collides, Is.False, $"{id} is brushed past");
					Bounds b = StructurePieces.Build(piece, 0, 0, Seed).Mesh.Bounds;
					Assert.That(b.max.z, Is.InRange(0f, 0.05f), $"{id}'s back plate is at z = 0");
					Assert.That(b.min.z, Is.LessThan(-0.2f), $"{id} stands out along −z");
				}
				else if (mount == BiomeWayLightMount.Roof)
				{
					Assert.That(piece.Anchor, Is.EqualTo(StructureAnchor.Roof));
				}
				else
				{
					Bounds b = StructurePieces.Build(piece, 0, 0, Seed).Mesh.Bounds;
					Assert.That(b.max.y, Is.GreaterThan(0.8f), $"{id} stands up from the ground");
				}
			}
			// The new lights' looks: an iron fire basket, a lantern on a crook, a glowjar's mote, a crystal's cradle, glowing runes.
			Assert.That(StructurePieces.Build(StructureKit.Get("Cresset"), 0, 0, Seed).Materials, Does.Contain(StructureMaterial.Iron).And.Member(StructureMaterial.Ash));
			Assert.That(StructurePieces.Build(StructureKit.Get("CrookLantern"), 0, 0, Seed).Materials, Does.Contain(StructureMaterial.Timber).And.Member(StructureMaterial.Lamplight));
			Assert.That(StructurePieces.Build(StructureKit.Get("Glowjar"), 0, 0, Seed).Materials, Does.Contain(StructureMaterial.Glass).And.Member(StructureMaterial.Faelight));
			Assert.That(StructurePieces.Build(StructureKit.Get("RuneStone"), 0, 0, Seed).Materials, Does.Contain(StructureMaterial.Monolith).And.Member(StructureMaterial.Runelight));
			Assert.That(StructurePieces.Build(StructureKit.Get("HangingBracket"), 0, 0, Seed).Materials, Does.Contain(StructureMaterial.Iron).And.Member(StructureMaterial.Lamplight));
		}

		/// <summary>The lights before 2026-10-16 (the street lights and the cave kit's lamps): every kind past them is I6c's.</summary>
		private const BiomeWayLightKind LastOldLight = BiomeWayLightKind.WallLantern;

		[Test]
		public void NewLights_JoinNoTemplateQuery_SoNoSitesPickMoves()
		{
			// A slot's piece is drawn by hash among its query's candidates: a new piece among them moves every pick the query makes.
			// The new lights are laid by id only (=Cresset), so no query a template or the template specs make may name one.
			var newLights = new HashSet<string>(((BiomeWayLightKind[])Enum.GetValues(typeof(BiomeWayLightKind)))
				.Where(l => l > LastOldLight).Select(BiomeWayLights.PieceId), StringComparer.Ordinal);
			// I6c's six and the suite's own (2026-10-26): every one laid by id only.
			Assert.That(newLights, Has.Count.EqualTo(BiomeWayLights.Count - 1 - (int)LastOldLight));
			var queries = new SortedSet<string>(StringComparer.Ordinal);
			foreach (System.Reflection.FieldInfo f in typeof(PointOfInterestTemplateSpecs).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
			{
				if (f.IsLiteral && f.FieldType == typeof(string))
				{
					queries.Add((string)f.GetRawConstantValue());
				}
			}
			int fromAssets = 0;
			foreach (string asset in Directory.GetFiles("Assets/Templates/World/PointsOfInterest", "*.asset", SearchOption.AllDirectories))
			{
				foreach (string line in File.ReadLines(asset))
				{
					string t = line.Trim();
					if (t.StartsWith("- Tag: ", StringComparison.Ordinal) || t.StartsWith("Tag: ", StringComparison.Ordinal))
					{
						queries.Add(t.Substring(t.IndexOf(':') + 1).Trim());
						fromAssets++;
					}
				}
			}
			Assert.That(fromAssets, Is.GreaterThan(100), "the templates' slots were read");
			Assert.That(queries, Does.Contain("fire"));
			foreach (string query in queries)
			{
				if (query.Length == 0 || query[0] == '=')
				{
					continue;
				}
				foreach (string style in new[] { null, "timber", "stone", "hide", "iron", "earth" })
				{
					foreach (StructurePiece p in StructureKitPieceSource.Candidates(query, style))
					{
						Assert.That(newLights.Contains(p.Id), Is.False, $"'{query}' ({style ?? "any style"}) now draws among {p.Id}");
					}
				}
			}
			// A camp's fire: the very pieces it drew among before (a cresset is no camp fire).
			Assert.That(StructureKitPieceSource.Candidates("fire", null).Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal),
				Is.EqualTo(new[] { "CampfireRing", "CharcoalClamp", "CookingTripod", "LimeKiln" }));
		}

		[Test]
		public void CrystalPost_HoldsItsBiomesCrystal_InItsCradle()
		{
			StructurePiece post = StructureKit.Get(StructurePieces.CrystalPostId);
			for (int v = 0; v < post.Variants; v++)
			{
				Assert.That(StructurePieces.TryCrystalSocket(post, v, out Vector3 socket, out float scale), Is.True);
				Bounds b = StructurePieces.Build(post, v, 0, Seed).Mesh.Bounds;
				Assert.That(socket.y, Is.InRange(b.max.y - 0.45f, b.max.y), $"v{v}: the crystal sits in the cradle at the top");
				Assert.That(Mathf.Abs(socket.x) + Mathf.Abs(socket.z), Is.LessThan(0.01f));
				Assert.That(scale, Is.InRange(0.2f, 0.6f));
				(Vector3 at, SceneEmitterKind kind) light = StructurePieces.EmittersOf(post, v).Single(e => e.kind == SceneEmitterKind.Glow);
				Assert.That(light.at.y, Is.GreaterThan(socket.y), "its light is the crystal's");
			}
			Assert.That(StructurePieces.TryCrystalSocket(StructureKit.Get("LanternPost"), 0, out _, out _), Is.False, "only the crystal post holds one");
			Assert.That(StructurePieces.TryCrystalSocket(post, post.Variants, out _, out _), Is.False);
			string site = Code("PointsOfInterest/PointOfInterestSiteContext.cs");
			Assert.That(site, Does.Contain("StructurePieces.TryCrystalSocket(piece, variant, out Vector3 socket, out float crystalScale)"), "every lay of a crystal post lays its crystal");
			Assert.That(site, Does.Contain("CaveMineral postMineral = cradled ? ProceduralArtCatalogue.CaveMineralOf(PostCrystal) : default;")
				.And.Contain("SceneGlow.WithCrystalHue(seed, cradled ? postMineral.LightHue : -1)"), "its glow takes the crystal's hue");
			Assert.That(site, Does.Contain("BiomeTemplateArt.PostCrystal("), "the crystal is the site biome's");
		}

		// ── Cave and mine lights on the template ──────────────────────

		[Test]
		public void CaveLights_DefaultsAreTheDressingsOldRule_ForEveryBiome()
		{
			// What the cave dressing laid before 2026-10-16, in every biome (copied from its code as it was).
			var old = new Dictionary<BiomeCaveLightPlace, (string piece, BiomeWayLightMount mount)>
			{
				[BiomeCaveLightPlace.Route] = ("LanternStand", BiomeWayLightMount.Ground),
				[BiomeCaveLightPlace.Camp] = ("LanternStand", BiomeWayLightMount.Ground),
				[BiomeCaveLightPlace.MineDrift] = ("WallTorch", BiomeWayLightMount.Wall),
				[BiomeCaveLightPlace.MineStope] = ("TorchPost", BiomeWayLightMount.Ground),
				[BiomeCaveLightPlace.MineLamp] = ("HangingLantern", BiomeWayLightMount.Roof),
			};
			Assert.That(Enum.GetValues(typeof(BiomeCaveLightPlace)).Length, Is.EqualTo(old.Count));
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				List<BiomeCaveLight> lights = entry.CaveLights();
				if (BiomeAuthoredRounds.Contains(entry.Biome))
				{
					// An authored round lights its own caves (legacy equality only): every light it names is a kind the kit has.
					foreach (BiomeCaveLight light in lights)
					{
						Assert.That(Enum.IsDefined(typeof(BiomeWayLightKind), light.Light), Is.True, $"{entry.Biome} {light.Place}");
					}
					continue;
				}
				Assert.That(lights.Select(l => l.ToString()), Is.EqualTo(BiomeCaveLights.Default().Select(l => l.ToString())), entry.Biome);
				foreach (KeyValuePair<BiomeCaveLightPlace, (string piece, BiomeWayLightMount mount)> place in old)
				{
					Assert.That(BiomeCaveLights.TryFor(lights, place.Key, out BiomeCaveLight light), Is.True, $"{entry.Biome} {place.Key} is lit");
					Assert.That(BiomeWayLights.PieceId(light.Light), Is.EqualTo(place.Value.piece), $"{entry.Biome} {place.Key}");
					Assert.That(BiomeWayLights.MountOf(light.Light), Is.EqualTo(place.Value.mount), $"{entry.Biome} {place.Key} is set as before");
				}
			}
			// A drift's torches every 14–20 m, exactly (the gap's bounds are exact in floats for the old 17 m mean).
			Assert.That(BiomeCaveLights.TryFor(BiomeCaveLights.Default(), BiomeCaveLightPlace.MineDrift, out BiomeCaveLight drift), Is.True);
			Assert.That(drift.Spacing, Is.EqualTo(17f));
			BiomeCaveLights.DriftGap(drift.Spacing, out float min, out float max);
			Assert.That(min, Is.EqualTo(14f));
			Assert.That(max, Is.EqualTo(20f));
			// The first entry for a place; a drift's spacing is read as at least 4 m (0 as the old 17); unlisted is unlit.
			var list = new List<BiomeCaveLight>
			{
				new BiomeCaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.HangingBracket, 1f),
				new BiomeCaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 17f),
				new BiomeCaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost),
			};
			Assert.That(BiomeCaveLights.TryFor(list, BiomeCaveLightPlace.MineDrift, out BiomeCaveLight first) && first.Light == BiomeWayLightKind.HangingBracket, Is.True);
			Assert.That(first.Spacing, Is.EqualTo(BiomeCaveLights.MinDriftSpacing));
			Assert.That(BiomeCaveLights.TryFor(new List<BiomeCaveLight> { new BiomeCaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch) }, BiomeCaveLightPlace.MineDrift, out BiomeCaveLight zero)
				&& zero.Spacing == BiomeCaveLights.DriftSpacing, Is.True);
			Assert.That(BiomeCaveLights.TryFor(list, BiomeCaveLightPlace.Camp, out _), Is.False, "a place not listed is not lit");
			Assert.That(BiomeCaveLights.TryFor(null, BiomeCaveLightPlace.Route, out _), Is.False);
			Assert.That(new BiomeCaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 17f).ToString(), Is.EqualTo("MineDrift=WallTorch@17"));
			Assert.That((int)BiomeCaveLightPlace.Route, Is.EqualTo(0));
			Assert.That((int)BiomeCaveLightPlace.MineLamp, Is.EqualTo(4));
		}

		[Test]
		public void CaveDressing_ReadsItsLightsAndCrystalsFromTheTemplate()
		{
			string main = Code("PointsOfInterest/Features/PointOfInterestCaveDressing.cs");
			string camps = Code("PointsOfInterest/Features/PointOfInterestCaveDressing.Camps.cs");
			string workings = Code("PointsOfInterest/Features/PointOfInterestCaveDressing.Workings.cs");
			string glow = Code("PointsOfInterest/Features/PointOfInterestCaveDressing.Glow.cs");
			string all = main + camps + workings + glow + Code("PointsOfInterest/Features/PointOfInterestCaveDressing.Ways.cs");
			// 2026-10-25 (I2n): through the cave's biomes (its host's lights, a linked interior's under the world's air).
			Assert.That(main, Does.Contain("CaveInteriorArt.CaveLight(in caves, place, breathable, out known.light)"));
			Assert.That(main, Does.Contain("CaveLight(BiomeCaveLightPlace.MineDrift, out BiomeCaveLight drift)"));
			Assert.That(main, Does.Contain("CaveLight(BiomeCaveLightPlace.MineStope, out BiomeCaveLight stopeLight)"));
			Assert.That(main, Does.Contain("nextLamp = along + rng.Range(gapMin, gapMax);"));
			Assert.That(camps, Does.Contain("CaveLight(BiomeCaveLightPlace.Route, out BiomeCaveLight route)"));
			Assert.That(camps, Does.Contain("CaveLight(BiomeCaveLightPlace.Camp, out BiomeCaveLight lamp)"));
			Assert.That(workings, Does.Contain("CaveLight(BiomeCaveLightPlace.MineLamp, out BiomeCaveLight lamp)"));
			// No light is laid by a fixed piece any more (the cave kit's own lamps are the template's defaults).
			foreach (string fixedLight in new[] { "Prefab(\"WallTorch\"", "Prefab(\"TorchPost\"", "Lay(CaveArt.LanternStand", "Lay(CaveArt.HangingLantern", "Lay(CaveArt.WallLantern" })
			{
				Assert.That(all, Does.Not.Contain(fixedLight), fixedLight);
			}
			// The chambers' deposits are the biome's crystals (as the shells are its rock), the ways' glow its glowing ones.
			Assert.That(main, Does.Contain("set.Crystals = CaveInteriorArt.Crystals(in caves);"));
			Assert.That(main, Does.Contain("CaveBiome caves = CaveInteriorArt.Of(context.Request, context.Record, system);"), "the cave's biomes: its host site's and its interior");
			// 2026-10-26 (NEW-CRYSTALS): in the look the biome grows the kind in.
			Assert.That(main, Does.Contain("FloorPiece(depositFoot - dir * 0.5f, () => Mineral(kind, BiomeCaveCrystals.LookOf(set.Crystals, kind)), 1f, rng.Range(0.8f, 1.6f), true, -dir, out Vector3 laid)"));
			Assert.That(main, Does.Contain("int variant = rng.Range(0, RockTypes.VariantCount);"), "a crystal's variant is drawn as a formation's");
			Assert.That(glow, Does.Contain("CaveGlowPalettes.For(system.Form, system.Seed, in caves)"));
			Assert.That(Code("PointsOfInterest/Features/CaveGlowPalette.cs"), Does.Contain("CaveInteriorArt.Crystals(in caves)"));
			Assert.That(glow, Does.Contain("if (!TryCrystal(kind, hue, variant, out CaveGlowArt art))"));
			string authoring = Code("ProceduralArt/BiomeArtAuthoring.cs");
			Assert.That(authoring, Does.Contain("BiomeTemplate.Authored.CaveCrystals, biome.CaveCrystals, entry.CaveCrystals()"));
			Assert.That(authoring, Does.Contain("BiomeTemplate.Authored.CaveLights, biome.CaveLights, entry.CaveLights(glowCaves)"));
			string props = Code("PointsOfInterest/PropsFeature.cs");
			Assert.That(props, Does.Contain("BiomeWayLights.MountOf(light.Light) == BiomeWayLightMount.Ground"), "a street light stands on the ground");
		}

		// ── The structure palette's leftovers (I6a) ───────────────────

		[Test]
		public void Decks_AreFloors_TheTurfRidgeIsTrim_AndHayAndBoneAreNeverAPart()
		{
			// Every deck's planks are its Floor (the stringers and posts its Trim), at every level of every variant.
			foreach (string id in new[] { "Pier", "BridgeSpan", "RopeBridgeSpan", "TrestleSpan", StructurePieces.RopeBridgeId(16f) })
			{
				StructurePiece piece = StructureKit.Get(id);
				Assert.That(piece, Is.Not.Null, id);
				for (int v = 0; v < piece.Variants; v++)
				{
					for (int lod = 0; lod < StructurePieces.LevelCount; lod++)
					{
						List<StructureSolid> solids = StructurePieces.BuildSolids(piece, v, lod, Seed);
						Assert.That(solids.Exists(s => s.Part == BiomeStructureRole.Floor && s.Material == StructureMaterial.Timber), Is.True, $"{id}_{v} L{lod}'s deck is its floor");
						// The deck is the solids at the walking surface (their top within a few centimetres of y = 0).
						foreach (StructureSolid s in solids.Where(s => s.Part == BiomeStructureRole.Floor))
						{
							float sag = id == StructurePieces.RopeBridgeId(16f) ? 16f / (v == 0 ? 30f : 24f) : 0f;
							float deck = -sag * (1f - 4f * s.Bounds.center.z * s.Bounds.center.z / (16f * 16f));
							Assert.That(s.Bounds.max.y, Is.InRange(-sag - 0.12f, 0.06f), $"{id}_{v} L{lod}: a floor solid off the deck");
							Assert.That(s.Bounds.center.y, Is.EqualTo(deck).Within(0.12f), $"{id}_{v} L{lod}: floor follows the walking surface");
						}
					}
				}
			}
			// The turf longhouse's ridge beam is the frame's timber: no shingle roof on a turf house.
			StructurePiece longhouse = StructureKit.Get("Longhouse");
			List<StructureSolid> turf = StructurePieces.BuildSolids(longhouse, 1, 0, Seed);
			Assert.That(turf.Exists(s => s.Part == BiomeStructureRole.ShingleRoof), Is.False, "the turf longhouse plays no shingle roof");
			Assert.That(turf.Exists(s => s.Part == BiomeStructureRole.Turf), Is.True);
			// A thatched house's ridge is still its thatch, a shingled one's still its shingles.
			Assert.That(StructurePieces.BuildSolids(longhouse, 0, 0, Seed).Exists(s => s.Part == BiomeStructureRole.Roof), Is.True);
			// Hay and straw goods, and bone anywhere, are what they are in every biome: never a part.
			foreach (bool fabric in new[] { true, false })
			{
				Assert.That(StructureParts.Defaults(fabric, StructureMaterial.Bone), Is.Empty, $"bone (fabric {fabric})");
				foreach (StructureStyle style in (StructureStyle[])Enum.GetValues(typeof(StructureStyle)))
				{
					foreach (BiomeStructureRole role in (BiomeStructureRole[])Enum.GetValues(typeof(BiomeStructureRole)))
					{
						Assert.That(StructureParts.Fits(style, role, StructureMaterial.Bone), Is.False, $"no role is bone in {style}");
					}
				}
			}
			Assert.That(StructureParts.Defaults(false, StructureMaterial.Thatch), Is.Empty, "hay bales, straw beds, a skep's straw");
			foreach (string goods in new[] { "HayBales", "StrawBed", "BonePile" })
			{
				foreach (StructureSolid s in StructurePieces.BuildSolids(StructureKit.Get(goods), 0, 0, Seed))
				{
					Assert.That(s.Material == StructureMaterial.Thatch || s.Material == StructureMaterial.Bone ? s.Part : null, Is.Null, $"{goods}: its {s.Material}");
				}
			}
			// A building of bone is the palette's: a biome's walls in bone-work lay every wall-part submesh in it.
			BiomeStructurePalette bone = BiomeStructurePalette.Kit();
			for (int i = 0; i < bone.Surfaces.Count; i++)
			{
				if (bone.Surfaces[i].Role == BiomeStructureRole.Wall)
				{
					BiomeStructureSurface s = bone.Surfaces[i];
					s.Material = BiomeStructureMaterial.BoneWork;
					s.Tint = Color.white;
					bone.Surfaces[i] = s;
				}
			}
			StructureSurfaceKey key = StructurePalettes.Resolve(bone, StructureStyle.Timber, new StructureSlot(StructureMaterial.Plaster, BiomeStructureRole.Wall));
			Assert.That(key.Material, Is.EqualTo(StructureMaterial.BoneWork));
		}
	}
}
