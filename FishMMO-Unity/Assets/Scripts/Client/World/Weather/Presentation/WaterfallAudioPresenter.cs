using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The roar of the waterfalls nearest the listener: a looping 3D source where each lands, and a quieter one at
	/// the lip of a high fall, on the Ambient channel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>How loud, from how big.</b> Loudness goes with the logarithm of a fall's <see cref="Waterfalls.Fall.Power"/>,
	/// as hearing does: a 1 kW trickle is barely there standing beside it, a 1 MW mountain fall carries a couple of
	/// hundred metres, a river's gigawatt is heard over a kilometre and a half. Bigger falls are also deeper, both
	/// in pitch and in their mix, which leans from the bright splash of a small fall to the low roar of a big one.
	/// </para>
	/// <para>
	/// <b>Distance takes the top off first.</b> Each landing plays two sources at the same point: the roar, low and
	/// heard to the full range, and the splash, bright and heard to only two fifths of it. Walking away, the hiss
	/// of the spray goes long before the rumble does, which is what air and ground do to a real fall's sound. Both
	/// fall away on a custom 1/r curve that reaches silence at the range: Unity's own logarithmic rolloff never
	/// does, and holds its last level for ever past the maximum distance.
	/// </para>
	/// <para>
	/// <b>No assets.</b> There are no audio files for this. Two loops — roar and splash — are synthesised from
	/// seeded noise when the first fall appears (scene load, where a few milliseconds go unnoticed), and played as
	/// ordinary clips: a generator in OnAudioFilterRead is not reliably spatialised, a clip always is.
	/// </para>
	/// </remarks>
	public sealed class WaterfallAudioPresenter : IDisposable
	{
		/// <summary>How many falls are heard at once.</summary>
		public const int MaxFalls = 4;

		/// <summary>A fall at least this high, metres, is heard at its lip as well as where it lands.</summary>
		public const float LipDropMetres = 20f;

		/// <summary>Seconds for a fall's sound to fade in or out as it becomes one of the nearest or stops being.</summary>
		public const float FadeSeconds = 1.5f;

		/// <summary>Seconds between choices of which falls are heard.</summary>
		public const float RankSeconds = 0.25f;

		/// <summary>The splash at a landing is heard to this share of the roar's range.</summary>
		public const float SplashRangeShare = 0.4f;

		/// <summary>The lip's source is heard to this share of the roar's range, at this share of its loudness.</summary>
		public const float LipRangeShare = 0.5f, LipLoudnessShare = 0.35f;

		private const int SampleRate = 44100;
		private const float ClipSeconds = 6f, CrossfadeSeconds = 0.5f;

		/// <summary>One sound source, with what it should be at full fade.</summary>
		private sealed class Emitter
		{
			public AudioSource Source;
			public ChannelAudioSource Channel;
			public float Volume;
		}

		/// <summary>One fall being heard: its landing's roar and splash and its lip's splash.</summary>
		private sealed class Voice
		{
			public Emitter Roar, Splash, Lip;
			public Vector3 Key;
			public bool Assigned, Wanted, HasLip;
			public float Level;
		}

		private readonly Transform parent;
		private readonly List<Voice> voices = new List<Voice>();
		private readonly List<KeyValuePair<float, int>> ranked = new List<KeyValuePair<float, int>>();
		private readonly List<int> chosen = new List<int>();
		private AudioClip roarClip, splashClip;
		private int rankedVersion = int.MinValue;
		private float sinceRank = float.MaxValue;

		/// <param name="parent">What the sources are parented under.</param>
		public WaterfallAudioPresenter(Transform parent)
		{
			this.parent = parent;
		}

		/// <summary>How loud a fall is at its loudest, 0..1, from the logarithm of its power.</summary>
		public static float Loudness(in Waterfalls.Fall fall)
		{
			float size = Size(fall);
			return 0.12f + 0.88f * Mathf.Pow(size, 0.8f);
		}

		/// <summary>0 for a 1 kW trickle, 1 for a 100 MW river, along the logarithm of the power between.</summary>
		public static float Size(in Waterfalls.Fall fall)
		{
			return Mathf.Clamp01((Mathf.Log10(Mathf.Max(1f, fall.Power)) - 3f) / 5f);
		}

		/// <summary>
		/// How far a fall's roar is heard, metres: about 15 m for 1 kW, 270 m for 1 MW, capped at 1.5 km. Each tenfold
		/// of power carries it about 2.6 times as far.
		/// </summary>
		public static float Range(in Waterfalls.Fall fall)
		{
			float decades = Mathf.Log10(Mathf.Max(1f, fall.Power)) - 3f;
			return Mathf.Clamp(15f * Mathf.Pow(10f, decades * 0.42f), 12f, 1500f);
		}

		/// <summary>Within this distance, metres, a fall is at its full loudness: close enough that it fills the ear.</summary>
		public static float Near(in Waterfalls.Fall fall, float range)
		{
			return Mathf.Clamp(0.03f * range + 0.5f * Mathf.Max(0f, fall.Width), 1f, 0.3f * range);
		}

		/// <summary>Brings the sources up to date for this frame.</summary>
		/// <param name="falls">Every fall in the loaded scenes.</param>
		/// <param name="version">Their <see cref="Waterfalls.Version"/>.</param>
		/// <param name="listener">Where they are heard from.</param>
		/// <param name="deltaTime">Seconds since the last update.</param>
		public void Update(IReadOnlyList<Waterfalls.Fall> falls, int version, Vector3 listener, float deltaTime)
		{
			if (falls.Count > 0 && roarClip == null)
			{
				roarClip = Synthesise("Waterfall Roar", false, 0x6A09E667u);
				splashClip = Synthesise("Waterfall Splash", true, 0xBB67AE85u);
			}

			sinceRank += deltaTime;
			if (version != rankedVersion || sinceRank >= RankSeconds)
			{
				Choose(falls, listener);
				Assign(falls);
				rankedVersion = version;
				sinceRank = 0f;
			}

			// Fades: a fall coming into the nearest few eases in, one leaving eases out, and only once silent is
			// its voice free for another — so a voice is never moved across the map while it can be heard.
			// Quicker when the falls have gone altogether: a scene unloaded should not leave its roar behind.
			float step = deltaTime / (falls.Count == 0 ? 0.3f : FadeSeconds);
			bool anyAssigned = false;
			foreach (Voice voice in voices)
			{
				if (!voice.Assigned)
				{
					continue;
				}
				voice.Level = Mathf.MoveTowards(voice.Level, voice.Wanted ? 1f : 0f, step);
				if (!voice.Wanted && voice.Level <= 0f)
				{
					Release(voice);
					continue;
				}
				anyAssigned = true;
				SetLevel(voice.Roar, voice.Level);
				SetLevel(voice.Splash, voice.Level);
				SetLevel(voice.Lip, voice.HasLip ? voice.Level : 0f);
			}

			// The falls are gone (a scene unloaded) and the last of them has faded: nothing is kept.
			if (falls.Count == 0 && !anyAssigned && voices.Count > 0)
			{
				DestroyVoices();
			}
		}

		/// <summary>Picks the falls loudest where the listener stands: the nearest as a share of each one's range.</summary>
		private void Choose(IReadOnlyList<Waterfalls.Fall> falls, Vector3 listener)
		{
			ranked.Clear();
			for (int i = 0; i < falls.Count; i++)
			{
				Waterfalls.Fall fall = falls[i];
				float range = Range(fall);
				float reach = Mathf.Min(Vector3.Distance(listener, fall.Landing),
					fall.Drop >= LipDropMetres ? Vector3.Distance(listener, fall.Lip) : float.MaxValue) / range;
				if (reach < 1f)
				{
					ranked.Add(new KeyValuePair<float, int>(reach, i));
				}
			}
			ranked.Sort((x, y) => x.Key.CompareTo(y.Key));
			chosen.Clear();
			for (int i = 0; i < ranked.Count && i < MaxFalls; i++)
			{
				chosen.Add(ranked[i].Value);
			}
		}

		/// <summary>Keeps the voices of falls still chosen, lets the rest fade, and gives a free voice to each newcomer.</summary>
		private void Assign(IReadOnlyList<Waterfalls.Fall> falls)
		{
			foreach (Voice voice in voices)
			{
				voice.Wanted = false;
			}
			foreach (int index in chosen)
			{
				Waterfalls.Fall fall = falls[index];
				Voice held = null;
				foreach (Voice voice in voices)
				{
					if (voice.Assigned && (voice.Key - fall.Landing).sqrMagnitude < 0.01f)
					{
						held = voice;
						break;
					}
				}
				if (held == null)
				{
					held = FreeVoice();
					if (held == null)
					{
						// Every voice is still fading out another fall; this one waits for the next choice.
						continue;
					}
					Bind(held, fall);
				}
				held.Wanted = true;
			}
		}

		/// <summary>An unassigned voice, made if there are fewer than the most allowed; null when all are busy.</summary>
		private Voice FreeVoice()
		{
			foreach (Voice voice in voices)
			{
				if (!voice.Assigned)
				{
					return voice;
				}
			}
			// A fall still fading out keeps its voice, so a full set of newcomers can need a few more than MaxFalls.
			if (voices.Count >= MaxFalls * 2)
			{
				return null;
			}
			var made = new Voice
			{
				Roar = MakeEmitter("Waterfall Roar"),
				Splash = MakeEmitter("Waterfall Splash"),
				Lip = MakeEmitter("Waterfall Lip"),
			};
			voices.Add(made);
			return made;
		}

		/// <summary>Points a voice at a fall: where it plays, how loud, how deep, how far, and starts it.</summary>
		private void Bind(Voice voice, in Waterfalls.Fall fall)
		{
			voice.Key = fall.Landing;
			voice.Assigned = true;
			voice.Level = 0f;
			voice.HasLip = fall.Drop >= LipDropMetres;

			float size = Size(fall);
			float loud = Loudness(fall);
			float range = Range(fall);
			float near = Near(fall, range);
			// Bigger is deeper: the roar drops most of an octave from a trickle to a river.
			float roarPitch = Mathf.Lerp(1.2f, 0.62f, size);
			float splashPitch = Mathf.Lerp(1.3f, 0.82f, size);
			float roarShare = Mathf.Lerp(0.25f, 0.9f, size);
			float splashShare = Mathf.Lerp(0.9f, 0.45f, size);
			// A wide fall is a wide source: up close it surrounds the listener rather than sitting at a point.
			float spread = Mathf.Clamp(fall.Width * 3f, 0f, 120f);
			// Each fall starts at its own place in the loop, so two falls of the same size are not in step.
			// Through int: a negative float cast straight to uint is not defined to wrap.
			uint hash = unchecked((uint)(int)(fall.Landing.x * 73f) * 73856093u ^ (uint)(int)(fall.Landing.z * 73f) * 19349663u);

			Configure(voice.Roar, roarClip, fall.Landing, loud * roarShare, roarPitch, near, range, spread, hash);
			Configure(voice.Splash, splashClip, fall.Landing, loud * splashShare, splashPitch, near, range * SplashRangeShare, spread, unchecked(hash * 2654435761u));
			if (voice.HasLip)
			{
				Configure(voice.Lip, splashClip, fall.Lip, loud * LipLoudnessShare, splashPitch * 1.1f, Mathf.Max(1f, 0.5f * fall.Width),
					range * LipRangeShare, spread, unchecked(hash * 40503u));
			}
			else
			{
				// The voice may have been a high fall's before; its lip must not go on playing that fall's sound.
				Stop(voice.Lip);
			}
		}

		private static void Configure(Emitter emitter, AudioClip clip, Vector3 position, float volume, float pitch, float near, float range, float spread, uint offset)
		{
			AudioSource source = emitter.Source;
			emitter.Volume = Mathf.Clamp01(volume);
			emitter.Channel.AuthoredVolume = 0f;
			source.transform.position = position;
			source.clip = clip;
			source.pitch = pitch;
			source.spread = spread;
			source.maxDistance = Mathf.Max(range, near * 2f);
			source.minDistance = near;
			source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff(near / source.maxDistance));
			if (clip != null)
			{
				source.timeSamples = (int)(offset % (uint)clip.samples);
				source.Play();
			}
		}

		/// <summary>
		/// A 1/r fall-off over the range, as a share of it: full within <paramref name="near"/>, then halving with
		/// each doubling of distance, eased to silence over the last third so it ends rather than holds.
		/// </summary>
		public static AnimationCurve Rolloff(float near)
		{
			near = Mathf.Clamp(near, 0.001f, 0.5f);
			var keys = new List<Keyframe> { new Keyframe(0f, 1f), new Keyframe(near, 1f) };
			for (float x = near * 2f; x < 1f; x *= 2f)
			{
				keys.Add(new Keyframe(x, RolloffAt(x, near)));
			}
			keys.Add(new Keyframe(1f, 0f));
			var curve = new AnimationCurve(keys.ToArray());
			for (int i = 0; i < curve.length; i++)
			{
				curve.SmoothTangents(i, 0f);
			}
			return curve;
		}

		private static float RolloffAt(float x, float near)
		{
			float inverse = Mathf.Min(1f, near / Mathf.Max(x, 1e-4f));
			float end = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.66f, 1f, x));
			return inverse * end;
		}

		private static void SetLevel(Emitter emitter, float level)
		{
			if (emitter != null && emitter.Channel != null)
			{
				emitter.Channel.AuthoredVolume = emitter.Volume * level;
			}
		}

		private void Release(Voice voice)
		{
			voice.Assigned = false;
			voice.Wanted = false;
			voice.Level = 0f;
			Stop(voice.Roar);
			Stop(voice.Splash);
			Stop(voice.Lip);
		}

		private static void Stop(Emitter emitter)
		{
			if (emitter != null && emitter.Source != null)
			{
				emitter.Source.Stop();
				emitter.Channel.AuthoredVolume = 0f;
			}
		}

		private Emitter MakeEmitter(string name)
		{
			var go = new GameObject(name);
			go.transform.SetParent(parent, false);
			var source = go.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.loop = true;
			source.spatialBlend = 1f;
			source.dopplerLevel = 0f;
			source.rolloffMode = AudioRolloffMode.Custom;
			source.volume = 0f;
			var channel = go.AddComponent<ChannelAudioSource>();
			channel.SetChannel(AudioChannel.Ambient);
			channel.AuthoredVolume = 0f;
			return new Emitter { Source = source, Channel = channel };
		}

		private void DestroyVoices()
		{
			foreach (Voice voice in voices)
			{
				DestroyEmitter(voice.Roar);
				DestroyEmitter(voice.Splash);
				DestroyEmitter(voice.Lip);
			}
			voices.Clear();
		}

		private static void DestroyEmitter(Emitter emitter)
		{
			if (emitter != null && emitter.Source != null)
			{
				UnityEngine.Object.Destroy(emitter.Source.gameObject);
			}
		}

		/// <summary>Stops every source and destroys them and the synthesised clips.</summary>
		public void Dispose()
		{
			DestroyVoices();
			if (roarClip != null)
			{
				UnityEngine.Object.Destroy(roarClip);
			}
			if (splashClip != null)
			{
				UnityEngine.Object.Destroy(splashClip);
			}
			roarClip = null;
			splashClip = null;
			rankedVersion = int.MinValue;
		}

		// ── The sound itself ──────────────────────────────────────────────

		/// <summary>
		/// A seamless loop of falling water, made from seeded noise.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The roar (<paramref name="bright"/> false) is mostly brown noise — white noise integrated, so its power
		/// falls 6 dB an octave and the low end dominates, the rumble of tonnes of water hitting a pool — with a
		/// third pink noise for body, and a slow surge, as the curtain's load comes and goes.
		/// </para>
		/// <para>
		/// The splash (<paramref name="bright"/> true) is pink noise with the hiss of differenced white noise and a
		/// sparse crackle of droplets: the spray and the water breaking on rock.
		/// </para>
		/// <para>
		/// Seamless by construction: the surge's cycles fit the loop a whole number of times, and the last half
		/// second generated is cross-faded (equal power, as the two halves are unrelated noise) into the first, so
		/// the sample after the loop's end continues the one before it. The same seed makes the same clip on every
		/// machine.
		/// </para>
		/// </remarks>
		public static AudioClip Synthesise(string name, bool bright, uint seed)
		{
			int length = (int)(ClipSeconds * SampleRate);
			int fade = (int)(CrossfadeSeconds * SampleRate);
			int total = length + fade;
			var raw = new float[total];
			uint state = seed == 0u ? 0x9E3779B9u : seed;

			float brown = 0f, b0 = 0f, b1 = 0f, b2 = 0f, previousWhite = 0f, crackle = 0f;
			float dcIn = 0f, dcOut = 0f;
			double surgeA = 2.0 * Math.PI * 3.0 / length, surgeB = 2.0 * Math.PI * 7.0 / length;
			for (int i = 0; i < total; i++)
			{
				float white = Next(ref state) * 2f - 1f;
				// Brown: integrated, leaking slowly so it does not wander off.
				brown = (brown + 0.02f * white) * 0.998f;
				// Pink: Paul Kellett's economy filter, three poles.
				b0 = 0.99765f * b0 + white * 0.0990460f;
				b1 = 0.96300f * b1 + white * 0.2965164f;
				b2 = 0.57000f * b2 + white * 1.0526913f;
				float pink = (b0 + b1 + b2 + white * 0.1848f) * 0.18f;
				float sample;
				if (bright)
				{
					float hiss = (white - previousWhite) * 0.25f;
					if (Next(ref state) < 0.0015f)
					{
						crackle += (Next(ref state) * 2f - 1f) * 0.8f;
					}
					crackle *= 0.975f;
					sample = 0.6f * pink + hiss + crackle;
				}
				else
				{
					sample = 2.6f * brown + 0.35f * pink;
				}
				previousWhite = white;
				// The loop's surge: whole cycles of the loop's length, so it repeats seamlessly.
				float surge = 1f + (float)(0.12 * Math.Sin(surgeA * i) + 0.07 * Math.Sin(surgeB * i + 1.3));
				sample *= bright ? 1f + 0.5f * (surge - 1f) : surge;
				// Blocks any DC the integration leaves, which would only waste headroom.
				dcOut = sample - dcIn + 0.995f * dcOut;
				dcIn = sample;
				raw[i] = dcOut;
			}

			var data = new float[length];
			for (int i = 0; i < length; i++)
			{
				data[i] = raw[i];
			}
			for (int i = 0; i < fade; i++)
			{
				float a = (float)i / fade * (Mathf.PI * 0.5f);
				data[i] = raw[i] * Mathf.Sin(a) + raw[length + i] * Mathf.Cos(a);
			}

			float peak = 1e-6f;
			for (int i = 0; i < length; i++)
			{
				peak = Mathf.Max(peak, Mathf.Abs(data[i]));
			}
			float gain = 0.9f / peak;
			for (int i = 0; i < length; i++)
			{
				data[i] *= gain;
			}

			AudioClip clip = AudioClip.Create(name, length, 1, SampleRate, false);
			clip.SetData(data, 0);
			return clip;
		}

		/// <summary>The next number in [0, 1) from a xorshift generator.</summary>
		private static float Next(ref uint state)
		{
			state ^= state << 13;
			state ^= state >> 17;
			state ^= state << 5;
			return (state & 0xFFFFFFu) / 16777216f;
		}
	}
}
