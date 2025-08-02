using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Custom InputField for UI that allows setting text programmatically and updates the label accordingly.
/// </summary>
[AddComponentMenu("UI/Custom/RageInputField", 34), RequireComponent(typeof(RectTransform))]
public class RageInputField : InputField
{
	/// <summary>
	/// Sets the text value of the input field and updates the label and caret position.
	/// </summary>
	/// <param name="value">The new text value to set.</param>
	public void SetText(string value)
	{
		// If the value is unchanged, do nothing
		if (text == value)
		{
			return;
		}
		// Set the internal text value
		m_Text = value;
		// If not playing, just update the label
		if (!Application.isPlaying)
		{
			UpdateLabel();
			return;
		}
		// Update the keyboard text if present
		if (m_Keyboard != null)
		{
			m_Keyboard.text = m_Text;
		}
		// Ensure caret position is valid after text change
		if (m_CaretPosition > m_Text.Length)
		{
			m_CaretPosition = (m_CaretSelectPosition = m_Text.Length);
		}
		UpdateLabel();
	}
}