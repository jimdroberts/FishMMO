using System;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How a ground layer glows (2026-10-25, phase I2r "molten ground", decision 65 (a)): not at all, along the seams of a crust that
	/// overturns on the shared clock (a lava lake's skin, a pāhoehoe sheet's cracks), or over the whole layer from the world's own
	/// heat (a Venus plain's dull red at night). Drawn on the GPU by the terrain array shader (FishGroundGlow.hlsl) and written into
	/// the lava's molten mask so its light and the heat shimmer see it (<see cref="FishMMO.Shared.Weather.GroundGlow"/> is the C# twin).
	/// </summary>
	/// <remarks>APPEND-ONLY: the value is serialised on the templates and packed into the terrain's layer table.</remarks>
	public enum BiomeGroundGlowMode : byte
	{
		/// <summary>No glow: every layer before 2026-10-25.</summary>
		None = 0,
		/// <summary>Glowing seams round crust plates, each plate overturning once a period at its own hashed moment (OVERTURN).</summary>
		Seams = 1,
		/// <summary>The whole layer glows at the world's mean surface temperature (plus the layer's offset).</summary>
		Whole = 2,
	}

	/// <summary>
	/// One ground layer's glow (<see cref="TerrainTextureLayer.glow"/>): its mode and the numbers the mode reads. Default (None) is no
	/// glow, and a scene whose layers name none draws exactly as before.
	/// </summary>
	[Serializable]
	public struct BiomeGroundGlow
	{
		/// <summary>The motion clock's wrap, s: every period divides it (WorldMotion.ShaderWrapSeconds).</summary>
		public const int WrapSeconds = 10000;
		/// <summary>The default melt at a freshly opened seam, K: basalt's (the lava material's 1420 K, rounded).</summary>
		public const float DefaultMeltKelvin = 1400f;
		/// <summary>The default plate size, m: a lava lake's crust plates.</summary>
		public const float DefaultCellMetres = 6f;
		/// <summary>The default overturn period, s: the Overturn glow mode's (SceneGlowModes.OverturnHz, one every 400 s).</summary>
		public const int DefaultPeriodSeconds = 400;

		[Tooltip("None (no glow, as before), Seams (glowing cracks round crust plates that overturn on the shared clock) or Whole (the whole layer at the world's temperature plus the offset below).")]
		public BiomeGroundGlowMode Mode;
		[Tooltip("Seams: the melt's temperature at a freshly opened seam, K (1400 basalt). Whole: kelvin added to the world's mean surface temperature (0: the world's own).")]
		public float Kelvin;
		[Tooltip("How much of the glow is drawn, 0..1.")]
		[Range(0f, 1f)] public float Strength;
		[Tooltip("Seams: the crust plates' size, m.")]
		public float CellMetres;
		[Tooltip("Seams: how often each plate overturns, s; snapped to a whole divisor of the shared clock's 10 000 s wrap.")]
		public int PeriodSeconds;

		/// <summary>True when the layer does not glow.</summary>
		public bool IsNone => Mode == BiomeGroundGlowMode.None || Mode > BiomeGroundGlowModes.Last || Strength <= 0f;

		/// <summary>Glowing seams round overturning crust plates.</summary>
		public static BiomeGroundGlow Seams(float meltKelvin = DefaultMeltKelvin, float cellMetres = DefaultCellMetres, int periodSeconds = DefaultPeriodSeconds,
			float strength = 1f)
		{
			return new BiomeGroundGlow
			{
				Mode = BiomeGroundGlowMode.Seams,
				Kelvin = Mathf.Clamp(meltKelvin, 600f, 2500f),
				Strength = Mathf.Clamp01(strength),
				CellMetres = Mathf.Clamp(cellMetres, 0.5f, 200f),
				PeriodSeconds = BiomeGroundGlowModes.SnapPeriod(periodSeconds),
			};
		}

		/// <summary>The whole layer at the world's mean surface temperature plus <paramref name="offsetKelvin"/>.</summary>
		public static BiomeGroundGlow Whole(float offsetKelvin = 0f, float strength = 1f)
		{
			return new BiomeGroundGlow
			{
				Mode = BiomeGroundGlowMode.Whole,
				Kelvin = Mathf.Clamp(offsetKelvin, -1000f, 2000f),
				Strength = Mathf.Clamp01(strength),
			};
		}

		/// <summary>
		/// The layer table's glow texel (TerrainLayerTable.RowGlow; FishGroundGlow.hlsl): x the mode plus 8 × the period (s), y the kelvin,
		/// z the strength, w the plate size (m). Zero for no glow.
		/// </summary>
		public Vector4 Pack()
		{
			if (IsNone)
			{
				return Vector4.zero;
			}
			int period = Mode == BiomeGroundGlowMode.Seams ? BiomeGroundGlowModes.SnapPeriod(PeriodSeconds) : 0;
			float cell = Mode == BiomeGroundGlowMode.Seams ? Mathf.Clamp(CellMetres > 0f ? CellMetres : DefaultCellMetres, 0.5f, 200f) : 0f;
			return new Vector4((float)Mode + 8f * period, Kelvin, Mathf.Clamp01(Strength), cell);
		}

		/// <summary>The authoring mark: empty for none, else "mode|kelvin|strength|cell|period" to 1/10 000.</summary>
		public string Signature()
		{
			if (IsNone)
			{
				return string.Empty;
			}
			Vector4 p = Pack();
			return ((BiomeGroundGlowMode)Mode).ToString() + "|" + Q(p.y) + "|" + Q(p.z) + "|" + Q(p.w) + "|" + Mathf.FloorToInt(p.x / 8f).ToString(CultureInfo.InvariantCulture);
		}

		private static string Q(float v) => (Mathf.Round(v * 10000f) / 10000f).ToString("R", CultureInfo.InvariantCulture);

		public override string ToString() => IsNone ? "None" : Signature();
	}

	/// <summary>Helpers for <see cref="BiomeGroundGlowMode"/> and <see cref="BiomeGroundGlow"/>.</summary>
	public static class BiomeGroundGlowModes
	{
		/// <summary>The highest mode the GPU knows (FishGroundGlow.hlsl FISH_GROUND_GLOW_WHOLE).</summary>
		public const BiomeGroundGlowMode Last = BiomeGroundGlowMode.Whole;

		/// <summary>The whole divisors of the 10 000 s wrap from 25 s up: the periods a seam's overturn may take.</summary>
		public static readonly int[] Periods = { 25, 40, 50, 80, 100, 125, 200, 250, 400, 500, 625, 1000, 1250, 2000, 2500, 5000, 10000 };

		/// <summary>The period nearest <paramref name="seconds"/> that divides the wrap (none or less: the default 400 s).</summary>
		public static int SnapPeriod(int seconds)
		{
			if (seconds <= 0)
			{
				return BiomeGroundGlow.DefaultPeriodSeconds;
			}
			int best = Periods[0];
			foreach (int p in Periods)
			{
				if (Math.Abs(p - seconds) < Math.Abs(best - seconds))
				{
					best = p;
				}
			}
			return best;
		}

		/// <summary>Unpacks a layer table glow texel (<see cref="BiomeGroundGlow.Pack"/>): the mode and the period.</summary>
		public static BiomeGroundGlowMode ModeOf(Vector4 packed, out int periodSeconds)
		{
			int whole = Mathf.Max(0, Mathf.RoundToInt(packed.x));
			periodSeconds = whole / 8;
			int mode = whole % 8;
			return mode > (int)Last ? BiomeGroundGlowMode.None : (BiomeGroundGlowMode)mode;
		}

		/// <summary>Parses a mode by name (case-insensitive); false for anything else.</summary>
		public static bool TryParse(string text, out BiomeGroundGlowMode mode)
		{
			mode = BiomeGroundGlowMode.None;
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			foreach (BiomeGroundGlowMode value in (BiomeGroundGlowMode[])Enum.GetValues(typeof(BiomeGroundGlowMode)))
			{
				if (string.Equals(value.ToString(), text.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					mode = value;
					return true;
				}
			}
			return false;
		}
	}
}
