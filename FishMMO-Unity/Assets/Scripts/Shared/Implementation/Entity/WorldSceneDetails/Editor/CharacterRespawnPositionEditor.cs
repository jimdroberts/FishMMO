using UnityEditor;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom Unity editor for adjusting the respawn position of a character in the scene. Inherits height adjustment logic from BaseHeightAdjustEditor.
	/// </summary>
	[CustomEditor(typeof(CharacterRespawnPosition))]
	public class CharacterRespawnPositionEditor : BaseHeightAdjustEditor
	{
		// Inherits all height adjustment and mouse event logic from BaseHeightAdjustEditor.
	}
}