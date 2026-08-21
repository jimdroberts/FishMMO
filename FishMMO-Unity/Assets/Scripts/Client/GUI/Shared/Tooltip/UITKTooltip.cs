using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit tooltip control. Displays a text box near the mouse cursor and adjusts its
	/// vertical position to remain on-screen.
	/// </summary>
	public class UITKTooltip : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Tooltip;

		/// <summary>
		/// Name of the tooltip box container element.
		/// </summary>
		private const string TOOLTIP_BOX_NAME = "tooltip-box";
		/// <summary>
		/// Name of the tooltip text label element.
		/// </summary>
		private const string TOOLTIP_TEXT_NAME = "tooltip-text";

		/// <summary>
		/// The tooltip box container element.
		/// </summary>
		private VisualElement tooltipBox;
		/// <summary>
		/// The tooltip text label.
		/// </summary>
		private Label tooltipText;

		/// <summary>
		/// Resolves cached elements and prepares the tooltip for absolute positioning.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			tooltipBox = Root.Q<VisualElement>(TOOLTIP_BOX_NAME);
			tooltipText = Root.Q<Label>(TOOLTIP_TEXT_NAME);

			// The root should never intercept pointer events.
			Root.pickingMode = PickingMode.Ignore;
			if (tooltipBox != null)
			{
				tooltipBox.pickingMode = PickingMode.Ignore;
				tooltipBox.style.position = Position.Absolute;
			}
		}

		/// <summary>
		/// Repositions the tooltip to follow the mouse while visible.
		/// </summary>
		private void Update()
		{
			if (!Visible || tooltipBox == null)
			{
				return;
			}

			UpdatePosition();
		}

		/// <summary>
		/// Opens the tooltip with the specified text and shows it near the cursor.
		/// </summary>
		/// <param name="text">Text to display in the tooltip.</param>
		public void Open(string text)
		{
			Hide();
			if (tooltipText == null)
			{
				return;
			}

			tooltipText.text = text;
			Show();
			UpdatePosition();
		}

		/// <summary>
		/// Calculates and applies the tooltip box position relative to the mouse cursor.
		/// Offsets upward when the cursor is in the upper half of the screen.
		/// </summary>
		private void UpdatePosition()
		{
			if (tooltipBox == null || Root == null || Root.panel == null)
			{
				return;
			}

			Mouse mouse = Mouse.current;
			if (mouse == null)
			{
				return;
			}

			Vector2 screenPosition = mouse.position.ReadValue();
			Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(Root.panel, screenPosition);

			float boxHeight = tooltipBox.resolvedStyle.height;
			float yOffset = (screenPosition.y > Screen.height * 0.5f) ? -boxHeight : 0.0f;

			tooltipBox.style.left = panelPosition.x;
			tooltipBox.style.top = panelPosition.y + yOffset;
		}
	}
}
