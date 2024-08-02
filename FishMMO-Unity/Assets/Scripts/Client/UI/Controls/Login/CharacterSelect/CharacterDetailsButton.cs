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

		public CharacterDetails Details;

		public delegate void CharacterSelectEvent(CharacterDetailsButton button);
		public event CharacterSelectEvent OnCharacterSelected;

		private Color labelColor;

		public void Initialize(CharacterDetails details)
		{
			Details = details;
			CharacterNameLabel.text = details.CharacterName;
			gameObject.SetActive(true);

			labelColor = CharacterNameLabel.color;
		}

		public void OnClick_CharacterButton()
		{
			OnCharacterSelected?.Invoke(this);
		}

		public void ResetLabelColor()
		{
			CharacterNameLabel.color = labelColor;
		}

		public void SetLabelColors(Color color)
		{
			labelColor = CharacterNameLabel.color;
			CharacterNameLabel.color = color;
		}
	}
}