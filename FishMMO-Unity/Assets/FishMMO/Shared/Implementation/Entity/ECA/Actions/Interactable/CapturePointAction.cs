using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that applies one capture interaction to an <see cref="ICapturePoint"/>.
	/// Server-only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This action is what connects capture points to the game at all. Interaction behaviour in
	/// this project is defined entirely by the ECA trigger list on each prefab, and capture points
	/// were the one interactable type with no action to put in that list — so
	/// <see cref="ICapturePoint.ApplyCapture"/> had no callers,
	/// <see cref="CapturePointUpdateBroadcast"/> was never sent, and the component, its template,
	/// its broadcast and <see cref="ObjectiveState"/> were all unreachable code. Interacting with a
	/// capture point did nothing.
	/// </para>
	/// <para>
	/// The action is deliberately thin. Ownership, progress, contest and decay are the capture
	/// point's own business — it is the authority on its state and broadcasts its own changes,
	/// including the decay expiry that no interaction triggers. All this does is apply one
	/// interaction and credit the achievement on the one that completes the capture.
	/// </para>
	/// </remarks>
	[Serializable]
	public class CapturePointAction : BaseAction
	{
		/// <summary>
		/// Applies one capture interaction and awards the capture achievement on completion.
		/// </summary>
		/// <param name="initiator">The character interacting with the capture point.</param>
		/// <param name="eventData">The event data containing the interaction context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (initiator is not IPlayerCharacter player) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			ICapturePoint capturePoint = data.Interactable as ICapturePoint;
			if (capturePoint?.Template == null) return;

			/* False covers two different outcomes — progress was added but the point is not taken
			 * yet, and a rival's attempt was broken — and neither earns the capture achievement.
			 * The capture point has already broadcast whichever one happened. */
			if (!capturePoint.ApplyCapture(player.ID))
			{
				return;
			}

			if (capturePoint.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(capturePoint.AchievementTemplate, 1);
			}
		}
	}
}
