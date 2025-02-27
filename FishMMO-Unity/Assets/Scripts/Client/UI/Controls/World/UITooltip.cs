using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	public class UITooltip : UIControl
	{
		public TMP_Text text;
		public RectTransform background;

		void Update()
		{
			if (text != null && background != null)
			{
				// Get the current mouse position
				Vector3 mousePosition = Input.mousePosition;

				// Get the screen height
				float screenHeight = Screen.height;

				float yOffset = (mousePosition.y > screenHeight / 2) ? -background.rect.height : 0.0f;

				Vector3 offset = new Vector3(0.0f, yOffset, 0.0f);
				transform.position = Input.mousePosition + offset;
			}
		}

		public void Open(string text)
		{
			Hide();
			if (this.text != null)
			{
				this.text.text = text;
				
				// Get the current mouse position
				Vector3 mousePosition = Input.mousePosition;

				// Get the screen height
				float screenHeight = Screen.height;

				float yOffset = (mousePosition.y > screenHeight / 2) ? -background.rect.height : 0.0f;

				Vector3 offset = new Vector3(0.0f, yOffset, 0.0f);
				transform.position = Input.mousePosition + offset;
				Show();
			}
		}
	}
}