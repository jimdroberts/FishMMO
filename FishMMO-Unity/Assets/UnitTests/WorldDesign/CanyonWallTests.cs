using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Canyon walls: a plateau's steep riser rebuilt as a standing rock face, sealed to the terrain at
	/// its edges, only where the ground is steep.
	/// </summary>
	[TestFixture]
	public class CanyonWallTests
	{
		private const int Samples = 201;
		private const float Cell = 2f;

		/// <summary>Two flat benches 20 m apart, joined by a riser across X about 12 m wide.</summary>
		private static SceneHeightField Step(out TerrainTilePlan plan)
		{
			plan = new TerrainTilePlan { CountX = 1, CountZ = 1, TileMetres = (Samples - 1) * Cell, Resolution = Samples };
			var metres = new float[Samples * Samples];
			for (int z = 0; z < Samples; z++)
			{
				for (int x = 0; x < Samples; x++)
				{
					float t = Mathf.Clamp01((x - 97) / 6f);
					metres[z * Samples + x] = 70f - 20f * t * t * (3f - 2f * t);
				}
			}
			return SceneHeightField.FromMetres(plan, metres);
		}

		private static SceneGeologyGrid Granite(SceneHeightField field)
		{
			// Massive rock: no beds to jut or recess, so the face's shape is the standing-up alone.
			int granite = System.Array.FindIndex(PlanetGeology.Lithologies, l => l.Name == "Granite");
			return SceneGeologyGrid.Build(field, (east, north) => new GeologyColumn(1, granite, 0f, 1e6f, 0f, 0f, 1));
		}

		private static CanyonWallReport BuildWalls(SceneHeightField field, TerrainTilePlan plan, out Scene scene, out GameObject root)
		{
			var weight = new float[field.Metres.Length];
			for (int i = 0; i < weight.Length; i++)
			{
				weight[i] = 1f;
			}
			scene = EditorSceneManager.NewPreviewScene();
			CanyonWallReport report = CanyonWalls.Build(scene, new Terrain[1, 1], plan, field, weight, Granite(field), null,
				new CanyonWallOptions { RoughnessMetres = 0f });
			root = null;
			foreach (GameObject go in scene.GetRootGameObjects())
			{
				if (go.name == CanyonWalls.RootName)
				{
					root = go;
				}
			}
			return report;
		}

		[Test]
		public void ASteepRiserStandsUpIntoAWallSealedToTheTerrain()
		{
			SceneHeightField field = Step(out TerrainTilePlan plan);
			CanyonWallReport report = BuildWalls(field, plan, out Scene scene, out GameObject root);
			try
			{
				Assert.That(report.Cells, Is.GreaterThan(0), "the riser is steep enough to become wall");
				Assert.That(root, Is.Not.Null);

				float midLow = float.MaxValue, midHigh = float.MinValue;
				int pinned = 0, edge = 0;
				foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>())
				{
					foreach (Vector3 v in collider.sharedMesh.vertices)
					{
						if (v.y > 56f && v.y < 64f)
						{
							midLow = Mathf.Min(midLow, v.x);
							midHigh = Mathf.Max(midHigh, v.x);
						}
						/* At the wall's top and foot nothing moves: every vertex sits where the terrain has
						 * it, on the face's lattice of half cells (two quads to a cell). */
						if (v.y > 69.9f || v.y < 50.1f)
						{
							edge++;
							float lattice = (v.x + plan.WidthMetres * 0.5f) / Cell * new CanyonWallOptions().Subdivision;
							if (Mathf.Abs(lattice - Mathf.Round(lattice)) < 1e-3f)
							{
								pinned++;
							}
						}
					}
				}
				// On the terrain the middle eight metres of height spread across about four metres of ground.
				Assert.That(midHigh - midLow, Is.LessThan(1.5f), $"the face should stand up: its middle spreads {midHigh - midLow:0.00} m");
				Assert.That(edge, Is.GreaterThan(0));
				Assert.That(pinned, Is.EqualTo(edge), "every vertex at the wall's top and foot sits on a terrain sample");
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		[Test]
		public void FlatGroundHasNoWalls()
		{
			var plan = new TerrainTilePlan { CountX = 1, CountZ = 1, TileMetres = (Samples - 1) * Cell, Resolution = Samples };
			SceneHeightField field = SceneHeightField.FromMetres(plan, new float[Samples * Samples]);
			CanyonWallReport report = BuildWalls(field, plan, out Scene scene, out GameObject root);
			try
			{
				Assert.That(report.Cells, Is.EqualTo(0));
				Assert.That(root, Is.Null);
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}
	}
}
