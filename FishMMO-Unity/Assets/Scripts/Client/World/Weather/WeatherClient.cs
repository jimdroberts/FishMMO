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
		/// <summary>How much of it the drifting field decided (1) against a preset or layer (0).</summary>
		public float DriverWeight;
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

		/// <summary>
		/// When set, presenters are given this instead of the real weather. For the test scene and
		/// photo mode; it never leaves the client and changes nothing on the server.
		/// </summary>
		public static WeatherFrame? LocalPreview;

		/// <summary>The most recent frame at the viewer.</summary>
		public static WeatherFrame LastFrame { get; internal set; }

		/// <summary>The most recent context.</summary>
		public static WeatherContext LastContext { get; internal set; }

		public static IReadOnlyList<IWeatherPresenter> Presenters => presenters;

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
			LastFrame = frame;
			LastContext = context;
			WeatherFrame shown = LocalPreview ?? frame;
			for (int i = presenters.Count - 1; i >= 0; i--)
			{
				try
				{
					presenters[i].Apply(shown, context);
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
