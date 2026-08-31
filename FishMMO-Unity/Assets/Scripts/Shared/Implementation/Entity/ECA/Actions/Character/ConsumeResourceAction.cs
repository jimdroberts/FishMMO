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
			/* The server, or the client that OWNS the initiator — see EcaAuthority.MayPredict.
			 *
			 * This is the caster's OWN resource, and the caster already predicts the same spend
			 * through the ability path's ConsumeResources; gating it to the server here meant a cost
			 * paid by an ECA step left the mana bar sitting still until the reconcile corrected it.
			 * An observer still answers false: it has no business spending somebody else's mana.
			 *
			 * Still reported as success either way. Returning FALSE would be worse than not gating
			 * at all: paired with StopChainOnFailure it would abort the rest of the chain on every
			 * peer that did not spend, taking the non-authoritative steps (FX, dialogue, UI) down
			 * with it. "Not this peer's decision" is not the same as "the cost could not be paid". */
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

			/* Drawn BEFORE the peer gate, never after — see AbilityObject.RNG. The two guards above
			 * are authoring faults that answer the same on every peer, so they may precede it; the
			 * gate may not. */
			int amount = AmountValue.GetValue(initiator, eventData);

			if (!EcaAuthority.MayPredict(initiator, eventData))
			{
				return true;
			}

			if (!TryResolveTargetOrInitiator(initiator, eventData, out ICharacter characterToConsume)) return false;
			if (!characterToConsume.TryGet(out ICharacterAttributeController attributeController)) return false;

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