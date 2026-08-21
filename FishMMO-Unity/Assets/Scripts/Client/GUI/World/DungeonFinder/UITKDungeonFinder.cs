using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit dungeon finder. Listens for <see cref="DungeonFinderBroadcast"/> messages,
	/// displays the dungeon image and name, and broadcasts a request to start the dungeon when
	/// the player confirms.
	/// </summary>
	public class UITKDungeonFinder : UITKCharacterControl
	{
		/// <summary>Name of the dungeon image element inside the UXML.</summary>
		private const string IMAGE_NAME = "dungeonfinder-image";

		/// <summary>Name of the dungeon description label inside the UXML.</summary>
		private const string DESCRIPTION_NAME = "dungeonfinder-description";

		/// <summary>Name of the start button inside the UXML.</summary>
		private const string START_BUTTON_NAME = "dungeonfinder-start-btn";

		/// <summary>Name of the close button inside the UXML.</summary>
		private const string CLOSE_BUTTON_NAME = "dungeonfinder-close-btn";

		/// <summary>The image element representing the dungeon entrance.</summary>
		private VisualElement dungeonImage;

		/// <summary>The label displaying the dungeon name.</summary>
		private Label dungeonDescriptionLabel;

		/// <summary>The interactable ID of the current dungeon entrance.</summary>
		private long currentInteractableID;

		/// <summary>
		/// Queries the dungeon finder elements and wires the start and close buttons.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			dungeonImage = root.Q(IMAGE_NAME);
			dungeonDescriptionLabel = root.Q<Label>(DESCRIPTION_NAME);

			Button startButton = root.Q<Button>(START_BUTTON_NAME);
			if (startButton != null)
			{
				startButton.clicked += OnClick_Start;
			}

			Button closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}
		}

		/// <summary>
		/// Registers the dungeon finder broadcast handler when the client is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<DungeonFinderBroadcast>(OnClientDungeonFinderBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the dungeon finder broadcast handler when the client is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DungeonFinderBroadcast>(OnClientDungeonFinderBroadcastReceived);
		}

		/// <summary>
		/// Handles broadcast messages for dungeon finder updates, populating the image and name.
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
					Log.Debug("UITKDungeonFinder", "Missing SceneObject");
				}
				else
				{
					Log.Debug("UITKDungeonFinder", "Missing ID:" + msg.InteractableID);
				}
				return;
			}

			if (sceneObject is DungeonEntrance dungeonEntrance)
			{
				currentInteractableID = msg.InteractableID;

				if (dungeonImage != null && dungeonEntrance.DungeonImage != null)
				{
					dungeonImage.style.backgroundImage = new StyleBackground(dungeonEntrance.DungeonImage);
				}
				if (dungeonDescriptionLabel != null)
				{
					dungeonDescriptionLabel.text = dungeonEntrance.DungeonName;
				}

				Show();
			}
		}

		/// <summary>
		/// Broadcasts a request to start the current dungeon.
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
