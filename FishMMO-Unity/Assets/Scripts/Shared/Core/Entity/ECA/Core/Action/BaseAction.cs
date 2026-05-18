using System;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Opt-in interface for actions that can meaningfully fail at runtime (e.g. insufficient
	/// resources, missing controllers, validation rejection). When combined with
	/// <see cref="BaseAction.StopChainOnFailure"/>, returning <c>false</c> aborts the rest of the
	/// action chain for the current event. Most actions don't need this — their <see cref="IAction.Execute"/>
	/// implementation simply runs and returns; only implement <see cref="IAbortableAction"/> when
	/// there is a genuine fail-and-bail outcome.
	/// </summary>
	public interface IAbortableAction
	{
		/// <summary>
		/// Attempts to perform the action. Returns false to signal that the action could not be
		/// performed (e.g. preconditions failed) so callers can short-circuit chains.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data for the action.</param>
		/// <returns>True on success; false when the action could not be performed.</returns>
		bool TryExecute(ICharacter initiator, EventData eventData);
	}

	/// <summary>
	/// Abstract base class for all ECA actions. Serialized inline via [SerializeReference] on Trigger assets.
	/// Derive from this class and add [Serializable] to create concrete actions.
	/// </summary>
	[Serializable]
	public abstract class BaseAction : IAction
	{
		/// <summary>
		/// Optional selector that picks one or more targets for this action. When set, the
		/// action runs once per selected target. When unset, the action runs once against the
		/// current event data (reading <see cref="EventData.TargetCharacter"/> or falling back
		/// to the initiator).
		/// </summary>
		[Tooltip("Optional selector for this action. When unset the action runs once against the current event target.")]
		[SerializeReference, SubclassSelector]
		public TargetSelector TargetSelector;

		/// <summary>
		/// When true and this action implements <see cref="IAbortableAction"/>, returning
		/// <c>false</c> from <see cref="IAbortableAction.TryExecute"/> aborts the remainder of the
		/// current action list (e.g. stop applying damage after a resource consume fails).
		/// Has no effect on actions that do not implement <see cref="IAbortableAction"/>.
		/// </summary>
		[Tooltip("If this action implements IAbortableAction, abort the rest of the action chain when TryExecute returns false.")]
		public bool StopChainOnFailure;

		/// <summary>
		/// Executes the action. Must be implemented by derived classes.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data for the action.</param>
		public abstract void Execute(ICharacter initiator, EventData eventData);

		/// <summary>
		/// Strict target resolution: returns the explicit <see cref="EventData.TargetCharacter"/>
		/// only. Does <b>not</b> fall back to the initiator — outward-effecting actions
		/// (damage, dispel, interrupt, knockback) should use this so a misconfigured selector
		/// can't silently make a caster attack itself.
		/// </summary>
		/// <param name="eventData">The action's event data (may be null).</param>
		/// <param name="target">Resolved target character, or null when none is present.</param>
		/// <returns>True when an explicit target was resolved.</returns>
		protected static bool TryResolveTarget(EventData eventData, out ICharacter target)
		{
			target = eventData?.TargetCharacter;
			return target != null;
		}

		/// <summary>
		/// Forgiving target resolution: prefers <see cref="EventData.TargetCharacter"/>, then
		/// falls back to the initiator. Use for self-effecting actions (resource costs,
		/// self-buffs, cooldown starts) where "no target" naturally means "act on self".
		/// Outward-effecting actions should use <see cref="TryResolveTarget"/> instead so a
		/// missing target produces a no-op rather than a self-hit.
		/// </summary>
		/// <param name="initiator">The action's initiator.</param>
		/// <param name="eventData">The action's event data (may be null).</param>
		/// <param name="target">Resolved character, or null when neither is available.</param>
		/// <returns>True when a non-null target was resolved.</returns>
		protected static bool TryResolveTargetOrInitiator(ICharacter initiator, EventData eventData, out ICharacter target)
		{
			target = (eventData?.TargetCharacter ?? initiator);
			return target != null;
		}
	}
}