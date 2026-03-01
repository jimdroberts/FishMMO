using System;
using UnityEngine;
using FishMMO.Logging;

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
		/// The amount of the resource to consume.
		/// </summary>
		[Tooltip("The amount of the resource to consume.")]
		public int Amount = 1;

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

			ICharacter characterToConsume = ResolveTarget(initiator, eventData);

			if (characterToConsume == null) return;
			if (!characterToConsume.TryGet(out ICharacterAttributeController attributeController)) return;

			if (attributeController.TryGetResourceAttribute(ResourceTemplate.ID, out CharacterResourceAttribute resource) &&
				resource.CurrentValue >= Amount)
			{
				resource.Consume(Amount);
			}
		}
	}
}