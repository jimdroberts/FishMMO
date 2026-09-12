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

/* Inside FishMMO.*, the bare name Client is the FishMMO.Client NAMESPACE, not the component in
 * it. The panels only touch it through SetClient and the field Awake fills, both of which type it
 * as the component, so it is aliased rather than qualified at every use. */
using GameClient = FishMMO.Client.Client;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: mounts the bank, inventory and equipment panels the way the scene does,
	/// opens them the way the player does, and reports who receives a click at an equipment socket.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Issue #268. Every panel in this project is its own <c>UIDocument</c> over one shared
	/// <c>PanelSettings</c>, so they are not panels at all — they are sibling subtrees of one
	/// UI Toolkit panel, and <c>UIDocument.sortingOrder</c> is what orders those siblings. Both
	/// rendering and picking follow it, so the sibling that is behind is not merely covered: a
	/// click at a point where it has content goes to whichever sibling is in front there.
	/// </para>
	/// <para>
	/// So the measurement is two questions, and the first one is not the one it looks like:
	/// which sibling subtree actually owns the element that gets picked at a socket's centre, and
	/// what that element is. <c>Panel.Pick</c> is global to the shared panel, which is why the
	/// winner is resolved by walking the picked element up to the document root that contains it
	/// rather than by asking each panel in turn.
	/// </para>
	/// <para>
	/// The rig's <c>FakeEquipment.RequestEquip</c> returns false, so this observes reach, not the
	/// outcome of an equip — the outcome is pinned by <c>PredictedEquipmentTests</c>.
	/// </para>
	/// </remarks>
	public static class EquipDropProbe
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string GUI = "Assets/Scripts/Client/GUI/";
		private const string OUT_DIR = "/home/jim/Dev/FishMMO-Dev/EquipProbe";
		private const string ITEM_TEMPLATE_DIR = "Assets/Templates/Entity/Items";

		/* Panel space. PanelSettings is ScaleWithScreenSize against 1200x800 with match = WIDTH, so
		 * the panel is always exactly 1200 units wide and its height is the screen's aspect. 675 is
		 * 16:9. */
		private const int WIDTH = 1200;
		private const int HEIGHT = 675;
		private const int SETTLE_FRAMES = 40;
		private const int BANK_SLOT = 4;

		private static readonly string[] Sockets =
		{
			"slot-head", "slot-chest", "slot-legs", "slot-feet",
			"slot-back", "slot-accessory",
			"slot-shoulders", "slot-hands", "slot-mainhand", "slot-offhand",
		};

		private sealed class Mount
		{
			public string Name;
			public UITKControl Panel;
			public UIDocument Document;
			public VisualElement AuthoredRoot;
			public int RegisteredAt;
			public bool HasClient;
			public bool InManager;

			public VisualElement Root => Document?.rootVisualElement;
			public IPanel SharedPanel => Document?.rootVisualElement?.panel;
			/// <summary>
			/// The document's own sorting order. There is no <c>VisualElement</c> counterpart: the
			/// documents are ordered against each other by the shared panel, not by their roots.
			/// </summary>
			public float DocumentOrder => Document != null ? Document.sortingOrder : float.NaN;

			/// <summary>The panel's layer, as the control reports it.</summary>
			public object Layer => Get(Panel, "Layer");

			public string Describe(VisualElement element)
			{
				if (element == null) { return "nothing"; }
				if (ReferenceEquals(element, Root)) { return "<document root>"; }
				string name = string.IsNullOrEmpty(element.name) ? "(unnamed)" : element.name;
				return $"{element.GetType().Name} name={name} classes=[{string.Join(" ", element.GetClasses())}]";
			}
		}

		private static readonly List<Mount> mounts = new List<Mount>();
		private static readonly StringBuilder report = new StringBuilder();

		private static PanelSettings settings;
		private static RenderTexture texture;
		private static GameObject characterHost;
		private static GameClient client;
		private static UITKDragObject drag;
		private static IBankController bank;
		private static IInventoryController inventory;
		private static IEquipmentController equipment;

		private static int step;
		private static int framesWaited;
		private static int markerHits;

		[MenuItem("FishMMO/UI Toolkit/Probe Bank Equip Drop")]
		public static void Run()
		{
			try
			{
				Directory.CreateDirectory(OUT_DIR);
				Seed.All();

				texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
				texture.Create();

				settings = UnityEngine.Object.Instantiate(
					AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);

				characterHost = new GameObject("ProbeCharacter") { hideFlags = HideFlags.HideAndDontSave };
				Rig.Build(characterHost);

				/* The slot handlers refuse to run without one, and every send site beyond them
				 * reaches for it. Nothing here needs it to work. */
				GameObject clientHost = new GameObject("ProbeClient") { hideFlags = HideFlags.HideAndDontSave };
				client = clientHost.AddComponent<GameClient>();

				/* Scene order, from ClientWorldGUI: UIInventory, UIEquipment, UIBank. It decides
				 * the order of the sibling subtrees that share a sorting order. */
				MountPanel("UIInventory", GUI + "World/Inventory/UIInventory.uxml", typeof(UITKInventory));
				MountPanel("UIEquipment", GUI + "World/Equipment/UIEquipment.uxml", typeof(UITKEquipment));
				MountPanel("UIBank", GUI + "World/Bank/UIBank.uxml", typeof(UITKBank));
				MountPanel("UIDragObject", GUI + "Shared/DragObject/UIDragObject.uxml", typeof(UITKDragObject));

				report.Clear();
				Line($"panel space {WIDTH}x{HEIGHT} units");
				Line($"registered in order: {string.Join(", ", mounts.ConvertAll(m => m.Name))}");

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[EquipDropProbe] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		/// <summary>
		/// Adds one panel the way the scene does: configured while inert, then activated, so
		/// <c>Awake</c> registers it and applies <c>StartOpen</c> exactly as it does in play.
		/// </summary>
		private static void MountPanel(string name, string uxml, Type panelType)
		{
			GameObject host = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };

			/* Inert until it is wired: Awake registers the panel, stamps its sorting order and
			 * applies StartOpen, and none of that may run against a half-configured control. All
			 * three windows ship with StartOpen = 0; the field's own default is true. */
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

			/* In edit mode Unity does not call Awake on a component it did not already run — no
			 * player loop, no MonoBehaviour startup — so nothing has registered the panel, stamped
			 * its sorting order or applied StartOpen. In play all four of those happen at scene
			 * load, and the state they produce is the state the reported bug is about, so Awake is
			 * invoked here rather than approximated. */
			try
			{
				MethodInfo awake = typeof(UITKControl).GetMethod("Awake",
					BindingFlags.NonPublic | BindingFlags.Instance);
				awake?.Invoke(panel, null);
			}
			catch (Exception ex)
			{
				Line($"{name}: invoking Awake raised {ex.GetType().Name}: {ex.Message}");
			}

			Mount mount = new Mount
			{
				Name = name,
				Panel = panel,
				Document = document,
				AuthoredRoot = document.rootVisualElement?.Q("panel-root"),
				RegisteredAt = mounts.Count,
			};
			mounts.Add(mount);

			/* Awake injected the manager's client, which is nothing outside a session — so the
			 * panels are left with a null one and every slot handler returns on sight. OnClientSet
			 * is not caught inside SetClient and it registers broadcasts on a network manager that
			 * is not there, so the field is written directly instead. */
			Rig.Set(panel, "Client", client);
			mount.HasClient = panel.Client != null;
			mount.InManager = UIManager.TryGetTK(name, out UITKControl found) && ReferenceEquals(found, panel);

			if (panel is UITKCharacterControl characterPanel)
			{
				characterPanel.SetCharacter(Rig.Character);
			}
			else if (panel is UITKDragObject dragPanel)
			{
				drag = dragPanel;
			}
		}

		/// <summary>
		/// Runs the phases in order, one settled step at a time: layout is resolved lazily and
		/// <c>worldBound</c> reads zero until a panel update has run.
		/// </summary>
		private static void Pump()
		{
			try
			{
				if (framesWaited < SETTLE_FRAMES)
				{
					++framesWaited;
					foreach (Mount m in mounts)
					{
						m.Root?.MarkDirtyRepaint();
					}
					return;
				}
				framesWaited = 0;

				switch (step++)
				{
					case 0:
						/* The player opens the character sheet. */
						Find("UIEquipment")?.Panel.Show();
						return;

					case 1:
						Measure("the character sheet on its own");
						return;

					case 2:
						/* Then the banker interaction. The bank opens the inventory alongside itself
						 * before it raises itself — the step this is about. */
						Find("UIBank")?.Panel.Show();
						return;

					case 3:
						Measure("after the reported open sequence (sheet, then bank)");
						return;

					case 4:
						/* A control, not a step the client takes: ShowInventoryIfClosed has already
						 * run by now. Raising the inventory again changes nothing if the two tables
						 * above agree, and isolates the raise as the cause if they do not. */
						Find("UIInventory")?.Panel.Show();
						return;

					case 5:
						Measure("after raising the inventory a second time");
						return;

					case 6:
						DriveTheDrop();
						Capture();
						WriteReport();
						Release();
						EditorApplication.Exit(0);
						return;
				}

				EditorApplication.update -= Pump;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[EquipDropProbe] pump failed: {ex}");
				try { WriteReport(); } catch { }
				Release();
				EditorApplication.Exit(1);
			}
		}

		private static bool CharacterReady()
		{
			return Rig.Character != null &&
				Rig.Character.TryGet(out bank) &&
				Rig.Character.TryGet(out inventory) &&
				Rig.Character.TryGet(out equipment);
		}

		private static Mount Find(string name)
		{
			for (int i = 0; i < mounts.Count; ++i)
			{
				if (mounts[i].Name == name) { return mounts[i]; }
			}
			return null;
		}

		// ── measurement ─────────────────────────────────────────────

		private static void Measure(string caption)
		{
			report.AppendLine();
			Line($"══ {caption} ══");
			Line($"{"panel",-14} {"order",5} {"layer",8} {"visible",7} {"doc",5} {"mgr",5} {"client",6}  rect");

			foreach (Mount m in mounts)
			{
				Line($"{m.Name,-14} {m.DocumentOrder,5:0} {m.Layer,8} {m.Panel.Visible,7} " +
					$"{m.Document.enabled,5} {m.InManager,5} {m.HasClient,6}  {Rect(m.AuthoredRoot)}");
			}

			/* One shared panel or four? Everything below depends on which it is. */
			HashSet<IPanel> panels = new HashSet<IPanel>();
			foreach (Mount m in mounts)
			{
				if (m.SharedPanel != null) { panels.Add(m.SharedPanel); }
			}
			Line($"distinct UI Toolkit panels across the {mounts.Count} documents: {panels.Count}");

			Mount sheet = Find("UIEquipment");
			Mount bag = Find("UIInventory");

			report.AppendLine();
			Line("══ where a click at each socket centre lands ════════════");
			Line($"{"socket",-16} {"centre",-12} {"(x, y, w, h)",-26} {"over bag",-9} owner  picked element");

			foreach (string socket in Sockets)
			{
				VisualElement element = sheet?.Root?.Q(socket);
				if (element == null)
				{
					Line($"{socket,-16} MISSING FROM THE TREE");
					continue;
				}

				Rect rect = element.worldBound;
				Vector2 centre = rect.center;
				bool overBag = bag?.AuthoredRoot != null && bag.AuthoredRoot.worldBound.Overlaps(rect);

				VisualElement picked = PickGlobal(centre);
				Mount owner = OwnerOf(picked);

				Line($"{socket,-16} ({centre.x,4:0},{centre.y,4:0}) {Rect(element),-26} {(overBag ? "yes" : "no"),-9} " +
					$"{owner?.Name ?? "none",-6} {owner?.Describe(picked) ?? "nothing pickable"}");
			}
		}

		/// <summary>The topmost pickable element at a point, across every document.</summary>
		/// <remarks>
		/// One pick, not one per panel: the documents are sibling subtrees of a single shared
		/// panel, so a per-document pick would return the same element every time.
		/// </remarks>
		private static VisualElement PickGlobal(Vector2 point)
		{
			IPanel panel = null;
			foreach (Mount m in mounts)
			{
				if (m.SharedPanel != null) { panel = m.SharedPanel; break; }
			}
			if (panel == null) { return null; }

			VisualElement picked = panel.Pick(point);
			return ReferenceEquals(picked, panel.visualTree) ? null : picked;
		}

		/// <summary>Which document's subtree a picked element belongs to.</summary>
		private static Mount OwnerOf(VisualElement element)
		{
			for (VisualElement e = element; e != null; e = e.parent)
			{
				foreach (Mount m in mounts)
				{
					if (ReferenceEquals(e, m.Root)) { return m; }
				}
			}
			return null;
		}

		private static string Rect(VisualElement element)
		{
			if (element == null) { return "<none>"; }
			Rect r = element.worldBound;
			return $"({r.x:0}, {r.y:0}, {r.width:0}, {r.height:0})";
		}

		// ── the drop ────────────────────────────────────────────────

		/// <summary>
		/// Arms a drag on a bank slot, then presses the primary-hand socket, dispatching to
		/// whichever element the shared panel actually picks there.
		/// </summary>
		private static void DriveTheDrop()
		{
			report.AppendLine();
			Line("══ the reported attempt: bank slot → primary-hand socket ══");

			if (!CharacterReady())
			{
				Line("the rig produced no controllers");
				return;
			}

			EquippableItemTemplate wearable = FindPrimaryHandTemplate();
			if (wearable == null)
			{
				Line($"no ItemSlot.Primary template under {ITEM_TEMPLATE_DIR}");
				return;
			}

			for (int i = 0; i < bank.Items.Count; ++i) { bank.Items[i] = null; }

			Item sword = new Item(9001, 0, wearable, 1);
			bank.SetItemSlot(sword, BANK_SLOT);
			Line($"bank slot {BANK_SLOT} = {wearable.name} (id {sword.ID}, socket {wearable.Slot})");

			VisualElement grid = Find("UIBank")?.Root?.Q("slot-grid");
			if (grid == null || grid.childCount <= BANK_SLOT)
			{
				Line($"the bank grid is not built: {(grid == null ? "no slot-grid" : grid.childCount + " slots")}");
				return;
			}

			VisualElement bankSlot = grid[BANK_SLOT];
			Line($"bank slot element {Rect(bankSlot)}, parent is the live grid: {ReferenceEquals(bankSlot.parent, grid)}, " +
				$"grid holds {grid.childCount}");

			/* A marker on the very element about to be pressed. If this does not fire, the press
			 * never reached the element and nothing downstream can be read from it. */
			markerHits = 0;
			bankSlot.RegisterCallback<PointerDownEvent>(_ => ++markerHits);

			string armed = Press(bankSlot);
			Line($"press on the bank slot{(armed == null ? " ok" : " raised " + armed)}: marker fired {markerHits}x, " +
				$"dragging={drag?.IsDragging} type={drag?.Type} reference={drag?.ReferenceID} itemID={drag?.ItemID}");

			/* Both gestures the report names end here: a press on a socket, whether the drag was
			 * started by dragging off the bank slot or by clicking it first. */
			TrySocket("slot-mainhand");
			TrySocket("slot-accessory");

			Line($"primary-hand socket holds: {Held(equipment, (int)ItemSlot.Primary)}");
			Line($"accessory socket holds: {Held(equipment, (int)ItemSlot.Accessory)}");
			Line($"bank slot {BANK_SLOT} holds: {Held(bank, BANK_SLOT)}");
			Line($"inventory holds {Filled(inventory)} items; first {First(inventory)}");
			Line($"pending marks: bank={Pending(ReferenceButtonType.Bank)} " +
				$"inventory={Pending(ReferenceButtonType.Inventory)} " +
				$"equipment={Pending(ReferenceButtonType.Equipment)}");
		}

		/// <summary>
		/// Presses one equipment socket, dispatching to whatever the shared panel actually picks at
		/// its centre and reporting where that press went.
		/// </summary>
		private static void TrySocket(string socketName)
		{
			report.AppendLine();
			Line($"-- press on {socketName} --");

			VisualElement socket = Find("UIEquipment")?.Root?.Q(socketName);
			if (socket == null)
			{
				Line("  missing from the tree");
				return;
			}

			Vector2 point = socket.worldBound.center;
			VisualElement target = PickGlobal(point);
			Mount owner = OwnerOf(target);

			Line($"  centre ({point.x:0}, {point.y:0}) picks {owner?.Name ?? "none"} → " +
				$"{owner?.Describe(target) ?? "nothing pickable"}");
			Line($"  the socket itself {(ReferenceEquals(target, socket) ? "IS" : "is NOT")} what was picked");

			string thrown = Press(target);
			if (thrown != null) { Line($"  the press raised {thrown}"); }
			Line($"  after: dragging={drag?.IsDragging}, bank slot {BANK_SLOT}={Held(bank, BANK_SLOT)}, " +
				$"inventory {Filled(inventory)} items, pending inv={Pending(ReferenceButtonType.Inventory)} " +
				$"bank={Pending(ReferenceButtonType.Bank)}");
		}

		private static EquippableItemTemplate FindPrimaryHandTemplate()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:BaseItemTemplate", new[] { ITEM_TEMPLATE_DIR }))
			{
				BaseItemTemplate template = AssetDatabase.LoadAssetAtPath<BaseItemTemplate>(
					AssetDatabase.GUIDToAssetPath(guid));
				if (template is EquippableItemTemplate equippable && equippable.Slot == ItemSlot.Primary)
				{
					return equippable;
				}
			}
			return null;
		}

		private static string Held(IItemContainer container, int slot)
		{
			return container != null && container.TryGetItem(slot, out Item item) && item != null
				? item.ID.ToString()
				: "empty";
		}

		private static int Filled(IItemContainer container)
		{
			int filled = 0;
			if (container?.Items == null) { return 0; }
			for (int i = 0; i < container.Items.Count; ++i)
			{
				if (container.Items[i] != null) { ++filled; }
			}
			return filled;
		}

		private static string First(IItemContainer container)
		{
			if (container?.Items == null) { return "none"; }
			for (int i = 0; i < container.Items.Count; ++i)
			{
				if (container.Items[i] != null) { return $"{container.Items[i].ID} at {i}"; }
			}
			return "none";
		}

		private static string Pending(ReferenceButtonType type)
		{
			List<string> slots = new List<string>();
			for (int i = 0; i < 64; ++i)
			{
				if (ItemOperationTracker.IsPending(type, i)) { slots.Add(i.ToString()); }
			}
			return slots.Count == 0 ? "none" : string.Join(",", slots);
		}

		/// <summary>One player press, with the button the panel handlers read.</summary>
		/// <returns>The exception it raised, or null.</returns>
		private static string Press(VisualElement element)
		{
			if (element == null) { return null; }

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
				/* A send site has no network behind it here. Reaching one is itself the evidence
				 * that the path ran, so it is reported rather than swallowed. */
				return $"{ex.GetType().Name}: {ex.Message}";
			}
		}

		// ── reflection, for what the panels keep protected ──────────

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

		// ── output ──────────────────────────────────────────────────

		private static void Line(string text)
		{
			report.AppendLine(text);
		}

		private static void WriteReport()
		{
			Directory.CreateDirectory(OUT_DIR);
			File.WriteAllText(Path.Combine(OUT_DIR, "equip-drop-probe.txt"), report.ToString());
			Debug.Log(report.ToString());
		}

		private static void Capture()
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
				image.Apply();
				File.WriteAllBytes(Path.Combine(OUT_DIR, "panels-composite.png"), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void Release()
		{
			foreach (Mount m in mounts)
			{
				if (m.Panel != null)
				{
					try { UnityEngine.Object.DestroyImmediate(m.Panel.gameObject); }
					catch (Exception ex) { Debug.LogWarning($"[EquipDropProbe] {m.Name}: {ex.Message}"); }
				}
			}
			mounts.Clear();
			drag = null;

			/* The stand-in client is deliberately left standing: Client.OnDestroy reaches into
			 * systems that were never started here, and the process is about to end anyway. */
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
