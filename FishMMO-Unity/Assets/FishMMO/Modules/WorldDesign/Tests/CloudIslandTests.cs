using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A scene on a gas giant is a cloud world (2026-10-14: "Cutting a scene on a gas giant shouldn't generate terrain…
	/// It should just be a cloud system… small floating islands, flying ships, and maybe flying cities"): its islands,
	/// their bridges and landing stages (CloudIslands), and the habitable layer its climate is asked about (CloudWorld).
	/// </summary>
	public class CloudIslandTests
	{
		private static CloudIslands.Plan Lay(float size, uint seed = 12345u) => CloudIslands.Lay(size, size, seed);

		[Test]
		public void TheSameSceneLaysTheSameIslands()
		{
			CloudIslands.Plan a = Lay(1500f), b = Lay(1500f);
			Assert.AreEqual(a.Islands.Count, b.Islands.Count);
			for (int i = 0; i < a.Islands.Count; i++)
			{
				Assert.AreEqual(a.Islands[i].Centre, b.Islands[i].Centre);
				Assert.AreEqual(a.Islands[i].Height, b.Islands[i].Height);
			}
			Assert.AreEqual(a.Bridges.Count, b.Bridges.Count);
			Assert.AreEqual(a.Docks.Count, b.Docks.Count);
		}

		[TestCase(500f)]
		[TestCase(1500f)]
		[TestCase(4000f)]
		public void ThereIsACityIslandAndAnArchipelagoAroundIt(float size)
		{
			CloudIslands.Plan plan = Lay(size);
			Assert.AreEqual(SceneCloudWorld.IslandKind.City, plan.Islands[0].Kind, "the city's island is laid first");
			Assert.Greater(plan.Islands.Count, 3, "more than the city");
			Assert.Greater(plan.Bridges.Count, 0, "some islands are a rope bridge apart");
		}

		[TestCase(500f)]
		[TestCase(1500f)]
		[TestCase(4000f)]
		public void IslandsKeepApartAndInsideTheScene(float size)
		{
			CloudIslands.Plan plan = Lay(size);
			for (int i = 0; i < plan.Islands.Count; i++)
			{
				CloudIslands.Island a = plan.Islands[i];
				Assert.LessOrEqual(Mathf.Abs(a.Centre.x) + a.Reach, size * 0.5f, $"island {i} reaches past the scene's edge");
				Assert.LessOrEqual(Mathf.Abs(a.Centre.y) + a.Reach, size * 0.5f, $"island {i} reaches past the scene's edge");
				Assert.That(a.Height, Is.InRange(plan.Floor, plan.Ceiling), $"island {i} floats outside the band");
				Assert.Greater(a.Height, CloudIslands.DeckTop + 100f, "well above the deck");
				for (int j = i + 1; j < plan.Islands.Count; j++)
				{
					CloudIslands.Island b = plan.Islands[j];
					Assert.GreaterOrEqual(Vector2.Distance(a.Centre, b.Centre), a.Reach + b.Reach + CloudIslands.ShortestGap - 1e-3f, $"islands {i} and {j} touch");
				}
			}
		}

		[Test]
		public void BridgesSpanOnlyWhatARopeWalkCanAndClimbGently()
		{
			CloudIslands.Plan plan = Lay(2500f);
			foreach (CloudIslands.Bridge bridge in plan.Bridges)
			{
				float span = Vector2.Distance(bridge.FromAt, bridge.ToAt);
				Assert.LessOrEqual(span, CloudIslands.LongestBridge + 1e-3f);
				CloudIslands.Island a = plan.Islands[bridge.A], b = plan.Islands[bridge.B];
				float rise = Mathf.Abs(a.GroundAt(bridge.FromAt.x, bridge.FromAt.y) - b.GroundAt(bridge.ToAt.x, bridge.ToAt.y));
				Assert.LessOrEqual(rise, span * CloudIslands.BridgeGrade + 1e-3f, "too steep to walk");
				// Each end stands on its own island.
				Assert.Less(a.Inside(bridge.FromAt.x, bridge.FromAt.y), 1f);
				Assert.Less(b.Inside(bridge.ToAt.x, bridge.ToAt.y), 1f);
			}
		}

		[Test]
		public void LandingStagesFaceOpenSky()
		{
			CloudIslands.Plan plan = Lay(2500f);
			Assert.Greater(plan.Docks.Count, 0, "the city at least has a landing stage");
			foreach (CloudIslands.Dock dock in plan.Docks)
			{
				Vector2 out30 = dock.Edge + dock.Outward * 30f;
				Assert.AreEqual(-1, plan.IslandAt(out30.x, out30.y), "a stage reaching into another island");
				Assert.AreEqual(1f, dock.Outward.magnitude, 1e-4f);
			}
		}

		[Test]
		public void OnlyTheIslandsAreSolidAndTheVoidTakesTheirRimsHeight()
		{
			var tiles = new TerrainTilePlan { CountX = 1, CountZ = 1, TileMetres = 500f, Resolution = 129 };
			CloudIslands.Plan plan = CloudIslands.Lay(tiles.WidthMetres, tiles.DepthMetres, 777u);
			int per = tiles.Resolution - 1;
			SceneHeightField field = SceneHeightField.FromMetres(tiles, new float[(per + 1) * (per + 1)]);
			bool[] solid = CloudIslands.Shape(plan, field);
			Assert.AreEqual(per * per, solid.Length);
			int island = 0;
			for (int z = 0; z < per; z++)
			{
				for (int x = 0; x < per; x++)
				{
					float east = 0.5f * (field.EastOf(x) + field.EastOf(x + 1)), north = 0.5f * (field.NorthOf(z) + field.NorthOf(z + 1));
					if (solid[z * per + x])
					{
						island++;
						Assert.GreaterOrEqual(plan.IslandAt(east, north), 0, "a solid cell over the void");
					}
				}
			}
			Assert.Greater(island, per * per / 10, "a fair share of the sky is island");
			Assert.Less(island, per * per * 6 / 10, "most of it is sky");
			field.Range(out float lowest, out float highest);
			Assert.GreaterOrEqual(lowest, plan.Floor - 5f, "no sample drops toward the deck: the void holds its rims' height");
		}

		/// <summary>The ground of a plan's islands at a point, NaN over the void: what the cut reads off the holed terrain.</summary>
		private static System.Func<float, float, float> GroundOf(CloudIslands.Plan plan)
			=> (east, north) =>
			{
				int i = plan.IslandAt(east, north);
				return i < 0 ? float.NaN : plan.Islands[i].GroundAt(east, north);
			};

		[TestCase(500f)]
		[TestCase(1500f)]
		[TestCase(4000f)]
		public void AirshipsFindLanesAmongTheIslands(float size)
		{
			int scenes = 0, served = 0, lanes = 0, stages = 0;
			for (uint seed = 1; seed <= 12; seed++)
			{
				CloudIslands.Plan plan = Lay(size, seed * 7919u);
				System.Func<float, float, float> ground = GroundOf(plan);
				SceneCloudWorld.Island[] islands = CloudWorldCut.IslandRecords(plan, out float lowest, out float highest);
				SceneCloudWorld.Dock[] docks = CloudWorldCut.DockRecords(plan, ground);
				var planner = new SkyLanePlanner(PointOfInterestSkyTraffic.Islands(islands, ground), size, size);
				var kinds = new System.Collections.Generic.List<string>();
				var planned = PointOfInterestSkyTraffic.PlanLanes(planner, docks, CloudIslands.DeckTop, lowest, highest, size, size,
					new DeterministicRNG((int)seed), kinds);
				scenes++;
				stages += docks.Length;
				lanes += planned.Count;
				served += planned.Count > 0 ? 1 : 0;
			}
			System.Console.WriteLine($"{size} m: {served}/{scenes} scenes have lanes; {lanes} lanes over {stages} stages.");
			Assert.GreaterOrEqual(served, scenes * 3 / 4, $"{size} m: only {served} of {scenes} scenes have an airship lane ({lanes} lanes over {stages} stages)");
		}

		/// <summary>
		/// The turf meets the rock with no gap from any side (2026-10-15: "can we blend the rock and terrain properly? Be more
		/// thorough"): round every island, with the rim built as the cut builds it (CloudWorldCut.RimRing), no turf is drawn
		/// past the rim (the terrain draws up to 0.6 of a cell past a solid cell either way, its filtered holes texture
		/// compressed); the turf's drawn edge lies under the stone; and at the ledge's inner edge the turf is back over the
		/// stone, so the grass shows and the stone is not a curb.
		/// </summary>
		[TestCase(0.5f)]
		[TestCase(1.5f)]
		[TestCase(3f)]
		public void TheTurfsEdgeLiesUnderTheRockAllRound(float km)
		{
			// The cut's own tiles for a scene this size: 513 samples a tile, up to a kilometre or so a tile.
			TerrainTilePlan tiles = SceneGeneration.PlanTiles(new Vector2(km, km));
			float size = tiles.WidthMetres;
			CloudIslands.Plan plan = CloudIslands.Lay(tiles.WidthMetres, tiles.DepthMetres, 4242u, tiles.MetresPerSample);
			int per = tiles.Resolution - 1, cellsX = tiles.CountX * per, cellsZ = tiles.CountZ * per;
			SceneHeightField field = SceneHeightField.FromMetres(tiles, new float[(cellsX + 1) * (cellsZ + 1)]);
			bool[] solid = CloudIslands.Shape(plan, field, out float[] rolled);
			float cell = tiles.MetresPerSample;
			System.Func<float, float, bool> hole = (east, north) =>
			{
				int x = Mathf.Clamp((int)((east + tiles.WidthMetres * 0.5f) / cell), 0, cellsX - 1);
				int z = Mathf.Clamp((int)((north + tiles.DepthMetres * 0.5f) / cell), 0, cellsZ - 1);
				return !solid[z * cellsX + x];
			};
			System.Func<float, float, float> ground = field.MetresAt;
			System.Func<float, float, float> turf = CloudIslands.Unrolled(field, rolled);
			// Where the terrain may draw turf: within 0.6 of a cell of a solid cell (its filtered holes texture, compressed).
			bool Drawn(float east, float north)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						if (!hole(east + dx * 0.6f * cell, north + dz * 0.6f * cell))
						{
							return true;
						}
					}
				}
				return false;
			}
			// The turf's drawn edge: the fringe drawn into hole cells, and the outermost sliver of the solid cells beside them.
			bool AtTheEdge(float east, float north)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						if (hole(east + dx * 0.3f * cell, north + dz * 0.3f * cell))
						{
							return true;
						}
					}
				}
				return false;
			}
			int columns = 0, past = 0;
			float worstUnder = float.PositiveInfinity, worstOver = float.PositiveInfinity;
			string where = string.Empty, whereUnder = string.Empty;
			for (int i = 0; i < plan.Islands.Count; i++)
			{
				CloudIslands.Island island = plan.Islands[i];
				int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * 2f * island.Reach * CloudWorldCut.ColumnsPerCell / cell), 24, 3000);
				var rimR = new float[n + 1];
				var rimY = new float[n + 1];
				var ledgeR = new float[n + 1];
				var ledgeY = new float[n + 1];
				CloudWorldCut.RimRing(island, n, ground, turf, hole, cell, rimR, rimY, ledgeR, ledgeY);
				for (int j = 0; j < n; j++)
				{
					float angle = j / (float)n * Mathf.PI * 2f;
					float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
					for (float d = ledgeR[j]; d <= rimR[j] + cell; d += 0.05f * cell)
					{
						float east = island.Centre.x + cos * d, north = island.Centre.y + sin * d;
						if (!Drawn(east, north))
						{
							continue;
						}
						if (d > rimR[j])
						{
							past++;
							where = $"island {i} angle {angle * Mathf.Rad2Deg:0.0}°: turf drawn {d - rimR[j]:0.00} m past the rim";
							continue;
						}
						if (!AtTheEdge(east, north))
						{
							continue;
						}
						float stone = Mathf.Lerp(ledgeY[j], rimY[j], Mathf.InverseLerp(ledgeR[j], rimR[j], d));
						float under = stone - ground(east, north);
						if (under < worstUnder)
						{
							worstUnder = under;
							whereUnder = $"island {i} angle {angle * Mathf.Rad2Deg:0.0}° at {rimR[j] - d:0.00} m inside the rim, {d - ledgeR[j]:0.00} m out from the ledge; stone {stone:0.00}, turf {ground(east, north):0.00}";
						}
					}
					float over = ground(island.Centre.x + cos * ledgeR[j], island.Centre.y + sin * ledgeR[j]) - ledgeY[j];
					worstOver = Mathf.Min(worstOver, over);
					columns++;
				}
			}
			System.Console.WriteLine($"{size:0} m, {cell:0.00} m cells: {columns} columns; turf drawn past the rim at {past} point(s); turf near its edge under the stone by at least {worstUnder:0.00} m ({whereUnder}); " +
				$"grass over the ledge by at least {worstOver:0.00} m.");
			Assert.AreEqual(0, past, $"turf drawn past the rim: {where}");
			Assert.Greater(worstUnder, 0.1f, $"the turf comes up through the rim: {whereUnder}");
			Assert.Greater(worstOver, 0.05f, "the ledge's inner edge stands over the grass");
		}

		/// <summary>
		/// A bridge lands on the grass and clears the rock (2026-10-15: "The bridges… no longer end properly on land, they also
		/// need to accommodate our world sim walker"): each end post on solid turf with its landing on the grass behind it,
		/// flush within a step (0.25 m, the walker's and the players' KCC); the walk never in the grass or the rim's stone on its
		/// way out; and no stretch of it steeper than a walker climbs.
		/// </summary>
		[TestCase(0.5f)]
		[TestCase(1.5f)]
		public void BridgesLandOnTheGrassAndClearTheRim(float km)
		{
			TerrainTilePlan tiles = SceneGeneration.PlanTiles(new Vector2(km, km));
			CloudIslands.Plan plan = CloudIslands.Lay(tiles.WidthMetres, tiles.DepthMetres, 4242u, tiles.MetresPerSample);
			int per = tiles.Resolution - 1, cellsX = tiles.CountX * per, cellsZ = tiles.CountZ * per;
			SceneHeightField field = SceneHeightField.FromMetres(tiles, new float[(cellsX + 1) * (cellsZ + 1)]);
			bool[] solid = CloudIslands.Shape(plan, field, out float[] rolled);
			float cell = tiles.MetresPerSample;
			System.Func<float, float, bool> hole = (east, north) =>
			{
				int x = Mathf.Clamp((int)((east + tiles.WidthMetres * 0.5f) / cell), 0, cellsX - 1);
				int z = Mathf.Clamp((int)((north + tiles.DepthMetres * 0.5f) / cell), 0, cellsZ - 1);
				return !solid[z * cellsX + x];
			};
			System.Func<float, float, float> ground = field.MetresAt, turf = CloudIslands.Unrolled(field, rolled);
			var rims = new CloudWorldCut.RimProfile[plan.Islands.Count];
			for (int i = 0; i < rims.Length; i++)
			{
				rims[i] = CloudWorldCut.Profile(plan.Islands[i], ground, turf, hole, cell);
			}
			int bridges = 0;
			float worstStep = 0f, worstClear = float.PositiveInfinity, worstSlope = 0f, closestPosts = float.PositiveInfinity;
			string where = string.Empty;
			var posts = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Vector3>>();
			System.Collections.Generic.List<Vector3> PostsOn(int island)
			{
				if (!posts.TryGetValue(island, out System.Collections.Generic.List<Vector3> list))
				{
					list = new System.Collections.Generic.List<Vector3>();
					posts[island] = list;
				}
				return list;
			}
			foreach (CloudIslands.Bridge bridge in plan.Bridges)
			{
				CloudIslands.Island a = plan.Islands[bridge.A], b = plan.Islands[bridge.B];
				if (!CloudWorldCut.BridgeWay(a, rims[bridge.A], b, rims[bridge.B], ground, out System.Collections.Generic.List<Vector3> joints, out int landA, out int landB,
					PostsOn(bridge.A), PostsOn(bridge.B)))
				{
					continue;
				}
				// No two bridges' end posts on one island crowd each other.
				foreach ((int island, Vector3 post) in new[] { (bridge.A, joints[0]), (bridge.B, joints[joints.Count - 1]) })
				{
					foreach (Vector3 other in PostsOn(island))
					{
						closestPosts = Mathf.Min(closestPosts, new Vector2(other.x - post.x, other.z - post.z).magnitude);
					}
					PostsOn(island).Add(post);
				}
				bridges++;
				Vector3 first = joints[0], last = joints[joints.Count - 1];
				Assert.IsFalse(hole(first.x, first.z), $"bridge {bridges}: its end post on {bridge.A} over the void");
				Assert.IsFalse(hole(last.x, last.z), $"bridge {bridges}: its end post on {bridge.B} over the void");
				// The landing behind each post, pitched onto the grass behind it, within a step of it all the way.
				foreach ((Vector3 post, Vector3 toward) in new[] { (first, (joints[1] - first)), (last, (joints[joints.Count - 2] - last)) })
				{
					var outward = new Vector3(toward.x, 0f, toward.z).normalized;
					var back = -outward;
					float pitch = CloudWorldCut.LandingPitch(post, outward, ground) * Mathf.Deg2Rad;
					for (float k = 0f; k <= CloudWorldCut.LandingLength + 1e-3f; k += 0.2f)
					{
						Vector3 p = post + back * (k * Mathf.Cos(pitch));
						float board = post.y + k * Mathf.Sin(pitch);
						float stepUp = Mathf.Abs(ground(p.x, p.z) - board);
						if (stepUp > worstStep)
						{
							worstStep = stepUp;
							where = $"bridge {bridges}: the landing {k:0.0} m behind its post is {stepUp:0.00} m off the grass";
						}
					}
				}
				// Out over each rim: the walk over the grass and over the stone.
				void Clear(CloudIslands.Island island, CloudWorldCut.RimProfile rim, int from, int to, int step)
				{
					for (int j = from; j != to; j += step)
					{
						Vector3 p0 = joints[j], p1 = joints[j + step];
						for (float t = 0f; t <= 1f; t += 0.05f)
						{
							Vector3 p = Vector3.Lerp(p0, p1, t);
							float dx = p.x - island.Centre.x, dz = p.z - island.Centre.y;
							rim.Sample(Mathf.Atan2(dz, dx), out float rimR, out float rimY, out float ledgeR, out float ledgeY);
							float r = Mathf.Sqrt(dx * dx + dz * dz);
							float under = float.NegativeInfinity;
							if (!hole(p.x, p.z))
							{
								under = ground(p.x, p.z);
							}
							if (r >= ledgeR && r <= rimR + CloudWorldCut.BrowOut)
							{
								under = Mathf.Max(under, Mathf.Lerp(ledgeY, rimY, Mathf.InverseLerp(ledgeR, rimR, r)));
							}
							float clear = p.y - under;
							if (clear < worstClear)
							{
								worstClear = clear;
								if (clear < 0f)
								{
									where = $"bridge {bridges}: the walk {-clear:0.00} m into the ground or the rim {r - rimR:0.00} m from the rim";
								}
							}
						}
					}
				}
				Clear(a, rims[bridge.A], 0, landA, 1);
				Clear(b, rims[bridge.B], joints.Count - 1, landB, -1);
				for (int j = 0; j < joints.Count - 1; j++)
				{
					Vector3 s = joints[j + 1] - joints[j];
					float slope = Mathf.Atan2(Mathf.Abs(s.y), new Vector2(s.x, s.z).magnitude) * Mathf.Rad2Deg;
					worstSlope = Mathf.Max(worstSlope, slope);
				}
			}
			System.Console.WriteLine($"{km} km: {bridges} of {plan.Bridges.Count} bridges laid; posts of two on one island {closestPosts:0.0} m apart at closest; landing off the grass by at most {worstStep:0.00} m; walk over the grass and stone by at least {worstClear:0.00} m; steepest {worstSlope:0.0}°. {where}");
			Assert.Greater(bridges, 0);
			Assert.LessOrEqual(worstStep, 0.25f, where);
			Assert.GreaterOrEqual(worstClear, -0.05f, where);
			Assert.Less(worstSlope, 30f, "steeper than a walker climbs");
			Assert.GreaterOrEqual(closestPosts, CloudWorldCut.PostSpacing - 1e-3f, "two bridges' posts crowd each other");
			Assert.GreaterOrEqual(bridges, plan.Bridges.Count * 9 / 10, "most planned bridges find footing");
		}

		[Test]
		public void AGasGiantsScenesAreInATemperateWetLayer()
		{
			var giant = ScriptableObject.CreateInstance<WorldBody>();
			try
			{
				giant.Kind = WorldBodyKind.GasGiant;
				giant.Atmosphere = AtmosphereKind.Thick;
				giant.Water = 0f;
				Assert.IsTrue(CloudWorld.Is(giant));
				Assert.AreEqual(CloudWorld.LayerKelvin, ClimateModel.MeanSurfaceKelvin(null, giant), 1e-6);
				Assert.AreEqual(AtmosphereKind.Standard, CloudWorld.AtmosphereOf(giant), "the layer's air is breathable");
				Assert.Greater(CloudWorld.WaterOf(giant), 0.4f, "and wet: it is cloud all round");
				BiomeWorldConditions conditions = BiomeWorldConditions.For(null, giant);
				Assert.AreEqual(AtmosphereKind.Standard, conditions.Atmosphere);
				Assert.IsTrue(conditions.HasLiquidWater, "water condenses in the layer");
			}
			finally
			{
				Object.DestroyImmediate(giant);
			}
		}
	}
}
