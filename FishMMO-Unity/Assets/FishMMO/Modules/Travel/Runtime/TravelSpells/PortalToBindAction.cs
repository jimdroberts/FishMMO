using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Portal to Bind spell's effect: sends the caster to its bind point (home point), in this scene or another
	/// (issue #241).
	/// </summary>
	/// <remarks>
	/// <para>Server only, and always the caster: put it on an <c>AbilityOnHitEvent</c> with an initiator target selector on
	/// a self-cast ability. The move is the one a respawn at the bind point makes (<see cref="CharacterTransfer.TransferToBindPoint"/>).</para>
	/// <para>This is the escape spell: it is castable in combat and never checks whether the bind point is occupied. The ability
	/// that carries it should have a cast time and <c>InterruptedByDamage</c>, so attacking the caster is how to stop it.</para>
	/// </remarks>
	[Serializable]
	public class PortalToBindAction : BaseAction
	{
		/// <summary>What the caster is told when it has no bind point.</summary>
		public const string NoBindPointText = "You have no home point. Bind at a bindstone first.";

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!EcaAuthority.IsServer(initiator, eventData) || initiator is not IPlayerCharacter player)
			{
				return;
			}

			TravelSpellRefusal refusal = TravelSpellRules.Decide(
				CharacterStateValidation.CanAct(player),
				TravelSpellFeedback.InArena(player),
				NoTravelZone.Blocks(player),
				!string.IsNullOrEmpty(player.BindScene));

			if (refusal == TravelSpellRefusal.None &&
				CharacterTransfer.TransferToBindPoint(player, out TransferRefusal transferRefusal) == TransferOutcome.Refused)
			{
				Log.Debug("PortalToBindAction", $"Character {player.ID} was not moved to its bind point: {transferRefusal}.");
				refusal = transferRefusal == TransferRefusal.NoBindPoint ? TravelSpellRefusal.NoDestination : TravelSpellRefusal.TransferFailed;
			}

			if (refusal != TravelSpellRefusal.None)
			{
				TravelSpellFeedback.Toast(player, TravelSpellRules.Describe(refusal, NoBindPointText));
			}
		}

		public override string GetTooltipContribution()
		{
			return "Returns you to your home point";
		}
	}
}
