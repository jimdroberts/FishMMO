using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: the HUD with a FULL party over a noisy world-like background, at real
	/// screen sizes and interface scales, measuring the party frame's rows and the resource bar
	/// group's centre. Output is tagged by FISHMMO_SCALE_TAG (before/after).
	/// </summary>
	public static class PartyBarsRender
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string ROOT = "Assets/Scripts/Client/GUI/World/";
		private const string OUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders/interface-scaling";
		private const int SETTLE_FRAMES = 45;

		private sealed class Job
		{
			public int Width;
			public int Height;
			public float Scale;
		}

		private static readonly (string uxml, Type type)[] hud =
		{
			(ROOT + "ResourceBar/UIResourceBar.uxml", typeof(UITKHealthBar)),
			(ROOT + "ResourceBar/UIResourceBar.uxml", typeof(UITKManaBar)),
			(ROOT + "ResourceBar/UIResourceBar.uxml", typeof(UITKStaminaBar)),
			(ROOT + "Buff/UIBuff.uxml", typeof(UITKBuff)),
			(ROOT + "Buff/UIDebuff.uxml", typeof(UITKDebuff)),
			(ROOT + "HotkeyBar/UIHotkeyBar.uxml", typeof(UITKHotkeyBar)),
			(ROOT + "Ability/UICastBar.uxml", typeof(UITKCastBar)),
			(ROOT + "Minimap/UIMinimap.uxml", typeof(UITKMinimap)),
			(ROOT + "Target/UITarget.uxml", typeof(UITKTarget)),
			(ROOT + "Toast/UIToast.uxml", typeof(UITKToast)),
			(ROOT + "Chat/UIChat.uxml", typeof(UITKChat)),
		};

		private static readonly List<Job> jobs = new List<Job>
		{
			new Job { Width = 1920, Height = 1080, Scale = 1.0f },
			new Job { Width = 3440, Height = 1440, Scale = 1.0f },
			new Job { Width = 1920, Height = 1080, Scale = 0.8f },
			new Job { Width = 1920, Height = 1080, Scale = 1.25f },
			new Job { Width = 2560, Height = 1080, Scale = 1.0f },
		};

		private static readonly List<GameObject> hosts = new List<GameObject>();
		private static readonly Dictionary<string, UIDocument> byName = new Dictionary<string, UIDocument>();
		private static readonly StringBuilder summary = new StringBuilder();
		private static RenderTexture texture;
		private static PanelSettings settings;
		private static Texture2D noise;
		private static Job current;
		private static int frames;
		private static string tag = "render";

		public static void Run()
		{
			try
			{
				tag = Environment.GetEnvironmentVariable("FISHMMO_SCALE_TAG") ?? "render";
				Directory.CreateDirectory(OUT_DIR);
				Seed.All();
				noise = BuildNoise();
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[PartyBars] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static Texture2D BuildNoise()
		{
			const int size = 192;
			Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
			System.Random rng = new System.Random(1234);
			for (int y = 0; y < size; ++y)
			{
				for (int x = 0; x < size; ++x)
				{
					// Mid-grey noise with broad light/dark bands and a few saturated patches, like foliage and sky.
					float band = 0.5f + 0.25f * Mathf.Sin(x * 0.07f) * Mathf.Cos(y * 0.05f);
					float n = (float)rng.NextDouble() * 0.35f - 0.175f;
					float v = Mathf.Clamp01(band + n);
					Color c = new Color(v, v, v, 1f);
					if (((x / 24) + (y / 20)) % 7 == 0) { c = Color.Lerp(c, new Color(0.2f, 0.55f, 0.25f), 0.6f); }
					if (((x / 32) * 3 + (y / 16)) % 11 == 0) { c = Color.Lerp(c, new Color(0.85f, 0.8f, 0.6f), 0.6f); }
					tex.SetPixel(x, y, c);
				}
			}
			tex.Apply();
			return tex;
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
					host?.GetComponent<UIDocument>()?.rootVisualElement?.MarkDirtyRepaint();
				}
				if (++frames < SETTLE_FRAMES) { return; }

				Measure(current);
				Capture(current);
				End();
				current = null;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[PartyBars] pump failed: {ex}");
				summary.AppendLine("FAILED " + ex.Message);
				End();
				current = null;
			}
		}

		private static string Label(Job job)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0}-party-bars-{1}x{2}{3}", tag, job.Width, job.Height,
				Mathf.Approximately(job.Scale, 1f) ? "" : "-scale" + job.Scale.ToString("0.00", CultureInfo.InvariantCulture));
		}

		private static void Begin(Job job)
		{
			texture = new RenderTexture(job.Width, job.Height, 24, RenderTextureFormat.ARGB32);
			texture.Create();
			settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.clearColor = true;
			settings.colorClearValue = Color.gray;
			settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;

			// The "world": a full-screen noise image behind everything.
			GameObject bgHost = new GameObject("PB_Background") { hideFlags = HideFlags.HideAndDontSave };
			hosts.Add(bgHost);
			UIDocument bg = bgHost.AddComponent<UIDocument>();
			bg.panelSettings = settings;
			bg.sortingOrder = -100;
			VisualElement image = new VisualElement();
			image.style.position = Position.Absolute;
			image.style.left = 0; image.style.top = 0; image.style.right = 0; image.style.bottom = 0;
			image.style.backgroundImage = new StyleBackground(noise);
			bg.rootVisualElement.Add(image);

			foreach ((string uxml, Type type) in hud)
			{
				Mount(type.Name, uxml, (h, d) =>
				{
					UITKControl control = (UITKControl)h.AddComponent(type);
					control.Document = d;
					control.OnStarting();
				}, showAll: true);
			}

			Mount("UITKParty", ROOT + "Party/UIParty.uxml", (h, d) =>
			{
				Panels.Party(h, d);
				h.GetComponent<UITKParty>().OnPartyAddMember(1006, PartyRank.Member, 0.6f);
			}, showAll: false);

			foreach (string name in new[] { "UITKBuff", "UITKDebuff" })
			{
				VisualElement list = byName[name].rootVisualElement.Q("buff-list");
				for (int i = 0; list != null && i < 6; ++i)
				{
					VisualElement group = new VisualElement();
					group.AddToClassList("buff-group");
					VisualElement icon = new VisualElement();
					icon.AddToClassList("buff-group__icon");
					group.Add(icon);
					list.Add(group);
				}
			}

			UITKPanelScale.Register(settings);
			UITKPanelScale.Apply(job.Scale);
		}

		private static void Mount(string name, string uxml, Action<GameObject, UIDocument> populate, bool showAll)
		{
			GameObject host = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
			hosts.Add(host);
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);
			byName[name] = document;
			try
			{
				populate(host, document);
			}
			catch (Exception ex)
			{
				summary.AppendLine($"  populate {name} threw {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
			}
			if (showAll)
			{
				ShowAll(document.rootVisualElement, 0);
			}
		}

		private static void ShowAll(VisualElement element, int depth)
		{
			if (element == null || depth > 6) { return; }
			if (element.resolvedStyle.display == DisplayStyle.None) { element.style.display = DisplayStyle.Flex; }
			foreach (VisualElement child in element.Children()) { ShowAll(child, depth + 1); }
		}

		private static string R(Rect r)
		{
			return string.Format(CultureInfo.InvariantCulture, "(x {0:0.#}..{1:0.#}, y {2:0.#}..{3:0.#}) {4:0.#}x{5:0.#}",
				r.xMin, r.xMax, r.yMin, r.yMax, r.width, r.height);
		}

		private static void Measure(Job job)
		{
			VisualElement partyDocRoot = byName["UITKParty"].rootVisualElement;
			float panelW = partyDocRoot.resolvedStyle.width;
			float panelH = partyDocRoot.resolvedStyle.height;
			summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
				"===== {0}: screen {1}x{2} scale {3:0.00}  reference {4}x{5}  panel {6:0.#}x{7:0.#}  {8:0.###} px/pt",
				Label(job), job.Width, job.Height, job.Scale, settings.referenceResolution.x, settings.referenceResolution.y,
				panelW, panelH, partyDocRoot.scaledPixelsPerPoint));

			// Party frame.
			VisualElement party = partyDocRoot.Q("party-root");
			ScrollView scroll = partyDocRoot.Q<ScrollView>("party-scroll");
			Rect partyRect = party.worldBound;
			Rect viewport = scroll != null ? scroll.contentViewport.worldBound : partyRect;
			summary.AppendLine($"  party-root       {R(partyRect)}   inside screen: {(partyRect.yMax <= panelH + 0.5f && partyRect.yMin >= -0.5f ? "yes" : "NO")}");
			summary.AppendLine($"  party viewport   {R(viewport)}");
			int rows = 0, whole = 0;
			partyDocRoot.Query(className: "party-member").ForEach(row =>
			{
				++rows;
				Rect r = row.worldBound;
				bool full = r.height >= 52.5f && r.yMin >= viewport.yMin - 0.5f && r.yMax <= viewport.yMax + 0.5f &&
					r.yMin >= partyRect.yMin - 0.5f && r.yMax <= partyRect.yMax + 0.5f &&
					r.yMin >= -0.5f && r.yMax <= panelH + 0.5f;
				if (full) { ++whole; }
				summary.AppendLine($"    row {rows}  {R(r)}  {(full ? "whole" : "CLIPPED")}");
			});
			summary.AppendLine($"  party rows: {rows}, wholly visible: {whole}");
			VisualElement firstBar = partyDocRoot.Q(className: "party-bar");
			if (firstBar != null)
			{
				Rect b = firstBar.worldBound;
				float ppp = partyDocRoot.scaledPixelsPerPoint;
				summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
					"  member bar       {0:0.#}x{1:0.#} pts = {2:0.#}x{3:0.#} px", b.width, b.height, b.width * ppp, b.height * ppp));
			}

			VisualElement target = byName["UITKTarget"].rootVisualElement.Q("target-root");
			if (target != null)
			{
				summary.AppendLine($"  target-root      {R(target.worldBound)}");
			}

			// Resource bars.
			float minX = float.MaxValue, maxX = float.MinValue;
			foreach (string name in new[] { "UITKHealthBar", "UITKManaBar", "UITKStaminaBar" })
			{
				Rect r = byName[name].rootVisualElement.Q("bar-root").worldBound;
				summary.AppendLine($"  {name,-16} {R(r)}");
				minX = Mathf.Min(minX, r.xMin);
				maxX = Mathf.Max(maxX, r.xMax);
			}
			float centre = (minX + maxX) * 0.5f;
			summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
				"  bar group x {0:0.#}..{1:0.#}, centre {2:0.##} vs panel centre {3:0.##}  offset {4:+0.##;-0.##;0}",
				minX, maxX, centre, panelW * 0.5f, centre - panelW * 0.5f));

			Rect cast = byName["UITKCastBar"].rootVisualElement.Q("castbar-root").worldBound;
			summary.AppendLine(string.Format(CultureInfo.InvariantCulture, "  cast bar         {0}  centre offset {1:+0.##;-0.##;0}",
				R(cast), cast.center.x - panelW * 0.5f));
			Rect buff = byName["UITKBuff"].rootVisualElement.Q("buff-root").worldBound;
			Rect debuff = byName["UITKDebuff"].rootVisualElement.Q("buff-root").worldBound;
			summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
				"  buff strip right edge {0:0.#} / debuff strip left edge {1:0.#}  (panel centre {2:0.#})",
				buff.xMax, debuff.xMin, panelW * 0.5f));
			Rect hotkeys = byName["UITKHotkeyBar"].rootVisualElement.Q("hotkey-list").worldBound;
			summary.AppendLine(string.Format(CultureInfo.InvariantCulture, "  hotkey-list      {0}  centre offset {1:+0.##;-0.##;0}",
				R(hotkeys), hotkeys.center.x - panelW * 0.5f));
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
				File.WriteAllBytes(Path.Combine(OUT_DIR, Label(job) + ".png"), image.EncodeToPNG());
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
			byName.Clear();
			if (settings != null) { UnityEngine.Object.DestroyImmediate(settings); }
			if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
			settings = null;
			texture = null;
		}

		private static void Finish()
		{
			EditorApplication.update -= Pump;
			UITKPanelScale.Apply(1.0f);
			string path = Path.Combine(OUT_DIR, tag + "-partybars.txt");
			File.WriteAllText(path, summary.ToString());
			Debug.Log("[PartyBars] wrote " + path);
			EditorApplication.Exit(0);
		}
	}
}
