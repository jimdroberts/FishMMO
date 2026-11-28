using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Call to Arms spell's effect (issue #241): every member of the caster's party, on any scene server, is asked whether to
	/// come to the caster; each who accepts within <see cref="AcceptSeconds"/> is brought to the caster's side.
	/// </summary>
	/// <remarks>
	/// Server only, and always the caster (on the spawn event of a pulse ability at the caster's feet). Refused outside a party, in
	/// an arena, a <see cref="NoTravelZone"/> and an instance. Members on other scene servers are reached through the party chat
	/// pump (<c>PartySignals</c>); the summons is a signed <see cref="TravelTicket"/>, so it cannot be forged by a member.
	/// </remarks>
	[Serializable]
	public class CallToArmsAction : BaseAction
	{
		[Tooltip("Seconds each summoned party member has to accept.")]
		public float AcceptSeconds = 60.0f;

		/// <summary>What the caster is told when cast inside an instance.</summary>
		public const string InInstanceText = "You cannot call your party into an instance.";

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!EcaAuthority.IsServer(initiator, eventData) || initiator is not IPlayerCharacter player)
			{
				return;
			}

			TravelSpellRefusal refusal = TravelSpellRules.DecideOpening(
				CharacterStateValidation.CanAct(player),
				TravelSpellFeedback.InArena(player),
				NoTravelZone.Blocks(player),
				player.IsInInstance());
			if (refusal != TravelSpellRefusal.None)
			{
				TravelSpellFeedback.Toast(player, TravelSpellRules.Describe(refusal, null, InInstanceText));
				return;
			}

			IGateHost host = GateHost.Current;
			string text = host == null ? GateHost.UnavailableText : host.CallToArms(player, new CallToArmsSettings()
			{
				AcceptSeconds = Mathf.Clamp(AcceptSeconds, 10.0f, 300.0f),
			});
			TravelSpellFeedback.Toast(player, text);
		}

		public override string GetTooltipContribution()
		{
			return "Calls your party to your side";
		}
	}
}
