using System;
using FishMMO.Shared.Core;
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Housing module's plot layer: what the world generator's housing step (<c>HousingPlotStage</c>, WorldDesign) asks to
	/// turn each lot's post into a working plot, through the core seam <see cref="PlotLayers"/>.
	/// </summary>
	/// <remarks>
	/// Registered at editor load. Without this module no layer is registered and the generator plans no lots at all
	/// (second-wave moves: PlotFoundation and the plot actions left core; the generator must not reference the
	/// module).
	/// </remarks>
	[InitializeOnLoad]
	public sealed class HousingPlotLayer : IPlotLayer
	{
		/// <summary>The deed posts' shared interaction trigger: opens the plot panel.</summary>
		public const string TriggerPath = "Assets/FishMMO/Modules/Housing/Templates/Interactions/Plot Interact.asset";

		/// <summary>How close a player must stand to a deed post to use it, in metres.</summary>
		private const float InteractionRange = 4f;

		static HousingPlotLayer()
		{
			PlotLayers.Register(new HousingPlotLayer());
		}

		public Component[] Lay(GameObject post, string plotKey, long price, Vector2 size, float volumeHeight, Vector3 volumeOffset,
			Func<string, Color, Material> material)
		{
			PlotFoundation foundation = post.AddComponent<PlotFoundation>();
			foundation.EditorConfigure(plotKey, price, size, volumeHeight, volumeOffset);
			foundation.InteractionRange = InteractionRange;
			// The post keeps its own name: the required namer would otherwise try to give it a person's.
			SceneObjectNamer namer = post.GetComponent<SceneObjectNamer>();
			if (namer != null)
			{
				var namerSerialized = new SerializedObject(namer);
				SerializedProperty mode = namerSerialized.FindProperty("settings.Mode");
				if (mode != null)
				{
					mode.enumValueIndex = Array.IndexOf(Enum.GetValues(typeof(SceneObjectNamingMode)), SceneObjectNamingMode.Authored);
					namerSerialized.ApplyModifiedPropertiesWithoutUndo();
				}
			}
			Trigger trigger = EnsureTrigger();
			if (trigger != null)
			{
				var serialized = new SerializedObject(foundation);
				SerializedProperty list = serialized.FindProperty("onInteractTriggers");
				list.ClearArray();
				list.InsertArrayElementAtIndex(0);
				list.GetArrayElementAtIndex(0).objectReferenceValue = trigger;
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}

			PlotFoundationVisual visual = post.AddComponent<PlotFoundationVisual>();
			visual.Wood = material("Plot Wood", new Color(0.36f, 0.25f, 0.15f));
			visual.Rope = material("Plot Rope", new Color(0.66f, 0.56f, 0.38f));
			visual.Stone = material("Plot Stone", new Color(0.52f, 0.5f, 0.47f));
			visual.Board = material("Plot Board", new Color(0.72f, 0.6f, 0.42f));
			return new Component[] { foundation, visual };
		}

		/// <summary>The deed posts' interaction trigger, made (opening the plot panel) if it is missing.</summary>
		private static Trigger EnsureTrigger()
		{
			Trigger trigger = AssetDatabase.LoadAssetAtPath<Trigger>(TriggerPath);
			if (trigger == null)
			{
				trigger = ScriptableObject.CreateInstance<Trigger>();
				AssetDatabase.CreateAsset(trigger, TriggerPath);
			}
			bool hasAction = false;
			foreach (BaseAction action in trigger.OnConditionsMetActions)
			{
				hasAction |= action is OpenPlotPanelAction;
			}
			if (!hasAction)
			{
				trigger.OnConditionsMetActions.Add(new OpenPlotPanelAction());
				EditorUtility.SetDirty(trigger);
			}
			WorldEditorAssets.RegisterAddressable(trigger);
			return trigger;
		}
	}
}
