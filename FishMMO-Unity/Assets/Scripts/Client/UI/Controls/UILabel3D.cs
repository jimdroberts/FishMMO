using UnityEngine;

namespace FishMMO.Client
{
	public class UILabel3D : UIObject3D
	{
		public string Text = "";

		void OnGUI()
		{
			if (!isVisible || Camera.main == null)
			{
				return;
			}
			Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position);
			GUI.Label(new Rect(screenPos.x - (pixelOffset.x + center.x), Screen.height - screenPos.y - (pixelOffset.y + center.y), size.x, size.y), Text, style);
		}

		public static UILabel3D Create(string text, int fontSize, Transform transform)
		{
			return Create(text, "", fontSize, Color.white, false, transform, Vector2.zero);
		}
		public static UILabel3D Create(string text, int fontSize, Transform transform, Vector2 pixelOffset)
		{
			return Create(text, "", fontSize, Color.white, false, transform, pixelOffset);
		}
		public static UILabel3D Create(string text, int fontSize, Color color, Transform transform)
		{
			return Create(text, "", fontSize, color, false, transform, Vector2.zero);
		}
		public static UILabel3D Create(string text, int fontSize, Color color, Transform transform, Vector2 pixelOffset)
		{
			return Create(text, "", fontSize, color, false, transform, pixelOffset);
		}
		public static UILabel3D Create(string text, int fontSize, Color color, bool isFade, Transform transform)
		{
			return Create(text, "", fontSize, color, isFade, transform, Vector2.zero);
		}
		public static UILabel3D Create(string text, int fontSize, Color color, bool isFade, Transform transform, Vector2 pixelOffset)
		{
			return Create(text, "", fontSize, color, isFade, transform, pixelOffset);
		}
		public static UILabel3D Create(string text, string extraText, int fontSize, Color color, bool isFade, Transform transform, Vector2 pixelOffset)
		{
			GameObject newObject = new GameObject("UILabel3D: " + text + extraText);
			newObject.transform.position = transform.position;

			Transform tryChild = transform.GetChild(0);
			newObject.transform.SetParent(tryChild != null ? tryChild : transform);

			UILabel3D label = (UILabel3D)newObject.AddComponent<UILabel3D>();
			label.Setup(text, extraText, FontStyle.Bold, null, fontSize, pixelOffset, color, isFade);
			return label;
		}

		public void Setup(string text, string extraText, FontStyle fontStyle, Font font, int fontSize, Vector2 pixelOffset, Color color, bool isFade)
		{
			Text = text;
			style.wordWrap = false;
			style.normal.textColor = color;
			style.fontStyle = fontStyle;
			style.fontSize = fontSize;
			style.alignment = TextAnchor.MiddleCenter;
			if (font != null)
			{
				style.font = font;
			}
			size = style.CalcSize(new GUIContent(Text));
			base.Setup(pixelOffset, isFade);
			text += extraText;
		}

		public void SetPosition(Vector3 position)
		{
			this.transform.position = position;
		}

		public void ChangeFont(int fontSize)
		{
			style.fontSize = fontSize;
			size = style.CalcSize(new GUIContent(Text));
			center = new Vector2(size.x * 0.5f, size.y * 0.5f);
		}

		public void SetText(string text)
		{
			SetText(text, "");
		}
		public void SetText(string text, string extraText)
		{
			Text = text;
			size = style.CalcSize(new GUIContent(Text));
			center = new Vector2(size.x * 0.5f, size.y * 0.5f);
			Text += extraText;
		}

		public void SetColor(Color color)
		{
			style.normal.textColor = color;
		}
	}
}