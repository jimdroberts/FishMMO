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
	/// <para>
	/// There is deliberately no close button. Respawning or being resurrected are the only
	/// ways out of the dead state, and both are reached through this dialog — a control that
	/// merely dismissed it would leave the player a corpse with no route back, because the
	/// server only re-sends <c>DeathBroadcast</c> on scene entry.
	/// </para>
	/// </summary>
	public class UITKDeathDialog : UITKControl
	{
		/// <summary>Name of the death message label.</summary>
		private const string MESSAGE_LABEL_NAME = "death-message";
		/// <summary>Name of the respawn button.</summary>
		private const string RESPAWN_BTN_NAME = "death-respawn-btn";
		/// <summary>Name of the resurrect button.</summary>
		private const string RESURRECT_BTN_NAME = "death-resurrect-btn";

		/// <summary>Label displaying the death message.</summary>
		private Label messageLabel;
		/// <summary>Button to respawn at the bind point.</summary>
		private Button respawnButton;
		/// <summary>Button to accept a resurrect offer.</summary>
		private Button resurrectButton;

		/// <summary>True once the UXML elements have been resolved and wired.</summary>
		private bool elementsBound;

		/// <summary>The ID of the player attempting to resurrect, or 0 if none.</summary>
		private long currentResurrectorID;

		/// <summary>Queries UXML elements and wires button click handlers.</summary>
		public override void OnStarting()
		{
			EnsureElementsBound();
		}

		/// <summary>
		/// Resolves the UXML elements and wires their handlers, at most once.
		/// </summary>
		/// <remarks>
		/// Not done unconditionally in <see cref="OnStarting"/> because that runs from
		/// <c>Awake</c>, and <see cref="UIDocument.rootVisualElement"/> is not built until the
		/// document's own <c>OnEnable</c>. Unity does not guarantee those run in a useful order
		/// for two components on the same GameObject, so a control that binds only in Awake can
		/// come up with every reference null — silently, because each one is null-checked. This
		/// is idempotent and is also called from the show paths, so the dialog binds on the
		/// first frame it is actually needed regardless of initialisation order.
		/// </remarks>
		private void EnsureElementsBound()
		{
			if (elementsBound || Root == null)
			{
				return;
			}

			messageLabel = Root.Q<Label>(MESSAGE_LABEL_NAME);
			respawnButton = Root.Q<Button>(RESPAWN_BTN_NAME);
			resurrectButton = Root.Q<Button>(RESURRECT_BTN_NAME);

			if (respawnButton != null)
				respawnButton.clicked += OnClickRespawn;
			if (resurrectButton != null)
				resurrectButton.clicked += OnClickAcceptResurrect;

			elementsBound = true;
		}

		/// <summary>
		/// Registers for resurrect offers for as long as a client is attached.
		/// </summary>
		/// <remarks>
		/// Registration is tied to the client, not to the dialog being visible. Doing it in
		/// <see cref="Show"/> registered the same handler again on every call — and the server
		/// re-sends <c>DeathBroadcast</c> whenever a dead character loads into a scene, so a
		/// player who died and then changed scenes accumulated duplicate handlers, each firing
		/// on the same offer. It also meant an offer that arrived while the dialog was hidden
		/// was dropped entirely.
		/// </remarks>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ResurrectOfferBroadcast>(OnResurrectOfferReceived);
		}

		/// <summary>Unregisters the resurrect-offer handler when the client is cleared.</summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ResurrectOfferBroadcast>(OnResurrectOfferReceived);
		}

		/// <summary>Shows the dialog, binding its elements on first use.</summary>
		public override void Show()
		{
			EnsureElementsBound();
			base.Show();
		}

		/// <summary>
		/// Called when the local player dies. Shows "You have died." and the Respawn button.
		/// </summary>
		public void ShowDeathDialog()
		{
			EnsureElementsBound();

			if (messageLabel != null)
				messageLabel.text = "You have died.";

			// Reset here rather than in Show: Show is a no-op when the dialog is already
			// visible, so state reset placed there was skipped exactly when a repeated death
			// broadcast needed it.
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
			EnsureElementsBound();

			currentResurrectorID = msg.ResurrectorID;

			if (messageLabel != null)
				messageLabel.text = "You have died.\nA player is attempting to resurrect you.";

			SetResurrectVisible(true);

			// An offer is only ever sent to a dead player, so surface the dialog rather than
			// assuming it is already up. Show is a no-op when it is.
			Show();
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
		/// Clears any pending resurrect offer when the player returns to the login screen, so a
		/// later session cannot inherit a stale resurrector ID.
		/// </summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			currentResurrectorID = 0;
			SetResurrectVisible(false);
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
