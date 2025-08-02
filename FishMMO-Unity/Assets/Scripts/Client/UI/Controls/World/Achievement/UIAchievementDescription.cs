using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
	public class UIAchievementDescription : MonoBehaviour
	{
		/// <summary>
		/// The image representing the achievement icon.
		/// </summary>
		public Image Image;
		/// <summary>
		/// The label displaying the achievement description.
		/// </summary>
		public TMP_Text Label;
		/// <summary>
		/// The slider showing the achievement progress.
		/// </summary>
		public Slider Progress;
		/// <summary>
		/// The text displaying the current and maximum achievement value.
		/// </summary>
		public TMP_Text Value;
	}
}