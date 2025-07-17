using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIAdvancedLabel : MonoBehaviour, IReference
	{
		public string Text = "";
		public Vector2 Size = Vector2.zero;
		public GUIStyle Style = new GUIStyle();
		public Vector2 Center = Vector2.zero;
		public Vector2 PixelOffset = Vector2.zero;

		//fade
		public bool FadeColor;
		public float RemainingLife = 0.0f;
		public float FadeTime = 4.0f;
		public Color TargetColor = Color.clear;

		//increase Y
		public bool IncreaseY;
		public float OldY = 0.0f;
		public float YIncreaseValue = 200.0f;

		//bounce
		public float Bounce = 0.0f;
		public float BounceDecay = 0.1f;

		void Update()
		{
			if (UIManager.TryGet("UILoadingScreen", out UILoadingScreen loadingScreen) &&
				loadingScreen.Visible)
			{
				this.gameObject.SetActive(false);
				return;
			}
			if (UIManager.TryGet("UIReconnectDisplay", out UIReconnectDisplay reconnectDisplay) &&
				reconnectDisplay.Visible)
			{
				this.gameObject.SetActive(false);
				return;
			}
			if (RemainingLife > 0.0f)
			{
				float t = Mathf.Clamp01(1.0f - (RemainingLife / FadeTime));

				if (FadeColor)
				{
					Color c = Style.normal.textColor;

					float alpha;
					if (t < 0.5f)
					{
						// Fade from Transparent to TargetColor
						alpha = Mathf.Lerp(Color.clear.a, TargetColor.a, t * 2);
					}
					else
					{
						// Fade from TargetColor back to Transparent
						alpha = Mathf.Lerp(TargetColor.a, Color.clear.a, (t - 0.5f) * 2);
					}

					c.a = alpha;
					Style.normal.textColor = c;
				}

				if (IncreaseY)
				{
					PixelOffset.y = Mathf.Lerp(OldY, YIncreaseValue, t);
				}

				RemainingLife -= Time.deltaTime;
			}
			else
			{
				this.gameObject.SetActive(false);
			}
		}

		void OnGUI()
		{
			if (Camera.main == null)
			{
				return;
			}
			GUI.Label(new Rect(Screen.width * 0.5f - PixelOffset.x - Center.x, Screen.height * 0.5f - PixelOffset.y + Center.y, Size.x, Size.y), Text, Style);
		}

		public static IReference Create(string text, FontStyle style, Font font, int fontSize, Color color, float lifeTime, bool fadeColor, bool increaseY, Vector2 pixelOffset)
		{
			GameObject newObject = new GameObject("UIAdvancedLabel: " + text);
			UIAdvancedLabel label = (UIAdvancedLabel)newObject.AddComponent<UIAdvancedLabel>();
			label.Initialize(text, style, font, fontSize, color, lifeTime, fadeColor, increaseY, pixelOffset);
			return label;
		}

		public void Initialize(string text, FontStyle fontStyle, Font font, int fontSize, Color color, float lifeTime, bool fadeColor, bool increaseY, Vector2 pixelOffset)
		{
			Text = text;
			Style.wordWrap = false;
			Style.normal.textColor = color;
			Style.fontStyle = fontStyle;
			Style.fontSize = fontSize;
			Style.alignment = TextAnchor.MiddleCenter;
			if (font != null)
			{
				Style.font = font;
			}
			Size = Style.CalcSize(new GUIContent(Text));
			Center = new Vector2(Size.x * 0.5f, Size.y * 0.5f);
			PixelOffset = pixelOffset;
			if (lifeTime > 0.0f)
			{
				RemainingLife = lifeTime;

				if (fadeColor)
				{
					FadeColor = fadeColor;
					FadeTime = lifeTime;
					TargetColor = Style.normal.textColor;
				}
				if (increaseY)
				{
					IncreaseY = increaseY;
					OldY = pixelOffset.y;
					YIncreaseValue = OldY + YIncreaseValue;
				}
			}
			this.gameObject.SetActive(true);
		}

		public void SetPosition(Vector3 position)
		{
			this.transform.position = position;
		}

		public void ChangeFont(int fontSize)
		{
			Style.fontSize = fontSize;
			Size = Style.CalcSize(new GUIContent(Text));
			Center = new Vector2(Size.x * 0.5f, Size.y * 0.5f);
		}

		public void SetText(string text)
		{
			Text = text;
			Size = Style.CalcSize(new GUIContent(Text));
			Center = new Vector2(Size.x * 0.5f, Size.y * 0.5f);
		}

		public void SetColor(Color color)
		{
			Style.normal.textColor = color;
		}
	}
}