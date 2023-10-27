using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class CharacterDetailsButton : MonoBehaviour
	{
		public Button characterButton;
		public TMP_Text characterNameLabel;

		public CharacterDetails Details;

		public delegate void CharacterSelectEvent(CharacterDetailsButton button);
		public event CharacterSelectEvent OnCharacterSelected;

		public void Initialize(CharacterDetails details)
		{
			Details = details;
			characterNameLabel.text = details.CharacterName;
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