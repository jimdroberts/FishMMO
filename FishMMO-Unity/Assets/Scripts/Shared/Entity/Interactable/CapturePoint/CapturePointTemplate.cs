using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Template defining a capture point's parameters for PvP or general objective capture.
	/// </summary>
	[CreateAssetMenu(fileName = "New Capture Point", menuName = "FishMMO/Interactable/Capture Point", order = 1)]
	public class CapturePointTemplate : CachedScriptableObject<CapturePointTemplate>, ICachedObject
	{
		/// <summary>
		/// Optional icon for the capture point in the UI.
		/// </summary>
		public Sprite Icon;

		/// <summary>
		/// Description displayed in tooltips or UI.
		/// </summary>
		[TextArea(2, 4)]
		public string Description;

		/// <summary>
		/// Score value awarded when this point is captured.
		/// </summary>
		[Min(1)]
		public int PointValue = 1;

		/// <summary>
		/// Number of interactions required to capture this point.
		/// </summary>
		[Min(1)]
		public int InteractionsToCapture = 1;

		/// <summary>
		/// The display name of this capture point template.
		/// </summary>
		public string Name { get { return this.name; } }
	}
}