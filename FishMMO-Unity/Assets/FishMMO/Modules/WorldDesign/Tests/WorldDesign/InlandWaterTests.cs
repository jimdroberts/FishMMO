using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Water;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Lakes and rivers in the running game: where the water stands and how high, one flow over the whole
	/// scene so water meets water without a seam, the surfaces drawn from it, and the sea beside them.
	/// </summary>
	[TestFixture]
	public class InlandWaterTests
	{
		private GameObject host;
		private SceneHydrology hydrology;
		private SceneWaterBodies bodies;

		/// <summary>
		/// A main river running east at y 10 → 9, a tributary coming in from the north and ending on it, a
		/// river running into a lake at level 8 that lies east of everything, and a river running into the sea.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
			hydrology.Rivers.Add(River(0, 0, new Vector3(-200f, 10f, 0f), new Vector3(200f, 9f, 0f), 12f, 1.0f, SceneHydrology.End.Edge));
			hydrology.Rivers.Add(River(1, 1, new Vector3(0f, 12f, 200f), new Vector3(0f, 9.5f, 0f), 6f, 0.8f, SceneHydrology.End.Confluence));
			hydrology.Rivers.Add(River(2, 2, new Vector3(300f, 9f, -200f), new Vector3(400f, 8f, -100f), 8f, 1.2f, SceneHydrology.End.Lake));
			hydrology.Rivers.Add(River(3, 3, new Vector3(-400f, 3f, -300f), new Vector3(-400f, 0f, -500f), 10f, 0.7f, SceneHydrology.End.Sea));
			hydrology.Rivers[1].JoinsRiver = 0;
			hydrology.Rivers[2].EndLake = 0;
			var lake = new SceneHydrology.Lake { Id = 0, Level = 8f, Bounds = new Rect(380f, -120f, 220f, 220f), MaskOrigin = new Vector2(400f, -100f), MaskCell = 4f, MaskWidth = 50, MaskHeight = 50 };
			lake.Mask = new byte[(50 * 50 + 7) / 8];
			for (int z = 0; z < 50; z++)
			{
				for (int x = 0; x < 50; x++)
				{
					int bit = z * 50 + x;
					lake.Mask[bit >> 3] |= (byte)(1 << (bit & 7));
				}
			}
			hydrology.Lakes.Add(lake);
			host = new GameObject("Inland Water Test");
			bodies = host.AddComponent<SceneWaterBodies>();
			bodies.Hydrology = hydrology;
			bodies.Build();
		}

		[TearDown]
		public void TearDown()
		{
			SurfaceWater.Unregister(bodies);
			Object.DestroyImmediate(host);
			Object.DestroyImmediate(hydrology);
		}

		private static SceneHydrology.River River(int id, int planet, Vector3 from, Vector3 to, float width, float speed, SceneHydrology.End end)
		{
			int n = 41;
			var river = new SceneHydrology.River
			{
				Id = id,
				PlanetRiver = planet,
				Perennial = true,
				Finish = end,
				Points = new Vector3[n],
				Bed = new float[n],
				Width = new float[n],
				Depth = new float[n],
				Discharge = new float[n],
				Speed = new float[n],
				Reach = new byte[n],
			};
			for (int i = 0; i < n; i++)
			{
				river.Points[i] = Vector3.Lerp(from, to, i / (float)(n - 1));
				river.Width[i] = width;
				river.Depth[i] = 1f;
				river.Bed[i] = river.Points[i].y - 1f;
				river.Speed[i] = speed;
			}
			return river;
		}

		[Test]
		public void WaterStandsInTheChannelAndOverTheLakeAndNowhereElse()
		{
			// Away from the junction, where only the main river stands.
			Assert.That(bodies.TryGetSurface(-100f, 3f, out float river), Is.True, "in the main channel");
			Assert.That(river, Is.EqualTo(9.75f).Within(0.05f), "at the river's own surface there");
			Assert.That(bodies.TryGetSurface(-100f, 9f, out _), Is.False, "past its bank");
			// Where the tributary's end lies in the main channel, the higher of the two surfaces stands.
			Assert.That(bodies.TryGetSurface(0f, 3f, out float junction), Is.True);
			Assert.That(junction, Is.GreaterThanOrEqualTo(9.5f));
			Assert.That(bodies.TryGetSurface(500f, 0f, out float lake), Is.True, "on the lake");
			Assert.That(lake, Is.EqualTo(8f));
			Assert.That(bodies.TryGetSurface(-100f, 100f, out _), Is.False, "dry ground");
			Assert.That(bodies.IsUnder(new Vector3(500f, 7f, 0f)), Is.True);
			Assert.That(bodies.IsUnder(new Vector3(500f, 9f, 0f)), Is.False);
		}

		[Test]
		public void TheWaterReachesPastItsBanksOnlyAsFarAsAsked()
		{
			// The main river is 12 m wide: banks 6 m either side of its line.
			Assert.That(bodies.TryGetSurfaceNear(-100f, 7.5f, 0f, out _), Is.False, "past the bank with no reach");
			Assert.That(bodies.TryGetSurfaceNear(-100f, 7.5f, 2f, out float near), Is.True, "within 2 m of the bank");
			Assert.That(near, Is.EqualTo(9.75f).Within(0.05f), "the river's own surface there");
			Assert.That(bodies.TryGetSurfaceNear(-100f, 9f, 5f, out _), Is.False, "the reach is capped at MaxReachMetres");
			// The lake's mask starts at x 400 inside bounds from 380.
			Assert.That(bodies.TryGetSurfaceNear(398f, 0f, 0f, out _), Is.False, "outside the lake's mask");
			Assert.That(bodies.TryGetSurfaceNear(398f, 0f, 2f, out float lake), Is.True, "within 2 m of the mask");
			Assert.That(lake, Is.EqualTo(8f));
			// Reach 0 is the plain question.
			Assert.That(bodies.TryGetSurfaceNear(-100f, 3f, 0f, out float plain) && bodies.TryGetSurface(-100f, 3f, out float same) && plain == same, Is.True);

			Assert.That(bodies.Reaches(new Rect(-150f, 7.5f, 50f, 50f)), Is.True, "a terrain beside the main river's bank");
			Assert.That(bodies.Reaches(new Rect(-150f, 8.5f, 50f, 50f)), Is.False, "a terrain past bank and reach");
			Assert.That(bodies.Reaches(new Rect(1000f, 1000f, 100f, 100f)), Is.False, "dry country");
			Assert.That(bodies.Reaches(new Rect(450f, -50f, 10f, 10f)), Is.True, "inside the lake");
		}

		[Test]
		public void TheFlowRunsWithoutAJumpFromATributaryIntoTheRiverItJoins()
		{
			// Down the tributary's line and on across the main river: the current turns from south to east, smoothly.
			Vector2 previous = bodies.FlowAt(0f, 40f, out _, out _);
			Assert.That(previous.y, Is.LessThan(-0.5f), "the tributary runs south");
			float largestStep = 0f;
			for (float z = 39.5f; z >= -5f; z -= 0.5f)
			{
				Vector2 here = bodies.FlowAt(0f, z, out _, out _);
				largestStep = Mathf.Max(largestStep, (here - previous).magnitude);
				previous = here;
			}
			Vector2 inMain = bodies.FlowAt(30f, 0f, out _, out _);
			Assert.That(inMain.x, Is.GreaterThan(0.8f), "the main river runs east");
			Assert.That(largestStep, Is.LessThan(0.25f), $"no jump in the current where they meet (largest step {largestStep:0.00} m/s in half a metre)");
		}

		[Test]
		public void ARiversCurrentCarriesOnIntoTheLakeAndDiesAway()
		{
			Vector2 near = bodies.FlowAt(410f, -90f, out _, out _);
			Vector2 farther = bodies.FlowAt(430f, -70f, out _, out _);
			Vector2 far = bodies.FlowAt(560f, 60f, out _, out _);
			Assert.That(near.magnitude, Is.GreaterThan(0.3f), "the jet past the mouth");
			Assert.That(farther.magnitude, Is.LessThan(near.magnitude), "slowing as it goes");
			Assert.That(far.magnitude, Is.LessThan(0.01f), "and gone across the lake");
			Assert.That(Vector2.Dot(near.normalized, new Vector2(1f, 1f).normalized), Is.GreaterThan(0.9f), "running on the river's way");
		}

		[Test]
		public void TheSeaAndTheInlandWaterAnswerTogether()
		{
			var sea = new FakeSea { Level = 0.5f };
			SurfaceWater.Register(sea);
			SurfaceWater.Register(bodies);
			try
			{
				Assert.That(SurfaceWater.TryGetSurfaceAt(500f, 0f, out float level), Is.True);
				Assert.That(level, Is.EqualTo(8f), "the lake stands above the sea's level");
				Assert.That(SurfaceWater.TryGetSurfaceAt(-100f, 100f, out float dry), Is.True);
				Assert.That(dry, Is.EqualTo(0.5f), "the sea's level everywhere else");
				Assert.That(SurfaceWater.TryGetInlandSurfaceAt(-100f, 100f, out _), Is.False, "no lake or river there");
				Assert.That(SurfaceWater.TryGetLevel(out float seaLevel) && seaLevel == 0.5f, Is.True, "the sea's own level");
				Assert.That(SurfaceWater.IsUnder(new Vector3(0f, 9f, 0f)), Is.True, "under the river");
				Assert.That(SurfaceWater.CurrentAt(30f, 0f).x, Is.GreaterThan(0.8f), "the river's current");
			}
			finally
			{
				SurfaceWater.Unregister(sea);
				SurfaceWater.Unregister(bodies);
			}
		}

		[Test]
		public void TheSurfacesCarryTheFlowAtTheirOwnPointsAndARiverFadesIntoTheSea()
		{
			Shader shader = Shader.Find("FishMMO/Water/Inland Water");
			Assert.That(shader, Is.Not.Null, "the inland water shader compiled and is found");
			var material = new Material(shader);
			var renderer = host.AddComponent<InlandWaterRenderer>();
			renderer.Material = material;
			try
			{
				renderer.Rebuild();
				Assert.That(renderer.Built.Count, Is.EqualTo(5), "one lake and four rivers");
				var flows = new List<Vector2>();
				var colours = new List<Color32>();
				foreach (GameObject surface in renderer.Built)
				{
					Mesh mesh = surface.GetComponent<MeshFilter>().sharedMesh;
					Vector3[] vertices = mesh.vertices;
					mesh.GetUVs(1, flows);
					for (int i = 0; i < vertices.Length; i += 7)
					{
						Vector2 expected = bodies.FlowAt(vertices[i].x, vertices[i].z, out _, out _);
						Assert.That((flows[i] - expected).magnitude, Is.LessThan(1e-4f), $"{surface.name} carries the scene's flow at its own vertex");
					}
					if (surface.name == "River 3")
					{
						mesh.GetColors(colours);
						// Column 0 and the last are the margin under the banks, transparent everywhere.
						Assert.That(colours[0].a, Is.EqualTo(0), "the margin under the bank is transparent");
						Assert.That(colours[1].a, Is.EqualTo(255), "the channel is whole upstream");
						Assert.That(colours[colours.Count - 2].a, Is.EqualTo(0), "and fades out where it meets the sea");
					}
				}
				// Drawn lake first, then larger rivers before smaller: the water a smaller one runs into wins.
				int lakeOrder = renderer.Built[0].GetComponent<MeshRenderer>().sortingOrder;
				foreach (GameObject surface in renderer.Built)
				{
					if (surface.name.StartsWith("River"))
					{
						Assert.That(surface.GetComponent<MeshRenderer>().sortingOrder, Is.GreaterThan(lakeOrder));
					}
				}
			}
			finally
			{
				Object.DestroyImmediate(renderer);
				Object.DestroyImmediate(material);
			}
		}

		/// <summary>
		/// A lake is drawn first and keeps the pixels it claims, and its surface is its mask grown by a 4 m cell. Beside a
		/// river it neither drains into nor from (a pond along a ponded stretch), those cells laid square patches of lake
		/// water over the channel (2026-10-08). It gives the channel up, along the bank's own line; at a mouth it
		/// keeps it, as before.
		/// </summary>
		[Test]
		public void ALakeGivesARiverPassingThroughItsChannelButKeepsAMouth()
		{
			Shader shader = Shader.Find("FishMMO/Water/Inland Water");
			var material = new Material(shader);
			var renderer = host.AddComponent<InlandWaterRenderer>();
			renderer.Material = material;
			// Through the middle of the lake, north to south, at its level, 6 m wide: channel x 497 … 503.
			hydrology.Rivers.Add(River(4, 4, new Vector3(500f, 8f, -150f), new Vector3(500f, 8f, 150f), 6f, 0.5f, SceneHydrology.End.Edge));
			try
			{
				renderer.Rebuild();
				GameObject lake = null;
				foreach (GameObject surface in renderer.Built)
				{
					if (surface.name == "Lake 0")
					{
						lake = surface;
					}
				}
				Assert.That(lake, Is.Not.Null);
				Mesh mesh = lake.GetComponent<MeshFilter>().sharedMesh;
				Vector3[] vertices = mesh.vertices;
				var colours = new List<Color32>();
				mesh.GetColors(colours);
				int[] triangles = mesh.triangles;
				int insideChannel = 0, mouth = 0;
				for (int i = 0; i < vertices.Length; i++)
				{
					float across = Mathf.Abs(vertices[i].x - 500f);
					if (across <= 2f)
					{
						insideChannel++;
						Assert.That(colours[i].g, Is.LessThan(128), $"the lake gives the channel up at {vertices[i]}");
					}
					else if (across >= 3.6f && vertices[i].z > -90f)
					{
						Assert.That(colours[i].g, Is.EqualTo(255), $"and keeps the water past its bank at {vertices[i]}");
					}
					// River 2 ends in the lake at (400, −100), on the line x − z = 500, 8 m wide: the lake keeps its mouth.
					if (Mathf.Abs(vertices[i].x - vertices[i].z - 500f) < 2f && vertices[i].x < 404f)
					{
						mouth++;
						Assert.That(colours[i].g, Is.EqualTo(255), $"the lake keeps the mouth of the river that ends in it at {vertices[i]}");
					}
				}
				Assert.That(insideChannel, Is.GreaterThan(0), "the lake's cells are cut finer over the channel");
				Assert.That(mouth, Is.GreaterThan(0), "the lake's ring reaches into the mouth");
				// Where the channel is cut out the lake draws nothing: no triangle wholly inside it.
				for (int t = 0; t < triangles.Length; t += 3)
				{
					float cx = (vertices[triangles[t]].x + vertices[triangles[t + 1]].x + vertices[triangles[t + 2]].x) / 3f;
					Assert.That(Mathf.Abs(cx - 500f), Is.GreaterThan(1.5f), "no lake triangle lies wholly in the channel");
				}
				// The rest of the lake is still whole 4 m quads: the four columns of cells by the channel are cut finer, not the 52.
				Assert.That(vertices.Length, Is.LessThan(53 * 53 + 5 * 8 * (52 * 8 + 1)), "only the cells by the channel are cut finer");
			}
			finally
			{
				Object.DestroyImmediate(renderer);
				Object.DestroyImmediate(material);
			}
		}

		/// <summary>
		/// A tributary ends on the centre line of the river it joins, and the carver cuts its channel with a round end of
		/// its mouth's half width past that point. Its ribbon stopped square at the point, so where its mouth was wider than
		/// the river it joins (mouths flare to 2.2 widths) the end's lobes past that river's banks were bed under no water:
		/// a dry patch at the T (2026-10-12). It runs on over the round end, whole and at its mouth's level.
		/// </summary>
		[Test]
		public void ATributarysWaterCoversTheRoundEndOfItsChannel()
		{
			Shader shader = Shader.Find("FishMMO/Water/Inland Water");
			var material = new Material(shader);
			var renderer = host.AddComponent<InlandWaterRenderer>();
			renderer.Material = material;
			try
			{
				renderer.Rebuild();
				GameObject tributary = null;
				foreach (GameObject surface in renderer.Built)
				{
					if (surface.name == "River 1")
					{
						tributary = surface;
					}
				}
				Assert.That(tributary, Is.Not.Null);
				Mesh mesh = tributary.GetComponent<MeshFilter>().sharedMesh;
				Vector3[] vertices = mesh.vertices;
				var colours = new List<Color32>();
				mesh.GetColors(colours);
				// River 1 runs south to (0, 9.5, 0), 6 m wide: its channel's round end reaches 3 m past, to z −3.
				float farthest = float.PositiveInfinity;
				for (int i = 0; i < vertices.Length; i++)
				{
					if (colours[i].g != 255 || colours[i].a != 255)
					{
						continue;
					}
					farthest = Mathf.Min(farthest, vertices[i].z);
					if (vertices[i].z < 0f)
					{
						Assert.That(vertices[i].y, Is.EqualTo(9.5f).Within(1e-3f), $"past its end the water stays at its mouth's level at {vertices[i]}");
					}
				}
				Assert.That(farthest, Is.LessThanOrEqualTo(-3f), "the water claims the whole round end past the joined river's centre line");
			}
			finally
			{
				Object.DestroyImmediate(renderer);
				Object.DestroyImmediate(material);
			}
		}

		[Test]
		public void ARiverDrainingToTheSeaIsMarkedToMeetItAtAnyTide()
		{
			Shader shader = Shader.Find("FishMMO/Water/Inland Water");
			var material = new Material(shader);
			var renderer = host.AddComponent<InlandWaterRenderer>();
			renderer.Material = material;
			hydrology.Rivers.Add(River(4, 4, new Vector3(-300f, 6f, -200f), new Vector3(-400f, 2f, -400f), 5f, 0.6f, SceneHydrology.End.Confluence));
			hydrology.Rivers[4].JoinsRiver = 3;
			try
			{
				renderer.Rebuild();
				var tides = new List<Vector2>();
				foreach (GameObject surface in renderer.Built)
				{
					surface.GetComponent<MeshFilter>().sharedMesh.GetUVs(3, tides);
					bool expected = surface.name == "River 3" || surface.name == "River 4";
					foreach (Vector2 tide in tides)
					{
						Assert.That(tide.y, Is.EqualTo(expected ? 1f : 0f), $"{surface.name}: drains to the sea, itself or through the river it joins");
						if (expected)
						{
							Assert.That(tide.x, Is.GreaterThanOrEqualTo(0.05f), $"{surface.name}: fades into the sea over a real fall");
						}
					}
				}
			}
			finally
			{
				Object.DestroyImmediate(renderer);
				Object.DestroyImmediate(material);
			}
		}

		/// <summary>
		/// A river is drawn before every river joining it, however the planet ranks them: a tributary's rows run on past its
		/// mouth across the river it joins at full strength, and drawn first they would take that river's pixels (the
		/// draw-once stencil) as a flat patch of the tributary's water. Larger rivers still come first otherwise.
		/// </summary>
		[Test]
		public void AJoinedRiverIsDrawnBeforeItsTributariesHoweverTheyRank()
		{
			var rivers = new List<SceneHydrology.River>
			{
				new SceneHydrology.River { Id = 0, PlanetRiver = 5, JoinsRiver = 1, Finish = SceneHydrology.End.Confluence },
				new SceneHydrology.River { Id = 1, PlanetRiver = 9, JoinsRiver = 2, Finish = SceneHydrology.End.Confluence },
				new SceneHydrology.River { Id = 2, PlanetRiver = 7 },
				new SceneHydrology.River { Id = 3, PlanetRiver = 1 },
				// A loop of joins (no network should have one) is drawn, not followed for ever.
				new SceneHydrology.River { Id = 4, PlanetRiver = 20, JoinsRiver = 5, Finish = SceneHydrology.End.Confluence },
				new SceneHydrology.River { Id = 5, PlanetRiver = 21, JoinsRiver = 4, Finish = SceneHydrology.End.Confluence },
			};
			List<SceneHydrology.River> order = InlandWaterRenderer.DrawOrder(rivers);
			Assert.That(order.Count, Is.EqualTo(rivers.Count), "every river is drawn once");
			int At(int id) => order.FindIndex(r => r.Id == id);
			Assert.That(At(2), Is.LessThan(At(1)), "river 1 joins river 2");
			Assert.That(At(1), Is.LessThan(At(0)), "river 0 joins river 1, though the planet ranks it first");
			Assert.That(At(3), Is.EqualTo(0), "otherwise the planet's ranking stands");
		}

		/// <summary>
		/// A ribbon's quads never twist, however its line bends. Flo Monolith's river 0 turned 146° in one point where river 1
		/// joins it (its points 446–455 here, as cut), and a bend tighter than the ribbon's half width closed its inside rows
		/// past each other: both drew quads back to front.
		/// </summary>
		[Test]
		public void ARibbonNeverFoldsOverATightBendOrAHairpin()
		{
			var hairpin = new List<Vector3>
			{
				new Vector3(-857.49f, 0f, -1470.72f), new Vector3(-858.50f, 0f, -1468.73f), new Vector3(-859.33f, 0f, -1466.66f),
				new Vector3(-860.15f, 0f, -1464.58f), new Vector3(-860.97f, 0f, -1462.50f), new Vector3(-861.51f, 0f, -1460.48f),
				new Vector3(-860.05f, 0f, -1458.78f), new Vector3(-858.70f, 0f, -1457.07f), new Vector3(-859.51f, 0f, -1454.98f),
				new Vector3(-860.31f, 0f, -1454.18f), new Vector3(-858.12f, 0f, -1454.61f), new Vector3(-855.92f, 0f, -1454.99f),
				new Vector3(-853.69f, 0f, -1455.13f), new Vector3(-851.46f, 0f, -1455.27f), new Vector3(-849.23f, 0f, -1455.32f),
			};
			var tight = new List<Vector3>();
			for (int k = 0; k <= 24; k++)
			{
				// A half turn of 3 m radius, rows a metre apart, for a ribbon reaching 6 m each side.
				float a = k / 24f * Mathf.PI;
				tight.Add(new Vector3(3f * Mathf.Cos(a), 0f, 3f * Mathf.Sin(a)));
			}
			foreach ((string name, List<Vector3> line, float half) in new[] { ("hairpin", hairpin, 5.5f), ("tight", tight, 6f) })
			{
				List<int> kept = InlandWaterRenderer.RowsThatAdvance(line);
				Assert.That(kept[0], Is.EqualTo(0), $"{name}: its first row is kept");
				Assert.That(kept[kept.Count - 1], Is.EqualTo(line.Count - 1), $"{name}: its last row is kept");
				var centres = new List<Vector3>();
				var halves = new List<float>();
				foreach (int r in kept)
				{
					centres.Add(line[r]);
					halves.Add(half);
				}
				InlandWaterRenderer.RibbonFrames(centres, halves, out Vector3[] forwards, out float[] leftReach, out float[] rightReach);
				// Laid out as RiverMesh lays them: the columns across each row, each side drawn in by its own share.
				const int across = 8;
				var rows = new Vector3[centres.Count, across + 1];
				for (int r = 0; r < centres.Count; r++)
				{
					var left = new Vector3(-forwards[r].z, 0f, forwards[r].x);
					for (int k = 0; k <= across; k++)
					{
						float offset = Mathf.Lerp(-half, half, k / (float)across);
						float scale = offset > 0f ? Mathf.Clamp01(leftReach[r] / half) : Mathf.Clamp01(rightReach[r] / half);
						rows[r, k] = centres[r] + left * (offset * scale);
					}
				}
				for (int r = 0; r + 1 < centres.Count; r++)
				{
					for (int k = 0; k < across; k++)
					{
						// Wound as RiverMesh winds them: (row, next, next + 1) and (row, next + 1, row + 1), facing up.
						Assert.That(Facing(rows[r, k], rows[r + 1, k], rows[r + 1, k + 1]), Is.GreaterThanOrEqualTo(-1e-4f), $"{name}: quad {r}.{k} is drawn back to front");
						Assert.That(Facing(rows[r, k], rows[r + 1, k + 1], rows[r, k + 1]), Is.GreaterThanOrEqualTo(-1e-4f), $"{name}: quad {r}.{k} is drawn back to front");
					}
				}
			}
			// A gentle bend keeps every row and its whole width.
			var gentle = new List<Vector3>();
			var gentleHalves = new List<float>();
			for (int k = 0; k <= 20; k++)
			{
				float a = k * 0.05f;
				gentle.Add(new Vector3(40f * Mathf.Cos(a), 0f, 40f * Mathf.Sin(a)));
				gentleHalves.Add(5f);
			}
			Assert.That(InlandWaterRenderer.RowsThatAdvance(gentle).Count, Is.EqualTo(gentle.Count));
			InlandWaterRenderer.RibbonFrames(gentle, gentleHalves, out _, out float[] gentleLeft, out float[] gentleRight);
			for (int k = 0; k < gentle.Count; k++)
			{
				Assert.That(Mathf.Min(gentleLeft[k], gentleRight[k]), Is.EqualTo(5f), $"row {k} reaches its whole half width");
			}
		}

		/// <summary>
		/// Twice a triangle's area seen from above, positive where it is wound clockwise seen from above (the face Unity
		/// draws toward the viewer, as every quad of a straight ribbon is), negative where it is drawn back to front.
		/// </summary>
		private static float Facing(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(c - a, b - a).y;

		/// <summary>
		/// A tributary's rows run on past its mouth flat at the mouth's level; they stop short of a fall on the river it joins
		/// (whose own ribbon gives the fall's pixels to the curtain, so nothing would hide them), and of that river where it
		/// stands well below the mouth.
		/// </summary>
		[Test]
		public void ATributarysRunOnStopsShortOfAFallOnTheRiverItJoins()
		{
			// The joined river runs east at 10 m, over a lip at x 6 (point 28) down to 2 m at x 8 (point 29) and on.
			var joined = new SceneHydrology.River { Id = 7, Points = new Vector3[51] };
			for (int i = 0; i < joined.Points.Length; i++)
			{
				float x = -50f + 2f * i;
				joined.Points[i] = new Vector3(x, x <= 6f ? 10f : 2f, 0f);
			}
			var falls = new List<InlandWaterRenderer.Fall> { new InlandWaterRenderer.Fall { River = 7, Lip = 28, Foot = 30 } };
			var runOn = new List<Vector3> { new Vector3(3f, 10f, 2f), new Vector3(6.5f, 10f, 0.5f), new Vector3(9f, 10f, -1f) };
			Assert.That(InlandWaterRenderer.ConfluenceRunOnRows(joined, falls, runOn, 10f), Is.EqualTo(1), "they stop where they would lie over the fall");
			var below = new List<Vector3> { new Vector3(-2f, 10f, 2f), new Vector3(12f, 10f, 0f), new Vector3(14f, 10f, -2f) };
			Assert.That(InlandWaterRenderer.ConfluenceRunOnRows(joined, null, below, 10f), Is.EqualTo(1), "and, with no fall named, where the river stands below the mouth");
			var clear = new List<Vector3> { new Vector3(-20f, 10f, 2f), new Vector3(-20f, 10f, 0f), new Vector3(-20f, 10f, -2f) };
			Assert.That(InlandWaterRenderer.ConfluenceRunOnRows(joined, falls, clear, 10f), Is.EqualTo(3), "away from the fall they all stand");
		}

		/// <summary>
		/// A ribbon built over a line that doubles back has no quad facing down: the rows where it turns back are left out
		/// and the inside of the bend is drawn in (InlandWaterRenderer.RowsThatAdvance, RibbonFrames).
		/// </summary>
		[Test]
		public void ARiversSurfaceHasNoTwistedQuadsWhereItsLineDoublesBack()
		{
			Shader shader = Shader.Find("FishMMO/Water/Inland Water");
			var material = new Material(shader);
			var renderer = host.AddComponent<InlandWaterRenderer>();
			renderer.Material = material;
			SceneHydrology.River hairpin = River(5, 5, new Vector3(-600f, 20f, 300f), new Vector3(-560f, 20f, 300f), 8f, 1f, SceneHydrology.End.Edge);
			// East 40 m, then back west-south-west: a 146° turn in one point, as Flo Monolith's river 0 took.
			for (int i = 21; i < hairpin.Points.Length; i++)
			{
				hairpin.Points[i] = hairpin.Points[20] + new Vector3(-2f * Mathf.Cos(34f * Mathf.Deg2Rad), 0f, -2f * Mathf.Sin(34f * Mathf.Deg2Rad)) * (i - 20);
			}
			hydrology.Rivers.Add(hairpin);
			try
			{
				renderer.Rebuild();
				GameObject surface = null;
				foreach (GameObject built in renderer.Built)
				{
					if (built.name == "River 5")
					{
						surface = built;
					}
				}
				Assert.That(surface, Is.Not.Null);
				Mesh mesh = surface.GetComponent<MeshFilter>().sharedMesh;
				Vector3[] vertices = mesh.vertices;
				int[] triangles = mesh.triangles;
				for (int t = 0; t < triangles.Length; t += 3)
				{
					Assert.That(Facing(vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]]), Is.GreaterThanOrEqualTo(-1e-4f), $"triangle {t / 3} faces down");
				}
			}
			finally
			{
				Object.DestroyImmediate(renderer);
				Object.DestroyImmediate(material);
			}
		}

		/// <summary>
		/// A lake's outlet runs a few rows on into the lake before the river's first point, and every one of them carries the
		/// river's speed at that first point. Only the first did; the rest took the speed at the river's mouth.
		/// </summary>
		[Test]
		public void ALakesOutletRowsCarryTheRiversStartSpeed()
		{
			Shader shader = Shader.Find("FishMMO/Water/Inland Water");
			var material = new Material(shader);
			var renderer = host.AddComponent<InlandWaterRenderer>();
			renderer.Material = material;
			// Out of the lake (level 8, east of everything) westward, slow at its outlet and fast at its far end.
			SceneHydrology.River outlet = River(6, 6, new Vector3(420f, 8f, 0f), new Vector3(300f, 7f, 100f), 6f, 1f, SceneHydrology.End.Edge);
			outlet.Start = SceneHydrology.End.Lake;
			outlet.StartLake = 0;
			for (int i = 0; i < outlet.Speed.Length; i++)
			{
				outlet.Speed[i] = Mathf.Lerp(0.4f, 2f, i / (float)(outlet.Speed.Length - 1));
			}
			hydrology.Rivers.Add(outlet);
			try
			{
				renderer.Rebuild();
				GameObject surface = null;
				foreach (GameObject built in renderer.Built)
				{
					if (built.name == "River 6")
					{
						surface = built;
					}
				}
				Assert.That(surface, Is.Not.Null);
				Mesh mesh = surface.GetComponent<MeshFilter>().sharedMesh;
				var uvs = new List<Vector2>();
				var banks = new List<Vector4>();
				mesh.GetUVs(0, uvs);
				mesh.GetUVs(4, banks);
				int lakeRows = 0;
				for (int v = 0; v < uvs.Count; v++)
				{
					if (uvs[v].x < 0f)
					{
						lakeRows++;
						Assert.That(banks[v].y, Is.EqualTo(0.4f).Within(1e-4f), $"vertex {v}, {-uvs[v].x:0.0} m into the lake, runs at the outlet's speed");
					}
				}
				Assert.That(lakeRows, Is.GreaterThan(2 * (renderer.Across + 1)), "it runs several rows on into the lake");
			}
			finally
			{
				Object.DestroyImmediate(renderer);
				Object.DestroyImmediate(material);
			}
		}

		[Test]
		public void RiversRunToTheLowestWaterTheTideCanReach()
		{
			Assert.That(new FishMMO.Shared.WorldDesign.RiverSettings().IntertidalMetres, Is.EqualTo(WaterEnvironment.DefaultMaximumTideMetres),
				"a river reaching the sea runs on to the low-water line of the largest tide a scene starts with");
		}

		private sealed class FakeSea : SurfaceWater.ISource
		{
			public float Level { get; set; }
			public float WaveHeight => 0f;
			public bool IsUnder(Vector3 point) => point.y < Level;
		}
	}
}
