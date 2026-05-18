using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishMMO.Client
{
	/// <summary>
	/// UITooltip is a UIControl that displays a tooltip with text near the mouse cursor.
	/// Adjusts vertical position to stay within screen bounds.
	/// </summary>
	public class UITooltip : UIControl
	{
		/// <summary>
		/// The text component used to display the tooltip content.
		/// </summary>
		public TMP_Text Text;

		/// <summary>
		/// The background RectTransform for positioning and sizing the tooltip.
		/// </summary>
		public RectTransform Background;

		/// <summary>
		/// Updates the tooltip position each frame to follow the mouse, adjusting vertical offset if near the top of the screen.
		/// Only runs when the tooltip is visible.
		/// </summary>
		void Update()
		{
			if (!Visible || Text == null || Background == null) return;

			UpdatePosition();
		}

		/// <summary>
		/// Opens the tooltip with the specified text, positions it near the mouse, and shows it.
		/// </summary>
		/// <param name="text">Text to display in the tooltip.</param>
		public void Open(string text)
		{
			Hide();
			if (Text == null) return;

			Text.text = text;
			UpdatePosition();
			Show();
		}

		/// <summary>
		/// Calculates and applies the tooltip position relative to the mouse cursor.
		/// Offsets upward when the cursor is in the upper half of the screen to prevent clipping.
		/// </summary>
		private void UpdatePosition()
		{
			Mouse mouse = Mouse.current;
			if (mouse == null)
			{
				return;
			}
			Vector3 mousePosition = mouse.position.ReadValue();
			float yOffset = (mousePosition.y > Screen.height * 0.5f) ? -Background.rect.height : 0f;
			transform.position = mousePosition + new Vector3(0f, yOffset, 0f);
		}
	}
}