using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that damages (or, given a negative amount, heals) a character standing in a region: a poison mist, a
	/// burning floor, a healing spring. Server-only; suppressed during prediction reconciliation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why not <see cref="ApplyRegionAttributeAction"/> on Health.</b> That action moves a resource's current value
	/// directly. Health is not just a resource: taking it to zero that way leaves the character standing at zero health,
	/// never killed, with no resistance applied, no combat report and no damage number. This goes through
	/// <see cref="IDamageable.Damage"/> and <see cref="IHealable.Heal"/>, the way drowning and lava do
	/// (<c>CharacterAttributeController.Breath</c>): resistances and immunities by <see cref="DamageType"/>, the combat
	/// report, and death at zero.
	/// </para>
	/// <para>
	/// Nobody is the attacker. A region hurts whoever stands in it, so the hit is credited to no one: no kill credit, no
	/// threat, and the harm rule (which only restrains one character attacking another) does not apply. Reported as
	/// periodic, like any damage over time.
	/// </para>
	/// <para>
	/// Runs once per execution. On <c>OnRegionStay</c> that is every tick, so pace it with a
	/// <see cref="TickIntervalCondition"/> on the same trigger.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ApplyRegionDamageAction : BaseAction
	{
		/// <summary>Damage per execution. Negative heals.</summary>
		[Tooltip("Damage per execution, before resistances. Negative heals instead.")]
		public int Amount = 5;

		/// <summary>The damage type, for resistances and immunities. None: true damage.</summary>
		[Tooltip("The damage type (resistances, race immunities). Leave empty for damage no resistance reduces.")]
		public DamageAttributeTemplate DamageType;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null || Amount == 0)
			{
				return;
			}

			// Server authoritative: health is gameplay state (and death is decided only there).
			if (!RegionActionGate.ShouldExecuteGameplay(initiator, eventData))
			{
				return;
			}

			if (!initiator.TryGet(out ICharacterDamageController damageController))
			{
				return;
			}

			if (Amount > 0)
			{
				damageController.Damage(null, Amount, DamageType, ignoreAchievements: false, periodic: true);
			}
			else
			{
				damageController.Heal(null, -Amount, ignoreAchievements: false, periodic: true);
			}
		}

		/// <inheritdoc />
		public override string GetTooltipContribution()
		{
			if (Amount == 0)
			{
				return null;
			}
			string type = DamageType != null ? " " + DamageType.Name : string.Empty;
			return Amount > 0 ? $"Deals {Amount}{type}" : $"Heals {-Amount}";
		}
	}
}
