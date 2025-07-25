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

		// Cached list of all trigger IDs for this ability template
		[System.NonSerialized]
		private List<int> cachedAllTriggerIDs;

		/// <summary>
		/// Returns a cached list of all unique trigger IDs from all trigger lists on this template.
		/// </summary>
		public List<int> GetAllAbilityTriggerIDs()
		{
			if (cachedAllTriggerIDs != null)
				return cachedAllTriggerIDs;

			var triggerIDs = new HashSet<int>();
			if (OnTickEvents != null)
				triggerIDs.UnionWith(OnTickEvents.FindAll(t => t != null).ConvertAll(t => t.ID));
			if (OnHitEvents != null)
				triggerIDs.UnionWith(OnHitEvents.FindAll(t => t != null).ConvertAll(t => t.ID));
			if (OnPreSpawnEvents != null)
				triggerIDs.UnionWith(OnPreSpawnEvents.FindAll(t => t != null).ConvertAll(t => t.ID));
			if (OnSpawnEvents != null)
				triggerIDs.UnionWith(OnSpawnEvents.FindAll(t => t != null).ConvertAll(t => t.ID));
			if (OnDestroyEvents != null)
				triggerIDs.UnionWith(OnDestroyEvents.FindAll(t => t != null).ConvertAll(t => t.ID));

			cachedAllTriggerIDs = new List<int>(triggerIDs);
			return cachedAllTriggerIDs;
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