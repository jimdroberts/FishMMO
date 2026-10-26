using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase NEW-ROCK-TYPES of the biome suite (2026-10-25, decision 34): every rock type, shape and stone set the 83 drafts
	/// name resolves in the catalogue; the biomes' crust rocks are cliffs with their own stones; the formation-only rocks never
	/// are; the soluble set takes coral rag and refuses salt rock, alunite, caliche and laterite; the volcanic cave's molten set
	/// (conflict C14) exists; nothing that was there moved (append-only). Pure: no Unity object is made.
	/// </summary>
	public class BiomeRegionalRockTests
	{
		/// <summary>The rocks the drafts add, with the shapes they name (a draft's "Slab" is "Slabs", its "Spire" a "Pinnacle" or "Tower").</summary>
		private static readonly (string Rock, bool FormationsOnly, string[] Shapes)[] DraftRocks =
		{
			("Rhyolite", false, new[] { "FlowBanded", "Spine", "AlteredKnoll", "Scree" }),
			("Trachyte", false, new[] { "Columns", "Platy", "RimeMantle", "Scree" }),
			("Dacite", false, new[] { "Platy", "Pinnacle", "Ledges", "FlowBand", "Scree", "Boulder" }),
			("Komatiite", false, new[] { "Block", "Fallen", "Raft", "Toe", "PressureRidge", "DribletSpire", "Scree" }),
			("Dolomite", false, new[] { "Tower", "Gendarme", "Block", "Scree", "RockGlacier", "Rimed" }),
			("Flysch", false, new[] { "Ribs", "SlabField", "Tor", "Scree" }),
			("Beachrock", false, new[] { "Pavement", "Bench", "Slabs" }),
			("Dolerite", false, new[] { "Ridge", "Gully", "Pinnacle", "BoulderField" }),
			("Coralstone", false, new[] { "Bommie", "Microatoll", "Spur", "StormBlock" }),
			("Suevite", false, new[] { "Megablock", "Flap", "Scree", "MeltLobe" }),
			("Cryolava", false, new[] { "FlowFront", "Tumulus", "Raft", "Block", "SpatterRampart", "Scree", "HydrateBlock" }),
			("Natrite", true, new[] { "FaculaMound", "SaltSpire" }),
			("Marl", false, new[] { "CanyonRib", "SlumpBlock", "RaftedBlock", "Scree" }),
			("Phosphorite", true, new[] { "Knob" }),
			("Duststone", false, new[] { "Plates", "Clod", "Hummock", "LaminaScarp", "Scree" }),
			("Till", false, new[] { "Kliff", "Slump", "Lag", "Findling" }),
			("Travertine", false, new[] { "Curtain", "Rimstone", "Block", "Ledges", "AbbeChimney" }),
			("Greenstone", false, new[] { "Whaleback", "Plucked", "Block", "Scree" }),
			("Loess", false, new[] { "Bluff", "Pillar", "Bank" }),
			("Charnockite", false, new[] { "Tor", "FellBlock", "Ledges", "Scree", "Boulder", "Windstone" }),
			("Troctolite", false, new[] { "Massif", "RookKnob", "Block", "ShockSlab", "Perched", "Scree" }),
			("Laterite", false, new[] { "Breakaway", "Pavement", "Block" }),
			("Molasse", false, new[] { "Ledges", "Tafoni", "Bluff" }),
			("CoralRag", false, new[] { "Champignon", "Lapiaz", "Notch" }),
			("Cryokarst", false, new[] { "Rampart", "FlutedPinnacle", "LabyrinthMesa", "SolutionBlock", "Scree" }),
			("Adamant", true, new[] { "Crag", "Block" }),
			("Phonolite", false, new[] { "Plug", "Organ", "Clinkslabs", "Rind", "Ledges", "Scree" }),
			("RaftIce", false, new[] { "Block", "Montes", "TiltSlab", "Scree" }),
			("NitrogenIce", true, new[] { "PitRim", "CellDome", "FlowLobe", "Hummock" }),
			("MethaneIce", true, new[] { "Blade", "BladeRow", "Bladelet" }),
			("Mudstone", false, new[] { "SlopeLedge", "Block", "Scree" }),
			("Moundstone", true, new[] { "MoundCore", "Slabs" }),
			("Redbed", false, new[] { "Butte", "Rimrock", "Slump", "Scree" }),
			("SableIce", false, new[] { "Scarp", "SoftRim", "Block", "Scree" }),
			("Norite", false, new[] { "Boulder", "Split", "Block", "Ledges", "Scree" }),
			("Variolite", false, new[] { "Boulder", "Block", "FaultBlock", "Knob", "Scree" }),
			("Greywacke", false, new[] { "Ribs", "Bluff", "Slabs", "Midstream", "Reef" }),
			("Flagrock", false, new[] { "Stack", "Ledges", "Block", "Flags", "Scree" }),
			("Hornfels", false, new[] { "Comb", "Pinnacle", "Block", "Split", "Scree" }),
			("Platebasalt", false, new[] { "Slabs", "PlateStack", "Block", "FlowFront", "Blister", "Scree" }),
			("Carbonatite", true, new[] { "LeveeBlock", "LeveeSlab" }),
			("Saltrock", false, new[] { "Bedded", "Fluted", "Diapir", "Scree" }),
			("Granodiorite", false, new[] { "Block", "Sheet", "Pinnacle", "Talus", "StreamBlocks", "Rocker" }),
			("Caliche", false, new[] { "Bench", "Plate", "Scree" }),
			("Hyaloclastite", false, new[] { "BrecciaLedge", "Buttress", "Scree" }),
			("Guyotstone", true, new[] { "CapLedge", "Slabs", "NotchBlock" }),
			("Eclogite", false, new[] { "Knoll", "Block", "Boulder", "Perched", "Scree" }),
			("Porphyry", false, new[] { "Tent", "Knob", "BlockField", "Split", "Scree" }),
			("MarineIce", false, new[] { "Scallop", "MeltFlute", "Ledges", "Shelf", "Accretion" }),
			("Anhydrite", true, new[] { "WhiteSmoker", "ChampagneCone" }),
			("VentCarbonate", true, new[] { "Tower" }),
			("Alunite", false, new[] { "Ledges", "Knoll", "Scree" }),
			("Tessera", false, new[] { "Fin", "RhombBlock", "RidgeComb", "GrabenStep", "Honeycomb", "Scree" }),
			("Leadfrost", true, new[] { "FrostComb", "FrostCrown" }),
			("Nenfro", false, new[] { "Block", "Ledges", "Boss", "Facade" }),
			("Hazestone", false, new[] { "Hummock", "Knuckle", "SandTailBlock", "GroovedTor", "Scree" }),
			("Duneslab", true, new[] { "SlabPavement", "CrossbedLedge", "Stack" }),
			("BrineIce", false, new[] { "DoubleRidge", "Raft", "Cusp", "Block", "Scree" }),
			("Porcelanite", false, new[] { "WallLedge", "Concretion", "Block", "Scree" }),
			("Oysterbank", true, new[] { "Curtain", "Bank" }),
			("Serpentinite", false, new[] { "Knocker", "ScaleCrag", "Crag", "Scree" }),
			("Moraine", true, new[] { "EarthPyramid", "Erratic" }),
			("Peridotite", false, new[] { "Ledges", "Bluff", "Buttress" }),
			("Chemoherm", true, new[] { "Mound" }),
			("Radiolarite", false, new[] { "Ribs", "Scarp", "Scree" }),
			("Mangancrust", true, new[] { "Knoll" }),
			("Carstone", false, new[] { "ScarpLedge", "BoxworkBlock", "PanSlab", "Scree", "Dogger", "BoxworkTor" }),
			("SulcusIce", false, new[] { "StripeFlank", "VentCone", "PlumeBlock", "Shard", "Scree" }),
			("Permafrost", true, new[] { "Pingo", "Palsa", "Polygons", "Lobe", "ThawSlump", "Thufur" }),
			("Sarsen", true, new[] { "Wether", "Stone" }),
		};

		/// <summary>The new shapes the drafts put on rocks that were already there.</summary>
		private static readonly (string Rock, string[] Shapes)[] DraftShapesOnOldRocks =
		{
			("Granite", new[] { "Bornhardt", "Whaleback", "Matopo", "BalancingStack", "SheetSpall" }),
			("Basalt", new[] { "Split", "Knob", "ShatterCone", "DribletCairn" }),
			("Andesite", new[] { "Byobu", "Iwakura", "Tateishi" }),
			("Obsidian", new[] { "Ridge" }),
			("Tuff", new[] { "Capped", "Cone", "Rib", "Citadel", "Spall" }),
			("Sandstone", new[] { "Dome", "Fin", "CrossBedded", "Yardang", "Gour" }),
			("Limestone", new[] { "Blade", "Tower", "Notch", "Gongshi", "Shelfstone" }),
			("Conglomerate", new[] { "Crone", "StoneSheep", "CeppoLedge" }),
			("Slate", new[] { "CleavageFin", "TalusCone" }),
			("Gneiss", new[] { "Felsenmeer", "FrostJacked" }),
			("Gypsum", new[] { "Beam" }),
			("Sulphur", new[] { "Flow", "Terrace", "Blister", "BipyramidDruse" }),
			("Sinter", new[] { "Shield", "PoolRim" }),
			("Breccia", new[] { "HouseBlock", "CoolingPlate" }),
			("Sulphide", new[] { "Pillar", "Beehive", "Toppled" }),
			("DarkLag", new[] { "Knoblet" }),
			("Firn", new[] { "Cornice", "SnowMushroom", "DriftTail", "WaveDune" }),
			("WaterIce", new[] { "Column", "Icefall", "FrozenBlocks", "Candles", "IceTower", "StrataBlock", "Scalloped" }),
		};

		/// <summary>The shapes every old rock had before this phase, in their order: new ones are only ever appended.</summary>
		private static readonly (string Rock, string[] Shapes)[] OldShapes =
		{
			("Granite", new[] { "Corestone", "Tor", "Perched", "Split" }),
			("Basalt", new[] { "Columns", "Causeway", "Fallen", "Block", "Ledges", "Scree", "Slabs", "Fin", "Dribble", "Ventifact" }),
			("Andesite", new[] { "Block", "Platy", "Talus" }),
			("Limestone", new[] { "Block", "Pinnacle", "Pavement", "Stalagmite", "Flowstone", "Gours" }),
			("Sulphide", new[] { "Smoker" }),
			("WaterIce", new[] { "Stalagmite", "Crystal", "FrostFlowers", "Cone", "Lobe", "GlacierTable", "DirtCone", "Ridge" }),
		};

		private static readonly string[] OldTypes =
		{
			"Granite", "Basalt", "Andesite", "Obsidian", "Pumice", "Tuff", "Sandstone", "Limestone", "Shale", "Conglomerate", "Chalk",
			"Slate", "Schist", "Gneiss", "Marble", "Quartzite",
			"Halite", "Gypsum", "Sulphur", "Sinter", "Scoria", "Breccia", "Anorthosite", "Sulphide", "Pyrite", "DarkLag", "Termitaria", "Firn", "WaterIce",
		};

		[Test]
		public void EveryDraftNamedRockAndShape_Resolves()
		{
			var missing = new List<string>();
			foreach ((string rock, bool formationsOnly, string[] shapes) in DraftRocks)
			{
				if (!RockTypes.TryGet(rock, out RockType type))
				{
					missing.Add(rock);
					continue;
				}
				Assert.That(type.FormationsOnly, Is.EqualTo(formationsOnly), $"{rock}: crust or formation-only");
				Assert.That(type.Summary, Is.Not.Empty, rock);
				foreach (string shape in shapes)
				{
					if (!RockTypes.TryShape(in type, shape, out _))
					{
						missing.Add($"{rock}/{shape}");
					}
				}
			}
			foreach ((string rock, string[] shapes) in DraftShapesOnOldRocks)
			{
				Assert.That(RockTypes.TryGet(rock, out RockType type), Is.True, rock);
				missing.AddRange(shapes.Where(s => !RockTypes.TryShape(in type, s, out _)).Select(s => $"{rock}/{s}"));
			}
			Assert.That(missing, Is.Empty);
			// Every regional rock is one the drafts asked for, and the lists hold no stragglers.
			var asked = new HashSet<string>(DraftRocks.Select(d => d.Rock));
			Assert.That(RegionalRocks.Crust.Concat(RegionalRocks.FormationRocks).Select(t => t.Name), Is.EquivalentTo(asked));
		}

		[Test]
		public void TheVolcanicCavesMoltenSet_Exists_AsCrustRocks()
		{
			// Conflict C14: the molten-shell set names these; they must exist before the cave phase lands.
			foreach (string rock in new[] { "Basalt", "Komatiite", "Andesite", "Tuff", "Rhyolite", "Trachyte", "Dacite" })
			{
				Assert.That(CliffRocks.IsCliffRock(rock), Is.True, rock);
			}
			Assert.That(RegionalRocks.Crust.Take(4).Select(t => t.Name), Is.EqualTo(new[] { "Rhyolite", "Trachyte", "Dacite", "Komatiite" }), "the molten set first");
		}

		[Test]
		public void EveryRegionalCrustRock_IsACliff_WithItsOwnStones()
		{
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			var materials = new HashSet<string>(ProceduralArtCatalogue.RockMaterialNames());
			foreach (RockType type in RegionalRocks.Crust)
			{
				string name = type.Name;
				Assert.That(type.FormationsOnly, Is.False, name);
				Assert.That(RockTypes.Crust, Has.Some.Matches<RockType>(t => t.Name == name), name);
				Assert.That(CliffRocks.IsCliffRock(name), Is.True, name);
				Assert.That(CliffRocks.MaterialName(name), Is.EqualTo(ProceduralArtCatalogue.RockMaterial(name)), name);
				Assert.That(materials, Does.Contain(ProceduralArtCatalogue.RockMaterial(name)), $"{name}: its material is written");
				// Decision 6: its own stones, not granite's shapes, wearing its own material; a river on it drops them.
				Assert.That(StoneStyles.Has(name), Is.True, $"{name} has stone shapes of its own");
				RockMaterialSpec stone = ProceduralArtCatalogue.AllRockMaterials.Single(m => m.Name == name);
				Assert.That(stone.WearsRockType, Is.EqualTo(name), name);
				Assert.That(RiverBoulders.Material(name), Is.EqualTo(name), $"{name}'s river boulders");
				foreach (RockShape slot in ProceduralArtCatalogue.BoulderShapes)
				{
					Assert.That(prefabs, Does.Contain(ProceduralArtCatalogue.BoulderPrefab(name, slot.Name)), name);
				}
				Assert.That(prefabs, Does.Contain(ProceduralArtCatalogue.SmallRocksPrefab(name)), name);
				Assert.That(prefabs, Does.Contain(ProceduralArtCatalogue.PebblesPrefab(name)), name);
				Assert.That(RegionalRocks.MossAmount(name), Is.Not.Null.And.InRange(0f, 1f), $"{name}: a moss amount of its own");
			}
		}

		[Test]
		public void RegionalFormationRocks_AreNeverCliffsOrStones()
		{
			var stones = new HashSet<string>(ProceduralArtCatalogue.AllRockMaterials.Select(m => m.Name));
			foreach (RockType type in RegionalRocks.FormationRocks)
			{
				Assert.That(type.FormationsOnly, Is.True, type.Name);
				Assert.That(CliffRocks.IsCliffRock(type.Name), Is.False, type.Name);
				// Unless a draft asks for its stones by name (2026-10-26, I6f: Tholin Plain's Duneslab, StoneStyles.Named.cs).
				Assert.That(stones.Contains(type.Name), Is.EqualTo(StoneStyles.NamedStoneNames.Contains(type.Name)), $"{type.Name} is formations only, with no stone set but a named one");
				Assert.That(RockTypes.FormationRocks, Has.Some.Matches<RockType>(t => t.Name == type.Name), type.Name);
				Assert.That(new[] { "Ice", "Nodule", "Coral", "Flint", "Cobble" }, Does.Not.Contain(type.Name), "Rock_ names of small-rock materials");
			}
		}

		[Test]
		public void EveryShapesDressAndCap_IsAMaterialThatIsWritten()
		{
			var materials = new HashSet<string>(ProceduralArtCatalogue.RockMaterialNames().Concat(ProceduralArtCatalogue.RockMaterials.Select(m => ProceduralArtCatalogue.RockMaterial(m.Name))));
			foreach ((RockType type, FormationShape shape, int variant) in RockArtNames.AllFormations())
			{
				if (variant != 0)
				{
					continue;
				}
				foreach (string material in RockArtNames.FormationMaterials(in type, in shape))
				{
					Assert.That(materials, Does.Contain(material), $"{type.Name}/{shape.Name} wears {material}");
				}
			}
			foreach (string dress in new[] { IceSurfaces.AmmoniaHydrate, IceSurfaces.Accreted, IceSurfaces.StripeBlue, IceSurfaces.Stratified, IceSurfaces.Scalloped })
			{
				Assert.That(IceSurfaces.TryGet(dress, out _), Is.True, dress);
				Assert.That(IceSurfaces.Materials.Any(m => m.Surface == dress), Is.True, dress);
			}
		}

		[Test]
		public void TheSolubleSet_TakesCoralRag_AndKeepsTheDraftsRefusals()
		{
			List<string> cave = BiomeCaveInteriors.DefaultInteriorRocks("Cave");
			foreach (string rock in new[] { "CoralRag", "Travertine", "Dolomite", "Limestone" })
			{
				Assert.That(BiomeCaveInteriors.Holds(BiomeCaveForm.Cave, cave, rock), Is.True, rock);
				Assert.That(RockTypes.TryGet(rock, out _), Is.True, $"{rock} is a rock type");
			}
			// Saltrock (no Cave link), Alunite (not in the set), Caliche (not soluble-linked), Laterite (pseudokarst), and the ice crusts.
			foreach (string rock in new[] { "Saltrock", "Alunite", "Caliche", "Laterite", "Cryokarst", "Coralstone", "Porcelanite" })
			{
				Assert.That(BiomeCaveInteriors.Holds(BiomeCaveForm.Cave, cave, rock), Is.False, rock);
			}
		}

		[Test]
		public void NothingThatWasThere_Moved()
		{
			// Append-only: the first twenty-nine types keep their places, every old shape its place in its type's list.
			Assert.That(RockTypes.Crust.Take(16).Select(t => t.Name), Is.EqualTo(OldTypes.Take(16)));
			Assert.That(RockTypes.FormationRocks.Take(13).Select(t => t.Name), Is.EqualTo(OldTypes.Skip(16)));
			foreach ((string rock, string[] shapes) in OldShapes)
			{
				RockTypes.TryGet(rock, out RockType type);
				Assert.That(type.Shapes.Take(shapes.Length).Select(s => s.Name), Is.EqualTo(shapes), rock);
			}
			// The soluble set kept its six in order and appended.
			Assert.That(BiomeCaveInteriors.SolubleRocks.Take(6), Is.EqualTo(new[] { "Limestone", "Chalk", "Marble", "Gypsum", "Travertine", "Dolomite" }));
		}

		[Test]
		public void TheNewSources_AreHashed_InTheRockFamilies()
		{
			foreach (string file in new[] { "RegionalRocks.cs", "StoneStyles.Regional.cs" })
			{
				Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain(file), file);
				Assert.That(ProceduralArtFamilies.FamiliesOfFile(file), Does.Contain(ArtFamily.Rocks), file);
			}
		}
	}
}
