#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Plain coloured terrain layers, so generated ground can be seen and read.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Flat colours rather than art. A generated terrain with no layers at all renders as one
	/// untextured grey mass in which a cliff, a beach and a snowfield are indistinguishable — you
	/// cannot tell whether the heightmap did anything, which is the one question the scene exists
	/// to answer at this stage. Four bands read at a glance, cost a handful of kilobytes, and are
	/// meant to be replaced by whatever the biome paints later.
	/// </para>
	/// <para>
	/// Made once and shared by every generated scene: they carry no per-scene information, and a
	/// set per scene would be hundreds of identical assets.
	/// </para>
	/// </remarks>
	public static class GeneratedTerrainLayers
	{
		public const string Folder = "Assets/Prefabs/Shared/TerrainLayers";

		/// <summary>How many metres of ground one tile of these colours covers.</summary>
		/// <remarks>Large, because they are flat colours: a small tile size would only cost memory to repeat the same pixel.</remarks>
		private const float TileMetres = 32f;

		/// <summary>The layers, in the order the splat weights below expect them.</summary>
		public static TerrainLayer[] Ensure()
		{
			WorldEditorAssets.EnsureFolder(Folder);
			return new[]
			{
				Layer("Generated Sand", new Color(0.76f, 0.70f, 0.50f)),
				Layer("Generated Grass", new Color(0.29f, 0.45f, 0.22f)),
				Layer("Generated Rock", new Color(0.45f, 0.42f, 0.38f)),
				Layer("Generated Snow", new Color(0.95f, 0.96f, 0.98f)),
			};
		}

		/// <summary>Indices into <see cref="Ensure"/>'s array, so the splatting reads as words.</summary>
		public const int Sand = 0;
		public const int Grass = 1;
		public const int Rock = 2;
		public const int Snow = 3;

		private static TerrainLayer Layer(string name, Color colour)
		{
			string path = $"{Folder}/{name}.terrainlayer";
			var existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
			if (existing != null)
			{
				return existing;
			}

			var layer = new TerrainLayer
			{
				diffuseTexture = Texture(name, colour),
				tileSize = new Vector2(TileMetres, TileMetres),
				// Flat colour, so no normal or specular response to fake.
				normalScale = 0f,
				specular = Color.black,
				metallic = 0f,
				smoothness = 0f,
			};
			AssetDatabase.CreateAsset(layer, path);
			return layer;
		}

		/// <summary>A small solid-colour texture. Eight pixels, not one: Unity's importer dislikes 1x1 mip chains.</summary>
		private static Texture2D Texture(string name, Color colour)
		{
			string path = $"{Folder}/{name}.png";
			var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
			if (existing != null)
			{
				return existing;
			}

			const int Size = 8;
			var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
			var pixels = new Color32[Size * Size];
			var packed = new Color32(
				(byte)(Mathf.Clamp01(colour.r) * 255f),
				(byte)(Mathf.Clamp01(colour.g) * 255f),
				(byte)(Mathf.Clamp01(colour.b) * 255f),
				255);
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = packed;
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, false);

			System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
			return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
		}

		/// <summary>
		/// The splat weights for a point, from how high and how steep it is.
		/// </summary>
		/// <param name="height01">0 the scene's lowest ground, 1 its highest.</param>
		/// <param name="steepnessDegrees">The slope there.</param>
		/// <param name="weights">Filled with one weight per layer, summing to 1.</param>
		/// <remarks>
		/// Pure, so the bands can be reasoned about without a terrain. Steepness wins over height
		/// because a cliff is rock whether it is at the shore or the summit, which is what makes
		/// the relief legible rather than a smooth colour ramp.
		/// </remarks>
		public static void Weights(float height01, float steepnessDegrees, float[] weights)
		{
			if (weights == null || weights.Length < 4)
			{
				return;
			}
			float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(28f, 48f, steepnessDegrees));
			float sand = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.16f, height01));
			float snow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.88f, height01));
			float grass = Mathf.Max(0f, 1f - sand - snow);

			// Rock takes its share from whatever the slope was going to be.
			weights[Sand] = sand * (1f - steep);
			weights[Grass] = grass * (1f - steep);
			weights[Snow] = snow * (1f - steep);
			weights[Rock] = steep;

			float total = weights[0] + weights[1] + weights[2] + weights[3];
			if (total <= 1e-5f)
			{
				weights[Rock] = 1f;
				return;
			}
			for (int i = 0; i < 4; i++)
			{
				weights[i] /= total;
			}
		}
	}
}
#endif
