using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The terrain's index map: for every texel of a tile's alphamaps, its four strongest layers and their weights, packed
	/// into eight bytes. What the array terrain shader reads in place of the alphamaps themselves, so a scene may carry up
	/// to <see cref="MaximumLayers"/> layers at the cost of four texel reads a pixel, however many it has.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The shader used to read every control map of a tile (four layers each) per pixel and keep the four
	/// strongest: 32 layers were eight maps and eight samples a pixel, and every layer more another map (2026-10-16:
	/// "every biome has unique textures for everything" needs far more than 32 layers a scene). The four strongest are
	/// found once per texel instead, here, and the pixel blends the four texels around it.
	/// </para>
	/// <para>
	/// <b>A texel</b> is two 32-bit words (R32G32_UInt): <c>x</c> the four layer ids, a byte each, strongest first;
	/// <c>y</c> their weights in bytes, each LESS the texel's fifth-strongest weight. That is the shader's seam-free rule
	/// (FishTopLayers) applied at the texel: a layer that drops out of the four does so at zero, so neither the bake nor
	/// the blend jumps where a fifth layer overtakes the fourth. Ground with four layers or fewer at a texel is stored
	/// exactly as painted.
	/// </para>
	/// <para>
	/// <b>Ties</b> go to the lower layer index, as the old per-pixel loop met them (it took the layers in index order and
	/// only a strictly greater weight moved one). Slots a texel has no layer for hold zero-weight layers, the lowest
	/// indices first, so the four ids of a texel are always distinct — the filter merges the four texels' entries by id.
	/// </para>
	/// <para>
	/// <b>The filter</b> (<see cref="Filter"/>, FishTerrainIndexMap.hlsl): ids cannot be filtered by the hardware, so the
	/// four texels a bilinear sample would read are loaded, each texel's weights scaled by its bilinear share, entries of
	/// the same layer summed, and the four strongest kept less the fifth, normalised — the old rule on the merged
	/// weights. Where every texel of the four has four layers or fewer, the merged weights ARE the bilinear weights of
	/// every layer, so the result is the old shader's exactly. Where a texel had a fifth layer, the blend there is the
	/// old one at that texel's centre and stays continuous between centres; it differs from the old per-pixel choice by
	/// at most the texels' own fifth weights.
	/// </para>
	/// <para>
	/// Baked on the GPU from the live alphamaps (TerrainIndexMapBaker, FishTerrainIndexBake.shader), so a brush stroke
	/// is on screen the same frame. This class is the same bake on the CPU — the reference the GPU bake is checked
	/// against and the tests run — and the shader's filter, line for line.
	/// </para>
	/// </remarks>
	public static class TerrainIndexMap
	{
		/// <summary>The most layers an index map can name: a byte per id.</summary>
		public const int MaximumLayers = 256;

		/// <summary>Layers kept per texel.</summary>
		public const int Slots = 4;

		/// <summary>Bytes per texel (two 32-bit words).</summary>
		public const int TexelBytes = 8;

		/// <summary>
		/// One texel: the four strongest of <paramref name="layers"/> weights (bytes, layer l at
		/// <paramref name="weights"/>[<paramref name="start"/> + l]) and their weights less the fifth.
		/// </summary>
		public static void BakeTexel(byte[] weights, int start, int layers, out uint ids, out uint packedWeights)
		{
			// Empty slots start below every weight, so zero-weight layers fill them in index order and never displace one.
			int t0 = -1, t1 = -1, t2 = -1, t3 = -1, fifth = -1;
			int i0 = 0, i1 = 0, i2 = 0, i3 = 0;
			int count = Mathf.Min(layers, MaximumLayers);
			for (int l = 0; l < count; l++)
			{
				Consider(weights[start + l], l, ref t0, ref t1, ref t2, ref t3, ref i0, ref i1, ref i2, ref i3, ref fifth);
			}
			// Fewer than four layers at all: the rest are layers past the palette, at zero, so the ids stay distinct.
			for (int l = count; l < Slots; l++)
			{
				Consider(0, l, ref t0, ref t1, ref t2, ref t3, ref i0, ref i1, ref i2, ref i3, ref fifth);
			}
			int f = Mathf.Max(fifth, 0);
			ids = (uint)i0 | (uint)i1 << 8 | (uint)i2 << 16 | (uint)i3 << 24;
			packedWeights = (uint)(t0 - f) | (uint)(t1 - f) << 8 | (uint)(t2 - f) << 16 | (uint)(t3 - f) << 24;
		}

		/// <summary>
		/// The four strongest kept sorted, and the fifth (FishWeatherTerrainArrayInput.hlsl FishConsiderLayer, and the bake
		/// shader's own copy): a strictly greater weight moves a layer up, so between equals the one met first stays ahead.
		/// </summary>
		private static void Consider(int weight, int index, ref int t0, ref int t1, ref int t2, ref int t3,
			ref int i0, ref int i1, ref int i2, ref int i3, ref int fifth)
		{
			bool b0 = weight > t0, b1 = weight > t1, b2 = weight > t2, b3 = weight > t3;
			fifth = b3 ? t3 : Mathf.Max(fifth, weight);
			int o0 = t0, o1 = t1, o2 = t2, o3 = t3;
			int j0 = i0, j1 = i1, j2 = i2, j3 = i3;
			t3 = b2 ? o2 : (b3 ? weight : o3); i3 = b2 ? j2 : (b3 ? index : j3);
			t2 = b1 ? o1 : (b2 ? weight : o2); i2 = b1 ? j1 : (b2 ? index : j2);
			t1 = b0 ? o0 : (b1 ? weight : o1); i1 = b0 ? j0 : (b1 ? index : j1);
			t0 = b0 ? weight : o0; i0 = b0 ? index : j0;
		}

		/// <summary>
		/// A whole tile from its control maps as <see cref="Texture2D.GetPixels32()"/> returns them (map k holds layers
		/// 4k..4k+3 in r, g, b, a): two words a texel, texel (x, y) at 2 (y × width + x).
		/// </summary>
		public static uint[] Bake(Color32[][] controls, int texels, int layers)
		{
			var map = new uint[texels * 2];
			var weights = new byte[Mathf.Max(layers, 1)];
			for (int i = 0; i < texels; i++)
			{
				for (int l = 0; l < layers; l++)
				{
					Color32 c = controls[l >> 2][i];
					weights[l] = (l & 3) == 0 ? c.r : (l & 3) == 1 ? c.g : (l & 3) == 2 ? c.b : c.a;
				}
				BakeTexel(weights, 0, layers, out map[2 * i], out map[2 * i + 1]);
			}
			return map;
		}

		/// <summary>Layer id in slot <paramref name="slot"/> of a texel's id word.</summary>
		public static int IdOf(uint ids, int slot) => (int)(ids >> (8 * slot) & 255u);

		/// <summary>Weight byte in slot <paramref name="slot"/> of a texel's weight word.</summary>
		public static int WeightOf(uint weights, int slot) => (int)(weights >> (8 * slot) & 255u);

		/// <summary>True when (<paramref name="w"/>, <paramref name="id"/>) ranks above (<paramref name="otherW"/>, <paramref name="otherId"/>): heavier first, the lower id between equals.</summary>
		private static bool Beats(float w, int id, float otherW, int otherId) => w > otherW || (w == otherW && id < otherId);

		/// <summary>
		/// The blend at a point of a tile: FishTerrainIndexMap.hlsl FishIndexFilter, line for line in float. Writes the four
		/// layers (strongest first) and their normalised weights; an unpainted point is layer 0 alone, as it always was.
		/// </summary>
		/// <param name="u">The tile's 0..1 coordinate across (x).</param>
		/// <param name="v">The tile's 0..1 coordinate along (y).</param>
		public static void Filter(uint[] map, int width, int height, float u, float v, int[] ids, float[] weights)
		{
			// Texel centres as the terrain addresses its control maps: position uv × (size − 1) between them.
			float px = Mathf.Clamp01(u) * (width - 1), py = Mathf.Clamp01(v) * (height - 1);
			int x0 = Mathf.Min((int)Mathf.Floor(px), Mathf.Max(width - 2, 0));
			int y0 = Mathf.Min((int)Mathf.Floor(py), Mathf.Max(height - 2, 0));
			float fx = Mathf.Clamp01(px - x0), fy = Mathf.Clamp01(py - y0);
			int x1 = Mathf.Min(x0 + 1, width - 1), y1 = Mathf.Min(y0 + 1, height - 1);
			Merge(
				map[2 * (y0 * width + x0)], map[2 * (y0 * width + x0) + 1],
				map[2 * (y0 * width + x1)], map[2 * (y0 * width + x1) + 1],
				map[2 * (y1 * width + x0)], map[2 * (y1 * width + x0) + 1],
				map[2 * (y1 * width + x1)], map[2 * (y1 * width + x1) + 1],
				fx, fy, ids, weights);
		}

		/// <summary>
		/// The merge of four texels — (x0, y0), (x1, y0), (x0, y1), (x1, y1) — at the bilinear position (fx, fy) between
		/// them: FishIndexFilter's body.
		/// </summary>
		public static void Merge(uint ids00, uint w00, uint ids10, uint w10, uint ids01, uint w01, uint ids11, uint w11,
			float fx, float fy, int[] ids, float[] weights)
		{
			uint[] texelIds = { ids00, ids10, ids01, ids11 };
			uint[] texelWeights = { w00, w10, w01, w11 };
			float[] share = { (1f - fx) * (1f - fy), fx * (1f - fy), (1f - fx) * fy, fx * fy };
			var id = new int[16];
			var w = new float[16];
			for (int t = 0; t < 4; t++)
			{
				for (int s = 0; s < Slots; s++)
				{
					id[t * 4 + s] = IdOf(texelIds[t], s);
					w[t * 4 + s] = WeightOf(texelWeights[t], s) * (share[t] / 255f);
				}
			}

			float t0 = 0f, t1 = 0f, t2 = 0f, t3 = 0f, fifth = 0f;
			int i0 = 0, i1 = 0, i2 = 0, i3 = 0;
			for (int t = 0; t < 4; t++)
			{
				for (int s = 0; s < Slots; s++)
				{
					int layer = id[t * 4 + s];
					// A layer is weighed once, at the first texel naming it, with every later texel's share of it added.
					bool seen = false;
					for (int e = 0; e < t; e++)
					{
						for (int k = 0; k < Slots; k++)
						{
							seen |= id[e * 4 + k] == layer;
						}
					}
					float total = w[t * 4 + s];
					for (int g = t + 1; g < 4; g++)
					{
						for (int k = 0; k < Slots; k++)
						{
							total += id[g * 4 + k] == layer ? w[g * 4 + k] : 0f;
						}
					}
					float weight = seen ? 0f : total;
					bool b0 = Beats(weight, layer, t0, i0), b1 = Beats(weight, layer, t1, i1);
					bool b2 = Beats(weight, layer, t2, i2), b3 = Beats(weight, layer, t3, i3);
					fifth = b3 ? t3 : Mathf.Max(fifth, weight);
					float o0 = t0, o1 = t1, o2 = t2, o3 = t3;
					int j0 = i0, j1 = i1, j2 = i2, j3 = i3;
					t3 = b2 ? o2 : (b3 ? weight : o3); i3 = b2 ? j2 : (b3 ? layer : j3);
					t2 = b1 ? o1 : (b2 ? weight : o2); i2 = b1 ? j1 : (b2 ? layer : j2);
					t1 = b0 ? o0 : (b1 ? weight : o1); i1 = b0 ? j0 : (b1 ? layer : j1);
					t0 = b0 ? weight : o0; i0 = b0 ? layer : j0;
				}
			}
			Normalise(t0, t1, t2, t3, fifth, i0, i1, i2, i3, ids, weights);
		}

		/// <summary>
		/// The old shader's last step, shared by both: the fifth subtracted from the four, normalised; nothing left (an
		/// unpainted texel, or five layers tied) draws layer 0.
		/// </summary>
		public static void Normalise(float t0, float t1, float t2, float t3, float fifth, int i0, int i1, int i2, int i3, int[] ids, float[] weights)
		{
			float w0 = Mathf.Max(t0 - fifth, 0f), w1 = Mathf.Max(t1 - fifth, 0f), w2 = Mathf.Max(t2 - fifth, 0f), w3 = Mathf.Max(t3 - fifth, 0f);
			float total = w0 + w1 + w2 + w3;
			if (total <= 1e-4f)
			{
				ids[0] = ids[1] = ids[2] = ids[3] = 0;
				weights[0] = 1f;
				weights[1] = weights[2] = weights[3] = 0f;
				return;
			}
			ids[0] = i0; ids[1] = i1; ids[2] = i2; ids[3] = i3;
			weights[0] = w0 / total; weights[1] = w1 / total; weights[2] = w2 / total; weights[3] = w3 / total;
		}
	}
}
