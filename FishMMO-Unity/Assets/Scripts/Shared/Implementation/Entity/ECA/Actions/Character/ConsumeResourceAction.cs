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
	public sealed class ConsumeResourceAction : BaseAction
	{
		/// <summary>
		/// The character attribute template representing the resource to consume (e.g., Mana, Stamina).
		/// </summary>
		[Tooltip("The resource attribute to consume.")]
		public CharacterAttributeTemplate ResourceTemplate;

		/// <summary>
		/// The value provider that determines how much of the resource to consume.
		/// </summary>
		[Tooltip("The value provider that determines how much of the resource to consume.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider AmountValue;

		/// <summary>
		/// Executes the action, consuming the specified resource from the initiator or target character.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data for the action.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (ResourceTemplate == null)
			{
				Log.Warning("ConsumeResourceAction", "ResourceTemplate is null.");
				return;
			}

			if (AmountValue == null)
			{
				Log.Warning("ConsumeResourceAction", "AmountValue provider is null.");
				return;
			}

			ICharacter characterToConsume = ResolveTarget(initiator, eventData);

			if (characterToConsume == null) return;
			if (!characterToConsume.TryGet(out ICharacterAttributeController attributeController)) return;

			int amount = AmountValue.GetValue(initiator, eventData);
			if (attributeController.TryGetResourceAttribute(ResourceTemplate.ID, out CharacterResourceAttribute resource) &&
				resource.CurrentValue >= amount)
			{
				resource.Consume(amount);
			}
		}
	}
}