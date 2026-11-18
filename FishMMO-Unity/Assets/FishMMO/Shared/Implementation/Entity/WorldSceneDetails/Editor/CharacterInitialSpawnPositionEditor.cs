using UnityEditor;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom Unity editor for adjusting the initial spawn position of a character in the scene. Inherits height adjustment logic from BaseHeightAdjustEditor.
	/// </summary>
	[CustomEditor(typeof(CharacterInitialSpawnPosition))]
	public class CharacterInitialSpawnPositionInspector : BaseHeightAdjustEditor
	{
		// Inherits all height adjustment and mouse event logic from BaseHeightAdjustEditor.
	}
}