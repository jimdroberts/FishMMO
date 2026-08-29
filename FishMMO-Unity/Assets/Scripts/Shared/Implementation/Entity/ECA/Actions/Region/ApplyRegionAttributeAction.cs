using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that adds a one-shot amount to a character's RESOURCE while it is in a region.
	/// Server-only (gameplay state); suppressed during prediction reconciliation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Resources only, deliberately.</b> This used to fall through to
	/// <c>CharacterAttribute.AddModifier</c> for a non-resource attribute, which was the one
	/// modifier source in the project with no paired removal and no owner: nothing reversed it when
	/// the character left the region, died, disconnected inside it or changed scene, and on an
	/// <c>OnRegionStay</c> trigger it accumulated once per tick forever. <c>ExternalModifier</c> is
	/// a bare accumulator with no per-source ledger, so an orphaned contribution is
	/// indistinguishable from a real one and survives until the server next recomputes the sheet
	/// from scratch.
	/// </para>
	/// <para>
	/// <b>Use <see cref="ApplyRegionBuffAction"/> with an <see cref="AttributeBuffTemplate"/> for a
	/// region-scoped attribute bonus.</b> That path already owns the pairing — the buff applies the
	/// modifier once and <c>OnRemove</c> reverses exactly it on expiry, dispel, death or teardown —
	/// and it is safe to fire every stay tick, because refreshing an existing buff resets its
	/// duration without touching the modifier.
	/// </para>
	/// <para>
	/// A resource delta needs none of that: it moves <c>CurrentValue</c>, which is depletable state
	/// the server reconciles and broadcasts outright, so there is nothing to reverse.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ApplyRegionAttributeAction : BaseAction
	{
		/// <summary>
		/// The resource attribute to modify (health, mana, stamina...).
		/// </summary>
		[Tooltip("The resource attribute to modify. Non-resource attributes are not supported — use ApplyRegionBuffAction with an AttributeBuffTemplate.")]
		public CharacterAttributeTemplate Attribute;

		/// <summary>
		/// The amount to add to the resource's current value. Negative drains it.
		/// </summary>
		[Tooltip("Amount added to the resource's current value. Negative drains it.")]
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
				return;
			}

			/* Refused rather than quietly applied as an unpaired modifier — see the remarks on this
			 * type. Logged once per execution because an author who wired a non-resource attribute
			 * here got a silently permanent bonus, which is worse than an obvious no-op. */
			Log.Warning("ApplyRegionAttributeAction",
				$"'{Attribute.Name}' is not a resource attribute. This action only adds to resource " +
				"current values; for a region-scoped attribute bonus use ApplyRegionBuffAction with " +
				"an AttributeBuffTemplate, which reverses itself when the buff ends.");
		}
	}
}