using System.Collections.Generic;
using Cysharp.Text;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Buff template that grants bonus attributes to a character while active.
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
		/// <param name="stringBuilder">The string builder to append to.</param>
		public override void SecondaryTooltip(Utf16ValueStringBuilder stringBuilder)
		{
			if (BonusAttributes != null &&
					BonusAttributes.Count > 0)
			{
				stringBuilder.AppendLine();
				stringBuilder.Append(RichText.Format("Bonus Attributes", true, "f5ad6e", "140%"));

				foreach (BuffAttributeTemplate buffAttribute in BonusAttributes)
				{
					// Show each bonus attribute and its value in the tooltip
					stringBuilder.Append(RichText.Format(buffAttribute.Template.Name, buffAttribute.Value, true, "FFFFFFFF", "", "s"));
				}
			}
		}

		/// <summary>
		/// Applies the bonus attributes to the target when the buff is applied.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public override void OnApply(Buff buff, ICharacter target)
		{
			if (buff == null)
			{
				return;
			}
			if (target == null)
			{
				return;
			}
			if (!target.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}
			foreach (BuffAttributeTemplate buffAttribute in BonusAttributes)
			{
				if (buffAttribute == null)
				{
					continue;
				}
				if (buffAttribute.Template == null)
				{
					continue;
				}
				// Try to apply to a regular attribute first, then to a resource attribute if not found
				if (attributeController.TryGetAttribute(buffAttribute.Template.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddValue(buffAttribute.Value);
				}
				else if (attributeController.TryGetResourceAttribute(buffAttribute.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddValue(buffAttribute.Value);
				}
			}
		}

		/// <summary>
		/// Removes the bonus attributes from the target when the buff is removed.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public override void OnRemove(Buff buff, ICharacter target)
		{
			if (buff == null)
			{
				return;
			}
			if (target == null)
			{
				return;
			}
			if (!target.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}
			foreach (BuffAttributeTemplate buffAttribute in BonusAttributes)
			{
				if (buffAttribute == null)
				{
					continue;
				}
				if (buffAttribute.Template == null)
				{
					continue;
				}
				// Remove from regular attribute first, then from resource attribute if not found
				if (attributeController.TryGetAttribute(buffAttribute.Template.ID, out CharacterAttribute characterAttribute))
				{
					characterAttribute.AddValue(-buffAttribute.Value);
				}
				else if (attributeController.TryGetResourceAttribute(buffAttribute.Template.ID, out CharacterResourceAttribute characterResourceAttribute))
				{
					characterResourceAttribute.AddValue(-buffAttribute.Value);
				}
			}
		}

		/// <summary>
		/// Applies the bonus attributes again when a stack is added (delegates to OnApply).
		/// </summary>
		/// <param name="buff">The buff instance being stacked.</param>
		/// <param name="target">The character receiving the stack.</param>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
			OnApply(buff, target);
		}

		/// <summary>
		/// Removes the bonus attributes again when a stack is removed (delegates to OnRemove).
		/// </summary>
		/// <param name="buff">The buff instance being unstacked.</param>
		/// <param name="target">The character losing the stack.</param>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
			OnRemove(buff, target);
		}

		/// <summary>
		/// Called on each tick while the buff is active. No operation for attribute buffs.
		/// </summary>
		/// <param name="buff">The buff instance.</param>
		/// <param name="target">The character affected.</param>
		public override void OnTick(Buff buff, ICharacter target)
		{
			// No periodic effect for attribute buffs.
		}
	}
}