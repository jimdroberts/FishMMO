using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's per-layer numbers as one small float texture the array terrain shader loads by layer: column = layer,
	/// row = which number. Replaces the per-layer uniform arrays (<c>_FishLayerST[32]</c> and the rest).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a texture.</b> At 256 layers the six arrays and the three path tables would be 27 KB of constants, pushed per
	/// terrain tile through its property block — more than the 16 KB uniform block OpenGL ES and WebGL guarantee, and
	/// exactly the pattern that broke the grass on OpenGL (large constant arrays varied per draw:
	/// unity-gl-indirect-args-barrier-bug). A structured buffer is not readable in a WebGL 2 fragment shader. A texture is
	/// one binding everywhere, read with <c>Load</c> (no sampler, exact floats).
	/// </para>
	/// <para>
	/// <b>Rows</b>, <see cref="Width"/> columns each (<see cref="TerrainArrayLayerParams.Pack"/> for the first five):
	/// <list type="bullet">
	/// <item><see cref="RowST"/>: 1 / tile size and offset / tile size, applied to world x/z.</item>
	/// <item><see cref="RowTint"/>: the diffuse remap maximum (a tint), normal scale in w.</item>
	/// <item><see cref="RowMaskOffset"/>, <see cref="RowMaskScale"/>: the mask remap as sample × scale + offset.</item>
	/// <item><see cref="RowSurface"/>: metallic, smoothness multiplier, has-mask flag.</item>
	/// <item><see cref="RowFlora"/>: what the layer is made of for the planet's looks (<see cref="TerrainArraySet.LayerFlora"/>).</item>
	/// <item><see cref="RowPaths"/>: the slices of the layer's own biome's path ground, road ground and cobbles (−1 none).</item>
	/// <item><see cref="RowBackdrop"/>: not a layer's but a backdrop channel's: the scene layer channel c of the backdrop's
	/// control maps holds (<see cref="SceneBackdrop.ControlLayers"/>), c itself when the backdrop names none.</item>
	/// </list>
	/// Columns past the scene's layers hold a neutral layer, no flora and no paths, as the arrays' unused slots did.
	/// </para>
	/// </remarks>
	public static class TerrainLayerTable
	{
		/// <summary>Columns: one per layer an index map can name.</summary>
		public const int Width = TerrainArrayLayerParams.MaximumLayers;

		/// <summary>Rows.</summary>
		public const int Rows = 8;

		public const int RowST = 0;
		public const int RowTint = 1;
		public const int RowMaskOffset = 2;
		public const int RowMaskScale = 3;
		public const int RowSurface = 4;
		public const int RowFlora = 5;
		public const int RowPaths = 6;
		public const int RowBackdrop = 7;

		/// <summary>The table's texels, row after row (texel (layer, row) at row × <see cref="Width"/> + layer).</summary>
		/// <param name="layerCount">The scene's layers; columns past it are neutral.</param>
		/// <param name="flora">Per layer what it is made of; null leaves the row zero (the looks then leave the ground as it is).</param>
		/// <param name="earth">Per layer its biome's path ground slice (−1 none); null or short: −1 past it.</param>
		/// <param name="backdropChannels">The scene layer of each backdrop channel; null or short: the channel's own number.</param>
		public static Vector4[] Pack(int layerCount, Vector4[] st, Vector4[] tint, Vector4[] maskOffset, Vector4[] maskScale, Vector4[] surface,
			Vector4[] flora, float[] earth, float[] road, float[] stone, int[] backdropChannels = null)
		{
			var texels = new Vector4[Width * Rows];
			TerrainArrayLayerParams.Allocate(1, out Vector4[] nst, out Vector4[] ntint, out Vector4[] noffset, out Vector4[] nscale, out Vector4[] nsurface);
			int count = Mathf.Clamp(layerCount, 0, Width);
			for (int l = 0; l < Width; l++)
			{
				bool used = l < count;
				texels[RowST * Width + l] = used && st != null && l < st.Length ? st[l] : nst[0];
				texels[RowTint * Width + l] = used && tint != null && l < tint.Length ? tint[l] : ntint[0];
				texels[RowMaskOffset * Width + l] = used && maskOffset != null && l < maskOffset.Length ? maskOffset[l] : noffset[0];
				texels[RowMaskScale * Width + l] = used && maskScale != null && l < maskScale.Length ? maskScale[l] : nscale[0];
				texels[RowSurface * Width + l] = used && surface != null && l < surface.Length ? surface[l] : nsurface[0];
				texels[RowFlora * Width + l] = used && flora != null && l < flora.Length ? flora[l] : Vector4.zero;
				texels[RowPaths * Width + l] = new Vector4(Slice(earth, l), Slice(road, l), Slice(stone, l), -1f);
				texels[RowBackdrop * Width + l] = new Vector4(backdropChannels != null && l < backdropChannels.Length ? backdropChannels[l] : l, 0f, 0f, 0f);
			}
			return texels;
		}

		/// <summary>A layer's slice from a per-layer path map: −1 past the map, or with no map at all.</summary>
		private static float Slice(float[] map, int layer) => map != null && layer < map.Length ? map[layer] : -1f;

		/// <summary>The table of a baked set (its numbers and flora), a scene's path maps and its backdrop's channels.</summary>
		public static Vector4[] Pack(TerrainArraySet set, float[] earth, float[] road, float[] stone, int[] backdropChannels = null)
		{
			if (set == null)
			{
				return Pack(0, null, null, null, null, null, null, earth, road, stone, backdropChannels);
			}
			return Pack(set.LayerCount, set.LayerST, set.LayerTint, set.LayerMaskOffset, set.LayerMaskScale, set.LayerSurface, set.LayerFlora(), earth, road, stone, backdropChannels);
		}

		/// <summary>A table texture, empty: RGBA float, point, clamped, no mips, never saved.</summary>
		public static Texture2D Create(string name)
		{
			return new Texture2D(Width, Rows, TextureFormat.RGBAFloat, false, true)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave,
			};
		}

		/// <summary>Writes packed texels (<see cref="Pack(int, Vector4[], Vector4[], Vector4[], Vector4[], Vector4[], Vector4[], float[], float[], float[])"/>) into a table and uploads it.</summary>
		public static void Fill(Texture2D table, Vector4[] texels)
		{
			var data = new float[texels.Length * 4];
			for (int i = 0; i < texels.Length; i++)
			{
				data[4 * i] = texels[i].x;
				data[4 * i + 1] = texels[i].y;
				data[4 * i + 2] = texels[i].z;
				data[4 * i + 3] = texels[i].w;
			}
			table.SetPixelData(data, 0);
			table.Apply(false, false);
		}
	}
}
