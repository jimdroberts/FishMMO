using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Plays the weather's loops on the Ambient channel, faded with the weather, and its one-shots
	/// when asked. A cue with no clip is silent.
	/// </summary>
	public sealed class WeatherAudioPresenter
	{
		private readonly Transform parent;
		private readonly Dictionary<WeatherAudioCue, (ChannelAudioSource channel, AudioSource source)> loops = new Dictionary<WeatherAudioCue, (ChannelAudioSource, AudioSource)>();
		private readonly Dictionary<WeatherAudioCue, float> levels = new Dictionary<WeatherAudioCue, float>();
		private WeatherAudioProfile boundProfile;
		private ChannelAudioSource oneShot;

		public WeatherAudioPresenter(Transform parent)
		{
			this.parent = parent;
		}

		/// <summary>How loud a loop should be for a frame, 0..1, before the cue's own volume.</summary>
		public static float TargetVolume(WeatherAudioCue cue, in WeatherFrame frame, float shelter)
		{
			float p = frame[WeatherChannel.Precipitation];
			float rain = p * frame[WeatherChannel.RainWeight];
			float heavy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, rain));
			float wind = frame[WeatherChannel.WindSpeed];
			float strong = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.8f, wind));
			float outdoors = 1f - 0.7f * Mathf.Clamp01(shelter);
			switch (cue)
			{
				case WeatherAudioCue.RainLight: return rain * (1f - heavy) * outdoors;
				case WeatherAudioCue.RainHeavy: return rain * heavy * outdoors;
				case WeatherAudioCue.RainOnRoof: return rain * Mathf.Clamp01(shelter);
				case WeatherAudioCue.Snow: return p * frame[WeatherChannel.SnowWeight] * 0.6f * outdoors;
				case WeatherAudioCue.Hail: return p * frame[WeatherChannel.HailWeight] * (1f - 0.3f * Mathf.Clamp01(shelter));
				case WeatherAudioCue.Wind: return wind * (1f - strong) * outdoors;
				case WeatherAudioCue.WindStrong: return wind * strong * (0.8f + 0.2f * frame[WeatherChannel.WindGust]) * outdoors;
				case WeatherAudioCue.Sand: return p * frame[WeatherChannel.SandWeight] * outdoors;
				case WeatherAudioCue.Ash: return p * frame[WeatherChannel.AshWeight] * 0.5f * outdoors;
				default: return 0f;
			}
		}

		public void Update(in WeatherFrame frame, float shelter, WeatherAudioProfile profile, float deltaTime)
		{
			if (profile != boundProfile)
			{
				Bind(profile);
			}
			if (profile == null)
			{
				return;
			}
			float step = deltaTime / Mathf.Max(0.05f, profile.FadeSeconds);
			foreach (KeyValuePair<WeatherAudioCue, (ChannelAudioSource channel, AudioSource source)> pair in loops)
			{
				WeatherAudioEntry entry = profile.Find(pair.Key);
				float target = TargetVolume(pair.Key, frame, shelter) * (entry != null ? entry.Volume : 0f);
				levels.TryGetValue(pair.Key, out float level);
				level = Mathf.MoveTowards(level, target, step);
				levels[pair.Key] = level;
				pair.Value.channel.AuthoredVolume = level;
				if (level > 0.001f && !pair.Value.source.isPlaying)
				{
					pair.Value.source.Play();
				}
				else if (level <= 0.001f && pair.Value.source.isPlaying)
				{
					pair.Value.source.Stop();
				}
			}
		}

		/// <summary>Plays a one-shot cue (thunder), if it has a clip.</summary>
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
			if (profile == null)
			{
				return;
			}
			foreach (WeatherAudioCue cue in (WeatherAudioCue[])Enum.GetValues(typeof(WeatherAudioCue)))
			{
				WeatherAudioEntry entry = profile.Find(cue);
				if (entry == null || entry.Clip == null || !WeatherAudioProfile.IsLoop(cue))
				{
					continue;
				}
				var go = new GameObject("Weather " + cue);
				go.transform.SetParent(parent, false);
				var source = go.AddComponent<AudioSource>();
				source.clip = entry.Clip;
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
		}
	}
}
