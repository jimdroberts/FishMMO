using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that changes a character's attribute while it is inside a region.
	/// Server-only (gameplay state); suppressed during prediction reconciliation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two different operations, chosen by the attribute.</b> A RESOURCE gets a one-shot change to
	/// its current value — a drain or a regeneration, safe on <c>OnRegionStay</c> and with nothing to
	/// reverse. A non-resource ATTRIBUTE gets a region-owned modifier that lasts while the character
	/// is inside and is released when it leaves.
	/// </para>
	/// <para>
	/// <b>The author does not wire the removal, and that is the point.</b> The contribution is keyed
	/// to the region (<see cref="ModifierSource.Region"/>) and
	/// <c>Region.ReleaseAttributeContributions</c> drops it on every path that ends membership:
	/// walking out, a deferred exit flushed after a reconcile or teleport, a descendant region taking
	/// ownership, and the region itself despawning. A character that dies or disconnects inside the
	/// region raises no exit at all — its whole ledger is cleared by
	/// <c>CharacterAttributeController.ResetState</c> on despawn, which is the same guarantee by a
	/// different route.
	/// </para>
	/// <para>
	/// This action used to refuse non-resource attributes outright. Applied through the old
	/// <c>AddModifier</c> a region bonus had no owner: nothing reversed it on leaving, dying,
	/// disconnecting or changing scene, and on an <c>OnRegionStay</c> trigger it accumulated once per
	/// tick forever. Naming the source fixes both — a release is possible, and a restatement every
	/// stay tick is idempotent rather than cumulative.
	/// </para>
	/// <para>
	/// <see cref="ApplyRegionBuffAction"/> with an <see cref="AttributeBuffTemplate"/> remains the
	/// right choice when the effect should outlive the region — a lingering blessing — or should be
	/// dispellable. This action is for a bonus that IS the region.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ApplyRegionAttributeAction : BaseAction
	{
		/// <summary>
		/// The attribute to modify. A resource is changed one-shot; anything else is modified for as long as the character is in the region.
		/// </summary>
		[Tooltip("Attribute to modify. A resource gets a one-shot change to its current value; any other attribute gets a region-owned modifier released when the character leaves.")]
		public CharacterAttributeTemplate Attribute;

		/// <summary>
		/// The amount to add to the resource's current value. Negative drains it.
		/// </summary>
		[Tooltip("Amount added to the resource's current value. Negative drains it.")]
		public int Value;

		/// <summary>
		/// Distinguishes this action's contribution from another action on the SAME region that
		/// modifies the SAME attribute.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Leave it at zero unless a region carries two of these actions pointed at one attribute.
		/// <c>CharacterAttribute.SetSource</c> STATES a whole contribution rather than adding to
		/// one, so two actions sharing a key are not two contributions — the second silently
		/// replaces the first and half the region's bonus disappears with no error anywhere. This is
		/// the same hazard <see cref="ModifierSource.Index"/> exists for and that
		/// <c>ItemGenerator</c> (keyed by item attribute template) and
		/// <c>AttributeBuffTemplate</c> (keyed by list position) already answer; the region action
		/// was the one contributor still passing the default.
		/// </para>
		/// <para>
		/// The release side needs no knowledge of this. <c>Region.ReleaseAttributeContributions</c>
		/// goes through <c>ClearSourceGroup</c>, which drops every entry sharing the region's
		/// (Kind, Id) whatever index wrote it.
		/// </para>
		/// </remarks>
		[Tooltip("Only needed when one region has two of these actions modifying the same attribute. Give them different values so their bonuses sum instead of overwriting each other.")]
		public int EntryIndex = 0;

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
				/* A resource delta is one-shot and has nothing to reverse: it moves CurrentValue,
				 * which is depletable state the server reconciles and broadcasts outright. Safe on
				 * an OnRegionStay trigger, where it is a drain or a regeneration. */
				r.AddToCurrentValue(Value);
				return;
			}

			if (!attributeController.TryGetAttribute(Attribute, out CharacterAttribute c))
			{
				Log.Warning("ApplyRegionAttributeAction",
					$"'{Attribute.Name}' resolves to no attribute on this character.");
				return;
			}

			/* A NAMED contribution, released by the region itself when the character leaves it —
			 * see Region.ReleaseAttributeContributions.
			 *
			 * This is what the action could not do before. A region bonus applied through
			 * AddModifier had no owner: nothing reversed it on leaving, dying, disconnecting inside
			 * the region or changing scene, and on an OnRegionStay trigger it accumulated once per
			 * tick forever. The action was cut down to resources rather than repaired, because
			 * there was no way to express "this region's contribution" for anything to release.
			 *
			 * Keyed by the region's ObjectId, which is stable for the region's life and unique
			 * within the scene. Idempotent, so an OnRegionStay trigger firing every tick states the
			 * same number every tick rather than accumulating — which is what made the stay case
			 * unusable. */
			if (!eventData.TryGet(out RegionEventData regionEvent) ||
				regionEvent.Region == null ||
				regionEvent.Region.NetworkObject == null)
			{
				Log.Warning("ApplyRegionAttributeAction",
					"No region on the event; a region-scoped modifier cannot be keyed to an owner and " +
					"would be unreleasable. Wire this to a Region trigger.");
				return;
			}

			c.SetSource(ModifierSource.Region(regionEvent.Region.NetworkObject.ObjectId, EntryIndex), Value);
		}
	}
}