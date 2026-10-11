using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>What a presenter is told about the moment it is drawing.</summary>
	public struct WeatherContext
	{
		public Scene Scene;
		public WorldSceneSettings Settings;
		public WeatherTimeline Timeline;
		public Vector3 ViewerPosition;
		/// <summary>Fractional server tick the frame was sampled at.</summary>
		public double Tick;
		/// <summary>Real hours since the calendar epoch.</summary>
		public double WorldHours;
		/// <summary>0.5 is noon in this scene.</summary>
		public double LocalTime01;
		public bool IsDaylight;
		public WeatherCover Cover;
		/// <summary>The weather without the storm cells: what the sky shows in the far distance.</summary>
		public WeatherFrame Background;
		/// <summary>
		/// Everything the weather at the viewer was worked out from: the air, its column, the world's
		/// air and what was added to it. The sky draws its clouds from the same physics.
		/// </summary>
		public WeatherSample Sample;
		public float Shelter;
		/// <summary>Local temperature at the viewer, -1..1.</summary>
		public float Temperature;

		/// <summary>
		/// What is falling at the viewer, or null for the kinds' own defaults. Resolved from the
		/// timeline both peers hold, so it costs nothing on the wire.
		/// </summary>
		public WeatherSubstance Substance;
	}

	/// <summary>
	/// Something that shows the weather: the built-in URP presenter (later phases), weather audio,
	/// or an adapter for an external sky package. Called once per frame on the main thread.
	/// </summary>
	public interface IWeatherPresenter
	{
		/// <summary>The weather at the viewer, already blended, re-typed and shaped by volumes.</summary>
		void Apply(in WeatherFrame frame, in WeatherContext context);

		/// <summary>The scene changed or the client disconnected: drop anything scene-bound.</summary>
		void Reset();
	}

	/// <summary>Client-side entry points for weather presentation.</summary>
	public static class WeatherClient
	{
		private static readonly List<IWeatherPresenter> presenters = new List<IWeatherPresenter>();

		/// <summary>The most recent frame at the viewer.</summary>
		public static WeatherFrame LastFrame { get; internal set; }

		/// <summary>The most recent context.</summary>
		public static WeatherContext LastContext { get; internal set; }

		public static IReadOnlyList<IWeatherPresenter> Presenters => presenters;

		/// <summary>
		/// Counts the times the weather must be SHOWN as it is, not eased into: the world clock set to another moment
		/// (<see cref="FishMMO.Shared.Celestial.WorldClock.Jumps"/>, noticed on the first <see cref="Present"/> after
		/// it), or another scene's weather taking over (<see cref="Snap"/>). Whatever eases, blends, builds over frames
		/// or keeps history compares this with the count it last saw, and on a change shows the new moment at once.
		/// </summary>
		public static uint Snaps { get; private set; }

		private static uint seenJumps;

		/// <summary>Asks every presenter to show the next weather as it is (see <see cref="Snaps"/>).</summary>
		public static void Snap() => Snaps++;

		/// <summary>A one-shot weather sound (thunder) with its volume. Presenters with audio play it.</summary>
		public static event Action<WeatherAudioCue, float> AudioCue;

		/// <summary>Asks the audio presenters to play a one-shot cue.</summary>
		public static void RaiseAudioCue(WeatherAudioCue cue, float volume = 1f) => AudioCue?.Invoke(cue, volume);

		public static void RegisterPresenter(IWeatherPresenter presenter)
		{
			if (presenter != null && !presenters.Contains(presenter))
			{
				presenters.Add(presenter);
			}
		}

		public static void UnregisterPresenter(IWeatherPresenter presenter) => presenters.Remove(presenter);

		public static void Present(in WeatherFrame frame, in WeatherContext context)
		{
			uint jumps = FishMMO.Shared.Celestial.WorldClock.Shared.Jumps;
			if (jumps != seenJumps)
			{
				seenJumps = jumps;
				Snaps++;
			}
			LastFrame = frame;
			LastContext = context;
			for (int i = presenters.Count - 1; i >= 0; i--)
			{
				try
				{
					presenters[i].Apply(frame, context);
				}
				catch (Exception ex)
				{
					FishMMO.Logging.Log.Error("WeatherClient", $"{presenters[i].GetType().Name} failed: {ex}");
				}
			}
		}

		public static void ResetPresenters()
		{
			for (int i = presenters.Count - 1; i >= 0; i--)
			{
				presenters[i].Reset();
			}
		}
	}
}
