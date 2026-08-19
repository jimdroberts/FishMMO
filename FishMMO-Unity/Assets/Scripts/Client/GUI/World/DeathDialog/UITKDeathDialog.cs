using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
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
	/// <para>
	/// Two properties keep a character from getting stuck dead:
	/// <list type="bullet">
	/// <item><description><b>It opens from state, not just from a message.</b> A character that
	/// arrives already dead — a login while dead, or a scene transfer while dead — surfaces the
	/// dialog from <see cref="OnPostSetCharacter"/>, which reads the replicated
	/// <see cref="CharacterFlags.IsDead"/> flag out of the spawn payload. That does not depend
	/// on a <c>DeathBroadcast</c> arriving at the right moment relative to the world GUI scene
	/// finishing its load.</description></item>
	/// <item><description><b>It confirms rather than assumes.</b> Clicking an action used to
	/// hide the dialog immediately, so a request the server declined — its respawn/resurrect
	/// ingress guard debounces at two seconds — left the player dead with no UI and no way to
	/// ask again. The dialog now stays up until the character is actually observed alive, and
	/// re-arms itself if that never happens.</description></item>
	/// </list>
	/// </para>
	/// </summary>
	public class UITKDeathDialog : UITKCharacterControl
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

		/// <summary>True while waiting for the server to act on a respawn or resurrect request.</summary>
		private bool awaitingRevive;
		/// <summary>Unscaled time by which the request must have taken effect.</summary>
		private float awaitingReviveUntil;
		/// <summary>Unscaled time of the next liveness poll.</summary>
		private float nextRevivePollTime;

		/// <summary>
		/// Seconds between liveness polls while a request is in flight.
		/// </summary>
		/// <remarks>
		/// Deliberately not every frame. <c>ICharacterDamageController.IsAlive</c> reads
		/// <c>ResourceInstance</c>, whose getter logs an error on every access when the health
		/// attribute is missing — a per-frame poll would turn one broken character into hundreds
		/// of log lines a second. Four times a second is imperceptible for a dialog dismissal
		/// and bounds that to a handful.
		/// </remarks>
		private const float RevivePollIntervalSeconds = 0.25f;

		/// <summary>
		/// How long to wait for a request to take effect before handing the buttons back.
		/// </summary>
		/// <remarks>
		/// Comfortably longer than the server's two-second respawn/resurrect debounce, so a
		/// request merely queued behind that guard is not reported as a failure, and long enough
		/// to cover a slow database round trip on the cross-scene respawn path.
		/// </remarks>
		private const float ReviveConfirmTimeoutSeconds = 8.0f;

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
		/// Opens the dialog when the character this control was just handed is already dead.
		/// </summary>
		/// <remarks>
		/// This is the login-while-dead and transfer-while-dead path. <c>Flags</c> travels in
		/// the spawn payload, so by the time the local character is injected here the client
		/// already knows it is dead — no broadcast required. The server does also re-send
		/// <c>DeathBroadcast</c> on scene entry, and the two converge on the same idempotent
		/// call; this one is the reliable half, because it cannot be missed by a message
		/// arriving before the world GUI scene carrying this dialog has finished loading.
		/// </remarks>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character != null && Character.IsFlagged(CharacterFlags.IsDead))
			{
				ShowDeathDialog();
				return;
			}

			/* A live character means whatever this dialog was showing is over. This is the
			 * closing half of a cross-scene respawn: that path disconnects the client, so the
			 * dialog is still up showing "Respawning..." while it reconnects and reloads into
			 * the bind scene — and the world GUI scene carrying this dialog is not unloaded by
			 * a server hop, only by a quit to login. Without this the player would arrive alive
			 * with a death dialog still on screen. */
			ClearAwaitingRevive();
			Hide();
		}

		/// <summary>
		/// Drops any in-flight request when the character goes away, so a new session cannot
		/// inherit a pending confirmation.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			// The character is going away — despawn, transfer, or logout. Nothing here applies
			// to whatever comes next, and leaving the panel up would float it over the loading
			// screen of the transfer it just triggered.
			ClearAwaitingRevive();
			Hide();
		}

		/// <summary>
		/// Watches for a requested respawn or resurrect actually taking effect.
		/// </summary>
		/// <remarks>
		/// Runs regardless of whether the panel is showing: <see cref="UITKControl.Hide"/>
		/// disables the <c>UIDocument</c>, not this component, so the confirmation is still
		/// driven while the dialog is hidden.
		/// <para>
		/// Health is the signal rather than <see cref="CharacterFlags.IsDead"/>, because the
		/// flag is only sent in the spawn payload and is never re-synced; the health resource
		/// is replicated continuously, and the server's revive path restores it.
		/// </para>
		/// </remarks>
		private void Update()
		{
			if (!awaitingRevive)
			{
				return;
			}

			// The character left (cross-scene respawn disconnects the client). The dialog goes
			// with the scene, so there is nothing left to confirm.
			if (Character == null)
			{
				ClearAwaitingRevive();
				return;
			}

			float now = Time.unscaledTime;
			if (now >= nextRevivePollTime)
			{
				nextRevivePollTime = now + RevivePollIntervalSeconds;

				if (Character.TryGet(out ICharacterDamageController damageController) &&
					damageController.IsAlive)
				{
					ClearAwaitingRevive();
					Hide();
					return;
				}
			}

			if (now < awaitingReviveUntil)
			{
				return;
			}

			/* The request never took effect — most likely refused by the server's ingress
			 * guard. Hand the buttons back rather than leaving the player staring at a dialog
			 * that has stopped responding, which is the state that used to require a relog. */
			ClearAwaitingRevive();

			if (messageLabel != null)
			{
				messageLabel.text = "That request was not accepted.\nPlease try again.";
			}

			FishMMO.Logging.Log.Warning("UITKDeathDialog",
				$"No revive was observed within {ReviveConfirmTimeoutSeconds:F0}s of the request; re-enabling the death dialog actions.");
		}

		/// <summary>Clears the pending-request state and re-enables the action buttons.</summary>
		private void ClearAwaitingRevive()
		{
			awaitingRevive = false;
			awaitingReviveUntil = 0f;
			nextRevivePollTime = 0f;
			SetActionsEnabled(true);
		}

		/// <summary>
		/// Marks a request as sent and locks the actions until it is confirmed or times out.
		/// </summary>
		/// <param name="pendingMessage">Status text to show while waiting.</param>
		private void BeginAwaitingRevive(string pendingMessage)
		{
			awaitingRevive = true;
			awaitingReviveUntil = Time.unscaledTime + ReviveConfirmTimeoutSeconds;
			nextRevivePollTime = Time.unscaledTime + RevivePollIntervalSeconds;
			SetActionsEnabled(false);

			if (messageLabel != null)
			{
				messageLabel.text = pendingMessage;
			}
		}

		/// <summary>Enables or disables both action buttons.</summary>
		/// <param name="enabled">True to allow input, false while a request is in flight.</param>
		private void SetActionsEnabled(bool enabled)
		{
			if (respawnButton != null)
			{
				respawnButton.SetEnabled(enabled);
			}
			if (resurrectButton != null)
			{
				resurrectButton.SetEnabled(enabled);
			}
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
			ClearAwaitingRevive();
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
			if (Client == null)
			{
				return;
			}

			Client.Broadcast(new RespawnAtBindPointBroadcast(), Channel.Reliable);

			/* Deliberately not Hide(). The server may decline this — its respawn/resurrect
			 * ingress guard debounces per connection — and hiding on the click alone left the
			 * player dead with no dialog and no way to ask again. Update() closes the dialog
			 * once the character is actually alive. */
			BeginAwaitingRevive("Respawning...");
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
			ClearAwaitingRevive();
		}

		/// <summary>
		/// Sends an accept resurrect request to the server and hides the dialog.
		/// </summary>
		private void OnClickAcceptResurrect()
		{
			if (Client == null || currentResurrectorID == 0)
			{
				return;
			}

			Client.Broadcast(new ResurrectAcceptBroadcast { ResurrectorID = currentResurrectorID }, Channel.Reliable);

			// Confirmed the same way as respawn — see OnClickRespawn.
			BeginAwaitingRevive("Accepting resurrect...");
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
