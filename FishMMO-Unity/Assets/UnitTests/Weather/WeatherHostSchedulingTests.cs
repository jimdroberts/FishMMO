using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Server.Implementation.World.SceneServer.Weather;
using FishMMO.Shared.Biomes;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The weather host's scene rotation and resync rule, and the terrain lookup every weather and
	/// biome sample goes through.
	/// </summary>
	/// <remarks>
	/// The rotation and the resync rule are pure functions, so they are driven here with a
	/// hand-moved frame clock rather than a running server.
	/// </remarks>
	[TestFixture]
	public class WeatherHostSchedulingTests
	{
		/// <summary>Frames at a steady rate; returns how many passes each scene got.</summary>
		private static int[] RunRotation(int sceneCount, float frameSeconds, int frames)
		{
			int[] passes = new int[sceneCount];
			float credits = 0f;
			int cursor = 0;
			for (int frame = 0; frame < frames; ++frame)
			{
				int run = WeatherHost.ScenePassesThisFrame(ref credits, sceneCount, frameSeconds);
				Assert.LessOrEqual(run, Mathf.Max(1, (sceneCount + WeatherHost.MinimumFramesPerScenePass - 1) / WeatherHost.MinimumFramesPerScenePass),
					"More passes on one frame than the cap allows.");
				for (int i = 0; i < run; ++i)
				{
					passes[cursor] += 1;
					cursor = (cursor + 1) % sceneCount;
				}
			}
			return passes;
		}

		[TestCase(1)]
		[TestCase(10)]
		[TestCase(60)]
		[TestCase(100)]
		public void EveryScene_GetsAboutOnePassASecond(int sceneCount)
		{
			// Ten seconds at 60 fps.
			int[] passes = RunRotation(sceneCount, 1f / 60f, 600);

			for (int i = 0; i < sceneCount; ++i)
			{
				Assert.That(passes[i], Is.InRange(9, 11), $"Scene {i} of {sceneCount} ran {passes[i]} passes in ten seconds.");
			}
		}

		[Test]
		public void AFewScenes_NeverShareAFrame()
		{
			/* The point of the rotation: every scene's pass used to land on the same frame once a
			 * second. Up to thirty scenes, one pass a frame at most. */
			float credits = 0f;
			for (int frame = 0; frame < 600; ++frame)
			{
				Assert.LessOrEqual(WeatherHost.ScenePassesThisFrame(ref credits, 20, 1f / 60f), 1);
			}
		}

		[Test]
		public void AHitch_OwesEachSceneOnePass_AndDrainsItOneAFrame()
		{
			float credits = 0f;

			Assert.AreEqual(1, WeatherHost.ScenePassesThisFrame(ref credits, 10, 5f), "Not every scene at once.");
			Assert.AreEqual(9f, credits, 1e-4f, "A five-second hitch owes each scene one pass, not five.");

			for (int frame = 0; frame < 9; ++frame)
			{
				Assert.AreEqual(1, WeatherHost.ScenePassesThisFrame(ref credits, 10, 1f / 60f), "The backlog drains one scene a frame.");
			}
		}

		[Test]
		public void NoScenes_RunNothingAndOweNothing()
		{
			float credits = 5f;
			Assert.AreEqual(0, WeatherHost.ScenePassesThisFrame(ref credits, 0, 1f));
			Assert.AreEqual(0f, credits);
		}

		[Test]
		public void ResyncWanted_RefusesOnlyARequestThatIsAlreadyCurrent()
		{
			Assert.IsFalse(WeatherHost.ResyncWanted(7u, 7u), "The client already has everything sent.");
			Assert.IsTrue(WeatherHost.ResyncWanted(5u, 7u), "A real gap is answered.");
			Assert.IsTrue(WeatherHost.ResyncWanted(9u, 7u), "Another scene's counter: answered, not guessed at.");
		}

		// --- Terrain lookup ---------------------------------------------------------------------

		[Test]
		public void TheSnapshot_IsComparedOncePerFrame_AndOnAMiss()
		{
			Assert.IsTrue(SceneTerrainExtent.MustCompareSnapshot(5, 4, cacheMiss: false), "A new frame compares.");
			Assert.IsFalse(SceneTerrainExtent.MustCompareSnapshot(5, 5, cacheMiss: false), "The same frame trusts it.");
			Assert.IsTrue(SceneTerrainExtent.MustCompareSnapshot(5, 5, cacheMiss: true),
				"A scene with no measurement may have loaded after this frame's comparison.");
		}

		[Test]
		public void OutsidePlayMode_TheLookupSeesTerrainAddedMovedAndRemoved()
		{
			/* Tools add, move and resize terrain between calls within one editor frame, so nothing
			 * is kept outside play mode: each answer is measured from the terrain as it is. Placed
			 * hundreds of kilometres out, so no terrain of whatever scene is open can answer for it. */
			Scene scene = SceneManager.GetActiveScene();
			var data = new TerrainData { heightmapResolution = 33, size = new Vector3(100f, 50f, 100f) };
			GameObject host = null;
			try
			{
				Vector3 probe = new Vector3(250050f, 0f, 250050f);
				Assert.IsFalse(BiomeSampler.TrySampleTerrainHeight(probe, scene, out _), "Nothing there yet.");

				host = new GameObject("WeatherHostSchedulingTests Terrain");
				host.transform.position = new Vector3(250000f, 10f, 250000f);
				Terrain terrain = host.AddComponent<Terrain>();
				terrain.terrainData = data;

				Assert.IsTrue(BiomeSampler.TrySampleTerrainHeight(probe, scene, out float height), "A terrain added this frame is found.");
				Assert.That(height, Is.InRange(0f, 1f));
				Assert.IsTrue(SceneTerrainExtent.Of(scene).Found);
				SceneTerrainExtent.TilesOf(scene, out SceneTerrainExtent extent);
				Assert.AreEqual(extent.TileCount, SceneTerrainExtent.Of(scene).TileCount, "One measurement answers both.");

				host.transform.position = new Vector3(500000f, 10f, 500000f);
				Assert.IsFalse(BiomeSampler.TrySampleTerrainHeight(probe, scene, out _), "A terrain moved away is not still found where it was.");

				Object.DestroyImmediate(host);
				host = null;
				Assert.IsFalse(BiomeSampler.TrySampleTerrainHeight(new Vector3(500050f, 0f, 500050f), scene, out _), "A destroyed terrain is gone.");
			}
			finally
			{
				if (host != null)
				{
					Object.DestroyImmediate(host);
				}
				Object.DestroyImmediate(data);
				SceneTerrainExtent.Invalidate();
			}
		}
	}
}
