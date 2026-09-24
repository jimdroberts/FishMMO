using System;
using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// The sea's wave spectrum on the CPU: the same components the GPU transforms, so a height asked
	/// for here is the height that is drawn.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why not read the GPU back.</b> A readback is a frame or more late, costs a stall or a copy,
	/// does not exist on WebGL at all, and a server has no GPU to read. The field is a sum of known
	/// components, so the surface at one point can simply be summed where it is wanted, with the same
	/// seed, the same hash and the same quantised dispersion as <c>FishWaterFFT.compute</c>. The two
	/// must agree line for line: this is a port of InitialSpectrum and TimeSpectrum, and a change to
	/// either kernel has to be made here too.
	/// </para>
	/// <para>
	/// <b>Only the strongest components.</b> A wind sea's energy is concentrated around its spectral
	/// peak. Measured against an independent sum of the full 196,000-component field over 200 points
	/// and times, the 512 strongest of each cascade put the height within 6.5% RMS at 5 m/s (about
	/// four centimetres), 2.3% at 8 m/s and 0.6% at 12 m/s, and the sideways throw the same — about
	/// 1,500 terms and 30 µs a query, where the whole field would be 130 times that. Building it
	/// takes 20 to 30 ms, which is why <see cref="Build"/> is safe to run off the main thread.
	/// </para>
	/// </remarks>
	public sealed class WaterSpectrum
	{
		/// <summary>How many components of each cascade are kept, strongest first.</summary>
		public const int ComponentsPerCascade = 512;

		/// <summary>How many times the kept count is shortlisted on expected energy before choosing on the draw.</summary>
		private const int ShortlistFactor = 4;

		private const float Directionality = 4f;
		private const float AgainstTheWind = 0.12f;

		// Per component, all cascades end to end.
		private readonly float[] kx;
		private readonly float[] kz;
		private readonly float[] ax;
		private readonly float[] ay;
		private readonly float[] bx;
		private readonly float[] by;
		private readonly float[] omega;
		private readonly float[] shift;   // half a texel of the component's cascade, in metres
		private readonly float[] hr;      // h(k, t), real …
		private readonly float[] hi;      // … and imaginary, at the evaluated instant
		private readonly int count;
		private double evaluatedAt = double.NaN;

		/// <summary>The horizontal displacement scale the spectrum was built with.</summary>
		public float Choppiness { get; }

		/// <summary>The wind speed the spectrum was built for, in metres per second.</summary>
		public float Wind { get; }

		/// <summary>The heading the spectrum was built for, degrees toward, clockwise from north.</summary>
		public float HeadingDegrees { get; }

		/// <summary>The gravity the spectrum was built for.</summary>
		public float Gravity { get; }

		private WaterSpectrum(int capacity, float choppiness, float wind, float heading, float gravity)
		{
			kx = new float[capacity];
			kz = new float[capacity];
			ax = new float[capacity];
			ay = new float[capacity];
			bx = new float[capacity];
			by = new float[capacity];
			omega = new float[capacity];
			shift = new float[capacity];
			hr = new float[capacity];
			hi = new float[capacity];
			count = capacity;
			Choppiness = choppiness;
			Wind = wind;
			HeadingDegrees = heading;
			Gravity = gravity;
		}

		/// <summary>
		/// Builds the spectrum for a sea state. Pure arithmetic with no Unity objects, so it can run on
		/// a worker thread.
		/// </summary>
		/// <param name="headingDegrees">Which way the wind blows toward, clockwise from north.</param>
		public static WaterSpectrum Build(float wind, float headingDegrees, float gravity, float amplitude,
			float choppiness, float loopPeriod, uint seed, float[] patchMetres)
		{
			int cascades = patchMetres.Length;
			var spectrum = new WaterSpectrum(cascades * ComponentsPerCascade, choppiness, wind, headingDegrees, gravity);

			float radians = headingDegrees * Mathf.Deg2Rad;
			float windX = Mathf.Sin(radians);
			float windZ = Mathf.Cos(radians);
			float windSpeed = Mathf.Max(0.1f, wind);
			float g = Mathf.Max(0.05f, gravity);
			const int N = WaterFFT.Size;

			var energy = new float[N * N];
			var index = new int[N * N];
			int written = 0;
			for (int c = 0; c < cascades; c++)
			{
				float patch = patchMetres[c];
				float low = 2f * Mathf.PI / patch;
				float high = c == cascades - 1 ? 1e6f : 2f * Mathf.PI / patchMetres[c + 1];
				float small = patch / N * 2f;
				float deltaK = 2f * Mathf.PI / Mathf.Max(1f, patch);
				float cell = deltaK * deltaK;
				uint salt = seed + (uint)c * 7919u;

				// The energy of every texel, to pick the strongest.
				for (int y = 0; y < N; y++)
				{
					for (int x = 0; x < N; x++)
					{
						float wx = 2f * Mathf.PI * (x - N * 0.5f) / Mathf.Max(1f, patch);
						float wz = 2f * Mathf.PI * (y - N * 0.5f) / Mathf.Max(1f, patch);
						float p = Phillips(wx, wz, windSpeed, windX, windZ, g, amplitude, small, low, high)
							+ Phillips(-wx, -wz, windSpeed, windX, windZ, g, amplitude, small, low, high);
						energy[y * N + x] = p;
						index[y * N + x] = y * N + x;
					}
				}
				Array.Sort(energy, index);

				/* Chosen in two passes. The spectrum says what a component's energy is EXPECTED to
				 * be; its random amplitude then scatters that by a factor of several either way. So
				 * a shortlist is taken on the expectation — cheap, no random numbers — and the final
				 * choice made on what each one actually drew. On the expectation alone the error at
				 * 5 m/s was 7.7%; on the draw it is the figure quoted above. */
				int shortlist = Mathf.Min(N * N, ComponentsPerCascade * ShortlistFactor);
				var candidates = new (float drawn, int id, float ax, float ay, float bx, float by)[shortlist];
				for (int n = 0; n < shortlist; n++)
				{
					// Strongest last after the sort; take them from the end.
					int id = index[N * N - 1 - n];
					uint x = (uint)(id % N);
					uint y = (uint)(id / N);
					float wx = 2f * Mathf.PI * (x - N * 0.5f) / Mathf.Max(1f, patch);
					float wz = 2f * Mathf.PI * (y - N * 0.5f) / Mathf.Max(1f, patch);
					Gaussian(x, y, salt, out float g1x, out float g1y);
					Gaussian((uint)(N - x) & (N - 1), (uint)(N - y) & (N - 1), salt + 7919u, out float g2x, out float g2y);
					float h0 = Mathf.Sqrt(Mathf.Max(0f, Phillips(wx, wz, windSpeed, windX, windZ, g, amplitude, small, low, high)) * cell * 0.5f);
					float h0conj = Mathf.Sqrt(Mathf.Max(0f, Phillips(-wx, -wz, windSpeed, windX, windZ, g, amplitude, small, low, high)) * cell * 0.5f);
					float cax = g1x * h0, cay = g1y * h0, cbx = g2x * h0conj, cby = g2y * h0conj;
					candidates[n] = (cax * cax + cay * cay + cbx * cbx + cby * cby, id, cax, cay, cbx, cby);
				}
				Array.Sort(candidates, (a, b) => b.drawn.CompareTo(a.drawn));

				for (int n = 0; n < ComponentsPerCascade; n++)
				{
					int id = candidates[n].id;
					uint x = (uint)(id % N);
					uint y = (uint)(id / N);
					float wx = 2f * Mathf.PI * (x - N * 0.5f) / Mathf.Max(1f, patch);
					float wz = 2f * Mathf.PI * (y - N * 0.5f) / Mathf.Max(1f, patch);

					float magnitude = Mathf.Max(1e-4f, Mathf.Sqrt(wx * wx + wz * wz));
					float w = Mathf.Sqrt(g * magnitude);
					if (loopPeriod > 0f)
					{
						float quantum = 2f * Mathf.PI / loopPeriod;
						w = Mathf.Floor(w / quantum) * quantum;
					}

					spectrum.kx[written] = wx;
					spectrum.kz[written] = wz;
					spectrum.ax[written] = candidates[n].ax;
					spectrum.ay[written] = candidates[n].ay;
					spectrum.bx[written] = candidates[n].bx;
					spectrum.by[written] = candidates[n].by;
					spectrum.omega[written] = w;
					/* The shader samples each cascade at world / patch, so texel m is drawn at
					 * (m + 0.5) texels while the transform put it at m: the drawn sea is the
					 * transform shifted half a texel. A metre on the swell cascade. */
					spectrum.shift[written] = 0.5f * patch / N;
					written++;
				}
			}
			return spectrum;
		}

		/// <summary>Moves every component to an instant. Cached, so any number of queries in a frame cost one.</summary>
		public void Evaluate(double seconds)
		{
			if (seconds == evaluatedAt)
			{
				return;
			}
			evaluatedAt = seconds;
			// As the GPU has it: the time goes in as a float.
			float t = (float)seconds;
			for (int i = 0; i < count; i++)
			{
				float phase = omega[i] * t;
				float cos = Mathf.Cos(phase);
				float sin = Mathf.Sin(phase);
				// h = h0·e^{iωt} + conj(h0 mirrored)·e^{-iωt}, exactly as TimeSpectrum forms it.
				hr[i] = ax[i] * cos - ay[i] * sin + bx[i] * cos - by[i] * sin;
				hi[i] = ax[i] * sin + ay[i] * cos - bx[i] * sin - by[i] * cos;
			}
		}

		/// <summary>
		/// The deep-water surface at an undisplaced point: its height, and how far the water there is
		/// thrown sideways.
		/// </summary>
		public void Sample(float x, float z, out float height, out float dx, out float dz)
		{
			float sumHeight = 0f;
			float sumX = 0f;
			float sumZ = 0f;
			for (int i = 0; i < count; i++)
			{
				float arg = kx[i] * (x - shift[i]) + kz[i] * (z - shift[i]);
				float cos = Mathf.Cos(arg);
				float sin = Mathf.Sin(arg);
				// Re(h·e^{ik·x}), and Re(-i·k̂·h·e^{ik·x}) for the sideways throw.
				sumHeight += hr[i] * cos - hi[i] * sin;
				float across = hi[i] * cos + hr[i] * sin;
				float magnitude = Mathf.Max(1e-4f, Mathf.Sqrt(kx[i] * kx[i] + kz[i] * kz[i]));
				sumX += kx[i] / magnitude * across;
				sumZ += kz[i] / magnitude * across;
			}
			height = sumHeight;
			dx = sumX * Choppiness;
			dz = sumZ * Choppiness;
		}

		// ── The kernel's arithmetic ─────────────────────────────────────

		private static uint Hash(uint x)
		{
			unchecked
			{
				x ^= x >> 16;
				x *= 0x7feb352du;
				x ^= x >> 15;
				x *= 0x846ca68bu;
				x ^= x >> 16;
				return x;
			}
		}

		private static float Random(uint x, uint y, uint salt)
		{
			unchecked
			{
				uint h = Hash(x + Hash(y * 57u + salt * 131u));
				return (h & 0x00FFFFFFu) / 16777216f;
			}
		}

		private static void Gaussian(uint x, uint y, uint salt, out float a, out float b)
		{
			float u1 = Mathf.Max(1e-6f, Random(x, y, salt));
			float u2 = Random(x, y, salt + 977u);
			float r = Mathf.Sqrt(-2f * Mathf.Log(u1));
			float theta = 2f * Mathf.PI * u2;
			a = r * Mathf.Cos(theta);
			b = r * Mathf.Sin(theta);
		}

		private static float Phillips(float wx, float wz, float windSpeed, float windX, float windZ, float gravity,
			float amplitude, float small, float low, float high)
		{
			float k2 = wx * wx + wz * wz;
			if (k2 < 1e-9f)
			{
				return 0f;
			}
			float magnitude = Mathf.Sqrt(k2);
			if (magnitude < low || magnitude >= high)
			{
				return 0f;
			}
			float length = windSpeed * windSpeed / gravity;
			float along = (wx * windX + wz * windZ) / magnitude;
			float directional = Mathf.Pow(Mathf.Abs(along), Directionality);
			if (along < 0f)
			{
				directional *= AgainstTheWind;
			}
			float spectrum = amplitude * Mathf.Exp(-1f / (k2 * length * length)) / (k2 * k2) * directional;
			return spectrum * Mathf.Exp(-k2 * small * small);
		}
	}
}
