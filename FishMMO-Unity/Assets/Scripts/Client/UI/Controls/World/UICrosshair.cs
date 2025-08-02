using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages the crosshair UI element, showing or hiding it based on the mouse mode.
	/// </summary>
	public class UICrosshair : UIControl
	{
		/// <summary>
		/// The image component used to display the crosshair.
		/// </summary>
		public Image Image;

		/// <summary>
		/// Called when the crosshair UI is starting. Subscribes to mouse mode toggle event.
		/// </summary>
		public override void OnStarting()
		{
			InputManager.OnToggleMouseMode += OnToggleMouseMode;
		}

		/// <summary>
		/// Called when the crosshair UI is being destroyed. Unsubscribes from mouse mode toggle event.
		/// </summary>
		public override void OnDestroying()
		{
			InputManager.OnToggleMouseMode -= OnToggleMouseMode;
		}

		/// <summary>
		/// Handles mouse mode toggle event. Hides crosshair when mouse mode is enabled, shows when disabled.
		/// </summary>
		/// <param name="mouseMode">True if mouse mode is enabled, false otherwise.</param>
		public void OnToggleMouseMode(bool mouseMode)
		{
			if (mouseMode)
			{
				Hide();
			}
			else
			{
				Show();
			}
		}
	}
}