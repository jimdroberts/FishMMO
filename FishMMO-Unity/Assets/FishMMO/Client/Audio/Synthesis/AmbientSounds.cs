using System;
using static FishMMO.Client.SoundSynth;

namespace FishMMO.Client
{
	/// <summary>The ambient beds that are not weather cues: the wind heard through what grows round the listener.</summary>
	public enum AmbientBed
	{
		/// <summary>Wind through standing grass: waves of hiss rolling across a field.</summary>
		GrassWind = 0,
		/// <summary>Wind through broadleaved crowns: the rustle of leaves knocking together.</summary>
		LeafWind = 1,
		/// <summary>Wind through conifers: the soughing of needles, lower and smoother than leaves.</summary>
		NeedleWind = 2,
	}

	/// <summary>
	/// The looping beds, synthesised: every weather cue's loop (<see cref="WeatherAudioCue"/>), the wind in the grass and
	/// the trees (<see cref="AmbientBed"/>), and the falls' roar and splash. Each is a seamless loop from a fixed seed,
	/// played at the place in it the shared clock says, so the rain's patter and the gusts in it are the same moment of
	/// sound for every player in the same weather.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Fitted to recordings (2026-10-19).</b> Each bed is a <see cref="Bed"/> measured from field recordings (Wikimedia
	/// Commons): its octave spectrum; how much it swells with the gusts and how fast; how much it flutters within them;
	/// and its events — the drops, knocks, hailstones, bubbles and leaves breaking — with a spread of loudness that gives
	/// the recording's peakiness (a light rain is a few loud drops over a quiet bed; a downpour, a steady roar). The first
	/// beds were each a noise colour under sine swells: the wind had its centre at 60 Hz where recorded wind has it at
	/// 150–400, the falls' splash was white hiss centred at 11 kHz where a brook's is bubbles at 3, and the light rain had
	/// none of a real one's spikiness.
	/// </para>
	/// <para>
	/// <b>No measured reference</b> for wind in grass, blowing sand, falling ash or falling snow: those four are made the
	/// same way from their nearest measured kin, and are the least certain.
	/// </para>
	/// </remarks>
	public static class AmbientSounds
	{
		/// <summary>
		/// Every loop's length in samples: a power of two, as the frequency-domain synthesis needs (<see
		/// cref="SoundSynth.SpectralNoise"/>), so each loop is periodic and has no seam at all.
		/// </summary>
		public const int LoopSamples = 1 << 18;

		/// <summary>Every loop's length, seconds (about six).</summary>
		public const float LoopSeconds = (float)LoopSamples / Rate;

		/// <summary>The key a weather loop is kept under (<see cref="SoundLibrary"/>): bump the version when a recipe changes.</summary>
		public static string Key(WeatherAudioCue cue) => "Weather.v4." + cue;

		public static string Key(AmbientBed bed) => "Ambient.v4." + bed;

		/// <summary>Whether a weather cue has a synthesised loop.</summary>
		public static bool Synthesised(WeatherAudioCue cue) => WeatherAudioProfile.IsLoop(cue);

		/// <summary>What an event in a bed is.</summary>
		public enum Event
		{
			None,
			/// <summary>A raindrop striking: a millisecond or few of band-passed noise.</summary>
			Drop,
			/// <summary>A drop on a roof: a short ring of the boards or the canvas, with its tick.</summary>
			Knock,
			/// <summary>A hailstone: a hard click, a thud where it lands, and a lighter bounce after.</summary>
			Stone,
			/// <summary>A bubble at the surface: a rising "plink" (Minnaert).</summary>
			Bubble,
			/// <summary>Leaves or grains touching: a very short faint tick, thousands a second.</summary>
			Tick,
		}

		/// <summary>One bed, as measured.</summary>
		public sealed class Bed
		{
			/// <summary>The energy in each octave (<see cref="FootstepSounds.Octaves"/>), dB against the whole.</summary>
			public float[] Spectrum;
			/// <summary>The loop's RMS level: how loud it is against the other beds at full volume.</summary>
			public float Rms = 0.15f;
			/// <summary>The slow swell of the gusts: the standard deviation of the log level, and its pace (Hz).</summary>
			public float Swell, SwellHz = 0.15f;
			/// <summary>The quicker flutter within the gusts: the standard deviation of the log level, and its pace (Hz).</summary>
			public float Flutter, FlutterHz = 3f;
			public Event Events;
			/// <summary>Events a second, on average.</summary>
			public float EventRate;
			/// <summary>The events' pitch range, Hz.</summary>
			public float EventLow, EventHigh;
			/// <summary>The loudest event against the bed's RMS.</summary>
			public float EventLevel;
			/// <summary>How the events' loudness is spread: u^spread, so a high spread is a few loud among many faint.</summary>
			public float EventSpread = 2f;
			/// <summary>Whether the events thicken and loudens with the gusts (leaves, sand), or fall regardless (rain).</summary>
			public bool EventsFollowSwell;
			/// <summary>Strong wind's howl: two narrow resonances wandering in pitch, against the bed.</summary>
			public float Howl;
			/// <summary>Band limits on the whole loop, Hz (0 for none).</summary>
			public float HighPass, LowPass;
		}

		public static Bed Of(WeatherAudioCue cue)
		{
			switch (cue)
			{
				case WeatherAudioCue.RainLight:
					// Three recordings of light rain: a quiet hiss under drops heard one by one, the louder ones far apart.
					return new Bed { Spectrum = new[] { -29.4f, -20.3f, -22.9f, -23.2f, -23.2f, -26.8f, -35.3f, -30.4f, -36.0f }, Rms = 0.1f,
						Swell = 0.15f, SwellHz = 0.1f, Flutter = 0.1f, Events = Event.Drop, EventRate = 80f, EventLow = 1500f, EventHigh = 7000f,
						EventLevel = 120f, EventSpread = 4f };
				case WeatherAudioCue.RainHeavy:
					// Two recordings of downpours: a steady roar centred near a kilohertz, too many drops to hear apart.
					return new Bed { Spectrum = new[] { -36.0f, -30.4f, -28.7f, -21.7f, -13.0f, -15.3f, -17.2f, -38.6f, -40.0f }, Rms = 0.2f,
						Swell = 0.05f, SwellHz = 0.12f, Flutter = 0.05f, Events = Event.Drop, EventRate = 800f, EventLow = 800f, EventHigh = 5000f,
						EventLevel = 2.5f, EventSpread = 1.5f };
				case WeatherAudioCue.RainOnRoof:
					// Rain on a veranda's roof and on a canvas hood: the patter is the roof's, mid-heavy, with its knocks.
					return new Bed { Spectrum = new[] { -15.7f, -1.6f, 4.2f, 2.8f, -22.1f, -34.5f, -0.1f, 0.1f, -17.7f }, Rms = 0.14f,
						Swell = 0.35f, SwellHz = 0.1f, Flutter = 0.12f, Events = Event.Knock, EventRate = 150f, EventLow = 400f, EventHigh = 2500f,
						EventLevel = 30f, EventSpread = 3f };
				case WeatherAudioCue.Hail:
					// Hail in Melbourne: stones thudding the ground (125–250 Hz) and ticking bright (8 kHz), coming in waves.
					return new Bed { Spectrum = new[] { -50.2f, -7.5f, -5.7f, -11.1f, -15.5f, -11.0f, -10.3f, 1.3f, -36.1f }, Rms = 0.17f,
						Swell = 0.6f, SwellHz = 0.08f, Flutter = 0.15f, Events = Event.Stone, EventRate = 60f, EventLow = 2000f, EventHigh = 8000f,
						EventLevel = 5f, EventSpread = 1.5f };
				case WeatherAudioCue.Snow:
					// No recording of snow falling, which is all but silent: a faint hush with the air's slow movement.
					return new Bed { Spectrum = new[] { -40f, -32f, -24f, -14f, -8f, -6f, -8f, -14f, -28f }, Rms = 0.04f,
						Swell = 0.25f, SwellHz = 0.1f, Flutter = 0.08f };
				case WeatherAudioCue.Wind:
					// Two recordings of wind in the open: low and broad, centred at 150–400 Hz, big slow gusts.
					return new Bed { Spectrum = new[] { -8.6f, -7.2f, -4.8f, -5.7f, -8.1f, -13.2f, -28.1f, -30.5f, -36.6f }, Rms = 0.14f,
						Swell = 0.4f, SwellHz = 0.12f, Flutter = 0.42f, FlutterHz = 2.5f, Howl = 0.02f };
				case WeatherAudioCue.WindStrong:
					// Strong winds before rain: heavier in the lows, buffeting, with the howl of air past edges.
					return new Bed { Spectrum = new[] { -4.2f, -2.8f, -7.7f, -7.3f, -17.9f, -21.0f, -25.8f, -27.6f, -28.9f }, Rms = 0.2f,
						Swell = 0.25f, SwellHz = 0.15f, Flutter = 0.38f, FlutterHz = 3.5f, Howl = 0.08f };
				case WeatherAudioCue.Sand:
					// No recording of blowing sand: the strong wind with grains hissing on everything, thickest in the gusts.
					return new Bed { Spectrum = new[] { -10f, -9f, -10f, -11f, -13f, -11f, -8f, -9f, -18f }, Rms = 0.16f,
						Swell = 0.4f, SwellHz = 0.15f, Flutter = 0.3f, Events = Event.Tick, EventRate = 2500f, EventLow = 3000f, EventHigh = 9000f,
						EventLevel = 1.2f, EventSpread = 1.5f, EventsFollowSwell = true };
				case WeatherAudioCue.Ash:
					// No recording of falling ash: the light wind, muffled, with the odd soft tick.
					return new Bed { Spectrum = new[] { -12f, -10f, -8f, -9f, -12f, -17f, -26f, -34f, -40f }, Rms = 0.06f,
						Swell = 0.3f, SwellHz = 0.1f, Flutter = 0.15f, Events = Event.Tick, EventRate = 30f, EventLow = 2000f, EventHigh = 5000f,
						EventLevel = 2f, EventSpread = 2f };
				default:
					return null;
			}
		}

		public static Bed Of(AmbientBed bed)
		{
			switch (bed)
			{
				case AmbientBed.GrassWind:
					// No recording of wind in grass: a softer kin of the leaves' — the hiss without the crackle, in waves.
					return new Bed { Spectrum = new[] { -45f, -36f, -24f, -15f, -10f, -8f, -6f, -6f, -18f }, Rms = 0.12f,
						Swell = 0.4f, SwellHz = 0.2f, Flutter = 0.2f, FlutterHz = 1.5f };
				case AmbientBed.LeafWind:
					// Bourne woods, windy: the crowns' rustle peaks at 4–8 kHz; the leaves knock thickest in the gusts.
					return new Bed { Spectrum = new[] { -79.1f, -49.8f, -25.8f, -13.2f, -6.8f, -13.7f, -3.8f, -1.5f, -28.9f }, Rms = 0.12f,
						Swell = 0.12f, SwellHz = 0.15f, Flutter = 0.25f, FlutterHz = 2f, Events = Event.Tick, EventRate = 1500f, EventLow = 2000f,
						EventHigh = 9000f, EventLevel = 10f, EventSpread = 2f, EventsFollowSwell = true };
				case AmbientBed.NeedleWind:
					// A Swedish pine forest at 25 m/s: a smooth roar centred near 1.6 kHz, swelling slowly.
					return new Bed { Spectrum = new[] { -37.3f, -24.5f, -10.4f, -7.4f, -4.5f, -3.9f, -10.3f, -16.2f, -30.0f }, Rms = 0.12f,
						Swell = 0.28f, SwellHz = 0.1f, Flutter = 0.03f, FlutterHz = 2f };
				default:
					return null;
			}
		}

		/// <summary>
		/// A waterfall's bed: <paramref name="bright"/> false for the roar of where it lands (the larger falls recorded:
		/// centred at 600–800 Hz, little below 125), true for the splash and the rush of a small fall or the white water
		/// below (a brook and small falls: centred at 2–3 kHz, bubbly).
		/// </summary>
		public static Bed Falls(bool bright)
		{
			return bright
				? new Bed { Spectrum = new[] { -39.8f, -42.1f, -35.5f, -30.3f, -22.3f, -25.7f, -25.8f, -32.7f, -41.3f }, Rms = 0.15f,
					Swell = 0.05f, SwellHz = 0.2f, Flutter = 0.3f, FlutterHz = 4f, Events = Event.Bubble, EventRate = 300f, EventLow = 500f,
					EventHigh = 3000f, EventLevel = 4f, EventSpread = 2.5f }
				: new Bed { Spectrum = new[] { -27.9f, -15.9f, -8.1f, -8.2f, -10.4f, -16.5f, -20.6f, -23.2f, -38.3f }, Rms = 0.15f,
					Swell = 0.25f, SwellHz = 0.15f, Flutter = 0.25f, FlutterHz = 4f };
		}

		public static float[] Make(WeatherAudioCue cue)
		{
			Bed bed = Of(cue);
			return bed != null ? Make(bed, Hash(0x3EA7u, (uint)cue)) : null;
		}

		public static float[] Make(AmbientBed which)
		{
			Bed bed = Of(which);
			return bed != null ? Make(bed, Hash(0xBEDu, (uint)which)) : null;
		}

		/// <summary>
		/// A loop of a bed, <see cref="LoopSamples"/> long and periodic: the texture made in the frequency domain to the
		/// measured spectrum exactly (a filter bank's overlapping skirts rippled between its centres), the gusts as periodic
		/// slow noise, and the events wrapped round the end.
		/// </summary>
		public static float[] Make(Bed bed, uint seed)
		{
			var noise = new Noise(seed);
			int n = LoopSamples;
			float[] spectrum = bed.Spectrum;
			float[] raw = SpectralNoise(n, Rate, hz => OctaveAmplitude(hz, FootstepSounds.Octaves, spectrum), ref noise);

			// The swell and the flutter: smooth random log-levels (log-normal, as gusts are), mean one in energy.
			float[] swell = Slow(n, bed.SwellHz, ref noise);
			float[] flutter = Slow(n, bed.FlutterHz, ref noise);
			float norm = MathF.Sqrt(MathF.Exp(-(bed.Swell * bed.Swell + bed.Flutter * bed.Flutter)));
			var level = new float[n];
			for (int i = 0; i < n; i++)
			{
				level[i] = MathF.Exp(bed.Swell * swell[i] + bed.Flutter * flutter[i]) * norm;
				raw[i] *= level[i];
			}

			if (bed.Events != Event.None && bed.EventRate > 0f)
			{
				Events(raw, bed, level, ref noise);
			}
			if (bed.Howl > 0f)
			{
				Howl(raw, bed.Howl, swell, ref noise);
			}
			Band(raw, bed.HighPass, bed.LowPass);
			Level(raw, bed.Rms);
			return raw;
		}

		/// <summary>
		/// Periodic slow noise of unit variance whose changes come at about <paramref name="hz"/>: a second-order low-pass
		/// spectrum (flat to <paramref name="hz"/>, falling 12 dB an octave above), made in the frequency domain so it
		/// loops with the sound.
		/// </summary>
		private static float[] Slow(int n, float hz, ref Noise noise)
		{
			float corner = Math.Max(0.02f, hz);
			return SpectralNoise(n, Rate, f => 1f / (1f + (f / corner) * (f / corner)), ref noise);
		}

		/// <summary>
		/// Brings a loop to <paramref name="rms"/>, or lower where its peaks would not fit: never clipped or limited. A
		/// limiter on a gusting wind put its distortion at 4 kHz, and on a light rain flattened the very drops that make it
		/// one (recorded light rain peaks 25–30 dB over its RMS).
		/// </summary>
		private static void Level(float[] data, float rms)
		{
			double sum = 0.0;
			float peak = 1e-9f;
			for (int i = 0; i < data.Length; i++)
			{
				sum += data[i] * data[i];
				peak = Math.Max(peak, Math.Abs(data[i]));
			}
			float now = MathF.Sqrt((float)(sum / Math.Max(1, data.Length))) + 1e-12f;
			float gain = Math.Min(rms / now, 0.97f / peak);
			for (int i = 0; i < data.Length; i++)
			{
				data[i] *= gain;
			}
		}



		/// <summary>The bed's events, Poisson in time, their loudness spread as u^spread.</summary>
		private static void Events(float[] into, Bed bed, float[] level, ref Noise noise)
		{
			float seconds = (float)into.Length / Rate;
			// Made on a scratch twice the loop's length and folded back, so an event near the end carries on at the start.
			var scratch = new float[into.Length + Rate];
			float t = noise.Poisson(bed.EventRate);
			while (t < seconds)
			{
				int at = (int)(t * Rate);
				float gust = bed.EventsFollowSwell ? level[Math.Min(at, level.Length - 1)] : 1f;
				float amplitude = bed.EventLevel * MathF.Pow(noise.Next(), bed.EventSpread) * gust;
				float pitch = noise.LogRange(bed.EventLow, bed.EventHigh);
				switch (bed.Events)
				{
					case Event.Drop:
						AddBurst(scratch, at, ref noise, pitch, 2f, amplitude, noise.Range(0.0015f, 0.004f), Rate, 0.0001f);
						break;
					case Event.Tick:
						AddBurst(scratch, at, ref noise, pitch, 2f, amplitude, noise.Range(0.0008f, 0.002f), Rate, 0.0001f);
						break;
					case Event.Knock:
						AddRing(scratch, at, pitch, amplitude * 0.6f, noise.Range(0.008f, 0.025f), Rate, noise.Next() * 6.28f);
						AddBurst(scratch, at, ref noise, pitch * 2.5f, 1.5f, amplitude * 0.5f, 0.002f, Rate, 0.0001f);
						break;
					case Event.Stone:
						AddBurst(scratch, at, ref noise, pitch, 1.5f, amplitude, noise.Range(0.001f, 0.003f), Rate, 0.0001f);
						AddRing(scratch, at, noise.LogRange(150f, 400f), amplitude * 0.5f, 0.02f, Rate, noise.Next() * 6.28f);
						int bounce = at + (int)(noise.Range(0.04f, 0.12f) * Rate);
						AddBurst(scratch, bounce, ref noise, pitch * 1.1f, 1.5f, amplitude * 0.35f, 0.0015f, Rate, 0.0001f);
						break;
					case Event.Bubble:
						AddChirp(scratch, at, pitch, noise.Range(0.4f, 1.2f), amplitude * 0.5f, noise.Range(0.01f, 0.04f), Rate);
						break;
				}
				// Rain falls regardless of the gusts; leaves and sand come thickest in them.
				float rate = bed.EventsFollowSwell ? bed.EventRate * Math.Max(0.15f, gust) : bed.EventRate;
				t += noise.Poisson(rate);
			}
			for (int i = 0; i < scratch.Length; i++)
			{
				into[i % into.Length] += scratch[i];
			}
		}

		/// <summary>
		/// Air past edges: two narrow resonances (420 and 610 Hz, Q about 14), louder in the gusts — made in the frequency
		/// domain like the bed, so the loop stays periodic.
		/// </summary>
		private static void Howl(float[] into, float howl, float[] swell, ref Noise noise)
		{
			int n = into.Length;
			float[] tone = SpectralNoise(n, Rate, hz => Peak(hz, 420f, 14f) + 0.7f * Peak(hz, 610f, 16f), ref noise);
			double sum = 0.0;
			for (int i = 0; i < n; i++)
			{
				float gust = MathF.Exp(0.8f * swell[i]);
				tone[i] *= gust * gust;
				sum += tone[i] * tone[i];
			}
			float scale = howl / MathF.Sqrt((float)(sum / n) + 1e-20f);
			for (int i = 0; i < n; i++)
			{
				into[i] += tone[i] * scale;
			}
		}

		/// <summary>A resonance's amplitude response at <paramref name="hz"/>: a band-pass of centre <paramref name="centre"/> and quality <paramref name="q"/>.</summary>
		private static float Peak(float hz, float centre, float q)
		{
			float x = q * (hz / centre - centre / Math.Max(hz, 1e-3f));
			return 1f / MathF.Sqrt(1f + x * x);
		}

		/// <summary>
		/// Limits a periodic sound to a band, fourth order each side (0 for no limit on that side): filtered twice round,
		/// keeping the second pass, so the filters start the loop already settled and its end still meets its start.
		/// </summary>
		private static void Band(float[] data, float highPass, float lowPass)
		{
			if (highPass <= 0f && lowPass <= 0f)
			{
				return;
			}
			var h1 = Biquad.HighPass(Math.Max(highPass, 1f), 0.7f, Rate);
			var h2 = Biquad.HighPass(Math.Max(highPass, 1f), 0.7f, Rate);
			var l1 = Biquad.LowPass(lowPass > 0f ? lowPass : Rate * 0.45f, 0.7f, Rate);
			var l2 = Biquad.LowPass(lowPass > 0f ? lowPass : Rate * 0.45f, 0.7f, Rate);
			int n = data.Length;
			for (int pass = 0; pass < 2; pass++)
			{
				for (int i = 0; i < n; i++)
				{
					float x = data[i];
					if (highPass > 0f)
					{
						x = h2.Process(h1.Process(x));
					}
					if (lowPass > 0f)
					{
						x = l2.Process(l1.Process(x));
					}
					if (pass == 1)
					{
						data[i] = x;
					}
				}
			}
		}
	}
}
