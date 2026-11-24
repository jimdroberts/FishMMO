using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

using GameClient = FishMMO.Client.Client;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness for issue #280: mounts the inventory, the drag ghost and the shared
	/// tooltip the way the scene does, then plays hover, pick up, drop and the server's echo of
	/// the swap, reporting what the tooltip says after each step.
	/// </summary>
	public static class TooltipSwapProbe
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string GUI = "Assets/FishMMO/Client/GUI/";
		private static readonly string OUT_DIR = ProbeOutput.Path("TooltipSwapProbe");
		private const int WIDTH = 1200;
		private const int HEIGHT = 675;
		private const int SETTLE_FRAMES = 20;

		private static readonly List<(string Name, UITKControl Panel, UIDocument Document)> mounts =
			new List<(string, UITKControl, UIDocument)>();
		private static readonly StringBuilder report = new StringBuilder();

		private static PanelSettings settings;
		private static RenderTexture texture;
		private static GameObject characterHost;
		private static GameClient client;
		private static IInventoryController inventory;
		private static int step;
		private static int framesWaited;
		private static int slotA = -1;
		private static int slotB = -1;
		private static Item itemA;
		private static Item itemB;

		[DashboardTool(DashboardToolAttribute.UITests, "Probe Tooltip Swap", Section = "Probes", Order = 4)]
		public static void Run()
		{
			try
			{
				Directory.CreateDirectory(OUT_DIR);
				Seed.All();

				texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
				texture.Create();
				settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;

				characterHost = new GameObject("ProbeCharacter") { hideFlags = HideFlags.HideAndDontSave };
				Rig.Build(characterHost);
				Rig.Character.TryGet(out inventory);

				GameObject clientHost = new GameObject("ProbeClient") { hideFlags = HideFlags.HideAndDontSave };
				client = clientHost.AddComponent<GameClient>();

				MountPanel("UIInventory", ItemsRenderScratch.InventoryUxml, typeof(UITKInventory));
				MountPanel("UIDragObject", GUI + "Shared/DragObject/UIDragObject.uxml", typeof(UITKDragObject));
				MountPanel("UITooltip", GUI + "Shared/Tooltip/UITooltip.uxml", typeof(UITKTooltip));

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[TooltipSwapProbe] setup failed: {ex}");
				EditorAutomation.Finish(1);
			}
		}

		private static void MountPanel(string name, string uxml, Type panelType)
		{
			GameObject host = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
			host.SetActive(false);

			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);
			if (document.visualTreeAsset == null)
			{
				throw new InvalidOperationException($"{uxml} did not load");
			}

			UITKControl panel = host.AddComponent(panelType) as UITKControl;
			panel.StartOpen = false;
			panel.Document = document;
			host.SetActive(true);

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
			awake?.Invoke(panel, null);

			Rig.Set(panel, "Client", client);
			if (panel is UITKCharacterControl characterPanel)
			{
				characterPanel.SetCharacter(Rig.Character);
			}
			mounts.Add((name, panel, document));
			Line($"mounted {name}: in manager = {UIManager.TryGetTK(name, out UITKControl found) && ReferenceEquals(found, panel)}");
		}

		private static void Pump()
		{
			try
			{
				if (framesWaited < SETTLE_FRAMES)
				{
					++framesWaited;
					foreach (var m in mounts)
					{
						m.Document?.rootVisualElement?.MarkDirtyRepaint();
					}
					return;
				}
				framesWaited = 0;

				switch (step++)
				{
					case 0:
						Find("UIInventory")?.Show();
						return;
					case 1:
						PickSlots();
						return;
					case 2:
						Enter(slotA);
						Report($"pointer enters slot {slotA}");
						return;
					case 3:
						Line($"press slot {slotA}: {Press(Slot(slotA)) ?? "ok"}");
						Report($"after picking up slot {slotA}");
						return;
					case 4:
						Leave(slotA);
						Enter(slotB);
						Report($"pointer moved to slot {slotB}");
						return;
					case 5:
						Line($"press slot {slotB}: {Press(Slot(slotB)) ?? "ok"}");
						Report($"after dropping on slot {slotB} (request sent, no reply yet)");
						return;
					case 6:
						/* The server's echo, as ItemContainer.SwapItemSlots raises it: from first, then to. */
						inventory.SetItemSlot(itemB, slotA);
						inventory.SetItemSlot(itemA, slotB);
						Report($"right after the swap echo (slot {slotB} now holds '{Name(itemA)}')");
						return;
					case 7:
						Report("after settling");
						WriteReport();
						Release();
						EditorAutomation.Finish(0);
						return;
				}

				EditorApplication.update -= Pump;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Line($"pump failed: {ex}");
				try { WriteReport(); } catch { }
				Release();
				EditorAutomation.Finish(1);
			}
		}

		private static void PickSlots()
		{
			for (int i = 0; i < inventory.Items.Count; ++i)
			{
				Item item = inventory.Items[i];
				if (item == null || item.Template == null)
				{
					continue;
				}
				if (slotA < 0)
				{
					slotA = i;
					itemA = item;
				}
				else if (item.Template != itemA.Template)
				{
					slotB = i;
					itemB = item;
					break;
				}
			}
			Line($"slot A = {slotA} '{Name(itemA)}', slot B = {slotB} '{Name(itemB)}'");
			if (slotA < 0 || slotB < 0)
			{
				throw new InvalidOperationException("the fixtures do not hold two different items");
			}
		}

		private static string Name(Item item) => item?.Template != null ? item.Template.Name : "null";

		private static UITKControl Find(string name)
		{
			foreach (var m in mounts)
			{
				if (m.Name == name) { return m.Panel; }
			}
			return null;
		}

		private static VisualElement Slot(int index)
		{
			object views = Get(Find("UIInventory"), "slotViews");
			if (views is System.Collections.IList list && index >= 0 && index < list.Count)
			{
				return Get(list[index], "Root") as VisualElement;
			}
			return null;
		}

		private static void Enter(int index)
		{
			VisualElement slot = Slot(index);
			using (PointerEnterEvent evt = new PointerEnterEvent())
			{
				evt.target = slot;
				slot.SendEvent(evt);
			}
		}

		private static void Leave(int index)
		{
			VisualElement slot = Slot(index);
			using (PointerLeaveEvent evt = new PointerLeaveEvent())
			{
				evt.target = slot;
				slot.SendEvent(evt);
			}
		}

		private static string Press(VisualElement element)
		{
			try
			{
				using (PointerDownEvent evt = new PointerDownEvent())
				{
					Rig.Set(evt, "button", 0);
					evt.target = element;
					element.SendEvent(evt);
				}
				return null;
			}
			catch (Exception ex)
			{
				return $"{ex.GetType().Name}: {ex.Message}";
			}
		}

		private static void Report(string caption)
		{
			UITKTooltip tooltip = Find("UITooltip") as UITKTooltip;
			VisualElement owner = Get(tooltip, "owner") as VisualElement;
			VisualElement root = tooltip?.Document?.rootVisualElement;
			Label title = root?.Q("tooltip-content")?.Q<Label>(className: "tip-title");
			UITKDragObject drag = Find("UIDragObject") as UITKDragObject;

			int ownerIndex = -1;
			for (int i = 0; i < inventory.Items.Count; ++i)
			{
				if (ReferenceEquals(Slot(i), owner)) { ownerIndex = i; break; }
			}

			Line($"── {caption}");
			Line($"   tooltip visible={tooltip?.Visible} owner slot={ownerIndex} title='{title?.text ?? "<none>"}'");
			Line($"   drag in flight={drag?.IsDragging} slot {slotA}='{Name(inventory.Items[slotA])}' slot {slotB}='{Name(inventory.Items[slotB])}'");
		}

		private static object Get(object target, string member)
		{
			if (target == null) { return null; }
			for (Type t = target.GetType(); t != null; t = t.BaseType)
			{
				PropertyInfo prop = t.GetProperty(member,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				if (prop != null) { return prop.GetValue(target); }
				FieldInfo field = t.GetField(member,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				if (field != null) { return field.GetValue(target); }
			}
			return null;
		}

		private static void Line(string text) => report.AppendLine(text);

		private static void WriteReport()
		{
			File.WriteAllText(Path.Combine(OUT_DIR, "tooltip-swap-probe.txt"), report.ToString());
			Debug.Log("[TooltipSwapProbe]\n" + report);
		}

		private static void Release()
		{
			foreach (var m in mounts)
			{
				if (m.Panel != null)
				{
					try { UnityEngine.Object.DestroyImmediate(m.Panel.gameObject); } catch { }
				}
			}
			mounts.Clear();
			if (characterHost != null) { UnityEngine.Object.DestroyImmediate(characterHost); }
			if (settings != null) { UnityEngine.Object.DestroyImmediate(settings); }
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.DestroyImmediate(texture);
			}
		}
	}
}
