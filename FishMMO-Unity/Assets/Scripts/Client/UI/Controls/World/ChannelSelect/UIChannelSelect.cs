using System.Collections.Generic;
using UnityEngine;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIChannelSelect : UICharacterControl
	{
		public RectTransform content;

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<SceneChannelListBroadcast>(OnClientSceneChannelListBroadcastReceived);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<SceneChannelListBroadcast>(OnClientSceneChannelListBroadcastReceived);
		}

		public override void OnDestroying()
		{
			DestroySlots();
		}

		private void DestroySlots()
		{
		}

		private void OnClientSceneChannelListBroadcastReceived(SceneChannelListBroadcast msg, Channel channel)
		{
			if (Character == null)
			{
				Hide();
				return;
			}
			Show();
		}

		public override void OnPreSetCharacter()
		{
		}

		public override void OnPostSetCharacter()
		{
		}
	}
}