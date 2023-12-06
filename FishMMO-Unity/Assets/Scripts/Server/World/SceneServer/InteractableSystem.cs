using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// This service helps the server validate clients interacting with Interactable objects in scenes.
	/// </summary>
	public class InteractableSystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived);
			}
		}

		/// <summary>
		/// Interactable broadcast received from a character.
		/// </summary>
		private void OnServerInteractableBroadcastReceived(NetworkConnection conn, InteractableBroadcast msg)
		{
			if (conn == null)
			{
				return;
			}

			// validate connection character
			if (conn.FirstObject == null)
			{
				return;
			}
			Character character = conn.FirstObject.GetComponent<Character>();
			if (character == null)
			{
				return;
			}

			// valid object exists
			if (!SceneObjectUID.IDs.TryGetValue(msg.InteractableID, out SceneObjectUID sceneObject))
			{
				return;
			}

			// valid scene match
			if (sceneObject.gameObject.scene.handle != character.SceneHandle)
			{
				return;
			}

			IInteractable interactable = sceneObject.GetComponent<IInteractable>();
			interactable?.OnInteract(character);
		}
	}
}