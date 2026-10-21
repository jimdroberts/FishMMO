using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A pool of positioned one-shot sources: footsteps, splashes, echoes, thunder. Each play is a clip at a point with
	/// the loudness already worked out, so the pool's sources apply no distance curve of their own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Loudness is the caller's.</b> Unity's rolloff curves end at the source's own maximum distance and are set per
	/// source; a footstep, its echo off a cliff and a thunderclap twelve kilometres off all want the same law (spherical
	/// spreading, 1/r) over very different ranges. So every source here plays flat, and the caller passes the gain for
	/// where the listener is: the direction still comes from the source's position (spatial blend 1), the level from
	/// physics. A one-shot lasts a second or two; the listener does not move far enough in that time to matter.
	/// </para>
	/// <para>
	/// <b>A start can be scheduled</b> on the audio clock (<see cref="AudioSettings.dspTime"/>), to the sample: an echo
	/// arrives so many milliseconds after its sound, and a sound already under way (thunder heard by a player who arrived
	/// mid-rumble) starts part of the way in.
	/// </para>
	/// <para>
	/// When every source is busy the oldest gives way: the newest sounds are the ones nearest in time to what the player
	/// is doing.
	/// </para>
	/// </remarks>
	public sealed class OneShotVoices : IDisposable
	{
		private sealed class Voice
		{
			public AudioSource Source;
			public ChannelAudioSource Channel;
			public AudioLowPassFilter LowPass;
			public double EndsAt;
			public double StartedAt;
			public float Gain;
		}

		private readonly Transform parent;
		private readonly string name;
		private readonly AudioChannel channel;
		private readonly int capacity;
		private readonly List<Voice> voices = new List<Voice>();
		private static readonly AnimationCurve Flat = AnimationCurve.Constant(0f, 1f, 1f);

		/// <param name="capacity">The most sources that play at once.</param>
		public OneShotVoices(Transform parent, string name, AudioChannel channel, int capacity)
		{
			this.parent = parent;
			this.name = name;
			this.channel = channel;
			this.capacity = Math.Max(1, capacity);
		}

		/// <summary>Whether air absorption can be applied (a low-pass per source): not on WebGL, which has no audio filters.</summary>
		public static bool Filters
		{
			get
			{
#if UNITY_WEBGL && !UNITY_EDITOR
				return false;
#else
				return true;
#endif
			}
		}

		/// <summary>
		/// Plays a clip at a point.
		/// </summary>
		/// <param name="gain">How loud, 0..1, distance included; the player's channel level is applied on top.</param>
		/// <param name="delaySeconds">Seconds from now to start; negative starts that far into the clip.</param>
		/// <param name="lowPassHz">The air's absorption over the path as a low-pass cut-off; 0 or above 20 kHz for none.</param>
		/// <param name="spread">Degrees the sound is spread over: 0 a point, larger for a big source near (a long bolt).</param>
		/// <param name="spatial">1 heard from its point, 0 heard everywhere alike.</param>
		public bool Play(AudioClip clip, Vector3 position, float gain, float pitch = 1f, double delaySeconds = 0.0, float lowPassHz = 0f,
			float spread = 0f, float spatial = 1f)
		{
			if (clip == null || gain <= 0.0005f)
			{
				return false;
			}
			pitch = Mathf.Clamp(pitch, 0.25f, 3f);
			double length = clip.length / pitch;
			if (delaySeconds < 0.0 && -delaySeconds >= length)
			{
				// Already over: nothing of it is left to hear.
				return false;
			}
			Voice voice = Take(gain);
			if (voice == null)
			{
				return false;
			}
			double now = AudioSettings.dspTime;
			AudioSource source = voice.Source;
			source.Stop();
			source.transform.position = position;
			source.clip = clip;
			source.pitch = pitch;
			source.spread = Mathf.Clamp(spread, 0f, 360f);
			source.spatialBlend = Mathf.Clamp01(spatial);
			voice.Channel.AuthoredVolume = Mathf.Clamp01(gain);
			voice.Gain = gain;
			if (voice.LowPass != null)
			{
				bool on = lowPassHz > 10f && lowPassHz < 20000f;
				voice.LowPass.enabled = on;
				if (on)
				{
					voice.LowPass.cutoffFrequency = lowPassHz;
				}
			}
			if (delaySeconds > 0.0)
			{
				source.timeSamples = 0;
				source.PlayScheduled(now + delaySeconds);
				voice.StartedAt = now + delaySeconds;
				voice.EndsAt = now + delaySeconds + length;
			}
			else
			{
				double into = -delaySeconds * pitch;
				source.timeSamples = Mathf.Clamp((int)(into * clip.frequency), 0, Math.Max(0, clip.samples - 1));
				source.Play();
				voice.StartedAt = now;
				voice.EndsAt = now + length + delaySeconds;
			}
			return true;
		}

		/// <summary>How many sources are playing or waiting to.</summary>
		public int Busy
		{
			get
			{
				double now = AudioSettings.dspTime;
				int busy = 0;
				foreach (Voice voice in voices)
				{
					if (voice.EndsAt > now)
					{
						busy++;
					}
				}
				return busy;
			}
		}

		/// <summary>Stops everything (a scene change, the world's sound going away).</summary>
		public void StopAll()
		{
			foreach (Voice voice in voices)
			{
				if (voice.Source != null)
				{
					voice.Source.Stop();
				}
				voice.EndsAt = 0.0;
			}
		}

		/// <summary>
		/// A free source, or a new one, or the quietest one playing (the oldest of equals) if it is quieter than the sound
		/// that wants it: a far rumble gives way to a near clap, never the other way round. Null when all are louder.
		/// </summary>
		private Voice Take(float gain)
		{
			double now = AudioSettings.dspTime;
			Voice quietest = null;
			foreach (Voice voice in voices)
			{
				if (voice.Source == null)
				{
					continue;
				}
				if (voice.EndsAt <= now)
				{
					return voice;
				}
				if (quietest == null || voice.Gain < quietest.Gain || voice.Gain == quietest.Gain && voice.StartedAt < quietest.StartedAt)
				{
					quietest = voice;
				}
			}
			if (voices.Count < capacity)
			{
				return Make();
			}
			return quietest != null && quietest.Gain <= gain ? quietest : null;
		}

		private Voice Make()
		{
			var go = new GameObject(name + " " + voices.Count);
			go.transform.SetParent(parent, false);
			AudioSource source = go.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.loop = false;
			source.spatialBlend = 1f;
			source.dopplerLevel = 0f;
			source.rolloffMode = AudioRolloffMode.Custom;
			source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Flat);
			source.minDistance = 1f;
			source.maxDistance = 100000f;
			source.volume = 0f;
			AudioLowPassFilter lowPass = null;
			if (Filters)
			{
				lowPass = go.AddComponent<AudioLowPassFilter>();
				lowPass.lowpassResonanceQ = 1f;
				lowPass.enabled = false;
			}
			ChannelAudioSource level = go.AddComponent<ChannelAudioSource>();
			level.SetChannel(channel);
			level.AuthoredVolume = 0f;
			var voice = new Voice { Source = source, Channel = level, LowPass = lowPass };
			voices.Add(voice);
			return voice;
		}

		public void Dispose()
		{
			foreach (Voice voice in voices)
			{
				if (voice.Source != null)
				{
					UnityEngine.Object.Destroy(voice.Source.gameObject);
				}
			}
			voices.Clear();
		}

		/// <summary>
		/// Spherical spreading: the gain at <paramref name="distance"/> of a sound whose level is 1 at <paramref name="near"/>
		/// and nearer, eased to silence over the last third of <paramref name="range"/> so a sound ends rather than holds.
		/// </summary>
		public static float Spreading(float distance, float near, float range)
		{
			float inverse = Mathf.Min(1f, Mathf.Max(near, 0.01f) / Mathf.Max(distance, 1e-3f));
			float end = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(range * 0.66f, range, distance));
			return inverse * end;
		}

		/// <summary>
		/// The air's absorption as a low-pass cut-off over a path: high frequencies are lost first and faster with
		/// distance (ISO 9613-1: about 10 dB/km at 2 kHz, 100 dB/km at 8 kHz in mild, humid air). Above 20 kHz near.
		/// </summary>
		public static float AirCutoff(float distance)
		{
			if (distance < 30f)
			{
				return 22000f;
			}
			// Where the loss reaches about 6 dB: ~8 kHz at 60 m, ~2 kHz at 600 m, a few hundred hertz past 5 km.
			return Mathf.Clamp(22000f / Mathf.Pow(distance / 20f, 0.62f), 120f, 22000f);
		}
	}
}
