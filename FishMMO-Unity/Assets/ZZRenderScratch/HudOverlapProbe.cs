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
	/// Throwaway harness: measures where EVERY HUD panel resolves at a given viewport height, so
	/// "these overlap" is a number rather than a guess.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each panel keeps its own UIDocument, exactly as in the World GUI scene, and all of them
	/// share ONE PanelSettings clone pointed at a RenderTexture. That matters: PanelSettings uses
	/// ScaleWithScreenSize with match = WIDTH, so the panel is always exactly 1200 units wide and
	/// its height is 1200 * (screenH / screenW) — 675 at 16:9, 800 at 16:10. Every vertical offset
	/// in the HUD is therefore aspect-dependent, which is why the height is a command-line
	/// argument and why this has to be run more than once.
	/// </para>
	/// <para>
	/// Measuring the panel root would be useless: several HUD roots fill the viewport and only
	/// their children draw. Each mount names the selector for the element that actually paints,
	/// and a selector that is missing or collapsed is reported as not drawn rather than folded in
	/// as a full-screen rectangle.
	/// </para>
	/// </remarks>
	public static class HudOverlapProbe
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string ROOT = "Assets/Scripts/Client/GUI/World/";
		private const int WIDTH = 1200;
		private const int SETTLE_FRAMES = 40;
		private const string OUT_DIR = "/home/jim/Dev/FishMMO-Dev/HudProbe";

		/// <summary>One panel to mount, and the element inside it that actually draws.</summary>
		private sealed class Mount
		{
			public string Name;
			public string Uxml;
			public string Selector;
			public Type PanelType;
			/// <summary>Skip <c>OnStarting</c> and measure the UXML/USS layout alone.</summary>
			public bool LayoutOnly;
			/// <summary>Measured, but excluded from the overlap matrix — see the crosshair mount.</summary>
			public bool IgnoreOverlap;
		}

		/// <summary>A mounted panel and its resolved drawing rectangle.</summary>
		private sealed class Panel
		{
			public string Name;
			public VisualElement Root;
			public string Selector;
			public VisualElement Measured;
			public bool Failed;
			public string Error;
			public bool IgnoreOverlap;
		}

		/// <summary>A buff/debuff strip, whose icon count this harness drives directly.</summary>
		private sealed class Strip
		{
			public string Name;
			public Panel Panel;
			public VisualElement List;
			public int Groups;
		}

		private static readonly List<Mount> mounts = new List<Mount>
		{
			new Mount { Name = "UIHealthBar",  Uxml = ROOT + "ResourceBar/UIResourceBar.uxml", Selector = "bar-root",        PanelType = typeof(UITKHealthBar) },
			new Mount { Name = "UIManaBar",    Uxml = ROOT + "ResourceBar/UIResourceBar.uxml", Selector = "bar-root",        PanelType = typeof(UITKManaBar) },
			new Mount { Name = "UIStaminaBar", Uxml = ROOT + "ResourceBar/UIResourceBar.uxml", Selector = "bar-root",        PanelType = typeof(UITKStaminaBar) },
			new Mount { Name = "UIBuff",       Uxml = ROOT + "Buff/UIBuff.uxml",                Selector = "buff-root",       PanelType = typeof(UITKBuff) },
			new Mount { Name = "UIDebuff",     Uxml = ROOT + "Buff/UIDebuff.uxml",              Selector = "buff-root",       PanelType = typeof(UITKDebuff) },
			new Mount { Name = "UIHotkeyBar",  Uxml = ROOT + "HotkeyBar/UIHotkeyBar.uxml",      Selector = "hotkey-list",     PanelType = typeof(UITKHotkeyBar) },
			new Mount { Name = "UICastBar",    Uxml = ROOT + "Ability/UICastBar.uxml",          Selector = "castbar-root",    PanelType = typeof(UITKCastBar) },
			new Mount { Name = "UIMinimap",    Uxml = ROOT + "Minimap/UIMinimap.uxml",          Selector = "minimap-frame",   PanelType = typeof(UITKMinimap) },
			/* Measures crosshair-icon, not crosshair-root: the root is a flex-grow container that
			 * fills the viewport so it can centre its child, so naming it reports every other panel
			 * as an overlap. LayoutOnly because OnStarting throws in this harness (the crosshair
			 * hides itself while Cursor.visible is true, which the editor leaves set) — the icon's
			 * 8x8 USS default is the same size an untouched install gets, since that is what
			 * ClientCrosshairSettings starts at.
			 *
			 * IgnoreOverlap because a crosshair is SUPPOSED to sit over whatever is under it — it
			 * marks the centre of the screen, which is where the target frame, the bars and the
			 * cast bar all are. Reporting those as collisions would be reporting the design. What
			 * matters for it is only that it is centred, which is checked separately. */
			new Mount { Name = "UICrosshair",  Uxml = ROOT + "Crosshair/UICrosshair.uxml",      Selector = "crosshair-icon",  PanelType = typeof(UITKCrosshair), LayoutOnly = true, IgnoreOverlap = true },
			new Mount { Name = "UITarget",     Uxml = ROOT + "Target/UITarget.uxml",            Selector = "target-root",     PanelType = typeof(UITKTarget) },
			new Mount { Name = "UIToast",      Uxml = ROOT + "Toast/UIToast.uxml",              Selector = "toast-stack",     PanelType = typeof(UITKToast) },
			new Mount { Name = "UIParty",      Uxml = ROOT + "Party/UIParty.uxml",              Selector = "party-root",      PanelType = typeof(UITKParty) },
			new Mount { Name = "UIChat",       Uxml = ROOT + "Chat/UIChat.uxml",                Selector = "chat-root",       PanelType = typeof(UITKChat) },
			new Mount { Name = "UIArenaHud",   Uxml = ROOT + "Arena/UIArenaHud.uxml",           Selector = "arenahud-root",   PanelType = typeof(UITKArenaHud) },
		};

		private static PanelSettings settings;
		private static RenderTexture texture;
		private static readonly List<GameObject> hosts = new List<GameObject>();
		private static readonly List<Panel> panels = new List<Panel>();
		private static readonly List<Strip> strips = new List<Strip>();

		private static readonly StringBuilder log = new StringBuilder();
		private static int height = 800;
		private static int frames;
		private static int stage;

		[MenuItem("FishMMO/UI Toolkit/Probe HUD Overlap")]
		public static void Run()
		{
			try
			{
				ReadArguments();
				Directory.CreateDirectory(OUT_DIR);

				texture = new RenderTexture(WIDTH, height, 24, RenderTextureFormat.ARGB32);
				texture.Create();

				settings = UnityEngine.Object.Instantiate(
					AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;
				settings.clearColor = true;
				settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);

				foreach (Mount mount in mounts)
				{
					MountPanel(mount);
				}

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Hud] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		/// <summary>Viewport height comes from the command line; the width is always 1200.</summary>
		private static void ReadArguments()
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; ++i)
			{
				if (string.Equals(args[i], "-hudHeight", StringComparison.Ordinal) &&
					int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
				{
					height = parsed;
				}
			}
		}

		private static void MountPanel(Mount mount)
		{
			Panel panel = new Panel
			{
				Name = mount.Name,
				Selector = mount.Selector,
				IgnoreOverlap = mount.IgnoreOverlap,
			};

			try
			{
				GameObject host = new GameObject(mount.Name) { hideFlags = HideFlags.HideAndDontSave };
				hosts.Add(host);

				UIDocument document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(mount.Uxml);

				UITKControl control = (UITKControl)host.AddComponent(mount.PanelType);
				control.Document = document;
				if (!mount.LayoutOnly)
				{
					control.OnStarting();
				}

				panel.Root = document.rootVisualElement;

				/* Several HUD panels start collapsed until their controller shows them, and a
				 * document mounted by hand never gets that call. */
				ShowAll(panel.Root, 0);

				panel.Measured = panel.Root.Q(mount.Selector);
				if (panel.Measured == null)
				{
					panel.Failed = true;
					panel.Error = $"no element named '{mount.Selector}'";
				}
			}
			catch (Exception ex)
			{
				panel.Failed = true;
				panel.Error = ex.GetType().Name + ": " + ex.Message;
			}

			panels.Add(panel);

			if (mount.PanelType == typeof(UITKBuff) || mount.PanelType == typeof(UITKDebuff))
			{
				strips.Add(new Strip
				{
					Name = mount.Name,
					Panel = panel,
					List = panel.Root?.Q("buff-list"),
				});
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

		/// <summary>Adds the elements UITKBuffContainer builds for one buff, without the model.</summary>
		private static void AddIcons(Strip strip, int count)
		{
			if (strip.List == null) { return; }

			for (int i = 0; i < count; ++i)
			{
				VisualElement group = new VisualElement();
				group.AddToClassList("buff-group");

				VisualElement fill = new VisualElement();
				fill.AddToClassList("buff-group__fill");
				group.Add(fill);

				VisualElement icon = new VisualElement();
				icon.AddToClassList("buff-group__icon");
				group.Add(icon);

				Label label = new Label(string.Empty);
				label.AddToClassList("buff-group__label");
				group.Add(label);

				strip.List.Add(group);
				++strip.Groups;
			}
		}

		private static void Pump()
		{
			try
			{
				if (++frames < SETTLE_FRAMES)
				{
					foreach (GameObject host in hosts)
					{
						host.GetComponent<UIDocument>()?.rootVisualElement?.MarkDirtyRepaint();
					}
					return;
				}
				frames = 0;

				switch (stage)
				{
					case 0:
						Log("empty strips");
						foreach (Strip strip in strips) { AddIcons(strip, 6); }
						break;

					case 1:
						Log("6 icons per strip");
						foreach (Strip strip in strips) { AddIcons(strip, 6); }
						break;

					case 2:
						Log("12 icons per strip");
						foreach (Strip strip in strips) { AddIcons(strip, 18); }
						break;

					case 3:
						Log("30 icons per strip");
						Capture();
						Finish();
						return;
				}

				++stage;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Hud] pump failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static void Log(string what)
		{
			log.AppendLine($"===== {what} =====");
			log.AppendLine($"viewport {WIDTH}x{height}  (panel width is always {WIDTH}; height is aspect dependent)");
			log.AppendLine();

			log.AppendLine("-- measured rects --");
			foreach (Panel panel in panels)
			{
				if (panel.Failed)
				{
					log.AppendLine($"{panel.Name,-14} NOT MOUNTED  [{panel.Error}]");
					continue;
				}
				log.AppendLine($"{panel.Name,-14} {Bounds(panel.Measured),-52} <- {panel.Selector}");
			}

			foreach (Strip strip in strips)
			{
				log.AppendLine($"{strip.Name,-14} groups {strip.Groups}, list {Bounds(strip.List)}");
			}

			log.AppendLine();
			log.AppendLine("-- overlapping pairs --");
			int found = 0;
			for (int i = 0; i < panels.Count; ++i)
			{
				if (panels[i].IgnoreOverlap) { continue; }

				for (int j = i + 1; j < panels.Count; ++j)
				{
					if (panels[j].IgnoreOverlap) { continue; }

					Rect a = RectOf(panels[i]);
					Rect b = RectOf(panels[j]);
					if (!Drawn(a) || !Drawn(b)) { continue; }

					string overlap = Overlap(a, b);
					if (overlap == "clear") { continue; }

					++found;
					log.AppendLine($"  {panels[i].Name} vs {panels[j].Name}: {overlap}");
				}
			}
			if (found == 0)
			{
				log.AppendLine("  none");
			}

			/* Overlap-exempt panels are judged on where they sit instead. Centring is the whole
			 * contract for a crosshair: an offset here is a sighting error at every range. */
			log.AppendLine();
			log.AppendLine("-- centring (panels exempt from the overlap matrix) --");
			foreach (Panel panel in panels)
			{
				if (!panel.IgnoreOverlap || panel.Failed) { continue; }

				Rect rect = RectOf(panel);
				float dx = rect.center.x - (WIDTH * 0.5f);
				float dy = rect.center.y - (height * 0.5f);
				log.AppendLine(string.Format(CultureInfo.InvariantCulture,
					"  {0}: centre ({1:0.#}, {2:0.#}) vs viewport centre ({3:0.#}, {4:0.#})  offset ({5:+0.#;-0.#;0}, {6:+0.#;-0.#;0})",
					panel.Name, rect.center.x, rect.center.y, WIDTH * 0.5f, height * 0.5f, dx, dy));
			}

			log.AppendLine();
			log.AppendLine("-- not drawn at this size --");
			foreach (Panel panel in panels)
			{
				if (panel.Failed) { continue; }
				if (!Drawn(RectOf(panel)))
				{
					log.AppendLine($"  {panel.Name} ({panel.Selector})");
				}
			}

			log.AppendLine();
			log.AppendLine("-- detail --");
			foreach (Panel panel in panels)
			{
				if (panel.Failed || panel.Root == null) { continue; }
				log.AppendLine($"  {panel.Name}:");
				LogTree(panel.Root, 2);
			}

			log.AppendLine();
		}

		private static void LogTree(VisualElement element, int depth)
		{
			if (element == null || depth > 2) { return; }

			foreach (VisualElement child in element.Children())
			{
				Rect rect = child.worldBound;
				if (rect.width <= 0f || rect.height <= 0f)
				{
					LogTree(child, depth + 1);
					continue;
				}

				log.AppendLine($"{new string(' ', depth * 4)}{(string.IsNullOrEmpty(child.name) ? "<anon>" : child.name),-22} {Bounds(child)}");
				LogTree(child, depth + 1);
			}
		}

		/// <summary>The rect a panel is judged by: the element that paints, or nothing.</summary>
		private static Rect RectOf(Panel panel)
		{
			if (panel.Failed || panel.Measured == null) { return new Rect(); }
			return panel.Measured.worldBound;
		}

		private static bool Drawn(Rect rect)
		{
			return rect.width > 0f && rect.height > 0f;
		}

		private static string Bounds(VisualElement element)
		{
			if (element == null) { return "<missing>"; }
			return Format(element.worldBound);
		}

		private static string Format(Rect rect)
		{
			return string.Format(CultureInfo.InvariantCulture,
				"(x {0:0.#}..{1:0.#}, y {2:0.#}..{3:0.#}) {4:0.#}x{5:0.#}",
				rect.xMin, rect.xMax, rect.yMin, rect.yMax, rect.width, rect.height);
		}

		private static string Overlap(Rect ra, Rect rb)
		{
			float x = Mathf.Min(ra.xMax, rb.xMax) - Mathf.Max(ra.xMin, rb.xMin);
			float y = Mathf.Min(ra.yMax, rb.yMax) - Mathf.Max(ra.yMin, rb.yMin);

			return x > 0f && y > 0f
				? string.Format(CultureInfo.InvariantCulture, "OVERLAP {0:0.#}x{1:0.#}px", x, y)
				: "clear";
		}

		private static void Capture()
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(WIDTH, height, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, WIDTH, height), 0, 0);
				image.Apply();
				File.WriteAllBytes(Path.Combine(OUT_DIR, $"hud-{WIDTH}x{height}.png"), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void Finish()
		{
			string path = Path.Combine(OUT_DIR, $"rects-{WIDTH}x{height}.txt");
			File.WriteAllText(path, log.ToString());
			Debug.Log("[Hud] wrote " + path);

			foreach (GameObject host in hosts)
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
			hosts.Clear();

			if (settings != null) { UnityEngine.Object.DestroyImmediate(settings); }
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.DestroyImmediate(texture);
			}

			EditorApplication.update -= Pump;
			EditorApplication.Exit(0);
		}
	}
}
