using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// The grass's own copy of a terrain array set's layers, for FishGrassBlades.compute's colour read, as uints that
	/// <see cref="GrassTerrainAtlas"/> lays into the grass's one shared buffer: first a header of <see cref="HeaderUints"/> (per layer its tiling and tint, 8 floats as bits), then
	/// per layer a pyramid from <see cref="Width"/>² down to 1 × 1, RGBA8 sRGB bytes per texel (layer after layer; in a
	/// layer, mip after mip; in a mip, rows bottom-up).
	/// </summary>
	/// <remarks>
	/// The compute read the set's Texture2DArray directly at first, and on OpenGL the blades of whole terrains flickered
	/// in unison: the array is the terrain material's (sRGB, compressed, mipmapped, bound and sampled every frame), and a
	/// texel read honours texture-object state (sRGB decode, base level) that drawing the terrain changes. A buffer has no
	/// texture or sampler state. The blades' colour only needs footprints of a quarter metre and up, so 128² per layer
	/// (from the array's own mips, by a blit) is plenty: about 2 MB for 21 layers.
	/// </remarks>
	public sealed class GrassAlbedoTexels
	{
		/// <summary>The largest mip kept, texels per side.</summary>
		public const int MaxWidth = 128;

		/// <summary>
		/// The header: per layer (up to 32) st xyzw and tint xyzw (FishGrassBlades.compute GRASS_ALBEDO_HEADER). The tables were
		/// float4[32] × 2 in the compute's constants, re-uploaded for every terrain's dispatch; they are the set's, written once here.
		/// </summary>
		public const int HeaderUints = FishMMO.Shared.TerrainArrayLayerParams.MaximumLayers * 8;

		/// <summary>The array this was built from (a set that reloads its arrays is rebuilt).</summary>
		public readonly Texture2DArray Source;
		/// <summary>The header and the pyramids, as uploaded (<see cref="GrassTerrainAtlas"/>).</summary>
		public readonly uint[] Data;
		public readonly int Width, Mips, LayerTexels, Layers;

		private GrassAlbedoTexels(Texture2DArray source, uint[] data, int width, int mips, int layerTexels, int layers)
		{
			Source = source;
			Data = data;
			Width = width;
			Mips = mips;
			LayerTexels = layerTexels;
			Layers = layers;
		}

		/// <summary>Texels in a layer's pyramid from <paramref name="width"/> down to 1 (FishGrassBlades.compute's offsets).</summary>
		public static int PyramidTexels(int width, out int mips)
		{
			int n = 0;
			mips = 0;
			for (int side = width; ; side >>= 1)
			{
				int s = Math.Max(1, side);
				n += s * s;
				mips++;
				if (s == 1)
				{
					return n;
				}
			}
		}

		/// <summary>Copies <paramref name="layers"/> slices of <paramref name="albedo"/>; null when there is nothing to copy.</summary>
		public static GrassAlbedoTexels Build(Texture2DArray albedo, int layers, Vector4[] layerST, Vector4[] layerTint)
		{
			layers = Math.Min(layers, albedo != null ? albedo.depth : 0);
			if (layers <= 0)
			{
				return null;
			}
			int width = Mathf.ClosestPowerOfTwo(Mathf.Clamp(Math.Min(albedo.width, albedo.height), 1, MaxWidth));
			int layerTexels = PyramidTexels(width, out int mips);
			var data = new uint[HeaderUints + layers * layerTexels];
			for (int l = 0; l < FishMMO.Shared.TerrainArrayLayerParams.MaximumLayers; l++)
			{
				Vector4 st = layerST != null && l < layerST.Length ? layerST[l] : Vector4.zero;
				Vector4 tint = layerTint != null && l < layerTint.Length ? layerTint[l] : Vector4.zero;
				int h = l * 8;
				data[h] = Bits(st.x); data[h + 1] = Bits(st.y); data[h + 2] = Bits(st.z); data[h + 3] = Bits(st.w);
				data[h + 4] = Bits(tint.x); data[h + 5] = Bits(tint.y); data[h + 6] = Bits(tint.z); data[h + 7] = Bits(tint.w);
			}
			var linear = new Vector3[width * width];

			// The blit samples the array's own mips (sRGB decoded), the sRGB target re-encodes: bytes as the array holds them.
			RenderTexture rt = RenderTexture.GetTemporary(width, width, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
			RenderTexture previous = RenderTexture.active;
			var read = new Texture2D(width, width, TextureFormat.RGBA32, false, false);
			try
			{
				for (int layer = 0; layer < layers; layer++)
				{
					Graphics.Blit(albedo, rt, layer, 0);
					RenderTexture.active = rt;
					read.ReadPixels(new Rect(0, 0, width, width), 0, 0, false);
					read.Apply(false, false);
					Color32[] px = read.GetPixels32();
					int at = HeaderUints + layer * layerTexels;
					for (int i = 0; i < px.Length; i++)
					{
						data[at + i] = Pack(px[i]);
						linear[i] = new Vector3(ToLinear(px[i].r), ToLinear(px[i].g), ToLinear(px[i].b));
					}
					at += width * width;
					// The smaller mips: 2 × 2 box averages in linear light.
					for (int side = width >> 1; side >= 1; side >>= 1)
					{
						int parent = side << 1;
						for (int y = 0; y < side; y++)
						{
							for (int x = 0; x < side; x++)
							{
								Vector3 sum = linear[2 * y * parent + 2 * x] + linear[2 * y * parent + 2 * x + 1]
									+ linear[(2 * y + 1) * parent + 2 * x] + linear[(2 * y + 1) * parent + 2 * x + 1];
								linear[y * side + x] = sum * 0.25f;
							}
						}
						for (int i = 0; i < side * side; i++)
						{
							data[at + i] = Pack(linear[i]);
						}
						at += side * side;
					}
				}
			}
			finally
			{
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(rt);
				Object.Destroy(read);
			}

			return new GrassAlbedoTexels(albedo, data, width, mips, layerTexels, layers);
		}

		private static uint Bits(float f) => BitConverter.ToUInt32(BitConverter.GetBytes(f), 0);

		private static uint Pack(Color32 c) => c.r | (uint)c.g << 8 | (uint)c.b << 16 | 255u << 24;

		private static uint Pack(Vector3 linear) => Pack(new Color32(ToSrgb(linear.x), ToSrgb(linear.y), ToSrgb(linear.z), 255));

		private static float ToLinear(byte b)
		{
			float c = b / 255f;
			return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
		}

		private static byte ToSrgb(float l)
		{
			l = Mathf.Clamp01(l);
			float c = l <= 0.0031308f ? l * 12.92f : 1.055f * Mathf.Pow(l, 1f / 2.4f) - 0.055f;
			return (byte)Mathf.Clamp(Mathf.RoundToInt(c * 255f), 0, 255);
		}
	}
}
