#if UNITY_EDITOR
using UnityEditor;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom editor for the interactable Teleporter. Provides a dropdown to select a destination
	/// from the TeleporterCache for cross-scene teleportation when no direct Target is set.
	/// </summary>
	[CustomEditor(typeof(Teleporter))]
	public class TeleporterEditor : Editor
	{
		/// <summary>
		/// Draws the default inspector plus a destination dropdown populated from the TeleporterCache.
		/// Shows a warning if the current DestinationID is set but missing from the cache.
		/// </summary>
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			SerializedProperty destinationIDProp = serializedObject.FindProperty("DestinationID");
			TeleporterDestinationDropdown.Draw(serializedObject, destinationIDProp);
		}
	}
}
#endif