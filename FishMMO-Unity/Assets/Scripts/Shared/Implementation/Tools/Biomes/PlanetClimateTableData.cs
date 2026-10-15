using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A body's climate table (<see cref="PlanetClimateTable"/>) as it was baked into the body's own asset: read at load in
	/// place of solving the year's energy balance, which takes the better part of a second per body.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>On the body, not beside it.</b> The table is the body's own data and both the server and the client need it (the
	/// weather runs on both), so it travels with the body: renaming, moving or removing a body carries it, and a build ships
	/// exactly what was baked — so every peer reads the same numbers, where solving it on each machine left them to agree
	/// only as far as their floating point did.
	/// </para>
	/// <para>
	/// <b>Compact.</b> Each value is held as a half-precision offset from the world's mean (within ±64 K that is to about
	/// 0.016 K, and 0.03 K out to ±128 K), packed in one byte array, which the asset writes as a single line.
	/// </para>
	/// <para>
	/// <b>Never silently stale.</b> It carries the fingerprint of everything it was solved from (<see cref="PlanetClimateTable.Fingerprint"/>,
	/// the model's version included). A table whose fingerprint no longer matches its body is not read: the body's climate
	/// is solved afresh and the editor says to bake.
	/// </para>
	/// </remarks>
	[Serializable]
	public sealed class PlanetClimateTableData
	{
		/// <summary>The fingerprint of what the table was solved from; 0 when nothing has been baked.</summary>
		public long Fingerprint;

		/// <summary>The year the phases divide, hours; the world's mean, K; what the solve shifted the table by, K.</summary>
		public double YearHours, MeanKelvin, Residual;

		/// <summary>The land then the ocean column, [latitude + 90, phase], each value a half-float offset from <see cref="MeanKelvin"/>.</summary>
		[HideInInspector] public byte[] Values = Array.Empty<byte>();

		/// <summary>How many values the arrays hold: 181 latitudes × the phases × two columns.</summary>
		public static int Count => 181 * SeasonalEnergyBalance.Phases * 2;

		/// <summary>True when there is a table to read.</summary>
		public bool HasTable => Fingerprint != 0L && Values != null && Values.Length == Count * 2;

		/// <summary>Packs a table's columns.</summary>
		public static PlanetClimateTableData Of(long fingerprint, double yearHours, double meanKelvin, double residual, float[] land, float[] ocean)
		{
			int half = land.Length;
			var bytes = new byte[(half + ocean.Length) * 2];
			for (int i = 0; i < half + ocean.Length; i++)
			{
				float kelvin = i < half ? land[i] : ocean[i - half];
				ushort h = ToHalf((float)(kelvin - meanKelvin));
				bytes[i * 2] = (byte)(h & 0xFF);
				bytes[i * 2 + 1] = (byte)(h >> 8);
			}
			return new PlanetClimateTableData
			{
				Fingerprint = fingerprint,
				YearHours = yearHours,
				MeanKelvin = meanKelvin,
				Residual = residual,
				Values = bytes,
			};
		}

		// ── IEEE half precision, managed (Mathf's is a native call: no tests outside the engine, no worker threads) ──

		[StructLayout(LayoutKind.Explicit)]
		private struct Bits
		{
			[FieldOffset(0)] public float Float;
			[FieldOffset(0)] public uint UInt;
		}

		/// <summary>A float to the nearest half, ties to even; past the half's range to infinity.</summary>
		public static ushort ToHalf(float value)
		{
			uint f = new Bits { Float = value }.UInt;
			uint sign = (f >> 16) & 0x8000u;
			int exponent = (int)((f >> 23) & 0xFF) - 127 + 15;
			uint mantissa = f & 0x7FFFFFu;
			if (exponent >= 31)
			{
				return (ushort)(sign | 0x7C00u | (((f >> 23) & 0xFF) == 0xFF && mantissa != 0 ? 0x200u : 0u));
			}
			if (exponent <= 0)
			{
				if (exponent < -10)
				{
					return (ushort)sign;
				}
				mantissa |= 0x800000u;
				int shift = 14 - exponent;
				uint rounded = mantissa >> shift;
				uint rest = mantissa & ((1u << shift) - 1u);
				uint halfway = 1u << (shift - 1);
				if (rest > halfway || (rest == halfway && (rounded & 1u) != 0u))
				{
					rounded++;
				}
				return (ushort)(sign | rounded);
			}
			uint m = mantissa >> 13;
			uint r = mantissa & 0x1FFFu;
			uint h = sign | ((uint)exponent << 10) | m;
			if (r > 0x1000u || (r == 0x1000u && (m & 1u) != 0u))
			{
				h++;
			}
			return (ushort)h;
		}

		/// <summary>A half to the float it stands for.</summary>
		public static float FromHalf(ushort half)
		{
			uint sign = (uint)(half & 0x8000) << 16;
			int exponent = (half >> 10) & 0x1F;
			uint mantissa = (uint)(half & 0x3FF);
			uint f;
			if (exponent == 0)
			{
				if (mantissa == 0)
				{
					f = sign;
				}
				else
				{
					exponent = 1;
					while ((mantissa & 0x400u) == 0)
					{
						mantissa <<= 1;
						exponent--;
					}
					mantissa &= 0x3FFu;
					f = sign | ((uint)(exponent - 15 + 127) << 23) | (mantissa << 13);
				}
			}
			else if (exponent == 31)
			{
				f = sign | 0x7F800000u | (mantissa << 13);
			}
			else
			{
				f = sign | ((uint)(exponent - 15 + 127) << 23) | (mantissa << 13);
			}
			return new Bits { UInt = f }.Float;
		}

		/// <summary>Unpacks the columns, K; false when there is no table.</summary>
		public bool TryUnpack(out float[] land, out float[] ocean)
		{
			land = null;
			ocean = null;
			if (!HasTable)
			{
				return false;
			}
			int half = Count / 2;
			land = new float[half];
			ocean = new float[half];
			for (int i = 0; i < Count; i++)
			{
				ushort h = (ushort)(Values[i * 2] | (Values[i * 2 + 1] << 8));
				float kelvin = (float)(MeanKelvin + FromHalf(h));
				if (i < half)
				{
					land[i] = kelvin;
				}
				else
				{
					ocean[i - half] = kelvin;
				}
			}
			return true;
		}
	}
}
