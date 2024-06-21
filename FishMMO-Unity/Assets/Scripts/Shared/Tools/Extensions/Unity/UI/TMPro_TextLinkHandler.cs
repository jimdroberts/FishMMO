using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(TMP_Text))]
	public class TMPro_TextLinkHandler : MonoBehaviour, IPointerClickHandler
	{
		public Action<string> OnLinkClicked;

		private TMP_Text htmlText;

		void Awake()
		{
			htmlText = GetComponent<TMP_Text>();
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (htmlText == null)
			{
				return;
			}
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