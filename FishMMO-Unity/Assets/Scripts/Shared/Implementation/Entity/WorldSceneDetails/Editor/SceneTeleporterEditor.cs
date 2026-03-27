#if UNITY_EDITOR
using UnityEditor;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom editor for SceneTeleporter. Provides a dropdown to select a destination from the TeleporterCache,
	/// grouped by scene name for intuitive selection at development time.
	/// </summary>
	[CustomEditor(typeof(SceneTeleporter))]
	public class SceneTeleporterEditor : Editor
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