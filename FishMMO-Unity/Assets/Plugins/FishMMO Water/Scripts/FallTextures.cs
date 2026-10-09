using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// A fall's own surface maps, made once in code: streaks drawn out down the water, the lace between them, the packets
	/// of water racing down a rope, and the ripples those raise. The curtain used to wear the river's foam texture
	/// stretched down it, round blobs pulled into smears: the look of a waterfall twenty years ago.
	/// </summary>
	/// <remarks>
	/// <b>Made, not imported.</b> Seamless value noise on integer periods, so every map tiles; seeded, so every client
	/// draws the same water. In code rather than as assets so there is no import setting to get wrong (a normal map
	/// imported as colour, a bare texture meta imported as a cube map).
	/// </remarks>
	public static class FallTextures
	{
		private const int Size = 256;
		private static Texture2D streaks, ripples;

		/// <summary>R streaks (long down the water, fine across it), G lace (the open net between jets), B packets (rounded masses).</summary>
		public static Texture2D Streaks
		{
			get
			{
				if (streaks == null)
				{
					Build();
				}
				return streaks;
			}
		}

		/// <summary>The ripples on the streaks, a tangent-space normal map read by UnpackNormalScale on every platform.</summary>
		public static Texture2D Ripples
		{
			get
			{
				if (ripples == null)
				{
					Build();
				}
				return ripples;
			}
		}

		private static float Hash(int x, int y, int seed)
		{
			unchecked
			{
				uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
				h = (h ^ (h >> 13)) * 1274126177u;
				h ^= h >> 16;
				return (h & 0xFFFFFF) / 16777215f;
			}
		}

		/// <summary>Value noise at (u, v) in 0…1 with <paramref name="px"/> × <paramref name="py"/> cells, wrapping at the edges.</summary>
		private static float Noise(float u, float v, int px, int py, int seed)
		{
			float x = u * px, y = v * py;
			int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
			float tx = x - x0, ty = y - y0;
			tx = tx * tx * (3f - 2f * tx);
			ty = ty * ty * (3f - 2f * ty);
			int xa = ((x0 % px) + px) % px, xb = (xa + 1) % px, ya = ((y0 % py) + py) % py, yb = (ya + 1) % py;
			float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
			return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
		}

		/// <summary>Four octaves of <see cref="Noise"/>, each twice as fine, normalised to 0…1.</summary>
		private static float Fbm(float u, float v, int px, int py, int seed)
		{
			float sum = 0f, amplitude = 0.5f, total = 0f;
			for (int o = 0; o < 4; o++)
			{
				sum += amplitude * Noise(u, v, px << o, py << o, seed + o * 31);
				total += amplitude;
				amplitude *= 0.5f;
			}
			return sum / total;
		}

		private static void Build()
		{
			var height = new float[Size * Size];
			var colours = new Color32[Size * Size];
			for (int y = 0; y < Size; y++)
			{
				float v = (y + 0.5f) / Size;
				for (int x = 0; x < Size; x++)
				{
					float u = (x + 0.5f) / Size;
					/* Streaks: eight times finer across than down, warped a little so no line runs dead straight, and soft:
					 * a hard-edged streak map read as a white texture pasted on the water. */
					float warp = Fbm(u, v, 3, 2, 11) - 0.5f;
					float streak = Fbm(u + 0.06f * warp, v, 16, 2, 3);
					streak = Mathf.SmoothStep(0.12f, 0.92f, streak);
					// Lace: the net of thin water between the jets, the edges of noise cells.
					float lace = 1f - Mathf.Abs(2f * Fbm(u, v + 0.1f * warp, 6, 4, 23) - 1f);
					lace = Mathf.SmoothStep(0.45f, 0.98f, lace);
					// Packets: rounded masses, a little longer than wide.
					float packet = Mathf.SmoothStep(0.3f, 0.9f, Fbm(u, v, 5, 3, 41));
					height[y * Size + x] = 0.65f * streak + 0.35f * packet;
					colours[y * Size + x] = new Color32((byte)(255f * streak), (byte)(255f * lace), (byte)(255f * packet), 255);
				}
			}
			streaks = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
			{
				name = "Fall streaks",
				hideFlags = HideFlags.HideAndDontSave,
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Trilinear,
				anisoLevel = 4,
			};
			streaks.SetPixels32(colours);
			streaks.Apply(true, true);

			/* The ripples: the slope of the streak height, wrapping. R, G and B carry the normal's x, y and z and A is 1, so
			 * the one map reads right whether the platform unpacks normals as RGB or as the A×R, G pair. */
			var normals = new Color32[Size * Size];
			const float strength = 2.5f;
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					float dx = height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size];
					float dy = height[((y + 1) % Size) * Size + x] - height[((y + Size - 1) % Size) * Size + x];
					var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
					normals[y * Size + x] = new Color32((byte)(127.5f + 127.5f * n.x), (byte)(127.5f + 127.5f * n.y), (byte)(127.5f + 127.5f * n.z), 255);
				}
			}
			ripples = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
			{
				name = "Fall ripples",
				hideFlags = HideFlags.HideAndDontSave,
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Trilinear,
				anisoLevel = 4,
			};
			ripples.SetPixels32(normals);
			ripples.Apply(true, true);
		}
	}
}
