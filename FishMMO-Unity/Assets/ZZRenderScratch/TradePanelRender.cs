using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Renders the trade window (issue #144) with a mock two-sided table to a PNG, so the
	/// layout can be looked at without two clients and a server.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Headless: <c>-executeMethod FishMMO.RenderScratch.TradePanelRender.Render</c> with a
	/// display (xvfb) and WITHOUT <c>-quit</c>; the capture needs frames to elapse, so this
	/// runs as an <c>EditorApplication.update</c> pump and exits itself. One PNG per stage of
	/// the two-stage flow is written to <c>FISHMMO_TRADE_RENDER_DIR</c>, defaulting to
	/// <c>PanelRenders/</c> beside the repository.
	/// </para>
	/// <para>
	/// The character comes from <see cref="Rig"/> — a real <c>PlayerCharacter</c> with a fake
	/// inventory seeded from the project's own item templates — so the left table shows real
	/// icons; the right table is built from other templates with seeds, exactly as the server
	/// would describe a partner's items to a client that does not hold them.
	/// </para>
	/// </remarks>
	public static class TradePanelRender
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/World/Trade/UITrade.uxml";
		private const int WIDTH = 1200;
		private const int HEIGHT = 800;

		/// <summary>USS transitions on the theme are ~0.1 s; sixty frames lets them settle.</summary>
		private const int SETTLE_FRAMES = 60;

		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static int framesWaited;
		private static string outputDirectory;

		/// <summary>The stages worth looking at, one PNG each.</summary>
		private static readonly List<TradePanelPopulator.Stage> queue = new List<TradePanelPopulator.Stage>();
		private static TradePanelPopulator.Stage current;

		[MenuItem("FishMMO/UI Toolkit/Render Trade Panel (mock)")]
		public static void Render()
		{
			outputDirectory = Environment.GetEnvironmentVariable("FISHMMO_TRADE_RENDER_DIR");
			if (string.IsNullOrEmpty(outputDirectory))
			{
				outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "PanelRenders"));
			}
			Directory.CreateDirectory(outputDirectory);

			queue.Clear();
			queue.Add(TradePanelPopulator.Stage.Editing);
			queue.Add(TradePanelPopulator.Stage.Locked);
			current = TradePanelPopulator.Stage.None;

			framesWaited = 0;
			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
		}

		private static void Pump()
		{
			try
			{
				if (current != TradePanelPopulator.Stage.None)
				{
					++framesWaited;
					if (framesWaited < SETTLE_FRAMES)
					{
						document?.rootVisualElement?.MarkDirtyRepaint();
						return;
					}

					string path = Path.Combine(outputDirectory, "UITrade-" + current.ToString().ToLowerInvariant() + ".png");
					Capture(path);
					Debug.Log($"[TradeRender] wrote {path}");
					Teardown();
					current = TradePanelPopulator.Stage.None;
					return;
				}

				if (queue.Count == 0)
				{
					EditorApplication.update -= Pump;
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
				Debug.LogError($"[TradeRender] pump failed on {current}: {ex}");
				Teardown();
				ReleaseTarget();
				if (Application.isBatchMode) EditorApplication.Exit(1);
			}
		}

		private static void Mount(TradePanelPopulator.Stage stage)
		{
			EnsureTarget();

			host = new GameObject("Render_UITrade_" + stage) { hideFlags = HideFlags.HideAndDontSave };
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);

			TradePanelPopulator.Trade(host, document, stage);

			document.rootVisualElement?.MarkDirtyRepaint();
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

	/// <summary>The trade window's populator, shared by the one-off render and the all-panels run.</summary>
	public static class TradePanelPopulator
	{
		/// <summary>Which stage of the two-stage flow to draw.</summary>
		public enum Stage
		{
			/// <summary>Nothing mounted.</summary>
			None = 0,

			/// <summary>Both sides still editing: offers on the table, nobody has confirmed.</summary>
			Editing,

			/// <summary>Both confirmed — the table is frozen — and the partner has accepted.</summary>
			Locked,
		}

		/// <summary>
		/// Mounts <see cref="UITKTrade"/> on a rigged character and applies a two-sided mock
		/// table: some of the character's own bag items on the left, items it does not hold on
		/// the right, currency both ways, at the requested stage.
		/// </summary>
		public static void Trade(GameObject h, UIDocument d, Stage stage = Stage.Locked)
		{
			PlayerCharacter character = Rig.Build(h);

			UITKTrade panel = h.AddComponent<UITKTrade>();
			panel.Document = d;
			panel.StartOpen = false;
			panel.CurrencyTemplateID = ResolveCurrencyTemplateID();

			// Awake registers the panel and wires its drag/focus handlers, as in the tests.
			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			awake?.Invoke(panel, null);

			panel.SetCharacter(character);
			panel.OpenWith(2002, "Maerwyn", TradeRules.DefaultMaxDistance);

			bool locked = stage == Stage.Locked;

			panel.ApplyState(new TradeStateBroadcast
			{
				Version = 7,
				OwnOffer = OwnOffers(character, 3),
				OwnCurrency = 125,
				OwnConfirmed = locked,
				OwnAccepted = false,
				PartnerOffer = PartnerOffers(character, 4),
				PartnerCurrency = 480,
				PartnerConfirmed = locked,
				// The most informative frame: they have accepted and are waiting on you.
				PartnerAccepted = locked,
			});
		}

		/// <summary>The first <paramref name="count"/> occupied bag slots, as the server would describe them.</summary>
		private static TradeOfferEntry[] OwnOffers(ICharacter character, int count)
		{
			var entries = new List<TradeOfferEntry>();
			if (character.TryGet(out IInventoryController inventory))
			{
				for (int i = 0; i < inventory.Items.Count && entries.Count < count; ++i)
				{
					Item item = inventory.Items[i];
					if (item?.Template == null) continue;
					entries.Add(new TradeOfferEntry
					{
						Slot = i,
						ItemID = item.ID,
						TemplateID = item.Template.ID,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						Amount = item.IsStackable ? item.Stackable.Amount : 1u,
					});
				}
			}
			return entries.ToArray();
		}

		/// <summary>Templates the character does NOT hold, so the two tables visibly differ.</summary>
		private static TradeOfferEntry[] PartnerOffers(ICharacter character, int count)
		{
			var held = new HashSet<int>();
			if (character.TryGet(out IInventoryController inventory))
			{
				foreach (Item item in inventory.Items)
				{
					if (item?.Template != null) held.Add(item.Template.ID);
				}
			}

			var templates = new List<BaseItemTemplate>();
			foreach (string guid in AssetDatabase.FindAssets("t:BaseItemTemplate", new[] { "Assets/Templates/Entity/Items" }))
			{
				BaseItemTemplate template = AssetDatabase.LoadAssetAtPath<BaseItemTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (template == null) continue;

				// The same registration the boot-time loader performs, so Get<T>(id) resolves.
				template.AddToCache(template.name);
				templates.Add(template);
			}

			/* Prefer templates the character does not hold, so the two tables visibly differ;
			 * fall back to held ones, because a small project can seed the whole catalogue
			 * into one bag and an empty partner table shows nothing worth looking at. */
			var chosen = new List<BaseItemTemplate>();
			foreach (BaseItemTemplate t in templates) { if (!held.Contains(t.ID) && chosen.Count < count) chosen.Add(t); }
			for (int i = templates.Count - 1; i >= 0 && chosen.Count < count; --i) { if (!chosen.Contains(templates[i])) chosen.Add(templates[i]); }

			var entries = new List<TradeOfferEntry>();
			long id = 9000;
			uint amount = 3;
			foreach (BaseItemTemplate template in chosen)
			{
				entries.Add(new TradeOfferEntry
				{
					Slot = entries.Count,
					ItemID = id++,
					TemplateID = template.ID,
					Seed = 0,
					Amount = template.MaxStackSize > 1 ? Math.Min(amount++, template.MaxStackSize) : 1u,
				});
			}
			return entries.ToArray();
		}

		private static int ResolveCurrencyTemplateID()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:CharacterAttributeTemplate", new[] { "Assets/Templates/Entity/CharacterAttributes" }))
			{
				var template = AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (template != null && template.name == "Currency")
				{
					template.AddToCache(template.name);
					return template.ID;
				}
			}
			return 0;
		}
	}
}
