using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Read-only access to weather from anywhere: AI, spawners, buffs, UI. The server registers the
	/// timelines it owns; the client registers the one it mirrors. Both answer the same question
	/// the same way.
	/// </summary>
	public static class WeatherQuery
	{
		private static readonly Dictionary<int, WeatherTimeline> timelines = new Dictionary<int, WeatherTimeline>();

		/// <summary>The server tick "now" means. Set by the server host or the client mirror.</summary>
		public static Func<uint> TickSource;

		/// <summary>
		/// Authoritative weather edits, or null on a peer that has no business making them.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The scene server sets this to its <c>WeatherHost</c> at startup, the same way it sets
		/// <see cref="TickSource"/>. It exists so shared content — the ECA actions a designer puts
		/// on a trigger, which both peers load — can reach a service that only the server assembly
		/// implements.
		/// </para>
		/// <para>
		/// <b>Null is not a failure to handle specially.</b> On a client it is always null, and a
		/// weather action that finds it so does nothing — which is exactly what the server-authority
		/// check in front of it would have decided in any case. The two agree, and the property is
		/// the cheaper of the two to ask.
		/// </para>
		/// </remarks>
		public static IWeatherService Commands;

		public static uint CurrentTick => TickSource != null ? TickSource() : 0u;

		public static void Register(Scene scene, WeatherTimeline timeline)
		{
			if (timeline != null)
			{
				timelines[scene.handle] = timeline;
			}
		}

		public static void Unregister(Scene scene) => timelines.Remove(scene.handle);

		public static void Clear()
		{
			timelines.Clear();
			// The service belongs to the host that registered it; a teardown that left a dead one
			// behind would have shared content calling into a server that has stopped.
			Commands = null;
		}

		public static bool TryGetTimeline(Scene scene, out WeatherTimeline timeline) => timelines.TryGetValue(scene.handle, out timeline);

		/// <summary>The weather at a position now.</summary>
		public static WeatherSample Sample(Scene scene, Vector3 position) => Sample(scene, position, CurrentTick);

		public static WeatherSample Sample(Scene scene, Vector3 position, uint tick)
		{
			TryGetTimeline(scene, out WeatherTimeline timeline);
			WorldSceneSettings.TryGetForScene(scene, out WorldSceneSettings settings);
			return WeatherField.Sample(timeline, settings, scene, position, tick);
		}

		/// <summary>Cells whose centre is within <paramref name="radius"/> metres, nearest first.</summary>
		public static void CellsWithin(Scene scene, Vector3 position, float radius, List<StormCell> results)
		{
			results.Clear();
			if (!TryGetTimeline(scene, out WeatherTimeline timeline))
			{
				return;
			}
			uint tick = CurrentTick;
			var here = new Vector2(position.x, position.z);
			foreach (StormCell cell in timeline.Cells)
			{
				if (cell.EnvelopeAt(tick) <= 0f)
				{
					continue;
				}
				if (Vector2.Distance(here, cell.CentreAt(tick, timeline.TickDelta)) - cell.RadiusMeters <= radius)
				{
					results.Add(cell);
				}
			}
			results.Sort((a, b) =>
				Vector2.Distance(here, a.CentreAt(tick, timeline.TickDelta)).CompareTo(Vector2.Distance(here, b.CentreAt(tick, timeline.TickDelta))));
		}
	}

	/// <summary>Weather notifications, raised on whichever side owns or mirrors the timeline.</summary>
	public static class WeatherEvents
	{
		/// <summary>A timeline was replaced or changed (any revision).</summary>
		public static event Action<Scene, WeatherTimeline> TimelineChanged;
		public static event Action<Scene, StormCell> CellSpawned;
		public static event Action<Scene, ushort> CellRetired;

		public static void RaiseTimelineChanged(Scene scene, WeatherTimeline timeline) => TimelineChanged?.Invoke(scene, timeline);
		public static void RaiseCellSpawned(Scene scene, StormCell cell) => CellSpawned?.Invoke(scene, cell);
		public static void RaiseCellRetired(Scene scene, ushort id) => CellRetired?.Invoke(scene, id);
	}
}
