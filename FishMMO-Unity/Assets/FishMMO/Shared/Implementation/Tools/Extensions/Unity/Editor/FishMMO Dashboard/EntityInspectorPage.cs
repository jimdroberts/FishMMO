#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FishMMO.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>The dashboard's Entity Inspector entry, in the Core group.</summary>
	internal sealed class EntityInspectorDashboardCategory : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 1;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add(new TemplateCategory
			{
				DisplayName = "Entity Inspector",
				Group = "Core",
				IsSpecial = true,
				BuildPage = EntityInspectorPage.Build,
			});
		}
	}

	/// <summary>
	/// Shows the Core worlds running in this editor (a server started in Play mode): their modules, what each
	/// system costs, how many of each component exist, the archetypes, and any entity's components field by
	/// field. Read-only, and refreshed while it is on screen.
	/// </summary>
	internal static class EntityInspectorPage
	{
		private const int RefreshMilliseconds = 500;
		private const int MaxListedEntities = 200;

		private static string selectedWorld;
		private static Entity selectedEntity;
		private static string filter = string.Empty;
		private static readonly Dictionary<string, bool> folded = new Dictionary<string, bool>();
		private static Font monospace;
		private static bool monospaceLooked;

		public static void Build(VisualElement root)
		{
			var note = new Label("Core worlds running in this editor: start a server in Play mode and its world appears here. Read-only.");
			note.style.whiteSpace = WhiteSpace.Normal;
			note.style.marginBottom = 6;
			root.Add(note);

			var filterField = new TextField("Filter entities by component") { value = filter };
			filterField.RegisterValueChangedCallback(e => filter = e.newValue ?? string.Empty);
			root.Add(filterField);

			var body = new VisualElement();
			root.Add(body);
			Refresh(body);
			body.schedule.Execute(() => Refresh(body)).Every(RefreshMilliseconds);
		}

		private static void Refresh(VisualElement body)
		{
			body.Clear();
			List<LiveWorld> worlds = LiveWorlds.All;
			if (worlds.Count == 0)
			{
				body.Add(Muted(Application.isPlaying
					? "No Core world is running. A server starts one the first time something needs it (the first NPC corpse, for now)."
					: "Not in Play mode."));
				return;
			}

			LiveWorld live = worlds.Find(w => w.Name == selectedWorld) ?? worlds[0];
			selectedWorld = live.Name;
			if (worlds.Count > 1)
			{
				var picker = new VisualElement();
				picker.style.flexDirection = FlexDirection.Row;
				picker.style.flexWrap = Wrap.Wrap;
				foreach (LiveWorld w in worlds)
				{
					string name = w.Name;
					var button = new Button(() => { selectedWorld = name; selectedEntity = Entity.Null; Refresh(body); }) { text = name };
					button.SetEnabled(w != live);
					picker.Add(button);
				}
				body.Add(picker);
			}

			World world = live.World;
			body.Add(Heading($"{live.Name}: {world.EntityCount} entities"));
			AddModules(body, world);
			AddSystems(body, live);
			Dictionary<Entity, List<IComponentStore>> byEntity = AddComponents(body, world);
			AddArchetypes(body, world);
			AddEntities(body, world, byEntity);
			AddSelected(body, world, byEntity);
		}

		private static void AddModules(VisualElement body, World world)
		{
			Foldout section = Section(body, "Modules");
			if (!world.TryGetResource(out ModuleInstall install))
			{
				section.Add(Muted("This world was not built from modules."));
				return;
			}
			foreach (GameModule module in install.Modules)
			{
				IReadOnlyList<string> systems = install.SystemsOf(module.Id);
				section.Add(Mono($"{module.Id} v{module.Version}" + (systems.Count > 0 ? $"   systems: {string.Join(", ", systems)}" : string.Empty)));
			}
			foreach (string problem in install.Problems)
			{
				Label label = Mono(problem);
				label.style.color = new Color(0.95f, 0.7f, 0.3f);
				label.style.whiteSpace = WhiteSpace.Normal;
				section.Add(label);
			}
		}

		private static void AddSystems(VisualElement body, LiveWorld live)
		{
			Foldout section = Section(body, "Systems (last / average, microseconds)");
			if (live.Scheduler == null)
			{
				return;
			}
			foreach (GameSystem system in live.Scheduler.Systems)
			{
				(double last, double average, long runs) = live.Timings != null ? live.Timings.Of(system) : (0d, 0d, 0L);
				section.Add(Mono($"{system.Phase,-12} {system.Order,5}  {system.Name,-34} {last,8:F1} {average,8:F1}   x{runs}"));
			}
		}

		private static Dictionary<Entity, List<IComponentStore>> AddComponents(VisualElement body, World world)
		{
			Foldout section = Section(body, "Components");
			var byEntity = new Dictionary<Entity, List<IComponentStore>>();
			foreach (IComponentStore store in world.Stores)
			{
				if (store.Count == 0)
				{
					continue;
				}
				section.Add(Mono($"{store.ComponentType.Name,-28} {store.Count,7}"));
				for (int i = 0; i < store.Count; i++)
				{
					Entity entity = store.EntityAt(i);
					if (!byEntity.TryGetValue(entity, out List<IComponentStore> list))
					{
						list = new List<IComponentStore>(4);
						byEntity[entity] = list;
					}
					list.Add(store);
				}
			}
			if (section.childCount == 0)
			{
				section.Add(Muted("No components yet."));
			}
			return byEntity;
		}

		private static void AddArchetypes(VisualElement body, World world)
		{
			if (!world.TryGetResource(out Archetypes archetypes))
			{
				return;
			}
			Foldout section = Section(body, "Archetypes", openByDefault: false);
			foreach (string name in archetypes.Names)
			{
				IReadOnlyList<(string Module, string Description)> parts = archetypes.PartsOf(name);
				var text = new StringBuilder(name);
				if (parts.Count == 0)
				{
					text.Append(": nothing yet");
				}
				foreach ((string module, string description) in parts)
				{
					text.Append($"\n    {description}  ({module})");
				}
				section.Add(Mono(text.ToString()));
			}
		}

		private static void AddEntities(VisualElement body, World world, Dictionary<Entity, List<IComponentStore>> byEntity)
		{
			Foldout section = Section(body, "Entities");
			int listed = 0;
			int matched = 0;
			foreach (KeyValuePair<Entity, List<IComponentStore>> pair in byEntity)
			{
				string names = Names(pair.Value);
				if (filter.Length > 0 && names.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				matched++;
				if (listed >= MaxListedEntities)
				{
					continue;
				}
				listed++;
				Entity entity = pair.Key;
				var button = new Button(() => selectedEntity = entity) { text = $"{entity}   {names}" };
				button.style.unityTextAlign = TextAnchor.MiddleLeft;
				if (entity == selectedEntity)
				{
					button.style.unityFontStyleAndWeight = FontStyle.Bold;
				}
				section.Add(button);
			}
			if (matched > listed)
			{
				section.Add(Muted($"…and {matched - listed} more; narrow the filter."));
			}
			if (matched == 0)
			{
				section.Add(Muted(byEntity.Count == 0 ? "No entity has a component yet." : "No entity matches the filter."));
			}
		}

		private static void AddSelected(VisualElement body, World world, Dictionary<Entity, List<IComponentStore>> byEntity)
		{
			if (selectedEntity.IsNull)
			{
				return;
			}
			Foldout section = Section(body, $"Selected: {selectedEntity}", "Selected");
			if (!world.IsAlive(selectedEntity) || !byEntity.TryGetValue(selectedEntity, out List<IComponentStore> stores))
			{
				section.Add(Muted("Gone: destroyed, or it has no components left."));
				return;
			}
			foreach (IComponentStore store in stores)
			{
				section.Add(Heading(store.ComponentType.Name));
				if (store.ComponentType == typeof(Stats) && world.TryStatsOf(selectedEntity, out StatTable table))
				{
					for (int i = 0; i < table.Count; i++)
					{
						StatEntry entry = table.At(i);
						section.Add(Mono(entry.IsResource
							? $"  {entry.Id,-28} {entry.Current,10:F2} / {entry.Value,-10:F2} (base {entry.Base:F2})"
							: $"  {entry.Id,-28} {entry.Value,10:F2}   (base {entry.Base:F2})"));
					}
					continue;
				}
				for (int i = 0; i < store.Count; i++)
				{
					if (store.EntityAt(i) != selectedEntity)
					{
						continue;
					}
					object component = store.GetBoxed(i);
					FieldInfo[] fields = store.ComponentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (fields.Length == 0)
					{
						section.Add(Muted("  (no fields)"));
					}
					foreach (FieldInfo field in fields)
					{
						section.Add(Mono($"  {field.Name,-20} {Describe(field.GetValue(component))}"));
					}
					break;
				}
			}
		}

		private static string Describe(object value)
		{
			switch (value)
			{
				case null:
					return "null";
				case UnityEngine.Object unityObject:
					return unityObject != null ? $"{unityObject.name} ({unityObject.GetType().Name})" : "missing";
				case Array array:
					var text = new StringBuilder($"{array.GetType().GetElementType()?.Name}[{array.Length}]");
					for (int i = 0; i < array.Length && i < 8; i++)
					{
						text.Append(i == 0 ? " " : ", ").Append(array.GetValue(i));
					}
					return array.Length > 8 ? text.Append(", …").ToString() : text.ToString();
				default:
					return value.ToString();
			}
		}

		private static string Names(List<IComponentStore> stores)
		{
			var text = new StringBuilder();
			for (int i = 0; i < stores.Count; i++)
			{
				text.Append(i == 0 ? string.Empty : ", ").Append(stores[i].ComponentType.Name);
			}
			return text.ToString();
		}

		/// <summary>A section whose open or closed state survives the refresh; <paramref name="key"/> names it across refreshes.</summary>
		private static Foldout Section(VisualElement body, string title, string key = null, bool openByDefault = true)
		{
			key ??= title;
			var foldout = new Foldout { text = title, value = !folded.TryGetValue(key, out bool closed) ? openByDefault : !closed };
			foldout.RegisterValueChangedCallback(e =>
			{
				if (e.target == foldout)
				{
					folded[key] = !e.newValue;
				}
			});
			foldout.style.marginTop = 6;
			body.Add(foldout);
			return foldout;
		}

		private static Label Heading(string text)
		{
			var label = new Label(text);
			label.style.unityFontStyleAndWeight = FontStyle.Bold;
			label.style.marginTop = 4;
			return label;
		}

		private static Label Mono(string text)
		{
			var label = new Label(text);
			if (!monospaceLooked)
			{
				monospaceLooked = true;
				monospace = UnityEditor.EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;
			}
			if (monospace != null)
			{
				label.style.unityFontDefinition = new StyleFontDefinition(monospace);
			}
			label.style.whiteSpace = WhiteSpace.Pre;
			return label;
		}

		private static Label Muted(string text)
		{
			var label = new Label(text);
			label.style.opacity = 0.7f;
			label.style.whiteSpace = WhiteSpace.Normal;
			return label;
		}
	}
}
#endif
