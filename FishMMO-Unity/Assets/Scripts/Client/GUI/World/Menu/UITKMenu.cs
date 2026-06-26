using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit main menu control. Provides access to the options panel, returning to the
	/// login screen, and quitting the game.
	/// </summary>
	public class UITKMenu : UITKControl
	{
		/// <summary>Name of the options button element in the UXML.</summary>
		private const string OPTIONS_BUTTON_NAME = "menu-options-btn";
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
			}
		}

		/// <summary>
		/// Returns the player to the login screen.
		/// </summary>
		public void OnButtonQuitToLogin()
		{
			Client.QuitToLogin();
		}

		/// <summary>
		/// Exits the game client.
		/// </summary>
		public void OnButtonQuit()
		{
			Client.Quit();
		}
	}
}
