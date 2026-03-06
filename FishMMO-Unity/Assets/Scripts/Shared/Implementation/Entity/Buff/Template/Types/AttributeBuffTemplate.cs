using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Buff template that grants bonus attributes to a character while active.
	/// Applies additive modifiers on apply/stack and removes them symmetrically on remove/unstack.
	/// </summary>
	[CreateAssetMenu(fileName = "New Attribute Buff Template", menuName = "FishMMO/Character/Buff/Attribute Buff", order = 1)]
	public class AttributeBuffTemplate : BaseBuffTemplate
	{
		/// <summary>
		/// List of bonus attributes applied by this buff.
		/// </summary>
		public List<BuffAttributeTemplate> BonusAttributes;

		/// <summary>
		/// Appends a secondary tooltip describing the bonus attributes granted by this buff.
		/// </summary>
		/// <param name="builder">The tooltip builder to populate.</param>
		public override void SecondaryTooltip(TooltipBuilder builder)
		{
			if (BonusAttributes == null || BonusAttributes.Count < 1) return;

			builder.AddLine("Bonus Attributes", 20, TooltipColors.Title, false, "140%");
			for (int i = 0; i < BonusAttributes.Count; i++)
			{
				BuffAttributeTemplate buffAttribute = BonusAttributes[i];
				if (buffAttribute?.Template == null) continue;
				builder.AddLine($"{buffAttribute.Template.Name}: {buffAttribute.Value}", 21 + i, TooltipColors.Stat);
			}
		}

		/// <summary>
		/// Applies the bonus attributes to the target when the buff is applied.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public override void OnApply(Buff buff, ICharacter target)
		{
			ApplyModifiers(target, 1);
		}

		/// <summary>
		/// Removes the bonus attributes from the target when the buff is removed.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public override void OnRemove(Buff buff, ICharacter target)
		{
			ApplyModifiers(target, -1);
		}

		/// <summary>
		/// Applies or removes attribute modifiers based on a sign multiplier.
		/// Positive sign adds modifiers, negative sign removes them.
		/// </summary>
		/// <param name="target">The character to modify.</param>
		/// <param name="sign">+1 to apply, -1 to remove.</param>
		private void ApplyModifiers(ICharacter target, int sign)
		{
			if (target == null || BonusAttributes == null) return;
			if (!target.TryGet(out ICharacterAttributeController attributeController)) return;

			for (int i = 0; i < BonusAttributes.Count; i++)
			{
				BuffAttributeTemplate buffAttribute = BonusAttributes[i];
				if (buffAttribute?.Template == null) continue;

				int modifier = buffAttribute.Value * sign;
				if (attributeController.TryGetAttribute(buffAttribute.Template.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddModifier(modifier);
				}
				else if (attributeController.TryGetResourceAttribute(buffAttribute.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddModifier(modifier);
				}
			}
		}
	}
}