using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The building blocks every synthesised sound is made from: seeded noise, filters, envelopes and the seamless
	/// loop. There are no audio files in the project; each sound is a recipe over these, run from a fixed seed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same seed makes the same sound on every machine.</b> Everything here is plain managed arithmetic in
	/// single precision, run in one order, with no Unity call inside: the same recipe gives the same samples on every
	/// client, and it can run off the main thread (<see cref="SoundLibrary"/>). Only <see cref="ToClip"/> touches Unity.
	/// </para>
	/// <para>
	/// <b>Filters are the RBJ cookbook biquads</b> (Bristow-Johnson's Audio EQ Cookbook), direct form I in float.
	/// </para>
	/// </remarks>
	public static class SoundSynth
	{
		/// <summary>The sample rate every loop and footstep is made at.</summary>
		public const int Rate = 44100;

		/// <summary>The speed of sound in air near the ground, metres a second (15 °C).</summary>
		public const float SpeedOfSound = 343f;

		/// <summary>A seeded xorshift generator: the only source of randomness any sound uses.</summary>
		public struct Noise
		{
			private uint state;

			public Noise(uint seed)
			{
				state = seed == 0u ? 0x9E3779B9u : seed;
				// A few turns so seeds that differ in one bit start far apart.
				Next(); Next(); Next();
			}

			/// <summary>The next number in [0, 1).</summary>
			public float Next()
			{
				state ^= state << 13;
				state ^= state >> 17;
				state ^= state << 5;
				return (state & 0xFFFFFFu) / 16777216f;
			}

			/// <summary>The next number in [-1, 1).</summary>
			public float Bipolar() => Next() * 2f - 1f;

			/// <summary>A number in [min, max).</summary>
			public float Range(float min, float max) => min + (max - min) * Next();

			/// <summary>A number spread evenly on a log scale between two positive bounds (pitches, sizes).</summary>
			public float LogRange(float min, float max) => min * MathF.Pow(max / min, Next());

			/// <summary>Seconds to the next event of a process happening <paramref name="perSecond"/> times a second on average.</summary>
			public float Poisson(float perSecond) => -MathF.Log(1f - Next() * 0.999999f) / MathF.Max(1e-6f, perSecond);

			/// <summary>A roughly normal number, mean 0 and spread 1 (the sum of four uniforms).</summary>
			public float Gauss() => (Next() + Next() + Next() + Next() - 2f) * 1.7320508f;
		}

		/// <summary>Paul Kellett's refined pink-noise filter: white noise in, -3 dB an octave out.</summary>
		public struct Pink
		{
			private float b0, b1, b2, b3, b4, b5, b6;

			public float Process(float white)
			{
				b0 = 0.99886f * b0 + white * 0.0555179f;
				b1 = 0.99332f * b1 + white * 0.0750759f;
				b2 = 0.96900f * b2 + white * 0.1538520f;
				b3 = 0.86650f * b3 + white * 0.3104856f;
				b4 = 0.55000f * b4 + white * 0.5329522f;
				b5 = -0.7616f * b5 - white * 0.0168980f;
				float pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362f;
				b6 = white * 0.115926f;
				return pink * 0.11f;
			}
		}

		/// <summary>Brown noise: white noise integrated with a slow leak, -6 dB an octave.</summary>
		public struct Brown
		{
			private float value;

			public float Process(float white)
			{
				value = (value + 0.02f * white) * 0.998f;
				return value * 3.5f;
			}
		}

		/// <summary>A one-pole low-pass, for smoothing envelopes and gentle tilts.</summary>
		public struct OnePole
		{
			private float a, z;

			public static OnePole LowPass(float cutoff, int rate)
			{
				return new OnePole { a = 1f - MathF.Exp(-2f * MathF.PI * cutoff / rate) };
			}

			public float Process(float x)
			{
				z += a * (x - z);
				return z;
			}

			public float Value => z;
		}

		/// <summary>A biquad filter from the RBJ cookbook.</summary>
		public struct Biquad
		{
			private float b0, b1, b2, a1, a2;
			private float x1, x2, y1, y2;

			public static Biquad LowPass(float frequency, float q, int rate)
			{
				Coefficients(frequency, q, rate, out float cos, out float alpha);
				float a0 = 1f + alpha;
				return Make((1f - cos) * 0.5f, 1f - cos, (1f - cos) * 0.5f, a0, -2f * cos, 1f - alpha);
			}

			public static Biquad HighPass(float frequency, float q, int rate)
			{
				Coefficients(frequency, q, rate, out float cos, out float alpha);
				float a0 = 1f + alpha;
				return Make((1f + cos) * 0.5f, -(1f + cos), (1f + cos) * 0.5f, a0, -2f * cos, 1f - alpha);
			}

			/// <summary>A band-pass with 0 dB at its centre: Q is the centre over the bandwidth.</summary>
			public static Biquad BandPass(float frequency, float q, int rate)
			{
				Coefficients(frequency, q, rate, out float cos, out float alpha);
				float a0 = 1f + alpha;
				return Make(alpha, 0f, -alpha, a0, -2f * cos, 1f - alpha);
			}

			/// <summary>Moves the filter to a new frequency (a sweep) without losing its state.</summary>
			public void RetuneBandPass(float frequency, float q, int rate)
			{
				Biquad tuned = BandPass(frequency, q, rate);
				b0 = tuned.b0; b1 = tuned.b1; b2 = tuned.b2; a1 = tuned.a1; a2 = tuned.a2;
			}

			public void RetuneLowPass(float frequency, float q, int rate)
			{
				Biquad tuned = LowPass(frequency, q, rate);
				b0 = tuned.b0; b1 = tuned.b1; b2 = tuned.b2; a1 = tuned.a1; a2 = tuned.a2;
			}

			public float Process(float x)
			{
				float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
				x2 = x1;
				x1 = x;
				y2 = y1;
				// Denormals cost a hundred times a normal multiply on some CPUs; a decayed tail is flushed to zero.
				y1 = MathF.Abs(y) < 1e-20f ? 0f : y;
				return y1;
			}

			private static void Coefficients(float frequency, float q, int rate, out float cos, out float alpha)
			{
				float f = MathF.Min(MathF.Max(frequency, 5f), rate * 0.49f);
				float w = 2f * MathF.PI * f / rate;
				cos = MathF.Cos(w);
				alpha = MathF.Sin(w) / (2f * MathF.Max(0.05f, q));
			}

			private static Biquad Make(float b0, float b1, float b2, float a0, float a1, float a2)
			{
				return new Biquad { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };
			}
		}

		/// <summary>
		/// A damped sine added into <paramref name="into"/> at <paramref name="start"/>: the ring of a struck body
		/// (a pebble, a board, a drop on a leaf). Decays by 60 dB over <paramref name="ringSeconds"/>.
		/// </summary>
		public static void AddRing(float[] into, int start, float frequency, float amplitude, float ringSeconds, int rate, float phase = 0f)
		{
			int length = Math.Min(into.Length - start, (int)(ringSeconds * rate));
			if (length <= 0 || start < 0)
			{
				return;
			}
			float decay = MathF.Exp(-6.9f / MathF.Max(1, ringSeconds * rate));
			float w = 2f * MathF.PI * frequency / rate;
			// A rotating phasor rather than a sine per sample: the same ring, a tenth of the cost.
			float c = MathF.Cos(w), s = MathF.Sin(w);
			float re = MathF.Cos(phase), im = MathF.Sin(phase), env = amplitude;
			for (int i = 0; i < length; i++)
			{
				into[start + i] += im * env;
				float nr = re * c - im * s;
				im = re * s + im * c;
				re = nr;
				env *= decay;
			}
		}

		/// <summary>
		/// A drop's or a bubble's "plink": a damped sine whose pitch rises as it rings, as a bubble's does when it
		/// shrinks at the surface (Minnaert resonance; the rise is what makes a drop on water sound wet).
		/// </summary>
		public static void AddChirp(float[] into, int start, float frequency, float rise, float amplitude, float ringSeconds, int rate)
		{
			int length = Math.Min(into.Length - start, (int)(ringSeconds * rate));
			if (length <= 0 || start < 0)
			{
				return;
			}
			float decay = MathF.Exp(-6.9f / MathF.Max(1, ringSeconds * rate));
			float phase = 0f, env = amplitude;
			// In over a millisecond: a sine switched on at full is a click across the whole spectrum.
			int attack = Math.Max(1, rate / 1000);
			for (int i = 0; i < length; i++)
			{
				float t = (float)i / length;
				phase += 2f * MathF.PI * frequency * (1f + rise * t) / rate;
				float rise01 = i < attack ? 0.5f - 0.5f * MathF.Cos(MathF.PI * i / attack) : 1f;
				into[start + i] += MathF.Sin(phase) * env * rise01;
				env *= decay;
			}
		}

		/// <summary>
		/// A short burst of filtered noise added into <paramref name="into"/>: an impact's click, a grain's crunch.
		/// The envelope is a fast attack and an exponential fall over <paramref name="seconds"/>.
		/// </summary>
		public static void AddBurst(float[] into, int start, ref Noise noise, float centre, float q, float amplitude, float seconds, int rate, float attackSeconds = 0.0005f)
		{
			int length = Math.Min(into.Length - start, (int)(seconds * rate));
			if (length <= 0 || start < 0)
			{
				return;
			}
			Biquad band = Biquad.BandPass(centre, q, rate);
			int attack = Math.Max(1, (int)(attackSeconds * rate));
			float decay = MathF.Exp(-6.9f / MathF.Max(1, length));
			float env = amplitude;
			for (int i = 0; i < length; i++)
			{
				float rise = i < attack ? (float)i / attack : 1f;
				into[start + i] += band.Process(noise.Bipolar()) * env * rise;
				env *= decay;
			}
		}

		/// <summary>
		/// A seamless loop of <paramref name="length"/> samples from <paramref name="raw"/>, which holds at least
		/// <paramref name="length"/> + <paramref name="fade"/>: the last <paramref name="fade"/> samples made are
		/// cross-faded into the first, equal power because the two are unrelated noise, so the sample after the end
		/// continues the one before it.
		/// </summary>
		public static float[] Loop(float[] raw, int length, int fade)
		{
			var data = new float[length];
			Array.Copy(raw, data, length);
			for (int i = 0; i < fade && length + i < raw.Length; i++)
			{
				float a = (float)i / fade * (MathF.PI * 0.5f);
				data[i] = raw[i] * MathF.Sin(a) + raw[length + i] * MathF.Cos(a);
			}
			return data;
		}

		/// <summary>Removes any constant offset, which only wastes headroom and thumps when a sound starts.</summary>
		public static void RemoveDc(float[] data)
		{
			float dcIn = 0f, dcOut = 0f;
			for (int i = 0; i < data.Length; i++)
			{
				float x = data[i];
				dcOut = x - dcIn + 0.9985f * dcOut;
				dcIn = x;
				data[i] = dcOut;
			}
		}

		/// <summary>Scales a sound so its loudest sample is <paramref name="peak"/>.</summary>
		public static void Normalize(float[] data, float peak)
		{
			float most = 1e-9f;
			for (int i = 0; i < data.Length; i++)
			{
				most = MathF.Max(most, MathF.Abs(data[i]));
			}
			float gain = peak / most;
			for (int i = 0; i < data.Length; i++)
			{
				data[i] *= gain;
			}
		}

		/// <summary>Scales a sound so its RMS is <paramref name="rms"/>, then softly limits any peak above 0.98.</summary>
		public static void NormalizeRms(float[] data, float rms)
		{
			double sum = 0.0;
			for (int i = 0; i < data.Length; i++)
			{
				sum += data[i] * data[i];
			}
			float now = (float)Math.Sqrt(sum / Math.Max(1, data.Length));
			float gain = rms / MathF.Max(1e-9f, now);
			for (int i = 0; i < data.Length; i++)
			{
				data[i] = SoftClip(data[i] * gain);
			}
		}

		/// <summary>
		/// Scales a sound by its loudest stretch rather than its loudest sample: the loudest <paramref name="windowSeconds"/>
		/// is brought to <paramref name="rms"/>. A sound whose energy is a long roll behind one sharp crack (thunder) would
		/// otherwise be set by the crack, a millisecond long, and the roll left at a hundredth of full level.
		/// Follow with <see cref="Limit"/>: the crack is now above full scale.
		/// </summary>
		public static void NormalizeLoudest(float[] data, int rate, float windowSeconds, float rms)
		{
			int window = Math.Max(1, (int)(windowSeconds * rate));
			double sum = 0.0, loudest = 0.0;
			for (int i = 0; i < data.Length; i++)
			{
				sum += data[i] * data[i];
				if (i >= window)
				{
					sum -= data[i - window] * data[i - window];
				}
				if (i >= window - 1)
				{
					loudest = Math.Max(loudest, sum / window);
				}
			}
			if (data.Length < window)
			{
				loudest = sum / Math.Max(1, data.Length);
			}
			float gain = rms / (float)Math.Sqrt(Math.Max(loudest, 1e-18));
			for (int i = 0; i < data.Length; i++)
			{
				data[i] *= gain;
			}
		}

		/// <summary>
		/// A peak limiter: the gain falls at once to hold every sample within <paramref name="ceiling"/>, and recovers over
		/// <paramref name="releaseSeconds"/>. A crack is held to full scale without the fizz of clipping it.
		/// </summary>
		public static void Limit(float[] data, int rate, float ceiling, float releaseSeconds)
		{
			float release = 1f - MathF.Exp(-1f / MathF.Max(1f, releaseSeconds * rate));
			float envelope = ceiling;
			for (int i = 0; i < data.Length; i++)
			{
				float level = MathF.Abs(data[i]);
				// Instant attack: the envelope is never below the sample, so the gain never lets one through.
				envelope = level > envelope ? level : envelope + (MathF.Max(level, ceiling) - envelope) * release;
				data[i] *= ceiling / MathF.Max(envelope, ceiling);
			}
		}

		/// <summary>Linear below 0.7, easing into ±1 above: keeps a rare loud peak from clipping hard.</summary>
		public static float SoftClip(float x)
		{
			float a = MathF.Abs(x);
			if (a <= 0.7f)
			{
				return x;
			}
			float over = (a - 0.7f) / 0.3f;
			return MathF.Sign(x) * (0.7f + 0.3f * MathF.Tanh(over));
		}

		/// <summary>Short linear fades at both ends, so a one-shot never starts or stops on a click.</summary>
		public static void FadeEnds(float[] data, int inSamples, int outSamples)
		{
			int n = data.Length;
			for (int i = 0; i < inSamples && i < n; i++)
			{
				data[i] *= (float)i / inSamples;
			}
			for (int i = 0; i < outSamples && i < n; i++)
			{
				data[n - 1 - i] *= (float)i / outSamples;
			}
		}

		/// <summary>
		/// A slow, seamless swell for a loop of <paramref name="length"/> samples: a sum of sines each fitting the loop
		/// a whole number of times, mean 1, swinging by about <paramref name="depth"/>.
		/// </summary>
		public static float Swell(int i, int length, float depth, int cyclesA, int cyclesB, int cyclesC, float phaseA = 0f, float phaseB = 1.3f, float phaseC = 2.9f)
		{
			float t = 2f * MathF.PI * i / length;
			float s = 0.55f * MathF.Sin(t * cyclesA + phaseA) + 0.3f * MathF.Sin(t * cyclesB + phaseB) + 0.15f * MathF.Sin(t * cyclesC + phaseC);
			return MathF.Max(0f, 1f + depth * s);
		}

		/// <summary>
		/// An in-place iterative radix-2 FFT (Cooley–Tukey) of <paramref name="re"/> + i·<paramref name="im"/>, whose
		/// length must be a power of two. The inverse is unscaled.
		/// </summary>
		public static void Fft(float[] re, float[] im, bool inverse)
		{
			int n = re.Length;
			for (int i = 1, j = 0; i < n; i++)
			{
				int bit = n >> 1;
				for (; (j & bit) != 0; bit >>= 1)
				{
					j ^= bit;
				}
				j ^= bit;
				if (i < j)
				{
					(re[i], re[j]) = (re[j], re[i]);
					(im[i], im[j]) = (im[j], im[i]);
				}
			}
			for (int size = 2; size <= n; size <<= 1)
			{
				double angle = (inverse ? 2.0 : -2.0) * Math.PI / size;
				float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);
				int half = size >> 1;
				for (int start = 0; start < n; start += size)
				{
					float cr = 1f, ci = 0f;
					for (int k = 0; k < half; k++)
					{
						int a = start + k, b = a + half;
						float tr = re[b] * cr - im[b] * ci;
						float ti = re[b] * ci + im[b] * cr;
						re[b] = re[a] - tr;
						im[b] = im[a] - ti;
						re[a] += tr;
						im[a] += ti;
						float nr = cr * wr - ci * wi;
						ci = cr * wi + ci * wr;
						cr = nr;
					}
				}
			}
		}

		/// <summary>
		/// Periodic noise of <paramref name="n"/> samples (a power of two) whose amplitude spectrum is
		/// <paramref name="amplitude"/>(Hz), made in the frequency domain: Gaussian bins (so the result is true noise, not
		/// a sum of steady tones) at random phases, transformed back. Periodic, so it loops with no seam at all; scaled to
		/// unit RMS.
		/// </summary>
		public static float[] SpectralNoise(int n, int rate, Func<float, float> amplitude, ref Noise noise)
		{
			var re = new float[n];
			var im = new float[n];
			for (int k = 1; k < n / 2; k++)
			{
				float a = amplitude((float)k * rate / n);
				float g1 = noise.Gauss(), g2 = noise.Gauss();
				re[k] = a * g1;
				im[k] = a * g2;
				// Hermitian, so the result is real.
				re[n - k] = re[k];
				im[n - k] = -im[k];
			}
			Fft(re, im, true);
			double sum = 0.0;
			for (int i = 0; i < n; i++)
			{
				sum += re[i] * re[i];
			}
			float scale = 1f / MathF.Sqrt((float)(sum / n) + 1e-30f);
			for (int i = 0; i < n; i++)
			{
				re[i] *= scale;
			}
			return re;
		}

		/// <summary>
		/// The amplitude spectrum, per hertz, of a sound whose energy in each octave is <paramref name="octaveDb"/> (dB
		/// against the whole) at the centres <paramref name="centres"/>: the dB joined linearly in log frequency between
		/// the centres and held beyond the ends, the energy per octave spread over the octave's width (∝ f).
		/// </summary>
		public static float OctaveAmplitude(float hz, float[] centres, float[] octaveDb)
		{
			if (hz <= 0f)
			{
				return 0f;
			}
			float db;
			if (hz <= centres[0])
			{
				db = octaveDb[0];
			}
			else if (hz >= centres[centres.Length - 1])
			{
				db = octaveDb[centres.Length - 1];
			}
			else
			{
				int b = 0;
				while (b + 1 < centres.Length && centres[b + 1] < hz)
				{
					b++;
				}
				float t = MathF.Log(hz / centres[b]) / MathF.Log(centres[b + 1] / centres[b]);
				db = octaveDb[b] + (octaveDb[b + 1] - octaveDb[b]) * t;
			}
			float amplitude = MathF.Pow(10f, db / 20f) / MathF.Sqrt(hz);
			// Rolled off past the measured octaves, 12 dB an octave: held flat below the lowest, the per-hertz amplitude
			// grew without bound toward nought, and a strong wind measured right octave by octave had its centre at 70 Hz.
			float low = centres[0] / 1.41f, high = centres[centres.Length - 1] * 1.2f;
			if (hz < low)
			{
				amplitude *= (hz / low) * (hz / low);
			}
			else if (hz > high)
			{
				amplitude *= (high / hz) * (high / hz);
			}
			return amplitude;
		}

		/// <summary>A 32-bit mix of two numbers, for seeds that must not collide.</summary>
		public static uint Hash(uint a, uint b)
		{
			unchecked
			{
				uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
				h ^= h >> 15;
				h *= 0x2C1B3C6Du;
				h ^= h >> 12;
				h *= 0x297A2D39u;
				h ^= h >> 15;
				return h;
			}
		}

		/// <summary>Makes a mono clip of the samples. The only call here that has to run on the main thread.</summary>
		public static AudioClip ToClip(string name, float[] data, int rate)
		{
			AudioClip clip = AudioClip.Create(name, Math.Max(1, data.Length), 1, rate, false);
			clip.hideFlags = HideFlags.DontSave;
			clip.SetData(data, 0);
			return clip;
		}
	}
}
