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
			/* Static event, and OnStarting re-runs every time the visual tree is rebuilt
			 * (UITKControl.ReinitializeIfTreeReplaced). A bare += therefore added one more handler
			 * per rebuild, and each of them called Show/Hide on this panel. Removing first makes
			 * the pair idempotent. */
			PlayerInputController.OnToggleMouseMode -= OnToggleMouseMode;
			PlayerInputController.OnToggleMouseMode += OnToggleMouseMode;

			// The crosshair's whole state is "is mouse mode on", which is known right now — the
			// toggle event only fires on a CHANGE, so a panel that waits for one starts out wrong.
			OnToggleMouseMode(PlayerInputController.MouseMode);
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
