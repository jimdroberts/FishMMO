using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using UnityEditor;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The three windows a player can have open at once — character sheet, bank, bag — must not
	/// cover each other, or the covered half of a window stops answering clicks.
	/// </summary>
	/// <remarks>
	/// Issue #268: "Cannot equip item directly from bank to equipment slot and once attempted
	/// item seems locked in inventory", and, from the report, "clicking an item in the bank (to
	/// get a drag) and then clicking an equipment slot doesn't work either". Neither gesture was
	/// broken: the click never reached the socket.
	/// <para>
	/// Every panel here is its own <see cref="UIDocument"/> over the ONE shared
	/// <c>PanelSettings</c> asset, which makes them siblings in a single UI Toolkit panel rather
	/// than three panels. Picking follows <see cref="UIDocument.sortingOrder"/> across the whole
	/// panel, so where two windows overlap, the whole click goes to whichever is in front — the
	/// one behind is not merely hidden, it is deaf. Opening the bank opens the bag with it
	/// (<c>UIBank.OpensInventoryOnShow</c>), and that sequence left the bag in front of the sheet
	/// and the bank beside it, covering eight of the sheet's ten equipment sockets. The press
	/// landed on the bag instead, which is a bank-to-bag move: the item left the bank, appeared
	/// in the bag, and the slot it landed in carried the pending mark that "locked" it.
	/// </para>
	/// <para>
	/// Geometry, not the panels' own code, is what these tests mount: the window rects and the
	/// sockets are authored in UXML and positioned by USS, so three bare documents sharing one
	/// <c>PanelSettings</c> reproduce the arrangement exactly. Layout settles over frames, hence
	/// the coroutines. The sorting orders the second test sets are the ones the play-mode probe
	/// measured for the reported sequence — sheet first, then the banker — so the test is the
	/// reported arrangement rather than a friendlier one.
	/// </para>
	/// <para>
	/// The shared settings carry a render target of 1200x675, which is what makes the panel that
	/// size: the arrangement is horizontal, and horizontal is the axis PanelSettings pins — it
	/// scales with screen size matching width, so panel space is 1200 units wide at every aspect
	/// ratio while the height follows the screen. Nothing here depends on the height.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class WindowOverlapTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string GuiRoot = "Assets/Scripts/Client/GUI/World/";

		/// <summary>The panel's unit width, which PanelSettings fixes by matching width.</summary>
		private const float PanelWidth = 1200f;

		/// <summary>The panel height at 16:9. Only the width is fixed; the height follows the screen.</summary>
		private const int PanelHeight = 675;

		/// <summary>One of the windows that share the panel, and where its UXML lives.</summary>
		private sealed class Window
		{
			public string Name;
			public string UxmlPath;
			public string PanelClass;
			public int SortingOrder;
		}

		/// <summary>
		/// The three windows, in the order the probe measured them being created in the scene,
		/// with the sorting orders the reported open sequence leaves behind.
		/// </summary>
		private static readonly Window[] Windows =
		{
			new Window { Name = "UIInventory", UxmlPath = GuiRoot + "Inventory/UIInventory.uxml", PanelClass = "inv-panel", SortingOrder = 101 },
			new Window { Name = "UIEquipment", UxmlPath = GuiRoot + "Equipment/UIEquipment.uxml", PanelClass = "eq-panel", SortingOrder = 100 },
			new Window { Name = "UIBank", UxmlPath = GuiRoot + "Bank/UIBank.uxml", PanelClass = "bank-panel", SortingOrder = 102 },
		};

		/// <summary>The sheet's sockets, as the UXML names them, and the slot each one fills.</summary>
		private static readonly string[] Sockets =
		{
			"slot-head", "slot-chest", "slot-legs", "slot-feet",
			"slot-back", "slot-accessory",
			"slot-shoulders", "slot-hands", "slot-mainhand", "slot-offhand",
		};

		private PanelSettings settings;
		private RenderTexture texture;
		private readonly List<GameObject> hosts = new List<GameObject>();
		private readonly List<UIDocument> documents = new List<UIDocument>();

		[SetUp]
		public void SetUp()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");

			/* A render target of the reference size, because that is what gives the panel its
			 * units. Left to the editor's own screen a hand-mounted panel comes up 640x480, and
			 * the windows below are fixed pixel widths: measured there they overlap in any
			 * arrangement, which is a fact about the harness rather than about the layout. At
			 * 1200x675 the panel is the space the player sees at 16:9. */
			texture = new RenderTexture((int)PanelWidth, PanelHeight, 24, RenderTextureFormat.ARGB32);
			texture.Create();

			// One instance shared by all three documents: that is the arrangement under test.
			settings = Object.Instantiate(asset);
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;

			foreach (Window window in Windows)
			{
				VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(window.UxmlPath);
				LogAssert.IsNotNull(uxml, $"the {window.Name} UXML must exist at {window.UxmlPath}");

				GameObject host = new GameObject(window.Name);
				UIDocument document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.visualTreeAsset = uxml;
				document.sortingOrder = window.SortingOrder;

				hosts.Add(host);
				documents.Add(document);
			}
		}

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject host in hosts)
			{
				if (host != null)
				{
					Object.DestroyImmediate(host);
				}
			}

			if (settings != null)
			{
				Object.DestroyImmediate(settings);
			}

			if (texture != null)
			{
				texture.Release();
				Object.DestroyImmediate(texture);
			}

			hosts.Clear();
			documents.Clear();
			settings = null;
			texture = null;
		}

		/// <summary>The window element of one mounted document.</summary>
		private VisualElement WindowElement(int index)
		{
			return documents[index].rootVisualElement.Q(className: Windows[index].PanelClass);
		}

		/// <summary>
		/// Waits for Yoga to place the three documents, and refuses to measure a tree that was
		/// never laid out — where every rect is zero and no overlap assertion could fail.
		/// </summary>
		private IEnumerator Settle()
		{
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			for (int i = 0; i < Windows.Length; ++i)
			{
				Rect rect = WindowElement(i).worldBound;
				LogAssert.IsTrue(rect.width > 1f && rect.height > 1f,
					$"{Windows[i].Name} must be laid out before the geometry below means anything; " +
					$"it resolved to {rect.width}x{rect.height}");
			}

			// PanelSettings is ScaleWithScreenSize with m_Match: 0 (match width), so panel space
			// is exactly this wide at every aspect ratio and the USS percentages are a fixed
			// horizontal arrangement. If this ever stops holding, the numbers in those
			// stylesheets stop meaning what their comments say they mean.
			float width = documents[0].rootVisualElement.layout.width;
			LogAssert.IsTrue(Mathf.Abs(width - PanelWidth) < 1f,
				$"panel space must be {PanelWidth} units wide; it resolved to {width}");
		}

		private static string Describe(Window window, Rect rect)
		{
			return $"{window.Name} {rect.xMin}..{rect.xMax} x {rect.yMin}..{rect.yMax}";
		}

		/// <summary>
		/// No two of the three windows may overlap: an overlap is a window that cannot be clicked
		/// where the other one covers it.
		/// </summary>
		[UnityTest]
		public IEnumerator NoTwoWindowsOverlap()
		{
			yield return Settle();

			for (int i = 0; i < Windows.Length; ++i)
			{
				Rect a = WindowElement(i).worldBound;

				LogAssert.IsTrue(a.xMin >= 0f && a.xMax <= PanelWidth,
					$"{Describe(Windows[i], a)} — a window pushed past the panel edge is a window " +
					"the player cannot reach");

				for (int j = i + 1; j < Windows.Length; ++j)
				{
					Rect b = WindowElement(j).worldBound;

					if (!a.Overlaps(b))
					{
						continue;
					}

					/* They are siblings of one UI Toolkit panel, so a shared rectangle is not a
					 * cosmetic overlap: the click there goes to whichever document sorts higher
					 * (UIDocument.sortingOrder) and the other one never sees it. */
					Assert.Fail(
						$"{Describe(Windows[i], a)} overlaps {Describe(Windows[j], b)} — the click " +
						"in the shared rectangle goes to the higher sorting order alone, so part of " +
						$"{Windows[i].Name} stops answering the mouse (issue #268)");
				}
			}
		}

		/// <summary>
		/// The reported gesture itself: with the bank open (and with it, the bag), a press at each
		/// equipment socket has to reach the sheet.
		/// </summary>
		/// <remarks>
		/// This is the half of the report that was not about the layout being pretty. The pick is
		/// the panel's own — the same <c>Panel.Pick</c> UI Toolkit runs for a real pointer — so a
		/// pass here means a click at that point is delivered into the sheet's tree, and the sockets
		/// whose handler would answer it are the ones the sheet itself registers on.
		/// </remarks>
		[UnityTest]
		public IEnumerator EveryEquipmentSocketIsClickableWithTheBankOpen()
		{
			yield return Settle();

			VisualElement equipment = WindowElement(1);
			LogAssert.IsNotNull(equipment, "the sheet's window element must exist");

			IPanel panel = equipment.panel;
			LogAssert.IsNotNull(panel, "the sheet must belong to a UI Toolkit panel");

			int reachable = 0;

			foreach (string socketName in Sockets)
			{
				VisualElement socket = equipment.Q(socketName);
				LogAssert.IsNotNull(socket, $"the sheet must author a {socketName}");

				Vector2 centre = socket.worldBound.center;
				VisualElement picked = panel.Pick(centre);

				LogAssert.IsNotNull(picked,
					$"a press at {socketName}'s centre ({centre.x}, {centre.y}) must land on something");

				/* Which document answered: walk up from the picked element, which is the sheet's
				 * own socket or one of its children when the click got through. */
				bool mine = false;
				for (VisualElement element = picked; element != null; element = element.parent)
				{
					if (ReferenceEquals(element, equipment))
					{
						mine = true;
						break;
					}
				}

				if (!mine)
				{
					Assert.Fail(
						$"a press at {socketName}'s centre ({centre.x}, {centre.y}) is delivered to " +
						$"{picked.name ?? "(unnamed)"} of class {string.Join(" ", picked.GetClasses())} " +
						"instead of the sheet, so the socket never sees the click and the item in " +
						"hand goes wherever that element sends it (issue #268)");
				}

				++reachable;
			}

			LogAssert.AreEqual(Sockets.Length, reachable,
				"every equipment socket must be clickable while the bank is open");
		}
	}
}
