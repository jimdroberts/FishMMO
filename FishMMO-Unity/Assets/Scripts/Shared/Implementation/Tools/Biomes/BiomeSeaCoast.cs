using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The sea's SURFACE in the look of its coast (2026-10-18, phase I2k, decision 32): a small texture over the scene, baked from
	/// the scene's biome map when the cut or a repaint writes it, that the ocean shader (FishWaterShading.hlsl FishSeaCoastLook)
	/// reads once a pixel to tint its shallow, deep, sediment and scatter colours and scale its absorption, fading to the planet's
	/// own sea (WaterEnvironment's climate colours) offshore. Pure: the bake, its encoding and a CPU mirror of the shader's read.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same coast as the underwater look.</b> Each texel is the blend <see cref="BiomeSeaLooks.Coast"/> gives at its centre,
	/// on the same world-fixed lattice, reach and falloff the underwater pass asks at the eye (<see cref="BiomeSeaLooks.ReachMetres"/>,
	/// <see cref="BiomeSeaLooks.SpacingMetres"/>, <see cref="BiomeSeaLooks.Weight"/>), mixed into today's sea by its coverage
	/// (<see cref="BiomeSeaLooks.Mix"/>). So a diver and a sailor off the same bog see the same water, from below and from above.
	/// </para>
	/// <para>
	/// <b>A look relative to today's sea, not a colour.</b> The surface's colours come per body from the world's climate (clear
	/// cold blue to warm productive green), and a coast's look is a diver's tint, not a surface colour. So the texture holds how the
	/// coast's water DIFFERS from today's: per channel, the ratio of its tint to today's, floored as the falls' ratios are
	/// (<see cref="RatioFloor"/>, InlandWaterRenderer.FallLookFloor), and the ratio of its extinction (murk ÷ visibility) to today's.
	/// The shader multiplies the climate's colours and absorption by them: a peat coast browns and darkens whichever sea it meets.
	/// Today's look is the ratio 1 everywhere.
	/// </para>
	/// <para>
	/// <b>Coded in doublings, 8 bits a channel (RGBA8, every platform).</b> A byte b holds log2(ratio) = (b − 128) ÷ <see cref="StepsPerDoubling"/>:
	/// 128 is exactly 1, the range a sixteenth to sixteen times, steps of about 2%. Bilinear filtering between texels then blends the
	/// ratios geometrically, which is how a stain thins.
	/// </para>
	/// <para>
	/// <b>Nothing when there is nothing to say.</b> A map whose every biome has today's sea look bakes NO texture (an empty bake),
	/// and the sea then never reads one: the surface draws exactly as before. The baked rectangle runs past the map by the reach
	/// and a texel (<see cref="Margin"/>), where no biome lies, so its edge texels are exactly 1 and the clamped read past them is the
	/// planet's sea, as far as the horizon.
	/// </para>
	/// </remarks>
	public static class BiomeSeaCoast
	{
		/// <summary>The texel the bake aims for, metres: a few texels across the reach's falloff, about 128² for a four-kilometre scene.</summary>
		public const float TexelMetres = 32f;

		/// <summary>The fewest texels on a side.</summary>
		public const int MinTexels = 8;

		/// <summary>The most texels on a side (a scene over eight kilometres takes coarser texels).</summary>
		public const int MaxTexels = 256;

		/// <summary>The byte that codes a ratio of exactly 1: today's sea.</summary>
		public const byte Neutral = 128;

		/// <summary>How many byte steps make a doubling of a ratio.</summary>
		public const float StepsPerDoubling = 32f;

		/// <summary>What each channel's ratio is floored by, so a near-black channel cannot run off (as InlandWaterRenderer.FallLookFloor).</summary>
		public const float RatioFloor = 0.05f;

		/// <summary>How far the baked rectangle runs past the map on every side, metres: the reach and a texel, so its edge says nothing.</summary>
		public static float Margin => BiomeSeaLooks.ReachMetres + TexelMetres;

		/// <summary>A baked coastal look: <see cref="Width"/> × <see cref="Height"/> RGBA8 texels, row-major from the south-west, over <see cref="Rect"/>.</summary>
		public struct Bake
		{
			public int Width;
			public int Height;
			/// <summary>xy the world minimum (east, north), zw the size, metres.</summary>
			public Vector4 Rect;
			/// <summary>Four bytes a texel: the tint ratio's doublings in RGB, the absorption ratio's in A (see <see cref="BiomeSeaCoast"/>).</summary>
			public byte[] Texels;

			/// <summary>True when there is a look to draw (an empty bake: the sea as it always was).</summary>
			public bool Any => Width > 0 && Height > 0 && Texels != null && Texels.Length == Width * Height * 4 && Rect.z > 0f && Rect.w > 0f;

			/// <summary>No coastal look.</summary>
			public static Bake None => new Bake { Texels = Array.Empty<byte>() };
		}

		/// <summary>
		/// Bakes a biome map's coastal look (pure, deterministic): <see cref="Bake.None"/> when no biome on it has a sea look of its own
		/// (<see cref="BiomeSeaLooks.OwnLook"/>), else the texels over the map grown by <see cref="Margin"/>.
		/// </summary>
		/// <param name="width">The map's columns (world X).</param>
		/// <param name="height">The map's rows (world Z).</param>
		/// <param name="ids">Its biome IDs, row-major from the south-west; 0 no biome.</param>
		/// <param name="origin">Its south-west corner, world metres.</param>
		/// <param name="size">Its extent, world metres.</param>
		/// <param name="lookOf">An ID's sea look, null for none (it says nothing, as today's does).</param>
		public static Bake For(int width, int height, int[] ids, Vector2 origin, Vector2 size, Func<int, BiomeSeaLook?> lookOf)
		{
			if (width <= 0 || height <= 0 || ids == null || ids.Length != width * height || lookOf == null || !(size.x > 0f) || !(size.y > 0f))
			{
				return Bake.None;
			}
			var looks = new Dictionary<int, BiomeSeaLook?>();
			BiomeSeaLook? LookOf(int id)
			{
				if (id == 0)
				{
					return null;
				}
				if (!looks.TryGetValue(id, out BiomeSeaLook? look))
				{
					look = lookOf(id);
					looks[id] = look;
				}
				return look;
			}
			bool any = false;
			foreach (int id in ids)
			{
				if (LookOf(id) is BiomeSeaLook l && BiomeSeaLooks.OwnLook(l))
				{
					any = true;
					break;
				}
			}
			if (!any)
			{
				return Bake.None;
			}

			float reach = BiomeSeaLooks.ReachMetres, spacing = BiomeSeaLooks.SpacingMetres;
			float margin = Margin;
			Vector4 rect;
			int w, h;
			for (int pass = 0; ; pass++)
			{
				rect = new Vector4(origin.x - margin, origin.y - margin, size.x + 2f * margin, size.y + 2f * margin);
				w = Texels(rect.z);
				h = Texels(rect.w);
				// A scene so large its texels pass TexelMetres (MaxTexels): the margin grows so the edge texels' centres still lie past the reach.
				float texel = Mathf.Max(rect.z / w, rect.w / h);
				if (margin - 0.5f * texel > reach || pass >= 3)
				{
					break;
				}
				margin = reach + texel;
			}

			// The map read once a lattice point: the texels' reaches overlap many times over.
			var lattice = new Dictionary<long, int>();
			int IdAt(float x, float z)
			{
				long key = ((long)Mathf.RoundToInt(x / spacing) << 32) ^ (uint)Mathf.RoundToInt(z / spacing);
				if (!lattice.TryGetValue(key, out int id))
				{
					id = MapID(width, height, ids, origin, size, x, z);
					lattice[key] = id;
				}
				return id;
			}

			var texels = new byte[w * h * 4];
			for (int j = 0; j < h; j++)
			{
				float z = rect.y + (j + 0.5f) * rect.w / h;
				for (int i = 0; i < w; i++)
				{
					float x = rect.x + (i + 0.5f) * rect.z / w;
					BiomeSeaLook coast = BiomeSeaLooks.Coast(x, z, IdAt, LookOf, reach, spacing, out float coverage);
					Encode(coast, coverage, texels, (j * w + i) * 4);
				}
			}
			return new Bake { Width = w, Height = h, Rect = rect, Texels = texels };
		}

		/// <summary>Texels along a side of <paramref name="metres"/>: <see cref="TexelMetres"/> apart, within <see cref="MinTexels"/> … <see cref="MaxTexels"/>.</summary>
		public static int Texels(float metres) => Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0f, metres) / TexelMetres), MinTexels, MaxTexels);

		/// <summary>The ID under (x, z) exactly as <see cref="SceneBiomeMap.IDAt"/> reads it: 0 outside the grid.</summary>
		public static int MapID(int width, int height, int[] ids, Vector2 origin, Vector2 size, float x, float z)
		{
			if (x < origin.x || x > origin.x + size.x || z < origin.y || z > origin.y + size.y)
			{
				return 0;
			}
			int cx = Mathf.Clamp(Mathf.FloorToInt((x - origin.x) / size.x * width), 0, width - 1);
			int cz = Mathf.Clamp(Mathf.FloorToInt((z - origin.y) / size.y * height), 0, height - 1);
			return ids[cz * width + cx];
		}

		/// <summary>
		/// The ratios the sea's surface takes at a point whose coast is <paramref name="coast"/> by <paramref name="coverage"/> (pure):
		/// xyz the tint's to today's per channel (gamma, as the tint), w the extinction's to today's. Exactly (1, 1, 1, 1) at coverage 0.
		/// </summary>
		public static Vector4 Ratios(BiomeSeaLook coast, float coverage)
		{
			BiomeSeaLook today = BiomeSeaLooks.Today;
			if (!(coverage > 0f))
			{
				return Vector4.one;
			}
			BiomeSeaLook mixed = BiomeSeaLooks.Mix(today, coast, coverage);
			float R(float look, float own) => (Mathf.Max(0f, look) + RatioFloor) / (Mathf.Max(0f, own) + RatioFloor);
			return new Vector4(R(mixed.Tint.r, today.Tint.r), R(mixed.Tint.g, today.Tint.g), R(mixed.Tint.b, today.Tint.b),
				mixed.Extinction / today.Extinction);
		}

		/// <summary>Writes a point's ratios (<see cref="Ratios"/>) as four bytes at <paramref name="at"/>.</summary>
		public static void Encode(BiomeSeaLook coast, float coverage, byte[] into, int at)
		{
			Vector4 ratios = Ratios(coast, coverage);
			into[at] = Code(ratios.x);
			into[at + 1] = Code(ratios.y);
			into[at + 2] = Code(ratios.z);
			into[at + 3] = Code(ratios.w);
		}

		/// <summary>A ratio as its byte: <see cref="Neutral"/> + its doublings × <see cref="StepsPerDoubling"/>, rounded and clamped.</summary>
		public static byte Code(float ratio)
		{
			if (!(ratio > 0f) || float.IsInfinity(ratio))
			{
				return ratio > 0f ? (byte)255 : (byte)0;
			}
			if (ratio == 1f)
			{
				return Neutral;
			}
			float steps = Mathf.Log(ratio, 2f) * StepsPerDoubling;
			return (byte)Mathf.Clamp(Mathf.RoundToInt(Neutral + steps), 0, 255);
		}

		/// <summary>The ratio a coded value stands for, as the shader works it out from the filtered texel (<paramref name="coded"/> 0 … 255, fractional where filtered).</summary>
		public static float Ratio(float coded) => Mathf.Pow(2f, (coded - Neutral) / StepsPerDoubling);

		/// <summary>
		/// What the shader reads at world (<paramref name="x"/>, <paramref name="z"/>) (the CPU's mirror of FishSeaCoastLook): the
		/// bake's coded values, bilinear between texel centres and held at the edge texels past them, as four ratios (xyz the tint's
		/// in gamma, w the absorption's). (1, 1, 1, 1) for an empty bake.
		/// </summary>
		public static Vector4 Sample(Bake bake, float x, float z)
		{
			if (!bake.Any)
			{
				return Vector4.one;
			}
			float gx = Mathf.Clamp((x - bake.Rect.x) / bake.Rect.z * bake.Width - 0.5f, 0f, bake.Width - 1);
			float gz = Mathf.Clamp((z - bake.Rect.y) / bake.Rect.w * bake.Height - 0.5f, 0f, bake.Height - 1);
			int x0 = Mathf.Min((int)gx, bake.Width - 1), z0 = Mathf.Min((int)gz, bake.Height - 1);
			int x1 = Mathf.Min(x0 + 1, bake.Width - 1), z1 = Mathf.Min(z0 + 1, bake.Height - 1);
			float fx = gx - x0, fz = gz - z0;
			var result = new Vector4();
			for (int c = 0; c < 4; c++)
			{
				float Texel(int i, int j) => bake.Texels[(j * bake.Width + i) * 4 + c];
				float coded = Mathf.Lerp(Mathf.Lerp(Texel(x0, z0), Texel(x1, z0), fx), Mathf.Lerp(Texel(x0, z1), Texel(x1, z1), fx), fz);
				result[c] = Ratio(coded);
			}
			return result;
		}
	}
}
