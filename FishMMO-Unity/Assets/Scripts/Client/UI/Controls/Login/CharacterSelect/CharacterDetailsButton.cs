using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class CharacterDetailsButton : MonoBehaviour
	{
		public Button CharacterButton;
		public TMP_Text CharacterNameLabel;
		public TMP_Text CharacterSceneLabel;

		public CharacterDetails Details;

		public delegate void CharacterSelectEvent(CharacterDetailsButton button);
		public event CharacterSelectEvent OnCharacterSelected;

		private Color labelColor;

		public void Initialize(CharacterDetails details)
		{
			Details = details;
			if (CharacterNameLabel != null)
			{
				CharacterNameLabel.text = details.CharacterName;
				labelColor = CharacterNameLabel.color;
			}
			if (CharacterSceneLabel != null)
			{
				CharacterSceneLabel.text = details.SceneName;
			}
			gameObject.SetActive(true);
		}

		public void OnClick_CharacterButton()
		{
			OnCharacterSelected?.Invoke(this);
		}

		public void ResetLabelColor()
		{
			if (CharacterNameLabel == null)
			{
				return;
			}
			CharacterNameLabel.color = labelColor;
		}

		public void SetLabelColors(Color color)
		{
			if (CharacterNameLabel == null)
			{
				return;
			}

			labelColor = CharacterNameLabel.color;
			CharacterNameLabel.color = color;
		}
	}
}