using UnityEditor;
using UnityEngine;

namespace FishMMO.EditorTools
{
	/// <summary>
	/// One-shot tool that gives every world-scene terrain a tiled ground texture.
	/// </summary>
	/// <remarks>
	/// TerrainData assets are binary even with ForceText serialization, so terrain layers
	/// cannot be authored by editing YAML the way scene materials can — this has to run
	/// inside the editor. Runnable headlessly:
	/// <c>Unity -batchmode -nographics -quit -projectPath . -executeMethod
	/// FishMMO.EditorTools.TerrainGroundPainter.PaintAll -logFile &lt;abs-path&gt;</c>.
	/// Idempotent: re-running reuses existing layer assets and repaints the same fill.
	/// </remarks>
	public static class TerrainGroundPainter
	{
		private const string GrassLayerPath = "Assets/Textures/Ground/GrassLayer.terrainlayer";
		private const string StoneLayerPath = "Assets/Textures/Ground/StoneLayer.terrainlayer";
		private const string GrassTexturePath = "Assets/Textures/Ground/Grass.png";
		private const string StoneTexturePath = "Assets/Textures/Ground/Stone.png";

		[MenuItem("FishMMO/World/Paint Terrain Ground Layers")]
		public static void PaintAll()
		{
			TerrainLayer grass = GetOrCreateLayer(GrassLayerPath, GrassTexturePath);
			TerrainLayer stone = GetOrCreateLayer(StoneLayerPath, StoneTexturePath);

			// Outdoor zones get grass, dungeons get stone.
			Paint("Assets/Prefabs/Shared/Terrain/Felwithe.asset", grass);
			Paint("Assets/Prefabs/Shared/Terrain/GreaterFaydark.asset", grass);
			Paint("Assets/Prefabs/Shared/Terrain/Crushbone.asset", stone);
			Paint("Assets/Prefabs/Shared/Terrain/TestDungeon.asset", stone);

			AssetDatabase.SaveAssets();
			Debug.Log("TerrainGroundPainter: painted 4 terrains.");
		}

		private static TerrainLayer GetOrCreateLayer(string layerPath, string texturePath)
		{
			TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
			if (layer == null)
			{
				layer = new TerrainLayer();
				AssetDatabase.CreateAsset(layer, layerPath);
			}

			Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
			if (texture == null)
			{
				throw new System.InvalidOperationException($"Missing ground texture at {texturePath}");
			}

			layer.diffuseTexture = texture;
			// 32px pixel-art tile repeating every 4m reads as ground rather than noise
			// from the third-person camera distance.
			layer.tileSize = new Vector2(4.0f, 4.0f);
			layer.tileOffset = Vector2.zero;
			EditorUtility.SetDirty(layer);
			return layer;
		}

		private static void Paint(string terrainDataPath, TerrainLayer layer)
		{
			TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainDataPath);
			if (data == null)
			{
				Debug.LogWarning($"TerrainGroundPainter: no TerrainData at {terrainDataPath}, skipped.");
				return;
			}

			data.terrainLayers = new TerrainLayer[] { layer };

			/* A single layer does not reliably imply full weight on old splat data —
			 * explicitly fill the alphamap so the result does not depend on whatever the
			 * asset previously stored. */
			int res = data.alphamapResolution;
			float[,,] weights = new float[res, res, 1];
			for (int y = 0; y < res; ++y)
			{
				for (int x = 0; x < res; ++x)
				{
					weights[y, x, 0] = 1.0f;
				}
			}
			data.SetAlphamaps(0, 0, weights);
			EditorUtility.SetDirty(data);
		}
	}
}
