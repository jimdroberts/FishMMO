using System.Collections.Generic;
using UnityEngine;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIDungeonFinder : UICharacterControl
	{
		public RectTransform content;

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<DungeonFinderBroadcast>(OnClientDungeonFinderBroadcastReceived);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DungeonFinderBroadcast>(OnClientDungeonFinderBroadcastReceived);
		}

		public override void OnDestroying()
		{
			DestroySlots();
		}

		private void DestroySlots()
		{
		}

		private void OnClientDungeonFinderBroadcastReceived(DungeonFinderBroadcast msg, Channel channel)
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