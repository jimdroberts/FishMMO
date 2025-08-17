using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject template for defining an ability, including prefabs, triggers, event lists, and requirements.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability", menuName = "FishMMO/Character/Ability/Ability", order = 1)]
	public class AbilityTemplate : BaseAbilityTemplate, ITooltip
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
				return cachedAllEventIDs;

			var eventIDs = new HashSet<int>();
			if (OnTickEvents != null)
				eventIDs.UnionWith(OnTickEvents.FindAll(t => t != null).ConvertAll(t => t.ID));
			if (OnHitEvents != null)
				eventIDs.UnionWith(OnHitEvents.FindAll(t => t != null).ConvertAll(t => t.ID));
			if (OnPreSpawnEvents != null)
				eventIDs.UnionWith(OnPreSpawnEvents.FindAll(t => t != null).ConvertAll(t => t.ID));
			if (OnSpawnEvents != null)
				eventIDs.UnionWith(OnSpawnEvents.FindAll(t => t != null).ConvertAll(t => t.ID));
			if (OnDestroyEvents != null)
				eventIDs.UnionWith(OnDestroyEvents.FindAll(t => t != null).ConvertAll(t => t.ID));

			cachedAllEventIDs = new List<int>(eventIDs);
			return cachedAllEventIDs;
		}

		/// <summary>
		/// Returns the tooltip string for the ability, including its type if set.
		/// </summary>
		/// <returns>The tooltip string for the ability.</returns>
		public override string Tooltip()
		{
			string tooltip = base.Tooltip(null);
			if (Type != AbilityType.None)
			{
				tooltip += RichText.Format($"\r\nType: {Type}", true, "f5ad6eFF", "120%");
			}
			return tooltip;
		}
	}
}