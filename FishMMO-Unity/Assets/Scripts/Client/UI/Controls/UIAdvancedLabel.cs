using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UIAdvancedLabel is a class that displays a label on the screen with advanced features like fading, movement, and bouncing effects.
	/// </summary>
	public class UIAdvancedLabel : MonoBehaviour, IReference
	{
		/// <summary>
		/// The text displayed by the label.
		/// </summary>
		public string Text = "";
		/// <summary>
		/// The size of the label in pixels.
		/// </summary>
		public Vector2 Size = Vector2.zero;
		/// <summary>
		/// The GUIStyle used to render the label.
		/// </summary>
		public GUIStyle Style = new GUIStyle();
		/// <summary>
		/// The center offset for the label.
		/// </summary>
		public Vector2 Center = Vector2.zero;
		/// <summary>
		/// Pixel offset for label placement on screen.
		/// </summary>
		public Vector2 PixelOffset = Vector2.zero;

		// fade
		/// <summary>
		/// If true, the label color will fade over its lifetime.
		/// </summary>
		public bool FadeColor;
		/// <summary>
		/// Remaining lifetime of the label in seconds.
		/// </summary>
		public float RemainingLife = 0.0f;
		/// <summary>
		/// Total fade time for the label in seconds.
		/// </summary>
		public float FadeTime = 4.0f;
		/// <summary>
		/// Target color to fade to.
		/// </summary>
		public Color TargetColor = Color.clear;

		// increase Y
		/// <summary>
		/// If true, the label will move upward over its lifetime.
		/// </summary>
		public bool IncreaseY;
		/// <summary>
		/// The starting Y position for upward movement.
		/// </summary>
		public float OldY = 0.0f;
		/// <summary>
		/// The target Y position for upward movement.
		/// </summary>
		public float YIncreaseValue = 200.0f;

		// bounce
		/// <summary>
		/// Bounce effect value for the label.
		/// </summary>
		public float Bounce = 0.0f;
		/// <summary>
		/// Decay rate for the bounce effect.
		/// </summary>
		public float BounceDecay = 0.1f;

		/// <summary>
		/// Per-frame update for label visibility, fading, and movement effects.
		/// </summary>
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

		/// <summary>
		/// Renders the label on the screen using GUI.Label.
		/// </summary>
		void OnGUI()
		{
			if (Camera.main == null)
			{
				return;
			}
			GUI.Label(new Rect(Screen.width * 0.5f - PixelOffset.x - Center.x, Screen.height * 0.5f - PixelOffset.y + Center.y, Size.x, Size.y), Text, Style);
		}

		/// <summary>
		/// Creates a new UIAdvancedLabel instance and initializes it with the provided parameters.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="style">Font style.</param>
		/// <param name="font">Font to use.</param>
		/// <param name="fontSize">Font size.</param>
		/// <param name="color">Text color.</param>
		/// <param name="lifeTime">Lifetime of the label in seconds.</param>
		/// <param name="fadeColor">If true, label fades color over time.</param>
		/// <param name="increaseY">If true, label moves upward over time.</param>
		/// <param name="pixelOffset">Pixel offset for label placement.</param>
		/// <returns>Reference to the created label.</returns>
		public static IReference Create(string text, FontStyle style, Font font, int fontSize, Color color, float lifeTime, bool fadeColor, bool increaseY, Vector2 pixelOffset)
		{
			GameObject newObject = new GameObject("UIAdvancedLabel: " + text);
			UIAdvancedLabel label = (UIAdvancedLabel)newObject.AddComponent<UIAdvancedLabel>();
			label.Initialize(text, style, font, fontSize, color, lifeTime, fadeColor, increaseY, pixelOffset);
			return label;
		}

		/// <summary>
		/// Initializes the label with the provided parameters.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="fontStyle">Font style.</param>
		/// <param name="font">Font to use.</param>
		/// <param name="fontSize">Font size.</param>
		/// <param name="color">Text color.</param>
		/// <param name="lifeTime">Lifetime of the label in seconds.</param>
		/// <param name="fadeColor">If true, label fades color over time.</param>
		/// <param name="increaseY">If true, label moves upward over time.</param>
		/// <param name="pixelOffset">Pixel offset for label placement.</param>
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

		/// <summary>
		/// Sets the world position of the label's transform.
		/// </summary>
		/// <param name="position">World position to set.</param>
		public void SetPosition(Vector3 position)
		{
			this.transform.position = position;
		}

		/// <summary>
		/// Changes the font size of the label and recalculates its size and center.
		/// </summary>
		/// <param name="fontSize">New font size.</param>
		public void ChangeFont(int fontSize)
		{
			Style.fontSize = fontSize;
			Size = Style.CalcSize(new GUIContent(Text));
			Center = new Vector2(Size.x * 0.5f, Size.y * 0.5f);
		}

		/// <summary>
		/// Sets the label's text and recalculates its size and center.
		/// </summary>
		/// <param name="text">New text to display.</param>
		public void SetText(string text)
		{
			Text = text;
			Size = Style.CalcSize(new GUIContent(Text));
			Center = new Vector2(Size.x * 0.5f, Size.y * 0.5f);
		}

		/// <summary>
		/// Sets the label's color.
		/// </summary>
		/// <param name="color">New color to set.</param>
		public void SetColor(Color color)
		{
			Style.normal.textColor = color;
		}
	}
}