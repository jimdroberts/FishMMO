#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Atlas;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The scene settings inspector, with the scene's world atlas entry above the scene's own
	/// values: where the scene is, what it overrides, and a button to open it in the atlas.
	/// </summary>
	[CustomEditor(typeof(WorldSceneSettings))]
	public class WorldSceneSettingsEditor : Editor
	{
		public override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();
			var settings = (WorldSceneSettings)target;
			string sceneName = settings.gameObject.scene.name;
			WorldAtlasScene entry = WorldAtlasScene.Find(sceneName);

			var box = new HelpBox(string.Empty, HelpBoxMessageType.Info);
			if (entry == null)
			{
				box.text = $"{sceneName} has no world atlas entry, so it sits on the home world at longitude 0 with its own climate. Open the World Atlas to add and place it.";
				box.messageType = HelpBoxMessageType.Warning;
			}
			else
			{
				string where = entry.Placed
					? $"{(entry.Body != null ? entry.Body.ResolvedName : "the home world")}, {entry.Latitude:0.##}°, {entry.Longitude:0.##}°, time zone {entry.TimeZone:+0;-0} h"
					: "not placed yet";
				string overrides = string.Empty;
				if (entry.Climate != null) overrides += " climate,";
				if (entry.BiomeMap != null) overrides += " biome map,";
				if (entry.MaxClients > 0) overrides += " client cap,";
				box.text = $"World atlas: {where}. Weather {entry.EffectiveWeather}, time {entry.TimeMode}." +
					(overrides.Length > 0 ? $" The atlas overrides this scene's{overrides.TrimEnd(',')}." : " Climate, biome map and client cap below are used as authored.");
			}
			root.Add(box);

			var buttons = new VisualElement();
			buttons.style.flexDirection = FlexDirection.Row;
			buttons.style.marginBottom = 6f;
			buttons.Add(new Button(() => FishMMODashboard.OpenWorldAtlas(sceneName)) { text = "Open in World Atlas" });
			if (entry != null)
			{
				buttons.Add(new Button(() => Selection.activeObject = entry) { text = "Select atlas entry" });
			}
			root.Add(buttons);

			InspectorElement.FillDefaultInspector(root, serializedObject, this);
			return root;
		}
	}
}
#endif
