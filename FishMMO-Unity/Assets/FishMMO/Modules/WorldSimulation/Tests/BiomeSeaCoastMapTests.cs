using FishMMO.Shared.Biomes;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The coastal look kept with a scene's biome map (2026-10-18, phase I2k): baked from the grid as it stands, stored on the
	/// asset, dropped whenever the grid is replaced. Needs Unity (a ScriptableObject); the bake itself is pure and tested in
	/// BiomeSeaCoastAndFieldClaimTests.
	/// </summary>
	public class BiomeSeaCoastMapTests
	{
		private const int Bog = 7, Sea = 9;

		[Test]
		public void Map_StoresItsCoastLook_AndDropsItWithTheGrid()
		{
			var map = ScriptableObject.CreateInstance<SceneBiomeMap>();
			try
			{
				int w = 40, h = 20;
				var ids = new int[w * h];
				for (int i = 0; i < ids.Length; i++)
				{
					ids[i] = i % w < w / 2 ? Bog : Sea;
				}
				var peat = new BiomeSeaLook(new Color(0.18f, 0.12f, 0.05f, 1f), 6f, 1.6f);
				map.Set(w, h, ids, new Vector2(-500f, -250f), new Vector2(1000f, 500f));
				Assert.That(map.HasCoastLook, Is.False, "a new grid has none until it is baked");

				map.BakeCoastLook(id => id == Bog ? peat : id == Sea ? BiomeSeaLooks.Today : (BiomeSeaLook?)null);
				Assert.That(map.HasCoastLook, Is.True);
				BiomeSeaCoast.Bake stored = map.CoastLookBake;
				BiomeSeaCoast.Bake direct = BiomeSeaCoast.For(w, h, ids, new Vector2(-500f, -250f), new Vector2(1000f, 500f),
					id => id == Bog ? peat : id == Sea ? BiomeSeaLooks.Today : (BiomeSeaLook?)null);
				Assert.That(stored.Texels, Is.EqualTo(direct.Texels));
				Assert.That(stored.Rect, Is.EqualTo(direct.Rect));

				map.BakeCoastLook(id => BiomeSeaLooks.Today);
				Assert.That(map.HasCoastLook, Is.False, "every look today's: nothing stored");

				map.BakeCoastLook(id => id == Bog ? peat : (BiomeSeaLook?)null);
				Assert.That(map.HasCoastLook, Is.True);
				map.Set(w, h, ids, new Vector2(-500f, -250f), new Vector2(1000f, 500f));
				Assert.That(map.HasCoastLook, Is.False, "replacing the grid drops the old grid's coast");
				Assert.That(map.CoastLook, Is.Empty);
			}
			finally
			{
				Object.DestroyImmediate(map);
			}
		}
	}
}
