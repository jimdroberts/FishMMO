using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Something another module draws with the weather, every frame the weather is drawn, from what the weather shows: the
	/// Ambient Life module's air motes (dust, pollen, diamond dust, fireflies, wisps).
	/// </summary>
	/// <remarks>Each <see cref="WeatherPresentation"/> makes its own from the registered factory and disposes it with itself.</remarks>
	public interface IWeatherOverlay : IDisposable
	{
		/// <summary>Draws for the camera the weather draws for, after the precipitation.</summary>
		/// <param name="shown">The weather being shown (eased toward the forecast).</param>
		/// <param name="hasContext">Whether <paramref name="context"/> holds a sample (none before the first forecast).</param>
		/// <param name="occlusion">The sky-occlusion map round the camera, or null.</param>
		void Render(Camera camera, WeatherTierSettings tier, in WeatherFrame shown, bool hasContext, in WeatherContext context, SkyOcclusionMap occlusion);
	}

	/// <summary>
	/// The overlays modules add to the weather's presentation (<see cref="IWeatherOverlay"/>), by id (Phase 4 of the core and
	/// modules plan, 2026-11-17). The weather names none of them.
	/// </summary>
	public static class WeatherOverlays
	{
		/// <summary>One registered overlay: its id and how to make one.</summary>
		public readonly struct Entry
		{
			public readonly string Id;
			public readonly Func<IWeatherOverlay> Create;

			public Entry(string id, Func<IWeatherOverlay> create)
			{
				Id = id;
				Create = create;
			}
		}

		private static readonly List<Entry> entries = new List<Entry>();

		/// <summary>The registered overlays, in registration order.</summary>
		public static IReadOnlyList<Entry> Entries => entries;

		/// <summary>Registers an overlay; registering an id again replaces it (a module's installer runs on every domain load).</summary>
		public static void Register(string id, Func<IWeatherOverlay> create)
		{
			if (string.IsNullOrEmpty(id) || create == null)
			{
				return;
			}
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Id == id)
				{
					entries[i] = new Entry(id, create);
					return;
				}
			}
			entries.Add(new Entry(id, create));
		}

		/// <summary>Removes an overlay by id. Presentations already holding one keep it until they are destroyed.</summary>
		public static void Unregister(string id)
		{
			entries.RemoveAll(e => e.Id == id);
		}
	}
}
