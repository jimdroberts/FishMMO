using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: renders the world HUD and two windows at real screen sizes, once through
	/// ConstantPhysicalSize (before) and once through ScaleWithScreenSize (after), plus the HUD at
	/// interface scale 0.8 and 1.25.
	/// </summary>
	/// <remarks>
	/// The render target is the SCREEN size, not the 1200-wide panel size the other probes use, so
	/// PanelSettings resolves its own scale factor exactly as it does against a real display.
	/// </remarks>
	public static class InterfaceScalingRender
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string ROOT = "Assets/Scripts/Client/GUI/World/";
		private const string OUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders/interface-scaling";
		private const int SETTLE_FRAMES = 45;

		private sealed class Job
		{
			public string Scene;
			public int Width;
			public int Height;
			public bool After;
			public float Scale;

			public string Label => string.Format(CultureInfo.InvariantCulture, "{0}-{1}x{2}-{3}{4}",
				Scene, Width, Height, After ? "after" : "before",
				Scale > 0f ? "-scale" + Scale.ToString("0.00", CultureInfo.InvariantCulture) : "");
		}

		private sealed class HudMount
		{
			public string Uxml;
			public Type PanelType;
			public bool LayoutOnly;
			public Action<GameObject, UIDocument> Populate;
		}

		private static readonly HudMount[] hud =
		{
			new HudMount { Uxml = ROOT + "ResourceBar/UIResourceBar.uxml", PanelType = typeof(UITKHealthBar) },
			new HudMount { Uxml = ROOT + "ResourceBar/UIResourceBar.uxml", PanelType = typeof(UITKManaBar) },
			new HudMount { Uxml = ROOT + "ResourceBar/UIResourceBar.uxml", PanelType = typeof(UITKStaminaBar) },
			new HudMount { Uxml = ROOT + "Buff/UIBuff.uxml", PanelType = typeof(UITKBuff) },
			new HudMount { Uxml = ROOT + "Buff/UIDebuff.uxml", PanelType = typeof(UITKDebuff) },
			new HudMount { Uxml = ROOT + "HotkeyBar/UIHotkeyBar.uxml", PanelType = typeof(UITKHotkeyBar) },
			new HudMount { Uxml = ROOT + "Ability/UICastBar.uxml", PanelType = typeof(UITKCastBar) },
			new HudMount { Uxml = ROOT + "Minimap/UIMinimap.uxml", PanelType = typeof(UITKMinimap) },
			new HudMount { Uxml = ROOT + "Crosshair/UICrosshair.uxml", PanelType = typeof(UITKCrosshair), LayoutOnly = true },
			new HudMount { Uxml = ROOT + "Target/UITarget.uxml", PanelType = typeof(UITKTarget) },
			new HudMount { Uxml = ROOT + "Toast/UIToast.uxml", PanelType = typeof(UITKToast) },
			new HudMount { Uxml = ROOT + "Party/UIParty.uxml", Populate = Panels.Party },
			new HudMount { Uxml = ROOT + "Chat/UIChat.uxml", PanelType = typeof(UITKChat) },
			new HudMount { Uxml = ROOT + "Arena/UIArenaHud.uxml", PanelType = typeof(UITKArenaHud) },
		};

		private static readonly List<Job> jobs = new List<Job>();
		private static readonly List<GameObject> hosts = new List<GameObject>();
		private static readonly StringBuilder summary = new StringBuilder();
		private static RenderTexture texture;
		private static PanelSettings settings;
		private static Job current;
		private static int frames;

		public static void Run()
		{
			try
			{
				Directory.CreateDirectory(OUT_DIR);
				Seed.All();

				(int w, int h)[] sizes = { (1280, 720), (1920, 1080), (2560, 1440), (3440, 1440) };
				// Set to re-render the "after" set only, leaving the original "before" captures alone.
				bool onlyAfter = Environment.GetEnvironmentVariable("FISHMMO_ONLY_AFTER") == "1";
				foreach ((int w, int h) in sizes)
				{
					foreach (bool after in onlyAfter ? new[] { true } : new[] { false, true })
					{
						jobs.Add(new Job { Scene = "hud", Width = w, Height = h, After = after });
						jobs.Add(new Job { Scene = "windows", Width = w, Height = h, After = after });
					}
					if (w == 1920)
					{
						jobs.Add(new Job { Scene = "hud", Width = w, Height = h, After = true, Scale = 0.8f });
						jobs.Add(new Job { Scene = "hud", Width = w, Height = h, After = true, Scale = 1.25f });
					}
				}

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Scale] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static void Pump()
		{
			try
			{
				if (current == null)
				{
					if (jobs.Count == 0)
					{
						Finish();
						return;
					}
					current = jobs[0];
					jobs.RemoveAt(0);
					Begin(current);
					frames = 0;
					return;
				}

				foreach (GameObject host in hosts)
				{
					if (host == null) { continue; }
					UITKControl control = host.GetComponent<UITKControl>();
					if (control != null)
					{
						Panels.StartWhenReady(control);
						Panels.Tick(control);
					}
					host.GetComponent<UIDocument>()?.rootVisualElement?.MarkDirtyRepaint();
				}

				if (++frames < SETTLE_FRAMES)
				{
					return;
				}

				Measure(current);
				Capture(current);
				End();
				current = null;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Scale] pump failed on {current?.Label}: {ex}");
				summary.AppendLine($"{current?.Label}: FAILED {ex.GetType().Name}: {ex.Message}");
				End();
				current = null;
			}
		}

		private static void Begin(Job job)
		{
			if (texture == null || texture.width != job.Width || texture.height != job.Height)
			{
				if (texture != null)
				{
					texture.Release();
					UnityEngine.Object.DestroyImmediate(texture);
				}
				texture = new RenderTexture(job.Width, job.Height, 24, RenderTextureFormat.ARGB32);
				texture.Create();
			}

			settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.clearColor = true;
			settings.colorClearValue = new Color(0.20f, 0.24f, 0.22f, 1.0f);
			settings.scaleMode = job.After ? PanelScaleMode.ScaleWithScreenSize : PanelScaleMode.ConstantPhysicalSize;

			if (job.Scene == "hud")
			{
				foreach (HudMount mount in hud)
				{
					Mount(mount.Uxml, 0, (h, d) =>
					{
						if (mount.Populate != null)
						{
							mount.Populate(h, d);
							return;
						}
						UITKControl control = (UITKControl)h.AddComponent(mount.PanelType);
						control.Document = d;
						if (!mount.LayoutOnly)
						{
							control.OnStarting();
						}
					}, showAll: true);
				}
			}
			else
			{
				Mount(ROOT + "CharacterSheet/UICharacterSheet.uxml", 100, Panels.Equipment, showAll: false);
				Mount(ROOT + "Inventory/UIInventory.uxml", 101, Panels.Inventory, showAll: false);
			}

			/* The game's own path: the first panel registers the asset, and the Options slider calls
			 * Apply. Applied for every job so a 1.25 job cannot leak into the next clone. */
			UITKPanelScale.Register(settings);
			UITKPanelScale.Apply(job.Scale > 0f ? job.Scale : 1.0f);
		}

		private static void Mount(string uxml, int sortingOrder, Action<GameObject, UIDocument> populate, bool showAll)
		{
			GameObject host = new GameObject("Scale_" + Path.GetFileNameWithoutExtension(uxml)) { hideFlags = HideFlags.HideAndDontSave };
			hosts.Add(host);

			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.sortingOrder = sortingOrder;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);

			try
			{
				populate(host, document);
			}
			catch (Exception ex)
			{
				Exception root = ex;
				while (root.InnerException != null) { root = root.InnerException; }
				summary.AppendLine($"  populate {uxml} threw {root.GetType().Name}: {root.Message}");
			}

			if (showAll)
			{
				ShowAll(document.rootVisualElement, 0);
			}
		}

		private static void ShowAll(VisualElement element, int depth)
		{
			if (element == null || depth > 6) { return; }
			if (element.resolvedStyle.display == DisplayStyle.None)
			{
				element.style.display = DisplayStyle.Flex;
			}
			foreach (VisualElement child in element.Children())
			{
				ShowAll(child, depth + 1);
			}
		}

		private static void Measure(Job job)
		{
			VisualElement first = null;
			foreach (GameObject host in hosts)
			{
				first = host != null ? host.GetComponent<UIDocument>()?.rootVisualElement : null;
				if (first != null) { break; }
			}

			float ppp = first != null && first.panel != null ? first.scaledPixelsPerPoint : float.NaN;
			summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
				"{0}: screen {1}x{2}  mode {3}  reference {4}x{5}  panel {6:0.#}x{7:0.#} pts  {8:0.###} px/pt",
				job.Label, job.Width, job.Height, settings.scaleMode,
				settings.referenceResolution.x, settings.referenceResolution.y,
				first?.resolvedStyle.width ?? float.NaN, first?.resolvedStyle.height ?? float.NaN, ppp));

			string[] names = job.Scene == "hud"
				? new[] { "target-root", "party-root", "chat-root", "hotkey-list", "minimap-frame", "toast-stack" }
				: new[] { "eq-panel", "inv-panel" };

			foreach (string name in names)
			{
				VisualElement found = null;
				foreach (GameObject host in hosts)
				{
					VisualElement root = host != null ? host.GetComponent<UIDocument>()?.rootVisualElement : null;
					found = root?.Q(name) ?? root?.Q(className: name);
					if (found != null) { break; }
				}
				if (found == null)
				{
					summary.AppendLine($"    {name,-14} <missing>");
					continue;
				}
				Rect r = found.worldBound;
				summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
					"    {0,-14} pts (x {1:0.#}, y {2:0.#}) {3:0.#}x{4:0.#}   px {5:0}x{6:0}",
					name, r.x, r.y, r.width, r.height, r.width * ppp, r.height * ppp));
			}

			if (job.Scene == "hud")
			{
				foreach (GameObject host in hosts)
				{
					VisualElement root = host != null ? host.GetComponent<UIDocument>()?.rootVisualElement : null;
					VisualElement party = root?.Q(className: "party-panel");
					if (party != null)
					{
						Rect r = party.worldBound;
						summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
							"    {0,-14} pts (x {1:0.#}, y {2:0.#}) {3:0.#}x{4:0.#}", ".party-panel", r.x, r.y, r.width, r.height));
						break;
					}
				}
			}
		}

		private static void Capture(Job job)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(job.Width, job.Height, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, job.Width, job.Height), 0, 0);
				image.Apply();
				File.WriteAllBytes(Path.Combine(OUT_DIR, job.Label + ".png"), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void End()
		{
			foreach (GameObject host in hosts)
			{
				if (host != null) { UnityEngine.Object.DestroyImmediate(host); }
			}
			hosts.Clear();
			if (settings != null) { UnityEngine.Object.DestroyImmediate(settings); }
			settings = null;
		}

		private static void Finish()
		{
			EditorApplication.update -= Pump;
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.DestroyImmediate(texture);
			}
			string path = Path.Combine(OUT_DIR, "summary.txt");
			File.WriteAllText(path, summary.ToString());
			Debug.Log("[Scale] wrote " + path);
			EditorApplication.Exit(0);
		}
	}
}
