using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for ability events. Extends <see cref="Trigger"/> to provide ECA-driven conditions and actions.
	/// Ability requirements (resources, faction, archetype, attributes) are defined as ECA conditions on the inherited
	/// <see cref="Trigger.Conditions"/> list or on the parent <see cref="BaseAbilityTemplate.ActivationConditions"/> list.
	/// </summary>
	public abstract class AbilityEvent : Trigger, ITooltip
	{
		/// <summary>
		/// The icon representing the ability event (set in the inspector).
		/// </summary>
		[SerializeField]
		private Sprite icon;

		/// <summary>
		/// Time required to activate the event (in seconds). Aggregated into the runtime ability.
		/// </summary>
		public float ActivationTime;

		/// <summary>
		/// Lifetime of the event effect (in seconds). Aggregated into the runtime ability.
		/// </summary>
		public float LifeTime;

		/// <summary>
		/// Speed of the event effect (units per second). Aggregated into the runtime ability.
		/// </summary>
		public float Speed;

		/// <summary>
		/// Cooldown time for the event (in seconds). Aggregated into the runtime ability.
		/// </summary>
		public float Cooldown;

		/// <summary>
		/// Crafting price of the event (in-game currency cost to add this event during ability crafting).
		/// </summary>
		public int Price;

		/// <summary>
		/// The name of the event (from the ScriptableObject name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon representing the event (property accessor).
		/// </summary>
		public Sprite Icon { get { return this.icon; } }

		/// <summary>
		/// Returns the formatted description for the ability event.
		/// </summary>
		/// <returns>A string describing the ability event.</returns>
		public string GetFormattedDescription()
		{
			return "Ability Event: " + Name;
		}

		/// <summary>
		/// Returns the tooltip string for the ability event.
		/// </summary>
		/// <returns>The tooltip string for the ability event.</returns>
		public string Tooltip()
		{
			return GetFormattedDescription();
		}

		/// <summary>
		/// Returns the tooltip string for the ability event, optionally combining with other tooltips.
		/// </summary>
		/// <param name="combineList">List of tooltips to combine.</param>
		/// <returns>The tooltip string for the ability event.</returns>
		public string Tooltip(List<ITooltip> combineList)
		{
			return GetFormattedDescription();
		}
	}
}