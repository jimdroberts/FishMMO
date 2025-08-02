using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Custom Slider for UI that allows setting the value programmatically without sending events.
/// </summary>
[AddComponentMenu("UI/Custom/RageSlider", 34), RequireComponent(typeof(RectTransform))]
public class RageSlider : Slider
{
	/// <summary>
	/// Sets the slider value without sending a value changed event.
	/// </summary>
	/// <param name="value">The new value to set.</param>
	public void SetValue(float value)
	{
		Set(value, false);
	}
}