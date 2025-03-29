using UnityEngine;
using FishNet.Transporting;
using FishMMO.Shared;
using TMPro;

namespace FishMMO.Client
{
	public class UIDungeonFinder : UICharacterControl
	{
		public RectTransform Content;
		public TMP_Text DungeonDescriptionLabel;

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

			if (!SceneObject.Objects.TryGetValue(msg.InteractableID, out ISceneObject sceneObject))
			{
				if (sceneObject == null)
				{
					Debug.Log("Missing SceneObject");
				}
				else
				{
					Debug.Log("Missing ID:" + msg.InteractableID);
				}
				return;
			}

			if (sceneObject is DungeonEntrance dungeonEntrance)
			{
				if (DungeonDescriptionLabel != null)
				{
					DungeonDescriptionLabel.text = dungeonEntrance.DungeonName;
				}
				Show();
			}
		}

		public override void OnPreSetCharacter()
		{
		}

		public override void OnPostSetCharacter()
		{
		}
	}
}