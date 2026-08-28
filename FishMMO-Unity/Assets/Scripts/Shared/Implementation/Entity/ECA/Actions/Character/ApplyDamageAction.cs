using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies damage to a target character using a configurable value provider and a
	/// given damage attribute type.
	/// <para>
	/// SERVER ONLY. <see cref="Execute"/> returns immediately on any other peer — damage is not
	/// predicted, and the owner learns the result through the reconcile and the combat broadcasts.
	/// </para>
	/// </summary>
	[Serializable]
	public class ApplyDamageAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the amount of damage to apply.
		/// </summary>
		[Tooltip("The value provider that determines the amount of damage to apply.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider DamageValue;

		/// <summary>
		/// The attribute template associated with this damage type (e.g., 'Physical', 'Fire').
		/// Used to determine the element or type of damage applied.
		/// </summary>
		[Tooltip("The attribute template associated with this damage type (e.g., 'Physical', 'Fire').")]
		public DamageAttributeTemplate DamageAttributeTemplate;

		/// <summary>
		/// Applies damage to the target character using the computed value and attribute template.
		/// Server only — see the note on the class.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* Server only. State forwarding is off, so an observer never simulates another
			 * character and has nothing to predict here; the outcome reaches every peer through the
			 * authoritative paths (reconcile, observer broadcast). Running it locally as well would
			 * apply the effect twice on the peer that also happens to be the server, and produce a
			 * value on a client that the server never agreed to. */
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return;
			}

			if (DamageValue == null)
			{
				Log.Warning("DamageAction", "DamageValue provider is null.");
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target))
			{
				return;
			}

			if (target.TryGet(out ICharacterDamageController defenderDamageController))
			{
				int amount = DamageValue.GetValue(initiator, eventData);
				defenderDamageController.Damage(initiator, amount, DamageAttributeTemplate);
			}
		}
	}
}