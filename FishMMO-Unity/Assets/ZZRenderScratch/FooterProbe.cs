using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Reports the resolved geometry of the dungeon finder's footer toggle, which renders its
	/// label truncated even though the row has slack to spare.
	/// </summary>
	public static class FooterProbe
	{
		private const string UXML = "Assets/Scripts/Client/GUI/World/DungeonFinder/UIDungeonFinder.uxml";
		private const string INSTANCE_UXML = "Assets/Scripts/Client/GUI/World/Instance/UIInstance.uxml";
		private const string PANEL_SETTINGS = "Assets/UI Toolkit/PanelSettings.asset";

		[MenuItem("FishMMO/UI Toolkit/Probe Footer")]
		public static void Run()
		{
			GameObject host = new GameObject("FooterProbe") { hideFlags = HideFlags.HideAndDontSave };
			try
			{
				RenderTexture rt = new RenderTexture(1200, 900, 24);
				rt.Create();

				PanelSettings settings = UnityEngine.Object.Instantiate(
					AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = rt;

				UIDocument doc = host.AddComponent<UIDocument>();
				doc.panelSettings = settings;
				doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML);

				VisualElement root = doc.rootVisualElement;
				root.MarkDirtyRepaint();

				Settle(doc);

				Toggle toggle = root.Q<Toggle>("dungeonfinder-public");
				Dump("df/footer", root.Q<VisualElement>("panel-footer"));
				Dump("df/toggle", toggle);
				Dump("df/label", toggle?.Q<Label>(className: "unity-base-field__label"));
				Dump("df/input", toggle?.Q<VisualElement>(className: "unity-base-field__input"));

				/* The same panel again, but driven through its own C# the way the capture does.
				 * The raw tree above is only the UXML; anything the panel changes about its own
				 * layout at runtime shows up as a difference between these two dumps. */
				GameObject liveHost = new GameObject("FooterProbeLive") { hideFlags = HideFlags.HideAndDontSave };
				try
				{
					UIDocument liveDoc = liveHost.AddComponent<UIDocument>();
					liveDoc.panelSettings = settings;
					liveDoc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML);
					Panels.DungeonFinder(liveHost, liveDoc);
					Settle(liveDoc);

					Toggle liveToggle = liveDoc.rootVisualElement.Q<Toggle>("dungeonfinder-public");
					Dump("live/root", liveDoc.rootVisualElement);
					Dump("live/footer", liveDoc.rootVisualElement.Q<VisualElement>("panel-footer"));
					Dump("live/toggle", liveToggle);
					Dump("live/label", liveToggle?.Q<Label>(className: "unity-base-field__label"));
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(liveHost);
				}

				// The same toggle in the Instance panel, which renders its label in full.
				doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(INSTANCE_UXML);
				Settle(doc);
				Toggle other = doc.rootVisualElement.Q<Toggle>("instance-privacy");
				Dump("inst/toggle", other);
				Dump("inst/label", other?.Q<Label>(className: "unity-base-field__label"));
			}
			catch (Exception ex)
			{
				Debug.LogError("[Footer] " + ex);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(host);
				EditorApplication.Exit(0);
			}
		}

		/// <summary>Drives enough panel updates for layout to resolve.</summary>
		private static void Settle(UIDocument doc)
		{
			for (int i = 0; i < 8; ++i)
			{
				doc.rootVisualElement?.MarkDirtyRepaint();
				typeof(UIDocument).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility")
					?.GetMethod("UpdatePanels", System.Reflection.BindingFlags.Static
						| System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
					?.Invoke(null, null);
			}
		}

		private static void Dump(string tag, VisualElement e)
		{
			if (e == null) { Debug.Log($"[Footer] {tag}: NULL"); return; }

			IResolvedStyle s = e.resolvedStyle;
			string text = "";
			if (e is TextElement t)
			{
				Vector2 measured = t.MeasureTextSize(t.text, 0, VisualElement.MeasureMode.Undefined,
					0, VisualElement.MeasureMode.Undefined);
				string font = s.unityFontDefinition.fontAsset != null
					? s.unityFontDefinition.fontAsset.name
					: (s.unityFontDefinition.font != null ? s.unityFontDefinition.font.name : "(none)");
				text = $" text='{t.text}' measured={measured.x:F2} font={font} " +
					$"style={s.unityFontStyleAndWeight} letterSpacing={s.letterSpacing}";
			}

			Debug.Log($"[Footer] {tag}: rect={e.layout} " +
				$"w={s.width} minW={s.minWidth} maxW={s.maxWidth} " +
				$"grow={s.flexGrow} shrink={s.flexShrink} basis={s.flexBasis} " +
				$"font={s.fontSize}{text} " +
				$"marginR={s.marginRight} classes=[{string.Join(",", e.GetClasses())}]");
		}
	}
}
