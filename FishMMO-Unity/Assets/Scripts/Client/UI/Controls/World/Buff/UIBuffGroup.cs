using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
	public class UIBuffGroup : MonoBehaviour
	{
		/// <summary>
		/// The icon representing the buff or debuff.
		/// </summary>
		public Image Icon;
		/// <summary>
		/// The tooltip button for displaying additional information about the buff or debuff.
		/// </summary>
		public UITooltipButton TooltipButton;
		/// <summary>
		/// The slider showing the remaining duration of the buff or debuff.
		/// </summary>
		public Slider DurationSlider;
		/// <summary>
		/// The text label for the buff or debuff button.
		/// </summary>
		public TMP_Text ButtonText;
	}
}