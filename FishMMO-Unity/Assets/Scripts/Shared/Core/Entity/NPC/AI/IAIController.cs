using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Composite interface for AI controllers managing NPC navigation, state transitions, and movement logic.
	/// Inherits from IAINavigation, IAIStateMachine, and IAIWaypoints for interface segregation.
	/// Consumers that only need navigation, state management, or waypoints should depend on
	/// the corresponding sub-interface instead.
	/// </summary>
	public interface IAIController : IAINavigation, IAIStateMachine, IAIWaypoints
	{
		/// <summary>
		/// The home position for this AI (used for leash and wandering).
		/// </summary>
		Vector3 Home { get; set; }

		/// <summary>
		/// The current target for the AI (e.g., enemy, destination).
		/// </summary>
		Transform Target { get; set; }

		/// <summary>
		/// The world-space point this NPC's abilities fire from, and the point it sees from.
		/// </summary>
		/// <remarks>
		/// Derived, never replicated — only the aim <em>direction</em> travels, and every peer
		/// re-derives the origin from the same state. See <see cref="CharacterAimOrigin"/>.
		/// </remarks>
		Vector3 AimOrigin { get; }

		/// <summary>
		/// The rotation whose forward vector is the direction this NPC aims.
		/// </summary>
		/// <remarks>
		/// The AI counterpart of <see cref="KCCController.VirtualCameraRotation"/>. A player aims
		/// with a real camera; an NPC has none, so its aim is solved toward its current target and
		/// refreshed on every network tick.
		/// </remarks>
		Quaternion AimRotation { get; }

		/// <summary>
		/// Initializes the controller with a home position and optional waypoints.
		/// </summary>
		/// <param name="home">The home position for the AI.</param>
		/// <param name="waypoints">Optional waypoints for patrol.</param>
		void Initialize(Vector3 home, Vector3[] waypoints = null);
	}
}