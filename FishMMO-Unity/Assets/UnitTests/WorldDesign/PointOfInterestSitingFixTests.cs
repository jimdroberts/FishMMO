using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The two defects the first real cut showed (Flo Monolith probe, 2026-10-10): a capital asked for before the cut was
	/// never sited on rugged ground ("0 of 1 sited"), and every fall on one river carried the river's one name ("Dunwy
	/// Falls" seven times). Pure: no scene, no asset.
	/// </summary>
	[TestFixture]
	public class PointOfInterestSitingFixTests
	{
		private const float Size = 3000f;

		/// <summary>Egg-crate hills, 44 m from trough to crest every ~380 m: no 400 m disc anywhere is flat enough.</summary>
		private static float Rugged(float x, float z) => 60f + 22f * Mathf.Sin(x / 60f) * Mathf.Sin(z / 70f);

		private static PointOfInterestPlanInput RuggedInput(bool capital) => new PointOfInterestPlanInput
		{
			WidthMetres = Size,
			DepthMetres = Size,
			Ground = Rugged,
			SeaLevel = 0f,
			HasSea = false,
			Water = new PointOfInterestWater
			{
				KindAt = (x, z) => WaterKind.None,
				DistanceAt = (x, z) => 10000f,
				Rivers = new List<RiverPath>(),
				Lakes = new List<PointOfInterestLake>(),
			},
			Biomes = new List<PointOfInterestBiome> { new PointOfInterestBiome("Plains", 11), new PointOfInterestBiome("Forest", 12) },
			BiomeAt = (x, z) => x < 0f ? 0 : 1,
			HardnessAt = (x, z, y) => 0.6f,
			Settings = new PointOfInterestPlanSettings { Capital = capital },
			Rules = PointOfInterestRules.Default(),
			Seed = 77,
		};

		[Test]
		public void ACapitalAskedForIsSitedEvenOnRuggedGround()
		{
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(RuggedInput(true));
			List<PointOfInterestRecord> capitals = plan.Records.FindAll(r => r.Kind == POIType.Capital);
			Assert.That(capitals.Count, Is.EqualTo(1), string.Join("\n", plan.Notes));
		}

		[Test]
		public void NoCapitalUnlessAskedForOnRuggedGroundEither()
		{
			PointOfInterestPlan plan = PointOfInterestPlanner.Plan(RuggedInput(false));
			Assert.That(plan.Records.Exists(r => r.Kind == POIType.Capital), Is.False);
		}

		[Test]
		public void RelaxedSitingIsDeterministic()
		{
			PointOfInterestPlan a = PointOfInterestPlanner.Plan(RuggedInput(true));
			PointOfInterestPlan b = PointOfInterestPlanner.Plan(RuggedInput(true));
			PointOfInterestRecord ca = a.Records.Find(r => r.Kind == POIType.Capital);
			PointOfInterestRecord cb = b.Records.Find(r => r.Kind == POIType.Capital);
			Assert.That(ca.Position, Is.EqualTo(cb.Position));
			Assert.That(ca.Radius, Is.EqualTo(cb.Radius));
		}

		private static PointOfInterestRecord Fall(int id, float y, string name, int planetRiver = 9)
			=> new PointOfInterestRecord { Id = id, Kind = POIType.Waterfall, Position = new Vector3(0f, y, 0f), Name = name, PlanetRiver = planetRiver };

		[Test]
		public void FallsSharingARiversNameAreToldApartHighestFirst()
		{
			var records = new List<PointOfInterestRecord> { Fall(1, 120f, "Dunwy Falls"), Fall(2, 300f, "Dunwy Falls"), Fall(3, 200f, "Dunwy Falls") };
			PointOfInterestNaming.QualifyRiverFeatures(records);
			Assert.That(records[1].Name, Is.EqualTo("Upper Dunwy Falls"));
			Assert.That(records[2].Name, Is.EqualTo("Middle Dunwy Falls"));
			Assert.That(records[0].Name, Is.EqualTo("Lower Dunwy Falls"));
		}

		[Test]
		public void ManyFallsAreNamedForThemselvesBetweenTheUpperAndTheLower()
		{
			var records = new List<PointOfInterestRecord>();
			for (int i = 0; i < 7; i++)
			{
				records.Add(Fall(i, 500f - i * 50f, "Dunwy Falls"));
			}
			records.Add(Fall(99, 10f, "Tamar Falls", planetRiver: 4));
			// A stub namer: a fall named as a place of its own (planet river cleared while it is asked) gets its id's name.
			PointOfInterestNaming.QualifyRiverFeatures(records, (record, index) => record.PlanetRiver < 0 ? $"Fall {record.Id}" : null, new HashSet<string>());
			Assert.That(records[0].Name, Is.EqualTo("Upper Dunwy Falls"));
			Assert.That(records[6].Name, Is.EqualTo("Lower Dunwy Falls"));
			Assert.That(records[3].Name, Is.EqualTo("Fall 3"), "the ones between take names of their own");
			Assert.That(records[3].PlanetRiver, Is.EqualTo(9), "and keep their river");
			Assert.That(records[7].Name, Is.EqualTo("Tamar Falls"));
			var names = new HashSet<string>();
			foreach (PointOfInterestRecord record in records)
			{
				Assert.That(names.Add(record.Name), Is.True, $"'{record.Name}' repeats");
			}
		}

		[Test]
		public void ARiversOwnRecordsAreNeverQualified()
		{
			var records = new List<PointOfInterestRecord>
			{
				new PointOfInterestRecord { Id = 1, Kind = POIType.River, Position = Vector3.up * 50f, Name = "River Dunwy", PlanetRiver = 9 },
				new PointOfInterestRecord { Id = 2, Kind = POIType.River, Position = Vector3.up * 20f, Name = "River Dunwy", PlanetRiver = 9 },
			};
			PointOfInterestNaming.QualifyRiverFeatures(records);
			Assert.That(records.TrueForAll(r => r.Name == "River Dunwy"), Is.True);
		}

		[Test]
		public void APadOnAHillsideBanksAtTheEmbankmentSlopeNotAScarp()
		{
			// A 60 m pad at 100 m on ground falling 0.4 m per metre (22°) to the east: the pad sits 24 m above the ground at its edge.
			const float radius = 60f, blend = 12f, height = 100f;
			float Ground(float d) => height - 0.4f * d;
			float previous = PointOfInterestTerrain.PadHeight(radius, Ground(radius), radius, blend, height);
			for (float d = radius + 1f; d <= radius + PointOfInterestTerrain.MaxEmbankmentMetres + 20f; d += 1f)
			{
				float h = PointOfInterestTerrain.PadHeight(d, Ground(d), radius, blend, height);
				// Never steeper than the bank, but for the fade over the reach's last quarter, which may add a little while it
				// closes a gap the bank has not yet closed (about 32° here); a scarp would be many times the slope.
				Assert.That(previous - h, Is.LessThanOrEqualTo(PointOfInterestTerrain.EmbankmentSlope + 0.05f), $"a step at {d} m");
				Assert.That(h, Is.GreaterThanOrEqualTo(Ground(d) - 1e-3f), "a fill never digs below the ground");
				previous = h;
			}
			// The bank (0.6/m after a 12 m fillet) meets the hillside (0.4/m) 24 m below the rim 138 m out.
			Assert.That(PointOfInterestTerrain.PadHeight(radius + 140f, Ground(radius + 140f), radius, blend, height), Is.EqualTo(Ground(radius + 140f)).Within(1e-3f));
			// And past the reach the ground is untouched, so the edit ends without a step.
			float past = radius + PointOfInterestTerrain.MaxEmbankmentMetres;
			Assert.That(PointOfInterestTerrain.PadHeight(past, Ground(past), radius, blend, height), Is.EqualTo(Ground(past)).Within(1e-3f));
		}

		[Test]
		public void APadOnLevelGroundLeavesItsSurroundsAlone()
		{
			const float radius = 40f, blend = 10f, height = 50f;
			Assert.That(PointOfInterestTerrain.PadHeight(20f, 51.5f, radius, blend, height), Is.EqualTo(height), "inside the pad: the pad");
			Assert.That(PointOfInterestTerrain.PadHeight(radius + blend + 5f, 50.4f, radius, blend, height), Is.EqualTo(50.4f).Within(1e-4f), "past the rim: the ground");
		}

		/// <summary>
		/// A dry channel 6 m wide and 2 m deep, its walls carved up to ground at 0 m; past the near bank flat ground, past
		/// the far bank a hillside climbing on. The deck spans the channel's walls and sits on the ground at both ends; the
		/// hillside is not climbed (2026-10-10: one end buried, the other in the air).
		/// </summary>
		private static float Channel(float z)
		{
			float d = Mathf.Abs(z);
			if (d < 3f)
			{
				return -2f;
			}
			if (d < 6f)
			{
				return -2f + (d - 3f) * (2f / 3f);
			}
			return z > 0f ? 0.5f * (d - 6f) : 0f;
		}

		[Test]
		public void ABridgeSpansAChannelsWallsAndSitsOnBothBanks()
		{
			PropsFeature.Crossing crossing = PropsFeature.MeasureCrossing(Channel, -2f, 20f);
			Assert.That(crossing.From, Is.InRange(-7f, -6f), "the near end just past the wall's lip");
			Assert.That(crossing.To, Is.InRange(6f, 7f), "the far end at its lip: the hillside beyond is not climbed");
			Assert.That(crossing.DeckFrom, Is.EqualTo(0f).Within(0.05f));
			Assert.That(crossing.DeckTo, Is.EqualTo(0f).Within(0.3f));
			Assert.That(crossing.DeckAt(crossing.From) - Channel(crossing.From), Is.InRange(-0.3f, 0.1f), "on its ground");
			Assert.That(crossing.DeckAt(crossing.To) - Channel(crossing.To), Is.InRange(-0.3f, 0.1f), "on its ground");
		}

		/// <summary>
		/// A lowland river: 6 m of water a metre deep, its banks rising gently (a fifth of a metre a metre) from just above
		/// the water. The deck reaches only as far as the banks take to clear the water, not out across the floodplain
		/// (2026-10-11: bridges far past the river line, over the path).
		/// </summary>
		private static float Lowland(float z)
		{
			float d = Mathf.Abs(z);
			return d < 3f ? -1f : d < 4f ? Mathf.Lerp(-1f, 0.1f, d - 3f) : 0.1f + 0.2f * (d - 4f);
		}

		[Test]
		public void ALowlandBridgeIsNoLongerThanTheBanksNeed()
		{
			PropsFeature.Crossing crossing = PropsFeature.MeasureCrossing(Lowland, 0f, 60f);
			// The banks clear the water by the deck's clearance 6.5 m out; the deck ends half a metre past that.
			Assert.That(crossing.To - crossing.From, Is.LessThanOrEqualTo(15f));
			Assert.That(crossing.DeckAt(0f), Is.GreaterThanOrEqualTo(PropsFeature.DeckClearance - 1e-3f), "clear of the water");
			Assert.That(crossing.DeckAt(crossing.To) - Lowland(crossing.To), Is.InRange(-0.3f, 0.1f), "on its ground");
		}

		[Test]
		public void ALowBankIsRampedOntoNotStoodOver()
		{
			// Water at 0, 8 m wide; the near bank a flat 0.2 m above it (never clear of it), the far one rising to 1.5 m.
			float Ground(float z) => z < -4f ? 0.2f : z <= 4f ? -1f : Mathf.Min(1.5f, -1f + (z - 4f) * 2f);
			PropsFeature.Crossing crossing = PropsFeature.MeasureCrossing(Ground, 0f, 20f);
			Assert.That(crossing.DeckAt(crossing.From), Is.EqualTo(Ground(crossing.From - (-0.5f))).Within(0.05f), "the low end comes down onto its bank, not over it");
			Assert.That(crossing.DeckAt(crossing.To) - Ground(crossing.To), Is.InRange(-0.3f, 0.1f), "the high end on its bank");
			for (float z = crossing.WaterFrom; z <= crossing.WaterTo; z += 0.5f)
			{
				Assert.That(crossing.DeckAt(z), Is.GreaterThanOrEqualTo(PropsFeature.DeckClearance - 1e-3f), $"clear of the water {z} m along");
			}
			// Every half metre of deck above its ground: rotated modules, never through the bank.
			for (float z = crossing.From; z <= crossing.To; z += 0.5f)
			{
				Assert.That(crossing.DeckAt(z), Is.GreaterThanOrEqualTo(Ground(z) - 0.35f), $"{z} m along");
			}
		}

		[Test]
		public void AHighBankIsComeDownToKeepTheDeckGentle()
		{
			// Water at 0; the near bank clears it at 1 m, the far bank's wall climbs 3 m to its lip.
			float Ground(float z) => z < 0f ? (Mathf.Abs(z) < 3f ? -1f : 0.8f) : z < 3f ? -1f : Mathf.Min(3f, -1f + (z - 3f) * 1.2f);
			PropsFeature.Crossing crossing = PropsFeature.MeasureCrossing(Ground, 0f, 20f);
			Assert.That(Mathf.Abs(crossing.SlopeDegrees), Is.LessThanOrEqualTo(PropsFeature.MaxDeckSlopeDegrees + 0.5f));
			Assert.That(crossing.DeckAt(0f), Is.GreaterThanOrEqualTo(PropsFeature.DeckClearance - 1e-3f));
		}

		/// <summary>
		/// A river 6 m wide crossed on a hillside: the near bank rises, the far one falls away downhill from the water's edge
		/// at half a metre a metre, under the water's level all the way. That ground was taken as more water, the deck held
		/// its level over it to the reach, and the layout cut it off there 6 m over the slope (Flo Monolith,
		/// 2026-10-12: "still floating in mid air on one end").
		/// </summary>
		private static float Hillside(float z) => z < -3f ? 0.5f * (-z - 3f) : z < 3f ? -1f : -0.5f * (z - 3f);

		[Test]
		public void AnEndWhereTheGroundFallsAwayLandsAtTheWater()
		{
			const float Reach = 16f;
			// The scene's water: the river only; the hillside beyond is dry ground.
			PropsFeature.Crossing crossing = PropsFeature.MeasureCrossing(Hillside, 0f, Reach, z => Mathf.Abs(z) < 3f ? 0f : float.NegativeInfinity);
			Assert.That(crossing.To, Is.LessThanOrEqualTo(Reach), "no end past the reach the layout lays to");
			Assert.That(crossing.To, Is.InRange(3f, 4f), "the far end at the water's edge, not out over the slope");
			Assert.That(crossing.DeckAt(crossing.To) - Hillside(crossing.To), Is.InRange(-0.3f, 0.1f), "the far end on its ground");
			Assert.That(crossing.DeckAt(crossing.From) - Hillside(crossing.From), Is.InRange(-0.3f, 0.1f), "the near end on its ground");
			Assert.That(crossing.Unfooted, Is.True);

			// Without the scene's water (a dry channel's reckoning) the fall is under the level all the way: still no end in the air.
			PropsFeature.Crossing dry = PropsFeature.MeasureCrossing(Hillside, 0f, Reach);
			Assert.That(dry.To, Is.LessThanOrEqualTo(Reach));
			Assert.That(dry.DeckAt(dry.To) - Hillside(dry.To), Is.InRange(-0.3f, 0.1f), "the far end on its ground");
		}

		/// <summary>
		/// A crossing at a fall's head: the river at 0 m over a bed a metre down, the far bank's lip at the water, and past it
		/// the fall's face and the lower reach 6 m down. The lower reach is other water, not more of this crossing.
		/// </summary>
		[Test]
		public void TheWaterBelowAFallIsNotTheWaterCrossed()
		{
			float Ground(float z) => z < -3f ? 1f : z < 3f ? -1f : z < 4f ? 0.2f : -6.5f;
			float Water(float z) => Mathf.Abs(z) < 3f ? 0f : z >= 4f ? -6f : float.NegativeInfinity;
			PropsFeature.Crossing crossing = PropsFeature.MeasureCrossing(Ground, 0f, 16f, Water);
			Assert.That(crossing.To, Is.InRange(3f, 4.5f), "ends on the lip above the fall");
			Assert.That(crossing.DeckAt(crossing.To) - Ground(crossing.To), Is.InRange(-0.3f, 0.1f), "on its ground");
			Assert.That(crossing.Level, Is.EqualTo(PropsFeature.DeckClearance).Within(1e-3f), "clear of this water, not raised for the other");
		}

		/// <summary>
		/// A gorge 20 m across between walls 30 m high, a river on its floor: laid rim to rim at the rims' height (the record
		/// stands there), not down onto the floor beside the water.
		/// </summary>
		[Test]
		public void AGorgeBridgeRunsLipToLipHighOverTheFloor()
		{
			float Ground(float z) => Mathf.Abs(z) < 8f ? 20f : Mathf.Abs(z) < 10f ? Mathf.Lerp(20f, 50f, (Mathf.Abs(z) - 8f) / 2f) : 50f;
			PropsFeature.Crossing crossing = PropsFeature.MeasureCrossing(Ground, 20.5f, 32f, z => Mathf.Abs(z) < 3f ? 20.5f : float.NegativeInfinity, 50f);
			Assert.That(crossing.From, Is.InRange(-11f, -9.5f));
			Assert.That(crossing.To, Is.InRange(9.5f, 11f));
			Assert.That(crossing.DeckAt(0f), Is.EqualTo(50f).Within(0.5f), "high over the floor");
			Assert.That(crossing.Gorge, Is.True, "built as a gorge's crossing (StructurePieces.Bridges), not plank spans");
			Assert.That(crossing.DeckAt(crossing.From) - Ground(crossing.From), Is.InRange(-0.3f, 0.1f), "on the near rim");
			Assert.That(crossing.DeckAt(crossing.To) - Ground(crossing.To), Is.InRange(-0.3f, 0.1f), "on the far rim");
		}

		/// <summary>
		/// Which gorge crossing a gorge gets: a stone arch where a settlement keeps it and it is no wider than an arch is
		/// thrown, the nearest in length (the least stretch); ropes for a wide or a wild one; planks for a wild cleft too
		/// narrow for ropes.
		/// </summary>
		[Test]
		public void AGorgeGetsTheCrossingThatFitsIt()
		{
			Assert.That(PropsFeature.GorgeBridgeFor(20.6f, true, out float arch), Is.EqualTo("ArchBridge20"));
			Assert.That(arch, Is.EqualTo(20f));
			Assert.That(PropsFeature.GorgeBridgeFor(9f, true, out _), Is.EqualTo("ArchBridge8"));
			Assert.That(PropsFeature.GorgeBridgeFor(38f, true, out float wide), Is.EqualTo("RopeBridge36"), "wider than an arch is thrown");
			Assert.That(wide, Is.EqualTo(36f));
			Assert.That(PropsFeature.GorgeBridgeFor(18f, false, out _), Is.EqualTo("RopeBridge16"), "a wild gorge is crossed on ropes");
			Assert.That(PropsFeature.GorgeBridgeFor(7f, false, out _), Is.Null, "a wild cleft is crossed on planks");
			foreach (float length in new[] { 6f, 12f, 17f, 24f, 31f, 40f })
			{
				foreach (bool kept in new[] { true, false })
				{
					string id = PropsFeature.GorgeBridgeFor(length, kept, out float nominal);
					if (id == null)
					{
						continue;
					}
					StructurePiece piece = StructurePieces.Find(id);
					Assert.That(piece, Is.Not.Null, id);
					Assert.That(piece.ModuleLength, Is.EqualTo(nominal), id);
					Assert.That(Mathf.Max(length / nominal, nominal / length), Is.LessThanOrEqualTo(1.4f), $"{id} stretched to {length} m");
				}
			}
		}

		[Test]
		public void WithNoNamerTheFallsBetweenAreNumbered()
		{
			var records = new List<PointOfInterestRecord>();
			for (int i = 0; i < 5; i++)
			{
				records.Add(Fall(i, 500f - i * 50f, "Dunwy Falls"));
			}
			PointOfInterestNaming.QualifyRiverFeatures(records);
			Assert.That(records[0].Name, Is.EqualTo("Upper Dunwy Falls"));
			Assert.That(records[2].Name, Is.EqualTo("Third Dunwy Falls"));
			Assert.That(records[4].Name, Is.EqualTo("Lower Dunwy Falls"));
		}

		[Test]
		public void ASiteIsCalledByWhatItsTemplateBuilds()
		{
			var bridge = new PointOfInterestRecord { Kind = POIType.Bridge, Template = "Bridge - Timber" };
			string[] words = PointOfInterestNaming.TypeWordsFor(bridge);
			Assert.That(words, Does.Contain("Bridge"));
			Assert.That(words, Has.None.EqualTo("Viaduct").And.None.EqualTo("Ford").And.None.EqualTo("Causeway"));
			var camp = new PointOfInterestRecord { Kind = POIType.Camp, Template = "Camp - Yurts (Medium-Large)" };
			Assert.That(PointOfInterestNaming.TypeWordsFor(camp), Does.Contain("Yurts"), "the size in brackets is not the style");
			Assert.That(PointOfInterestNaming.TypeWordsFor(new PointOfInterestRecord { Kind = POIType.Cave, Template = "Cave - Deep" }), Is.Null,
				"a kind whose own words all fit keeps them");
		}

		[Test]
		public void DescriptionsReadAsEnglish()
		{
			Assert.That(PointOfInterestNaming.Describe(new PointOfInterestRecord { Kind = POIType.Lake }, "Lake"), Is.EqualTo("A lake."));
			Assert.That(PointOfInterestNaming.Describe(new PointOfInterestRecord { Kind = POIType.Ruins }, "Tundra"), Is.EqualTo("Ruins in the Tundra."));
			Assert.That(PointOfInterestNaming.Describe(new PointOfInterestRecord { Kind = POIType.Rapids }, "Forest"), Is.EqualTo("Rapids in the Forest."));
			Assert.That(PointOfInterestNaming.Describe(new PointOfInterestRecord { Kind = POIType.TradingPost }, "Forest"), Is.EqualTo("A trading post in the Forest."));
			Assert.That(PointOfInterestNaming.Describe(new PointOfInterestRecord { Kind = POIType.Overhang }, "Forest"), Is.EqualTo("An overhang in the Forest."));
		}

		[Test]
		public void EveryCountryHasWordsForItsRegions()
		{
			foreach (GeneratedSceneNames.RegionCountry country in (GeneratedSceneNames.RegionCountry[])Enum.GetValues(typeof(GeneratedSceneNames.RegionCountry)))
			{
				string[] words = GeneratedSceneNames.RegionWords(country);
				Assert.That(words, Is.Not.Empty, country.ToString());
				Assert.That(words, Is.Unique, country.ToString());
			}
			Assert.That(GeneratedSceneNames.RegionWords(GeneratedSceneNames.RegionCountry.Forest), Does.Contain("Weald"));
			Assert.That(GeneratedSceneNames.RegionWords(GeneratedSceneNames.RegionCountry.Forest), Has.None.EqualTo("Monolith").And.None.EqualTo("Obelisk"),
				"a region is named as country, never as one landmark");
		}

		[Test]
		public void TheLeadingArticleStaysInFront()
		{
			Assert.That(PointOfInterestNaming.Qualify("The Hidden Race", "Upper"), Is.EqualTo("The Upper Hidden Race"));
			Assert.That(PointOfInterestNaming.Qualify("Dunwy Falls", "Lower"), Is.EqualTo("Lower Dunwy Falls"));
		}
	}
}
