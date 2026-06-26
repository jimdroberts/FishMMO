using FishNet.Transporting;
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
		private const string MESSAGE_LABEL_NAME = "death-message";
		private const string RESPAWN_BTN_NAME = "death-respawn-btn";
		private const string RESURRECT_BTN_NAME = "death-resurrect-btn";
		private const string CLOSE_BTN_NAME = "death-close-btn";

		private Label messageLabel;
		private Button respawnButton;
		private Button resurrectButton;
		private Button closeButton;

		private long currentResurrectorID;

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

		public override void Show()
		{
			base.Show();

			// Register for resurrect offers while the dialog is visible
			if (Client != null)
			{
				Client.NetworkWrapper.RegisterBroadcast<ResurrectOfferBroadcast>(OnResurrectOfferReceived);
			}

			// Reset state
			currentResurrectorID = 0;
			SetResurrectVisible(false);
		}

		public override void Hide()
		{
			// Unregister broadcast handlers
			if (Client != null)
			{
				Client.NetworkWrapper.UnregisterBroadcast<ResurrectOfferBroadcast>(OnResurrectOfferReceived);
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

		private void OnResurrectOfferReceived(ResurrectOfferBroadcast msg, Channel channel)
		{
			currentResurrectorID = msg.ResurrectorID;

			if (messageLabel != null)
				messageLabel.text = "You have died.\nA player is attempting to resurrect you.";

			SetResurrectVisible(true);
		}

		private void OnClickRespawn()
		{
			if (Client != null)
			{
				Client.Broadcast(new RespawnAtBindPointBroadcast(), Channel.Reliable);
			}
			Hide();
		}

		private void OnClickAcceptResurrect()
		{
			if (Client != null && currentResurrectorID != 0)
			{
				Client.Broadcast(new ResurrectAcceptBroadcast { ResurrectorID = currentResurrectorID }, Channel.Reliable);
			}
			Hide();
		}

		private void SetResurrectVisible(bool visible)
		{
			if (resurrectButton != null)
				resurrectButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
