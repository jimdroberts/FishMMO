using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Reports the resolved geometry of the dungeon finder's footer: the toggle, whose label used
	/// to truncate, and the three action buttons beside it, which have to fit in the panel with it.
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

					VisualElement liveRoot = liveDoc.rootVisualElement;
					Toggle liveToggle = liveRoot.Q<Toggle>("dungeonfinder-public");
					Dump("live/root", liveRoot);
					Dump("live/footer", liveRoot.Q<VisualElement>("panel-footer"));
					Dump("live/toggle", liveToggle);
					Dump("live/label", liveToggle?.Q<Label>(className: "unity-base-field__label"));

					/* The footer is a row of four controls that share one fixed width, and the panel
					 * clips anything past its edge (theme .fish-panel is overflow:hidden). Measure the
					 * whole chain so the budget question is answered with numbers: how wide the panel
					 * gives the row, how wide the row's children each resolve, and where the last one
					 * lands relative to the panel's own right edge. */
					VisualElement livePanel = liveRoot.Q<VisualElement>("dungeonfinder-panel");
					VisualElement liveFooter = liveRoot.Q<VisualElement>("panel-footer");
					VisualElement liveActions = liveFooter?.Q<VisualElement>(className: "dungeonfinder-footer__actions");
					Dump("live/panel", livePanel);
					Dump("live/actions", liveActions);
					Dump("live/btn-find", liveRoot.Q<Button>("dungeonfinder-findgroup-btn"));
					Dump("live/btn-refresh", liveRoot.Q<Button>("dungeonfinder-refresh-btn"));
					Dump("live/btn-start", liveRoot.Q<Button>("dungeonfinder-start-btn"));

					Debug.Log($"[Footer] budget: actions children sum=" +
						$"{SumChildren(liveActions):F2} + footer padding L/R=" +
						$"{liveFooter.resolvedStyle.paddingLeft}/{liveFooter.resolvedStyle.paddingRight}" +
						$" toggle={liveToggle?.resolvedStyle.width:F2} panel={livePanel.resolvedStyle.width:F2}");

					Clip("live/panel", livePanel, "footer", liveFooter);
					Clip("live/panel", livePanel, "actions", liveActions);
					Clip("live/panel", livePanel, "toggle", liveToggle);
					Clip("live/panel", livePanel, "btn-find", liveRoot.Q<Button>("dungeonfinder-findgroup-btn"));
					Clip("live/panel", livePanel, "btn-refresh", liveRoot.Q<Button>("dungeonfinder-refresh-btn"));
					Clip("live/panel", livePanel, "btn-start", liveRoot.Q<Button>("dungeonfinder-start-btn"));
					Clip("live/panel", livePanel, "btn-close", liveRoot.Q<Button>("dungeonfinder-close-btn"));
					Clip("live/panel", livePanel, "scroll", liveRoot.Q<ScrollView>("dungeonfinder-scroll"));
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

		/// <summary>Total intrinsic width of a row's children, margins included.</summary>
		private static float SumChildren(VisualElement row)
		{
			if (row == null) { return 0f; }
			float sum = 0f;
			foreach (VisualElement child in row.Children())
			{
				IResolvedStyle s = child.resolvedStyle;
				sum += child.layout.width + s.marginLeft + s.marginRight;
			}
			return sum;
		}

		/// <summary>
		/// Whether an element falls outside the box that clips it. The panel is overflow:hidden, so
		/// anything past its edge is invisible to the player however correctly it laid itself out.
		/// </summary>
		private static void Clip(string panelTag, VisualElement panel, string tag, VisualElement e)
		{
			if (panel == null || e == null) { Debug.Log($"[Footer] clip {tag}: NULL"); return; }

			Rect p = panel.worldBound;
			Rect r = e.worldBound;
			float right = r.xMax - p.xMax;
			float left = p.xMin - r.xMin;
			float bottom = r.yMax - p.yMax;
			float top = p.yMin - r.yMin;
			bool clipped = right > 0.01f || left > 0.01f || bottom > 0.01f || top > 0.01f;

			Debug.Log($"[Footer] clip {panelTag} <- {tag}: {(clipped ? "CLIPPED" : "inside")} " +
				$"world={r.xMin:F2},{r.yMin:F2},{r.width:F2}x{r.height:F2} " +
				$"panel={p.xMin:F2},{p.yMin:F2},{p.width:F2}x{p.height:F2} " +
				$"over right={right:F2} left={left:F2} bottom={bottom:F2} top={top:F2}");
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
