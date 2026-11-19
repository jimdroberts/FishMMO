using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The weather at a place and tick, for gameplay: the core's question, answered by whichever weather is installed.
	/// </summary>
	/// <remarks>
	/// Core (Phase 4 of the core and modules plan, 2026-11-17). The Climate and Weather module installs
	/// <see cref="Sampler"/> as it loads (its <c>WeatherTimelines</c>); without it every place reads
	/// <see cref="WeatherReading.Calm"/>. The storm timelines, cells and weather commands are the module's.
	/// </remarks>
	public static class WeatherQuery
	{
		/// <summary>The server tick "now" means; forwards to <see cref="WorldTick"/>.</summary>
		public static Func<uint> TickSource
		{
			get => WorldTick.Source;
			set => WorldTick.Source = value;
		}

		/// <summary>The current tick.</summary>
		public static uint CurrentTick => WorldTick.Current;

		/// <summary>The installed weather's answer for (scene, position, tick), or null for none.</summary>
		public static Func<Scene, Vector3, uint, WeatherReading> Sampler;

		/// <summary>Whether a scene has weather at all (a timeline); installed by the weather with <see cref="Sampler"/>.</summary>
		public static Func<Scene, bool> SceneHasWeather;

		/// <summary>Whether <paramref name="scene"/> has weather; false when no weather is installed.</summary>
		public static bool HasWeather(Scene scene) => SceneHasWeather != null && SceneHasWeather(scene);

		/// <summary>The weather at <paramref name="position"/> now.</summary>
		public static WeatherReading Sample(Scene scene, Vector3 position) => Sample(scene, position, CurrentTick);

		/// <summary>The weather at <paramref name="position"/> at <paramref name="tick"/>; calm when no weather is installed.</summary>
		public static WeatherReading Sample(Scene scene, Vector3 position, uint tick)
		{
			Func<Scene, Vector3, uint, WeatherReading> sampler = Sampler;
			return sampler != null ? sampler(scene, position, tick) : WeatherReading.Calm;
		}
	}
}
