using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class CharacterDetailsButton : MonoBehaviour
	{
		public Button characterButton;
		public TMP_Text characterNameLabel;
		public CharacterDetails details;

		public delegate void CharacterSelectEvent(CharacterDetailsButton button);
		public event CharacterSelectEvent OnCharacterSelected;

		public void Initialize(CharacterDetails details)
		{
			this.details = details;
			characterNameLabel.text = details.characterName;
		}

		public void OnClick_CharacterButton()
		{
			OnCharacterSelected?.Invoke(this);
		}

		public void SetLabelColors(Color color)
		{
			characterNameLabel.color = color;
		}
	}
}