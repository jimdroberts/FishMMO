#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// Tool pages: the dashboard's home for what used to be <c>FishMMO/…</c> menu items.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Tools are static editor methods tagged <see cref="DashboardToolAttribute"/>, found through
	/// <see cref="TypeCache"/> so they can live in any editor assembly that references this one.
	/// A few live in assemblies this one references instead (the world map baker), which cannot
	/// see the attribute; those are listed in <see cref="BuiltInTools"/>.
	/// </para>
	/// <para>
	/// A page is either a sidebar entry of its own (Validate, Unit Tests, Maintenance, AI Tools,
	/// World Map) or the landing view of an asset category (World Scene Details, Spawn Tables),
	/// shown until an asset is selected.
	/// </para>
	/// </remarks>
	public partial class FishMMODashboard
	{
		/// <summary>Sidebar name of the embedded name generator.</summary>
		private const string NAME_GENERATION_CATEGORY = "Name Generation";

		/// <summary>One button on a tool page.</summary>
		private sealed class DashboardTool
		{
			public string Page;
			public string Section;
			public string Label;
			public string Tooltip;
			public string Confirm;
			public int Order;
			public Action Run;
			/// <summary>
			/// For long jobs that finish on later editor updates: starts the job, then calls the
			/// callback with a one-line result. Returns false when nothing was started.
			/// </summary>
			public Func<Action<string>, bool> RunAsync;
		}

		/// <summary>True while a long-running tool is working; every tool button is locked meanwhile.</summary>
		private static bool AnyToolBusy => FishMMO.Shared.WorldMaps.WorldMapBaker.IsBusy;

		/// <summary>Tool buttons on the page being shown, relocked whenever a job starts or ends.</summary>
		private readonly List<Button> toolButtons = new List<Button>();

		private void RefreshToolButtons()
		{
			bool enabled = !EditorApplication.isPlayingOrWillChangePlaymode && !AnyToolBusy;
			toolButtons.RemoveAll(b => b == null || b.panel == null);
			foreach (Button button in toolButtons)
			{
				button.SetEnabled(enabled);
			}
		}

		/// <summary>What each page is for, shown above its buttons.</summary>
		private static readonly Dictionary<string, string> ToolPageDescriptions = new Dictionary<string, string>
		{
			{ DashboardToolAttribute.Validate, "Checks that report problems in project assets. Most only log; the ones that change assets say so." },
			{ DashboardToolAttribute.UnitTests, "Runs the FishMMO.UnitTests EditMode assembly in this editor. Results go to the console." },
			{ DashboardToolAttribute.UITests, "UI Toolkit panel checks, renders and probes. Renders and reports are written to disk; see the console for where. Client editor only: these tools are absent under the Server build subtarget." },
			{ DashboardToolAttribute.Maintenance, "One-off wiring passes, mock content and test scene generators. Most of these write assets or scenes." },
			{ DashboardToolAttribute.AITools, "Repairs and migrations for NPC prefabs and AI assets." },
			{ DashboardToolAttribute.WorldSceneDetails, "The world scene details cache is rebuilt by every build; rebuild it here after changing a world scene's settings." },
			{ DashboardToolAttribute.Weather, "Weather content and textures. The generator creates layer templates and presets and fills every biome and climate weather profile nobody has authored; the baker writes the precipitation and noise textures." },
			{ DashboardToolAttribute.WorldMap, "World maps are build output. Client builds bake them, build, then remove them and rebuild the world scene details cache. A manual bake is for previewing; run Remove Baked Maps and rebuild World Scene Details before committing." },
		};

		/// <summary>Tools found by the last scan; rebuilt on every domain reload.</summary>
		private List<DashboardTool> toolCache;

		/// <summary>The embedded name generator, destroyed with the page.</summary>
		private FishMMO.Shared.NameGeneration.Editor.NameGeneratorWindow embeddedNameGenerator;

		/// <summary>Host for pages that need the whole right-hand area instead of the inspector.</summary>
		private VisualElement fullPageHost;

		/// <summary>
		/// Tools in assemblies this one references, which therefore cannot carry the attribute.
		/// </summary>
		private static IEnumerable<DashboardTool> BuiltInTools()
		{
			yield return new DashboardTool
			{
				Page = DashboardToolAttribute.WorldMap,
				Section = "Bake",
				Label = "Bake Maps",
				Order = 0,
				Tooltip = "Photographs every world scene, one at a time with nothing else loaded, and writes a map definition and image for each into the gitignored baked folder. Shows progress and can be cancelled; the dashboard's tools are locked until it finishes.",
				RunAsync = FishMMO.Shared.WorldMaps.WorldMapBaker.StartBake,
			};
			yield return new DashboardTool
			{
				Page = DashboardToolAttribute.WorldMap,
				Section = "Bake",
				Label = "Remove Baked Maps",
				Order = 1,
				Tooltip = "Deletes the baked definitions, their images and their addressable group.",
				Run = FishMMO.Shared.WorldMaps.WorldMapBaker.CleanBakedMaps,
			};
		}

		/// <summary>
		/// Every tool, attributed and built in, sorted by page, section, order and label.
		/// </summary>
		private List<DashboardTool> LoadTools()
		{
			if (toolCache != null)
			{
				return toolCache;
			}

			toolCache = new List<DashboardTool>(BuiltInTools());
			foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<DashboardToolAttribute>())
			{
				DashboardToolAttribute attribute = method.GetCustomAttribute<DashboardToolAttribute>();
				if (attribute == null)
				{
					continue;
				}
				if (!method.IsStatic || method.GetParameters().Length != 0)
				{
					Debug.LogWarning($"[FishMMODashboard] {method.DeclaringType?.Name}.{method.Name} is tagged DashboardTool but is not static and parameterless; skipped.");
					continue;
				}

				MethodInfo target = method;
				toolCache.Add(new DashboardTool
				{
					Page = attribute.Page,
					Section = string.IsNullOrEmpty(attribute.Section) ? "Tools" : attribute.Section,
					Label = attribute.Label,
					Tooltip = attribute.Tooltip,
					Confirm = attribute.Confirm,
					Order = attribute.Order,
					Run = () => target.Invoke(null, null),
				});
			}

			toolCache.Sort((a, b) =>
			{
				int cmp = string.CompareOrdinal(a.Page, b.Page);
				if (cmp != 0) return cmp;
				cmp = string.Compare(a.Section, b.Section, StringComparison.OrdinalIgnoreCase);
				if (cmp != 0) return cmp;
				cmp = a.Order.CompareTo(b.Order);
				return cmp != 0 ? cmp : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
			});
			return toolCache;
		}

		/// <summary>
		/// The tools on one page.
		/// </summary>
		private List<DashboardTool> ToolsFor(string page)
		{
			return LoadTools().FindAll(t => t.Page == page);
		}

		/// <summary>
		/// Adds a sidebar entry for a tool page.
		/// </summary>
		private void AddToolPageCategory(string page, string group)
		{
			categories.Add(new TemplateCategory
			{
				DisplayName = page,
				Group = group,
				IsSpecial = true,
			});
		}

		/// <summary>
		/// Adds the embedded name generator to the Naming group.
		/// </summary>
		private void AddNameGenerationCategory()
		{
			categories.Add(new TemplateCategory
			{
				DisplayName = NAME_GENERATION_CATEGORY,
				Group = "Naming",
				IsSpecial = true,
			});
		}

		/// <summary>
		/// True when a special category is a tool page.
		/// </summary>
		private static bool IsToolPage(string displayName)
		{
			return Array.IndexOf(DashboardToolAttribute.Pages, displayName) >= 0;
		}

		/// <summary>
		/// Shows a tool page on its own.
		/// </summary>
		private void ShowToolPage(string page)
		{
			ClearInspector();

			if (inspectorHeader != null)
			{
				inspectorHeader.text = page;
			}

			if (ToolPageDescriptions.TryGetValue(page, out string description))
			{
				AddToolNote(description);
			}

			if (!AddToolSections(page))
			{
				Label empty = new Label("No tools on this page.");
				empty.AddToClassList("empty-state-label");
				inspectorContent.Add(empty);
			}
		}

		/// <summary>
		/// Adds one section per heading, with a button per tool.
		/// </summary>
		/// <returns>False when the page has no tools.</returns>
		private bool AddToolSections(string page)
		{
			List<DashboardTool> tools = ToolsFor(page);
			VisualElement section = null;
			string sectionName = null;

			for (int i = 0; i < tools.Count; i++)
			{
				DashboardTool tool = tools[i];
				if (section == null || tool.Section != sectionName)
				{
					sectionName = tool.Section;
					section = CreateConstantsSection(sectionName);
					inspectorContent.Add(section);
				}

				Button button = new Button(() => RunTool(tool));
				button.text = tool.Label;
				button.tooltip = tool.Tooltip ?? string.Empty;
				button.style.height = 24;
				button.style.marginTop = 2;
				button.style.unityTextAlign = TextAnchor.MiddleLeft;
				button.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode && !AnyToolBusy);
				toolButtons.Add(button);
				section.Add(button);

				if (!string.IsNullOrEmpty(tool.Tooltip))
				{
					Label hint = new Label(tool.Tooltip);
					hint.AddToClassList("empty-state-label");
					hint.style.unityTextAlign = TextAnchor.MiddleLeft;
					hint.style.fontSize = 10;
					hint.style.marginLeft = 6;
					hint.style.marginBottom = 4;
					section.Add(hint);
				}
			}
			return tools.Count > 0;
		}

		/// <summary>
		/// Runs a tool, confirming first when it asks to, and reports the outcome.
		/// </summary>
		private void RunTool(DashboardTool tool)
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				SetStatus($"{tool.Label}: not available in play mode.");
				return;
			}
			if (AnyToolBusy)
			{
				// A click queued while the editor was busy must not start a second job.
				SetStatus($"{tool.Label}: wait for the running job to finish.");
				return;
			}
			if (!string.IsNullOrEmpty(tool.Confirm) &&
				!EditorUtility.DisplayDialog(tool.Label, tool.Confirm, "Run", "Cancel"))
			{
				SetStatus($"{tool.Label}: cancelled.");
				return;
			}

			SetStatus($"Running {tool.Label}…");
			try
			{
				if (tool.RunAsync != null)
				{
					tool.RunAsync(result =>
					{
						// The window may have been closed while the job ran.
						if (this == null)
						{
							return;
						}
						SetStatus($"{tool.Label}: {result}");
						RefreshToolButtons();
						ReloadCurrentCategory();
					});
					RefreshToolButtons();
					return;
				}
				tool.Run();
				SetStatus($"{tool.Label}: done. See the console for details.");
			}
			catch (Exception ex)
			{
				Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
				Debug.LogException(inner);
				SetStatus($"{tool.Label} failed: {inner.Message}");
			}

			// A tool may have created or removed assets the current page lists.
			ReloadCurrentCategory();
			if (selectedAssetIndex < 0 && selectedCategoryIndex >= 0 && selectedCategoryIndex < categories.Count &&
				!categories[selectedCategoryIndex].IsSpecial)
			{
				ShowCategoryLanding(categories[selectedCategoryIndex]);
			}
		}

		/// <summary>
		/// What an asset category shows before an asset is selected: its overview or its tools.
		/// </summary>
		private void ShowCategoryLanding(TemplateCategory cat)
		{
			if (cat.DisplayName == SPAWN_TABLES_CATEGORY)
			{
				ShowSpawnTablesOverview();
			}
			else if (ToolsFor(cat.DisplayName).Count > 0)
			{
				ShowToolPage(cat.DisplayName);
			}
		}

		/// <summary>
		/// An explanatory line at the top of a tool page.
		/// </summary>
		private void AddToolNote(string text)
		{
			Label note = new Label(text);
			note.AddToClassList("empty-state-label");
			note.style.unityTextAlign = TextAnchor.MiddleLeft;
			note.style.marginBottom = 6;
			inspectorContent.Add(note);
		}

		// ── Name Generation ─────────────────────────────────────────────────────────────

		/// <summary>
		/// Hosts the name generator in the space the entity list and inspector normally share.
		/// </summary>
		private void ShowNameGenerationPage()
		{
			ClearInspector();

			VisualElement inspectorPanel = rootVisualElement.Q<VisualElement>("inspector-panel");
			VisualElement inspectorScroll = rootVisualElement.Q<VisualElement>("inspector-scroll");
			if (inspectorPanel == null || inspectorScroll == null)
			{
				return;
			}

			if (inspectorHeader != null)
			{
				inspectorHeader.text = NAME_GENERATION_CATEGORY;
			}

			SetEntityPanelVisible(false);
			inspectorScroll.style.display = DisplayStyle.None;

			fullPageHost = new VisualElement();
			fullPageHost.style.flexGrow = 1;
			inspectorPanel.Add(fullPageHost);

			embeddedNameGenerator = CreateInstance<FishMMO.Shared.NameGeneration.Editor.NameGeneratorWindow>();
			embeddedNameGenerator.hideFlags = HideFlags.HideAndDontSave;
			embeddedNameGenerator.BuildUI(fullPageHost);
		}

		/// <summary>
		/// Removes a full-width page and gives the inspector and entity list their space back.
		/// </summary>
		private void ClearFullPage()
		{
			if (fullPageHost != null)
			{
				fullPageHost.RemoveFromHierarchy();
				fullPageHost = null;

				VisualElement inspectorScroll = rootVisualElement.Q<VisualElement>("inspector-scroll");
				if (inspectorScroll != null)
				{
					inspectorScroll.style.display = DisplayStyle.Flex;
				}
				SetEntityPanelVisible(true);
			}

			if (embeddedNameGenerator != null)
			{
				DestroyImmediate(embeddedNameGenerator);
				embeddedNameGenerator = null;
			}
		}

		/// <summary>
		/// Shows or hides the entity list and the resizer beside it.
		/// </summary>
		private void SetEntityPanelVisible(bool visible)
		{
			DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			if (entityPanel != null)
			{
				entityPanel.style.display = display;
			}
			VisualElement resizer = rootVisualElement.Q<VisualElement>("right-resizer");
			if (resizer != null)
			{
				resizer.style.display = display;
			}
		}
	}
}
#endif
