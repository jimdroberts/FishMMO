using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Water;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The sea's CPU spectrum (<see cref="WaterSpectrum"/>), its Burst sampler (<see cref="WaterSpectrumSampler"/>), and when
	/// the drawn spectrum is rebuilt (<see cref="WaterSurface.SpectrumMoved"/>): the heights floaters and the underwater
	/// pass are drawn by must not move when the way they are summed does.
	/// </summary>
	public class WaterSpectrumTests
	{
		private static readonly float[] Patches = { 520f, 118f, 27f };

		private static WaterSpectrum Build(float wind = 8f, float heading = 45f)
			=> WaterSpectrum.Build(wind, heading, 9.81f, 8.2e-3f, 1.2f, 120f, 1u, (float[])Patches.Clone());

		private static float[] Field(WaterSpectrum spectrum, string name)
			=> (float[])typeof(WaterSpectrum).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(spectrum);

		/// <summary>
		/// The sum as it was written before the unit wave directions and the half-texel shift were worked out once: each
		/// component's own shift and its own division, per query.
		/// </summary>
		private static void ReferenceSample(WaterSpectrum spectrum, float x, float z, out float height, out float dx, out float dz)
		{
			float[] kx = Field(spectrum, "kx"), kz = Field(spectrum, "kz"), hr = Field(spectrum, "hr"), hi = Field(spectrum, "hi");
			float sumHeight = 0f, sumX = 0f, sumZ = 0f;
			for (int i = 0; i < kx.Length; i++)
			{
				float shift = 0.5f * Patches[i / WaterSpectrum.ComponentsPerCascade] / WaterFFT.Size;
				float arg = kx[i] * (x - shift) + kz[i] * (z - shift);
				float cos = Mathf.Cos(arg);
				float sin = Mathf.Sin(arg);
				sumHeight += hr[i] * cos - hi[i] * sin;
				float across = hi[i] * cos + hr[i] * sin;
				float magnitude = Mathf.Max(1e-4f, Mathf.Sqrt(kx[i] * kx[i] + kz[i] * kz[i]));
				sumX += kx[i] / magnitude * across;
				sumZ += kz[i] / magnitude * across;
			}
			height = sumHeight;
			dx = sumX * spectrum.Choppiness;
			dz = sumZ * spectrum.Choppiness;
		}

		private static Vector2 Point(int n) => new Vector2(
			Mathf.Sin(n * 12.9898f) * 3000f, Mathf.Cos(n * 78.233f) * 3000f);

		[Test]
		public void TheManagedSumIsTheSumItWas()
		{
			WaterSpectrum spectrum = Build();
			double[] instants = { 0.0, 37.25, 119.9 };
			int compared = 0;
			foreach (double t in instants)
			{
				spectrum.Evaluate(t);
				for (int n = 0; n < 40; n++)
				{
					Vector2 p = Point(n);
					spectrum.Sample(p.x, p.y, out float h, out float dx, out float dz);
					ReferenceSample(spectrum, p.x, p.y, out float rh, out float rdx, out float rdz);
					// The same arithmetic in the same order: equal but for how the runtime holds a float between two steps.
					Assert.AreEqual(rh, h, 1e-4f, $"height at {p}, t {t}");
					Assert.AreEqual(rdx, dx, 1e-4f, $"throw x at {p}, t {t}");
					Assert.AreEqual(rdz, dz, 1e-4f, $"throw z at {p}, t {t}");
					compared++;
				}
			}
			// The control: a sea with waves on it, or equal sums prove nothing.
			spectrum.Evaluate(37.25);
			spectrum.Sample(10f, 20f, out float some, out _, out _);
			spectrum.Sample(60f, -40f, out float other, out _, out _);
			Assert.Greater(Mathf.Abs(some - other), 1e-3f, "the sea must have waves for the comparison to mean anything");
			Assert.AreEqual(instants.Length * 40, compared);
		}

		/// <summary>Native (Burst, NativeArray): the sampler sums the same sea to within float rounding.</summary>
		[Test]
		public void TheBurstSamplerSumsTheSameSea()
		{
			WaterSpectrum spectrum = Build(11f, 200f);
			var sampler = new WaterSpectrumSampler();
			try
			{
				sampler.Bind(spectrum);
				float worst = 0f, tallest = 0f;
				foreach (double t in new[] { 3.5, 71.0 })
				{
					spectrum.Evaluate(t);
					sampler.Evaluate(t);
					for (int n = 0; n < 60; n++)
					{
						Vector2 p = Point(n);
						spectrum.Sample(p.x, p.y, out float h, out float dx, out float dz);
						sampler.Sample(p.x, p.y, out float bh, out float bdx, out float bdz);
						worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(h - bh), Mathf.Max(Mathf.Abs(dx - bdx), Mathf.Abs(dz - bdz))));
						tallest = Mathf.Max(tallest, Mathf.Abs(h));
					}
				}
				Assert.Greater(tallest, 0.2f, "the control: an 11 m/s sea stands well over 20 cm somewhere");
				// A millimetre on a sea a metre or more high: the rounding of a few thousand float terms, nothing a floater shows.
				Assert.Less(worst, 1e-3f, "the Burst sum strays from the managed one");
			}
			finally
			{
				sampler.Dispose();
			}
		}

		[Test]
		public void TheDrawnSpectrumIsRebuiltOnlyWhenTheWindHasMovedPerceptibly()
		{
			Assert.IsTrue(WaterSurface.SpectrumMoved(-1f, 8f, 0f, 45f), "never built");
			Assert.IsFalse(WaterSurface.SpectrumMoved(8f, 8f, 45f, 45f), "the same wind");
			// The weather's easing: a hair a frame, which rebuilt the three 256² dispatches every frame.
			Assert.IsFalse(WaterSurface.SpectrumMoved(8f, 8f * (1f + 1e-4f), 45f, 45.01f), "a hair of wind and turn");
			Assert.IsTrue(WaterSurface.SpectrumMoved(8f, 8f * (1f + 1e-3f), 45f, 45f), "a tenth of a percent of wind");
			Assert.IsTrue(WaterSurface.SpectrumMoved(8f, 8f, 45f, 45.2f), "a fifth of a degree");
			Assert.IsTrue(WaterSurface.SpectrumMoved(8f, 8f, 359.98f, 0.1f), "a turn across north, the short way");
			Assert.IsFalse(WaterSurface.SpectrumMoved(8f, 8f, 359.99f, 0.01f), "a hair across north");
			// Accumulated drift is caught: the test is against the wind it was BUILT for, not last frame's.
			float built = 8f, wind = 8f;
			int frames = 0;
			while (!WaterSurface.SpectrumMoved(built, wind, 45f, 45f) && frames < 100000)
			{
				wind *= 1f + 2e-5f;
				frames++;
			}
			Assert.Less(frames, 100000, "a slow drift must rebuild in the end");
			Assert.LessOrEqual(Mathf.Abs(wind - built) / built, WaterSurface.SpectrumWindShare * 1.1f, "and no later than the share allows");
		}
	}
}
