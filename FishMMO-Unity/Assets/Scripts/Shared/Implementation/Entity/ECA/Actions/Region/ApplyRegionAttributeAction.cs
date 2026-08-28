using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that modifies a character attribute when in a region.
	/// Adds to a resource attribute's current value or adds a modifier to a regular attribute.
	/// Server-only (gameplay state); suppressed during prediction reconciliation.
	/// </summary>
	[Serializable]
	public class ApplyRegionAttributeAction : BaseAction
	{
		/// <summary>
		/// The character attribute template to modify.
		/// </summary>
		[Tooltip("The character attribute to modify.")]
		public CharacterAttributeTemplate Attribute;

		/// <summary>
		/// The value to add.
		/// </summary>
		[Tooltip("The value to add to the attribute.")]
		public int Value;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null || Attribute == null)
			{
				return;
			}

			// Server authoritative: attribute mutation is gameplay state and must only run on the
			// server (also refuses during reconcile replay as belt-and-braces).
			if (!RegionActionGate.ShouldExecuteGameplay(initiator, eventData))
			{
				return;
			}

			if (!initiator.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			if (attributeController.TryGetResourceAttribute(Attribute, out CharacterResourceAttribute r))
			{
				r.AddToCurrentValue(Value);
			}
			else if (attributeController.TryGetAttribute(Attribute, out CharacterAttribute c))
			{
				c.AddModifier(Value);
			}
		}
	}
}