using UnityEngine;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// UI component for displaying a single character attribute's name and value.
	/// </summary>
	public class UIAttribute : MonoBehaviour
	{
		/// <summary>
		/// The label displaying the attribute name.
		/// </summary>
		public TMP_Text Name;

		/// <summary>
		/// The label displaying the attribute value.
		/// </summary>
		public TMP_Text Value;
	}
}