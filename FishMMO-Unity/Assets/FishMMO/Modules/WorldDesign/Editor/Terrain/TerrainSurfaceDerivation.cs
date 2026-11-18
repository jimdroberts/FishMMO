#if UNITY_EDITOR
using System;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A normal and a mask for a terrain layer that has only an albedo, worked out from the albedo itself when the arrays
	/// are baked (<see cref="TerrainArrayBaker"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The biome layers built from older art (<see cref="BiomeTerrainLayers"/>: Grass, Woodland, Forest,
	/// Glacier, the sea floor) carry an albedo and nothing else, so they drew perfectly flat beside the generated ground
	/// layers' 10–45° of relief, and they are the commonest ground there is. Baked, so it costs nothing at runtime: the
	/// arrays already hold a normal and a mask slice for every layer.
	/// </para>
	/// <para>
	/// <b>Height from brightness, band-passed.</b> In ground art the dark is the deep (the gaps between stones and
	/// blades, the shade under litter) and the light is the high, at the scale of the detail. The brightness is taken at
	/// two scales of detail and the broad tone removed (a blur over an eighth of the tile), so a darker patch of grass is
	/// not read as a pit. Every filter wraps, because a slice tiles.
	/// </para>
	/// <para>
	/// <b>Calibrated, not guessed.</b> The normal is scaled so its mean tilt is <see cref="TargetTiltDegrees"/>, within
	/// the range the generated layers have, whatever the albedo's contrast. The mask holds the layer's metallic slider
	/// (red), a cavity occlusion from the height (green), the height for height blending (blue) and the same smoothness
	/// the layer drew with before (alpha: the albedo's alpha when it carries smoothness, else the slider), so only the
	/// relief changes.
	/// </para>
	/// </remarks>
	public static class TerrainSurfaceDerivation
	{
		/// <summary>Raised whenever what this derives changes, so every machine rebakes (part of the bake's version).</summary>
		public const int Version = 1;
		/// <summary>The mean tilt of a derived normal, degrees: inside the generated ground layers' 6–45°.</summary>
		public const float TargetTiltDegrees = 18f;
		/// <summary>No pixel tilts more than this, degrees: a speck of white on black is not a spike.</summary>
		public const float MaxTiltDegrees = 60f;
		/// <summary>The mean occlusion of a derived mask: the generated layers' run 0.7–0.96.</summary>
		public const float MeanOcclusion = 0.86f;

		/// <summary>A layer's derived normal (plain RGB, as the baker writes normals) and mask.</summary>
		public struct Result
		{
			public Color32[] Normal;
			public Color32[] Mask;
		}

		/// <summary>
		/// Derives a square, tiling slice's normal and mask from its albedo (<paramref name="size"/>², rows bottom-up).
		/// <paramref name="albedoHasAlpha"/>: the albedo's alpha is smoothness; otherwise <paramref name="smoothness"/> is.
		/// </summary>
		public static Result Derive(Color32[] albedo, int size, float metallic, float smoothness, bool albedoHasAlpha)
		{
			int n = size * size;
			var luminance = new float[n];
			for (int i = 0; i < n; i++)
			{
				Color32 c = albedo[i];
				luminance[i] = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
			}

			// Fine and medium detail over the broad tone: a blur an eighth of the tile wide is the tone.
			float[] fine = Blur(luminance, size, Mathf.Max(1, size / 512));
			float[] medium = Blur(luminance, size, Mathf.Max(2, size / 128));
			float[] broad = Blur(luminance, size, Mathf.Max(4, size / 16));
			var height = new float[n];
			for (int i = 0; i < n; i++)
			{
				height[i] = 0.6f * (fine[i] - broad[i]) + 0.4f * (medium[i] - broad[i]);
			}

			// Slopes in height per pixel, wrapped; then one scale for the whole slice so the mean tilt is the target.
			var dx = new float[n];
			var dy = new float[n];
			double slopeSum = 0.0;
			for (int y = 0; y < size; y++)
			{
				int up = ((y + 1) % size) * size;
				int down = ((y - 1 + size) % size) * size;
				int row = y * size;
				for (int x = 0; x < size; x++)
				{
					int right = (x + 1) % size;
					int left = (x - 1 + size) % size;
					float gx = (height[row + right] - height[row + left]) * 0.5f;
					float gy = (height[up + x] - height[down + x]) * 0.5f;
					dx[row + x] = gx;
					dy[row + x] = gy;
					slopeSum += Math.Sqrt(gx * gx + gy * gy);
				}
			}
			float meanSlope = (float)(slopeSum / n);
			float scale = meanSlope > 1e-6f ? Mathf.Tan(TargetTiltDegrees * Mathf.Deg2Rad) / meanSlope : 0f;
			float maxSlope = Mathf.Tan(MaxTiltDegrees * Mathf.Deg2Rad);

			var normal = new Color32[n];
			for (int i = 0; i < n; i++)
			{
				float sx = -dx[i] * scale;
				float sy = -dy[i] * scale;
				float slope = Mathf.Sqrt(sx * sx + sy * sy);
				if (slope > maxSlope)
				{
					sx *= maxSlope / slope;
					sy *= maxSlope / slope;
				}
				var v = new Vector3(sx, sy, 1f).normalized;
				normal[i] = new Color32(Byte(v.x * 0.5f + 0.5f), Byte(v.y * 0.5f + 0.5f), Byte(v.z * 0.5f + 0.5f), 255);
			}

			// Height for blending: spread to 0..1 about a middle of 0.5 by its own spread.
			double mean = 0.0, squares = 0.0;
			for (int i = 0; i < n; i++)
			{
				mean += height[i];
				squares += (double)height[i] * height[i];
			}
			mean /= n;
			float spread = (float)Math.Sqrt(Math.Max(1e-12, squares / n - mean * mean));

			// Cavity: how far a point sits below the ground round it, as occlusion with the generated layers' mean.
			float[] around = Blur(height, size, Mathf.Max(2, size / 96));
			var cavity = new float[n];
			double cavitySum = 0.0;
			for (int i = 0; i < n; i++)
			{
				cavity[i] = Mathf.Max(0f, around[i] - height[i]) / spread;
				cavitySum += cavity[i];
			}
			float meanCavity = (float)(cavitySum / n);
			float occlusionScale = meanCavity > 1e-6f ? (1f - MeanOcclusion) / meanCavity : 0f;

			var mask = new Color32[n];
			byte metal = Byte(metallic);
			byte flatSmoothness = Byte(smoothness);
			for (int i = 0; i < n; i++)
			{
				float h = Mathf.Clamp01(0.5f + (float)(height[i] - mean) / (4f * spread));
				float occlusion = Mathf.Clamp(1f - cavity[i] * occlusionScale, 0.35f, 1f);
				mask[i] = new Color32(metal, Byte(occlusion), Byte(h), albedoHasAlpha ? albedo[i].a : flatSmoothness);
			}
			return new Result { Normal = normal, Mask = mask };
		}

		/// <summary>The mean tilt of a plain-RGB normal slice, degrees: for the tests and the bake's log.</summary>
		public static float MeanTiltDegrees(Color32[] normal)
		{
			double sum = 0.0;
			for (int i = 0; i < normal.Length; i++)
			{
				float x = normal[i].r / 255f * 2f - 1f;
				float y = normal[i].g / 255f * 2f - 1f;
				sum += Math.Asin(Math.Min(1.0, Math.Sqrt(x * x + y * y)));
			}
			return (float)(sum / Math.Max(1, normal.Length) * Mathf.Rad2Deg);
		}

		/// <summary>Two passes of a wrapped box blur of radius <paramref name="radius"/> each way: near enough a Gaussian.</summary>
		public static float[] Blur(float[] source, int size, int radius)
		{
			float[] a = BoxPass(source, size, radius, true);
			a = BoxPass(a, size, radius, false);
			a = BoxPass(a, size, radius, true);
			return BoxPass(a, size, radius, false);
		}

		private static float[] BoxPass(float[] source, int size, int radius, bool horizontal)
		{
			var result = new float[source.Length];
			float inverse = 1f / (2 * radius + 1);
			for (int line = 0; line < size; line++)
			{
				float sum = 0f;
				for (int k = -radius; k <= radius; k++)
				{
					sum += source[Index(line, Wrap(k, size), size, horizontal)];
				}
				for (int t = 0; t < size; t++)
				{
					result[Index(line, t, size, horizontal)] = sum * inverse;
					sum += source[Index(line, Wrap(t + radius + 1, size), size, horizontal)]
						- source[Index(line, Wrap(t - radius, size), size, horizontal)];
				}
			}
			return result;
		}

		private static int Index(int line, int t, int size, bool horizontal) => horizontal ? line * size + t : t * size + line;

		private static int Wrap(int i, int size) => ((i % size) + size) % size;

		private static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
	}
}
#endif
