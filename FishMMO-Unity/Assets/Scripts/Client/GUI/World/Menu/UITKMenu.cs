using UnityEngine.UIElements;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit main menu control. Provides access to the options panel, returning to the
	/// login screen, and quitting the game.
	/// </summary>
	/// <remarks>
	/// Both destructive actions ask first. They sit directly under Resume and Options in a panel
	/// the Escape key opens mid-combat, so a single mis-aimed click used to drop the player to the
	/// login screen or close the client outright — with a character still in the world, and no way
	/// to take it back.
	/// </remarks>
	public class UITKMenu : UITKControl
	{
		/// <summary>Name of the shared confirmation dialog.</summary>
		private const string DIALOG_NAME = "UIDialogBox";

		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Menu;

		/// <summary>Name of the options button element in the UXML.</summary>
		private const string OPTIONS_BUTTON_NAME = "menu-options-btn";
		/// <summary>Name of the scene-channel picker button element in the UXML.</summary>
		private const string CHANNELS_BUTTON_NAME = "menu-channels-btn";
		/// <summary>Name of the instance-management button element in the UXML.</summary>
		private const string INSTANCE_BUTTON_NAME = "menu-instance-btn";
		/// <summary>Name of the quit-to-login button element in the UXML.</summary>
		private const string QUIT_TO_LOGIN_BUTTON_NAME = "menu-quit-to-login-btn";
		/// <summary>Name of the quit button element in the UXML.</summary>
		private const string QUIT_BUTTON_NAME = "menu-quit-btn";
		/// <summary>Name of the close button element in the UXML.</summary>
		private const string CLOSE_BUTTON_NAME = "menu-close-btn";

		/// <summary>
		/// Resolves and wires the menu buttons.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			Button optionsButton = Root.Q<Button>(OPTIONS_BUTTON_NAME);
			if (optionsButton != null)
			{
				optionsButton.clicked += OnButtonOptions;
			}

			Button channelsButton = Root.Q<Button>(CHANNELS_BUTTON_NAME);
			if (channelsButton != null)
			{
				channelsButton.clicked += OnButtonChannels;
			}

			Button instanceButton = Root.Q<Button>(INSTANCE_BUTTON_NAME);
			if (instanceButton != null)
			{
				instanceButton.clicked += OnButtonInstance;
			}

			Button quitToLoginButton = Root.Q<Button>(QUIT_TO_LOGIN_BUTTON_NAME);
			if (quitToLoginButton != null)
			{
				quitToLoginButton.clicked += OnButtonQuitToLogin;
			}

			Button quitButton = Root.Q<Button>(QUIT_BUTTON_NAME);
			if (quitButton != null)
			{
				quitButton.clicked += OnButtonQuit;
			}

			Button closeButton = Root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}
		}

		/// <summary>
		/// Shows the options panel if available.
		/// </summary>
		public void OnButtonOptions()
		{
			if (UIManager.TryGetTK("UIOptions", out UITKOptions uiOptions))
			{
				uiOptions.Show();
				return;
			}

			ReportUnavailable("Options are not available right now.");
		}

		/// <summary>
		/// Opens the scene-channel picker.
		/// </summary>
		/// <remarks>
		/// Reached from here rather than from a key binding because a channel switch is a rare,
		/// deliberate act — it releases the character and reconnects them through the world server
		/// — and because the menu is the one panel a player can always open. The picker asks the
		/// server for a fresh list as it opens; nothing is armed by this click.
		/// <para>
		/// The menu stays open behind it. Closing here would leave a player who opened the picker
		/// by mistake with nothing on screen and no way back to Resume.
		/// </para>
		/// </remarks>
		public void OnButtonChannels()
		{
			if (UIManager.TryGetTK("UISceneChannel", out UITKSceneChannel sceneChannel))
			{
				sceneChannel.Show();
				return;
			}

			/* Reported, not swallowed. The picker lives in ClientWorldGUI while this menu can be
			 * reached from anywhere that loads it, so "no panel answered to that name" is a real
			 * state — and a button that does nothing at all, twice, is indistinguishable from a
			 * frozen client. */
			ReportUnavailable("Channel selection is not available right now.");
		}

		/// <summary>
		/// Tells the player that a menu destination could not be opened.
		/// </summary>
		/// <param name="message">What could not be opened.</param>
		/// <remarks>
		/// Best-effort, and deliberately silent when even the dialog is missing: this is already
		/// the failure path, and the alternative is an exception raised from a button handler.
		/// </remarks>
		private static void ReportUnavailable(string message)
		{
			if (UIManager.TryGetTK(DIALOG_NAME, out UITKDialogBox dialog))
			{
				dialog.Open(message);
				return;
			}

			Log.Warning("UITKMenu", message);
		}

		/// <summary>
		/// Opens the instance-management panel.
		/// </summary>
		/// <remarks>
		/// Offered unconditionally rather than only while the player is in a dungeon. The menu is
		/// built once and the panel is what knows whether there is an instance to describe — it
		/// asks the server on every open and says "You are not in a dungeon" when there is not,
		/// which is a better answer than a button that is sometimes missing for reasons the player
		/// has to infer.
		/// <para>
		/// The menu stays open behind it, as it does for Options and Channels: closing here would
		/// leave a player who opened it by mistake with no way back to Resume.
		/// </para>
		/// </remarks>
		public void OnButtonInstance()
		{
			if (UIManager.TryGetTK("UIInstance", out UITKInstance instance))
			{
				instance.Show();
				return;
			}

			ReportUnavailable("Dungeon information is not available right now.");
		}

		/// <summary>
		/// Asks for confirmation, then returns the player to the login screen.
		/// </summary>
		public void OnButtonQuitToLogin()
		{
			Confirm("Return to the login screen?", () => Client.QuitToLogin());
		}

		/// <summary>
		/// Asks for confirmation, then exits the game client.
		/// </summary>
		public void OnButtonQuit()
		{
			Confirm("Quit to desktop?", () => Client.Quit());
		}

		/// <summary>
		/// Runs an action behind a confirmation dialog.
		/// </summary>
		/// <param name="question">The question to put to the player.</param>
		/// <param name="onAccept">What to do if they agree.</param>
		/// <remarks>
		/// <para>Three outcomes, and the middle one was being handled as the wrong one. If the
		/// dialog panel does not exist at all the action still runs — refusing to quit because a
		/// confirmation panel could not be found would leave the player with a menu whose exit
		/// buttons do nothing, which is a worse failure than the one being guarded against.</para>
		/// <para>But <c>UITKDialogBox.Open</c> now <b>refuses rather than replaces</b> when another
		/// question is already on screen, and a refusal returns exactly the same <c>false</c> as
		/// "no dialog panel". The combined test therefore fell through to <c>onAccept</c> and
		/// quit to desktop — or dropped the character to the login screen — <b>without asking</b>,
		/// while a dialog the player was in the middle of answering sat on top of it. That is the
		/// precise failure the confirmation exists to prevent, reached by the guard itself.</para>
		/// <para>A busy dialog is now a no-op. The player is already being asked something; the
		/// menu is still open behind it, and the button is still there when they are done.</para>
		/// </remarks>
		private void Confirm(string question, System.Action onAccept)
		{
			if (!UIManager.TryGetTK(DIALOG_NAME, out UITKDialogBox dialog))
			{
				// No confirmation panel in this scene at all — proceed rather than dead-end.
				onAccept?.Invoke();
				return;
			}

			// Busy: another question owns the dialog. Do nothing at all, and above all do not run
			// a destructive action the player has not confirmed.
			dialog.Open(question, onAccept);
		}
	}
}
