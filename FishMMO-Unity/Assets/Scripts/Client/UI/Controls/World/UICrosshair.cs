using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UICrosshair : UIControl
	{
		public Image Image;
		public override void OnStarting()
		{
			InputManager.OnToggleMouseMode += OnToggleMouseMode;
		}

		public override void OnDestroying()
		{
			InputManager.OnToggleMouseMode -= OnToggleMouseMode;
		}

		public void OnToggleMouseMode(bool mouseMode)
		{
			if (mouseMode)
			{
				Visible = false;
			}
			else
			{
				Visible = true;
			}
		}
	}
}