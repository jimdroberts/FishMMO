using System;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks whether a character is <b>carrying</b> a specific item.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Despite the name, this condition checks inventory presence and nothing else.</b> It is an
	/// "owns one of these" test, not a "may use this right now" test. Read the name as
	/// <c>HasItemInInventory</c>; the class name is kept because conditions are persisted by type
	/// through <c>[SerializeReference]</c> and renaming it would silently null out the condition on
	/// every authored Trigger asset that already references it.
	/// </para>
	/// <para>
	/// <b>What it does NOT check, and where each of those actually lives:</b>
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// <b>Cooldown.</b> Compose <see cref="HasCooldownCondition"/> (with
	/// <see cref="BaseCondition.Invert"/> set) alongside this one, giving it the consumable
	/// template's <c>ID</c> — that is the key <see cref="ConsumableTemplate.Invoke"/> registers the
	/// cooldown under. Doing it here instead would mean a second, parallel copy of that condition's
	/// tick-domain resolution: a replicate-domain tick has to be preferred over a raw authoritative
	/// one and an authoritative one mapped through
	/// <see cref="ICooldownController.ResolveAuthoritativeTick"/>, or the comparison is made in the
	/// wrong clock and answers wrongly during reconcile replay. One copy of that, in the condition
	/// named after it, is the whole point of ECA conditions being small composable predicates.
	/// </description></item>
	/// <item><description>
	/// <b>Charges / stack amount.</b> <see cref="ConsumableTemplate.CanConsume"/> owns it, and needs
	/// the <see cref="Item"/> instance and an <see cref="IPlayerCharacter"/> — neither of which this
	/// condition has, since <c>IItemContainer.ContainsItem</c> answers only yes or no and the subject
	/// here may be an NPC.
	/// </description></item>
	/// <item><description>
	/// <b>"Requirements".</b> There is no such concept to check. <see cref="BaseItemTemplate"/> has
	/// no level, class, attribute or faction requirement fields of any kind — the FIXME that used to
	/// sit in <see cref="Evaluate"/> promising a "requirements" check referred to a system that has
	/// never existed. If one is added later, this is the condition to extend.
	/// </description></item>
	/// </list>
	/// <para>
	/// <b>This is not the authoritative use gate and must not be treated as one.</b> Item use is
	/// validated server-side by <see cref="ConsumableTemplate.CanConsume"/>, called from both
	/// <c>AbilityController.ActivateConsumable</c> (client pre-filter) and the authoritative
	/// activation path. A Trigger that gates on this condition alone is gating on possession, so a
	/// character who owns a potion satisfies it while the potion is still on cooldown.
	/// </para>
	/// </remarks>
	[Serializable]
	public class CanUseItemCondition : BaseCondition
	{
		/// <summary>
		/// The item template required for this condition to pass.
		/// </summary>
		public BaseItemTemplate RequiredItem;

		/* The gap this condition leaves is documented above, but a remark in a source file is not
		 * read by whoever is wiring a Trigger in the inspector six months from now. The failure mode
		 * is specific and silent: an author points RequiredItem at a potion, sees a condition called
		 * "CanUseItem" pass, and ships content that lets the potion's effect fire on every attempt
		 * because nothing in the Trigger ever consulted the cooldown.
		 *
		 * So say it out loud, once, the first time such a condition is actually evaluated. It fires
		 * only for the exact misauthoring case — a consumable that HAS a cooldown, gated by a
		 * condition that does not check it — and it changes no answer; Evaluate still returns what it
		 * always returned. A warning that altered the result would be the "half-check that looks
		 * complete" this deliberately refuses to be. */
		[NonSerialized]
		private bool warnedAboutUncheckedCooldown;

		/// <summary>
		/// Evaluates whether the character (or event target) is carrying the specified item.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character has the required item in their inventory; otherwise, false.</returns>
		/// <remarks>
		/// Presence only. See the remarks on <see cref="CanUseItemCondition"/> for what is not
		/// checked here and which condition or template method checks it instead.
		/// </remarks>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);
			if (characterToCheck == null)
			{
				Log.Warning("CanUseItemCondition", "Character does not exist.");
				return false;
			}
			// Ensure a required item is assigned.
			if (RequiredItem == null)
			{
				Log.Warning("CanUseItemCondition", "RequiredItem is not assigned.");
				return false;
			}
			// Check if the character has an inventory controller.
			if (!characterToCheck.TryGet(out IInventoryController inventoryController))
			{
				Log.Warning("CanUseItemCondition", "Character does not have an IInventoryController.");
				return false;
			}

			WarnIfGatingACooldownItem();

			return inventoryController.ContainsItem(RequiredItem);
		}

		/// <summary>
		/// Warns once per condition instance when <see cref="RequiredItem"/> is a consumable that
		/// carries a cooldown, since this condition does not and cannot check that cooldown.
		/// </summary>
		private void WarnIfGatingACooldownItem()
		{
			if (warnedAboutUncheckedCooldown ||
				!(RequiredItem is ConsumableTemplate consumable) ||
				consumable.Cooldown <= 0.0f)
			{
				return;
			}

			// Latch before logging, not after: the log call is fire-and-forget and this method is
			// reached from a per-target evaluation, so an early return is the only thing keeping this
			// to one line rather than one per target per cast.
			warnedAboutUncheckedCooldown = true;

			Log.Warning("CanUseItemCondition",
				$"'{consumable.Name}' has a {consumable.Cooldown:0.##}s cooldown, but CanUseItemCondition only checks that the item is in the inventory. " +
				"It will pass while the item is still on cooldown. Add an inverted HasCooldownCondition with this item's template ID to the same Trigger if the cooldown must gate it.");
		}
	}
}
