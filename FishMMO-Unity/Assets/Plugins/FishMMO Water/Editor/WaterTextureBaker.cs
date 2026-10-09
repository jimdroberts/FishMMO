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

		/// <summary>The foam map's size: the lace's threads are a few texels wide at this size, and vanish at 512.</summary>
		private const int FoamSize = 1024;

		/// <summary>
		/// The foam map. R (and B) the old blotch mask and G the smooth clarity field, as they always were (the
		/// waterfall and the clarity still read them); A the foam's LACE (<see cref="FoamLace"/>), which the foam
		/// itself is now cut from (FishWaterFoam.hlsl).
		/// </summary>
		private static void BakeFoam()
		{
			var texture = new Texture2D(FoamSize, FoamSize, TextureFormat.RGBA32, true, true);
			var pixels = new Color32[FoamSize * FoamSize];
			float[] lace = FoamLace(FoamSize);
			for (int y = 0; y < FoamSize; y++)
			{
				for (int x = 0; x < FoamSize; x++)
				{
					float u = x / (float)FoamSize;
					float v = y / (float)FoamSize;
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
					byte cut = (byte)Mathf.Clamp(Mathf.RoundToInt(lace[y * FoamSize + x] * 255f), 0, 255);
					pixels[y * FoamSize + x] = new Color32(level, clarity, level, cut);
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
				importer.alphaSource = TextureImporterAlphaSource.FromInput;
				importer.alphaIsTransparency = false;
				// The lace's threads are a texel or two wide: the default compression smeared them into blocks.
				importer.textureCompression = TextureImporterCompression.CompressedHQ;
				importer.maxTextureSize = FoamSize;
				importer.SaveAndReimport();
			}
		}

		// ── The foam's lace ───────────────────────────────────────────

		/// <summary>
		/// The foam's lace, 0 … 1 per texel, EQUALISED: cut at 1 − c it covers exactly c of the area, so a foam's
		/// coverage is what the shader passes and the pattern says what that much foam looks like.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Foam decays by its bubbles bursting.</b> Holes open, grow and merge; what is left between them is a
		/// web of thin walls (Plateau borders) that outlasts the pockets round it, then tears into strands with
		/// clumps where the walls meet. So the lace is a field of packed bubbles of random size, the least distance
		/// over radius to any of them (continuous: the nearest bubble's own radius jumped at every cell border and
		/// drew straight seams), raised along the walls between two holes and broken here and there, with fine
		/// bubbles punched into the walls and a few large holes torn through. A smooth field grows the holes where
		/// the foam is to part into rafts. Cut low it is dense froth with round holes; cut high, a web; higher,
		/// strands.
		/// </para>
		/// <para>
		/// The blotch mask it replaces was a contrasted noise: any cut through a smooth field is a soft blob, so
		/// every foam drawn from it, whatever its coverage, read as cotton wool pasted on the water.
		/// </para>
		/// <para>Prototyped offline (numpy) and ported exactly: the same hash, sites, radii and order of terms.</para>
		/// </remarks>
		private static float[] FoamLace(int size)
		{
			var u = new float[size];
			for (int i = 0; i < size; i++)
			{
				u[i] = (i + 0.5f) / size;
			}
			var web = new float[size * size];
			var gap = new float[size * size];
			Holes(size, u, 20, 23, 0.55f, 1.45f, (a, b) => 0.55f + 1.3f * Mathf.Pow(Smooth(a, b, 3, 53), 1.5f), web, gap);
			var fine = new float[size * size];
			var torn = new float[size * size];
			Holes(size, u, 72, 37, 0.4f, 1.3f, null, fine, null);
			Holes(size, u, 5, 11, 0.3f, 1.1f, null, torn, null);

			var value = new double[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					int i = y * size + x;
					float width = 0.06f + 0.12f * Smooth(u[x], u[y], 6, 83);
					float thread = Mathf.Pow(Mathf.Clamp01(1f - gap[i] / width), 1.5f);
					thread *= Mathf.Clamp01((Smooth(u[x], u[y], 9, 71) - 0.4f) / 0.3f);
					float s = (float)System.Math.Tanh(1.3 * web[i])
						+ 0.06f * Smooth(u[x], u[y], 14, 89)
						+ 0.55f * thread
						- 0.22f * Mathf.Clamp01(1f - Mathf.Min(fine[i], 1f))
						- 0.35f * Mathf.Clamp01(1f - Mathf.Min(torn[i], 1f));
					// Ties would rank in row order and draw horizontal dashes: dithered below a rank.
					value[i] = s + LaceHash(x, y, size, 97) * 1e-6;
				}
			}
			// Equalised: rank / count.
			var order = new int[value.Length];
			for (int i = 0; i < order.Length; i++)
			{
				order[i] = i;
			}
			System.Array.Sort((double[])value.Clone(), order);
			var lace = new float[value.Length];
			for (int r = 0; r < order.Length; r++)
			{
				lace[order[r]] = (r + 0.5f) / order.Length;
			}
			return lace;
		}

		/// <summary>
		/// For every texel, the least distance-over-radius to any bubble of a jittered grid of <paramref name="cells"/>
		/// a side (each its own radius, scaled by <paramref name="grow"/> at the bubble), into <paramref name="best"/>;
		/// and the margin to the second least (0 on the wall between two holes) into <paramref name="gap"/>.
		/// </summary>
		private static void Holes(int size, float[] u, int cells, uint seed, float low, float high,
			System.Func<float, float, float> grow, float[] best, float[] gap)
		{
			const float Jitter = 0.95f;
			// Every site once (its position and radius), then each texel reads the 5×5 round its own cell.
			var px = new float[cells * cells];
			var py = new float[cells * cells];
			var pr = new float[cells * cells];
			for (int gy = 0; gy < cells; gy++)
			{
				for (int gx = 0; gx < cells; gx++)
				{
					int k = gy * cells + gx;
					px[k] = gx + 0.5f + (LaceHash(gx, gy, cells, seed) - 0.5f) * Jitter;
					py[k] = gy + 0.5f + (LaceHash(gx, gy, cells, seed + 101) - 0.5f) * Jitter;
					float h = LaceHash(gx, gy, cells, seed + 202);
					pr[k] = 0.5f * (low + (high - low) * h * h);
					if (grow != null)
					{
						pr[k] *= grow(Wrap01(px[k] / cells), Wrap01(py[k] / cells));
					}
				}
			}
			for (int y = 0; y < size; y++)
			{
				float Y = u[y] * cells;
				int cy = Mathf.FloorToInt(Y);
				for (int x = 0; x < size; x++)
				{
					float X = u[x] * cells;
					int cx = Mathf.FloorToInt(X);
					float first = 9f, second = 9f;
					for (int dy = -2; dy <= 2; dy++)
					{
						int gy = cy + dy;
						int wy = ((gy % cells) + cells) % cells;
						for (int dx = -2; dx <= 2; dx++)
						{
							int gx = cx + dx;
							int wx = ((gx % cells) + cells) % cells;
							int k = wy * cells + wx;
							// The site in this copy of the tile.
							float sx = px[k] + (gx - wx), sy = py[k] + (gy - wy);
							float d = Mathf.Sqrt((X - sx) * (X - sx) + (Y - sy) * (Y - sy)) / pr[k];
							if (d < first)
							{
								second = first;
								first = d;
							}
							else if (d < second)
							{
								second = d;
							}
						}
					}
					best[y * size + x] = first;
					if (gap != null)
					{
						gap[y * size + x] = second - first;
					}
				}
			}
		}

		private static float Wrap01(float v) => v - Mathf.Floor(v);

		/// <summary>Two octaves of value noise wrapping at <paramref name="cells"/> and twice that, 0 … 1.</summary>
		private static float Smooth(float u, float v, int cells, uint seed)
		{
			return (LaceNoise(u, v, cells, seed) + 0.5f * LaceNoise(u, v, cells * 2, seed)) / 1.5f;
		}

		private static float LaceNoise(float u, float v, int period, uint seed)
		{
			float x = u * period, y = v * period;
			int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
			float fx = x - x0, fy = y - y0;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);
			float a = LaceHash(x0, y0, period, seed), b = LaceHash(x0 + 1, y0, period, seed);
			float c = LaceHash(x0, y0 + 1, period, seed), d = LaceHash(x0 + 1, y0 + 1, period, seed);
			return (a * (1f - fx) + b * fx) * (1f - fy) + (c * (1f - fx) + d * fx) * fy;
		}

		/// <summary>The lace's hash (32-bit unsigned, as the prototype's): one value per lattice point, modulo the period.</summary>
		private static float LaceHash(int x, int y, int period, uint seed)
		{
			uint ux = (uint)(((x % period) + period) % period), uy = (uint)(((y % period) + period) % period);
			unchecked
			{
				uint h = ux * 374761393u + uy * 668265263u + seed * 2246822519u;
				h = (h ^ (h >> 13)) * 1274126177u;
				h ^= h >> 16;
				return (h & 0xFFFFFFu) / (float)0xFFFFFF;
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
