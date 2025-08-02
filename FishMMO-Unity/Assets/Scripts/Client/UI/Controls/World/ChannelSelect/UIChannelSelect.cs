using UnityEngine;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIChannelSelect : UICharacterControl
	{
		/// <summary>
		/// The RectTransform that contains the channel selection UI elements.
		/// </summary>
		public RectTransform content;

		/// <summary>
		/// Called when the client is set. Registers the broadcast handler for scene channel list updates.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<SceneChannelListBroadcast>(OnClientSceneChannelListBroadcastReceived);
		}

		/// <summary>
		/// Called when the client is unset. Unregisters the broadcast handler for scene channel list updates.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<SceneChannelListBroadcast>(OnClientSceneChannelListBroadcastReceived);
		}

		/// <summary>
		/// Called when the UI is being destroyed. Cleans up channel selection slots.
		/// </summary>
		public override void OnDestroying()
		{
			DestroySlots();
		}

		/// <summary>
		/// Destroys all channel selection slots in the UI. (Implementation pending)
		/// </summary>
		private void DestroySlots()
		{
		}

		/// <summary>
		/// Handles the broadcast message for scene channel list updates.
		/// Shows or hides the UI based on character presence.
		/// </summary>
		/// <param name="msg">The broadcast message containing channel list info.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientSceneChannelListBroadcastReceived(SceneChannelListBroadcast msg, Channel channel)
		{
			// If no character is present, hide the UI. Otherwise, show it.
			if (Character == null)
			{
				Hide();
				return;
			}
			Show();
		}

		/// <summary>
		/// Called before the character is set. (No implementation)
		/// </summary>
		public override void OnPreSetCharacter()
		{
		}

		/// <summary>
		/// Called after the character is set. (No implementation)
		/// </summary>
		public override void OnPostSetCharacter()
		{
		}
	}
}