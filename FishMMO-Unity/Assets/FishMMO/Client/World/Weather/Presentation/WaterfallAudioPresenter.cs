using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Celestial;
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

		/// <summary>The rate the falls' loops are made at.</summary>
		public const int LoopSampleRate = SampleRate;

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
			// Each fall at its own place in the loop, so two falls of the same size are not in step; and every player's at
			// the same place, as the shared clock has it (Configure).
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
				// Where the shared clock has this fall's loop now: the same moment of its roar for every player.
				long along = (long)WorldMotion.Repeat(WorldMotion.Seconds * clip.frequency * pitch, clip.samples);
				source.timeSamples = (int)((offset % (uint)clip.samples + along) % clip.samples);
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
		/// A seamless loop of falling water, made from seeded noise (<see cref="SynthesiseSamples"/>).
		/// </summary>
		/// <remarks>
		/// <para>
		/// The roar (<paramref name="bright"/> false) is the larger falls' sound as recorded: centred at 600–800 Hz, little
		/// below 125, swelling a little as the curtain's load comes and goes. The splash (<paramref name="bright"/> true) is
		/// a small fall's or the white water's: centred at 2–3 kHz, full of bubbles.
		/// </para>
		/// <para>
		/// Seamless by construction (<see cref="AmbientSounds.Make(AmbientSounds.Bed, uint)"/>): made in the frequency
		/// domain, so it is periodic. The same seed makes the same clip on every machine.
		/// </para>
		/// </remarks>
		public static AudioClip Synthesise(string name, bool bright, uint seed)
		{
			float[] data = SynthesiseSamples(bright, seed);
			AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
			clip.SetData(data, 0);
			return clip;
		}

		/// <summary>
		/// The samples <see cref="Synthesise"/> makes its clip from: the falls' bed (<see cref="AmbientSounds.Falls"/>,
		/// fitted to recordings of falls and brooks), a periodic loop of <see cref="AmbientSounds.LoopSamples"/>. Pure arithmetic, at
		/// <see cref="LoopSampleRate"/>. It was brown noise for the roar and white hiss for the splash: the recorded falls
		/// have little below 125 Hz, and a small fall's or a brook's splash is bubbles centred at 2–3 kHz, not hiss at 11.
		/// </summary>
		public static float[] SynthesiseSamples(bool bright, uint seed)
		{
			return AmbientSounds.Make(AmbientSounds.Falls(bright), seed);
		}
	}
}
