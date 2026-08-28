using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that consumes a specified amount of a resource attribute from a character.
	/// Useful for per-event resource costs (e.g., channeled abilities consuming mana per tick).
	/// </summary>
	[Serializable]
	public sealed class ConsumeResourceAction : BaseAction, IAbortableAction
	{
		/// <summary>
		/// The character attribute template representing the resource to consume (e.g., Mana, Stamina).
		/// </summary>
		[Tooltip("The resource attribute to consume.")]
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int ResourceTemplateID;

		/// <summary>
		/// The value provider that determines how much of the resource to consume.
		/// </summary>
		[Tooltip("The value provider that determines how much of the resource to consume.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider AmountValue;

		/// <summary>
		/// Executes the action, consuming the specified resource from the target character.
		/// Prefer <see cref="TryExecute"/> for fail-fast chains (set <see cref="BaseAction.StopChainOnFailure"/>
		/// to abort follow-up actions when the resource is insufficient).
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			TryExecute(initiator, eventData);
		}

		/// <summary>
		/// Attempts to consume the configured resource. Returns false when the template/amount
		/// configuration is invalid or when the target lacks sufficient resource.
		/// </summary>
		public bool TryExecute(ICharacter initiator, EventData eventData)
		{
			/* Server only, but reported as success elsewhere. The resource is authoritative state and
			 * reaches clients through the attribute reconcile pipeline, so a client must not spend it
			 * a second time. Returning FALSE here would be worse than not gating at all: paired with
			 * StopChainOnFailure it would abort the rest of the chain on every client, taking the
			 * non-authoritative steps (FX, dialogue, UI) down with it. "Not this peer's decision" is
			 * not the same as "the cost could not be paid". */
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return true;
			}

			if (ResourceTemplateID == 0)
			{
				Log.Warning("ConsumeResourceAction", "ResourceTemplateID is not set.");
				return false;
			}

			if (AmountValue == null)
			{
				Log.Warning("ConsumeResourceAction", "AmountValue provider is null.");
				return false;
			}

			if (!TryResolveTargetOrInitiator(initiator, eventData, out ICharacter characterToConsume)) return false;
			if (!characterToConsume.TryGet(out ICharacterAttributeController attributeController)) return false;

			int amount = AmountValue.GetValue(initiator, eventData);
			if (!attributeController.TryGetResourceAttribute(ResourceTemplateID, out CharacterResourceAttribute resource))
			{
				return false;
			}
			if (resource.CurrentValue < amount)
			{
				return false;
			}
			resource.Consume(amount);
			return true;
		}
	}
}