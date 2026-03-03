using UnityEditor;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom Unity editor for adjusting the destination position of a teleporter in the scene. Inherits height adjustment logic from BaseHeightAdjustEditor.
	/// </summary>
	[CustomEditor(typeof(TeleporterDestination))]
	public class TeleporterDestinationEditor : BaseHeightAdjustEditor
	{
		// Inherits all height adjustment and mouse event logic from BaseHeightAdjustEditor.
	}
}