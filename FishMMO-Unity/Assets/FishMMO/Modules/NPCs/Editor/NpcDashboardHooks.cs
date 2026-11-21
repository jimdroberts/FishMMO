#if UNITY_EDITOR
using FishMMO.Server.Implementation.World.SceneServer.AI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// The NPCs module's dashboard hooks (2026-11-20): new AI assets file themselves by type, and a behaviour tree's
	/// inspector offers its graph editor.
	/// </summary>
	internal static class NpcDashboardHooks
	{
		[InitializeOnLoadMethod]
		private static void Install()
		{
			/* The "AI States" category covers attacking, movement, pet and positioning states, which live in different
			 * sub-folders; the organizer knows which. */
			DashboardHooks.AssetFolderResolvers.Add(AIAssetOrganizer.ResolveFolder);
			DashboardHooks.InspectorExtras.Add(AddBehaviorTreeButton);
		}

		private static void AddBehaviorTreeButton(Object asset, VisualElement inspector)
		{
			if (!(asset is AIBehaviorTree behaviorTree))
			{
				return;
			}
			Button openGraphButton = new Button(() => BehaviorTreeEditorWindow.Open(behaviorTree));
			openGraphButton.text = "Open Behavior Tree Editor";
			openGraphButton.style.marginTop = 4;
			openGraphButton.style.height = 26;
			openGraphButton.style.backgroundColor = new Color(0.22f, 0.30f, 0.45f, 1f);
			openGraphButton.style.color = new Color(0.80f, 0.88f, 1f, 1f);
			inspector.Add(openGraphButton);
		}
	}
}
#endif
