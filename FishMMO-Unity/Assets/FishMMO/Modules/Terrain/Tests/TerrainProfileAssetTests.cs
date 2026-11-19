using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;

namespace FishMMO.UnitTests
{
	/// <summary>The Terrain module's saved profile (split from SeaLifeTests, Phase 4 of the core and modules plan, 2026-11-18).</summary>
	public class TerrainProfileAssetTests
	{
		[Test]
		public void NoSeaPlantIsGrass()
		{
			// Code defaults do not reach a saved profile: the prototypes must be right in the asset itself.
			var terrain = AssetDatabase.LoadAssetAtPath<TerrainRenderProfile>(ProjectFiles.Path(TerrainRenderAssets.ProfilePath));
			Assume.That(terrain, Is.Not.Null);
			// Seagrass and seaweed drawn as blades read as a lawn and dead grass on the sea floor: both were removed.
			Assert.That(terrain.Grass.IsBladePrototype("Detail_Seagrass"), Is.False);
			Assert.That(terrain.Grass.IsBladePrototype("Detail_Seaweed"), Is.False);
		}
	}
}
