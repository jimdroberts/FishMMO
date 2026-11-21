using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Renders one card per tooltip-producing type, so every description in the game can be looked
	/// at side by side.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Headless: <c>-executeMethod FishMMO.RenderScratch.TooltipGalleryRender.Render</c> under xvfb
	/// and WITHOUT <c>-quit</c>.
	/// </para>
	/// <para>
	/// Every card goes through the shipped <see cref="UITKTooltipView"/> against real
	/// <see cref="TooltipContent"/> built by the real producers, so what is captured is what a
	/// player sees on hover. Subjects are the project's own assets wherever one exists; the few
	/// that are built in memory are the kinds of thing the project ships no example of, and each
	/// says so where it is created.
	/// </para>
	/// </remarks>
	public static class TooltipGalleryRender
	{
		private static readonly string OUTPUT_DIR = ProbeOutput.Path("PanelRenders");
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string THEME_USS = "Assets/FishMMO/Client/GUI/FishMMO-Theme.uss";
		private const string VIEW_USS = "Assets/FishMMO/Client/GUI/Shared/Tooltip/UITooltipView.uss";
		private const int WIDTH = 1400;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 40;

		private sealed class Page
		{
			public string Label;
			public List<(string caption, TooltipContent content)> Cards = new List<(string, TooltipContent)>();
		}

		private static readonly List<Page> queue = new List<Page>();
		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static Page current;
		private static int framesWaited;

		/// <summary>Modules' tooltip pages, as (label, cards): the Abilities module adds its abilities.</summary>
		public static readonly List<(string label, System.Func<List<(string, TooltipContent)>> cards)> ExtraPages =
			new List<(string label, System.Func<List<(string, TooltipContent)>> cards)>();

		[DashboardTool(DashboardToolAttribute.UITests, "Render Tooltip Gallery", Section = "Renders", Order = 5)]
		public static void Render()
		{
			try
			{
				Directory.CreateDirectory(OUTPUT_DIR);
				TooltipSubjects.Load();

				queue.Clear();
				queue.Add(new Page { Label = "Tooltips-items", Cards = TooltipSubjects.Items() });
				// Modules' pages (the Abilities module's ability tooltips), after the items'.
				foreach ((string label, System.Func<List<(string, TooltipContent)>> cards) in ExtraPages)
				{
					queue.Add(new Page { Label = label, Cards = cards() });
				}

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[TooltipGallery] setup failed: {ex}");
				if (Application.isBatchMode) EditorApplication.Exit(1);
			}
		}

		private static void Pump()
		{
			try
			{
				if (current == null)
				{
					if (queue.Count == 0)
					{
						EditorApplication.update -= Pump;
						Teardown();
						Debug.Log("[TooltipGallery] done");
						if (Application.isBatchMode) EditorApplication.Exit(0);
						return;
					}

					current = queue[0];
					queue.RemoveAt(0);
					framesWaited = 0;
					Mount(current);
					return;
				}

				++framesWaited;
				if (framesWaited < SETTLE_FRAMES)
				{
					document?.rootVisualElement?.MarkDirtyRepaint();
					return;
				}

				Capture(Path.Combine(OUTPUT_DIR, current.Label + ".png"));
				Debug.Log($"[TooltipGallery] wrote {current.Label}.png");
				Unmount();
				current = null;
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[TooltipGallery] pump failed on {current?.Label}: {ex}");
				Teardown();
				if (Application.isBatchMode) EditorApplication.Exit(1);
			}
		}

		private static void Mount(Page page)
		{
			if (texture == null)
			{
				texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
				texture.Create();
			}

			if (settings == null)
			{
				settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.02f, 0.04f, 0.05f, 1.0f);
			}

			host = new GameObject("Render_" + page.Label) { hideFlags = HideFlags.HideAndDontSave };
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;

			VisualElement root = document.rootVisualElement;
			root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(THEME_USS));
			root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(VIEW_USS));
			root.style.flexDirection = FlexDirection.Row;
			root.style.flexWrap = Wrap.Wrap;
			root.style.alignItems = Align.FlexStart;
			root.style.paddingLeft = 12;
			root.style.paddingTop = 12;

			foreach ((string caption, TooltipContent content) in page.Cards)
			{
				root.Add(BuildCard(caption, content));
			}

			root.MarkDirtyRepaint();
		}

		/// <summary>One captioned tooltip, drawn exactly as the hover tooltip draws it.</summary>
		private static VisualElement BuildCard(string caption, TooltipContent content)
		{
			VisualElement column = new VisualElement();
			column.style.width = 320;
			column.style.marginRight = 14;
			column.style.marginBottom = 14;

			Label heading = new Label(caption);
			heading.AddToClassList("fish-col-head");
			heading.style.marginBottom = 4;
			column.Add(heading);

			/* .fish-tooltip is the theme's tooltip surface — the same class the real tooltip box
			 * carries — so the card is the real thing rather than an approximation of it. */
			VisualElement box = new VisualElement();
			box.AddToClassList("fish-tooltip");
			box.style.paddingLeft = 8;
			box.style.paddingRight = 8;
			box.style.paddingTop = 6;
			box.style.paddingBottom = 6;

			UITKTooltipView.Render(box, content);
			column.Add(box);
			return column;
		}

		private static void Unmount()
		{
			if (host != null) UnityEngine.Object.DestroyImmediate(host);
			host = null;
			document = null;
		}

		private static void Capture(string path)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
				image.Apply();
				File.WriteAllBytes(path, image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void Teardown()
		{
			Unmount();
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

	/// <summary>The subjects the gallery describes, one per producer type in the game.</summary>
	public static class TooltipSubjects
	{
		private static readonly List<ScriptableObject> temporary = new List<ScriptableObject>();
		private static bool loaded;

		/// <summary>Registers every template type the gallery reads, as the boot loader would.</summary>
		public static void Load()
		{
			if (loaded)
			{
				return;
			}
			loaded = true;

			RegisterAll<BaseItemTemplate>("Assets/FishMMO/Modules/Items/Templates");
			RegisterAll<ItemAttributeTemplate>("Assets/FishMMO/Modules/Items/Templates");
			RegisterAll<CharacterAttributeTemplate>("Assets/FishMMO/Templates/Entity/CharacterAttributes");
			// Module templates (abilities, premades) are registered by the module's own page (TooltipGalleryRender.ExtraPages).
		}

		/// <summary>Adds every <typeparamref name="T"/> under <paramref name="folder"/> to its cache, as the boot loader would.</summary>
		public static void RegisterAll<T>(string folder) where T : ScriptableObject
		{
			foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder }))
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset is ICachedObject)
				{
					asset.GetType()
						.GetMethod("AddToCache", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
						?.Invoke(asset, new object[] { asset.name });
				}
			}
		}

		/// <summary>Every item-side tooltip: a template, a generated instance, and each item kind.</summary>
		public static List<(string, TooltipContent)> Items()
		{
			List<(string, TooltipContent)> cards = new List<(string, TooltipContent)>();

			WeaponTemplate weapon = First<WeaponTemplate>();
			ArmorTemplate armor = First<ArmorTemplate>();
			ConsumableTemplate consumable = First<ConsumableTemplate>();
			BaseItemTemplate stackable = FirstWhere<BaseItemTemplate>(t => t.MaxStackSize > 1);

			if (weapon != null)
			{
				cards.Add(($"Weapon template — {weapon.name}", weapon.BuildContent()));

				/* A generated instance: the roll is what the player is actually holding, and it is
				 * written by ItemGenerator on top of the template's own description. */
				Item rolled = new Item(9001, 20250909, weapon, 1);
				cards.Add(($"Weapon instance (rolled) — {weapon.name}", rolled.BuildContent()));
			}

			if (armor != null)
			{
				cards.Add(($"Armor template — {armor.name}", armor.BuildContent()));
			}

			if (armor != null)
			{
				Item worn = new Item(9003, 20250101, armor, 1) { Slot = 4 };
				cards.Add(($"Armor instance in a slot — {armor.name}", worn.BuildContent()));
			}

			/* The mock consumables, which are real assets now — see MockConsumableBuilder. The
			 * potion is shown as a held stack because that is where the Amount row comes from. */
			ResourceConsumableTemplate potionTemplate = Find<ResourceConsumableTemplate>("Minor Healing Potion");
			if (potionTemplate != null)
			{
				Item potion = new Item(9002, 0, potionTemplate, 7);
				cards.Add(("Resource consumable — " + potionTemplate.name, potion.BuildContent()));
			}

			ResourceConsumableTemplate restorative = Find<ResourceConsumableTemplate>("Tideborn Restorative");
			if (restorative != null)
			{
				cards.Add(("Multi-resource consumable — " + restorative.name, restorative.BuildContent()));
			}

			// Knowledge scrolls are the Abilities module's page.

			if (stackable != null)
			{
				cards.Add(($"Stackable template — {stackable.name}", stackable.BuildContent()));
			}

			return cards;
		}

		/// <summary>Creates a throwaway template that lives only for this run.</summary>
		public static T Temp<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.hideFlags = HideFlags.HideAndDontSave;
			asset.name = name;
			temporary.Add(asset);
			return asset;
		}

		/// <summary>Finds an asset of a type by its exact name, anywhere under Templates.</summary>
		public static T Find<T>(string name) where T : ScriptableObject
		{
			return FirstWhere<T>(t => t.name == name);
		}

		public static T First<T>() where T : ScriptableObject
		{
			return FirstWhere<T>(_ => true);
		}

		public static T FirstWhere<T>(Func<T, bool> predicate) where T : ScriptableObject
		{
			foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets/FishMMO/Templates", "Assets/FishMMO/Modules" }))
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset != null && predicate(asset))
				{
					return asset;
				}
			}
			return null;
		}
	}

}
