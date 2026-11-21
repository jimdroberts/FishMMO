using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action for lore object interactions. Sends a <see cref="LoreObjectBroadcast"/>
	/// to display the UILore window, grants abilities and ability events inline (idempotent),
	/// invokes <see cref="PlayerInteractionEventData.OnGrantItem"/> once per item grant so that
	/// <c>InteractableSystem</c> can persist each item to the database, and increments
	/// the achievement counter. Server-only.
	/// </summary>
	[Serializable]
	[UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "FishMMO.Shared", sourceAssembly: "FishMMO.Shared")]
	public class LoreObjectAction : BaseAction
	{
		/// <summary>
		/// Executes the lore object interaction: sends the lore broadcast, grants abilities and
		/// ability events, grants items, and increments the achievement counter.
		/// Server-only.
		/// </summary>
		/// <param name="initiator">The character interacting with the lore object.</param>
		/// <param name="eventData">The event data containing the interaction context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			ILoreObject loreObject = data.Interactable as ILoreObject;
			if (loreObject?.Template == null) return;

			LoreObjectTemplate template = loreObject.Template;

			// Show the lore window on the client
			SendToOwner(initiator, new LoreObjectBroadcast()
			{
				InteractableID = data.Interactable.ID,
				TemplateID = template.ID,
			});

			// Grant abilities and ability events (idempotent: already-known are skipped). The Abilities module teaches them
			// (AbilityGrants); without it the lore is read and nothing is learned.
			AbilityGrants.Grant(initiator, template.GrantAbilities, template.GrantAbilityEvents);

			// Achievement
			if (loreObject.AchievementTemplate != null)
			{
				Accomplishments.Record(initiator, loreObject.AchievementTemplate);
			}
		}
	}
}