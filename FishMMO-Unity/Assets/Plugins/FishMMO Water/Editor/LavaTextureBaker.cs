#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Water.Editor
{
	/// <summary>
	/// Bakes the two tiling textures the lava shader's crust needs: a detail normal map and a mask.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a texture.</b> A lava lake's crust is quenched basaltic glass, and what makes it read as
	/// rock rather than as a flat dark plane is detail at the centimetre scale: pāhoehoe's ropy folds,
	/// burst-bubble vesicles, the crazing of the glassy rind into small facets that each catch the light
	/// their own way. That is dozens of octaves and cellular lookups a texel — far beyond what a pixel
	/// can afford, and exactly what a baked map with mipmaps gives for one tap, filtered by distance.
	/// </para>
	/// <para>
	/// <b>Deterministic, from code, committed.</b> As <see cref="WaterTextureBaker"/>: every octave and
	/// every cellular lattice wraps at the texture's period, so the maps tile, and the same code always
	/// bakes the same pixels. The PNGs are committed so nobody has to bake before the lava draws;
	/// re-bake after changing anything here (FishMMO/Water/Bake Lava Textures). An offline Python port
	/// of this file produced the first committed copies and previews; it agrees to a code value or two.
	/// </para>
	/// <para>
	/// The maps describe <see cref="TileMetres"/> of crust. The height they are built from is in METRES,
	/// so the normal map's slopes are true slopes, and the shader's _CrustTile must match (Assign sets it).
	/// </para>
	/// </remarks>
	public static class LavaTextureBaker
	{
		public const string NormalPath = WaterTextureBaker.Folder + "/LavaCrustNormal.png";
		public const string MaskPath = WaterTextureBaker.Folder + "/LavaCrustMask.png";
		public const string MaterialPath = "Assets/Plugins/FishMMO Water/Materials/Lava.mat";

		/// <summary>Texels across. FishLava.hlsl's FISH_LAVA_DETAIL_TEXELS must match.</summary>
		public const int Size = 512;
		/// <summary>Metres of crust the maps cover: 4.9 mm a texel.</summary>
		public const float TileMetres = 2.5f;

		private const float Texel = TileMetres / Size;

		[MenuItem("FishMMO/Water/Bake Lava Textures")]
		public static void Bake()
		{
			Directory.CreateDirectory(WaterTextureBaker.Folder);
			float slopeVariance = BakeMaps();
			AssetDatabase.Refresh();
			Assign();
			// FishLava.hlsl's FISH_LAVA_DETAIL_VARIANCE is this number; update it if the bake changes.
			Debug.Log($"[Water] Baked {NormalPath} and {MaskPath}. Mean squared slope {slopeVariance:F3} (FISH_LAVA_DETAIL_VARIANCE).");
		}

		/// <summary>Puts the baked maps on the lava material, with the tile they were baked for.</summary>
		public static void Assign()
		{
			var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
			if (material == null)
			{
				return;
			}
			var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
			var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
			if (normal != null)
			{
				material.SetTexture("_CrustNormal", normal);
			}
			if (mask != null)
			{
				material.SetTexture("_CrustMask", mask);
			}
			material.SetFloat("_CrustTile", TileMetres);
			EditorUtility.SetDirty(material);
			AssetDatabase.SaveAssets();
		}

		private static float BakeMaps()
		{
			var height = new float[Size, Size];
			var ropeA = new float[Size, Size];
			var ropeB = new float[Size, Size];
			var crestA = new float[Size, Size];
			var crestB = new float[Size, Size];
			var groove = new float[Size, Size];
			var pitWall = new float[Size, Size];
			const float TwoPi = 2f * Mathf.PI;

			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					// Texel centres; rows bottom first, as Texture2D.SetPixels32 takes them.
					float u = (x + 0.5f) / Size;
					float v = (y + 0.5f) / Size;

					// 1. The skin's slow undulation, a centimetre.
					float h = 0.010f * (Fbm(u, v, 3, 2f, 11) - 0.5f) * 2f;

					/* 2. Ropes: pāhoehoe folds — arcuate crests across the flow, bowed and wandering,
					 * rounded on top with cusped troughs (√ of a raised cosine), in patches, in two
					 * orientations. Integer wave numbers on both axes keep them tiling. */
					float bow = (Fbm(u, v, 2, 2f, 23) - 0.5f) * 0.16f;
					float warp = (Fbm(u, v, 3, 6f, 37) - 0.5f) * 0.05f;
					float patches = Fbm(u, v, 2, 3f, 41);
					float ra = SmoothStep(0.40f, 0.56f, patches);
					float rb = SmoothStep(0.40f, 0.56f, 1f - patches) * 0.8f;
					float ca = Mathf.Sqrt(0.5f + 0.5f * Mathf.Cos(TwoPi * 16f * (v + bow + warp)));
					float cb = Mathf.Sqrt(0.5f + 0.5f * Mathf.Cos(TwoPi * (6f * u + 9f * v + 9f * (bow * 0.7f - warp))));
					h += 0.010f * (ra * ca + rb * cb);
					// Fine skin wrinkles riding the folds.
					float wrinkle = 0.5f + 0.5f * Mathf.Cos(TwoPi * 48f * (v + 1.3f * bow + 2.0f * warp) + 6f * Fbm(u, v, 2, 8f, 53));
					h += 0.0018f * wrinkle * (0.3f + 0.7f * Mathf.Max(ra, rb));

					/* 3. Glassy fracture: the quenched rind crazes into facets a quarter of a metre
					 * across, each tilted its own way (the sparkle of fresh crust), parted by
					 * hairline grooves — in patches, not everywhere. */
					float wu = u + 0.025f * (Fbm(u, v, 2, 5f, 59) - 0.5f);
					float wv = v + 0.025f * (Fbm(u, v, 2, 5f, 63) - 0.5f);
					Cell facet = Cellular(wu, wv, 9, 61);
					float crazed = SmoothStep(0.45f, 0.6f, Fbm(u, v, 2, 4f, 65));
					float tiltA = Hash(facet.X, facet.Y, 9, 67) - 0.5f;
					float tiltB = Hash(facet.X, facet.Y, 9, 73) - 0.5f;
					h += 0.05f * TileMetres * (tiltA * -facet.OffsetX + tiltB * -facet.OffsetY) * 0.5f;
					float g = (1f - SmoothStep(0f, 2.5f * Texel, (facet.F2 - facet.F1) * 0.5f * TileMetres)) * crazed;
					h -= 0.004f * g;

					/* 4. Vesicles: burst bubbles, hemispherical pits — many of a centimetre or so,
					 * a few of several. */
					float pits = 0f;
					float wall = 0f;
					Pits(u, v, 64, 0.28f, 0.14f, 0.34f, 71, ref pits, ref wall);
					Pits(u, v, 14, 0.30f, 0.07f, 0.17f, 79, ref pits, ref wall);
					h -= pits;

					height[y, x] = h;
					ropeA[y, x] = ra;
					ropeB[y, x] = rb;
					crestA[y, x] = ca;
					crestB[y, x] = cb;
					groove[y, x] = g;
					pitWall[y, x] = wall;
				}
			}

			float[,] blurred = WrappedBlur(height, 4);
			var tear = new float[Size, Size];
			var tear2 = new float[Size, Size];
			double tearSum = 0, tear2Sum = 0;
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					float u = (x + 0.5f) / Size;
					float v = (y + 0.5f) / Size;
					// Two independent tear fields: what jags and displaces a plate's border.
					tear[y, x] = Fbm(u + 0.06f * (Fbm(u, v, 2, 3f, 89) - 0.5f), v, 5, 4f, 101);
					tear2[y, x] = Fbm(u, v + 0.06f * (Fbm(u, v, 2, 3f, 103) - 0.5f), 5, 4f, 107);
					tearSum += tear[y, x];
					tear2Sum += tear2[y, x];
				}
			}
			float tearMean = (float)(tearSum / (Size * Size));
			float tear2Mean = (float)(tear2Sum / (Size * Size));

			var normalPixels = new Color32[Size * Size];
			var maskPixels = new Color32[Size * Size];
			double slopeSum = 0;
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					float u = (x + 0.5f) / Size;
					float v = (y + 0.5f) / Size;

					// Slope in metres per metre, from wrapped neighbours so the derivative tiles too.
					float dx = (height[y, (x + 1) % Size] - height[y, (x - 1 + Size) % Size]) / (2f * Texel);
					float dy = (height[(y + 1) % Size, x] - height[(y - 1 + Size) % Size, x]) / (2f * Texel);
					slopeSum += dx * dx + dy * dy;
					float inv = 1f / Mathf.Sqrt(dx * dx + dy * dy + 1f);
					normalPixels[y * Size + x] = new Color32(Byte(-dx * inv * 0.5f + 0.5f), Byte(-dy * inv * 0.5f + 0.5f), Byte(inv * 0.5f + 0.5f), 255);

					// Roughness: glassy stretched crests, ash in the troughs, rough pit walls and grooves.
					float ropes = Mathf.Max(ropeA[y, x], ropeB[y, x]);
					float trough = (1f - Mathf.Max(ropeA[y, x] * crestA[y, x], ropeB[y, x] * crestB[y, x])) * ropes;
					float rough = 0.22f + 0.22f * trough + 0.55f * pitWall[y, x] + 0.20f * groove[y, x] + 0.16f * (Fbm(u, v, 3, 24f, 83) - 0.5f);
					// Cavity: how far a texel lies below its surroundings.
					float cavity = 1f - 70f * Mathf.Max(0f, blurred[y, x] - height[y, x]);
					maskPixels[y * Size + x] = new Color32(
						Byte((tear2[y, x] - tear2Mean) * 2.2f + 0.5f),
						Byte(rough),
						Byte((tear[y, x] - tearMean) * 2.2f + 0.5f),
						Byte(cavity));
				}
			}

			Write(normalPixels, NormalPath);
			Write(maskPixels, MaskPath);
			Import(NormalPath, TextureImporterType.NormalMap);
			Import(MaskPath, TextureImporterType.Default);
			return (float)(slopeSum / (Size * Size));
		}

		private static void Pits(float u, float v, int cells, float chance, float smallest, float largest, int seed, ref float depth, ref float wall)
		{
			Cell cell = Cellular(u, v, cells, seed);
			if (cell.Value >= chance)
			{
				return;
			}
			float radius = (smallest + (largest - smallest) * (cell.Value / chance)) / cells;
			float inside = Mathf.Clamp01(1f - (cell.F1 / radius) * (cell.F1 / radius));
			depth = Mathf.Max(depth, 0.8f * radius * TileMetres * Mathf.Sqrt(inside));
			if (cell.F1 < radius)
			{
				wall = 1f;
			}
		}

		private static void Write(Color32[] pixels, string path)
		{
			var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
			texture.SetPixels32(pixels);
			texture.Apply();
			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
		}

		private static void Import(string path, TextureImporterType type)
		{
			if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
			{
				return;
			}
			importer.textureType = type;
			importer.sRGBTexture = false;
			importer.wrapMode = TextureWrapMode.Repeat;
			importer.filterMode = FilterMode.Trilinear;
			importer.anisoLevel = 8;
			importer.mipmapEnabled = true;
			importer.SaveAndReimport();
		}

		private static byte Byte(float value)
		{
			return (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
		}

		private static float SmoothStep(float a, float b, float x)
		{
			float t = Mathf.Clamp01((x - a) / (b - a));
			return t * t * (3f - 2f * t);
		}

		/// <summary>A box blur with wrapped edges, one axis then the other.</summary>
		private static float[,] WrappedBlur(float[,] source, int radius)
		{
			var across = new float[Size, Size];
			var result = new float[Size, Size];
			float scale = 1f / (2 * radius + 1);
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					float sum = 0f;
					for (int k = -radius; k <= radius; k++)
					{
						sum += source[y, (x + k + Size) % Size];
					}
					across[y, x] = sum * scale;
				}
			}
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					float sum = 0f;
					for (int k = -radius; k <= radius; k++)
					{
						sum += across[(y + k + Size) % Size, x];
					}
					result[y, x] = sum * scale;
				}
			}
			return result;
		}

		// ── Tiling noise ──────────────────────────────────────────────

		private struct Cell
		{
			public float F1;       // distance to the nearest seed, in tile units
			public float F2;       // to the second nearest
			public float Value;    // the nearest seed's own random value
			public float OffsetX;  // from the point to the nearest seed, in tile units
			public float OffsetY;
			public int X;          // the nearest seed's lattice cell (unwrapped; Hash wraps it)
			public int Y;
		}

		/// <summary>A jittered grid of <paramref name="cells"/> a side that wraps at the tile's edge.</summary>
		private static Cell Cellular(float u, float v, int cells, int seed)
		{
			float x = u * cells;
			float y = v * cells;
			int cx = Mathf.FloorToInt(x);
			int cy = Mathf.FloorToInt(y);
			var result = new Cell { F1 = 9f, F2 = 9f };
			for (int j = -1; j <= 1; j++)
			{
				for (int i = -1; i <= 1; i++)
				{
					int gx = cx + i;
					int gy = cy + j;
					float dx = (gx + Hash(gx, gy, cells, seed) - x) / cells;
					float dy = (gy + Hash(gx, gy, cells, seed + 7) - y) / cells;
					float d = Mathf.Sqrt(dx * dx + dy * dy);
					if (d < result.F1)
					{
						result.F2 = result.F1;
						result.F1 = d;
						result.Value = Hash(gx, gy, cells, seed + 13);
						result.OffsetX = dx;
						result.OffsetY = dy;
						result.X = gx;
						result.Y = gy;
					}
					else if (d < result.F2)
					{
						result.F2 = d;
					}
				}
			}
			return result;
		}

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
