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
	/// Throwaway harness: for every flex-wrap container in a set of populated windows, reports how
	/// many children fit on the first row at px/pt 1.0 and at real screen sizes under
	/// ScaleWithScreenSize, so a grid that only fits at one point per pixel shows up as a number.
	/// </summary>
	public static class GridFitProbe
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string ROOT = "Assets/Scripts/Client/GUI/World/";
		private const string OUT = "/home/jim/Dev/FishMMO-Dev/PanelRenders/interface-scaling/gridfit.txt";
		private const int SETTLE_FRAMES = 30;

		private static readonly (int w, int h)[] sizes =
		{
			(1200, 675), (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160),
		};

		private static readonly (string uxml, Action<GameObject, UIDocument> populate)[] windows =
		{
			(ROOT + "Inventory/UIInventory.uxml", Panels.Inventory),
			(ROOT + "Bank/UIBank.uxml", Panels.Bank),
			(ROOT + "CharacterSheet/UICharacterSheet.uxml", Panels.Equipment),
			(ROOT + "Party/UIParty.uxml", Panels.Party),
			(ROOT + "Trade/UITrade.uxml", (h, d) => TradePanelPopulator.Trade(h, d)),
			(ROOT + "Achievement/UIAchievements.uxml", Panels.Achievements),
		};

		private static readonly StringBuilder log = new StringBuilder();
		private static readonly List<GameObject> hosts = new List<GameObject>();
		private static RenderTexture texture;
		private static PanelSettings settings;
		private static int index = -1;
		private static int frames;

		public static void Run()
		{
			try
			{
				Seed.All();
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Grid] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static void Pump()
		{
			try
			{
				if (index >= 0)
				{
					foreach (GameObject host in hosts)
					{
						UITKControl control = host != null ? host.GetComponent<UITKControl>() : null;
						if (control != null)
						{
							try { Panels.StartWhenReady(control); Panels.Tick(control); }
							catch (Exception) { /* a panel's own tick needing live state is not what is measured */ }
						}
						host?.GetComponent<UIDocument>()?.rootVisualElement?.MarkDirtyRepaint();
					}
					if (++frames < SETTLE_FRAMES) { return; }
					Measure(sizes[index]);
					Teardown();
				}

				if (++index >= sizes.Length)
				{
					File.WriteAllText(OUT, log.ToString());
					Debug.Log("[Grid] wrote " + OUT);
					EditorApplication.update -= Pump;
					EditorApplication.Exit(0);
					return;
				}

				Begin(sizes[index]);
				frames = 0;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Grid] pump failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static void Begin((int w, int h) size)
		{
			texture = new RenderTexture(size.w, size.h, 24, RenderTextureFormat.ARGB32);
			texture.Create();
			settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;

			int order = 100;
			foreach ((string uxml, Action<GameObject, UIDocument> populate) in windows)
			{
				GameObject host = new GameObject("Grid_" + Path.GetFileNameWithoutExtension(uxml)) { hideFlags = HideFlags.HideAndDontSave };
				hosts.Add(host);
				UIDocument document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.sortingOrder = order++;
				document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);
				try
				{
					populate(host, document);
				}
				catch (Exception ex)
				{
					log.AppendLine($"populate {uxml} threw {ex.GetBaseException().Message}");
				}
			}
		}

		private static void Measure((int w, int h) size)
		{
			VisualElement first = hosts[0].GetComponent<UIDocument>().rootVisualElement;
			log.AppendLine(string.Format(CultureInfo.InvariantCulture, "===== {0}x{1}  {2:0.###} px/pt  panel {3:0.#}x{4:0.#}",
				size.w, size.h, first.scaledPixelsPerPoint, first.resolvedStyle.width, first.resolvedStyle.height));

			foreach (GameObject host in hosts)
			{
				VisualElement root = host != null ? host.GetComponent<UIDocument>()?.rootVisualElement : null;
				if (root == null)
				{
					log.AppendLine($"  {host?.name ?? "?"}: no root");
					continue;
				}
				root.Query<VisualElement>().ForEach(element =>
				{
					if (element.resolvedStyle.flexWrap != Wrap.Wrap || element.childCount < 2) { return; }

					float y0 = element[0].layout.y;
					int firstRow = 0;
					float childOuter = 0f;
					foreach (VisualElement child in element.Children())
					{
						if (child.resolvedStyle.display == DisplayStyle.None) { continue; }
						if (Mathf.Abs(child.layout.y - y0) > 0.5f) { break; }
						++firstRow;
						childOuter += child.layout.width + child.resolvedStyle.marginLeft + child.resolvedStyle.marginRight;
					}

					log.AppendLine(string.Format(CultureInfo.InvariantCulture,
						"  {0,-20} {1,-28} children {2,3}  first row {3,2}  content {4:0.###}  row needs {5:0.###}  slack {6:0.###}",
						host.name, Describe(element), element.childCount, firstRow, element.contentRect.width, childOuter,
						element.contentRect.width - childOuter));
				});
			}
		}

		private static string Describe(VisualElement element)
		{
			string cls = string.Join(".", element.GetClasses());
			return string.IsNullOrEmpty(element.name) ? "." + cls : "#" + element.name + (cls.Length > 0 ? " ." + cls : "");
		}

		private static void Teardown()
		{
			foreach (GameObject host in hosts)
			{
				if (host != null) { UnityEngine.Object.DestroyImmediate(host); }
			}
			hosts.Clear();
			if (settings != null) { UnityEngine.Object.DestroyImmediate(settings); }
			if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
			settings = null;
			texture = null;
		}
	}
}
