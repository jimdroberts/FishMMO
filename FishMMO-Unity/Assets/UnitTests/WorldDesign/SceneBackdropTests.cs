using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The backdrop around a generated scene: how far it reaches, how far the camera must see, and
	/// that a server build never carries it.
	/// </summary>
	[TestFixture]
	public class SceneBackdropTests
	{
		[Test]
		public void AServerBuildDropsEveryClientOnlyObjectAndKeepsEverythingElse()
		{
			/* World scenes ship in the server player too. The backdrop is meshes and textures a
			 * headless server never draws, so the server build's scene processor deletes it — and
			 * must delete nothing else: the terrain, boundary and colliders are what the server
			 * plays on. */
			Scene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				var backdrop = new GameObject("Backdrop");
				SceneManager.MoveGameObjectToScene(backdrop, scene);
				backdrop.AddComponent<SceneBackdrop>();
				new GameObject("Ring 0 North").transform.SetParent(backdrop.transform);

				var terrain = new GameObject("Terrain 0_0");
				SceneManager.MoveGameObjectToScene(terrain, scene);
				var boundary = new GameObject("Scene Boundary");
				SceneManager.MoveGameObjectToScene(boundary, scene);
				boundary.AddComponent<SceneBoundary>();

				int removed = ClientOnlySceneStripper.Strip(scene);

				Assert.That(removed, Is.EqualTo(1));
				Assert.That(scene.GetRootGameObjects().Length, Is.EqualTo(2), "only the backdrop and its children go");
				Assert.That(terrain != null && boundary != null, "what the server plays on stays");
				Assert.That(backdrop == null, "the backdrop and everything under it is gone");
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		[Test]
		public void TheEditorIsNotAServerBuildUnlessTheServerSubtargetIsActive()
		{
			// Play mode and ordinary editing must never strip: the backdrop is what designers see.
			Assert.That(ClientOnlySceneStripper.IsServerBuild(),
				Is.EqualTo(UnityEditor.EditorUserBuildSettings.standaloneBuildSubtarget == UnityEditor.StandaloneBuildSubtarget.Server));
		}

		[Test]
		public void TheBackdropReachesTenKilometresButNeverFarAroundASmallGlobe()
		{
			/* A scene is a flat piece of a sphere; the further flat ground runs, the more it
			 * stretches what it shows. A 30 km atlas globe gets the full reach, a 10 km one less. */
			WorldBody body = ScriptableObject.CreateInstance<WorldBody>();
			try
			{
				body.RadiusMode = AtlasRadiusMode.Manual;
				body.ManualRadiusKm = 30f;
				var request = new SceneGenerationRequest { Body = body };
				Assert.That(SceneBackdropBuilder.ReachFor(request), Is.EqualTo(SceneBackdropBuilder.ReachMetres));

				body.ManualRadiusKm = 10f;
				Assert.That(SceneBackdropBuilder.ReachFor(request), Is.EqualTo(10000f * (float)SceneBackdropBuilder.MaximumArcRadians).Within(1f));
			}
			finally
			{
				Object.DestroyImmediate(body);
			}
		}

		[Test]
		public void TheCameraSeesTheBackdropsFarCornerFromTheSceneOppositeCorner()
		{
			/* The client camera draws to 1000 m. From one corner of the scene, the backdrop's far
			 * corner is the scene's width plus the reach away on each axis. */
			TerrainTilePlan plan = SceneGeneration.PlanTiles(new Vector2(6.5f, 5.416667f));
			float far = SceneBackdropBuilder.FarPlaneFor(plan, 10000f);
			float corner = Mathf.Sqrt(Mathf.Pow(plan.WidthMetres + 10000f, 2f) + Mathf.Pow(plan.DepthMetres + 10000f, 2f));
			Assert.That(far, Is.GreaterThanOrEqualTo(corner));
			Assert.That(far, Is.LessThan(corner * 1.1f), "and not much further, which costs depth precision");
		}
	}
}
