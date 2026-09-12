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
		/// Event triggered on the owning client when a target is pinned.
		/// </summary>
		event Action<Transform> OnPinTarget;
		/// <summary>
		/// Event triggered on the owning client when the pinned target is released. The argument
		/// is the released transform, or null when it was destroyed out from under the pin.
		/// </summary>
		event Action<Transform> OnUnpinTarget;

		/// <summary>
		/// The character the owning client has pinned to its target frame, or null.
		/// </summary>
		/// <remarks>
		/// A pin is a HUD concept: the pinned card stays up while the pointer wanders, so the
		/// player can follow one opponent through a fight. It is never a combat target — ability
		/// acquisition stays a server-side raycast from the replicated aim — and it lives on the
		/// owning client only. On the server this is always null.
		/// </remarks>
		Transform PinnedTarget { get; }

		/// <summary>
		/// Strict toggle: releases a held pin whatever is under the pointer, and otherwise pins the
		/// hovered character. Owning client only.
		/// </summary>
		/// <remarks>
		/// A release always wins. Making the key contextual instead — releasing only when the
		/// pointer happened to be on the pinned character or on nothing — meant a player with an
		/// enemy under the crosshair had no way to let go of a pin, and re-pinning silently moved
		/// it. Moving a pin therefore takes two presses; dropping one always takes exactly one.
		/// </remarks>
		/// <returns>True when a target is pinned after the call.</returns>
		bool TogglePinnedTarget();

		/// <summary>
		/// Pins a specific character. Refused for non-characters, unspawned objects and the
		/// player's own character. Owning client only.
		/// </summary>
		/// <param name="target">The transform to pin.</param>
		/// <returns>True when the target is pinned after the call.</returns>
		bool TryPinTarget(Transform target);

		/// <summary>
		/// Releases the pinned target, if any.
		/// </summary>
		void ClearPinnedTarget();
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