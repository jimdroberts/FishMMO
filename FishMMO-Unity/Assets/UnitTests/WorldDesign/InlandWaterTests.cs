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
