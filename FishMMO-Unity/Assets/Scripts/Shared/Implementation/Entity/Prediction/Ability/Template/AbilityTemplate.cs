using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for defining an ability, including prefabs, triggers, event lists, and requirements.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability", menuName = "FishMMO/Character/Ability/Ability", order = 1)]
	public class AbilityTemplate : BaseAbilityTemplate
	{
		/// <summary>
		/// The prefab for the ability object (visual or functional representation).
		/// </summary>
		public GameObject AbilityObjectPrefab;

		/// <summary>
		/// The spawn target type for the ability (where the ability effect appears).
		/// </summary>
		public AbilitySpawnTarget AbilitySpawnTarget;

		/// <summary>
		/// Whether the ability requires a target to be used.
		/// </summary>
		public bool RequiresTarget;

		/// <summary>
		/// Additional event slots for the ability (for extensibility).
		/// </summary>
		public byte AdditionalEventSlots;

		/// <summary>
		/// The number of times the ability can hit (e.g., for multi-hit abilities).
		/// </summary>
		public int HitCount;

		/// <summary>
		/// The type of the ability (e.g., offensive, defensive, utility).
		/// </summary>
		public AbilityType Type;

		[Header("Event-Condition-Action (ECA) Triggers")]
		[Tooltip("Ability Events to execute when the ability object 'ticks' (e.g., moves, applies continuous effects).")]
		/// <summary>
		/// Events executed when the ability object ticks.
		/// </summary>
		public List<AbilityOnTickEvent> OnTickEvents = new List<AbilityOnTickEvent>();

		[Tooltip("Ability Events to execute when the ability object collides or hits a character.")]
		/// <summary>
		/// Events executed when the ability object collides or hits a character.
		/// </summary>
		public List<AbilityOnHitEvent> OnHitEvents = new List<AbilityOnHitEvent>();

		[Tooltip("Ability Events to execute before the primary ability object is spawned.")]
		/// <summary>
		/// Events executed before the primary ability object is spawned.
		/// </summary>
		public List<AbilityOnPreSpawnEvent> OnPreSpawnEvents = new List<AbilityOnPreSpawnEvent>();

		[Tooltip("Ability Events to execute when the primary ability object is spawned.")]
		/// <summary>
		/// Events executed when the primary ability object is spawned.
		/// </summary>
		public List<AbilityOnSpawnEvent> OnSpawnEvents = new List<AbilityOnSpawnEvent>();

		[Tooltip("Ability Events to execute when the ability object is destroyed.")]
		/// <summary>
		/// Events executed when the ability object is destroyed.
		/// </summary>
		public List<AbilityOnDestroyEvent> OnDestroyEvents = new List<AbilityOnDestroyEvent>();

		/// <summary>
		/// Cached list of all event IDs for this ability template.
		/// </summary>
		[System.NonSerialized]
		private List<int> cachedAllEventIDs;

		/// <summary>
		/// Returns a cached list of all unique event IDs from all event lists on this template.
		/// </summary>
		/// <returns>A list of all unique event IDs used by this ability template.</returns>
		public List<int> GetAllAbilityEventIDs()
		{
			if (cachedAllEventIDs != null)
			{
				return cachedAllEventIDs;
			}

			HashSet<int> eventIDs = new HashSet<int>();
			AppendEventIDs(OnTickEvents, eventIDs);
			AppendEventIDs(OnHitEvents, eventIDs);
			AppendEventIDs(OnPreSpawnEvents, eventIDs);
			AppendEventIDs(OnSpawnEvents, eventIDs);
			AppendEventIDs(OnDestroyEvents, eventIDs);

			cachedAllEventIDs = new List<int>(eventIDs);
			return cachedAllEventIDs;
		}

		/// <summary>
		/// Clears cached event ID data when the template is edited in the inspector.
		/// </summary>
		private void OnValidate()
		{
			cachedAllEventIDs = null;
		}

		/// <summary>
		/// Appends all non-null event IDs from the provided list into the target set without extra list allocations.
		/// </summary>
		/// <typeparam name="T">The concrete ability event type.</typeparam>
		/// <param name="source">The source list to scan.</param>
		/// <param name="eventIDs">The destination set receiving event IDs.</param>
		private static void AppendEventIDs<T>(List<T> source, HashSet<int> eventIDs) where T : AbilityEvent
		{
			if (source == null)
			{
				return;
			}

			for (int i = 0; i < source.Count; ++i)
			{
				T abilityEvent = source[i];
				if (abilityEvent != null)
				{
					eventIDs.Add(abilityEvent.ID);
				}
			}
		}

		/// <summary>
		/// Returns the tooltip string for the ability, including type if set.
		/// </summary>
		/// <returns>The tooltip string for the ability.</returns>
		public override string Tooltip()
		{
			return TooltipWithEvents(null);
		}

		/// <summary>
		/// Returns a tooltip string composed with optional event tooltips, including type if set.
		/// Used by ability crafting to preview the composed ability tooltip.
		/// </summary>
		/// <param name="combineList">Optional list of event tooltips to combine.</param>
		/// <returns>The composed tooltip string.</returns>
		public string TooltipWithEvents(List<ITooltip> combineList)
		{
			using (var builder = new TooltipBuilder())
			{
				BuildTooltip(builder, combineList);
				if (Type != AbilityType.None)
				{
					builder.AddLine($"Type: {Type}", 90, TooltipColors.Title, false, "120%");
				}
				return builder.Build();
			}
		}
	}
}