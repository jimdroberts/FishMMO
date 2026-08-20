using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FishMMO.Client.Editor
{
	/// <summary>
	/// Wires UI Toolkit panels into the open scene: for each <see cref="UITKControl"/> subclass
	/// that has no GameObject yet, creates one with a configured <see cref="UIDocument"/> and
	/// deactivates the legacy UGUI panel it replaces.
	/// </summary>
	/// <remarks>
	/// The UI Toolkit migration wrote a control, a UXML and a USS per panel, but placing each one
	/// in a scene stayed a manual step — so a panel could be fully implemented and still never
	/// appear, with the legacy version answering in its place. That is not a state the project
	/// can detect on its own: <see cref="UIManager"/> resolves panels by GameObject name, so an
	/// unwired panel looks exactly like one that was never written.
	/// <para>
	/// Scene authoring belongs in the editor, so this does the mechanical part and reports
	/// anything it will not decide for itself rather than guessing.
	/// </para>
	/// </remarks>
	public static class UITKPanelWiringTool
	{
		/// <summary>Panel settings asset applied to every generated <see cref="UIDocument"/>.</summary>
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

		/// <summary>Prefix on the UI Toolkit control types.</summary>
		private const string ControlPrefix = "UITK";

		/// <summary>Prefix shared by GameObject names, UXML file names and legacy controls.</summary>
		private const string PanelPrefix = "UI";

		/// <summary>
		/// What wiring a single panel would do, or why it cannot be done.
		/// </summary>
		private sealed class PanelPlan
		{
			public Type ControlType;
			public string PanelName;
			public VisualTreeAsset SourceAsset;
			public GameObject LegacyObject;
			public string SkipReason;
		}

		/// <summary>
		/// Reports what wiring would do without changing anything.
		/// </summary>
		[MenuItem("FishMMO/UI Toolkit/Report Unwired Panels", priority = 100)]
		public static void ReportUnwiredPanels()
		{
			List<PanelPlan> plans = BuildPlans();
			Debug.Log(Describe(plans, "Report only — nothing was changed."));
		}

		/// <summary>
		/// Wires every panel that can be wired into the currently open scene.
		/// </summary>
		[MenuItem("FishMMO/UI Toolkit/Wire Unwired Panels Into Open Scene", priority = 101)]
		public static void WireUnwiredPanels()
		{
			List<PanelPlan> plans = BuildPlans();
			List<PanelPlan> actionable = plans.Where(p => p.SkipReason == null).ToList();

			if (actionable.Count == 0)
			{
				Debug.Log(Describe(plans, "Nothing to wire."));
				return;
			}

			Scene scene = SceneManager.GetActiveScene();
			bool proceed = EditorUtility.DisplayDialog(
				"Wire UI Toolkit Panels",
				$"Wire {actionable.Count} panel(s) into '{scene.name}'?\n\n" +
				"Each one gets a new GameObject with a UIDocument and its control, and the legacy " +
				"UGUI panel of the same name is deactivated (not deleted).\n\n" +
				"This is undoable, and the scene is not saved for you.",
				"Wire Them",
				"Cancel");

			if (!proceed)
			{
				return;
			}

			PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			if (panelSettings == null)
			{
				Debug.LogError($"[UITKPanelWiringTool] Panel settings not found at '{PanelSettingsPath}'. Nothing was wired.");
				return;
			}

			int undoGroup = Undo.GetCurrentGroup();
			Undo.SetCurrentGroupName("Wire UI Toolkit Panels");

			foreach (PanelPlan plan in actionable)
			{
				WirePanel(plan, panelSettings);
			}

			Undo.CollapseUndoOperations(undoGroup);
			EditorSceneManager.MarkSceneDirty(scene);

			Debug.Log(Describe(plans, $"Wired {actionable.Count} panel(s) into '{scene.name}'. Save the scene to keep this."));
		}

		/// <summary>
		/// Creates the GameObject, document and control for one panel, and retires its legacy twin.
		/// </summary>
		private static void WirePanel(PanelPlan plan, PanelSettings panelSettings)
		{
			GameObject go = new GameObject(plan.PanelName);
			Undo.RegisterCreatedObjectUndo(go, "Create UI Toolkit Panel");

			UIDocument document = Undo.AddComponent<UIDocument>(go);
			document.panelSettings = panelSettings;
			document.visualTreeAsset = plan.SourceAsset;

			UITKControl control = (UITKControl)Undo.AddComponent(go, plan.ControlType);
			control.Document = document;

			/* Visibility flags are copied from the legacy panel rather than defaulted. They decide
			 * whether a panel is on screen at world entry and whether quitting to login closes it,
			 * and the answer differs per panel — a HUD element that is always open and a dialog
			 * that opens on demand disagree on every one of them. The legacy panel is the only
			 * place that answer already exists. */
			if (plan.LegacyObject != null)
			{
				UIControl legacy = plan.LegacyObject.GetComponent<UIControl>();
				if (legacy != null)
				{
					control.StartOpen = legacy.StartOpen;
					control.IsAlwaysOpen = legacy.IsAlwaysOpen;
					control.CloseOnQuitToMenu = legacy.CloseOnQuitToMenu;

					/* CloseOnEscape is what gates the legacy cursor release, so it is the existing
					 * answer to "is this a window the player clicks, or a HUD element?" — which is
					 * exactly what ReleasesCursor decides. Different name, same question. */
					control.ReleasesCursor = legacy.CloseOnEscape;
				}

				if (plan.LegacyObject.activeSelf)
				{
					Undo.RecordObject(plan.LegacyObject, "Deactivate Legacy Panel");
					plan.LegacyObject.SetActive(false);
				}
			}

			EditorUtility.SetDirty(go);
		}

		/// <summary>
		/// Works out, for every UI Toolkit control type, whether it can be wired and how.
		/// </summary>
		private static List<PanelPlan> BuildPlans()
		{
			List<PanelPlan> plans = new List<PanelPlan>();

			IEnumerable<Type> controlTypes = TypeCache.GetTypesDerivedFrom<UITKControl>()
				.Where(t => !t.IsAbstract)
				.OrderBy(t => t.Name);

			GameObject[] sceneRoots = SceneManager.GetActiveScene().GetRootGameObjects();

			foreach (Type type in controlTypes)
			{
				PanelPlan plan = new PanelPlan { ControlType = type };

				if (!type.Name.StartsWith(ControlPrefix, StringComparison.Ordinal))
				{
					plan.SkipReason = $"type name does not start with '{ControlPrefix}'";
					plans.Add(plan);
					continue;
				}

				plan.PanelName = PanelPrefix + type.Name.Substring(ControlPrefix.Length);

				// Already wired: a control of this exact type is present in the scene.
				if (sceneRoots.Any(r => r.GetComponentsInChildren(type, true).Length > 0))
				{
					plan.SkipReason = "already in this scene";
					plans.Add(plan);
					continue;
				}

				plan.SourceAsset = FindSourceAsset(type, plan.PanelName);
				if (plan.SourceAsset == null)
				{
					/* Panels that share a UXML — the resource bars all use UIResourceBar.uxml —
					 * cannot be resolved from the name alone, and picking one would be a guess
					 * about which document a panel is meant to display. Reported instead. */
					plan.SkipReason = $"no '{plan.PanelName}.uxml' beside the script; wire this one by hand";
					plans.Add(plan);
					continue;
				}

				plan.LegacyObject = FindLegacyPanel(sceneRoots, plan.PanelName);
				if (plan.LegacyObject == null)
				{
					/* A panel is wired into the scene that already hosts the version it replaces.
					 * Without that twin there is nothing to say this panel belongs here: the login
					 * panels live in ClientLoginGUI and the shared dialogs in ClientPreboot, and
					 * creating them here would put a login screen in the world scene. The same
					 * answer covers a genuinely new panel that has no legacy version anywhere —
					 * where it goes is a design decision, not one to infer from a name. */
					plan.SkipReason = $"no legacy '{plan.PanelName}' in this scene; it likely belongs to another scene, or is new — place this one by hand";
					plans.Add(plan);
					continue;
				}

				plans.Add(plan);
			}

			return plans;
		}

		/// <summary>
		/// Finds the UXML sitting beside the control's own source file.
		/// </summary>
		private static VisualTreeAsset FindSourceAsset(Type type, string panelName)
		{
			string[] scriptGuids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
			foreach (string guid in scriptGuids)
			{
				string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
				if (Path.GetFileNameWithoutExtension(scriptPath) != type.Name)
				{
					continue;
				}

				string directory = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
				if (string.IsNullOrEmpty(directory))
				{
					continue;
				}

				string candidate = $"{directory}/{panelName}.uxml";
				VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(candidate);
				if (asset != null)
				{
					return asset;
				}
			}

			return null;
		}

		/// <summary>
		/// Finds an active legacy UGUI panel occupying the name this panel wants.
		/// </summary>
		/// <remarks>
		/// Matched by name because that is what <see cref="UIManager"/> resolves on. Two objects
		/// may end up sharing a name, one of them inactive — which is what the already-migrated
		/// panels look like, and is harmless: an inactive GameObject never runs Awake, so it never
		/// registers.
		/// </remarks>
		private static GameObject FindLegacyPanel(GameObject[] sceneRoots, string panelName)
		{
			foreach (GameObject root in sceneRoots)
			{
				foreach (UIControl legacy in root.GetComponentsInChildren<UIControl>(true))
				{
					// Inactive ones count. A panel that opens on demand may sit deactivated in the
					// scene, and it still carries the visibility flags worth copying.
					if (legacy.gameObject.name == panelName)
					{
						return legacy.gameObject;
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Builds a readable summary of what was, or would be, done.
		/// </summary>
		private static string Describe(List<PanelPlan> plans, string headline)
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine($"[UITKPanelWiringTool] {headline}");

			List<PanelPlan> actionable = plans.Where(p => p.SkipReason == null).ToList();
			if (actionable.Count > 0)
			{
				sb.AppendLine($"\nWirable ({actionable.Count}):");
				foreach (PanelPlan p in actionable)
				{
					string legacy = p.LegacyObject != null && p.LegacyObject.activeSelf ? " (deactivates legacy panel)" : " (legacy already inactive)";
					sb.AppendLine($"  {p.PanelName}  <- {p.ControlType.Name}{legacy}");
				}
			}

			List<PanelPlan> needsHand = plans
				.Where(p => p.SkipReason != null && p.SkipReason != "already in this scene")
				.ToList();
			if (needsHand.Count > 0)
			{
				sb.AppendLine($"\nNeeds a decision ({needsHand.Count}):");
				foreach (PanelPlan p in needsHand)
				{
					sb.AppendLine($"  {p.ControlType.Name}: {p.SkipReason}");
				}
			}

			int already = plans.Count(p => p.SkipReason == "already in this scene");
			sb.AppendLine($"\nAlready in this scene: {already}");

			return sb.ToString();
		}
	}
}
