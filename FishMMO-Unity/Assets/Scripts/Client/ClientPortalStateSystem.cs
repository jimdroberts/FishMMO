using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Writes the server's portal reports into <see cref="PortalClientStates"/>, which the portals
	/// themselves read to choose how they look.
	/// </summary>
	/// <remarks>
	/// Lives outside any panel and any character for the same reason as
	/// <see cref="ClientInteractableStateSystem"/>: a portal's state is world state. A
	/// <see cref="SceneTeleporter"/> portal has no NetworkObject, so there is no object a message
	/// could be addressed to; the report names the scene and index instead.
	/// </remarks>
	public static class ClientPortalStateSystem
	{
		private static Client client;

		/// <summary>Registers the two portal broadcasts.</summary>
		public static void Initialize(Client client)
		{
			if (client == null || ClientPortalStateSystem.client != null)
			{
				return;
			}

			ClientPortalStateSystem.client = client;

			Client.NetworkManager.ClientManager.RegisterBroadcast<PortalStatesBroadcast>(OnClientPortalStatesBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<PortalStateChangedBroadcast>(OnClientPortalStateChangedBroadcastReceived);
		}

		/// <summary>Unregisters them and forgets every reported state.</summary>
		public static void Destroy()
		{
			if (client == null)
			{
				return;
			}

			if (Client.NetworkManager != null)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<PortalStatesBroadcast>(OnClientPortalStatesBroadcastReceived);
				Client.NetworkManager.ClientManager.UnregisterBroadcast<PortalStateChangedBroadcast>(OnClientPortalStateChangedBroadcastReceived);
			}

			PortalClientStates.Clear();
			client = null;
		}

		private static void OnClientPortalStatesBroadcastReceived(PortalStatesBroadcast msg, Channel channel)
		{
			PortalClientStates.ApplyScene(msg.SceneName, msg.Indices, msg.RemainingSeconds);
		}

		private static void OnClientPortalStateChangedBroadcastReceived(PortalStateChangedBroadcast msg, Channel channel)
		{
			PortalClientStates.Apply(msg.SceneName, msg.Index, msg.RemainingSeconds);
		}
	}
}
