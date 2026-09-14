using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: the player's own buff/debuff strips with 0/1/2/6/12/30 icons at real screen
	/// sizes and interface scales, measuring every icon against the strip band and the resource bars.
	/// Icons are built by UITKBuffContainer's own CreateGroup, so the element tree is the shipped one.
	/// </summary>
	public static class BuffStripProbe
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string ROOT = "Assets/Scripts/Client/GUI/World/";
		private const string OUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders/interface-scaling";
		private const int SETTLE_FRAMES = 20;
		private const float BAND_BOTTOM = 106f;
		private const float ICON = 16f;

		private sealed class Job
		{
			public int Width;
			public int Height;
			public float Scale;
			public int Count;
			public bool Capture;
		}

		private static readonly List<Job> jobs = new List<Job>();
		private static readonly List<GameObject> hosts = new List<GameObject>();
		private static readonly Dictionary<string, UIDocument> byName = new Dictionary<string, UIDocument>();
		private static readonly StringBuilder log = new StringBuilder();
		private static readonly StringBuilder summary = new StringBuilder();
		private static RenderTexture texture;
		private static PanelSettings settings;
		private static BaseBuffTemplate template;
		private static Job current;
		private static int frames;
		private static int failures;
		private static string tag = "probe";

		public static void Run()
		{
			try
			{
				tag = Environment.GetEnvironmentVariable("FISHMMO_SCALE_TAG") ?? "probe";
				Directory.CreateDirectory(OUT_DIR);
				template = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>("Assets/Templates/Entity/Buffs/Minor Increase Armor.asset");

				(int w, int h)[] sizes = { (3440, 1440), (1200, 506), (1200, 675), (1200, 800), (1920, 1080), (2560, 1080) };
				float[] scales = { 1.0f, 0.8f, 1.25f };
				int[] counts = { 0, 1, 2, 6, 12, 30, 40 };
				foreach ((int w, int h) in sizes)
				{
					foreach (float scale in scales)
					{
						foreach (int count in counts)
						{
							jobs.Add(new Job
							{
								Width = w, Height = h, Scale = scale, Count = count,
								Capture = w == 3440 && (count == 1 || count == 2),
							});
						}
					}
				}

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Buff] setup failed: {ex}");
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
					host?.GetComponent<UIDocument>()?.rootVisualElement?.MarkDirtyRepaint();
				}
				if (++frames < SETTLE_FRAMES) { return; }

				Measure(current);
				if (current.Capture) { Capture(current); }
				End();
				current = null;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Buff] pump failed: {ex}");
				End();
				current = null;
			}
		}

		private static string Label(Job job)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0}x{1} scale {2:0.00} icons {3}", job.Width, job.Height, job.Scale, job.Count);
		}

		private static void Begin(Job job)
		{
			texture = new RenderTexture(job.Width, job.Height, 24, RenderTextureFormat.ARGB32);
			texture.Create();
			settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.clearColor = true;
			settings.colorClearValue = new Color(0.35f, 0.38f, 0.36f, 1f);

			Mount("UITKHealthBar", ROOT + "ResourceBar/UIResourceBar.uxml", typeof(UITKHealthBar));
			Mount("UITKManaBar", ROOT + "ResourceBar/UIResourceBar.uxml", typeof(UITKManaBar));
			Mount("UITKStaminaBar", ROOT + "ResourceBar/UIResourceBar.uxml", typeof(UITKStaminaBar));
			Mount("UITKCastBar", ROOT + "Ability/UICastBar.uxml", typeof(UITKCastBar));
			Mount("UITKBuff", ROOT + "Buff/UIBuff.uxml", typeof(UITKBuff));
			Mount("UITKDebuff", ROOT + "Buff/UIDebuff.uxml", typeof(UITKDebuff));

			foreach (string name in new[] { "UITKBuff", "UITKDebuff" })
			{
				UIDocument document = byName[name];
				UITKBuffContainer container = document.GetComponent<UITKBuffContainer>();
				VisualElement list = document.rootVisualElement.Q("buff-list");
				MethodInfo create = typeof(UITKBuffContainer).GetMethod("CreateGroup", BindingFlags.NonPublic | BindingFlags.Instance);
				for (int i = 0; i < job.Count; ++i)
				{
					object view = create.Invoke(container, new object[] { template });
					VisualElement groupRoot = (VisualElement)view.GetType().GetField("Root").GetValue(view);
					VisualElement fill = (VisualElement)view.GetType().GetField("Fill").GetValue(view);
					fill.style.height = Length.Percent(100f);
					list.Add(groupRoot);
				}

				// What ApplyEntry does after every add: wrap only past one row's capacity.
				typeof(UITKBuffContainer).GetMethod("UpdateWrap", BindingFlags.NonPublic | BindingFlags.Instance)
					?.Invoke(container, null);
			}

			UITKPanelScale.Register(settings);
			UITKPanelScale.Apply(job.Scale);
		}

		private static void Mount(string name, string uxml, Type type)
		{
			GameObject host = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
			hosts.Add(host);
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);
			UITKControl control = (UITKControl)host.AddComponent(type);
			control.Document = document;
			control.OnStarting();
			byName[name] = document;
		}

		private static string R(Rect r)
		{
			return string.Format(CultureInfo.InvariantCulture, "(x {0:0.##}..{1:0.##}, y {2:0.##}..{3:0.##}) {4:0.##}x{5:0.##}",
				r.xMin, r.xMax, r.yMin, r.yMax, r.width, r.height);
		}

		private static void Measure(Job job)
		{
			VisualElement anyRoot = byName["UITKBuff"].rootVisualElement;
			float panelW = anyRoot.layout.width;
			float panelH = anyRoot.layout.height;
			float bandTop = panelH - BAND_BOTTOM - ICON;
			float bandBottom = panelH - BAND_BOTTOM;

			Rect bars = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
			foreach (string name in new[] { "UITKHealthBar", "UITKManaBar", "UITKStaminaBar" })
			{
				Rect r = byName[name].rootVisualElement.Q("bar-root").worldBound;
				bars = Rect.MinMaxRect(Mathf.Min(bars.xMin, r.xMin), Mathf.Min(bars.yMin, r.yMin), Mathf.Max(bars.xMax, r.xMax), Mathf.Max(bars.yMax, r.yMax));
			}

			log.AppendLine(string.Format(CultureInfo.InvariantCulture, "===== {0}  panel {1:0.##}x{2:0.##}  {3:0.###} px/pt  band y {4:0.##}..{5:0.##}  bars {6}",
				Label(job), panelW, panelH, anyRoot.scaledPixelsPerPoint, bandTop, bandBottom, R(bars)));

			int bad = 0;
			foreach (string name in new[] { "UITKBuff", "UITKDebuff" })
			{
				VisualElement root = byName[name].rootVisualElement;
				VisualElement container = root.Q("buff-root");
				VisualElement list = root.Q("buff-list");
				log.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-10} container {1} layout {2}  list {3} layout {4}",
					name, R(container.worldBound), R(container.layout), R(list.worldBound), R(list.layout)));

				int i = 0;
				foreach (VisualElement icon in list.Children())
				{
					Rect r = icon.worldBound;
					// Past 33 icons a second row is intended; it must grow UP from the band, never down.
					bool inBand = job.Count > 33
						? r.yMax <= bandBottom + 0.5f
						: r.yMin >= bandTop - 0.5f && r.yMax <= bandBottom + 0.5f;
					bool hitsBars = r.Overlaps(bars);
					// The rows of 30 per side fit one line; any extra line is itself a defect only past 33.
					if (!inBand || hitsBars) { ++bad; }
					if (job.Count <= 2 || !inBand || hitsBars)
					{
						log.AppendLine(string.Format(CultureInfo.InvariantCulture, "    icon {0,2} {1} {2}{3}", i, R(r),
							inBand ? "in band" : "OFF BAND", hitsBars ? " HITS BARS" : ""));
					}
					++i;
				}
			}

			if (bad > 0) { ++failures; }
			summary.AppendLine($"{Label(job)}: {(bad == 0 ? "ok" : bad + " icon(s) off band / on bars")}");
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
				string file = string.Format(CultureInfo.InvariantCulture, "{0}-buffstrip-{4}icon-{1}x{2}{3}.png", tag, job.Width, job.Height,
					Mathf.Approximately(job.Scale, 1f) ? "" : "-scale" + job.Scale.ToString("0.00", CultureInfo.InvariantCulture), job.Count);
				File.WriteAllBytes(Path.Combine(OUT_DIR, file), image.EncodeToPNG());
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
			string path = Path.Combine(OUT_DIR, tag + "-buffstrip.txt");
			File.WriteAllText(path, "failing states: " + failures + "\n\n" + summary + "\n" + log);
			Debug.Log("[Buff] wrote " + path);
			EditorApplication.Exit(0);
		}
	}
}
