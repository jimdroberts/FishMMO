using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Transporting;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles mailbox interactions. Sends a <see cref="MailboxBroadcast"/> to the client to open the mail UI.
	/// Follow-up mail operations (fetch, send, delete) are handled by <see cref="InteractableSystem"/> partial.
	/// </summary>
	[HandlesInteractable(typeof(Mailbox))]
	public class MailboxHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public MailboxHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			server.NetworkWrapper.Broadcast(character.Owner, new MailboxBroadcast()
			{
				InteractableID = sceneObject.ID,
			}, true, Channel.Reliable);

			// Increment achievement
			IMailbox mailbox = interactable as IMailbox;
			if (mailbox != null &&
				mailbox.AchievementTemplate != null &&
				character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(mailbox.AchievementTemplate, 1);
			}
		}
	}
}