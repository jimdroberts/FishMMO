using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles the initial interaction with a <see cref="DialogueInteractable"/>.
	/// Starts a server-authoritative dialogue session and sends the <see cref="DialogueStartBroadcast"/> to the client.
	/// </summary>
	[HandlesInteractable(typeof(DialogueInteractable))]
	public class DialogueInteractableHandler : IInteractableHandler
	{
		/// <summary>
		/// Server instance for sending broadcasts.
		/// </summary>
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		/// <summary>
		/// Constructs the handler with the server instance for broadcast access.
		/// </summary>
		/// <param name="server">The server instance.</param>
		public DialogueInteractableHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		/// <summary>
		/// Initiates a dialogue session between the player and the dialogue interactable.
		/// Evaluates the start node, creates a server-side session, and broadcasts to the client.
		/// </summary>
		/// <param name="interactable">The dialogue interactable being interacted with.</param>
		/// <param name="character">The player character initiating the dialogue.</param>
		/// <param name="sceneObject">The scene object associated with the interactable.</param>
		/// <param name="interactableSystem">The interactable system managing sessions.</param>
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			IDialogueInteractable dialogue = interactable as IDialogueInteractable;
			if (dialogue == null || dialogue.Template == null)
			{
				return;
			}

			interactableSystem.StartDialogueSession(character, sceneObject, dialogue);
			interactableSystem.OnInteractNPC(character, interactable);
		}
	}
}