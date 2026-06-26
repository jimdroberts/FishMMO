using FishNet.Transporting;
using FishMMO.Shared;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// Death dialog shown when the local player dies. Offers two options:
	/// "Respawn at Bind Point" (always visible) and "Accept Resurrect"
	/// (appears when another player casts resurrect on the corpse).
	/// Sends the appropriate broadcast on button click and hides on success.
	/// </summary>
	public class UITKDeathDialog : UITKControl
	{
		/// <summary>Name of the death message label.</summary>
		private const string MESSAGE_LABEL_NAME = "death-message";
		/// <summary>Name of the respawn button.</summary>
		private const string RESPAWN_BTN_NAME = "death-respawn-btn";
		/// <summary>Name of the resurrect button.</summary>
		private const string RESURRECT_BTN_NAME = "death-resurrect-btn";
		/// <summary>Name of the close button.</summary>
		private const string CLOSE_BTN_NAME = "death-close-btn";

		/// <summary>Label displaying the death message.</summary>
		private Label messageLabel;
		/// <summary>Button to respawn at the bind point.</summary>
		private Button respawnButton;
		/// <summary>Button to accept a resurrect offer.</summary>
		private Button resurrectButton;
		/// <summary>Button to close the dialog.</summary>
		private Button closeButton;

		/// <summary>The ID of the player attempting to resurrect, or 0 if none.</summary>
		private long currentResurrectorID;

		/// <summary>Queries UXML elements and wires button click handlers.</summary>
		public override void OnStarting()
		{
			if (Root == null) return;

			messageLabel = Root.Q<Label>(MESSAGE_LABEL_NAME);
			respawnButton = Root.Q<Button>(RESPAWN_BTN_NAME);
			resurrectButton = Root.Q<Button>(RESURRECT_BTN_NAME);
			closeButton = Root.Q<Button>(CLOSE_BTN_NAME);

			if (respawnButton != null)
				respawnButton.clicked += OnClickRespawn;
			if (resurrectButton != null)
				resurrectButton.clicked += OnClickAcceptResurrect;
			if (closeButton != null)
				closeButton.clicked += Hide;
		}

		/// <summary>Shows the dialog and registers for ResurrectOfferBroadcast. Resets resurrector state.</summary>
		public override void Show()
		{
			base.Show();

			// Register for resurrect offers while the dialog is visible
			if (Client != null)
			{
				Client.NetworkManager.ClientManager.RegisterBroadcast<ResurrectOfferBroadcast>(OnResurrectOfferReceived);
			}

			// Reset state
			currentResurrectorID = 0;
			SetResurrectVisible(false);
		}

		/// <summary>Hides the dialog and unregisters from ResurrectOfferBroadcast.</summary>
		public override void Hide()
		{
			// Unregister broadcast handlers
			if (Client != null)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<ResurrectOfferBroadcast>(OnResurrectOfferReceived);
			}

			base.Hide();
		}

		/// <summary>
		/// Called when the local player dies. Shows "You have died." and the Respawn button.
		/// </summary>
		public void ShowDeathDialog()
		{
			if (messageLabel != null)
				messageLabel.text = "You have died.";

			currentResurrectorID = 0;
			SetResurrectVisible(false);
			Show();
		}

		/// <summary>
		/// Handles a resurrect offer broadcast from another player.
		/// </summary>
		/// <param name="msg">The resurrect offer broadcast message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnResurrectOfferReceived(ResurrectOfferBroadcast msg, Channel channel)
		{
			currentResurrectorID = msg.ResurrectorID;

			if (messageLabel != null)
				messageLabel.text = "You have died.\nA player is attempting to resurrect you.";

			SetResurrectVisible(true);
		}

		/// <summary>
		/// Sends a respawn-at-bind-point request to the server and hides the dialog.
		/// </summary>
		private void OnClickRespawn()
		{
			if (Client != null)
			{
				Client.Broadcast(new RespawnAtBindPointBroadcast(), Channel.Reliable);
			}
			Hide();
		}

		/// <summary>
		/// Sends an accept resurrect request to the server and hides the dialog.
		/// </summary>
		private void OnClickAcceptResurrect()
		{
			if (Client != null && currentResurrectorID != 0)
			{
				Client.Broadcast(new ResurrectAcceptBroadcast { ResurrectorID = currentResurrectorID }, Channel.Reliable);
			}
			Hide();
		}

		/// <summary>
		/// Shows or hides the resurrect button.
		/// </summary>
		/// <param name="visible">True to show the resurrect button, false to hide it.</param>
		private void SetResurrectVisible(bool visible)
		{
			if (resurrectButton != null)
				resurrectButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
