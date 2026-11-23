using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Gate spell's effect (issue #241): once cast, the caster chooses a discovered Gate Stone on the map, and a portal opens
	/// in front of it to that stone for <see cref="LifetimeSeconds"/>.
	/// </summary>
	/// <remarks>
	/// <para>Server only, and always the caster: put it on an <c>AbilityOnHitEvent</c> with an initiator target selector on a
	/// self-cast ability, like the other travel spells. The cast itself is short; the choice that follows is the long part,
	/// held by the server (<see cref="IGateHost.BeginChoice"/>) for up to <see cref="ChooseSeconds"/> and ended by choosing,
	/// cancelling (closing the map), damage or the time running out.</para>
	/// <para>Castable in combat. Refused in an arena, a <see cref="NoTravelZone"/> and an instance (no gate leads into or out
	/// of one).</para>
	/// </remarks>
	[Serializable]
	public class OpenGateAction : BaseAction
	{
		[Tooltip("Seconds the gate stays open once a Gate Stone is chosen.")]
		public float LifetimeSeconds = 30.0f;

		[Tooltip("Seconds the caster has to choose a Gate Stone on the map.")]
		public float ChooseSeconds = 60.0f;

		[Tooltip("How far in front of the caster the portal opens, in metres.")]
		public float ForwardDistance = 3.0f;

		[Tooltip("The portal's walk-in radius, in metres.")]
		public float Radius = 1.2f;

		[Tooltip("Who may step through the portal.")]
		public GateAccess Access = GateAccess.Anyone;

		/// <summary>What the caster is told when cast inside an instance.</summary>
		public const string InInstanceText = "You cannot open a gate inside an instance.";

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
			string text = host == null ? GateHost.UnavailableText : host.BeginChoice(player, Settings);
			TravelSpellFeedback.Toast(player, text);
		}

		/// <summary>The template's settings, clamped.</summary>
		public GateSettings Settings => new GateSettings()
		{
			LifetimeSeconds = GateRules.ClampLifetime(LifetimeSeconds),
			ChooseSeconds = GateRules.ClampChoose(ChooseSeconds),
			ForwardDistance = Mathf.Clamp(ForwardDistance, 1.0f, 10.0f),
			Radius = GateRules.ClampRadius(Radius),
			Access = Access,
		};

		public override string GetTooltipContribution()
		{
			string who = Access switch
			{
				GateAccess.PartyAndGuild => "your party and guild",
				GateAccess.Party => "your party",
				GateAccess.CasterOnly => "you",
				_ => "anyone",
			};
			return $"Opens a portal to a Gate Stone you have discovered for {GateRules.ClampLifetime(LifetimeSeconds):0} seconds, for {who}";
		}
	}
}
