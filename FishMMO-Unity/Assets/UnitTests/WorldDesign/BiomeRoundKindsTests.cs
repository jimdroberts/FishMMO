using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The point-of-interest kinds the biome rounds named (2026-10-25, phase NEW-POI-KINDS): each exists with its map marker,
	/// kind facts, words and catalogue row; no biome hosts any until its round says so, so no scene gains or loses a site; the
	/// lived-in ones need breathable air and the wet ones liquid water by default; Wasteland left the arid family; and the
	/// detected kinds' biome lists are only defaults, a template that hosts the kind finding it. Pure but for one template test.
	/// </summary>
	[TestFixture]
	public class BiomeRoundKindsTests
	{
		/// <summary>Every kind a draft's approved section named that the catalogue lacked, by draft.</summary>
		private static readonly string[] Named =
		{
			// Underwater Canyon (92).
			"TrenchEdge", "SeaCavern",
			// Volcanic (93).
			"CinderCone", "Kipuka", "HoluaSlide", "LavaFall",
			// Volcanic Temple (95).
			"AncientForge", "PhoenixNest", "LavaCaveTemple",
			// Swamp Ruins (87).
			"MireGate", "SunkenPlaza", "MarshWatchtower", "ShatteredBridge", "FallenColossus", "SealedVault", "DrownedForest",
			// Swamp Temple (88).
			"FrogShrine", "FloatingIsland", "FowlersHide", "EelWeir", "BrickKiln",
			// Wasteland (96).
			"BlightedCrater", "Chapel", "HolyWell", "PlagueVillage", "DeadForest", "WarEngineGraveyard",
			// Abyssal Plain (Q4: the floor's whale fall) and Subsurface Ocean Vent (83: the sea hall).
			"SunkenWhaleFall", "SeaHall",
		};

		private static IEnumerable<POIType> Kinds() => Named.Select(n => (POIType)Enum.Parse(typeof(POIType), n));

		[Test]
		public void EveryNamedKind_Exists_Appended_WithItsMarkerFactsWordsAndRow()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			int last = (int)POIType.WhaleTow;
			foreach (string name in Named)
			{
				Assert.That(Enum.TryParse(name, false, out POIType kind), Is.True, $"{name} is a POIType");
				Assert.That((int)kind, Is.GreaterThan(last), $"{name} is appended after the I2p kinds, in the order named");
				last = (int)kind;
				Assert.That(Enum.TryParse(name, false, out MapMarkerType marker), Is.True, $"{name} has its map marker type");
				Assert.That(PointOfInterestKinds.MarkerFor(kind), Is.EqualTo(marker));
				Assert.That(PointOfInterestKinds.IsKnown(kind), Is.True, $"{name} has its kind facts");
				Assert.That(PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Detected), Is.False, $"{name} is placed, not found");
				Assert.That(PlaceNameDefaults.TypeWordsFor(kind).Length, Is.GreaterThanOrEqualTo(6), $"{name} has its type words");
				foreach (string word in PlaceNameDefaults.TypeWordsFor(kind))
				{
					Assert.That(word.Length, Is.LessThanOrEqualTo(16), $"{name}: '{word}' leaves room in a 40-letter name");
				}
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.That(rule, Is.Not.Null, $"{name} has a catalogue row");
				Assert.That(rule.Placement, Is.EqualTo(PointOfInterestPlacement.Placed), name);
				Assert.That(rule.PerKm2, Is.GreaterThan(0f), name);
				Assert.That(rule.Max, Is.GreaterThan(0), name);
			}
			Assert.That(Enum.GetValues(typeof(POIType)).Cast<int>().Max(), Is.EqualTo(last), "nothing appended after them");
			// The two underwater kinds and the floor's whale fall stand under the sea; the whale fall adrift stays on it.
			foreach (POIType kind in new[] { POIType.TrenchEdge, POIType.SeaCavern, POIType.SunkenWhaleFall })
			{
				Assert.That(rules.RuleFor(kind).Underwater && !rules.RuleFor(kind).Afloat, Is.True, kind.ToString());
				Assert.That(PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Underwater), Is.True, kind.ToString());
				Assert.That(rules.RuleFor(kind).MaxDepth, Is.EqualTo(PointOfInterestKindRule.Unbounded), kind + ": the biome's band narrows it");
			}
			Assert.That(rules.RuleFor(POIType.SunkenWhaleFall).MinDepth, Is.EqualTo(200f), "the abyssal plain's falls, 200 m and deeper");
			Assert.That(rules.RuleFor(POIType.WhaleFall).Afloat, Is.True, "the I2p whale fall is still the one adrift");
		}

		/// <summary>No biome hosts a new kind: not by the catalogue's rows by name, not in any of the 83 files, so no scene changes.</summary>
		[Test]
		public void TheDefaults_HostNoNewKind_InAnyBiome()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			var named = new HashSet<POIType>(Kinds());
			foreach (POIType kind in named)
			{
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.That(rule.DefaultBiomeWeight, Is.EqualTo(0f), $"{kind}: .Only() with no biome");
				Assert.That(rule.Biomes, Is.Empty, $"{kind}: no biome named until its round");
			}
			int entries = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach ((POIType kind, float weight) in rules.KindWeightsFor(entry.Biome))
				{
					Assert.That(named.Contains(kind) && weight > 0f, Is.False, $"{entry.Biome} hosts {kind} by name");
				}
				foreach (BiomeSiteKind site in entry.SiteKinds(rules))
				{
					Assert.That(named.Contains(site.Kind), Is.False, $"{entry.Biome}'s file hosts {site.Kind} before its round");
				}
				entries++;
			}
			Assert.That(entries, Is.EqualTo(83));
			foreach (string none in new string[] { null, string.Empty, "No Such Biome" })
			{
				foreach ((POIType kind, float weight) in rules.KindWeightsFor(none))
				{
					Assert.That(named.Contains(kind), Is.False, $"{kind} on ground of no biome");
				}
			}
		}

		/// <summary>
		/// A plan over ground of several biomes (their files' kinds) is the same, site for site, with the new rows in the catalogue
		/// and without them: appended last in their tiers and hosted nowhere, they move nothing.
		/// </summary>
		[Test]
		public void APlan_IsTheSame_WithAndWithoutTheNewRows()
		{
			PointOfInterestRules with = PointOfInterestRules.Default();
			PointOfInterestRules without = PointOfInterestRules.Default();
			foreach (POIType kind in Kinds())
			{
				without.Remove(kind);
			}
			foreach (string[] biomes in new[] { new[] { "Plains", "Forest" }, new[] { "Volcanic", "Swamp Ruins" }, new[] { "Swamp Temple", "Wasteland" } })
			{
				PointOfInterestPlan a = PointOfInterestPlanner.Plan(Input(with, biomes));
				PointOfInterestPlan b = PointOfInterestPlanner.Plan(Input(without, biomes));
				Assert.That(a.Records.Count, Is.GreaterThan(3), $"{string.Join("+", biomes)}: a scene with sites to compare");
				Assert.That(a.Records.Count, Is.EqualTo(b.Records.Count), string.Join("+", biomes));
				for (int i = 0; i < a.Records.Count; i++)
				{
					Assert.That((a.Records[i].Kind, a.Records[i].Id, a.Records[i].Position), Is.EqualTo((b.Records[i].Kind, b.Records[i].Id, b.Records[i].Position)),
						$"{string.Join("+", biomes)} site {i}");
				}
				Assert.That(a.Records.Any(r => Kinds().Contains(r.Kind)), Is.False);
			}
		}

		[Test]
		public void LivedInKindsNeedAir_WetKindsNeedWater_AndTheFilesWriteBoth()
		{
			var lived = new[]
			{
				POIType.MarshWatchtower, POIType.BrickKiln, POIType.FowlersHide, POIType.EelWeir, POIType.HoluaSlide, POIType.PlagueVillage,
				POIType.Chapel, POIType.HolyWell, POIType.FrogShrine, POIType.LavaCaveTemple, POIType.Kipuka, POIType.FloatingIsland, POIType.PhoenixNest,
			};
			var wet = new[] { POIType.FloatingIsland, POIType.DrownedForest, POIType.EelWeir, POIType.FowlersHide, POIType.SunkenPlaza, POIType.MireGate };
			foreach (POIType kind in Kinds())
			{
				Assert.That(BiomeSiteAir.NeedsAirByDefault(kind), Is.EqualTo(lived.Contains(kind)), $"{kind}: breathable air");
				Assert.That(BiomeSiteAir.NeedsWaterByDefault(kind), Is.EqualTo(wet.Contains(kind)), $"{kind}: liquid water");
			}
			// Decision 96: the holy well needs no water as a kind (its silent well-head stands on a dry world), only its spring chapel.
			Assert.That(BiomeSiteAir.NeedsWaterByDefault(POIType.HolyWell), Is.False);
			// No older kind gained a water default (decision 68 left them all dry until a file says so).
			foreach (POIType kind in (POIType[])Enum.GetValues(typeof(POIType)))
			{
				if ((int)kind <= (int)POIType.WhaleTow)
				{
					Assert.That(BiomeSiteAir.NeedsWaterByDefault(kind), Is.False, kind.ToString());
				}
			}

			// A file hosting one writes the defaults, and its own word wins either way.
			var entry = new BiomeArtSpec.Entry { Biome = "Swamp Ruins" };
			entry.Hosts(POIType.MireGate, 1f).Hosts(POIType.FloatingIsland, 0.3f).Hosts(POIType.Chapel, 0.5f).Hosts(POIType.DrownedForest, 0.6f)
				.Water(POIType.DrownedForest, false).Air(POIType.Chapel, false);
			List<BiomeSiteKind> list = entry.SiteKinds(PointOfInterestRules.Default());
			BiomeSiteKind gate = list.Single(k => k.Kind == POIType.MireGate);
			Assert.That((gate.NeedsBreathableAir, gate.NeedsLiquidWater), Is.EqualTo((false, true)));
			Assert.That(gate.ToString(), Is.EqualTo("MireGate×1×water"));
			BiomeSiteKind isle = list.Single(k => k.Kind == POIType.FloatingIsland);
			Assert.That((isle.NeedsBreathableAir, isle.NeedsLiquidWater), Is.EqualTo((true, true)));
			Assert.That(list.Single(k => k.Kind == POIType.DrownedForest).NeedsLiquidWater, Is.False, "the file's word over the default");
			Assert.That(list.Single(k => k.Kind == POIType.Chapel).NeedsBreathableAir, Is.False, "the file's word over the default");
			Assert.That(BiomeSiteAir.WeightOn(gate, true, false), Is.EqualTo(0f), "a gate sunk in black water needs water");
			Assert.That(BiomeSiteAir.WeightOn(gate, false, true), Is.EqualTo(1f), "a relic stands under any air");
		}

		/// <summary>
		/// Placeholders only where the kit reads as the kind: every structure kind among the new ones has a template on today's
		/// pieces, every other lays nothing; the holy well's spring chapel alone needs liquid water.
		/// </summary>
		[Test]
		public void Placeholders_OnlyWhereTheKitReadsAsTheKind()
		{
			var built = new[]
			{
				POIType.AncientForge, POIType.PhoenixNest, POIType.LavaCaveTemple, POIType.MireGate, POIType.SunkenPlaza, POIType.MarshWatchtower,
				POIType.FallenColossus, POIType.SealedVault, POIType.FrogShrine, POIType.FowlersHide, POIType.BrickKiln, POIType.BlightedCrater,
				POIType.Chapel, POIType.HolyWell, POIType.PlagueVillage,
			};
			List<PointOfInterestTemplateSpec> specs = PointOfInterestTemplateSpecs.All();
			foreach (POIType kind in Kinds())
			{
				bool structure = PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Structure);
				Assert.That(structure, Is.EqualTo(built.Contains(kind)), $"{kind}: built from the kit or not");
				Assert.That(specs.Exists(s => s.Kind == kind), Is.EqualTo(structure), $"{kind}: a template exactly when it is built");
			}
			Assert.That(specs.Where(s => s.NeedsLiquidWater).Select(s => s.Name), Is.EqualTo(new[] { "HolyWell - Spring Chapel" }));
			Assert.That(specs.Exists(s => s.Kind == POIType.HolyWell && !s.NeedsLiquidWater), Is.True, "a well-head for the dry worlds");
			foreach (PointOfInterestTemplateSpec spec in specs.Where(s => s.Kind == POIType.MireGate || s.Kind == POIType.SunkenPlaza))
			{
				Assert.That(spec.InWater && spec.Finish == PointOfInterestFinish.Algae, Is.True, spec.Name);
			}
			Func<PointOfInterestTemplate, float> weights = t => 2f;
			Assert.That(PointOfInterestTemplate.WaterFit(weights, true), Is.SameAs(weights), "a world with water picks as before");
			Assert.That(PointOfInterestTemplate.WaterFit(null, true), Is.Null);
		}

		/// <summary>The water filter on real template objects (ScriptableObjects: Unity only).</summary>
		[Test]
		public void WaterFit_DropsOnlyTheWetTemplates_OnADryWorld()
		{
			var wet = ScriptableObject.CreateInstance<PointOfInterestTemplate>();
			var dry = ScriptableObject.CreateInstance<PointOfInterestTemplate>();
			try
			{
				wet.NeedsLiquidWater = true;
				dry.DefaultBiomeWeight = 0.7f;
				Func<PointOfInterestTemplate, float> fit = PointOfInterestTemplate.WaterFit(t => 1.5f, false);
				Assert.That(fit(wet), Is.EqualTo(0f));
				Assert.That(fit(dry), Is.EqualTo(1.5f));
				Func<PointOfInterestTemplate, float> none = PointOfInterestTemplate.WaterFit(null, false);
				Assert.That(none(wet), Is.EqualTo(0f));
				Assert.That(none(dry), Is.EqualTo(0.7f), "no biome: the template's own weight elsewhere");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(wet);
				UnityEngine.Object.DestroyImmediate(dry);
			}
		}

		/// <summary>
		/// Decision 96: Wasteland left the arid family (the rules' and the templates'), so the desert's kinds and variants no longer
		/// default in; every other arid biome keeps them.
		/// </summary>
		[Test]
		public void Wasteland_LeftTheAridFamily()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			Assert.That(PointOfInterestTemplateSpecs.Arid, Does.Not.Contain("Wasteland"));
			var waste = rules.KindWeightsFor("Wasteland").Select(k => k.Kind).ToList();
			var desert = rules.KindWeightsFor("Badlands").Select(k => k.Kind).ToList();
			foreach (POIType kind in new[] { POIType.Overhang, POIType.NaturalArch, POIType.Nest, POIType.Barrow, POIType.Battlefield, POIType.Obelisk })
			{
				Assert.That(waste, Has.No.Member(kind), $"Wasteland no longer takes the arid {kind}");
				Assert.That(desert, Has.Member(kind), $"Badlands keeps the arid {kind}");
			}
			Assert.That(waste, Has.Member(POIType.Ruins), "kinds found anywhere stay");
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				Assert.That(spec.BiomeWeightByName("Wasteland"), Is.EqualTo(spec.DefaultBiomeWeight), $"{spec.Name}: no arid variant weight in the Wasteland");
			}
		}

		/// <summary>
		/// The detected kinds' .Only lists are defaults (decisions 90, 95, Rille Q9): a biome whose template hosts Volcano,
		/// ObsidianField, FumaroleField or TidalRift finds its cluster site whatever its name, and none without; today's default
		/// host lists are unchanged.
		/// </summary>
		[Test]
		public void DetectedKinds_DeferToTheTemplate_AndKeepTodaysDefaults()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			Assert.That(Hosts(rules, POIType.Volcano), Is.EqualTo(new[] { "Volcanic" }));
			Assert.That(Hosts(rules, POIType.ObsidianField), Is.EqualTo(new[] { "Volcanic" }));
			Assert.That(Hosts(rules, POIType.FumaroleField), Is.EqualTo(new[] { "Geyser Basin", "Sulphur Flats" }));
			Assert.That(Hosts(rules, POIType.TidalRift), Is.EqualTo(new[] { "Tidal Fracture" }));
			Assert.That(Hosts(rules, POIType.LavaTube), Is.EqualTo(new[] { "Volcanic", "Volcanic Cave", "Volcanic Temple", "Geyser Basin", "Sulphur Flats", "Lava Tube" }));
			foreach (POIType kind in new[] { POIType.Volcano, POIType.ObsidianField, POIType.FumaroleField, POIType.TidalRift })
			{
				PointOfInterestPlan hosted = PointOfInterestPlanner.Plan(Single(rules, new Dictionary<POIType, float> { [kind] = 1f }));
				PointOfInterestPlan bare = PointOfInterestPlanner.Plan(Single(rules, new Dictionary<POIType, float>()));
				Assert.That(hosted.Records.Count(r => r.Kind == kind), Is.EqualTo(1), $"{kind}: found where the template hosts it");
				Assert.That(bare.Records.Count(r => r.Kind == kind), Is.EqualTo(0), $"{kind}: never where it does not");
			}
		}

		private static string[] Hosts(PointOfInterestRules rules, POIType kind)
			=> rules.RuleFor(kind).Biomes.Where(b => b.Weight > 0f).Select(b => b.Biome).ToArray();

		// ── A small scene ────────────────────────────────────────────

		private const float Size = 3000f;

		private static float Ground(float x, float z) => 40f + 30f * Mathf.Sin(x * 0.002f) * Mathf.Cos(z * 0.0017f) + 0.004f * x;

		private static PointOfInterestPlanInput Input(PointOfInterestRules rules, string[] biomes)
		{
			var list = new List<PointOfInterestBiome>();
			for (int i = 0; i < biomes.Length; i++)
			{
				list.Add(PointOfInterestPlannerTests.Hosting(biomes[i], 20 + i, rules));
			}
			int count = biomes.Length;
			return new PointOfInterestPlanInput
			{
				WidthMetres = Size,
				DepthMetres = Size,
				Ground = Ground,
				SeaLevel = 0f,
				HasSea = false,
				Water = new PointOfInterestWater { KindAt = (x, z) => WaterKind.None, DistanceAt = (x, z) => 400f },
				Biomes = list,
				BiomeAt = (x, z) => Mathf.Clamp((int)((x + Size * 0.5f) / Size * count), 0, count - 1),
				HardnessAt = (x, z, y) => 0.6f,
				Settings = new PointOfInterestPlanSettings(),
				Rules = rules,
				Seed = 4321,
			};
		}

		private static PointOfInterestPlanInput Single(PointOfInterestRules rules, Dictionary<POIType, float> kinds)
		{
			return new PointOfInterestPlanInput
			{
				WidthMetres = Size,
				DepthMetres = Size,
				Ground = Ground,
				SeaLevel = 0f,
				HasSea = false,
				Water = new PointOfInterestWater { KindAt = (x, z) => WaterKind.None, DistanceAt = (x, z) => 400f },
				Biomes = new List<PointOfInterestBiome> { new PointOfInterestBiome("Any Name", 30, null, kinds) },
				BiomeAt = (x, z) => 0,
				HardnessAt = (x, z, y) => 0.6f,
				Settings = new PointOfInterestPlanSettings(),
				Rules = rules,
				Seed = 99,
			};
		}
	}
}
