using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Plays the weather's loops on the Ambient channel, faded with the weather, and its one-shots when asked. A cue the
	/// profile gives no clip plays its synthesised loop (<see cref="AmbientSounds"/>); a cue with neither is silent.
	/// </summary>
	/// <remarks>
	/// Each loop plays at the place in it the shared clock says (<see cref="ClockedLoop"/>), so players in the same
	/// weather hear the same rain. The synthesised clips are made the first time their cue is wanted, off the main thread
	/// (<see cref="SoundLibrary"/>); until one is ready its cue waits silent.
	/// </remarks>
	public sealed class WeatherAudioPresenter
	{
		private readonly Transform parent;
		private readonly Dictionary<WeatherAudioCue, (ChannelAudioSource channel, AudioSource source)> loops = new Dictionary<WeatherAudioCue, (ChannelAudioSource, AudioSource)>();
		private readonly Dictionary<WeatherAudioCue, float> levels = new Dictionary<WeatherAudioCue, float>();
		private WeatherAudioProfile boundProfile;
		private ChannelAudioSource oneShot;
		private bool bound;

		public WeatherAudioPresenter(Transform parent)
		{
			this.parent = parent;
		}

		/// <summary>The normalised wind (1 = 30 m/s) at which the wind is at its full level: 15 m/s, a near gale.</summary>
		public const float FullWind = 0.5f;

		/// <summary>How loud a loop should be for a frame, 0..1, before the cue's own volume.</summary>
		public static float TargetVolume(WeatherAudioCue cue, in WeatherFrame frame, float shelter)
		{
			float p = frame[WeatherChannel.Precipitation];
			// A trace of rain in the field (a few hundredths) is not rain to the ear: silent, then in over a light drizzle.
			float rain = p * frame[WeatherChannel.RainWeight];
			rain *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.08f, rain));
			float heavy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, rain));
			float wind = frame[WeatherChannel.WindSpeed];
			// Wind noise grows faster than the wind does: a 3 m/s breeze is a whisper, a 15 m/s gale fills the ear.
			float blowing = Mathf.Pow(Mathf.Clamp01(wind / FullWind), 1.3f) * (0.85f + 0.3f * frame[WeatherChannel.WindGust]);
			float strong = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.6f, wind));
			float outdoors = 1f - 0.7f * Mathf.Clamp01(shelter);
			switch (cue)
			{
				case WeatherAudioCue.RainLight: return rain * (1f - heavy) * outdoors;
				case WeatherAudioCue.RainHeavy: return rain * heavy * outdoors;
				case WeatherAudioCue.RainOnRoof: return rain * Mathf.Clamp01(shelter);
				case WeatherAudioCue.Snow: return p * frame[WeatherChannel.SnowWeight] * 0.6f * outdoors;
				case WeatherAudioCue.Hail: return p * frame[WeatherChannel.HailWeight] * (1f - 0.3f * Mathf.Clamp01(shelter));
				case WeatherAudioCue.Wind: return blowing * (1f - strong) * outdoors;
				case WeatherAudioCue.WindStrong: return blowing * strong * outdoors;
				case WeatherAudioCue.Sand: return p * frame[WeatherChannel.SandWeight] * outdoors;
				case WeatherAudioCue.Ash: return p * frame[WeatherChannel.AshWeight] * 0.5f * outdoors;
				default: return 0f;
			}
		}

		/// <param name="snap">The weather snapped to another moment (<see cref="WeatherClient.Snaps"/>): every loop goes straight to its level.</param>
		/// <param name="audible">0..1: how much of the weather outside reaches the listener at all (0 sealed deep in a cave).</param>
		public void Update(in WeatherFrame frame, float shelter, WeatherAudioProfile profile, float deltaTime, bool snap = false, float audible = 1f)
		{
			if (!bound || profile != boundProfile)
			{
				Bind(profile);
			}
			float fade = profile != null ? profile.FadeSeconds : 1.5f;
			float step = snap ? float.MaxValue : deltaTime / Mathf.Max(0.05f, fade);
			foreach (KeyValuePair<WeatherAudioCue, (ChannelAudioSource channel, AudioSource source)> pair in loops)
			{
				WeatherAudioCue cue = pair.Key;
				AudioSource source = pair.Value.source;
				WeatherAudioEntry entry = profile != null ? profile.Find(cue) : null;
				float volume = entry != null ? entry.Volume : 1f;
				float target = TargetVolume(cue, frame, shelter) * volume * Mathf.Clamp01(audible);
				levels.TryGetValue(cue, out float level);
				if (source.clip == null)
				{
					// Asked for only once it is wanted: a desert never makes the rain.
					if (target > 0.001f)
					{
						source.clip = ClipFor(cue, entry);
					}
					if (source.clip == null)
					{
						continue;
					}
				}
				level = Mathf.MoveTowards(level, target, step);
				levels[cue] = level;
				pair.Value.channel.AuthoredVolume = level;
				if (level > 0.001f && !source.isPlaying)
				{
					ClockedLoop.Start(source);
				}
				else if (level <= 0.001f && source.isPlaying)
				{
					source.Stop();
				}
				else
				{
					ClockedLoop.Keep(source);
				}
			}
		}

		/// <summary>The profile's clip for a cue, or its synthesised loop (null while that is being made).</summary>
		private static AudioClip ClipFor(WeatherAudioCue cue, WeatherAudioEntry entry)
		{
			if (entry != null && entry.Clip != null)
			{
				return entry.Clip;
			}
			return AmbientSounds.Synthesised(cue) ? SoundLibrary.Get(AmbientSounds.Key(cue), () => AmbientSounds.Make(cue)) : null;
		}

		/// <summary>Plays a one-shot cue, if the profile gives it a clip. Thunder is made per strike instead (<see cref="ThunderPresenter"/>).</summary>
		public void Play(WeatherAudioCue cue, float volume = 1f)
		{
			WeatherAudioEntry entry = boundProfile != null ? boundProfile.Find(cue) : null;
			if (entry == null || entry.Clip == null || oneShot == null)
			{
				return;
			}
			oneShot.GetComponent<AudioSource>().PlayOneShot(entry.Clip, Mathf.Clamp01(volume) * entry.Volume);
		}

		public void Silence()
		{
			foreach (var pair in loops)
			{
				pair.Value.source.Stop();
				pair.Value.channel.AuthoredVolume = 0f;
			}
			levels.Clear();
		}

		private void Bind(WeatherAudioProfile profile)
		{
			Dispose();
			boundProfile = profile;
			bound = true;
			foreach (WeatherAudioCue cue in (WeatherAudioCue[])Enum.GetValues(typeof(WeatherAudioCue)))
			{
				if (!WeatherAudioProfile.IsLoop(cue))
				{
					continue;
				}
				var go = new GameObject("Weather " + cue);
				go.transform.SetParent(parent, false);
				var source = go.AddComponent<AudioSource>();
				WeatherAudioEntry entry = profile != null ? profile.Find(cue) : null;
				// The profile's own clip at once; a synthesised one when the cue is first wanted.
				source.clip = entry != null ? entry.Clip : null;
				source.loop = true;
				source.playOnAwake = false;
				source.spatialBlend = 0f;
				source.volume = 0f;
				var channel = go.AddComponent<ChannelAudioSource>();
				channel.SetChannel(AudioChannel.Ambient);
				channel.AuthoredVolume = 0f;
				loops[cue] = (channel, source);
			}
			var shotObject = new GameObject("Weather One-Shots");
			shotObject.transform.SetParent(parent, false);
			var shotSource = shotObject.AddComponent<AudioSource>();
			shotSource.playOnAwake = false;
			shotSource.spatialBlend = 0f;
			shotSource.volume = 1f;
			oneShot = shotObject.AddComponent<ChannelAudioSource>();
			oneShot.SetChannel(AudioChannel.Ambient);
			oneShot.AuthoredVolume = 1f;
		}

		public void Dispose()
		{
			foreach (var pair in loops)
			{
				if (pair.Value.channel != null)
				{
					UnityEngine.Object.Destroy(pair.Value.channel.gameObject);
				}
			}
			loops.Clear();
			levels.Clear();
			if (oneShot != null)
			{
				UnityEngine.Object.Destroy(oneShot.gameObject);
				oneShot = null;
			}
			boundProfile = null;
			bound = false;
		}
	}
}
