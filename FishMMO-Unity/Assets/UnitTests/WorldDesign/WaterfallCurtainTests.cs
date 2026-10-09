using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using FishMMO.Water;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The falls' hydraulics against the worked figures of the waterfall plan (Castillo &amp; Carrillo 2016), and the curtain
	/// traced down a 50 m cliff: thrown out as far as its brink speed carries it, and striking a ledge that exists only as a
	/// baked prop's collision (no live collider), as every prop in a real scene does when the falls are built.
	/// </summary>
	[TestFixture]
	public class WaterfallCurtainTests
	{
		// ── Hydraulics ──────────────────────────────────────────────────

		[Test]
		public void TheBrinkOfASmallRiverMatchesTheWorkedExample()
		{
			// A river 0.6 m deep at 1 m/s: q 0.6 m²/s.
			const float q = 0.6f;
			Assert.That(FallHydraulics.CriticalDepth(q), Is.EqualTo(0.332f).Within(0.002f));
			Assert.That(FallHydraulics.BrinkDepth(q), Is.EqualTo(0.2376f).Within(0.002f));
			Assert.That(FallHydraulics.BrinkSpeed(q), Is.EqualTo(2.525f).Within(0.02f));
		}

		[Test]
		public void BreakUpLengthAgreesWithHoreniAcrossTrickleToRiver()
		{
			Assert.That(FallHydraulics.BreakupLength(0.6f, 0.013f), Is.EqualTo(6.47f).Within(0.05f));
			Assert.That(FallHydraulics.BreakupLengthHoreni(0.6f), Is.EqualTo(5.10f).Within(0.02f));
			foreach (float q in new[] { 0.05f, 0.2f, 0.6f, 1.33f, 5f })
			{
				float castillo = FallHydraulics.BreakupLength(q), horeni = FallHydraulics.BreakupLengthHoreni(q);
				Assert.That(castillo / horeni, Is.InRange(0.5f, 2f), $"q {q}: Castillo {castillo:0.00} m against Horeni {horeni:0.00} m");
			}
		}

		[Test]
		public void SpreadAndImpactThicknessMatchTheWorkedExample()
		{
			// 51 m: ξ ≈ 0.2 m a side at T_u 0.013 and 0.76 m at 0.05; B_j ≈ 0.42 m.
			Assert.That(FallHydraulics.Spread(0.6f, 51f, 0.013f), Is.EqualTo(0.198f).Within(0.005f));
			Assert.That(FallHydraulics.Spread(0.6f, 51f, 0.05f), Is.EqualTo(0.76f).Within(0.01f));
			Assert.That(FallHydraulics.JetThickness(0.6f, 51f, 0.013f), Is.EqualTo(0.414f).Within(0.01f));
			// Nothing spreads until the fall is deeper than twice the head, and the core is the brink's depth at the lip.
			Assert.That(FallHydraulics.Spread(0.6f, 0.5f), Is.EqualTo(0f));
			Assert.That(FallHydraulics.CoreThickness(0.6f, 0f), Is.EqualTo(FallHydraulics.BrinkDepth(0.6f)).Within(1e-4f));
		}

		[Test]
		public void AWideFallSpreadsByMetresNotByItsWidth()
		{
			// The old curtain grew 2 % of its own width a metre: a 30 m fall 60 m wide at its foot. The spread is the water's, not the width's.
			Assert.That(FallHydraulics.Spread(1f, 50f), Is.LessThan(1.5f));
		}

		// ── The traced curtain ─────────────────────────────────────────

		private const float Plateau = 60f, Base = 10f, Size = 128f;

		[SetUp]
		public void OpenScene()
		{
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		}

		[TearDown]
		public void CloseScene()
		{
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		}

		[Test]
		public void TheCurtainIsThrownOutAsFarAsItsBrinkSpeedCarriesIt()
		{
			BuildCliff();
			InlandWaterRenderer renderer = BuildWater(out _);
			Assert.That(renderer.Falls.Count, Is.EqualTo(1));
			InlandWaterRenderer.Fall fall = renderer.Falls[0];
			Assert.That(fall.Speed, Is.EqualTo(FallHydraulics.BrinkSpeed(fall.UnitDischarge)).Within(1e-3f));
			float drop = fall.LipPoint.y - fall.Landing.y;
			float freeThrow = fall.Speed * Mathf.Sqrt(2f * drop / FallHydraulics.Gravity);
			float thrown = fall.Landing.z - fall.LipPoint.z;
			// Drag on the broken water shortens the throw a little; it never lands back against the cliff, nor beyond the free throw.
			Assert.That(thrown, Is.InRange(0.5f * freeThrow, 1.05f * freeThrow), $"thrown {thrown:0.00} m against a free throw of {freeThrow:0.00} m");
			Assert.That(fall.Parts.Count, Is.EqualTo(1), "nothing stands in its way: one fall, one landing");
			Assert.That(fall.Strikes == null || fall.Strikes.Count == 0, "a plunge fall clear of its cliff strikes nothing");
			Assert.That(Mathf.Abs(fall.Landing.x), Is.LessThan(0.5f), "it lands under its own lip, not aside");
		}

		[Test]
		public void ALedgeInTheCollisionSetOnlyIsStruckByTheCurtain()
		{
			BuildCliff();
			// A ledge 4 m below the lip, out 4.5 m from the face, under the left half of the fall: baked collision only.
			var owner = new GameObject(ScenePropColliders.ObjectName).AddComponent<ScenePropColliders>();
			var set = ScriptableObject.CreateInstance<ScenePropCollisionSet>();
			set.Source = "Test";
			set.Prototypes = new[] { new ScenePropCollisionSet.Prototype { Mesh = BoxMesh(), Layer = 0 } };
			set.Instances = new[]
			{
				new ScenePropCollisionSet.Instance { Prototype = 0, Position = new Vector3(1.6f, Plateau - 5f, 1.75f), Rotation = Quaternion.identity, Scale = new Vector3(3.2f, 1.2f, 4.5f) },
			};
			owner.Sets.Add(set);
			Assert.That(Physics.OverlapSphere(new Vector3(1.6f, Plateau - 5f, 1.75f), 2f), Is.Empty, "the ledge must not be in physics: the falls are built before any prop chunk is streamed in");

			InlandWaterRenderer renderer = BuildWater(out _);
			InlandWaterRenderer.Fall fall = renderer.Falls[0];
			Assert.That(fall.Strikes, Is.Not.Null.And.Not.Empty, "the streams over the ledge strike it");
			foreach (InlandWaterRenderer.Strike strike in fall.Strikes)
			{
				Assert.That(strike.Point.x, Is.GreaterThan(-0.5f), $"a strike at {strike.Point} is not under the ledge");
			}
			// The water that struck the ledge is thrown off its front, so the left half lands further out, or apart.
			Assert.That(fall.Parts.Count > 1 || Mathf.Abs(fall.Landing.x) > 0.2f, $"the ledge changed nothing where the water lands ({fall.Parts.Count} part(s), landing {fall.Landing})");
		}

		[Test]
		public void TheFallsArePublishedForTheMistTheWetRockAndTheRoar()
		{
			BuildCliff();
			InlandWaterRenderer renderer = BuildWater(out _);
			bool mine = false;
			foreach (Waterfalls.Fall published in Waterfalls.All)
			{
				if (Vector3.Distance(published.Lip, renderer.Falls[0].LipPoint) < 0.01f)
				{
					mine = true;
					Assert.That(published.Power, Is.EqualTo(Waterfalls.PowerOf(published.Discharge, published.Drop)).Within(1f));
					Assert.That(Waterfalls.PlumeRadius(published), Is.GreaterThan(0.5f * published.Width));
				}
			}
			Assert.That(mine, "the fall is in the registry");
			Object.DestroyImmediate(renderer.gameObject);
			foreach (Waterfalls.Fall published in Waterfalls.All)
			{
				Assert.That(Vector3.Distance(published.Lip, new Vector3(0f, Plateau - 0.25f, -0.2f)), Is.GreaterThan(0.01f), "a destroyed renderer's falls are withdrawn");
			}
		}

		/// <summary>A unit box, readable, wound outward: a baked prop's collision mesh.</summary>
		private static Mesh BoxMesh()
		{
			GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Mesh source = cube.GetComponent<MeshFilter>().sharedMesh;
			var mesh = new Mesh { vertices = source.vertices, triangles = source.triangles };
			mesh.RecalculateBounds();
			Object.DestroyImmediate(cube);
			return mesh;
		}

		/// <summary>A 50 m cliff: a plateau to z = 0, then the base and a pool, with no colliders but the terrain's.</summary>
		private static void BuildCliff()
		{
			const int res = 257;
			float cell = Size / (res - 1);
			var heights = new float[res, res];
			for (int zi = 0; zi < res; zi++)
			{
				float z = -0.5f * Size + zi * cell;
				for (int xi = 0; xi < res; xi++)
				{
					heights[zi, xi] = (z <= 0f ? Plateau - 1f : Base - 4f) / 80f;
				}
			}
			var data = new TerrainData { heightmapResolution = res, size = new Vector3(Size, 80f, Size) };
			data.SetHeights(0, 0, heights);
			GameObject go = Terrain.CreateTerrainGameObject(data);
			go.transform.position = new Vector3(-0.5f * Size, 0f, -0.5f * Size);
		}

		/// <summary>One river down a channel to the lip, over the cliff, into the pool; the inland water built over it.</summary>
		private static InlandWaterRenderer BuildWater(out SceneHydrology hydrology)
		{
			var points = new List<Vector3>();
			var reach = new List<byte>();
			for (float z = -40f; z < -0.4f; z += 2f)
			{
				points.Add(new Vector3(0f, Plateau - 0.25f, z));
				reach.Add(0);
			}
			points.Add(new Vector3(0f, Plateau - 0.25f, -0.2f));
			reach.Add(0);
			points.Add(new Vector3(0f, Plateau - 20f, 0.8f));
			reach.Add(InlandWaterRenderer.FallReach);
			points.Add(new Vector3(0f, Base - 1.8f, 1.6f));
			reach.Add(InlandWaterRenderer.FallReach);
			for (float z = 4f; z <= 40f; z += 2f)
			{
				points.Add(new Vector3(0f, Base - 1.8f, z));
				reach.Add(0);
			}
			int n = points.Count;
			var river = new SceneHydrology.River
			{
				Id = 0,
				Perennial = true,
				Points = points.ToArray(),
				Bed = new float[n],
				Width = new float[n],
				Depth = new float[n],
				Discharge = new float[n],
				Speed = new float[n],
				Reach = reach.ToArray(),
			};
			for (int i = 0; i < n; i++)
			{
				river.Width[i] = 6f;
				river.Depth[i] = 0.9f;
				river.Bed[i] = points[i].y - 0.9f;
				river.Discharge[i] = 8f;
				river.Speed[i] = 1.4f;
			}
			hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
			hydrology.Rivers.Add(river);
			var host = new GameObject("Inland Water");
			host.AddComponent<SceneWaterBodies>().Hydrology = hydrology;
			var renderer = host.AddComponent<InlandWaterRenderer>();
			renderer.Material = new Material(Shader.Find(SceneGenerator.InlandWaterShaderName));
			renderer.FallMaterial = new Material(Shader.Find(SceneGenerator.WaterfallShaderName));
			renderer.Rebuild();
			return renderer;
		}
	}
}
