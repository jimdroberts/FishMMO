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
		/// Triggers invoked when the target changes to a new target.
		/// </summary>
		List<Trigger> OnTargetChangeTriggers { get; }
		/// <summary>
		/// Triggers invoked when the current target is cleared.
		/// </summary>
		List<Trigger> OnTargetClearTriggers { get; }
	}
}