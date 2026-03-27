#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom Unity editor for TeleporterDestination. Inherits height adjustment logic from BaseHeightAdjustEditor.
	/// Automatically registers the destination in the TeleporterCache when placed or modified.
	/// </summary>
	[CustomEditor(typeof(TeleporterDestination))]
	public class TeleporterDestinationEditor : BaseHeightAdjustEditor
	{
		/// <summary>
		/// Draws the default inspector and displays the current DestinationID.
		/// Auto-registers the destination in the TeleporterCache when changes are detected.
		/// </summary>
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			TeleporterDestination destination = (TeleporterDestination)target;

			if (string.IsNullOrEmpty(destination.DestinationID))
			{
				EditorGUILayout.HelpBox("This destination has no DestinationID. Remove and re-add the component to generate one.", MessageType.Error);
				return;
			}

			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.TextField("Destination ID", destination.DestinationID);
			EditorGUI.EndDisabledGroup();

			if (GUILayout.Button("Update Cache Entry"))
			{
				string sceneName = SceneManager.GetActiveScene().name;
				TeleporterCache.RegisterDestination(destination, sceneName);
			}
		}

		/// <summary>
		/// Auto-updates the cache entry when the destination is moved in the scene view.
		/// </summary>
		protected override void HandleMouseUp(GameObject clickedObject)
		{
			base.HandleMouseUp(clickedObject);

			if (clickedObject == null)
			{
				return;
			}

			TeleporterDestination destination = clickedObject.GetComponent<TeleporterDestination>();
			if (destination != null && !string.IsNullOrEmpty(destination.DestinationID))
			{
				string sceneName = SceneManager.GetActiveScene().name;
				TeleporterCache.RegisterDestination(destination, sceneName);
			}
		}
	}
}
#endif