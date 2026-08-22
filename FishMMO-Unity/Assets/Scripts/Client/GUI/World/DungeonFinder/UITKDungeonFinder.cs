using UnityEngine;
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
		/// Dungeon preview image last received, held as data rather than written straight to the
		/// element. See <see cref="ApplyDungeon"/>.
		/// </summary>
		private Sprite currentImage;

		/// <summary>Dungeon name last received.</summary>
		private string currentName;

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
				currentImage = dungeonEntrance.DungeonImage;
				currentName = dungeonEntrance.DungeonName;

				/* Show first, then render. Enabling the document re-clones the UXML, so the image
				 * and the name written before this line belonged to a tree that was discarded
				 * microseconds later and the panel opened blank. Show() calls OnAfterShow, which
				 * does the writing. */
				Show();

				// Already visible: Show is a no-op and OnAfterShow never ran, so render directly.
				ApplyDungeon();
			}
		}

		/// <summary>
		/// Writes the pending dungeon into the tree the player will actually see.
		/// </summary>
		protected override void OnAfterShow()
		{
			ApplyDungeon();
		}

		/// <summary>
		/// Writes the pending dungeon again after the visual tree has been rebuilt.
		/// </summary>
		/// <remarks>
		/// Both hooks are needed: on a panel's first open <c>hasStarted</c> is still false and the
		/// tree-replacement check bails out before <c>OnAfterShow</c> would help, while
		/// <c>OnAfterStarting</c> alone misses every later reopen.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyDungeon();
		}

		/// <summary>
		/// Pushes the stored dungeon image and name onto the live elements.
		/// </summary>
		private void ApplyDungeon()
		{
			if (dungeonImage != null)
			{
				/* The null case writes an empty background rather than skipping the assignment.
				 * Leaving the previous sprite in place is what made a cleared panel still show the
				 * last dungeon's artwork under a blank name, and it also held a reference to that
				 * sprite for as long as the element lived. */
				dungeonImage.style.backgroundImage = currentImage != null
					? new StyleBackground(currentImage)
					: new StyleBackground();
			}
			if (dungeonDescriptionLabel != null)
			{
				dungeonDescriptionLabel.text = currentName ?? string.Empty;
			}
		}

		/// <summary>
		/// Broadcasts a request to start the current dungeon and closes the panel.
		/// </summary>
		/// <remarks>
		/// <para><b>One request per opening.</b> The panel used to stay open with a live Start
		/// button after the request went out, and the request is answered by a disconnect-and-
		/// reroute that takes a database round trip to arrange — so there was a window, seconds
		/// long on a loaded server, in which the player saw nothing happen and clicked again. The
		/// server's ingress guard debounces those, but it debounces them into
		/// <c>SceneTransferRefusalReason.OnCooldown</c>, so the reward for an impatient second
		/// click was "You are travelling too often" on top of a request that was already
		/// succeeding. Clearing the ID and closing removes the second click rather than punishing
		/// it.</para>
		/// <para>Closing loses nothing: the entrance re-sends <see cref="DungeonFinderBroadcast"/>
		/// on the next interaction, which reopens the panel with fresh data. Nothing is armed
		/// server-side by having the panel open, and nothing is left armed by closing it — the
		/// request only exists once this method has sent it, and from that point it is the
		/// server's, cancelled only by its own refusal.</para>
		/// </remarks>
		public void OnClick_Start()
		{
			if (currentInteractableID == 0)
			{
				return;
			}

			long requestedID = currentInteractableID;
			ClearDungeon();
			Hide();

			Client.Broadcast(new DungeonFinderBroadcast()
			{
				InteractableID = requestedID,
			});
		}

		/// <summary>
		/// Drops the pending dungeon so a stale entrance cannot be re-sent.
		/// </summary>
		private void ClearDungeon()
		{
			currentInteractableID = 0;
			currentImage = null;
			currentName = null;
		}

		/// <summary>
		/// Drops the pending dungeon when the character goes away.
		/// </summary>
		/// <remarks>
		/// <c>currentInteractableID</c> is a scene-object handle belonging to the scene the
		/// previous character was standing in. Carrying it across a character switch or a scene
		/// transfer meant a reopened panel showed the old dungeon and Start sent an ID that means
		/// something different — or nothing — on the new server. The server validates the handle
		/// against the character's own scene and refuses, so this was never exploitable; it was a
		/// panel showing one dungeon and a button asking for another.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			ClearDungeon();
			ApplyDungeon();
		}
	}
}
