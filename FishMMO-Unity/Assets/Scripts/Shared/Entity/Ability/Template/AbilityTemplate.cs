using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Ability", menuName = "FishMMO/Character/Ability/Ability", order = 1)]
	public class AbilityTemplate : BaseAbilityTemplate, ITooltip
	{
		public GameObject AbilityObjectPrefab;
		public AbilitySpawnTarget AbilitySpawnTarget;
		public bool RequiresTarget;
		public byte AdditionalEventSlots;
		public int HitCount;
		public AbilityType Type;

		[Header("Event-Condition-Action (ECA) Triggers")]
		[Tooltip("The Ability Event executed when the ability is activated if AbilitySpawnTarget is Self or when the Ability hits a target.")]
		public AbilityEvent TargetTrigger;

		[Tooltip("Ability Events to execute when the ability object 'ticks' (e.g., moves, applies continuous effects).")]
		public List<AbilityOnTickEvent> OnTickEvents = new List<AbilityOnTickEvent>();

		[Tooltip("Ability Events to execute when the ability object collides or hits a character.")]
		public List<AbilityOnHitEvent> OnHitEvents = new List<AbilityOnHitEvent>();

		[Tooltip("Ability Events to execute before the primary ability object is spawned.")]
		public List<AbilityOnPreSpawnEvent> OnPreSpawnEvents = new List<AbilityOnPreSpawnEvent>();

		[Tooltip("Ability Events to execute when the primary ability object is spawned.")]
		public List<AbilityOnSpawnEvent> OnSpawnEvents = new List<AbilityOnSpawnEvent>();

		[Tooltip("Ability Events to execute when the ability object is destroyed.")]
		public List<AbilityOnDestroyEvent> OnDestroyEvents = new List<AbilityOnDestroyEvent>();

		// Cached list of all event IDs for this ability template
		[System.NonSerialized]
		private List<int> cachedAllEventIDs;

		/// <summary>
		/// Returns a cached list of all unique event IDs from all event lists on this template.
		/// </summary>
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