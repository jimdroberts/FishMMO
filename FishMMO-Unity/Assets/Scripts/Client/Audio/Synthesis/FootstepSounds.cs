using System;
using static FishMMO.Client.SoundSynth;

namespace FishMMO.Client
{
	/// <summary>
	/// The footfall sounds, synthesised: a foot on each kind of ground, a landing, a swimmer's stroke and plunge, a hand on
	/// a rung, and a stride through tall grass. Each is a recipe over <see cref="SoundSynth"/> run from a seed, so a
	/// variant is the same sound on every client.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Fitted to recordings (2026-10-19).</b> Every ground is a <see cref="Surface"/> measured from real footsteps on
	/// it (OpenGameArt's CC0/CC-BY foley and Wikimedia Commons field recordings): its octave spectrum from 63 Hz to 16 kHz,
	/// how long the step takes to build (the weight rolling from heel to ball: tens of milliseconds on stone, a tenth of a
	/// second and more in snow or gravel), how long it lasts to 35 dB down, and how many small transients it holds — the
	/// pebbles knocking, the grit, the leaves breaking. The first recipes were each a click over a heavy low thump, a tenth
	/// of a second long: a step on grass or sand has almost nothing below 1 kHz, and lasts three or four tenths.
	/// </para>
	/// <para>
	/// <b>How a step is made.</b> A texture — noise through an octave filter bank weighted to the ground's spectrum, under
	/// an envelope that rises over the attack and decays to −35 dB at the step's length — and grains scattered through
	/// it in time with the envelope: bursts (grit, litter), rings (pebbles, ice), chirps (mud's bubbles, water's drops).
	/// Hard ground adds the impact of the heel; boards their bending modes.
	/// </para>
	/// </remarks>
	public static class FootstepSounds
	{
		/// <summary>How many variants of a step there are on each ground.</summary>
		public const int StepVariants = 6;

		/// <summary>How many variants of a landing, a splash, a rung and a rustle.</summary>
		public const int OtherVariants = 3;

		/// <summary>The key a sound is kept under (<see cref="SoundLibrary"/>): bump the version when a recipe changes.</summary>
		public static string StepKey(SurfaceKind kind, bool landing, int variant) => $"Footstep.v2.{kind}.{(landing ? "Land" : "Step")}.{variant}";

		/// <summary>The octave centres the spectra are given at, Hz.</summary>
		public static readonly float[] Octaves = { 63f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };

		/// <summary>The grains' loudness against the texture's: set so a step holds as many audible transients as the recordings'.</summary>
		public const float GrainScale = 4f;

		/// <summary>What a grain is.</summary>
		public enum Grain
		{
			/// <summary>A millisecond or few of band-passed noise: grit, litter, a crystal breaking.</summary>
			Burst,
			/// <summary>A short damped ring: a pebble knocked, ice ticking.</summary>
			Ring,
			/// <summary>A rising "plink": a bubble at the surface, a drop falling back.</summary>
			Chirp,
		}

		/// <summary>One ground, as measured.</summary>
		public sealed class Surface
		{
			/// <summary>The energy in each octave (<see cref="Octaves"/>), dB against the whole step.</summary>
			public float[] Spectrum;
			/// <summary>Seconds from the first sound to the loudest moment.</summary>
			public float Attack;
			/// <summary>Seconds from the first sound until 35 dB down.</summary>
			public float Length;
			/// <summary>Seconds from the first sound until 20 dB down: how long the crunch holds near its peak.</summary>
			public float Sustain;
			/// <summary>Band limits on the whole step, Hz (0 for none): sand has nothing below a kilohertz, mud nothing at the very top.</summary>
			public float HighPass, LowPass;
			/// <summary>How many grains a step holds, on average.</summary>
			public float Grains;
			public Grain GrainKind;
			/// <summary>The grains' pitch range, Hz.</summary>
			public float GrainLow, GrainHigh;
			/// <summary>The grains' loudness against the texture's peak.</summary>
			public float GrainLevel;
			/// <summary>A burst grain's length, seconds, and its band's Q: a short wide burst is a click, a longer narrow one a grain's hiss.</summary>
			public float GrainSeconds = 0.0025f, GrainQ = 1.5f;
			/// <summary>The heel's impact, against the texture's peak (0 on soft ground).</summary>
			public float Impact;
			/// <summary>The impact's thump pitch, Hz.</summary>
			public float ImpactPitch;
			/// <summary>The centre of the impact's knock, Hz: low for boards, higher for stone and ice.</summary>
			public float ImpactBand = 900f;
			/// <summary>Boards: the fundamental of the bending modes rung by the impact (0 for none).</summary>
			public float Modes;
		}

		/// <summary>
		/// Every ground's measurements. Medians over the reference steps (grass 2, leaves 2, earth 10, mud 26, sand 2,
		/// gravel 2, stone 11, boards 9, snow 50, snow over ice 24, water 4); the metal is from hits on metal, there being
		/// no free recording of a step on a plate, and is the least certain.
		/// </summary>
		public static Surface Of(SurfaceKind kind)
		{
			switch (kind)
			{
				case SurfaceKind.Grass:
					return new Surface { Spectrum = new[] { -18.6f, -17.8f, -14.4f, -15.4f, -13.9f, -13.9f, -19.5f, -24.0f, -33.7f }, Attack = 0.064f, Length = 0.46f,
						Grains = 8f, GrainKind = Grain.Burst, GrainLow = 3000f, GrainHigh = 9000f, GrainLevel = 1.0f, Sustain = 0.4f };
				case SurfaceKind.Leaves:
					return new Surface { Spectrum = new[] { -29.7f, -19.7f, -14.6f, -14.3f, -11.3f, -10.1f, -9.9f, -16.4f, -28.1f }, Attack = 0.13f, Length = 0.42f,
						Grains = 30f, GrainKind = Grain.Burst, GrainLow = 2000f, GrainHigh = 9000f, GrainLevel = 1.2f, Sustain = 0.347f };
				case SurfaceKind.Dirt:
					return new Surface { Spectrum = new[] { -19.3f, -17.8f, -15.2f, -10.5f, -6.3f, -6.8f, -15.0f, -15.2f, -49.6f }, Attack = 0.07f, Length = 0.37f,
						Grains = 7f, GrainKind = Grain.Burst, GrainLow = 800f, GrainHigh = 4000f, GrainLevel = 1.0f, Impact = 0.25f, ImpactPitch = 110f, ImpactBand = 400f, Sustain = 0.306f };
				case SurfaceKind.Mud:
					return new Surface { Spectrum = new[] { -57.0f, -24.0f, -23.9f, -35.2f, -35.7f, -11.7f, -7.0f, -7.1f, -56.8f }, Attack = 0.062f, Length = 0.3f,
						Grains = 8f, GrainKind = Grain.Chirp, GrainLow = 300f, GrainHigh = 1600f, GrainLevel = 0.25f, Sustain = 0.172f, LowPass = 10000f };
				case SurfaceKind.Sand:
					return new Surface { Spectrum = new[] { -30.4f, -38.4f, -41.0f, -36.7f, -53.6f, -1.3f, 1.4f, -44.7f, -28.0f }, Attack = 0.095f, Length = 0.31f,
						Grains = 12f, GrainKind = Grain.Burst, GrainLow = 2000f, GrainHigh = 5000f, GrainLevel = 0.8f, GrainSeconds = 0.008f, GrainQ = 3f, Sustain = 0.306f, HighPass = 1200f, LowPass = 7000f };
				case SurfaceKind.Gravel:
					return new Surface { Spectrum = new[] { -63.1f, -10.9f, -1.0f, 2.9f, 2.7f, -34.5f, -37.6f, -36.3f, -5.4f }, Attack = 0.1f, Length = 0.49f,
						Grains = 18f, GrainKind = Grain.Ring, GrainLow = 1500f, GrainHigh = 12000f, GrainLevel = 0.7f, Sustain = 0.402f, HighPass = 90f };
				case SurfaceKind.Stone:
					return new Surface { Spectrum = new[] { -20.7f, -23.4f, -22.8f, -19.1f, -18.6f, -22.7f, -38.5f, -44.4f, -47.8f }, Attack = 0.009f, Length = 0.26f,
						Grains = 2f, GrainKind = Grain.Burst, GrainLow = 2000f, GrainHigh = 6000f, GrainLevel = 0.6f, Impact = 0.5f, ImpactPitch = 95f, ImpactBand = 700f, Sustain = 0.161f };
				case SurfaceKind.Wood:
					return new Surface { Spectrum = new[] { -32.9f, -36.5f, -35.9f, -38.2f, -38.1f, -40.4f, -42.3f, -43.4f, -24.2f }, Attack = 0.025f, Length = 0.22f,
						Grains = 4f, GrainKind = Grain.Burst, GrainLow = 1500f, GrainHigh = 2500f, GrainLevel = 0.05f, Impact = 0.2f, ImpactPitch = 90f, Modes = 180f, ImpactBand = 260f, Sustain = 0.17f, LowPass = 9000f };
				case SurfaceKind.Snow:
					return new Surface { Spectrum = new[] { -23.9f, -19.3f, -15.5f, -16.0f, -21.8f, -37.9f, -43.6f, -56.1f, -25.9f }, Attack = 0.1f, Length = 0.6f,
						Grains = 10f, GrainKind = Grain.Burst, GrainLow = 400f, GrainHigh = 2500f, GrainLevel = 1.2f, Sustain = 0.5f, GrainSeconds = 0.006f, GrainQ = 2.5f, LowPass = 6000f };
				case SurfaceKind.Ice:
					return new Surface { Spectrum = new[] { -11.0f, -8.8f, -1.7f, 3.8f, 2.5f, -36.2f, -42.1f, -13.3f, -29.6f }, Attack = 0.12f, Length = 0.58f,
						Grains = 16f, GrainKind = Grain.Ring, GrainLow = 1500f, GrainHigh = 5000f, GrainLevel = 0.2f, Impact = 0.35f, ImpactPitch = 120f, ImpactBand = 1600f, Sustain = 0.405f };
				case SurfaceKind.Water:
					return new Surface { Spectrum = new[] { -7.2f, -3.9f, -2.9f, -2.6f, -34.0f, -3.8f, 2.1f, 0.9f, -13.4f }, Attack = 0.032f, Length = 0.78f,
						Grains = 14f, GrainKind = Grain.Chirp, GrainLow = 800f, GrainHigh = 4000f, GrainLevel = 0.6f, Sustain = 0.678f };
				case SurfaceKind.Metal:
					return new Surface { Spectrum = new[] { -24f, -20f, -14f, -9f, -7f, -6f, -7f, -9f, -22f }, Attack = 0.01f, Length = 0.3f,
						Grains = 3f, GrainKind = Grain.Ring, GrainLow = 2500f, GrainHigh = 9000f, GrainLevel = 0.4f, Impact = 0.6f, ImpactPitch = 110f, Modes = 420f, ImpactBand = 1500f, Sustain = 0.158f };
				default:
					return Of(SurfaceKind.Dirt);
			}
		}

		public static float[] Step(SurfaceKind kind, bool landing, int variant)
		{
			uint seed = Hash(Hash(0xF008u, (uint)kind), (uint)(variant + (landing ? 100 : 0)));
			var noise = new Noise(seed);
			return Render(Of(kind), landing, ref noise);
		}

		/// <summary>One footfall of a surface: its texture, its grains and its impact, varied by the seed.</summary>
		private static float[] Render(Surface surface, bool landing, ref Noise noise)
		{
			// Each variant its own step: a little quicker or slower to build, longer or shorter, its spectrum leant a little.
			float attack = surface.Attack * noise.Range(0.7f, 1.3f);
			float stretch = noise.Range(0.8f, 1.2f);
			float length = surface.Length * stretch;
			float sustain = surface.Sustain * stretch;
			float grains = surface.Grains * noise.Range(0.7f, 1.3f);
			float impact = surface.Impact;
			if (landing)
			{
				// Both feet at once and the body's weight behind them: quicker to its peak, longer, more of everything.
				attack *= 0.4f;
				length *= 1.3f;
				sustain *= 1.3f;
				grains *= 1.6f;
				impact = Math.Max(impact, 0.4f) * 1.5f;
			}
			var data = new float[(int)((length + 0.08f) * Rate)];
			int start = (int)(0.004f * Rate);
			float[] gains = Gains(surface.Spectrum, ref noise, 2f);
			Texture(data, start, attack, sustain, length, gains, ref noise);
			Grains(data, start, attack, sustain, length, (int)MathF.Round(grains * noise.Range(0.8f, 1.2f)), surface, ref noise);
			if (impact > 0f)
			{
				Impact(data, start, impact, surface, landing, ref noise);
			}
			if (landing)
			{
				// The body's weight through the legs: a deep thud under the ground's own sound, felt more than heard.
				Thump(data, start, ref noise, noise.Range(55f, 70f), 0.35f, 0.08f);
			}
			Band(data, surface.HighPass, surface.LowPass);
			return Finish(data);
		}

		/// <summary>An octave spectrum in dB as linear band gains, each leant by up to ±<paramref name="jitter"/> dB.</summary>
		private static float[] Gains(float[] spectrumDb, ref Noise noise, float jitter)
		{
			var gains = new float[spectrumDb.Length];
			for (int b = 0; b < spectrumDb.Length; b++)
			{
				// Energy per octave → amplitude of a constant-Q band of white noise: the band's own width grows with its
				// centre (a band of white noise twice as wide has twice the energy), so its gain falls by √2 an octave.
				float db = spectrumDb[b] + noise.Range(-jitter, jitter);
				gains[b] = MathF.Pow(10f, db / 20f) / MathF.Sqrt(Octaves[b] / 1000f);
			}
			return gains;
		}

		/// <summary>
		/// Noise through an octave filter bank weighted by <paramref name="gains"/>, under the step's envelope: a raised
		/// cosine up over <paramref name="attack"/>, then down exponentially to −35 dB at <paramref name="length"/>.
		/// </summary>
		private static void Texture(float[] into, int at, float attack, float sustain, float length, float[] gains, ref Noise noise)
		{
			int n = Math.Min(into.Length - at, (int)(length * Rate * 1.15f));
			// Fourth-order bands (two biquads each): a second-order band's skirts fall 6 dB an octave, and a ground like sand
			// drops 45 dB in the three octaves below its peak.
			var bank = new Biquad[Octaves.Length];
			var bank2 = new Biquad[Octaves.Length];
			for (int b = 0; b < Octaves.Length; b++)
			{
				float centre = Math.Min(Octaves[b], Rate * 0.42f);
				bank[b] = Biquad.BandPass(centre, 1.2f, Rate);
				bank2[b] = Biquad.BandPass(centre, 1.2f, Rate);
			}
			for (int i = 0; i < n; i++)
			{
				float white = noise.Bipolar();
				float sum = 0f;
				for (int b = 0; b < Octaves.Length; b++)
				{
					sum += bank2[b].Process(bank[b].Process(white)) * gains[b];
				}
				into[at + i] += sum * Envelope((float)i / Rate, attack, sustain, length);
			}
		}

		/// <summary>
		/// The step's envelope at <paramref name="t"/> seconds: up over the attack (a raised cosine), held near the peak —
		/// easing 6 dB down — while the crunch goes on, then falling: 20 dB down at <paramref name="sustain"/>, 35 dB down at
		/// <paramref name="length"/>, as measured. A plain exponential from the peak reached −20 dB in half the time the
		/// recordings take.
		/// </summary>
		public static float Envelope(float t, float attack, float sustain, float length)
		{
			if (t < attack)
			{
				return 0.5f - 0.5f * MathF.Cos(MathF.PI * t / Math.Max(attack, 1e-4f));
			}
			// From −6 dB at the end of the hold to −35 dB at the length, passing −20 dB at the sustain.
			float fall = Math.Max(0.015f, (length - sustain) * 29f / 15f);
			float hold = Math.Max(0f, length - attack - fall);
			float s = t - attack;
			float db = s < hold ? -6f * s / Math.Max(hold, 1e-4f) : -6f - 29f * (s - hold) / fall;
			return MathF.Pow(10f, db / 20f);
		}

		/// <summary>Limits a sound to a band, fourth order each side (0 for no limit on that side).</summary>
		private static void Band(float[] data, float highPass, float lowPass)
		{
			if (highPass > 0f)
			{
				var a = Biquad.HighPass(highPass, 0.7f, Rate);
				var b = Biquad.HighPass(highPass, 0.7f, Rate);
				for (int i = 0; i < data.Length; i++)
				{
					data[i] = b.Process(a.Process(data[i]));
				}
			}
			if (lowPass > 0f)
			{
				var a = Biquad.LowPass(lowPass, 0.7f, Rate);
				var b = Biquad.LowPass(lowPass, 0.7f, Rate);
				for (int i = 0; i < data.Length; i++)
				{
					data[i] = b.Process(a.Process(data[i]));
				}
			}
		}

		/// <summary>The grains, placed in time as the envelope is loud: most near the peak, a few in the tail.</summary>
		private static void Grains(float[] into, int at, float attack, float sustain, float length, int count, Surface surface, ref Noise noise)
		{
			for (int g = 0; g < count; g++)
			{
				// By rejection against the envelope: a grain is as likely as the step is loud.
				float t = 0f, level = 0f;
				for (int tries = 0; tries < 20; tries++)
				{
					t = noise.Next() * length;
					level = Envelope(t, attack, sustain, length);
					if (noise.Next() < level)
					{
						break;
					}
				}
				// Most grains are faint, a few stand out.
				float amplitude = surface.GrainLevel * level * (0.15f + 0.85f * MathF.Pow(noise.Next(), 2f)) * GrainScale;
				int start = at + (int)(t * Rate);
				float pitch = noise.LogRange(surface.GrainLow, surface.GrainHigh);
				switch (surface.GrainKind)
				{
					case Grain.Burst:
						AddBurst(into, start, ref noise, pitch, surface.GrainQ, amplitude, surface.GrainSeconds * noise.Range(0.5f, 1.5f), Rate, 0.0001f);
						break;
					case Grain.Ring:
						AddRing(into, start, pitch, amplitude * 0.6f, noise.Range(0.004f, 0.015f), Rate, noise.Next() * 6.28f);
						AddBurst(into, start, ref noise, pitch, 1.2f, amplitude * 0.5f, 0.0015f, Rate, 0.0001f);
						break;
					case Grain.Chirp:
						AddChirp(into, start, pitch, noise.Range(0.4f, 1.2f), amplitude * 0.8f, noise.Range(0.01f, 0.04f), Rate);
						break;
				}
			}
		}

		/// <summary>The heel striking hard ground: a short band-limited knock, a low thump, and a board's modes.</summary>
		private static void Impact(float[] into, int at, float level, Surface surface, bool landing, ref Noise noise)
		{
			float a = level * 0.12f;
			AddBurst(into, at, ref noise, surface.ImpactBand * noise.Range(0.8f, 1.25f), 0.8f, a, 0.012f, Rate, 0.0003f);
			Thump(into, at, ref noise, surface.ImpactPitch * noise.Range(0.9f, 1.1f), a * 0.8f, landing ? 0.05f : 0.03f);
			if (surface.Modes > 0f)
			{
				// A beam's bending modes: f, 2.76f, 5.40f… not a string's harmonics; each dying faster than the last.
				float f0 = surface.Modes * noise.Range(0.8f, 1.25f);
				AddRing(into, at, f0, a * 0.8f, 0.16f, Rate);
				AddRing(into, at, f0 * 2.76f, a * 0.45f, 0.1f, Rate, 0.7f);
				AddRing(into, at, f0 * 5.40f, a * 0.2f, 0.05f, Rate, 1.9f);
			}
		}

		// ── Swimming, climbing, the long grass ─────────────────────────────

		public static string SwimKey(bool plunge, int variant) => $"Footstep.v2.{(plunge ? "Plunge" : "Stroke")}.{variant}";

		/// <summary>An arm breaking the surface (a stroke), or a body going in (a plunge): water's spectrum, its drops, longer.</summary>
		public static float[] Swim(bool plunge, int variant)
		{
			var noise = new Noise(Hash(plunge ? 0x5012u : 0x5708u, (uint)variant));
			Surface water = Of(SurfaceKind.Water);
			float length = (plunge ? 1.4f : 0.7f) * noise.Range(0.85f, 1.15f);
			float attack = plunge ? 0.02f : noise.Range(0.06f, 0.12f);
			var data = new float[(int)((length + 0.1f) * Rate)];
			float[] gains = Gains(water.Spectrum, ref noise, 2f);
			if (plunge)
			{
				// The body's cavity closing behind it puts weight under the splash.
				gains[0] *= 3f;
				gains[1] *= 2.5f;
				gains[2] *= 1.8f;
			}
			Texture(data, 0, attack, length * 0.75f, length, gains, ref noise);
			Grains(data, 0, attack, length * 0.75f, length, plunge ? 60 : 18, water, ref noise);
			if (plunge)
			{
				AddChirp(data, (int)(0.06f * Rate), noise.Range(110f, 160f), 1.2f, 0.12f, 0.15f, Rate);
			}
			return Finish(data);
		}

		public static string RungKey(int variant) => $"Footstep.v3.Rung.{variant}";

		/// <summary>A hand on a rung and the rung knocking under the weight: a short step on boards, quicker and lighter.</summary>
		public static float[] Rung(int variant)
		{
			var noise = new Noise(Hash(0x2028u, (uint)variant));
			return Render(RungSurface, false, ref noise);
		}

		/// <summary>
		/// A hand and a boot on a wooden rung, as recorded knocks on wood measure: bright for wood (the hand's slap and the
		/// rung's knock, centred near 2 kHz), quick to its peak, ringing on a few tenths.
		/// </summary>
		private static Surface RungSurface => new Surface
		{
			Spectrum = new[] { -23.7f, -13.0f, -7.6f, -14.3f, -11.6f, -8.3f, -9.2f, -9.0f, -27.3f }, Attack = 0.015f, Length = 0.39f, Sustain = 0.09f,
			Grains = 7f, GrainKind = Grain.Burst, GrainLow = 1000f, GrainHigh = 6000f, GrainLevel = 0.6f,
			Impact = 0.3f, ImpactPitch = 140f, ImpactBand = 1200f, Modes = 380f,
		};

		public static string RustleKey(int variant) => $"Footstep.v3.Rustle.{variant}";

		/// <summary>
		/// A leg pushing through tall grass, as a walk through it measures: broad and even from 125 Hz up, slow to build
		/// (the stems give before they part), long, blades snapping through it. The recording's lowest octave is the wind on
		/// its microphone and is left out.
		/// </summary>
		public static float[] Rustle(int variant)
		{
			var noise = new Noise(Hash(0x6A57u, (uint)variant));
			var rustle = new Surface
			{
				Spectrum = new[] { -24f, -13f, -10f, -14f, -14f, -15f, -18f, -16f, -18f }, Attack = 0.22f, Length = 0.75f, Sustain = 0.6f,
				Grains = 16f, GrainKind = Grain.Burst, GrainLow = 1500f, GrainHigh = 5000f, GrainLevel = 0.4f,
			};
			return Render(rustle, false, ref noise);
		}

		// ── Pieces ─────────────────────────────────────────────────────────

		/// <summary>The mass meeting the ground: a damped low sine with a puff of low-passed noise.</summary>
		private static void Thump(float[] data, int at, ref Noise noise, float frequency, float amplitude, float seconds)
		{
			AddRing(data, at, frequency, amplitude * 0.8f, seconds, Rate, 1.5708f * noise.Next());
			int length = Math.Min(data.Length - at, (int)(seconds * Rate));
			var low = Biquad.LowPass(frequency * 2.5f, 0.7f, Rate);
			float env = amplitude * 0.6f, decay = MathF.Exp(-6.9f / Math.Max(1, length));
			for (int i = 0; i < length; i++)
			{
				data[at + i] += low.Process(noise.Bipolar()) * env;
				env *= decay;
			}
		}

		private static float[] Finish(float[] data)
		{
			RemoveDc(data);
			FadeEnds(data, 24, (int)(0.04f * Rate));
			Normalize(data, 0.9f);
			return data;
		}
	}
}
