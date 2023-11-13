using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("UI/Custom/RageInputField", 34), RequireComponent(typeof(RectTransform))]
public class RageInputField : InputField
{
	public void SetText(string value)
	{
		if (text == value)
		{
			return;
		}
		m_Text = value;
		if (!Application.isPlaying)
		{
			UpdateLabel();
			return;
		}
		if (m_Keyboard != null)
		{
			m_Keyboard.text = m_Text;
		}
		if (m_CaretPosition > m_Text.Length)
		{
			m_CaretPosition = (m_CaretSelectPosition = m_Text.Length);
		}
		UpdateLabel();
	}
}