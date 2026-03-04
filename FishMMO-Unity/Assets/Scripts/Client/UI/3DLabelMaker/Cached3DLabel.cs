using UnityEngine;
using TMPro;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// A poolable 3D text label rendered via TextMeshPro.
	/// Supports automatic caching after a timed duration or manual caching via <see cref="LabelMaker.Cache"/>.
	/// </summary>
	public sealed class Cached3DLabel : MonoBehaviour, IReference
	{
		/// <summary>
		/// If true, the label must be returned to the pool manually via <see cref="LabelMaker.Cache"/>.
		/// </summary>
		private bool manualCache;

		/// <summary>
		/// Remaining time in seconds before the label is automatically cached.
		/// </summary>
		private float remainingTime;

		/// <summary>
		/// The TextMeshPro component used to render the label text.
		/// </summary>
		[SerializeField]
		private TextMeshPro textMesh;

		/// <summary>
		/// Gets the TextMeshPro component for this label.
		/// </summary>
		public TextMeshPro TMP => textMesh;

		/// <summary>
		/// Handles automatic caching when the persist timer expires.
		/// </summary>
		void Update()
		{
			if (manualCache) return;

			remainingTime -= Time.deltaTime;
			if (remainingTime <= 0.0f)
			{
				LabelMaker.Cache(this);
			}
		}

		/// <summary>
		/// Initializes the label with the specified display properties and activates it.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="position">World position for the label.</param>
		/// <param name="color">Text color.</param>
		/// <param name="fontSize">Font size in Unity units.</param>
		/// <param name="persistTime">Duration in seconds before automatic caching. Ignored if manualCache is true.</param>
		/// <param name="manualCache">If true, the label must be cached manually via <see cref="LabelMaker.Cache"/>.</param>
		public void Initialize(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache)
		{
			textMesh.text = text;
			textMesh.fontSize = fontSize;
			textMesh.color = color;

			textMesh.ForceMeshUpdate();
			position.y += textMesh.textBounds.size.y;
			transform.position = position;

			remainingTime = persistTime;
			this.manualCache = manualCache;

			gameObject.SetActive(true);
		}

		/// <summary>
		/// Sets the world position of the label.
		/// </summary>
		/// <param name="position">The new world position.</param>
		public void SetPosition(Vector3 position)
		{
			transform.position = position;
		}

		/// <summary>
		/// Sets the displayed text content.
		/// </summary>
		/// <param name="text">The new text to display.</param>
		public void SetText(string text)
		{
			if (textMesh == null) return;

			textMesh.text = text;
		}

		/// <summary>
		/// Sets the text color.
		/// </summary>
		/// <param name="color">The new color.</param>
		public void SetColor(Color color)
		{
			if (textMesh == null) return;

			textMesh.color = color;
		}

		/// <summary>
		/// Sets the font size.
		/// </summary>
		/// <param name="fontSize">The new font size in Unity units.</param>
		public void SetFontSize(float fontSize)
		{
			if (textMesh == null) return;

			textMesh.fontSize = fontSize;
		}
	}
}