using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>The sounds weather can make. Loops fade with the weather; one-shots are raised as events.</summary>
	public enum WeatherAudioCue
	{
		RainLight = 0,
		RainHeavy = 1,
		RainOnRoof = 2,
		Snow = 3,
		Hail = 4,
		Wind = 5,
		WindStrong = 6,
		Sand = 7,
		Ash = 8,
		ThunderNear = 9,
		ThunderFar = 10,
	}

	/// <summary>One cue: an empty clip means silence, never an error.</summary>
	[Serializable]
	public class WeatherAudioEntry
	{
		public WeatherAudioCue Cue;
		public AudioClip Clip;
		[Range(0f, 1f)] public float Volume = 1f;
	}

	/// <summary>
	/// The weather cue table. Ships with every cue listed and no clips; assigning a clip is all it
	/// takes to hear it.
	/// </summary>
	[CreateAssetMenu(fileName = "Weather Audio Profile", menuName = "FishMMO/Weather/Audio Profile", order = 21)]
	public class WeatherAudioProfile : CachedScriptableObject<WeatherAudioProfile>, ICachedObject
	{
		[Tooltip("Seconds a loop takes to fade to a new level.")]
		[Min(0.05f)] public float FadeSeconds = 1.5f;
		public List<WeatherAudioEntry> Entries = AllCues();

		public static List<WeatherAudioEntry> AllCues()
		{
			var list = new List<WeatherAudioEntry>();
			foreach (WeatherAudioCue cue in (WeatherAudioCue[])Enum.GetValues(typeof(WeatherAudioCue)))
			{
				list.Add(new WeatherAudioEntry { Cue = cue });
			}
			return list;
		}

		public WeatherAudioEntry Find(WeatherAudioCue cue)
		{
			foreach (WeatherAudioEntry entry in Entries)
			{
				if (entry != null && entry.Cue == cue)
				{
					return entry;
				}
			}
			return null;
		}

		public static bool IsLoop(WeatherAudioCue cue) => cue != WeatherAudioCue.ThunderNear && cue != WeatherAudioCue.ThunderFar;
	}
}
