using UnityEngine;
using TMPro;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public sealed class Cached3DLabel : MonoBehaviour, IReference
	{
		/// <summary>
		/// If true, label will only be cached manually; otherwise, cached after time expires.
		/// </summary>
		private bool manualCache;
		/// <summary>
		/// Remaining time before label is automatically cached.
		/// </summary>
		private float remainingTime;

		/// <summary>
		/// The TextMeshPro component used to display the label.
		/// </summary>
		public TextMeshPro TMP;

		/// <summary>
		/// Unity Update method. Handles automatic caching of label when time expires.
		/// </summary>
		void Update()
		{
			if (!manualCache)
			{
				if (remainingTime < 0.0f)
				{
					LabelMaker.Cache(this);
					return;
				}
				remainingTime -= Time.deltaTime;
			}
		}

		/// <summary>
		/// Initializes the label with text, position, color, font size, persistence time, and caching mode.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="position">World position for the label.</param>
		/// <param name="color">Text color.</param>
		/// <param name="fontSize">Font size.</param>
		/// <param name="persistTime">Time to persist the label.</param>
		/// <param name="manualCache">Whether to manually cache the label after use.</param>
		public void Initialize(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache)
		{
			// Set text with color if available, otherwise plain text
			string hex = color.ToHex();
			if (!string.IsNullOrWhiteSpace(hex))
			{
				TMP.text = "<color=#" + hex + ">" + text + "</color>";
			}
			else
			{
				TMP.text = text;
			}
			TMP.fontSize = fontSize;

			// Adjust position offset based on the text mesh height
			TMP.ForceMeshUpdate();
			float textHeight = TMP.textBounds.size.y;
			position.y += textHeight;
			transform.position = position;

			remainingTime = persistTime;
			this.manualCache = manualCache;

			gameObject.SetActive(true);
		}

		/// <summary>
		/// Sets the world position of the label.
		/// </summary>
		/// <param name="position">The new position.</param>
		public void SetPosition(Vector3 position)
		{
			transform.position = position;
		}

		/// <summary>
		/// Sets the text of the label.
		/// </summary>
		/// <param name="text">The new text to display.</param>
		public void SetText(string text)
		{
			if (TMP != null)
			{
				TMP.text = text;
			}
		}
	}
}
