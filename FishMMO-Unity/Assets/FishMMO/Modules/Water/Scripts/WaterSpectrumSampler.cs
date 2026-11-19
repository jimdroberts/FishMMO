using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace FishMMO.Water
{
	/// <summary>
	/// <see cref="WaterSpectrum"/>'s evaluation and sum in Burst, four components to a lane: what
	/// <see cref="WaterSurface.HeightAt"/> asks for the sea under the camera and under every floater.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Presentation only.</b> The height a client draws its floaters and its underwater pass by, which no other peer
	/// computes: the simulation's sea is <c>SeaTide</c> through <c>WaterQuery</c>, still and managed, and never comes
	/// here. So it may differ from <see cref="WaterSpectrum.Sample"/> by float rounding — the sum is taken in four
	/// lanes and added at the end, and the sines are Burst's (at its high precision, a single unit in the last place)
	/// rather than the double-precision ones Mathf rounds — which is far below what the eye can see on a wave.
	/// </para>
	/// <para>
	/// <b>Why.</b> A height is the spectrum summed three times over (the throw is solved back to its starting point),
	/// fifteen hundred components each, two sines apiece: about a hundred microseconds managed, and several systems ask
	/// every frame. Here a sum is a few microseconds.
	/// </para>
	/// <para>
	/// The components are copied in once per spectrum (it is rebuilt only when the sea state moves by percents) and the
	/// evolved amplitudes once per instant.
	/// </para>
	/// </remarks>
	public sealed class WaterSpectrumSampler : IDisposable
	{
		private WaterSpectrum bound;
		private double evaluatedAt = double.NaN;
		private int lanesPerCascade;
		private float choppiness;

		private NativeArray<float4> waveX, waveZ, unitX, unitZ;
		private NativeArray<float4> ax, ay, bx, by, omega;
		private NativeArray<float4> hr, hi;
		private NativeArray<float> shifts;
		private NativeArray<float> result;

		/// <summary>Copies a spectrum's components in, when it is not the one already here.</summary>
		public void Bind(WaterSpectrum spectrum)
		{
			if (ReferenceEquals(spectrum, bound) || spectrum == null)
			{
				return;
			}
			int count = spectrum.Count;
			int cascades = spectrum.CascadeCount;
			int lanes = count / 4;
			// Lanes must not straddle a cascade (each has its own shift): ComponentsPerCascade is a multiple of four.
			if (count % 4 != 0 || cascades <= 0 || (count / cascades) % 4 != 0)
			{
				throw new InvalidOperationException("The spectrum's cascades must hold a multiple of four components each.");
			}
			if (!waveX.IsCreated || waveX.Length != lanes || shifts.Length != cascades)
			{
				Release();
				waveX = new NativeArray<float4>(lanes, Allocator.Persistent);
				waveZ = new NativeArray<float4>(lanes, Allocator.Persistent);
				unitX = new NativeArray<float4>(lanes, Allocator.Persistent);
				unitZ = new NativeArray<float4>(lanes, Allocator.Persistent);
				ax = new NativeArray<float4>(lanes, Allocator.Persistent);
				ay = new NativeArray<float4>(lanes, Allocator.Persistent);
				bx = new NativeArray<float4>(lanes, Allocator.Persistent);
				by = new NativeArray<float4>(lanes, Allocator.Persistent);
				omega = new NativeArray<float4>(lanes, Allocator.Persistent);
				hr = new NativeArray<float4>(lanes, Allocator.Persistent);
				hi = new NativeArray<float4>(lanes, Allocator.Persistent);
				shifts = new NativeArray<float>(cascades, Allocator.Persistent);
				result = new NativeArray<float>(3, Allocator.Persistent);
			}
			Pack(spectrum.WaveX, waveX);
			Pack(spectrum.WaveZ, waveZ);
			Pack(spectrum.UnitX, unitX);
			Pack(spectrum.UnitZ, unitZ);
			Pack(spectrum.AmplitudeAX, ax);
			Pack(spectrum.AmplitudeAY, ay);
			Pack(spectrum.AmplitudeBX, bx);
			Pack(spectrum.AmplitudeBY, by);
			Pack(spectrum.Frequency, omega);
			float[] cascadeShift = spectrum.CascadeShift;
			for (int c = 0; c < cascades; c++)
			{
				shifts[c] = cascadeShift[c];
			}
			lanesPerCascade = lanes / cascades;
			choppiness = spectrum.Choppiness;
			bound = spectrum;
			evaluatedAt = double.NaN;
		}

		private static void Pack(float[] source, NativeArray<float4> target)
		{
			for (int lane = 0; lane < target.Length; lane++)
			{
				int i = lane * 4;
				target[lane] = new float4(source[i], source[i + 1], source[i + 2], source[i + 3]);
			}
		}

		/// <summary>Moves every component to an instant (<see cref="WaterSpectrum.Evaluate"/>). Cached by the instant.</summary>
		public void Evaluate(double seconds)
		{
			if (bound == null || seconds == evaluatedAt)
			{
				return;
			}
			evaluatedAt = seconds;
			new EvaluateJob
			{
				// As the GPU has it: the time goes in as a float.
				Time = (float)seconds,
				AX = ax, AY = ay, BX = bx, BY = by, Omega = omega,
				HR = hr, HI = hi,
			}.Run();
		}

		/// <summary>The deep-water surface at an undisplaced point (<see cref="WaterSpectrum.Sample"/>).</summary>
		public void Sample(float x, float z, out float height, out float dx, out float dz)
		{
			if (bound == null)
			{
				height = dx = dz = 0f;
				return;
			}
			new SampleJob
			{
				X = x,
				Z = z,
				Choppiness = choppiness,
				LanesPerCascade = lanesPerCascade,
				WaveX = waveX, WaveZ = waveZ, UnitX = unitX, UnitZ = unitZ,
				HR = hr, HI = hi, Shifts = shifts,
				Result = result,
			}.Run();
			height = result[0];
			dx = result[1];
			dz = result[2];
		}

		public void Dispose()
		{
			Release();
			bound = null;
			evaluatedAt = double.NaN;
		}

		private void Release()
		{
			Free(ref waveX);
			Free(ref waveZ);
			Free(ref unitX);
			Free(ref unitZ);
			Free(ref ax);
			Free(ref ay);
			Free(ref bx);
			Free(ref by);
			Free(ref omega);
			Free(ref hr);
			Free(ref hi);
			if (shifts.IsCreated)
			{
				shifts.Dispose();
			}
			if (result.IsCreated)
			{
				result.Dispose();
			}
		}

		private static void Free(ref NativeArray<float4> array)
		{
			if (array.IsCreated)
			{
				array.Dispose();
			}
			array = default;
		}

		/// <summary>h(k, t) = h0·e^{-iωt} + conj(h0 mirrored)·e^{+iωt}, as TimeSpectrum forms it (WaterSpectrum.Evaluate).</summary>
		[BurstCompile(FloatPrecision.High, FloatMode.Default)]
		private struct EvaluateJob : IJob
		{
			public float Time;
			[ReadOnly] public NativeArray<float4> AX, AY, BX, BY, Omega;
			[WriteOnly] public NativeArray<float4> HR, HI;

			public void Execute()
			{
				for (int lane = 0; lane < Omega.Length; lane++)
				{
					// Backward, as TimeSpectrum runs it: the sign that sends the waves downwind.
					math.sincos(-Omega[lane] * Time, out float4 sin, out float4 cos);
					float4 ax = AX[lane], ay = AY[lane], bx = BX[lane], by = BY[lane];
					HR[lane] = ax * cos - ay * sin + bx * cos - by * sin;
					HI[lane] = ax * sin + ay * cos - bx * sin - by * cos;
				}
			}
		}

		/// <summary>Re(h·e^{ik·x}) summed for the height, and Re(-i·k̂·h·e^{ik·x}) for the sideways throw (WaterSpectrum.Sample).</summary>
		[BurstCompile(FloatPrecision.High, FloatMode.Default)]
		private struct SampleJob : IJob
		{
			public float X, Z, Choppiness;
			public int LanesPerCascade;
			[ReadOnly] public NativeArray<float4> WaveX, WaveZ, UnitX, UnitZ, HR, HI;
			[ReadOnly] public NativeArray<float> Shifts;
			[WriteOnly] public NativeArray<float> Result;

			public void Execute()
			{
				float4 sumHeight = float4.zero, sumX = float4.zero, sumZ = float4.zero;
				for (int c = 0, lane = 0; c < Shifts.Length; c++)
				{
					float xs = X - Shifts[c];
					float zs = Z - Shifts[c];
					for (int end = lane + LanesPerCascade; lane < end; lane++)
					{
						math.sincos(WaveX[lane] * xs + WaveZ[lane] * zs, out float4 sin, out float4 cos);
						float4 hr = HR[lane], hi = HI[lane];
						sumHeight += hr * cos - hi * sin;
						float4 across = hi * cos + hr * sin;
						sumX += UnitX[lane] * across;
						sumZ += UnitZ[lane] * across;
					}
				}
				Result[0] = math.csum(sumHeight);
				Result[1] = math.csum(sumX) * Choppiness;
				Result[2] = math.csum(sumZ) * Choppiness;
			}
		}
	}
}
