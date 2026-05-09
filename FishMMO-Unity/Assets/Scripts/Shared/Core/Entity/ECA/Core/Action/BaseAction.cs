using System;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Shared.Core
{
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
		/// Executes the action. Must be implemented by derived classes.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data for the action.</param>
		public abstract void Execute(ICharacter initiator, EventData eventData);
	}
}