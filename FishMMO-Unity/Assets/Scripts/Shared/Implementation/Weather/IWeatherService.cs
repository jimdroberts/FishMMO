using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Authoritative weather edits for the scenes this server hosts. Every edit is scheduled a
	/// moment ahead (<see cref="WeatherTimeline.LeadSeconds"/>) and broadcast once per frame, so
	/// clients hold it before it takes effect.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Weather changes gameplay (exposure buffs, spawns, ability modifiers), so only
	/// administrators and server code may call this. Game masters get read-only reports.
	/// </para>
	/// <para>
	/// <b>Declared here rather than beside its implementation.</b> The only implementation is the
	/// scene server's <c>WeatherHost</c> and always will be — but the ECA actions that drive it are
	/// shared content, authored on triggers that both peers load, and shared code cannot reference
	/// the server assembly. So the interface lives here and the server registers its host into
	/// <see cref="WeatherQuery.Commands"/> at startup, exactly as it already does for
	/// <see cref="WeatherQuery.TickSource"/>. On a client that property is simply null, which is
	/// the same answer the authority check would have given anyway.
	/// </para>
	/// </remarks>
	public interface IWeatherService
	{
		/// <summary>
		/// Moves what is added to the scene's air to new values over a transition. Additions, never a
		/// replacement: all zero hands the scene back to its air as it is.
		/// </summary>
		bool SetAirOffsets(Scene scene, AirOffsets offsets, float transitionSeconds);

		/// <summary>What is currently added to the scene's air at runtime (the authored offsets not included).</summary>
		bool TryGetAirOffsets(Scene scene, out AirOffsets offsets);

		/// <summary>Starts a storm of a kind. Returns its id, or 0.</summary>
		/// <param name="radiusMeters">Its size; 0 or less lets the air there decide.</param>
		/// <param name="lifetimeSeconds">How long it lasts; 0 or less lets the air there decide.</param>
		ushort SpawnCell(Scene scene, StormKind kind, Vector3 at, float radiusMeters, Vector2 velocity, float lifetimeSeconds);

		/// <summary>Sends a cell toward a point at a speed (m/s).</summary>
		bool SteerCell(Scene scene, ushort id, Vector3 towards, float speed);

		/// <summary>Fades a cell out over a time.</summary>
		bool RetireCell(Scene scene, ushort id, float fadeSeconds);

		/// <summary>Switches the automatic storm director for a scene.</summary>
		bool SetDirector(Scene scene, bool enabled);

		bool TryGetTimeline(Scene scene, out WeatherTimeline timeline);

		bool IsDirectorEnabled(Scene scene);
	}
}
