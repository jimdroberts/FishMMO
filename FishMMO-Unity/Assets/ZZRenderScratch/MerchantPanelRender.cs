using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FishNet.Transporting;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Renders the merchant panel against mock data — a real <c>PlayerCharacter</c> with real item
	/// templates, a runtime merchant template, and the panel's own click path — to PNGs, so the
	/// quantity box can be looked at filled in rather than reasoned about.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Headless: <c>-executeMethod FishMMO.RenderScratch.MerchantPanelRender.Render</c> with a
	/// display (xvfb) and WITHOUT <c>-quit</c>; the capture needs frames to elapse, so this runs as
	/// an <c>EditorApplication.update</c> pump and exits itself. Output goes to
	/// <c>FISHMMO_MERCHANT_RENDER_DIR</c>, defaulting to <c>PanelRenders/</c> beside the repository.
	/// </para>
	/// <para>
	/// Two shots. <c>items</c> is the Buy tab with a stackable row selected and Max applied, which
	/// is issue #263's box. <c>sell</c> is the Sell tab with the deepest bag stack selected and Max
	/// applied, which is #277's "Max Sell Quantity at a time should be max stack size".
	/// </para>
	/// <para>
	/// Selection goes through the panel's own <c>SelectEntry</c> rather than a synthesised pointer
	/// event. A pooled <c>PointerDownEvent</c> reaches the row with no button — even when the
	/// internal setter is driven by reflection, the callback does not run — so the click path
	/// cannot be driven from here. It is covered by MerchantPanelTests, which asserts on the rows
	/// the panel builds; what a capture needs is the resulting state.
	/// </para>
	/// </remarks>
	public static class MerchantPanelRender
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/World/Merchant/UIMerchant.uxml";
		private const int WIDTH = 1200;
		private const int HEIGHT = 800;

		/// <summary>How many rows of the catalogue to show, so the list fits without scrolling.</summary>
		private const int CATALOGUE_SIZE = 9;

		/// <summary>USS transitions on the theme are ~0.1 s; sixty frames lets them settle.</summary>
		private const int SETTLE_FRAMES = 60;

		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static UITKMerchant panel;
		private static int framesWaited;
		private static string outputDirectory;

		private static readonly List<string> queue = new List<string>();
		private static string current;
		private static readonly StringBuilder report = new StringBuilder();

		private static MerchantTemplate template;
		private static BaseItemTemplate stackTemplate;
		private static uint stackAmount = 1;
		private static int stackIndex;

		[MenuItem("FishMMO/UI Toolkit/Render Merchant Panel (mock)")]
		public static void Render()
		{
			outputDirectory = Environment.GetEnvironmentVariable("FISHMMO_MERCHANT_RENDER_DIR");
			if (string.IsNullOrEmpty(outputDirectory))
			{
				outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "PanelRenders"));
			}
			Directory.CreateDirectory(outputDirectory);

			report.Clear();

			queue.Clear();
			queue.Add("items");
			queue.Add("sell");
			current = null;

			framesWaited = 0;
			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
		}

		private static void Pump()
		{
			try
			{
				if (current != null)
				{
					++framesWaited;
					if (framesWaited < SETTLE_FRAMES)
					{
						document?.rootVisualElement?.MarkDirtyRepaint();
						return;
					}

					Measure();
					string path = Path.Combine(outputDirectory, "UIMerchant-" + current + ".png");
					Capture(path);
					Debug.Log($"[MerchantRender] wrote {path}");
					Teardown();
					current = null;
					return;
				}

				if (queue.Count == 0)
				{
					EditorApplication.update -= Pump;
					File.WriteAllText(Path.Combine(outputDirectory, "merchant-panel-mock.txt"),
						"glyph = the .unity-text-element inside #unity-text-input — the element that draws the digits.\n\n" +
						report);
					ReleaseTarget();
					if (Application.isBatchMode) EditorApplication.Exit(0);
					return;
				}

				current = queue[0];
				queue.RemoveAt(0);
				Mount(current);
				framesWaited = 0;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[MerchantRender] pump failed on {current}: {ex}");
				File.WriteAllText(Path.Combine(outputDirectory, "merchant-panel-mock.txt"), report + "\nFAILED: " + ex);
				Teardown();
				ReleaseTarget();
				if (Application.isBatchMode) EditorApplication.Exit(1);
			}
		}

		private static void Mount(string stage)
		{
			EnsureTarget();

			host = new GameObject("Render_UIMerchant_" + stage) { hideFlags = HideFlags.HideAndDontSave };
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);

			// A start-hidden panel: the document is off until the broadcast shows it.
			document.enabled = false;

			List<BaseItemTemplate> catalogue = BuildCatalogue();
			PlayerCharacter character = Rig.Build(host);
			SeedBigStack(character);

			panel = host.AddComponent<UITKMerchant>();
			panel.Document = document;
			panel.StartOpen = false;
			panel.IsAlwaysOpen = false;
			panel.CloseOnQuitToMenu = true;
			panel.ReleasesCursor = false;
			panel.CloseOnEscape = true;

			// Awake registers the panel and wires its handlers, as in the tests.
			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			awake?.Invoke(panel, null);

			panel.SetCharacter(character);

			// Exactly what the server sends when a player opens a general merchant.
			Broadcast();

			VisualElement root = document.rootVisualElement;
			Note($"── {stage} " + new string('─', 40));
			Note($"merchant offers {catalogue.Count} items; bag holds {stackAmount} x {stackTemplate?.name} in one slot");

			if (stage == "items")
			{
				DriveItems(root, catalogue);
			}
			else
			{
				DriveSell(root);
			}

			root?.MarkDirtyRepaint();
		}

		/// <summary>The Buy tab: click a stackable row, then press Max, exactly as a player would.</summary>
		private static void DriveItems(VisualElement root, List<BaseItemTemplate> catalogue)
		{
			VisualElement list = root?.Q("merchant-items");
			if (list == null || list.childCount == 0)
			{
				Note("  !! the Items tab rendered no rows");
				return;
			}

			VisualElement row = stackIndex < list.childCount ? list[stackIndex] : list[0];
			Note($"  Buy row {stackIndex}: '{catalogue[stackIndex].name}' (max stack {catalogue[stackIndex].MaxStackSize})");
			SelectRow(row, false);

			NoteStackableChoice(root);
		}

		/// <summary>The Sell tab: switch tabs, click the deepest stack in the bags, then press Max.</summary>
		private static void DriveSell(VisualElement root)
		{
			Button sellTab = root?.Q<Button>("merchant-tab-sell");
			if (sellTab == null)
			{
				Note("  !! the Sell tab button is missing");
				return;
			}
			Press(sellTab);

			VisualElement list = root.Q("merchant-sell");
			if (list == null || list.childCount == 0)
			{
				Note("  !! the Sell tab rendered no rows");
				return;
			}

			VisualElement row = DeepestStack(list);
			Note($"  Sell tab: {list.childCount} rows; the deepest stack is the one selected");
			SelectRow(row, true);

			NoteStackableChoice(root);
		}

		/// <summary>Presses Max and reports what the box then holds.</summary>
		private static void NoteStackableChoice(VisualElement root)
		{
			Button max = root?.Q<Button>("merchant-qty-max");
			if (max == null)
			{
				Note("  !! the Max button is missing");
				return;
			}

			int ceiling = ReadInt("selectedMaxQuantity");
			Press(max);

			IntegerField field = root.Q<IntegerField>("merchant-qty-field");
			if (field != null && field.value != ceiling)
			{
				// The press did not take; fall back to the same call the button makes.
				Invoke("SetQuantity", ceiling);
				Note($"  (Max press did not land; SetQuantity called directly)");
			}

			Label name = root.Q<Label>("merchant-transaction-name");
			Label total = root.Q<Label>("merchant-transaction-total");
			Label status = root.Q<Label>("merchant-status");
			Note($"  selected: '{name?.text}'  Max ceiling {ceiling}  total '{total?.text}'  status '{status?.text}'");
		}

		/// <summary>
		/// Selects a row through the panel's own <c>SelectEntry</c>. The row's own labels supply the
		/// name, the price and the stack, which is everything that method takes.
		/// </summary>
		private static void SelectRow(VisualElement row, bool isSale)
		{
			if (row == null)
			{
				Note("  !! there is no row to select");
				return;
			}

			string name = null;
			int price = 0;
			int stack = 1;

			foreach (Label label in row.Query<Label>().Build())
			{
				if (label.ClassListContains("merchant-entry__name")) { name = label.text; }
				else if (label.ClassListContains("merchant-entry__price")) { int.TryParse(label.text, out price); }
			}

			if (!string.IsNullOrEmpty(name))
			{
				Match match = Regex.Match(name, @"^(.*)\sx(\d+)$");
				if (match.Success)
				{
					name = match.Groups[1].Value;
					int.TryParse(match.Groups[2].Value, out stack);
				}
			}

			if (stack <= 1) { stack = isSale ? 1 : (int)stackAmount; }

			Invoke("SelectEntry", row, MerchantTabType.Item, 0, price, stack, name, isSale, 0L);
		}

		/// <summary>The row whose name label carries the largest " xN" stack suffix.</summary>
		private static VisualElement DeepestStack(VisualElement list)
		{
			VisualElement best = list[0];
			int bestAmount = 0;
			for (int i = 0; i < list.childCount; ++i)
			{
				VisualElement row = list[i];
				foreach (Label label in row.Query<Label>().Build())
				{
					Match match = Regex.Match(label.text ?? string.Empty, @"\sx(\d+)$");
					if (match.Success && int.TryParse(match.Groups[1].Value, out int amount) && amount > bestAmount)
					{
						bestAmount = amount;
						best = row;
					}
				}
			}
			return best;
		}

		/// <summary>Measures the quantity box in the laid-out tree, which is the point of the shot.</summary>
		private static void Measure()
		{
			VisualElement root = document?.rootVisualElement;
			IntegerField field = root?.Q<IntegerField>("merchant-qty-field");
			VisualElement input = field?.Q("unity-text-input");
			VisualElement glyph = input?.Q(className: "unity-text-element");

			if (field == null || input == null || glyph == null)
			{
				Note("  !! the quantity box is not in the tree");
				return;
			}

			Note($"  measured: field {field.layout.width}x{field.layout.height}, " +
				$"input {input.layout.width}x{input.layout.height}, " +
				$"glyph {glyph.layout.width}x{glyph.layout.height}, " +
				$"value '{field.value}', colour {glyph.resolvedStyle.color}");
		}

		// ── the panel's own paths ───────────────────────────────────

		private static void Broadcast()
		{
			var msg = new MerchantBroadcast { InteractableID = 9001, TemplateID = template.ID };
			MethodInfo handler = typeof(UITKMerchant).GetMethod("OnClientMerchantBroadcastReceived",
				BindingFlags.Instance | BindingFlags.NonPublic);
			handler?.Invoke(panel, new object[] { msg, Channel.Reliable });
		}

		private static void Invoke(string method, params object[] args)
		{
			MethodInfo info = typeof(UITKMerchant).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
			info?.Invoke(panel, args);
		}

		private static int ReadInt(string field)
		{
			FieldInfo info = typeof(UITKMerchant).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
			return info != null ? (int)info.GetValue(panel) : 0;
		}

		/// <summary>The click a player makes on a button.</summary>
		private static void Press(VisualElement element)
		{
			if (element == null) { return; }
			using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
			{
				evt.target = element;
				element.SendEvent(evt);
			}
		}

		// ── mock data ───────────────────────────────────────────────

		/// <summary>
		/// The project's own item templates, registered the way the boot loader registers them and
		/// sorted so the stackable ones — the ones with a quantity worth choosing — come first.
		/// </summary>
		private static List<BaseItemTemplate> BuildCatalogue()
		{
			var all = new List<BaseItemTemplate>();
			foreach (string guid in AssetDatabase.FindAssets("t:BaseItemTemplate", new[] { "Assets/Templates/Entity/Items" }))
			{
				BaseItemTemplate item = AssetDatabase.LoadAssetAtPath<BaseItemTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (item == null) { continue; }

				// The same registration the boot-time loader performs, so Get<T>(id) resolves.
				item.AddToCache(item.name);
				ForceIcon(item);
				all.Add(item);
			}

			all.Sort((a, b) => b.MaxStackSize.CompareTo(a.MaxStackSize));

			stackTemplate = null;
			stackIndex = 0;
			for (int i = 0; i < all.Count; ++i)
			{
				if (all[i].MaxStackSize > 1)
				{
					stackTemplate = all[i];
					stackIndex = i;
					stackAmount = all[i].MaxStackSize;
					break;
				}
			}

			if (stackTemplate == null && all.Count > 0)
			{
				stackTemplate = all[0];
				stackAmount = 1;
			}

			var catalogue = new List<BaseItemTemplate>();
			foreach (BaseItemTemplate item in all)
			{
				if (catalogue.Count >= CATALOGUE_SIZE) { break; }
				catalogue.Add(item);
			}

			template = ScriptableObject.CreateInstance<MerchantTemplate>();
			template.name = "MockGeneralMerchant";
			template.Description = "General goods — mock data for a render.";
			template.BuysItems = true;
			template.SellPriceMultiplier = 0.25f;
			template.Items = catalogue;
			template.AddToCache(template.name);

			return catalogue;
		}

		/// <summary>Puts the deepest stack the catalogue allows into an empty bag slot.</summary>
		private static void SeedBigStack(ICharacter character)
		{
			if (stackTemplate == null || !character.TryGet(out IInventoryController inventory))
			{
				return;
			}

			for (int slot = 0; slot < inventory.Items.Count; ++slot)
			{
				if (inventory.Items[slot] != null) { continue; }
				inventory.SetItemSlot(new Item(7001, 0, stackTemplate, stackAmount), slot);
				return;
			}
		}

		/// <summary>
		/// The template's icon is loaded from an addressable at runtime; nothing has run that
		/// loader here, so the same sprite is pulled straight out of the AssetDatabase. The
		/// reference is read by reflection because this assembly does not reference Addressables.
		/// </summary>
		private static void ForceIcon(BaseItemTemplate item)
		{
			try
			{
				object reference = typeof(BaseItemTemplate)
					.GetField("icon", BindingFlags.Instance | BindingFlags.Public)
					?.GetValue(item);
				if (reference == null) { return; }

				string guid = reference.GetType().GetProperty("AssetGUID")?.GetValue(reference) as string;
				if (string.IsNullOrEmpty(guid)) { return; }

				Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
				if (sprite == null) { return; }

				typeof(BaseItemTemplate)
					.GetField("loadedIcon", BindingFlags.Instance | BindingFlags.NonPublic)
					?.SetValue(item, sprite);
			}
			catch
			{
				// A missing icon is a blank square, not a reason to abandon the shot.
			}
		}

		// ── plumbing ────────────────────────────────────────────────

		private static void Note(string line)
		{
			report.AppendLine(line);
			Debug.Log("[MerchantRender] " + line);
		}

		private static void EnsureTarget()
		{
			if (texture != null)
			{
				return;
			}

			texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
			texture.Create();

			settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.clearColor = true;
			settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);
		}

		private static void Capture(string outputPath)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
				image.Apply();
				File.WriteAllBytes(outputPath, image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void Teardown()
		{
			if (host != null) UnityEngine.Object.DestroyImmediate(host);
			host = null;
			document = null;
			panel = null;
		}

		private static void ReleaseTarget()
		{
			if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.DestroyImmediate(texture);
			}
			settings = null;
			texture = null;
		}
	}
}
