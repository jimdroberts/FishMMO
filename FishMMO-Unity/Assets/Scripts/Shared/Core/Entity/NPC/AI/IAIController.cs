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
		/// The simulated camera position for ability targeting.
		/// Mirrors <see cref="KCCController.VirtualCameraPosition"/> for player characters.
		/// </summary>
		Vector3 VirtualCameraPosition { get; }

		/// <summary>
		/// The simulated camera rotation for ability targeting.
		/// Mirrors <see cref="KCCController.VirtualCameraRotation"/> for player characters.
		/// </summary>
		Quaternion VirtualCameraRotation { get; }

		/// <summary>
		/// Initializes the controller with a home position and optional waypoints.
		/// </summary>
		/// <param name="home">The home position for the AI.</param>
		/// <param name="waypoints">Optional waypoints for patrol.</param>
		void Initialize(Vector3 home, Vector3[] waypoints = null);
	}
}