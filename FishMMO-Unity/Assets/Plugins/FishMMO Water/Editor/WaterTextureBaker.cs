#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Water.Editor
{
	/// <summary>
	/// Bakes the two tiling textures the water shader needs: a wave normal map and a foam mask.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why textures rather than maths in the shader.</b> An earlier version summed four
	/// sinusoids per pixel for its ripples. Sinusoids are coherent — they stay in step with
	/// themselves across the whole sea — so however the amplitude was tuned the surface read as
	/// corduroy: regular parallel ribbing that the eye locks onto immediately. Real capillary waves
	/// are broadband and isotropic, which is exactly what a noise-built normal map is and exactly
	/// what a small sum of sinusoids can never be.
	/// </para>
	/// <para>
	/// <b>Seamless by construction.</b> The noise lattice wraps at the texture's period, so every
	/// octave tiles and the map can be scrolled forever without a seam. Two copies of it at
	/// different scales, speeds and directions are what the shader mixes; because both come from
	/// the same seamless source, the mix is seamless too.
	/// </para>
	/// </remarks>
	public static class WaterTextureBaker
	{
		public const string Folder = "Assets/Plugins/FishMMO Water/Textures";
		public const string NormalPath = Folder + "/WaterNormal.png";
		public const string FoamPath = Folder + "/WaterFoam.png";

		private const int Size = 512;

		[MenuItem("FishMMO/Water/Bake Water Textures")]
		public static void Bake()
		{
			Directory.CreateDirectory(Folder);
			BakeNormal();
			BakeFoam();
			AssetDatabase.Refresh();
			Assign();
			Debug.Log($"[Water] Baked {NormalPath} and {FoamPath}.");
		}

		/// <summary>Puts the baked maps on the shared ocean material, so nothing has to be wired by hand.</summary>
		public static void Assign()
		{
			var material = AssetDatabase.LoadAssetAtPath<Material>(
				"Assets/Plugins/FishMMO Water/Materials/OceanWater.mat");
			if (material == null)
			{
				return;
			}
			var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
			var foam = AssetDatabase.LoadAssetAtPath<Texture2D>(FoamPath);
			if (normal != null)
			{
				material.SetTexture("_NormalMap", normal);
			}
			if (foam != null)
			{
				material.SetTexture("_FoamTexture", foam);
			}
			EditorUtility.SetDirty(material);
			AssetDatabase.SaveAssets();
		}

		/// <summary>The wave normal map: the gradient of a summed, domain-warped noise height field.</summary>
		private static void BakeNormal()
		{
			var height = new float[Size, Size];
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					float u = x / (float)Size;
					float v = y / (float)Size;

					/* Domain warp first. Without it the octaves stack into something that still
					 * reads as a grid of blobs; warped, the crests curve and fork the way wind
					 * chop actually does. */
					float wx = Fbm(u, v, 3, 3f, 0) * 0.35f;
					float wy = Fbm(u, v, 3, 3f, 7919) * 0.35f;
					height[y, x] = Fbm(u + wx, v + wy, 5, 5f, 104729);
				}
			}

			var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true);
			var pixels = new Color32[Size * Size];
			// Slope, in the same units the height is in. 3.5 is what makes the map read as water
			// rather than as crumpled foil at the tiling scale the shader uses.
			const float Strength = 3.5f;
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					// Wrapped neighbours, so the derivative is seamless too.
					float left = height[y, (x - 1 + Size) % Size];
					float right = height[y, (x + 1) % Size];
					float down = height[(y - 1 + Size) % Size, x];
					float up = height[(y + 1) % Size, x];

					var normal = new Vector3((left - right) * Strength, (down - up) * Strength, 1f).normalized;
					pixels[y * Size + x] = new Color32(
						(byte)Mathf.Clamp(Mathf.RoundToInt((normal.x * 0.5f + 0.5f) * 255f), 0, 255),
						(byte)Mathf.Clamp(Mathf.RoundToInt((normal.y * 0.5f + 0.5f) * 255f), 0, 255),
						(byte)Mathf.Clamp(Mathf.RoundToInt((normal.z * 0.5f + 0.5f) * 255f), 0, 255),
						255);
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply();
			Write(texture, NormalPath);
			Object.DestroyImmediate(texture);

			var importer = AssetImporter.GetAtPath(NormalPath) as TextureImporter;
			if (importer != null)
			{
				importer.textureType = TextureImporterType.NormalMap;
				importer.wrapMode = TextureWrapMode.Repeat;
				importer.filterMode = FilterMode.Trilinear;
				importer.anisoLevel = 8;
				importer.mipmapEnabled = true;
				importer.SaveAndReimport();
			}
		}

		/// <summary>The foam mask: blotches, not a cloud. Foam clumps and tears; it does not fade.</summary>
		private static void BakeFoam()
		{
			var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true);
			var pixels = new Color32[Size * Size];
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					float u = x / (float)Size;
					float v = y / (float)Size;
					float coarse = Fbm(u, v, 4, 4f, 60013);
					float fine = Fbm(u, v, 3, 12f, 15485863);
					/* Contrast, deliberately hard. A smooth noise used as a foam mask makes a grey
					 * haze; foam in life is bubbles and holes, so the mask has to have edges. */
					float value = Mathf.Clamp01((coarse * 0.65f + fine * 0.35f - 0.35f) * 2.6f);
					value = Mathf.SmoothStep(0f, 1f, value);
					byte level = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);

					/* GREEN carries a SMOOTH, low-frequency field instead: how clear the water is.
					 * The red mask is deliberately near-binary — foam is bubbles and holes — and
					 * using it for clarity painted the shallows with hard mud-coloured blotches.
					 * Clarity varies over hundreds of metres and has no edges. */
					float smooth = Fbm(u, v, 2, 2f, 776531401);
					byte clarity = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.SmoothStep(0f, 1f, smooth) * 255f), 0, 255);
					pixels[y * Size + x] = new Color32(level, clarity, level, level);
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply();
			Write(texture, FoamPath);
			Object.DestroyImmediate(texture);

			var importer = AssetImporter.GetAtPath(FoamPath) as TextureImporter;
			if (importer != null)
			{
				importer.textureType = TextureImporterType.Default;
				importer.wrapMode = TextureWrapMode.Repeat;
				importer.filterMode = FilterMode.Trilinear;
				importer.sRGBTexture = false;
				importer.mipmapEnabled = true;
				importer.SaveAndReimport();
			}
		}

		private static void Write(Texture2D texture, string path)
		{
			File.WriteAllBytes(path, texture.EncodeToPNG());
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
		}

		// ── Tiling noise ──────────────────────────────────────────────

		/// <summary>Summed octaves, every one of them wrapping at the texture's edge.</summary>
		private static float Fbm(float u, float v, int octaves, float frequency, int seed)
		{
			float total = 0f;
			float amplitude = 1f;
			float sum = 0f;
			int period = Mathf.Max(1, Mathf.RoundToInt(frequency));
			for (int i = 0; i < octaves; i++)
			{
				total += Noise(u, v, period, seed + i * 1013) * amplitude;
				sum += amplitude;
				amplitude *= 0.5f;
				period *= 2;
			}
			return sum > 0f ? total / sum : 0f;
		}

		/// <summary>Value noise on a lattice that wraps at <paramref name="period"/> cells.</summary>
		private static float Noise(float u, float v, int period, int seed)
		{
			float x = u * period;
			float y = v * period;
			int x0 = Mathf.FloorToInt(x);
			int y0 = Mathf.FloorToInt(y);
			float fx = x - x0;
			float fy = y - y0;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);

			float a = Hash(x0, y0, period, seed);
			float b = Hash(x0 + 1, y0, period, seed);
			float c = Hash(x0, y0 + 1, period, seed);
			float d = Hash(x0 + 1, y0 + 1, period, seed);
			return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
		}

		/// <summary>One value per lattice point, taken modulo the period so the field tiles.</summary>
		private static float Hash(int x, int y, int period, int seed)
		{
			x = ((x % period) + period) % period;
			y = ((y % period) + period) % period;
			unchecked
			{
				int h = x * 374761393 + y * 668265263 + seed * 1442695041;
				h = (h ^ (h >> 13)) * 1274126177;
				h ^= h >> 16;
				return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
			}
		}
	}
}
#endif
