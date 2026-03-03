using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Transporting;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles capture point interactions. Applies capture progress from the interacting player.
	/// When a point is captured, broadcasts the new state to the interacting player.
	/// The <see cref="CapturePoint"/> component manages internal state and fires static events
	/// that an external objective system can subscribe to.
	/// </summary>
	[HandlesInteractable(typeof(CapturePoint))]
	public class CapturePointHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public CapturePointHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			ICapturePoint capturePoint = interactable as ICapturePoint;
			if (capturePoint == null || capturePoint.Template == null)
			{
				return;
			}

			bool captured = capturePoint.ApplyCapture(character.ID);

			// Broadcast state update to the interacting player
			server.NetworkWrapper.Broadcast(character.Owner, new CapturePointUpdateBroadcast()
			{
				InteractableID = sceneObject.ID,
				TemplateID = capturePoint.Template.ID,
				OwnerCharacterID = capturePoint.OwnerCharacterID,
				State = capturePoint.State,
				CaptureProgress = capturePoint.CaptureProgress,
				InteractionsToCapture = capturePoint.Template.InteractionsToCapture,
			}, true, Channel.Reliable);

			if (captured)
			{
				Log.Debug("InteractableSystem", $"CapturePoint '{capturePoint.Template.Name}' captured by CharID={character.ID}, worth {capturePoint.Template.PointValue} points.");

				// Increment achievement on capture
				if (capturePoint.AchievementTemplate != null &&
					character.TryGet(out IAchievementController achievementController))
				{
					achievementController.Increment(capturePoint.AchievementTemplate, 1);
				}
			}
		}
	}
}