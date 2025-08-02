using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// UI component for displaying a single faction's description, progress, and value.
	/// </summary>
	public class UIFactionDescription : MonoBehaviour
	{
		/// <summary>
		/// The image representing the faction icon.
		/// </summary>
		public Image Image;

		/// <summary>
		/// The label displaying the faction name and description.
		/// </summary>
		public TMP_Text Label;

		/// <summary>
		/// The slider representing the faction progress value.
		/// </summary>
		public Slider Progress;

		/// <summary>
		/// The image used to fill the progress bar and show color.
		/// </summary>
		public Image ProgressFillImage;

		/// <summary>
		/// The text displaying the numeric value of the faction.
		/// </summary>
		public TMP_Text Value;
	}
}