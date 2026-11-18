using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The pure part of the GPU-driven renderer's look families (TerrainGpuRenderer.Looks.cs), free of GPU objects so it can be
	/// tested: which materials may share a family material, the look table's layout, and what a pooled mesh's vertices carry for
	/// the look they wear.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The props (structures, rocks, ice, the cave glow art) wear one material per texture set and tint: ~30 in view at
	/// Moron 2, each one draw's worth of Unity's material set-up outside the SRP Batcher (StdRender.ApplyShader, ~6-7 µs a switch,
	/// 2026-10-27 RenderDoc attribution). Materials that differ only in their slices and a few scalars share ONE family material
	/// instead: the scalars and slices sit in a small table texture, and each vertex of the merged meshes names its look (uv3.x),
	/// so a family draws as one material whatever it wears.
	/// </para>
	/// <para>
	/// <b>No texture is copied.</b> The art generator writes each surface family's maps as Texture2DArray assets and its materials
	/// name their slices (keyword <see cref="Keyword"/>, ProceduralArtLookArrays): every member of a family already references the
	/// same arrays, which the family material simply keeps. Only the table is made at run time (a few KB).
	/// </para>
	/// <para>
	/// <b>The shader side</b> is <see cref="TableKeyword"/> in FishMMO/Weather Lit Indirect's ForwardLit pass
	/// (FishWeatherLitInput.hlsl FishLookSetup, the column layout below). The other passes draw what every member draws alike: a
	/// family is opaque and not alpha-tested, so its shadow, depth and motion passes sample nothing.
	/// </para>
	/// </remarks>
	public static class TerrainLookMath
	{
		/// <summary>The keyword of a material whose maps are look array slices (local; the generator sets it).</summary>
		public const string Keyword = "_FISH_LOOK_ARRAY";

		/// <summary>The keyword of a look family's material: slices and scalars from the table, the look per vertex (local, ForwardLit).</summary>
		public const string TableKeyword = "_FISH_LOOK_TABLE";

		/// <summary>The arrays (FishWeatherLitInput.hlsl): albedo and emission (sRGB), normals, gloss and occlusion (linear).</summary>
		public const string ColourArrayName = "_FishLookColour", NormalArrayName = "_FishLookNormal", MaskArrayName = "_FishLookMask";

		/// <summary>A member's slices (albedo, normal, gloss, occlusion; −1 empty) and emission slice; a family's table and row.</summary>
		public const string SlicesName = "_FishLookSlices", EmissionSliceName = "_FishLookEmissionSlice", TableName = "_FishLookTable", RowName = "_FishLook";

		/// <summary>
		/// Keywords that keep a material out of every family: alpha tested or blended (its depth, shadow and motion passes would
		/// sample the family's first member), parallax and detail maps (textures the look table does not carry), cave rock (its
		/// habitat takes the look's vertex channel). Mirrored in ProceduralArtVariants.LookBlockingKeywords (the editor assembly
		/// cannot see this one; a test pins them equal).
		/// </summary>
		public static readonly string[] BlockingKeywords =
		{
			"_ALPHATEST_ON", "_SURFACE_TYPE_TRANSPARENT", "_ALPHAPREMULTIPLY_ON", "_ALPHAMODULATE_ON", "_PARALLAXMAP", "_DETAIL_MULX2", "_DETAIL_SCALED",
			"_FISH_CAVE_ROCK",
		};

		/// <summary>True when none of <see cref="BlockingKeywords"/> is among <paramref name="keywords"/>.</summary>
		public static bool KeywordsAllowLooks(IEnumerable<string> keywords)
		{
			if (keywords == null)
			{
				return true;
			}
			foreach (string k in keywords)
			{
				if (Array.IndexOf(BlockingKeywords, k) >= 0)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// The per-look scalars, colours and slices (FishMMO/Weather Lit's UnityPerMaterial names) the table carries; every other
		/// property must be equal across a family — the arrays themselves too, by identity. <c>_BaseMap</c>'s scale and offset are
		/// baked into the pooled uv0 instead.
		/// </summary>
		public static readonly string[] LookProperties =
		{
			"_BaseColor", "_SpecColor", "_EmissionColor", "_Cutoff", "_Smoothness", "_Metallic", "_BumpScale", "_OcclusionStrength",
			"_FishMoss", "_FishStructure", "_FishWeatherAmount", SlicesName, EmissionSliceName, RowName,
		};

		/// <summary>
		/// Properties a family leaves out of its key: the shader's legacy and inspector-only names, which act through keywords or
		/// not at all, and the 2D map slots, empty on an array-backed member (their tiling is the look's own, baked into uv0).
		/// </summary>
		public static readonly string[] IgnoredProperties =
		{
			"_MainTex", "_Color", "_GlossMapScale", "_Glossiness", "_GlossyReflections", "_SmoothnessTextureChannel", "_WorkflowMode",
			"_BaseMap", "_BumpMap", "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap", "_EmissionMap",
		};

		/// <summary>Whether a property is the look's (table), ignored, or part of the family key.</summary>
		public static bool IsLookOrIgnored(string property) => Array.IndexOf(LookProperties, property) >= 0 || Array.IndexOf(IgnoredProperties, property) >= 0;

		/// <summary>The look table's width: float4 columns a look (FishWeatherLitInput.hlsl FishLookSetup reads them in this order).</summary>
		public const int TableColumns = 7;

		/// <summary>One look: what its material gives the table (its slices included: −1 an empty slot, the shader's default).</summary>
		public struct LookValues
		{
			public Vector4 BaseMapST;
			public Color BaseColor, SpecColor, EmissionColor;
			public float Cutoff, Smoothness, Metallic, BumpScale, OcclusionStrength, Moss, Structure, WeatherAmount;
			public int AlbedoSlice, NormalSlice, GlossSlice, OcclusionSlice, EmissionSlice;

			/// <summary>No texture in any slot (all defaults).</summary>
			public static LookValues Untextured()
			{
				return new LookValues { BaseMapST = new Vector4(1f, 1f, 0f, 0f), AlbedoSlice = -1, NormalSlice = -1, GlossSlice = -1, OcclusionSlice = -1, EmissionSlice = -1 };
			}

			/// <summary>The slices as a material holds them (<see cref="SlicesName"/>, <see cref="EmissionSliceName"/>), rounded.</summary>
			public void SetSlices(Vector4 slices, float emission)
			{
				AlbedoSlice = Slice(slices.x);
				NormalSlice = Slice(slices.y);
				GlossSlice = Slice(slices.z);
				OcclusionSlice = Slice(slices.w);
				EmissionSlice = Slice(emission);
			}

			private static int Slice(float v) => v < 0f ? -1 : Mathf.RoundToInt(v);
		}

		/// <summary>
		/// The look's table row, column by column:
		/// 0 base colour; 1 specular colour; 2 emission colour (rgb), alpha cutoff; 3 smoothness, metallic, bump scale,
		/// occlusion strength; 4 moss, structure, weather amount, 0; 5 slices albedo, normal, gloss, occlusion; 6 emission slice.
		/// </summary>
		public static void WriteRow(in LookValues v, Color[] pixels, int width, int row)
		{
			int at = row * width;
			pixels[at + 0] = v.BaseColor;
			pixels[at + 1] = v.SpecColor;
			pixels[at + 2] = new Color(v.EmissionColor.r, v.EmissionColor.g, v.EmissionColor.b, v.Cutoff);
			pixels[at + 3] = new Color(v.Smoothness, v.Metallic, v.BumpScale, v.OcclusionStrength);
			pixels[at + 4] = new Color(v.Moss, v.Structure, v.WeatherAmount, 0f);
			pixels[at + 5] = new Color(v.AlbedoSlice, v.NormalSlice, v.GlossSlice, v.OcclusionSlice);
			pixels[at + 6] = new Color(v.EmissionSlice, 0f, 0f, 0f);
		}

		/// <summary>Rows of the table texture for <paramref name="looks"/> looks: the next power of two, at least 16.</summary>
		public static int TableRows(int looks)
		{
			int c = 16;
			while (c < looks)
			{
				c *= 2;
			}
			return c;
		}

		/// <summary>The GPU bytes of a table of <paramref name="rows"/> rows (RGBAHalf, <see cref="TableColumns"/> texels a row).</summary>
		public static long TableBytes(int rows) => (long)rows * TableColumns * 8;

		/// <summary>
		/// What a pooled part's vertices carry for its look: uv0 transformed by the look's <c>_BaseMap</c> scale and offset (the
		/// shader's TRANSFORM_TEX, done once here so a family needs no per-look tiling), uv3.x the look. The part's vertices are
		/// the ones its submesh's <paramref name="indices"/> (absolute, base vertex applied) name: each is baked once
		/// (<paramref name="baked"/>), whatever CombineMeshes copied. Returns the vertices baked.
		/// </summary>
		public static int BakeLook(Vector2[] uv0, Vector2[] uv3, bool[] baked, int[] indices, Vector4 st, int look)
		{
			int n = 0;
			for (int k = 0; k < indices.Length; k++)
			{
				int i = indices[k];
				if (i < 0 || i >= uv3.Length || baked[i])
				{
					continue;
				}
				baked[i] = true;
				n++;
				if (uv0 != null && i < uv0.Length)
				{
					Vector2 uv = uv0[i];
					uv0[i] = new Vector2(uv.x * st.x + st.z, uv.y * st.y + st.w);
				}
				uv3[i] = new Vector2(look, 0f);
			}
			return n;
		}
	}
}
