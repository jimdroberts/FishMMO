using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// UI group for a hotkey, containing the button, label, and cooldown mask.
	/// Handles initialization and cleanup of hotkey UI elements.
	/// </summary>
	public class UIHotkeyGroup : MonoBehaviour
	{
		/// <summary>
		/// The hotkey button associated with this group.
		/// </summary>
		public UIHotkeyButton Button;

		/// <summary>
		/// The label displaying the hotkey name or description.
		/// </summary>
		public TMP_Text Label;

		/// <summary>
		/// The slider used to visually represent cooldown progress.
		/// </summary>
		public Slider CooldownMask;

		/// <summary>
		/// Unity Awake callback. Initializes the cooldown mask for the button.
		/// </summary>
		private void Awake()
		{
			// Link the cooldown mask to the button and reset its value
			if (Button != null &&
				CooldownMask != null)
			{
				Button.CooldownMask = CooldownMask;
				CooldownMask.value = 0;
			}
		}

		/// <summary>
		/// Unity OnDestroy callback. Cleans up references on the button.
		/// </summary>
		private void OnDestroy()
		{
			// Unlink the cooldown mask and reset the hotkey slot
			if (Button != null)
			{
				Button.CooldownMask = null;
				Button.HotkeySlot = 0;
			}
		}
	}
}