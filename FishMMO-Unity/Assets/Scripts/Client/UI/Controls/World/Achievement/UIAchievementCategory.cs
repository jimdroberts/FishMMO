using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// UI component for displaying an achievement category button with its name label.
	/// </summary>
	public class UIAchievementCategory : MonoBehaviour
	{
		/// <summary>
		/// The button used to select the achievement category.
		/// </summary>
		public Button Button;
		/// <summary>
		/// The label displaying the achievement category name.
		/// </summary>
		public TMP_Text Label;
	}
}