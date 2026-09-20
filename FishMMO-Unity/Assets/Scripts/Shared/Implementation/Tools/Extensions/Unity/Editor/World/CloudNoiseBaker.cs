#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Bakes the 3D noise the volumetric clouds are carved from: a shape volume and a finer detail
	/// volume, both tiling.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The shape volume is the classic cloud recipe: red is Perlin noise pulled toward Worley
	/// ("billows"), and green, blue and alpha are Worley alone at rising frequencies, so the
	/// raymarcher can round a cloud off or eat into it by choosing a channel. The detail volume is
	/// three Worley frequencies used to erode the edges into wisps.
	/// </para>
	/// <para>
	/// Both tile in all three axes: every lattice wraps, so a cloud drifting across the sky never
	/// meets a seam. Baked once from a seed and kept; the textures are build content, so they live
	/// under the client's weather folder and are registered like the other render assets.
	/// </para>
	/// </remarks>
	public static class CloudNoiseBaker
	{
		public const string Folder = "Assets/Prefabs/Client/Weather/Textures";
		public const string ShapePath = Folder + "/Cloud Shape.asset";
		public const string DetailPath = Folder + "/Cloud Detail.asset";
		public const int ShapeSize = 128;
		public const int DetailSize = 32;
		public const int DefaultSeed = 238;

		[DashboardTool(DashboardToolAttribute.Weather, "Bake cloud noise", Section = "Content", Order = 3,
			Tooltip = "Bakes the tiling 3D shape and detail volumes the volumetric clouds are carved from, into " + Folder + ". Existing textures are kept; delete them to re-bake.")]
		public static void BakeFromDashboard()
		{
			Texture3D shape = Ensure(ShapePath, ShapeSize, DefaultSeed, true);
			Texture3D detail = Ensure(DetailPath, DetailSize, DefaultSeed + 17, false);
			Debug.Log($"[Cloud noise] {AssetDatabase.GetAssetPath(shape)} ({ShapeSize}³) and {AssetDatabase.GetAssetPath(detail)} ({DetailSize}³) are ready.");
		}

		/// <summary>The baked volume at a path, baking it first when it is missing.</summary>
		public static Texture3D Ensure(string path, int size, int seed, bool shape)
		{
			Texture3D existing = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
			if (existing != null)
			{
				// The march picks its own mip from how much world a sample stands for, and asks for a
				// fractional one. Bilinear snaps that to the nearest level, so the sky grows visible
				// rings where the chosen mip steps over — a volume baked before this was needed keeps
				// the old setting, so fix it in place rather than making anyone re-bake nineteen
				// megabytes of noise.
				if (existing.filterMode != FilterMode.Trilinear)
				{
					existing.filterMode = FilterMode.Trilinear;
					EditorUtility.SetDirty(existing);
					AssetDatabase.SaveAssetIfDirty(existing);
				}
				return existing;
			}
			WorldEditorAssets.EnsureFolder(Folder);
			Texture3D texture = shape ? BuildShape(size, seed) : BuildDetail(size, seed);
			texture.name = System.IO.Path.GetFileNameWithoutExtension(path);
			AssetDatabase.CreateAsset(texture, path);
			AssetDatabase.SaveAssets();
			WorldEditorAssets.RegisterAddressable(texture, WorldEditorAssets.ClientStaticGroup);
			return AssetDatabase.LoadAssetAtPath<Texture3D>(path);
		}

		/// <summary>Red: Perlin pulled toward Worley. Green/blue/alpha: Worley at 1×, 2× and 4×.</summary>
		private static Texture3D BuildShape(int size, int seed)
		{
			var texture = new Texture3D(size, size, size, TextureFormat.RGBA32, true)
			{
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Trilinear,
			};
			var pixels = new Color32[size * size * size];
			for (int z = 0; z < size; z++)
			{
				float w = z / (float)size;
				for (int y = 0; y < size; y++)
				{
					float v = y / (float)size;
					for (int x = 0; x < size; x++)
					{
						float u = x / (float)size;
						float perlin = FractalPerlin(u, v, w, 4, 7, seed);
						float worley = 1f - FractalWorley(u, v, w, 6, seed + 1);
						// Perlin remapped by the inverse Worley: solid cores, billowed edges.
						float shape = Mathf.Clamp01(Remap(perlin, worley - 1f, 1f, 0f, 1f));
						byte r = (byte)(Mathf.Clamp01(shape) * 255f);
						byte g = (byte)((1f - FractalWorley(u, v, w, 8, seed + 2)) * 255f);
						byte b = (byte)((1f - FractalWorley(u, v, w, 16, seed + 3)) * 255f);
						byte a = (byte)((1f - FractalWorley(u, v, w, 32, seed + 4)) * 255f);
						pixels[x + y * size + z * size * size] = new Color32(r, g, b, a);
					}
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(true);
			return texture;
		}

		/// <summary>Three Worley frequencies, for eroding a cloud's edges into wisps.</summary>
		private static Texture3D BuildDetail(int size, int seed)
		{
			var texture = new Texture3D(size, size, size, TextureFormat.RGBA32, true)
			{
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Trilinear,
			};
			var pixels = new Color32[size * size * size];
			for (int z = 0; z < size; z++)
			{
				float w = z / (float)size;
				for (int y = 0; y < size; y++)
				{
					float v = y / (float)size;
					for (int x = 0; x < size; x++)
					{
						float u = x / (float)size;
						byte r = (byte)((1f - FractalWorley(u, v, w, 4, seed)) * 255f);
						byte g = (byte)((1f - FractalWorley(u, v, w, 8, seed + 1)) * 255f);
						byte b = (byte)((1f - FractalWorley(u, v, w, 16, seed + 2)) * 255f);
						pixels[x + y * size + z * size * size] = new Color32(r, g, b, 255);
					}
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(true);
			return texture;
		}

		private static float Remap(float value, float low, float high, float newLow, float newHigh)
		{
			return newLow + (value - low) / Mathf.Max(1e-5f, high - low) * (newHigh - newLow);
		}

		// ── Noise ──────────────────────────────────────────────────────

		/// <summary>Worley (cellular) noise over a wrapping lattice: 0 at a cell point, 1 far from one.</summary>
		private static float Worley(float x, float y, float z, int cells, int seed)
		{
			float fx = x * cells, fy = y * cells, fz = z * cells;
			int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy), iz = Mathf.FloorToInt(fz);
			float best = float.MaxValue;
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						int cx = Wrap(ix + dx, cells), cy = Wrap(iy + dy, cells), cz = Wrap(iz + dz, cells);
						Vector3 point = CellPoint(cx, cy, cz, seed) + new Vector3(ix + dx, iy + dy, iz + dz);
						float distance = (point - new Vector3(fx, fy, fz)).sqrMagnitude;
						best = Mathf.Min(best, distance);
					}
				}
			}
			return Mathf.Clamp01(Mathf.Sqrt(best));
		}

		/// <summary>Worley over three octaves, the finer ones at a quarter weight each.</summary>
		private static float FractalWorley(float x, float y, float z, int cells, int seed)
		{
			float a = Worley(x, y, z, cells, seed);
			float b = Worley(x, y, z, cells * 2, seed + 31);
			float c = Worley(x, y, z, cells * 4, seed + 67);
			return Mathf.Clamp01(a * 0.625f + b * 0.25f + c * 0.125f);
		}

		/// <summary>Gradient noise over a wrapping lattice, several octaves, in 0..1.</summary>
		private static float FractalPerlin(float x, float y, float z, int period, int octaves, int seed)
		{
			float sum = 0f, weight = 0f, amplitude = 1f;
			int frequency = period;
			for (int i = 0; i < octaves; i++)
			{
				sum += amplitude * Perlin(x, y, z, frequency, seed + i * 13);
				weight += amplitude;
				amplitude *= 0.5f;
				frequency *= 2;
			}
			return Mathf.Clamp01(sum / Mathf.Max(1e-5f, weight) * 0.5f + 0.5f);
		}

		private static float Perlin(float x, float y, float z, int period, int seed)
		{
			float fx = x * period, fy = y * period, fz = z * period;
			int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy), iz = Mathf.FloorToInt(fz);
			float tx = fx - ix, ty = fy - iy, tz = fz - iz;
			float sx = Fade(tx), sy = Fade(ty), sz = Fade(tz);
			float n000 = Dot(ix, iy, iz, tx, ty, tz, period, seed);
			float n100 = Dot(ix + 1, iy, iz, tx - 1f, ty, tz, period, seed);
			float n010 = Dot(ix, iy + 1, iz, tx, ty - 1f, tz, period, seed);
			float n110 = Dot(ix + 1, iy + 1, iz, tx - 1f, ty - 1f, tz, period, seed);
			float n001 = Dot(ix, iy, iz + 1, tx, ty, tz - 1f, period, seed);
			float n101 = Dot(ix + 1, iy, iz + 1, tx - 1f, ty, tz - 1f, period, seed);
			float n011 = Dot(ix, iy + 1, iz + 1, tx, ty - 1f, tz - 1f, period, seed);
			float n111 = Dot(ix + 1, iy + 1, iz + 1, tx - 1f, ty - 1f, tz - 1f, period, seed);
			float x00 = Mathf.Lerp(n000, n100, sx), x10 = Mathf.Lerp(n010, n110, sx);
			float x01 = Mathf.Lerp(n001, n101, sx), x11 = Mathf.Lerp(n011, n111, sx);
			return Mathf.Lerp(Mathf.Lerp(x00, x10, sy), Mathf.Lerp(x01, x11, sy), sz);
		}

		private static float Dot(int ix, int iy, int iz, float dx, float dy, float dz, int period, int seed)
		{
			Vector3 gradient = Gradient(Wrap(ix, period), Wrap(iy, period), Wrap(iz, period), seed);
			return gradient.x * dx + gradient.y * dy + gradient.z * dz;
		}

		private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

		private static int Wrap(int value, int period) => ((value % period) + period) % period;

		private static Vector3 Gradient(int x, int y, int z, int seed)
		{
			uint h = Hash(x, y, z, seed);
			// A point on the sphere from two hashed angles: no axis bias.
			float theta = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
			float cosPhi = ((h >> 16) & 0xFFFF) / 65535f * 2f - 1f;
			float sinPhi = Mathf.Sqrt(Mathf.Max(0f, 1f - cosPhi * cosPhi));
			return new Vector3(Mathf.Cos(theta) * sinPhi, Mathf.Sin(theta) * sinPhi, cosPhi);
		}

		private static Vector3 CellPoint(int x, int y, int z, int seed)
		{
			uint h = Hash(x, y, z, seed);
			return new Vector3((h & 0x3FF) / 1023f, ((h >> 10) & 0x3FF) / 1023f, ((h >> 20) & 0x3FF) / 1023f);
		}

		private static uint Hash(int x, int y, int z, int seed)
		{
			unchecked
			{
				uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(seed * 2654435761);
				h ^= h >> 13;
				h *= 2246822519u;
				h ^= h >> 15;
				h *= 3266489917u;
				h ^= h >> 16;
				return h;
			}
		}
	}
}
#endif
