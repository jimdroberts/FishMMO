using System;
using UnityEngine;

namespace FishMMO.Shared
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
	}
}