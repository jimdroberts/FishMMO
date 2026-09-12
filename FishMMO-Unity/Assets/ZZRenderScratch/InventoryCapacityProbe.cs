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
	/// Throwaway harness: captures the inventory and bank panels to show the moved capacity
	/// readout and the new footer currency chip.
	/// </summary>
	/// <remarks>
	/// <para>Two things this probe does that the shared <see cref="AllPanelsRender"/> helper cannot.
	/// It sets <c>CurrencyTemplateID</c> <em>before</em> the character, because the panel subscribes
	/// to the currency inside <c>OnPostSetCharacter</c> and a designer field assigned afterwards is
	/// read by nothing. And it mounts tree-first (see <see cref="EquipmentProbe"/> for why that
	/// order is the one a panel already on screen sees).</para>
	/// <para>Captures are cropped to the panel's own <c>worldBound</c>. Both panels are absolutely
	/// positioned in a screen-sized document, so an uncropped 1200x900 frame is nearly all backdrop
	/// and the 9px count on the capacity bar is unreadable.</para>
	/// </remarks>
	public static class InventoryCapacityProbe
	{
		private const string OUTPUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders";
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string CURRENCY_ASSET = "Assets/Templates/Entity/CharacterAttributes/Misc/Currency.asset";
		private const int WIDTH = 1200;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 40;
		private const int CROP_MARGIN = 16;

		/// <summary>The balance the probe parks on the character, so the chip reads a round number.</summary>
		private const int CURRENCY_VALUE = 12450;

		private sealed class Job
		{
			public string Name;
			public string Uxml;
			public bool IsBank;
		}

		private static readonly List<Job> queue = new List<Job>();
		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static Job current;
		private static int framesWaited;

		[MenuItem("FishMMO/UI Toolkit/Render Inventory Capacity + Currency")]
		public static void Render()
		{
			try
			{
				Directory.CreateDirectory(OUTPUT_DIR);
				Seed.All();

				queue.Clear();
				queue.Add(new Job
				{
					Name = "UIInventory-capacity-currency",
					Uxml = "Assets/Scripts/Client/GUI/World/Inventory/UIInventory.uxml",
					IsBank = false,
				});
				queue.Add(new Job
				{
					Name = "UIBank-capacity-currency",
					Uxml = "Assets/Scripts/Client/GUI/World/Bank/UIBank.uxml",
					IsBank = true,
				});

				texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
				texture.Create();

				settings = UnityEngine.Object.Instantiate(
					AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[CapacityProbe] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
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

					Capture(current);
					Teardown();
					current = null;
					return;
				}

				if (queue.Count == 0)
				{
					EditorApplication.update -= Pump;
					Release();
					EditorApplication.Exit(0);
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
				Debug.LogError($"[CapacityProbe] pump failed on {current?.Name}: {ex}");
				Release();
				EditorApplication.Exit(1);
			}
		}

		private static void Mount(Job job)
		{
			host = new GameObject("Render_" + job.Name) { hideFlags = HideFlags.HideAndDontSave };
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(job.Uxml);

			PlayerCharacter character = Rig.Build(host);

			/* Parked on a round number: the shared fixtures roll every non-resource attribute, and
			 * a rolled 63 tells the viewer nothing about whether the chip is wired to the right
			 * attribute. Rebuilt rather than mutated, so the value the panel subscribes to below is
			 * the instance that holds it — SetAttribute replaces the object. */
			CharacterAttributeTemplate currency =
				AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(CURRENCY_ASSET);
			if (currency == null)
			{
				throw new InvalidOperationException("currency template not found at " + CURRENCY_ASSET);
			}
			if (character.TryGet(out ICharacterAttributeController attributes))
			{
				attributes.SetAttribute(currency.ID, CURRENCY_VALUE);
			}

			UITKItemGridPanel panel = job.IsBank
				? (UITKItemGridPanel)host.AddComponent<UITKBank>()
				: host.AddComponent<UITKInventory>();
			panel.Document = document;

			// Before the character, so OnPostSetCharacter's SubscribeCurrency has a template to
			// resolve. Assign it after and the chip stays hidden — which is the whole failure mode
			// this probe exists to not reproduce.
			panel.CurrencyTemplateID = currency.ID;

			panel.OnStarting();
			panel.SetCharacter(character);

			Debug.Log($"[CapacityProbe] {job.Name}: currency template '{currency.name}' " +
				$"id={currency.ID} value={CURRENCY_VALUE}");

			document.rootVisualElement?.MarkDirtyRepaint();
		}

		private static void Capture(Job job)
		{
			Report(job);

			Rect crop = PanelRect();
			int x = Mathf.Clamp(Mathf.FloorToInt(crop.x), 0, WIDTH - 1);
			int y = Mathf.Clamp(Mathf.FloorToInt(crop.y), 0, HEIGHT - 1);
			int w = Mathf.Clamp(Mathf.CeilToInt(crop.width), 1, WIDTH - x);
			int h = Mathf.Clamp(Mathf.CeilToInt(crop.height), 1, HEIGHT - y);

			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(w, h, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(x, y, w, h), 0, 0);
				image.Apply();
				File.WriteAllBytes(Path.Combine(OUTPUT_DIR, job.Name + ".png"), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
				Debug.Log($"[CapacityProbe] wrote {job.Name}.png ({w}x{h} from {x},{y})");
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		/// <summary>
		/// Prints what the panel actually resolved, so the capture is checked against a measurement
		/// rather than against a viewer's impression of a PNG.
		/// </summary>
		private static void Report(Job job)
		{
			VisualElement root = document?.rootVisualElement;
			if (root == null) { return; }

			string prefix = job.IsBank ? "bank" : "inv";
			Label count = root.Q<Label>("header-subtitle");
			VisualElement fill = root.Q(prefix + "-capacity-fill");
			VisualElement chip = root.Q(prefix + "-currency-chip");
			Label name = root.Q<Label>(prefix + "-currency-name");
			Label value = root.Q<Label>(prefix + "-currency-value");

			// The bar and the fill are both inside the header's title column, which is the point of
			// the change: the readout and the bar it describes share one box.
			VisualElement bar = root.Q(prefix + "-capacity-track");

			Debug.Log($"[CapacityProbe] {job.Name}:" +
				$" count='{count?.text}'" +
				$" bar={(bar != null ? bar.worldBound.ToString() : "MISSING")}" +
				$" fillWidth={fill?.resolvedStyle.width ?? -1f} of {bar?.resolvedStyle.width ?? -1f}" +
				$" chipHidden={chip?.ClassListContains(prefix + "-hidden") ?? true}" +
				$" chip={(chip != null ? chip.worldBound.ToString() : "MISSING")}" +
				$" name='{name?.text}' value='{value?.text}'");
		}

		/// <summary>
		/// The panel root's bounds in the render target's pixel space, padded and flipped.
		/// </summary>
		/// <remarks>
		/// <c>worldBound</c> is top-left origin; <c>ReadPixels</c> is bottom-left. Falls back to the
		/// full frame when the panel has no layout — an unpainted panel still produces a PNG, and a
		/// blank one says more than a crash does.
		/// </remarks>
		private static Rect PanelRect()
		{
			VisualElement root = document?.rootVisualElement?.Q("panel-root");
			Rect bound = root?.worldBound ?? default;

			if (root == null || bound.width < 1.0f || bound.height < 1.0f)
			{
				Debug.LogWarning("[CapacityProbe] panel-root has no layout; capturing the full frame");
				return new Rect(0, 0, WIDTH, HEIGHT);
			}

			float left = Mathf.Max(0.0f, bound.xMin - CROP_MARGIN);
			float right = Mathf.Min(WIDTH, bound.xMax + CROP_MARGIN);
			float top = Mathf.Max(0.0f, bound.yMin - CROP_MARGIN);
			float bottom = Mathf.Min(HEIGHT, bound.yMax + CROP_MARGIN);

			return new Rect(left, HEIGHT - bottom, right - left, bottom - top);
		}

		private static void Teardown()
		{
			if (host != null) UnityEngine.Object.DestroyImmediate(host);
			host = null;
			document = null;
		}

		private static void Release()
		{
			Teardown();
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
