using FishMMO.Shared.Weather;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer.Weather
{
	/// <summary>
	/// Authoritative weather edits for the scenes this server hosts. Every edit is scheduled a
	/// moment ahead (<see cref="WeatherTimeline.LeadSeconds"/>) and broadcast once per frame, so
	/// clients hold it before it takes effect.
	/// </summary>
	/// <remarks>
	/// Weather changes gameplay (exposure buffs, spawns, ability modifiers), so only
	/// administrators and server code may call this. Game masters get read-only reports.
	/// </remarks>
	public interface IWeatherService
	{
		/// <summary>Replaces the scene's layers with a preset's, over a transition.</summary>
		bool ApplyPreset(Scene scene, WeatherPreset preset, float intensity, float transitionSeconds);

		/// <summary>Adds a scene-wide layer. Returns its handle, or 0 when the scene has no weather.</summary>
		ushort AddLayer(Scene scene, WeatherLayerTemplate template, float intensity, float transitionSeconds);

		bool SetLayerIntensity(Scene scene, ushort handle, float intensity, float transitionSeconds);

		/// <summary>Fades a layer out, then forgets it.</summary>
		bool RemoveLayer(Scene scene, ushort handle, float transitionSeconds);

		/// <summary>Fades every scene layer out. Storm cells are left alone.</summary>
		bool ClearLayers(Scene scene, float transitionSeconds);

		/// <summary>Starts a storm cell. Returns its id, or 0.</summary>
		ushort SpawnCell(Scene scene, WeatherPreset preset, Vector3 at, float radiusMeters, Vector2 velocity, float lifetimeSeconds);

		/// <summary>Sends a cell toward a point at a speed (m/s).</summary>
		bool SteerCell(Scene scene, ushort id, Vector3 towards, float speed);

		/// <summary>Fades a cell out over a time.</summary>
		bool RetireCell(Scene scene, ushort id, float fadeSeconds);

		/// <summary>Switches the automatic storm director for a scene.</summary>
		bool SetDirector(Scene scene, bool enabled);

		/// <summary>Moves the scene-wide climate shift to new values over a transition.</summary>
		bool SetClimateOffset(Scene scene, float temperature, float humidity, float transitionSeconds);

		bool TryGetTimeline(Scene scene, out WeatherTimeline timeline);

		bool IsDirectorEnabled(Scene scene);
	}
}
