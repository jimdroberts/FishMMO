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
			Biomes = new List<PointOfInterestBiome> { PointOfInterestPlannerTests.Hosting("Plains", 11, null), PointOfInterestPlannerTests.Hosting("Forest", 12, null) },
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
		public void APadAtAClifftopLeavesTheGorgeBelowTheCliffAlone()
		{
			// A shrine's 8 m pad at 115 m on a clifftop that runs 22 m out from it, then a 70 m drop to a gorge floor at 45 m.
			const float radius = 8f, blend = 8f, height = 115f;
			float Ground(float d) => d < 30f ? height : height - 70f;
			float[] reach = PointOfInterestTerrain.BankReach((east, north) => Ground(Mathf.Sqrt(east * east + north * north)), Vector3.zero, radius, blend, height);
			foreach (float r in reach)
			{
				Assert.That(r, Is.LessThan(30f), "the bank meets the clifftop's ground before the edge in every direction");
			}
			foreach (float d in new[] { 40f, 60f, 96f, 140f })
			{
				float h = PointOfInterestTerrain.PadHeight(d, Ground(d), radius, blend, height, PointOfInterestTerrain.ReachToward(reach, d, 0f));
				Assert.That(h, Is.EqualTo(Ground(d)).Within(1e-3f), $"the gorge {d} m out is not filled (the cone alone filled it to {PointOfInterestTerrain.PadHeight(d, Ground(d), radius, blend, height):0.0} m)");
			}
		}

		[Test]
		public void APadOnAHillsideStillBanksOutToWhereTheBankMeetsTheHillside()
		{
			// The hillside pad above: traced, its bank runs out to where the cone meets the slope, 138 m past the rim.
			const float radius = 60f, blend = 12f, height = 100f;
			float Ground(float d) => height - 0.4f * d;
			float[] reach = PointOfInterestTerrain.BankReach((east, north) => Ground(Mathf.Sqrt(east * east + north * north)), Vector3.zero, radius, blend, height);
			Assert.That(PointOfInterestTerrain.ReachToward(reach, 1f, 0f), Is.InRange(radius + 130f, radius + 145f));
			for (float d = radius; d < radius + 130f; d += 5f)
			{
				float traced = PointOfInterestTerrain.PadHeight(d, Ground(d), radius, blend, height, PointOfInterestTerrain.ReachToward(reach, d, 0f));
				Assert.That(traced, Is.EqualTo(PointOfInterestTerrain.PadHeight(d, Ground(d), radius, blend, height)).Within(1e-3f), $"the same bank at {d} m");
			}
		}

		[Test]
		public void APadsBankEndsShortOfTheRiverBelowIt()
		{
			// A town's 46 m pad at 126 m on ground falling 0.4 m per metre (gentler than the bank) to a river whose bank begins 60 m out.
			const float radius = 46f, blend = 12f, height = 126f, riverBank = 60f;
			float Ground(float d) => height - 0.4f * (d - radius) - 1f;
			float[] reach = PointOfInterestTerrain.BankReach((east, north) => Ground(Mathf.Sqrt(east * east + north * north)), Vector3.zero, radius, blend, height,
				(east, north) => Mathf.Sqrt(east * east + north * north) >= riverBank);
			float toward = PointOfInterestTerrain.ReachToward(reach, 1f, 0f);
			Assert.That(toward, Is.LessThanOrEqualTo(riverBank - PointOfInterestTerrain.BankHandoverMetres - PointOfInterestTerrain.WaterSetbackMetres + 1e-3f));
			foreach (float d in new[] { riverBank, riverBank + 3f, riverBank + 10f })
			{
				Assert.That(PointOfInterestTerrain.PadHeight(d, Ground(d), radius, blend, height, toward), Is.EqualTo(Ground(d)).Within(1e-3f),
					$"the river {d - riverBank:0} m into its bank is left as it is");
			}
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

		/// <summary>
		/// A gorge 20 m across between walls 2 m thick, its floor at 20 m, the near rim (z &lt; 0) at 50 m and the far rim at
		/// <paramref name="far"/>; past the near lip the rim falls away outward at <paramref name="nearFall"/> metres a metre.
		/// </summary>
		private static Func<float, float> RakedGorge(float far, float nearFall = 0f) => z =>
		{
			float d = Mathf.Abs(z), rim = z < 0f ? 50f - nearFall * Mathf.Max(0f, d - 10.5f) : far;
			return d < 8f ? 20f : d < 10f ? Mathf.Lerp(20f, rim, (d - 8f) / 2f) : rim;
		};

		private static PropsFeature.Crossing MeasureGorge(Func<float, float> ground)
			=> PropsFeature.MeasureCrossing(ground, 20.5f, 32f, z => Mathf.Abs(z) < 3f ? 20.5f : float.NegativeInfinity, 50f);

		/// <summary>
		/// A kept gorge whose far rim stands 4 m over its near one (about 11° lip to lip): the stone arch is not tilted whole to
		/// reach lip to lip. Its deck is level or gently graded; the low rim is built up and the high one cut down to it, each
		/// on a ramp a cart could take, meeting the deck's ends without a step and the rims' ground where they end; the gorge
		/// is not filled; and the ground so shaped is measured, when the site is built, as rims that meet the same deck.
		/// </summary>
		[Test]
		public void ARakedGorgeIsArchedOnALevelDeckWithItsApproachesBuilt()
		{
			Func<float, float> ground = RakedGorge(54f);
			PropsFeature.Crossing crossing = MeasureGorge(ground);
			Assert.That(crossing.Gorge, Is.True);
			Assert.That(crossing.SlopeDegrees, Is.GreaterThan(8f), "the rims are raked");
			string id = PropsFeature.GorgeBridgeFor(ground, crossing, true, out _, out PropsFeature.ArchDeck arch);
			Assert.That(id, Does.StartWith("ArchBridge"));
			Assert.That(arch.Valid, Is.True);
			Assert.That(Mathf.Abs(arch.GradeDegrees), Is.LessThanOrEqualTo(PropsFeature.MaxArchGradeDegrees + 1e-3f), "a level or gently graded deck");
			Assert.That(arch.Settled, Is.False, "the rims must be built up and cut to it");
			Assert.That(arch.RiseFrom, Is.LessThan(0f), "the low rim built up, its ramp coming back down");
			Assert.That(arch.RiseTo, Is.GreaterThan(0f), "the high rim cut down, its ramp climbing out");
			Assert.That(arch.Fill, Is.InRange(0.3f, PropsFeature.MaxArchFill + PropsFeature.ArchStepTolerance));
			Assert.That(arch.Cut, Is.InRange(0.3f, PropsFeature.MaxArchCut + 0.05f));

			float Carved(float z) => PropsFeature.ApproachGround(arch, z, 0f, ground(z));
			// The deck's ends on their approaches: within the seat, no step.
			foreach (float end in new[] { arch.EndFrom, arch.EndTo })
			{
				Assert.That(arch.DeckAt(end) - Carved(end), Is.InRange(0f, PropsFeature.ArchStepTolerance), $"the deck's end at {end:0.0} m meets its approach");
			}
			// Each ramp no steeper than a cart's grade, out to where it meets its rim, and the rim untouched past it.
			float cart = ScenePathStyle.For(ScenePathClass.CartTrack).MaxGrade;
			for (int side = -1; side <= 1; side += 2)
			{
				float end = side < 0 ? arch.EndFrom : arch.EndTo, ramp = side < 0 ? arch.RampFrom : arch.RampTo;
				for (float r = 0f; r < ramp + 2f; r += 0.5f)
				{
					float a = end + side * r, b = end + side * (r + 0.5f);
					Assert.That(Mathf.Abs(Carved(b) - Carved(a)), Is.LessThanOrEqualTo(cart * 0.5f + 0.02f), $"the approach at {a:0.0} m");
				}
				float beyond = end + side * (ramp + 1f);
				Assert.That(Carved(beyond) - ground(beyond), Is.EqualTo(0f).Within(1e-4f), "the ramp has met its rim");
			}
			// The gorge's walls and floor are left as they were.
			foreach (float z in new[] { -9f, -4f, 0f, 4f })
			{
				Assert.That(Carved(z), Is.EqualTo(ground(z)).Within(1e-4f), $"{z} m along, in the gorge");
			}

			// Measured again on the shaped ground (the site is built after the cut): rims that meet the same deck.
			PropsFeature.Crossing built = MeasureGorge(Carved);
			Assert.That(built.Gorge, Is.True);
			string again = PropsFeature.GorgeBridgeFor(Carved, built, true, out _, out PropsFeature.ArchDeck laid);
			Assert.That(again, Does.StartWith("ArchBridge"));
			Assert.That(laid.Settled, Is.True, $"fill {laid.Fill:0.00} m, cut {laid.Cut:0.00} m still wanted");
			Assert.That(laid.DeckAt(0f), Is.EqualTo(arch.DeckAt(0f)).Within(0.15f), "the deck laid where it was planned");
			Assert.That(Mathf.Abs(laid.GradeDegrees), Is.LessThanOrEqualTo(PropsFeature.MaxArchGradeDegrees + 1e-3f));
		}

		/// <summary>A gorge with level rims needs no approaches: the arch lies level on them as it did.</summary>
		[Test]
		public void ALevelGorgesArchNeedsNoApproaches()
		{
			Func<float, float> ground = RakedGorge(50f);
			PropsFeature.Crossing crossing = MeasureGorge(ground);
			string id = PropsFeature.GorgeBridgeFor(ground, crossing, true, out _, out PropsFeature.ArchDeck arch);
			Assert.That(id, Does.StartWith("ArchBridge"));
			Assert.That(arch.Settled, Is.True);
			Assert.That(arch.GradeDegrees, Is.EqualTo(0f).Within(1e-3f));
			Assert.That(arch.DeckAt(0f) - 50f, Is.InRange(0f, PropsFeature.ArchStepTolerance), "on its rims");
		}

		/// <summary>
		/// A rake more than a ramp of sensible length makes up (7 m over 21 m, under the bridges' 20° so the way still crosses),
		/// or a low rim falling away under its approach, is not arched: it is hung with ropes, which may slope.
		/// </summary>
		[Test]
		public void ARakeTooSteepForAnArchIsHungWithRopes()
		{
			Func<float, float> steep = RakedGorge(57f);
			PropsFeature.Crossing crossing = MeasureGorge(steep);
			Assert.That(crossing.Gorge, Is.True);
			Assert.That(PropsFeature.GorgeBridgeFor(steep, crossing, true, out _, out PropsFeature.ArchDeck arch), Does.StartWith("RopeBridge"));
			Assert.That(arch.Valid, Is.False);

			Func<float, float> falling = RakedGorge(54f, 0.2f);
			PropsFeature.Crossing away = MeasureGorge(falling);
			Assert.That(away.Gorge, Is.True);
			Assert.That(PropsFeature.GorgeBridgeFor(falling, away, true, out _, out _), Does.StartWith("RopeBridge"), "the built-up ramp never meets a rim falling away faster");
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
