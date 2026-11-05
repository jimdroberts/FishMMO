using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The pure half of the vegetation families (DetailScatterRenderer, 2026-11-04): which FishMMO/Vegetation properties a family's
	/// table carries per kind, and the table's row layout as FishVegetationPasses.hlsl FishVegTableSetup reads it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The art generator writes a material per kind: 678 Detail_* materials, 614 of them one structure (the shared foliage atlas,
	/// _ALPHATEST_ON, queue 2450, cull off) that differ only in the scalars of <see cref="TableProperties"/>. A family is every
	/// clone equal in everything else (TerrainGpuRenderer.VegFamilyKey); it draws through ONE material with <see cref="Keyword"/>
	/// on, each kind a row of the family's table, named per vertex by the pooled mesh's uv4.x. Exact up to the table's half
	/// precision (11 bits: a colour within 1/2048, far under one 8-bit step).
	/// </para>
	/// <para>
	/// Colours are stored linear: the project renders in linear space, where Unity converts a material's Color properties
	/// before the shader reads them (a Vector property, <c>_GroundSink</c>, is passed as it is).
	/// </para>
	/// </remarks>
	public static class TerrainVegTableMath
	{
		/// <summary>The family material's keyword (FishVegetationIndirect.shader, every pass).</summary>
		public const string Keyword = "_FISH_VEG_TABLE";

		/// <summary>The family's table texture (RGBAHalf, point, <see cref="Columns"/> texels a row).</summary>
		public const string TableName = "_FishVegTable";

		/// <summary>float4 columns a row.</summary>
		public const int Columns = 7;

		/// <summary>The mesh channel naming a vertex's row (TEXCOORD4: the vegetation meshes use 0..3).</summary>
		public const int RowChannel = 4;

		/// <summary>The properties a row carries, which a family's members may differ in. Everything else they share exactly.</summary>
		public static readonly string[] TableProperties =
		{
			"_HealthyColor", "_DryColor", "_GroundSink", "_SnowBury", "_TintPatchMetres", "_WindSway", "_WindFlutter", "_Aquatic",
			"_Evergreen", "_Deciduous", "_TintSpread", "_VaryLean", "_VaryPartDroop", "_DistanceFade", "_Clinging",
			"_FishWeatherAmount", "_Translucency",
		};

		public static bool IsTableProperty(string property) => Array.IndexOf(TableProperties, property) >= 0;

		/// <summary>One kind's row: its <see cref="TableProperties"/> as the shader reads them (colours linear).</summary>
		public struct Row
		{
			public Color HealthyColor, DryColor;
			public Vector4 GroundSink;
			public float SnowBury, TintPatchMetres, WindSway, WindFlutter;
			public float Aquatic, Evergreen, Deciduous, TintSpread;
			public float VaryLean, VaryPartDroop, DistanceFade, Clinging;
			public float WeatherAmount, Translucency;
		}

		/// <summary>A material's row: its own values, colours converted to linear when the project renders linear.</summary>
		public static Row RowOf(Material m, bool linear)
		{
			Color healthy = m.GetColor("_HealthyColor"), dry = m.GetColor("_DryColor");
			return new Row
			{
				HealthyColor = linear ? healthy.linear : healthy,
				DryColor = linear ? dry.linear : dry,
				GroundSink = m.GetVector("_GroundSink"),
				SnowBury = m.GetFloat("_SnowBury"),
				TintPatchMetres = m.GetFloat("_TintPatchMetres"),
				WindSway = m.GetFloat("_WindSway"),
				WindFlutter = m.GetFloat("_WindFlutter"),
				Aquatic = m.GetFloat("_Aquatic"),
				Evergreen = m.GetFloat("_Evergreen"),
				Deciduous = m.GetFloat("_Deciduous"),
				TintSpread = m.GetFloat("_TintSpread"),
				VaryLean = m.GetFloat("_VaryLean"),
				VaryPartDroop = m.GetFloat("_VaryPartDroop"),
				DistanceFade = m.GetFloat("_DistanceFade"),
				Clinging = m.GetFloat("_Clinging"),
				WeatherAmount = m.GetFloat("_FishWeatherAmount"),
				Translucency = m.GetFloat("_Translucency"),
			};
		}

		/// <summary>
		/// The row's texels: 0 healthy colour; 1 dry colour; 2 ground sink; 3 snow bury, tint patch metres, wind sway, wind
		/// flutter; 4 aquatic, evergreen, deciduous, tint spread; 5 vary lean, vary part droop, distance fade, clinging;
		/// 6 weather amount, translucency, 0, 0.
		/// </summary>
		public static void WriteRow(in Row r, Color[] pixels, int row)
		{
			int at = row * Columns;
			pixels[at + 0] = r.HealthyColor;
			pixels[at + 1] = r.DryColor;
			pixels[at + 2] = new Color(r.GroundSink.x, r.GroundSink.y, r.GroundSink.z, r.GroundSink.w);
			pixels[at + 3] = new Color(r.SnowBury, r.TintPatchMetres, r.WindSway, r.WindFlutter);
			pixels[at + 4] = new Color(r.Aquatic, r.Evergreen, r.Deciduous, r.TintSpread);
			pixels[at + 5] = new Color(r.VaryLean, r.VaryPartDroop, r.DistanceFade, r.Clinging);
			pixels[at + 6] = new Color(r.WeatherAmount, r.Translucency, 0f, 0f);
		}

		/// <summary>Rows of a table texture for <paramref name="rows"/> kinds: the next power of two, at least 16 (grown, never shrunk).</summary>
		public static int TableRows(int rows) => TerrainLookMath.TableRows(rows);
	}
}
