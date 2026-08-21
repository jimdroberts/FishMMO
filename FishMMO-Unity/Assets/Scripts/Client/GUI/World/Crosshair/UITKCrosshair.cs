namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit crosshair control. Shows or hides the crosshair based on the current mouse mode.
	/// </summary>
	public class UITKCrosshair : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>
		/// Subscribes to the mouse mode toggle event.
		/// </summary>
		public override void OnStarting()
		{
			PlayerInputController.OnToggleMouseMode += OnToggleMouseMode;
		}

		/// <summary>
		/// Unsubscribes from the mouse mode toggle event.
		/// </summary>
		public override void OnDestroying()
		{
			PlayerInputController.OnToggleMouseMode -= OnToggleMouseMode;
		}

		/// <summary>
		/// Hides the crosshair when mouse mode is enabled, shows it otherwise.
		/// </summary>
		/// <param name="mouseMode">True if mouse mode is enabled.</param>
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
