using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for ability events, providing common event fields, requirements, and tooltip logic.
	/// </summary>
	public abstract class AbilityEvent : Trigger, ITooltip
	{
		/// <summary>
		/// The icon representing the ability event (set in the inspector).
		/// </summary>
		[SerializeField]
		private Sprite icon;

		/// <summary>
		/// Time required to activate the event (in seconds).
		/// </summary>
		public float ActivationTime;

		/// <summary>
		/// Lifetime of the event effect (in seconds).
		/// </summary>
		public float LifeTime;

		/// <summary>
		/// Speed of the event effect (units per second).
		/// </summary>
		public float Speed;

		/// <summary>
		/// Cooldown time for the event (in seconds).
		/// </summary>
		public float Cooldown;

		/// <summary>
		/// Price or cost of the event (e.g., in-game currency).
		/// </summary>
		public int Price;

		/// <summary>
		/// Resources required to use the event (e.g., mana, stamina).
		/// </summary>
		public AbilityResourceDictionary Resources = new AbilityResourceDictionary();

		/// <summary>
		/// Attributes required to use the event (e.g., strength, intelligence).
		/// </summary>
		public AbilityResourceDictionary RequiredAttributes = new AbilityResourceDictionary();

		/// <summary>
		/// Faction required to use the event.
		/// </summary>
		public FactionTemplate RequiredFaction;

		/// <summary>
		/// Archetype required to use the event.
		/// </summary>
		public ArchetypeTemplate RequiredArchetype;

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