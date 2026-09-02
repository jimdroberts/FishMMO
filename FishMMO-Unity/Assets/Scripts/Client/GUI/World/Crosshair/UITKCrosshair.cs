using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit crosshair control. Shows or hides the crosshair based on the current mouse mode,
	/// and paints it in the shape, size and strength the player chose.
	/// </summary>
	/// <remarks>
	/// Visibility has two independent owners and both must agree for the crosshair to be drawn:
	/// mouse mode hides it while the cursor is free, and the player's own
	/// <see cref="ClientCrosshairSettings.Enabled"/> hides it always. They are kept as one
	/// decision in <see cref="ApplyVisibility"/> rather than as two callers racing to call
	/// <c>Show</c> and <c>Hide</c> — with two, whichever fired last won, so turning the crosshair
	/// off and then opening and closing the map brought it back.
	/// </remarks>
	public class UITKCrosshair : UITKControl
	{
		/// <summary>Name of the element that draws the crosshair itself.</summary>
		private const string ICON_NAME = "crosshair-icon";

		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>The element the shape, size and opacity are written onto.</summary>
		private VisualElement icon;

		/// <summary>Last mouse mode reported, so a settings change can re-decide visibility.</summary>
		private bool mouseMode;

		/// <summary>
		/// Resolves the icon, subscribes to the mouse mode toggle and to the player's own
		/// crosshair settings, and paints the current state.
		/// </summary>
		public override void OnStarting()
		{
			icon = Root?.Q<VisualElement>(ICON_NAME);

			/* Static events, and OnStarting re-runs every time the visual tree is rebuilt
			 * (UITKControl.ReinitializeIfTreeReplaced). A bare += therefore added one more handler
			 * per rebuild, and each of them called Show/Hide on this panel. Removing first makes
			 * the pair idempotent. */
			PlayerInputController.OnToggleMouseMode -= OnToggleMouseMode;
			PlayerInputController.OnToggleMouseMode += OnToggleMouseMode;

			ClientCrosshairSettings.OnChanged -= ApplyAppearance;
			ClientCrosshairSettings.OnChanged += ApplyAppearance;

			/* The theme owns the crosshair's COLOUR, and the two box-drawn shapes take it as an
			 * inline background or border rather than as the text colour the theme writes — so a
			 * palette change has to come back through here to be picked up. */
			UITKThemeManager.OnThemeChanged -= ApplyAppearance;
			UITKThemeManager.OnThemeChanged += ApplyAppearance;

			// The crosshair's whole state is "is mouse mode on", which is known right now — the
			// toggle event only fires on a CHANGE, so a panel that waits for one starts out wrong.
			mouseMode = PlayerInputController.MouseMode;

			ApplyAppearance();
		}

		/// <summary>
		/// Unsubscribes from the mouse mode toggle and the settings events.
		/// </summary>
		public override void OnDestroying()
		{
			PlayerInputController.OnToggleMouseMode -= OnToggleMouseMode;
			ClientCrosshairSettings.OnChanged -= ApplyAppearance;
			UITKThemeManager.OnThemeChanged -= ApplyAppearance;
		}

		/// <summary>
		/// Hides the crosshair when mouse mode is enabled, shows it otherwise.
		/// </summary>
		/// <param name="mouseModeEnabled">True if mouse mode is enabled.</param>
		public void OnToggleMouseMode(bool mouseModeEnabled)
		{
			mouseMode = mouseModeEnabled;
			ApplyVisibility();
		}

		/// <summary>
		/// Writes the chosen shape, size and opacity onto the icon, then re-decides visibility.
		/// </summary>
		private void ApplyAppearance()
		{
			if (icon != null)
			{
				float size = ClientCrosshairSettings.Size;

				icon.style.width = size;
				icon.style.height = size;
				icon.style.opacity = ClientCrosshairSettings.Opacity;

				/* Every style class is removed before the chosen one is added. The classes are
				 * mutually exclusive shapes and RemoveFromClassList on an absent class is a
				 * no-op, so clearing the set is cheaper than tracking which one was last on —
				 * and it cannot leave two background rules fighting after a change. */
				for (int i = 0; i < ClientCrosshairSettings.StyleClasses.Length; ++i)
				{
					icon.RemoveFromClassList(ClientCrosshairSettings.StyleClasses[i]);
				}

				ClientCrosshairSettings.CrosshairStyle style = ClientCrosshairSettings.Style;
				icon.AddToClassList(ClientCrosshairSettings.StyleClasses[(int)style]);

				ApplyShapeColor(style, size);
			}

			ApplyVisibility();
		}

		/// <summary>
		/// Writes the themed crosshair colour onto whichever box property the chosen shape draws
		/// with, and clears the properties the other shapes use.
		/// </summary>
		/// <param name="style">The shape in force.</param>
		/// <param name="size">The chosen edge length, which sets the ring's thickness.</param>
		/// <remarks>
		/// <see cref="UITKThemeManager"/> paints <c>color</c> and
		/// <c>unityBackgroundImageTintColor</c> on this element, which is everything the sprite
		/// shape needs and nothing the other two do: a filled dot is a background colour and a
		/// ring is a border colour, and the theme has no reason to know that. Reading the palette
		/// here keeps all three shapes on the one colour the player set.
		/// <para>
		/// Cleared to <see cref="StyleKeyword.Null"/> rather than to transparent when a shape does
		/// not use a property. Null hands the property back to the stylesheet; transparent would
		/// be an inline override that outranks it, so switching from Circle to Cross would leave
		/// a permanently invisible border occupying layout.
		/// </para>
		/// </remarks>
		private void ApplyShapeColor(ClientCrosshairSettings.CrosshairStyle style, float size)
		{
			UITKTheme theme = UITKThemeManager.Current;
			Color color = theme != null && theme.HasOverride("Crosshair") ? theme.Crosshair : Color.white;

			switch (style)
			{
				case ClientCrosshairSettings.CrosshairStyle.Dot:
					icon.style.backgroundColor = color;
					ClearBorder();
					break;

				case ClientCrosshairSettings.CrosshairStyle.Circle:
					icon.style.backgroundColor = StyleKeyword.Null;

					/* Proportional, floored at one point. A ring drawn at a fixed hairline
					 * disappears at 32px and swallows its own hole at 4px. */
					float thickness = Mathf.Max(1.0f, Mathf.Round(size * 0.15f));
					icon.style.borderTopWidth = thickness;
					icon.style.borderBottomWidth = thickness;
					icon.style.borderLeftWidth = thickness;
					icon.style.borderRightWidth = thickness;
					icon.style.borderTopColor = color;
					icon.style.borderBottomColor = color;
					icon.style.borderLeftColor = color;
					icon.style.borderRightColor = color;
					break;

				default:
					icon.style.backgroundColor = StyleKeyword.Null;
					ClearBorder();
					break;
			}
		}

		/// <summary>Hands every border property back to the stylesheet.</summary>
		private void ClearBorder()
		{
			icon.style.borderTopWidth = StyleKeyword.Null;
			icon.style.borderBottomWidth = StyleKeyword.Null;
			icon.style.borderLeftWidth = StyleKeyword.Null;
			icon.style.borderRightWidth = StyleKeyword.Null;
			icon.style.borderTopColor = StyleKeyword.Null;
			icon.style.borderBottomColor = StyleKeyword.Null;
			icon.style.borderLeftColor = StyleKeyword.Null;
			icon.style.borderRightColor = StyleKeyword.Null;
		}

		/// <summary>
		/// Shows the panel only when mouse mode is off and the player has the crosshair enabled.
		/// </summary>
		private void ApplyVisibility()
		{
			if (!mouseMode && ClientCrosshairSettings.Enabled)
			{
				Show();
			}
			else
			{
				Hide();
			}
		}
	}
}
