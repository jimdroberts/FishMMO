using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Handles clickable text links in a TMP_Text component and invokes a callback when a link is clicked.
	/// </summary>
	[RequireComponent(typeof(TMP_Text))]
	public class TMPro_TextLinkHandler : MonoBehaviour, IPointerClickHandler
	{
		/// <summary>
		/// Callback invoked when a text link is clicked. The link ID is passed as a string.
		/// </summary>
		public Action<string> OnLinkClicked;

		/// <summary>
		/// Reference to the TMP_Text component containing the clickable links.
		/// </summary>
		private TMP_Text htmlText;

		/// <summary>
		/// Initializes the TMP_Text reference on Awake.
		/// </summary>
		void Awake()
		{
			htmlText = GetComponent<TMP_Text>();
		}

		/// <summary>
		/// Handles pointer click events, detects if a TMP link was clicked, and invokes the callback.
		/// </summary>
		/// <param name="eventData">Pointer event data from the click.</param>
		public void OnPointerClick(PointerEventData eventData)
		{
			if (htmlText == null)
			{
				return;
			}
			// Get mouse position and check for intersecting TMP link
			Vector3 mousePos = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0.0f);
			var linkTagText = TMP_TextUtilities.FindIntersectingLink(htmlText, mousePos, null);
			if (linkTagText < 0)
			{
				return;
			}
			TMP_LinkInfo linkInfo = htmlText.textInfo.linkInfo[linkTagText];
			string linkId = linkInfo.GetLinkID();
			if (!string.IsNullOrWhiteSpace(linkId))
			{
				OnLinkClicked?.Invoke(linkId);
			}
		}
	}
}