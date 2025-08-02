namespace FishMMO.Client
{
	/// <summary>
	/// Main menu UI control for handling options, quitting to login, and quitting the game.
	/// </summary>
	public class UIMenu : UIControl
	{
		/// <summary>
		/// Called when the Options button is pressed. Shows the options UI if available.
		/// </summary>
		public void OnButtonOptions()
		{
			if (UIManager.TryGet("UIOptions", out UIOptions uiOptions))
			{
				uiOptions.Show();
			}
		}

		/// <summary>
		/// Called when the Quit to Login button is pressed. Returns the player to the login screen.
		/// </summary>
		public void OnButtonQuitToLogin()
		{
			Client.QuitToLogin();
		}

		/// <summary>
		/// Called when the Quit button is pressed. Exits the game client.
		/// </summary>
		public void OnButtonQuit()
		{
			Client.Quit();
		}
	}
}