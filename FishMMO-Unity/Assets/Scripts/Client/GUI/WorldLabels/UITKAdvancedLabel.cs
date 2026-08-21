using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// A transient caption drawn at a screen position, with optional fade and upward drift.
	/// Used for region names and similar one-off announcements.
	/// </summary>
	/// <remarks>
	/// Replaces the IMGUI-based <c>UIAdvancedLabel</c>, which drew through <c>GUI.Label</c> in an
	/// <c>OnGUI</c> pass — a separate immediate-mode render path that measured its own text with
	/// <c>GUIStyle.CalcSize</c> and positioned everything by hand against <c>Screen.width</c>.
	///
	/// The public surface is deliberately identical: <see cref="Create"/>, <see cref="Initialize"/>,
	/// <see cref="SetText"/>, <see cref="SetColor"/>, <see cref="ChangeFont"/> and
	/// <see cref="SetPosition"/> keep their old signatures, including the legacy <c>FontStyle</c>
	/// and <c>Font</c> parameters. <c>DisplayRegionNameAction</c> in FishMMO-Shared raises its
	/// event with those types and the server compiles that file too, so leaving the signature
	/// alone keeps a rendering change out of the shared assembly.
	///
	/// The element lives in <see cref="UITKWorldLabelLayer.ScreenContainer"/> rather than in a
	/// document of its own, because these are created at runtime with a bare
	/// <c>new GameObject</c> and have nowhere to hang a UIDocument.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class UITKAdvancedLabel : MonoBehaviour, IReference
	{
		/// <summary>USS class applied to the caption element.</summary>
		private const string LABEL_CLASS = "screen-label";

		/// <summary>
		/// The text displayed by the label.
		/// </summary>
		public string Text = "";

		/// <summary>
		/// Pixel offset from the centre of the screen.
		/// </summary>
		public Vector2 PixelOffset = Vector2.zero;

		// fade

		/// <summary>If true, the label colour fades in then out over its lifetime.</summary>
		public bool FadeColor;

		/// <summary>Remaining lifetime of the label in seconds.</summary>
		public float RemainingLife = 0.0f;

		/// <summary>Total fade time for the label in seconds.</summary>
		public float FadeTime = 4.0f;

		/// <summary>Colour the label fades towards.</summary>
		public Color TargetColor = Color.clear;

		// increase Y

		/// <summary>If true, the label drifts upward over its lifetime.</summary>
		public bool IncreaseY;

		/// <summary>The starting Y offset for upward movement.</summary>
		public float OldY = 0.0f;

		/// <summary>The target Y offset for upward movement.</summary>
		public float YIncreaseValue = 200.0f;

		/// <summary>The element this label draws through.</summary>
		private Label element;

		/// <summary>Colour the label is currently drawn in, before fade is applied.</summary>
		private Color currentColor = Color.white;

		/// <summary>Font size in panel points.</summary>
		private int fontSize = 16;

		/// <summary>Font style requested by the caller.</summary>
		private FontStyle fontStyle = FontStyle.Normal;

		/// <summary>
		/// Creates the backing element, or returns the existing one.
		/// </summary>
		/// <returns>The element, or null when no label layer is loaded.</returns>
		private Label ResolveElement()
		{
			if (element != null)
			{
				return element;
			}

			UITKWorldLabelLayer layer = UITKWorldLabelLayer.Instance;
			VisualElement parent = layer != null ? layer.ScreenContainer : null;
			if (parent == null)
			{
				return null;
			}

			element = new Label { name = "screen-label", pickingMode = PickingMode.Ignore };
			element.AddToClassList(LABEL_CLASS);
			element.style.position = Position.Absolute;
			parent.Add(element);
			return element;
		}

		/// <summary>
		/// Pushes the current text, colour, font and position onto the element.
		/// </summary>
		private void Apply()
		{
			Label target = ResolveElement();
			if (target == null)
			{
				return;
			}

			target.text = UITKRichText.ToUITK(Text, fontSize);
			target.style.color = currentColor;
			target.style.fontSize = fontSize;
			// UI Toolkit's unityFontStyleAndWeight is the same UnityEngine.FontStyle enum the
			// caller already passes, so the legacy value carries straight over.
			target.style.unityFontStyleAndWeight = fontStyle;

			/* Positioned from the centre of the panel rather than from Screen.width: the panel is
			 * scaled by PanelSettings, so screen pixels and panel points differ, and using screen
			 * pixels here put the caption progressively further off-centre as resolution rose.
			 * Percentages resolve against the panel, which is the space the offsets belong to. */
			target.style.left = Length.Percent(50.0f);
			target.style.top = Length.Percent(50.0f);
			target.style.translate = new Translate(
				new Length(-PixelOffset.x, LengthUnit.Pixel),
				new Length(-PixelOffset.y, LengthUnit.Pixel));
		}

		/// <summary>
		/// Per-frame update for label visibility, fading, and movement effects.
		/// </summary>
		private void Update()
		{
			if (UIManager.TryGetTK("UILoadingScreen", out UITKLoadingScreen loadingScreen) &&
				loadingScreen.Visible)
			{
				this.gameObject.SetActive(false);
				return;
			}
			if (UIManager.TryGetTK("UIReconnectDisplay", out UITKReconnectDisplay reconnectDisplay) &&
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
					float alpha;
					if (t < 0.5f)
					{
						// Fade from transparent to TargetColor.
						alpha = Mathf.Lerp(Color.clear.a, TargetColor.a, t * 2.0f);
					}
					else
					{
						// Fade from TargetColor back to transparent.
						alpha = Mathf.Lerp(TargetColor.a, Color.clear.a, (t - 0.5f) * 2.0f);
					}

					currentColor.a = alpha;
				}

				if (IncreaseY)
				{
					PixelOffset.y = Mathf.Lerp(OldY, YIncreaseValue, t);
				}

				RemainingLife -= Time.deltaTime;
				Apply();
			}
			else
			{
				this.gameObject.SetActive(false);
			}
		}

		private void OnEnable()
		{
			if (element != null)
			{
				element.style.display = DisplayStyle.Flex;
			}
		}

		private void OnDisable()
		{
			if (element != null)
			{
				element.style.display = DisplayStyle.None;
			}
		}

		private void OnDestroy()
		{
			if (element != null)
			{
				element.RemoveFromHierarchy();
				element = null;
			}
		}

		/// <summary>
		/// Creates a new label instance and initializes it with the provided parameters.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="style">Font style.</param>
		/// <param name="font">Font to use. Ignored — the panel's theme supplies the face.</param>
		/// <param name="fontSize">Font size.</param>
		/// <param name="color">Text color.</param>
		/// <param name="lifeTime">Lifetime of the label in seconds.</param>
		/// <param name="fadeColor">If true, label fades color over time.</param>
		/// <param name="increaseY">If true, label moves upward over time.</param>
		/// <param name="pixelOffset">Pixel offset for label placement.</param>
		/// <returns>Reference to the created label.</returns>
		public static IReference Create(string text, FontStyle style, Font font, int fontSize, Color color, float lifeTime, bool fadeColor, bool increaseY, Vector2 pixelOffset)
		{
			GameObject newObject = new GameObject("UITKAdvancedLabel: " + text);
			UITKAdvancedLabel label = newObject.AddComponent<UITKAdvancedLabel>();
			label.Initialize(text, style, font, fontSize, color, lifeTime, fadeColor, increaseY, pixelOffset);
			return label;
		}

		/// <summary>
		/// Initializes the label with the provided parameters.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="fontStyle">Font style.</param>
		/// <param name="font">Font to use. Ignored — the panel's theme supplies the face.</param>
		/// <param name="fontSize">Font size.</param>
		/// <param name="color">Text color.</param>
		/// <param name="lifeTime">Lifetime of the label in seconds.</param>
		/// <param name="fadeColor">If true, label fades color over time.</param>
		/// <param name="increaseY">If true, label moves upward over time.</param>
		/// <param name="pixelOffset">Pixel offset for label placement.</param>
		public void Initialize(string text, FontStyle fontStyle, Font font, int fontSize, Color color, float lifeTime, bool fadeColor, bool increaseY, Vector2 pixelOffset)
		{
			Text = text;
			this.fontStyle = fontStyle;

			/* A zero font size is how the old caller created its idle placeholder; CalcSize
			 * tolerated it, but a zero-point element in UI Toolkit is simply invisible and would
			 * stay that way after a later Initialize that also passed zero. Falling back keeps a
			 * caption that forgets to specify a size legible instead of absent. */
			this.fontSize = fontSize > 0 ? fontSize : 16;

			currentColor = color;
			PixelOffset = pixelOffset;

			FadeColor = false;
			IncreaseY = false;

			if (lifeTime > 0.0f)
			{
				RemainingLife = lifeTime;

				if (fadeColor)
				{
					FadeColor = true;
					FadeTime = lifeTime;
					TargetColor = color;
				}
				if (increaseY)
				{
					IncreaseY = true;
					OldY = pixelOffset.y;
					YIncreaseValue = OldY + YIncreaseValue;
				}
			}
			else
			{
				RemainingLife = 0.0f;
			}

			this.gameObject.SetActive(true);
			Apply();
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
		/// Changes the font size of the label.
		/// </summary>
		/// <param name="fontSize">New font size.</param>
		public void ChangeFont(int fontSize)
		{
			this.fontSize = fontSize > 0 ? fontSize : this.fontSize;
			Apply();
		}

		/// <summary>
		/// Sets the label's text.
		/// </summary>
		/// <param name="text">New text to display.</param>
		public void SetText(string text)
		{
			Text = text;
			Apply();
		}

		/// <summary>
		/// Sets the label's color.
		/// </summary>
		/// <param name="color">New color to set.</param>
		public void SetColor(Color color)
		{
			currentColor = color;
			Apply();
		}
	}
}
