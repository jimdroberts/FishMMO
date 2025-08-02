#if !UNITY_SERVER
using UnityEngine;
using UnityEngine.UI;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for managing and displaying dungeon finder information and interactions.
	/// </summary>
	public class UIDungeonFinder : UICharacterControl
	{
		/// <summary>
		/// The parent RectTransform for dungeon finder content.
		/// </summary>
		public RectTransform Content;

		/// <summary>
		/// The image representing the dungeon entrance.
		/// </summary>
		public Image DungeonImage;

		/// <summary>
		/// The label displaying the dungeon description or name.
		/// </summary>
		public TMP_Text DungeonDescriptionLabel;

		/// <summary>
		/// The interactable ID of the current dungeon entrance.
		/// </summary>
		private long currentInteractableID;

		/// <summary>
		/// Called when the client is set. Registers broadcast handler for dungeon finder updates.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<DungeonFinderBroadcast>(OnClientDungeonFinderBroadcastReceived);
		}

		/// <summary>
		/// Called when the client is unset. Unregisters broadcast handler for dungeon finder updates.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DungeonFinderBroadcast>(OnClientDungeonFinderBroadcastReceived);
		}

		/// <summary>
		/// Called when the UI is being destroyed. Cleans up dungeon finder slots.
		/// </summary>
		public override void OnDestroying()
		{
			DestroySlots();
		}

		/// <summary>
		/// Destroys all dungeon finder slots. (Currently empty, placeholder for future logic.)
		/// </summary>
		private void DestroySlots()
		{
		}

		/// <summary>
		/// Handles broadcast message for dungeon finder updates. Updates UI with dungeon info.
		/// </summary>
		/// <param name="msg">The broadcast message containing dungeon finder data.</param>
		/// <param name="channel">The network channel.</param>
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
					Log.Debug("UIDungeonFinder", "Missing SceneObject");
				}
				else
				{
					Log.Debug("UIDungeonFinder", "Missing ID:" + msg.InteractableID);
				}
				return;
			}

			if (sceneObject is DungeonEntrance dungeonEntrance)
			{
				currentInteractableID = msg.InteractableID;
				if (DungeonImage != null)
				{
					DungeonImage = dungeonEntrance.DungeonImage;
				}
				if (DungeonDescriptionLabel != null)
				{
					DungeonDescriptionLabel.text = dungeonEntrance.DungeonName;
				}
				Show();
			}
		}

		/// <summary>
		/// Called before setting the character reference. (Currently empty, placeholder for future logic.)
		/// </summary>
		public override void OnPreSetCharacter()
		{
		}

		/// <summary>
		/// Called after setting the character reference. (Currently empty, placeholder for future logic.)
		/// </summary>
		public override void OnPostSetCharacter()
		{
		}

		/// <summary>
		/// Handles the start button click. Broadcasts a request to start the dungeon.
		/// </summary>
		public void OnClick_Start()
		{
			if (currentInteractableID == 0)
			{
				return;
			}

			Client.Broadcast(new DungeonFinderBroadcast()
			{
				InteractableID = currentInteractableID,
			});
		}
	}
}
#endif