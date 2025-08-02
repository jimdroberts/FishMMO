using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class CharacterDetailsButton : MonoBehaviour
	{
		/// <summary>
		/// The button component for selecting the character.
		/// </summary>
		public Button CharacterButton;
		/// <summary>
		/// The label displaying the character's name.
		/// </summary>
		public TMP_Text CharacterNameLabel;
		/// <summary>
		/// The label displaying the character's scene name.
		/// </summary>
		public TMP_Text CharacterSceneLabel;

		/// <summary>
		/// The details of the character represented by this button.
		/// </summary>
		public CharacterDetails Details;

		/// <summary>
		/// Delegate for character selection event.
		/// </summary>
		/// <param name="button">The button that was selected.</param>
		public delegate void CharacterSelectEvent(CharacterDetailsButton button);
		/// <summary>
		/// Event triggered when this character button is selected.
		/// </summary>
		public event CharacterSelectEvent OnCharacterSelected;

		/// <summary>
		/// The color used for the character name label (for reset purposes).
		/// </summary>
		private Color labelColor;

		/// <summary>
		/// Initializes the button with character details and sets up labels.
		/// </summary>
		/// <param name="details">The details of the character.</param>
		public void Initialize(CharacterDetails details)
		{
			Details = details;
			// Set character name and store original color for later reset
			if (CharacterNameLabel != null)
			{
				CharacterNameLabel.text = details.CharacterName;
				labelColor = CharacterNameLabel.color;
			}
			// Set character scene name
			if (CharacterSceneLabel != null)
			{
				CharacterSceneLabel.text = details.SceneName;
			}
			// Ensure the button is active in the UI
			gameObject.SetActive(true);
		}

		/// <summary>
		/// Called when the character button is clicked. Triggers selection event.
		/// </summary>
		public void OnClick_CharacterButton()
		{
			OnCharacterSelected?.Invoke(this);
		}

		/// <summary>
		/// Resets the character name label color to its original value.
		/// </summary>
		public void ResetLabelColor()
		{
			if (CharacterNameLabel == null)
			{
				return;
			}
			CharacterNameLabel.color = labelColor;
		}

		/// <summary>
		/// Sets the character name label color to the specified color and stores the previous color for reset.
		/// </summary>
		/// <param name="color">The color to set for the label.</param>
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