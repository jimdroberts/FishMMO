using System;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Recall spell's effect: sends the caster to its marked location (issue #241), in this scene or another.
	/// </summary>
	/// <remarks>
	/// <para>Server only, and always the caster: put it on an <c>AbilityOnHitEvent</c> with an initiator target selector on a
	/// self-cast ability. Castable in combat; the ability should carry <c>InterruptedByDamage</c>.</para>
	/// <para><b>Channel.</b> A mark in the scene the caster is standing in is a local move in the caster's own channel, so
	/// recalling never splits a group across channels of one scene. From any other scene (or from inside an instance) the
	/// caster is handed to the mark's own channel when it still runs and has room, and to another channel of the scene
	/// otherwise.</para>
	/// </remarks>
	[Serializable]
	public class RecallAction : BaseAction
	{
		/// <summary>What the caster is told when it has no mark.</summary>
		public const string NoMarkText = "You have not marked a location. Cast Mark first.";

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!EcaAuthority.IsServer(initiator, eventData) || initiator is not IPlayerCharacter player ||
				!player.TryGet(out ITravelMarkController marks))
			{
				return;
			}

			TravelSpellRefusal refusal = TravelSpellRules.Decide(
				CharacterStateValidation.CanAct(player),
				TravelSpellFeedback.InArena(player),
				NoTravelZone.Blocks(player),
				marks.HasMark);

			if (refusal == TravelSpellRefusal.None)
			{
				TransferDestination destination = new TransferDestination(
					marks.SceneName,
					RecallChannel(player.CurrentSceneName(), player.IsInInstance(), marks.SceneName, marks.SceneID),
					marks.Position,
					marks.Rotation);

				if (CharacterTransfer.Transfer(player, destination, out TransferRefusal transferRefusal) == TransferOutcome.Refused)
				{
					Log.Debug("RecallAction", $"Character {player.ID} was not recalled to {marks.SceneName}: {transferRefusal}.");
					refusal = TravelSpellRefusal.TransferFailed;
				}
			}

			if (refusal != TravelSpellRefusal.None)
			{
				TravelSpellFeedback.Toast(player, TravelSpellRules.Describe(refusal, NoMarkText));
			}
		}

		/// <summary>
		/// The channel a Recall asks for: none (stay in this channel) when the mark is in the open-world scene the caster stands
		/// in, otherwise the mark's own.
		/// </summary>
		public static long RecallChannel(string currentScene, bool inInstance, string markScene, long markSceneID)
		{
			return !inInstance && string.Equals(currentScene, markScene, StringComparison.Ordinal) ? 0L : markSceneID;
		}

		public override string GetTooltipContribution()
		{
			return "Returns you to your marked location";
		}
	}
}
