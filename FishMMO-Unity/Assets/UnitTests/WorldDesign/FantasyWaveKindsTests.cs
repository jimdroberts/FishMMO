using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The point-of-interest fantasy wave (2026-10-27, memory poi-fantasy-wave/WAVE.md): 121 kinds appended after the hot ground's,
	/// each with its map marker, kind facts, six or more short type words and a placed catalogue row hosted by no biome by default
	/// (a kind stands only where a biome's .Sites names it); hostile kinds get a monster pack, great lairs an empty boss spawner
	/// from medium size, and every wave kind's marker has its group's map style. Pure.
	/// </summary>
	[TestFixture]
	public class FantasyWaveKindsTests
	{
		/// <summary>The wave's kinds in the order they were appended.</summary>
		internal static readonly string[] Wave =
		{
			"DragonLair", "TrollBridge", "GiantsSteading", "GoblinWarren", "HauntedManor", "HangingTree", "BuriedHoard", "AmbushSite",
			"WreckedCaravan", "Crossroads", "BoundaryStone", "SignalBeacon", "TollGate", "Hamlet", "ManorHouse", "SiegeCamp",
			"BurnedVillage", "WarbandCamp", "CultistHideout", "AncientWall", "SpellScar", "PlanarRift", "PetrifiedBattle", "TitansBones",
			"WishingStone", "ElderMonolith", "WaygateRing", "GolemGraveyard", "KoboldDelving", "LostLibrary", "DruidGrove", "TreeVillage",
			"TrollStones", "SpiderThicket", "CharcoalClamps", "GreenwoodHideout", "DryadOak", "GoblinMarket", "ElfWaystone", "WolfDen",
			"MossColossus", "StepPyramid", "Cenote", "JaguarTemple", "SerpentIdol", "LostCity", "LizardfolkVillage", "IdolVault",
			"Grange", "Dovecote", "BeeGarden", "VillageGreen", "ScarecrowField", "LongBarrow", "Henge", "GallowsHill", "KurganField",
			"NomadCamp", "StoneFigures", "Kraal", "Baobab", "Shieling", "Necropolis", "Pyramid", "Sphinx", "RockCutFacade",
			"SandBuriedTemple", "DjinnPrison", "SandwormPit", "CaravanHalt", "BeastBoneyard", "CliffPalace", "DreadRoad", "DwarvenGate",
			"MountainKingsHall", "HangingMonastery", "PassFort", "Eyrie", "GriffinRoost", "WyvernNest", "CliffRelief", "SummitCairn",
			"PrayerFlagPass", "SilverMineTown", "FrostGiantSteading", "RuneStone", "FrozenArmy", "DragonInTheIce", "MammothGraveyard",
			"WinterWolfDen", "AuroraShrine", "LichSpire", "IcePalace", "FrozenShipwreck", "Crannog", "HagsCauldron", "HydraLair",
			"ReedVillage", "CovenCircle", "LizardfolkHatchery", "SwampCathedral", "TidalIslandAbbey", "CliffCastle", "SmugglersTunnel",
			"WreckersBeacon", "SirenRock", "SeaWitchGrotto", "LongshipLanding", "KrakenWreck", "FireTemple", "FireGiantHall",
			"SalamanderLair", "AshBuriedCity", "MerfolkVillage", "KrakenGrounds", "LadyOfTheLake", "NymphPool", "LakeMonsterLair",
			"Commandery", "LostThrone", "RoyalMausoleum",
		};

		internal static IEnumerable<POIType> Kinds() => Wave.Select(n => (POIType)Enum.Parse(typeof(POIType), n));

		[Test]
		public void EveryWaveKind_IsAppendedInOrder_WithItsMarkerFactsWordsAndRow()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			int last = (int)POIType.SulphurPool;
			foreach (string name in Wave)
			{
				Assert.That(Enum.TryParse(name, false, out POIType kind), Is.True, $"{name} is a POIType");
				Assert.That((int)kind, Is.EqualTo(last + 1), $"{name} follows the kind before it");
				last = (int)kind;
				Assert.That(Enum.TryParse(name, false, out MapMarkerType marker), Is.True, $"{name} has its map marker type");
				Assert.That(PointOfInterestKinds.MarkerFor(kind), Is.EqualTo(marker));
				Assert.That(PointOfInterestKinds.IsKnown(kind), Is.True, $"{name} has its kind facts");
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(kind);
				Assert.That(info.Has(PointOfInterestTraits.Structure) || info.Has(PointOfInterestTraits.Terrain), Is.True, $"{name} is built or carved");
				Assert.That(info.Has(PointOfInterestTraits.Detected), Is.False, $"{name} is placed, not found");
				string[] words = PlaceNameDefaults.TypeWordsFor(kind);
				Assert.That(words.Length, Is.GreaterThanOrEqualTo(6), $"{name} has its type words");
				Assert.That(words.Distinct().Count(), Is.EqualTo(words.Length), $"{name}: no word twice");
				foreach (string word in words)
				{
					Assert.That(word.Length, Is.LessThanOrEqualTo(16), $"{name}: '{word}' leaves room in a 40-letter name");
				}
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.That(rule, Is.Not.Null, $"{name} has a catalogue row");
				Assert.That(rule.Placement, Is.EqualTo(PointOfInterestPlacement.Placed), name);
				Assert.That(rule.PerKm2, Is.GreaterThan(0f), name);
				Assert.That(rule.Max, Is.GreaterThan(0), name);
				Assert.That(rule.DefaultBiomeWeight, Is.EqualTo(0f), $"{name}: hosted by no biome by default");
				Assert.That(rule.Biomes, Is.Empty, $"{name}: its biomes are the .Sites lists that name it");
			}
			Assert.That(Enum.GetValues(typeof(POIType)).Cast<int>().Max(), Is.EqualTo((int)POIType.RoyalMausoleum), "nothing appended after the wave");
			Assert.That(Enum.GetValues(typeof(MapMarkerType)).Cast<MapMarkerType>().Count(t => Wave.Contains(t.ToString())), Is.EqualTo(Wave.Length));
		}

		/// <summary>A hostile wave kind holds a monster pack; a great lair adds an empty boss spawner from medium size, after the pack.</summary>
		[Test]
		public void HostileKindsHoldAPack_AndGreatLairsAnEmptyBossAfterIt()
		{
			var bosses = new HashSet<POIType>(new[] { POIType.DragonLair, POIType.SandwormPit, POIType.FrostGiantSteading, POIType.DragonInTheIce, POIType.LichSpire, POIType.HydraLair, POIType.FireGiantHall, POIType.KrakenGrounds, POIType.LakeMonsterLair });
			foreach (POIType kind in Kinds())
			{
				List<PointOfInterestFeature> small = PointOfInterestFeatureDefaults.For(kind, 0);
				List<PointOfInterestFeature> medium = PointOfInterestFeatureDefaults.For(kind, 1);
				bool hostile = PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Hostile);
				Assert.That(medium.OfType<NpcSpawnerFeature>().Any(f => f.Role == PointOfInterestNpcRole.MonsterPack), Is.EqualTo(hostile), $"{kind}: a pack when hostile");
				Assert.That(small.OfType<BossSpawnerFeature>().Any(), Is.False, $"{kind}: no boss at the small size");
				int boss = medium.FindIndex(f => f is BossSpawnerFeature);
				Assert.That(boss >= 0, Is.EqualTo(bosses.Contains(kind)), $"{kind}: a boss spawner only in a great lair");
				if (boss >= 0)
				{
					Assert.That(medium.FindIndex(f => f is NpcSpawnerFeature n && n.Role == PointOfInterestNpcRole.MonsterPack), Is.LessThan(boss), $"{kind}: pack before boss");
				}
				Assert.That(medium.OfType<RegionFeature>().Count(), Is.EqualTo(1), $"{kind}: one name-toast region");
				Assert.That(medium.OfType<ExplorationFeature>().Count(), Is.EqualTo(1), $"{kind}: discovered");
			}
		}

		/// <summary>Every wave kind's marker type is listed in its own group's map rule (UIMapPOI.uss is generated from the kind table).</summary>
		[Test]
		public void EveryWaveMarker_IsStyledByItsGroupsRule()
		{
			string uss = System.IO.File.ReadAllText("Assets/Scripts/Client/GUI/World/Map/UIMapPOI.uss");
			foreach (POIType kind in Kinds())
			{
				string selector = ".map-marker--" + kind.ToString().ToLowerInvariant() + " ";
				Assert.That(uss.Contains(selector), Is.True, $"{kind} has a map style rule");
			}
		}
	}
}
