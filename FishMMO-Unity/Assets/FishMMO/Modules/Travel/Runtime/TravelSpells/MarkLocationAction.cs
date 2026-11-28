using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Mark spell's effect: records where the caster stands as its one marked location, replacing any earlier mark
	/// (issue #241). Recall sends it back there.
	/// </summary>
	/// <remarks>
	/// <para>Server only, and always the caster: put it on the spawn event of an ability whose object is an invisible pulse at
	/// the caster's feet (<c>PointBlank</c>), after the effects that event plays, so observers see the cast.</para>
	/// <para>Refused in an arena, in a <see cref="NoTravelZone"/>, inside an instance, and anywhere the caster could not safely
	/// be put back down later (<see cref="StandingSpot"/>, the bindstone's rule). A plot the caster may not enter needs no rule
	/// of its own: the Housing module moves trespassers off a plot, on arrival as at any other time.</para>
	/// </remarks>
	[Serializable]
	public class MarkLocationAction : BaseAction
	{
		[Tooltip("Total vertical clearance the spot needs, in metres from the ground. Always at least the character's height + 1m.")]
		public float RequiredClearance = StandingSpot.DefaultRequiredClearance;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!EcaAuthority.IsServer(initiator, eventData) || initiator is not IPlayerCharacter player ||
				!player.TryGet(out ITravelMarkController marks))
			{
				return;
			}

			bool canAct = CharacterStateValidation.CanAct(player);
			string rejection = null;
			TravelSpellRefusal refusal = TravelSpellRules.DecideMark(
				canAct,
				TravelSpellFeedback.InArena(player),
				NoTravelZone.Blocks(player),
				player.IsInInstance(),
				canAct && StandingSpot.IsValid(player, RequiredClearance, out rejection));

			if (refusal != TravelSpellRefusal.None)
			{
				if (rejection != null)
				{
					Log.Debug("MarkLocationAction", $"Character {player.ID} cannot mark here: {rejection}");
				}
				TravelSpellFeedback.Toast(player, TravelSpellRules.Describe(refusal, null));
				return;
			}

			Transform motor = player.Motor.Transform;
			marks.SetMark(player.SceneName, player.SceneHandle, motor.position, motor.rotation.eulerAngles.y);
			TravelSpellFeedback.Toast(player, "Location marked.", ToastSeverity.Success);
		}

		public override string GetTooltipContribution()
		{
			return "Marks this spot for Recall";
		}
	}
}
