using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// UITooltip is a UIControl that displays a tooltip with text near the mouse cursor.
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
		/// Updates the tooltip position each frame to follow the mouse, adjusting vertical offset if near the top or bottom of the screen.
		/// </summary>
		void Update()
		{
			if (Text != null && Background != null)
			{
				// Get the current mouse position
				Vector3 mousePosition = Input.mousePosition;

				// Get the screen height
				float screenHeight = Screen.height;

				// If mouse is in the upper half of the screen, offset the tooltip upward so it doesn't go off-screen
				float yOffset = (mousePosition.y > screenHeight / 2) ? -Background.rect.height : 0.0f;

				Vector3 offset = new Vector3(0.0f, yOffset, 0.0f);
				transform.position = Input.mousePosition + offset;
			}
		}

		/// <summary>
		/// Opens the tooltip with the specified text, positions it near the mouse, and shows it.
		/// </summary>
		/// <param name="text">Text to display in the tooltip.</param>
		public void Open(string text)
		{
			Hide();
			if (this.Text != null)
			{
				this.Text.text = text;
				// Get the current mouse position
				Vector3 mousePosition = Input.mousePosition;

				// Get the screen height
				float screenHeight = Screen.height;

				// If mouse is in the upper half of the screen, offset the tooltip upward so it doesn't go off-screen
				float yOffset = (mousePosition.y > screenHeight / 2) ? -Background.rect.height : 0.0f;

				Vector3 offset = new Vector3(0.0f, yOffset, 0.0f);
				transform.position = Input.mousePosition + offset;
				Show();
			}
		}
	}
}