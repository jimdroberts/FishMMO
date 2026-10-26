using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The baked shelter map: chunk addressing either side of the origin, cover only under a surface clear of the point,
	/// and exposure that halves at a slab's edge.
	/// </summary>
	[TestFixture]
	public class SceneShelterMapTests
	{
		[Test]
		public void Locate_AddressesEveryCellOnceEitherSideOfTheOrigin()
		{
			int n = SceneShelterMap.ChunkCells;
			foreach (float x in new[] { -65.5f, -33f, -32.5f, -0.5f, 0.5f, 31.5f, 32.5f, 64.5f })
			{
				SceneShelterMap.Locate(x, 0.5f, out int chunkX, out _, out int cell);
				int cx = Mathf.FloorToInt(x / SceneShelterMap.CellMetres);
				Assert.AreEqual(cx, chunkX * n + cell % n, $"x {x}: chunk {chunkX}, column {cell % n}");
				Assert.That(cell % n, Is.InRange(0, n - 1));
			}
			Assert.AreNotEqual(SceneShelterMap.KeyOf(-1, 0), SceneShelterMap.KeyOf(0, -1), "negative chunks keep distinct keys");
		}

		[Test]
		public void ACellUnderARoof_IsCoveredBelowItAndOpenOnTop()
		{
			var map = ScriptableObject.CreateInstance<SceneShelterMap>();
			try
			{
				// One chunk at the origin, a 4 m roof at y 5 over cells x 10..13, z 10..13.
				int n = SceneShelterMap.ChunkCells;
				var heights = new ushort[n * n];
				System.Array.Fill(heights, SceneShelterMap.Open);
				for (int z = 10; z <= 13; z++)
				{
					for (int x = 10; x <= 13; x++)
					{
						heights[z * n + x] = PlanetClimateTableData.ToHalf(5f);
					}
				}
				map.Set(new[] { SceneShelterMap.KeyOf(0, 0) }, heights);

				Assert.IsTrue(map.IsCovered(new Vector3(11.5f, 1f, 11.5f)), "on the floor under the roof");
				Assert.IsFalse(map.IsCovered(new Vector3(11.5f, 5f, 11.5f)), "standing on the roof");
				Assert.IsFalse(map.IsCovered(new Vector3(20.5f, 1f, 20.5f)), "open ground");
				Assert.IsFalse(map.IsCovered(new Vector3(-5.5f, 1f, -5.5f)), "a chunk never baked");
				Assert.AreEqual(0f, map.Exposure(new Vector3(11.5f, 1f, 11.5f)), 1e-6f, "the middle is wholly sheltered");
				Assert.AreEqual(1f / 6f, map.Exposure(new Vector3(10.5f, 1f, 11.5f)), 1e-6f, "one of the six weighted samples is outside");
				Assert.AreEqual(5f / 6f, map.Exposure(new Vector3(9.5f, 1f, 11.5f)), 1e-6f, "outside, only the sample toward the roof is covered");
			}
			finally
			{
				Object.DestroyImmediate(map);
			}
		}

		[Test]
		public void TheAdminsTemperatureLimit_IsWhatTheClimateApplies()
		{
			Assert.AreEqual((float)(SceneClimate.MaxOffsetScale * ClimateModel.KelvinPerUnit), SceneClimate.MaxOffsetKelvin, 1e-4f);
			string admin = System.IO.File.ReadAllText("Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.AdminCommands.Weather.cs");
			StringAssert.Contains("SceneClimate.MaxOffsetKelvin", admin, "/admin weather air clamps to the limit the climate applies");
		}
	}
}
