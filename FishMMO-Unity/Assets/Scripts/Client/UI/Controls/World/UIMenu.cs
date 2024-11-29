namespace FishMMO.Client
{
	public class UIMenu : UIControl
	{
		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void OnButtonOptions()
		{
			if (UIManager.TryGet("UIOptions", out UIOptions uiOptions))
			{
				uiOptions.Show();
			}
		}

		public void OnButtonQuitToLogin()
		{
			Client.QuitToLogin();
		}

		public void OnButtonQuit()
		{
			Client.Quit();
		}
	}
}