using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// One resource an item restores, and by how much.
	/// </summary>
	[Serializable]
	public struct ResourceRestoration
	{
		/// <summary>The resource to restore — Health, Mana, Stamina.</summary>
		[Tooltip("The resource attribute to restore.")]
		public CharacterAttributeTemplate Resource;

		/// <summary>How much to restore, before any clamping at the resource's maximum.</summary>
		[Tooltip("How much to restore. Clamped at the resource's maximum by the controller.")]
		public int Amount;
	}

	/// <summary>
	/// A consumable that restores resources: a healing potion, a mana draught, a ration.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The first concrete consumable in the project. <see cref="ConsumableTemplate"/> and
	/// <see cref="ScrollConsumableTemplate"/> are both abstract and nothing derived from either, so
	/// no consumable item could exist at all — the charge, cooldown and destroy machinery was
	/// written and had nothing to run on.
	/// </para>
	/// <para>
	/// Health goes through <see cref="ICharacterDamageController.Heal"/> rather than straight onto
	/// the attribute, so a potion behaves like every other heal in the game: it is refused on a
	/// dead character, it draws a combat number, and it counts toward healing achievements. Every
	/// other resource is a plain <see cref="CharacterResourceAttribute.Gain"/>, which clamps at the
	/// maximum so an overfill tops the bar up rather than pushing past full.
	/// </para>
	/// <para>
	/// The base class has already taken the charge and applied the cooldown by the time this runs,
	/// so a restoration that finds no controller still consumes: the item was drunk either way.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Resource Consumable", menuName = "FishMMO/Character/Item/Consumable/Resource Consumable", order = 1)]
	public class ResourceConsumableTemplate : ConsumableTemplate
	{
		/// <summary>
		/// What this restores. More than one entry restores several resources from one use.
		/// </summary>
		[Tooltip("The resources this restores, and by how much.")]
		public List<ResourceRestoration> Restores = new List<ResourceRestoration>();

		/// <summary>Describes what drinking it gives back.</summary>
		/// <param name="content">The content being assembled.</param>
		public override void BuildTooltip(TooltipContent content, bool describingInstance)
		{
			base.BuildTooltip(content, describingInstance);

			if (Restores == null || Restores.Count < 1)
			{
				return;
			}

			content.AddHeader("Restores", TooltipPriority.Effects);
			for (int i = 0; i < Restores.Count; ++i)
			{
				ResourceRestoration restoration = Restores[i];
				if (restoration.Resource == null || restoration.Amount == 0)
				{
					continue;
				}

				content.AddStat(restoration.Resource.Name, $"+{restoration.Amount}",
					TooltipPriority.Effects + 1 + i, tone: TooltipTone.Good);
			}
		}

		/// <summary>
		/// Consumes the item and restores its resources.
		/// </summary>
		/// <param name="character">The character drinking it.</param>
		/// <param name="item">The item being consumed.</param>
		/// <param name="currentTick">The tick the use happened on.</param>
		/// <returns>True when the item was consumed.</returns>
		public override bool Invoke(IPlayerCharacter character, Item item, uint currentTick)
		{
			if (!base.Invoke(character, item, currentTick))
			{
				return false;
			}

			if (Restores == null ||
				!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return true;
			}

			character.TryGet(out ICharacterDamageController damageController);
			attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health);

			foreach (ResourceRestoration restoration in Restores)
			{
				if (restoration.Resource == null || restoration.Amount == 0 ||
					!attributeController.TryGetResourceAttribute(restoration.Resource, out CharacterResourceAttribute resource))
				{
					continue;
				}

				/* Health is healing, and healing is the damage controller's business — it refuses a
				 * dead target, draws the number, and feeds the achievement counters. Writing the
				 * attribute directly would restore a corpse in silence. */
				if (damageController != null && health != null && ReferenceEquals(resource, health))
				{
					damageController.Heal(character, restoration.Amount);
					continue;
				}

				resource.Gain(restoration.Amount);
			}
			return true;
		}
	}
}
