using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The Moron 2 round of 2026-10-27: (1) a scene with no river or lake on its ground is given a hydrology asset for the water its
	/// sites lay (hot spring and sulphur pools, paddies, rills), which until then stood dry; (2) the hot ground's dug sites are points
	/// of interest (GeothermalPointsOfInterest: geysers, hot springs, mud pots, fumaroles, sulphur pools, the Great Spout); (3) a
	/// biome's structure styles never strand a sited kind without a template (Earth 0 had made every cave, ice cave, nest and bone
	/// den of Alpine Meadow, Scree, Ice Sheet and Glacier a bare marker). Pure, apart from the source pins.
	/// </summary>
	public class HotGroundSitesAndWaterTests
	{
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";

		private static string Code(string path) => File.ReadAllText(path);

		private static GeothermalSite Site(GeothermalKind kind, float x, float z, float size, uint seed = 1u)
			=> new GeothermalSite { Kind = kind, Position = new Vector2(x, z), Size = size, Seed = seed };

		private static GeothermalGroundCarve.Plan HotPlan(params GeothermalSite[] sites)
		{
			var plan = new GeothermalGroundCarve.Plan();
			plan.Sites.AddRange(sites);
			for (int i = 0; i < sites.Length; i++)
			{
				plan.Dug.Add(new GeothermalGroundCarve.Dug { Site = sites[i], Index = i, Base = 0f });
			}
			return plan;
		}

		// ── (1) The water ─────────────────────────────────────────────

		[Test]
		public void AHydrologyAsset_IsMadeForTheSitesWater_NeverInALocalCopysFolder()
		{
			Assert.That(PointOfInterestPaddies.MayMakeHydrology("Assets/Scenes/WorldScene/Moron/Moron 2 Terrain", "Moron 2"), Is.True);
			Assert.That(PointOfInterestPaddies.MayMakeHydrology("Assets/LOCAL/SceneCopies/Moron 2/Terrain", "Moron 2"), Is.False, "a LOCAL copy reads the committed water");
			Assert.That(PointOfInterestPaddies.MayMakeHydrology("assets/local/x", "Moron 2"), Is.False, "any capitalisation, as the gitignore");
			Assert.That(PointOfInterestPaddies.MayMakeHydrology(string.Empty, "Moron 2"), Is.False, "a harness");
			Assert.That(PointOfInterestPaddies.MayMakeHydrology("Assets/X", null), Is.False);
		}

		/// <summary>
		/// The cause (run 2026-10-27: Moron 2's 6 and Ithaewyn 1's 5 pools "left dry"): a re-cut deletes the terrain folder, the cut
		/// saves a hydrology asset only for a river or lake on the ground, and the pools were written before a cave's water made one.
		/// Now the pools' and the paddies' writers make it, at the sea's level the place stage read before letting a dry scene's water go.
		/// </summary>
		[Test]
		public void ThePoolsAndThePaddies_MakeTheHydrologyTheyNeed()
		{
			string carve = Code(WorldFolder + "/Terrain/GeothermalGroundCarve.cs");
			Assert.That(carve, Does.Contain("SceneHydrology hydrology = PointOfInterestPaddies.EnsureHydrology(scene, terrainFolder, sceneName, seaLevel);"));
			Assert.That(carve, Does.Not.Contain("the scene has no hydrology asset (no river or lake) to hold their water"));
			string paddies = Code(WorldFolder + "/PointsOfInterest/Features/PointOfInterestPaddies.cs");
			Assert.That(paddies, Does.Contain("? EnsureHydrology(context.Scene, context.TerrainFolder, context.Request.SceneName,"));
			Assert.That(paddies, Does.Contain("SceneGenerator.EnsureInlandWater(scene, hydrology);"), "drawn and queried like the cut's");
			string stage = Code(WorldFolder + "/PointsOfInterest/PointOfInterestStage.cs");
			int sea = stage.IndexOf("float seaLevel = water != null ? water.SeaLevel : float.NegativeInfinity;", StringComparison.Ordinal);
			Assert.That(sea, Is.GreaterThan(0));
			int dry = stage.IndexOf("if (water != null && !water.Any)", sea, StringComparison.Ordinal);
			Assert.That(dry - sea, Is.LessThan(400), "the sea read just before a dry scene's water is let go");
			Assert.That(stage, Does.Contain("GeothermalGroundCarve.WriteWater(scene, TerrainFolder, Request.SceneName, notes, seaLevel);"));
		}

		// ── (2) The hot ground's points of interest ───────────────────

		[Test]
		public void TheHotGroundsKinds_AreAppendedFoundVolcanicAndNamed()
		{
			POIType[] kinds = { POIType.Geyser, POIType.MudPot, POIType.Fumarole, POIType.SulphurPool };
			Assert.That((int)POIType.Geyser, Is.EqualTo((int)POIType.SeaHall + 1), "appended");
			Assert.That((int)POIType.SulphurPool, Is.EqualTo((int)POIType.Geyser + 3), "in that order");
			// Only the fantasy wave follows them (2026-10-27, FantasyWaveKindsTests pins the rest).
			Assert.That((int)POIType.DragonLair, Is.EqualTo((int)POIType.SulphurPool + 1));
			PointOfInterestRules rules = PointOfInterestRules.Default();
			string sheet = Code("Assets/Scripts/Client/GUI/World/Map/UIMapPOI.uss");
			foreach (POIType kind in kinds)
			{
				Assert.That(Enum.TryParse(kind.ToString(), false, out MapMarkerType marker), Is.True, $"{kind} has its marker type");
				Assert.That(PointOfInterestKinds.MarkerFor(kind), Is.EqualTo(marker));
				PointOfInterestKindInfo info = PointOfInterestKinds.Info(kind);
				Assert.That(PointOfInterestKinds.IsKnown(kind), Is.True);
				Assert.That(info.Group, Is.EqualTo(PointOfInterestGroup.Volcanic), kind.ToString());
				Assert.That(info.Has(PointOfInterestTraits.Detected), Is.True, $"{kind} is found, not placed");
				Assert.That(info.DetailTier, Is.EqualTo(3));
				Assert.That(rules.RuleFor(kind), Is.Null, $"{kind} has no catalogue row: only the hot ground marks it, never the planner's budget");
				Assert.That(PlaceNameDefaults.TypeWordsFor(kind).Length, Is.GreaterThanOrEqualTo(6));
				Assert.That(PlaceNameDefaults.TypeWordsFor(kind).All(w => w.Length <= 16), Is.True);
				Assert.That(PlaceNameDefaults.Templates.Any(t => t.Applies(kind)), Is.True, $"{kind} has a built-in composition");
				Assert.That(BiomeSiteAir.NeedsAirByDefault(kind), Is.False, "natural");
				Assert.That(sheet, Does.Contain(".map-marker--" + kind.ToString().ToLowerInvariant() + " .map-marker__icon"));
			}
			Assert.That(PointOfInterestKinds.Info(POIType.MudPot).DisplayName, Is.EqualTo("Mud Pot"));
		}

		[Test]
		public void EveryGeothermalKind_IsMarkedAsItsOwn_ASmokerNever()
		{
			var expected = new Dictionary<GeothermalKind, POIType>
			{
				[GeothermalKind.Fumarole] = POIType.Fumarole, [GeothermalKind.HotSpring] = POIType.HotSpring, [GeothermalKind.Geyser] = POIType.Geyser,
				[GeothermalKind.MudPot] = POIType.MudPot, [GeothermalKind.Jet] = POIType.Geyser, [GeothermalKind.SulphurPool] = POIType.SulphurPool,
			};
			foreach (GeothermalKind kind in (GeothermalKind[])Enum.GetValues(typeof(GeothermalKind)))
			{
				bool marked = GeothermalPointsOfInterest.TryKindOf(kind, out POIType poi);
				if (kind == GeothermalKind.Smoker)
				{
					Assert.That(marked, Is.False, "a sea hall's smoker is the hall's");
					continue;
				}
				Assert.That(marked, Is.True, kind.ToString());
				Assert.That(poi, Is.EqualTo(expected[kind]), kind.ToString());
			}
		}

		[Test]
		public void Choose_TheGreatFirst_ClustersMerged_KindsCapped_InSiteOrder()
		{
			var sites = new List<GeothermalSite>
			{
				Site(GeothermalKind.Fumarole, 0f, 0f, 0.2f),
				Site(GeothermalKind.Fumarole, 10f, 0f, 0.9f),   // the larger of a cluster: it stands, site 0 is it
				Site(GeothermalKind.Geyser, 15f, 0f, 0.3f),     // another kind beside them: its own place
				Site(GeothermalKind.Geyser, 200f, 0f, 0.8f),
				Site(GeothermalKind.Geyser, 210f, 0f, 0.1f),    // the great one, though smaller: it is never merged away
				Site(GeothermalKind.Smoker, 400f, 0f, 1f),
			};
			List<GeothermalPointsOfInterest.Pick> picks = GeothermalPointsOfInterest.Choose(sites, new[] { 0, 1, 2, 3, 4, 5 }, 4);
			Assert.That(picks.Select(p => p.Site), Is.EqualTo(new[] { 1, 2, 4 }), "site 0 merged into 1, site 3 into the great 4, the smoker never");
			Assert.That(picks.Single(p => p.Great).Site, Is.EqualTo(4));
			Assert.That(picks.Select(p => p.Kind), Is.EqualTo(new[] { POIType.Fumarole, POIType.Geyser, POIType.Geyser }));

			// Only the dug sites: one in water or in a footprint is no place of its own.
			Assert.That(GeothermalPointsOfInterest.Choose(sites, new[] { 2 }, 4).Select(p => p.Site), Is.EqualTo(new[] { 2 }));

			var many = Enumerable.Range(0, 40).Select(i => Site(GeothermalKind.Jet, i * 100f, 0f, i / 40f)).ToList();
			List<GeothermalPointsOfInterest.Pick> capped = GeothermalPointsOfInterest.Choose(many, Enumerable.Range(0, 40).ToList(), -1);
			Assert.That(capped.Count, Is.EqualTo(GeothermalPointsOfInterest.MaxPerKind));
			Assert.That(capped.All(p => p.Site >= 40 - GeothermalPointsOfInterest.MaxPerKind), Is.True, "the largest kept");
		}

		[Test]
		public void Replace_MarksTheDugSites_AgainOnEveryRun_LeavingThePlannersOwn()
		{
			var plan = new PointOfInterestPlan { Seed = 77 };
			var riverSpring = new PointOfInterestRecord { Id = 5, Kind = POIType.HotSpring, RiverId = 3, PlanetRiver = 9, LakeId = -1, Name = "Vor Springs" };
			var peak = new PointOfInterestRecord { Id = 6, Kind = POIType.Peak, RiverId = -1, PlanetRiver = -1, Name = "Geyser" };
			plan.Records.Add(riverSpring);
			plan.Records.Add(peak);
			GeothermalGroundCarve.Plan hot = HotPlan(
				Site(GeothermalKind.Geyser, 100f, 50f, 0.9f),
				Site(GeothermalKind.HotSpring, -80f, 20f, 0.5f),
				Site(GeothermalKind.MudPot, 300f, -40f, 0.2f),
				Site(GeothermalKind.Geyser, -300f, 200f, 0.3f));
			hot.GreatSpout = 0;

			int added = GeothermalPointsOfInterest.Replace(plan, hot, (x, z) => 42, (x, z) => 12.3456f, null, r => "A " + r.Kind + ".");
			Assert.That(added, Is.EqualTo(4));
			Assert.That(plan.Records, Has.Member(riverSpring), "a river's hot spring is the planner's");
			Assert.That(plan.Records, Has.Member(peak));
			PointOfInterestRecord great = plan.Records.Single(r => r.Kind == POIType.Geyser && r.DetailTier == GeothermalPointsOfInterest.GreatSpoutTier);
			Assert.That(great.Name, Is.EqualTo("Great Geyser"), "no namer: the kind's name, the spout qualified");
			Assert.That(great.Description, Does.StartWith("The Great Spout"));
			Assert.That(great.SizeClass, Is.EqualTo(2));
			Assert.That(great.Position, Is.EqualTo(new Vector3(100f, 12.35f, 50f)), "centimetres");
			PointOfInterestRecord other = plan.Records.Single(r => r.Kind == POIType.Geyser && r != great);
			Assert.That(other.Name, Is.EqualTo("Geyser 2"), "a peak already holds 'Geyser'");
			Assert.That(other.DetailTier, Is.EqualTo(3));
			PointOfInterestRecord spring = plan.Records.Single(r => r.Kind == POIType.HotSpring && r != riverSpring);
			Assert.That(spring.LakeId, Is.EqualTo(GeothermalGroundCarve.PoolLakeId(1)), "its own pool, as the hydrology holds it");
			Assert.That(GeothermalGroundCarve.IsPool(spring.LakeId), Is.True);
			Assert.That(plan.Records.Single(r => r.Kind == POIType.MudPot).LakeId, Is.EqualTo(-1));
			foreach (PointOfInterestRecord r in plan.Records.Where(GeothermalPointsOfInterest.IsHotGroundRecord))
			{
				Assert.That(r.BiomeID, Is.EqualTo(42));
				Assert.That(r.RequiresDiscovery, Is.True, "found, as every detected kind");
				Assert.That(r.Template, Is.Empty);
				Assert.That(r.Id, Is.EqualTo(PointOfInterestPlanner.StableId(r.Kind, r.Position.x, r.Position.z)));
				Assert.That(r.SiteSeed, Is.EqualTo(PointOfInterestPlanner.SiteSeed(77, r.Id)));
			}
			Assert.That(plan.Records.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(plan.Records.Count), "every name its own");

			// A repaint: the same plan read back marks the same again, never twice; a site the ground lost goes.
			string[] before = plan.Records.Select(r => r.Id + r.Name).ToArray();
			GeothermalPointsOfInterest.Replace(plan, hot, (x, z) => 42, (x, z) => 12.3456f, null, r => "A " + r.Kind + ".");
			Assert.That(plan.Records.Select(r => r.Id + r.Name), Is.EquivalentTo(before));
			hot.Dug.RemoveAt(3);
			GeothermalPointsOfInterest.Replace(plan, hot, (x, z) => 42, (x, z) => 0f, null, null);
			Assert.That(plan.Records.Count(r => r.Kind == POIType.Geyser), Is.EqualTo(1));
			GeothermalPointsOfInterest.Replace(plan, null, null, null, null, null);
			Assert.That(plan.Records, Is.EqualTo(new[] { riverSpring, peak }), "no hot ground: none of its records");
		}

		[Test]
		public void TheNamer_NamesThem_TheSpoutGreat_AndIsRetriedOnAClash()
		{
			var plan = new PointOfInterestPlan { Seed = 3 };
			plan.Records.Add(new PointOfInterestRecord { Id = 1, Kind = POIType.Village, Name = "Vor Spout" });
			GeothermalGroundCarve.Plan hot = HotPlan(Site(GeothermalKind.Geyser, 0f, 0f, 0.9f), Site(GeothermalKind.Fumarole, 500f, 0f, 0.5f));
			hot.GreatSpout = 0;
			PointOfInterestNamer namer = (record, index) => record.Kind == POIType.Geyser ? "Vor Spout" : index == 0 ? "Vor Spout" : "Hissing Reek";
			GeothermalPointsOfInterest.Replace(plan, hot, null, null, namer, null);
			Assert.That(plan.Records.Single(r => r.Kind == POIType.Geyser).Name, Is.EqualTo("Great Vor Spout"));
			Assert.That(plan.Records.Single(r => r.Kind == POIType.Fumarole).Name, Is.EqualTo("Hissing Reek"), "the first draw clashed");
		}

		[Test]
		public void RegeneratePois_CarriesTheOldOnesOver_AndHotSpringHeatSkipsThem()
		{
			var plan = new PointOfInterestPlan();
			plan.Records.Add(new PointOfInterestRecord { Id = 1, Kind = POIType.Camp, Name = "Camp" });
			var old = new[]
			{
				new PointOfInterestRecord { Id = 1, Kind = POIType.Camp, Name = "Old Camp" },
				new PointOfInterestRecord { Id = 9, Kind = POIType.Geyser, RiverId = -1, PlanetRiver = -1, Name = "Great Spout" },
				new PointOfInterestRecord { Id = 10, Kind = POIType.HotSpring, RiverId = -1, PlanetRiver = -1, LakeId = GeothermalGroundCarve.PoolLakeId(2), Name = "Steam Pool", Position = new Vector3(5f, 0f, 6f) },
				new PointOfInterestRecord { Id = 11, Kind = POIType.HotSpring, RiverId = 2, PlanetRiver = 4, LakeId = -1, Name = "River Spring", Position = new Vector3(1f, 0f, 2f) },
			};
			Assert.That(GeothermalPointsOfInterest.CarryOver(plan, old), Is.EqualTo(2));
			Assert.That(plan.Records.Select(r => r.Id), Is.EqualTo(new[] { 1, 9, 10 }), "the planner's own are the new plan's");

			plan.Records.Add(old[3]);
			Assert.That(GeothermalGroundCarve.HotSpringsOf(plan), Is.EqualTo(new[] { new Vector2(1f, 2f) }), "only the planner's springs warm the ground round them");
			Assert.That(Code(WorldFolder + "/PointsOfInterest/PointOfInterestGenerator.cs"), Does.Contain("GeothermalPointsOfInterest.KeepFrom(stage, scene, notes);"));
		}

		[Test]
		public void TheBiomeStage_MarksThemRightAfterTheCarve()
		{
			string generator = Code(WorldFolder + "/SceneGenerator.cs");
			int lay = generator.IndexOf("GeothermalGroundCarve.Plan hotGround = GeothermalGroundCarve.Lay(", StringComparison.Ordinal);
			int mark = generator.IndexOf("GeothermalPointsOfInterest.AddTo(poi, hotGround, field, request, terrains, plan, result.Notes);", StringComparison.Ordinal);
			int heat = generator.IndexOf("BiomeHeatMask heat = hotGround.Heat;", StringComparison.Ordinal);
			Assert.That(lay, Is.GreaterThan(0));
			Assert.That(mark, Is.GreaterThan(lay));
			Assert.That(heat, Is.GreaterThan(mark), "in the biome stage, which runs before the place stage writes the records");
		}

		// ── (3) Styles never strand a sited kind ──────────────────────

		[Test]
		public void ANamedVariant_KeepsItsWeight_UnderAStyleWeighedZero()
		{
			Assert.That(BiomeSiteStyle.Apply(0.4f, true, 0f), Is.EqualTo(0.4f));
			Assert.That(BiomeSiteStyle.Apply(0.4f, false, 0f), Is.EqualTo(0f));
			Assert.That(BiomeSiteStyle.Apply(0.4f, true, 0.5f), Is.EqualTo(0.2f), "any other style weight multiplies as before");
			Assert.That(BiomeSiteStyle.Apply(0f, true, 0f), Is.EqualTo(0f), "a variant named at 0 stays 0");
			var variants = new List<BiomeSiteVariant> { new BiomeSiteVariant(POIType.MonsterDen, "Bones", 0.4f) };
			var styles = new List<BiomeSiteStyle> { new BiomeSiteStyle(BiomeStructureStyle.Earth, 0f) };
			Assert.That(BiomeTemplateArt.SiteVariantWeight(variants, styles, POIType.MonsterDen, "Bones", "earth", 1f), Is.EqualTo(0.4f));
			Assert.That(BiomeTemplateArt.SiteVariantWeight(variants, styles, POIType.Nest, "Bones", "earth", 1f), Is.EqualTo(0f), "unnamed: the style's 0");
		}

		/// <summary>
		/// Every authored biome, every kind it sites that has templates, every size: where the styles leave no template at all the
		/// variants alone give one (the stage's second pick), and the biomes that cost the Moron scenes their caves and nests are
		/// among those cases, so the fallback is what builds them.
		/// </summary>
		[Test]
		public void WhereTheStylesWeighEveryTemplateZero_TheVariantsAloneStillBuildOne()
		{
			List<PointOfInterestTemplateAuthoring.Planned> planned = PointOfInterestTemplateAuthoring.Plan(PointOfInterestTemplateSpecs.All(), (spec, size) => size.ToString());
			var byKind = planned.GroupBy(p => p.Spec.Kind).ToDictionary(g => g.Key, g => g.ToList());
			var stranded = new HashSet<string>();
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				List<BiomeSiteVariant> variants = entry.SiteVariants();
				List<BiomeSiteStyle> styles = entry.SiteStyles();
				foreach (BiomeSiteKind hosted in entry.SiteKinds())
				{
					if (hosted.Weight <= 0f || !byKind.TryGetValue(hosted.Kind, out List<PointOfInterestTemplateAuthoring.Planned> list))
					{
						continue;
					}
					for (int size = 0; size < 3; size++)
					{
						float styled = 0f, plain = 0f;
						bool any = false;
						foreach (PointOfInterestTemplateAuthoring.Planned t in list)
						{
							bool fits = size == 0 ? t.Small : size == 1 ? t.Medium : t.Large;
							if (!fits)
							{
								continue;
							}
							any = true;
							string variant = PointOfInterestTemplate.VariantOf(t.Name, t.Spec.Kind);
							styled += t.Spec.Weight * BiomeTemplateArt.SiteVariantWeight(variants, styles, t.Spec.Kind, variant, t.Spec.Style, t.Spec.DefaultBiomeWeight);
							plain += t.Spec.Weight * BiomeTemplateArt.SiteVariantWeight(variants, null, t.Spec.Kind, variant, t.Spec.Style, t.Spec.DefaultBiomeWeight);
						}
						if (any && styled <= 0f && plain > 0f)
						{
							stranded.Add($"{entry.Biome}/{hosted.Kind}");
						}
					}
				}
			}
			foreach (string expected in new[] { "Alpine Meadow/Nest", "Alpine Meadow/Cave", "Scree/Cave", "Ice Sheet/IceCave", "Glacier/Nest" })
			{
				Assert.That(stranded, Has.Member(expected), "the styles alone would leave it bare");
			}
			string stage = Code(WorldFolder + "/PointsOfInterest/PointOfInterestStage.cs");
			Assert.That(stage, Does.Contain("if (template == null && biome != null && biome.SiteStyles != null && biome.SiteStyles.Count > 0)"));
			Assert.That(stage, Does.Contain("CaveInteriorArt.SiteVariantWeights(in caves, false) : BiomeTemplateArt.SiteVariantWeights(biome, false);"));
		}
	}
}
