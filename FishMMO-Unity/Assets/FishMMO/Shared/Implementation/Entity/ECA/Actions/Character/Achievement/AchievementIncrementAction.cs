using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that increments achievement progress for a character.
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class AchievementIncrementAction : BaseAction
	{
		/// <summary>
		/// The achievement template to increment.
		/// </summary>
		[Tooltip("The achievement template to increment.")]
		public AchievementTemplate AchievementTemplate;

		/// <summary>
		/// The value provider that determines the amount to increment by.
		/// </summary>
		[Tooltip("The value provider that determines the amount to increment the achievement by.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider AmountValue;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* Server only, decided at runtime rather than by a build define.
			 *
			 * This body was wrapped in `#if UNITY_SERVER`, which is a BUILD TARGET define and is
			 * undefined in the editor the scene server is developed in — so the action compiled away
			 * entirely there and did nothing on the server either. That failure is invisible: the
			 * action still exists, still serialises, and its trigger still fires; it simply never has
			 * an effect, which reads as "the quest/item/achievement hook is broken" rather than as a
			 * build-configuration problem. EcaAuthority asks the question that was meant all along,
			 * of the peer the character actually belongs to. See EcaAuthority's own remarks. */
			if (AchievementTemplate == null || initiator == null)
			{
				return;
			}

			if (AmountValue == null)
			{
				Log.Warning("AchievementIncrementAction", "AmountValue provider is null.");
				return;
			}

			/* Drawn BEFORE the peer gate, never after — see AbilityObject.RNG. A provider may
			 * consume the ability object's generator, which every action in the event chain shares,
			 * so evaluating it behind the gate advanced it only on the server and left an ungated
			 * action later in the chain reading a different number. The two guards above may
			 * precede it — both are authoring faults that answer the same on every peer — but the
			 * gate may not, and neither may the controller lookup below: an achievement controller
			 * is a server-side component, so a client would have returned before drawing. */
			int value = AmountValue.GetValue(initiator, eventData);

			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return;
			}

			if (!initiator.TryGet(out IAchievementController achievementController))
			{
				return;
			}

			if (value < 1)
			{
				return;
			}
			achievementController.Increment(AchievementTemplate, (uint)value);
		}
	}
}