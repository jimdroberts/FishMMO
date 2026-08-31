using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for a character's target controller, handling targeting logic and events.
	/// </summary>
	public interface ITargetController : ICharacterBehaviour
	{
		/// <summary>
		/// Event triggered when the target changes.
		/// </summary>
		event Action<Transform> OnChangeTarget;
		/// <summary>
		/// Event triggered when the target is updated.
		/// </summary>
		event Action<Transform> OnUpdateTarget;
		/// <summary>
		/// Event triggered when the target is cleared.
		/// </summary>
		event Action<Transform> OnClearTarget;

		/// <summary>
		/// The current target information.
		/// </summary>
		TargetInfo Current { get; }
		/// <summary>
		/// Updates the target based on the given origin, direction, and max distance.
		/// </summary>
		TargetInfo UpdateTarget(Vector3 origin, Vector3 direction, float maxDistance);

		/// <summary>
		/// SERVER-side view of the owning client's reported target frame:
		/// the NetworkObject id the player is looking at, or 0. Advisory — feeds interest
		/// management, never combat resolution. See <c>TargetSelectionBroadcast</c>.
		/// </summary>
		int ClientSelectedTargetObjectId { get; }

		/// <summary>
		/// True once any client target report has been accepted for this character. Distinguishes
		/// "the player reports no target" (authoritative 0) from "this character has no reporting
		/// client at all" (an NPC, or an old client), where readers fall back to the cast-scoped
		/// <see cref="Current"/>.
		/// </summary>
		bool HasClientSelectedTarget { get; }

		/// <summary>
		/// Installs a VERIFIED client target report. Server only; callers must have validated the
		/// id resolves to a live character in the reporting client's own scene (or pass 0).
		/// </summary>
		void ServerSetClientSelectedTarget(int targetObjectId);

		/// <summary>
		/// Triggers invoked when the target changes to a new target.
		/// </summary>
		List<Trigger> OnTargetChangeTriggers { get; }
		/// <summary>
		/// Triggers invoked when the current target is cleared.
		/// </summary>
		List<Trigger> OnTargetClearTriggers { get; }
	}
}