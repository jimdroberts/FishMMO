using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: renders every UI panel, populated with live data wherever the panel
	/// exposes a seam this harness can drive.
	/// </summary>
	/// <remarks>
	/// <para>The file name says which state a panel was captured in: <b>-live</b> means the panel's
	/// own C# ran and was fed data through the same entry points the network layer uses;
	/// <b>-chrome</b> means UXML only.</para>
	/// <para><b>One render target for the whole run.</b> Creating and destroying a RenderTexture per
	/// capture crashed the software GL rasteriser — SIGSEGV inside
	/// <c>GfxDeviceGLES::DrawBufferRanges</c> — roughly a fifth of the way through sixty captures.
	/// A single reused surface is both stabler and much faster.</para>
	/// <para><b>Resumable.</b> A capture whose PNG already exists is skipped, so a run that dies
	/// continues where it stopped rather than starting over.</para>
	/// </remarks>
	public static class AllPanelsRender
	{
		/* Outside Assets/ deliberately: Unity never imports these, there is no .meta churn, and
		 * nothing has to be cleaned up afterwards — which is how earlier sets got thrown away. */
		private const string OUTPUT_DIR = "/home/jim/Dev/FishMMO-Dev/PanelRenders";
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string GUI_ROOT = "Assets/Scripts/Client/GUI";
		private const int WIDTH = 1200;
		private const int HEIGHT = 900;
		private const int SETTLE_FRAMES = 24;

		private sealed class Job
		{
			public string Label;
			public string Uxml;
			public Action<GameObject, UIDocument> Populate;   // null = chrome only
		}

		private static readonly List<Job> queue = new List<Job>();
		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static Job current;
		private static int framesWaited;
		private static readonly List<string> live = new List<string>();
		private static readonly List<string> chrome = new List<string>();
		private static readonly List<string> failed = new List<string>();

		[MenuItem("FishMMO/UI Toolkit/Render All Panels Live")]
		public static void Render()
		{
			try
			{
				Directory.CreateDirectory(OUTPUT_DIR);
				Seed.All();

				Dictionary<string, Action<GameObject, UIDocument>> populators = Populators();

				foreach (string path in AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { GUI_ROOT })
					.Select(AssetDatabase.GUIDToAssetPath)
					.Where(p => p.EndsWith(".uxml"))
					.OrderBy(p => p))
				{
					string name = Path.GetFileNameWithoutExtension(path);
					populators.TryGetValue(name, out var populate);

					if (name == "UIGuild")
					{
						queue.Add(Make("UIGuild-Roster", path, (h, d) => Panels.Guild(h, d, "guild-tab-roster")));
						queue.Add(Make("UIGuild-Info", path, (h, d) => Panels.Guild(h, d, "guild-tab-info")));
						queue.Add(Make("UIGuild-Log", path, (h, d) => Panels.Guild(h, d, "guild-tab-log")));
						continue;
					}
					if (name == "UIOptions")
					{
						foreach (string tab in Panels.OptionsTabs)
						{
							string captured = tab;
							queue.Add(Make("UIOptions-" + captured, path, (h, d) => Panels.Options(h, d, captured)));
						}

						// The UI tab's profile controls are below the fold; capture them too.
						queue.Add(Make("UIOptions-interface-profiles", path,
							(h, d) => Panels.Options(h, d, "interface", scrollToEnd: true)));
						continue;
					}

					queue.Add(Make(name, path, populate));
				}

				int total = queue.Count;
				queue.RemoveAll(AlreadyRendered);
				Debug.Log($"[All] queued {queue.Count} captures ({total - queue.Count} already present)");

				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[All] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static bool AlreadyRendered(Job job)
		{
			return File.Exists(Path.Combine(OUTPUT_DIR, job.Label + "-live.png"))
				|| File.Exists(Path.Combine(OUTPUT_DIR, job.Label + "-chrome.png"))
				|| File.Exists(Path.Combine(OUTPUT_DIR, job.Label + ".png"));
		}

		private static Job Make(string label, string uxml, Action<GameObject, UIDocument> populate)
		{
			return new Job { Label = label, Uxml = uxml, Populate = populate };
		}

		private static Dictionary<string, Action<GameObject, UIDocument>> Populators()
		{
			return new Dictionary<string, Action<GameObject, UIDocument>>
			{
				{ "UIParty",          Panels.Party },
				{ "UIChat",           Panels.Chat },
				{ "UITooltip",        Panels.Tooltip },
				{ "UIContextMenu",    Panels.ContextMenu },
				{ "UIDropdown",       Panels.Dropdown },
				{ "UIDialogBox",      Panels.DialogBox },
				{ "UIColorPicker",    Panels.ColorPicker },
				{ "UILoadingScreen",  Panels.LoadingScreen },
				{ "UIDeathDialog",    Panels.DeathDialog },
				{ "UIInventory",      Panels.Inventory },
				{ "UIEquipment",      Panels.Equipment },
				{ "UIBank",           Panels.Bank },
				{ "UIAchievements",   Panels.Achievements },
				{ "UIFactions",       Panels.Factions },
				{ "UIFriendList",     Panels.FriendList },
				{ "UIDungeonFinder",  Panels.DungeonFinder },
				{ "UIInstance",       Panels.InstancePanel },
			};
		}

		// ── Capture plumbing ────────────────────────────────────────

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
					ReleaseTarget();
					Report();
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
				Debug.LogError($"[All] pump failed on {current?.Label}: {ex.Message}");
				failed.Add(current?.Label ?? "?");
				Teardown();
				current = null;
			}
		}

		/// <summary>Allocates the shared render target once for the whole run.</summary>
		private static void EnsureTarget()
		{
			if (texture != null) { return; }

			texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
			texture.Create();

			settings = UnityEngine.Object.Instantiate(
				AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.clearColor = true;
			settings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);
		}

		private static void Mount(Job job)
		{
			EnsureTarget();

			host = new GameObject("Render_" + job.Label) { hideFlags = HideFlags.HideAndDontSave };
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(job.Uxml);

			if (job.Populate != null)
			{
				try
				{
					job.Populate(host, document);
				}
				catch (Exception ex)
				{
					/* Unwrapped: reflection-driven populators surface everything as
					 * TargetInvocationException, whose own message names nothing useful. */
					Exception root = ex;
					while (root.InnerException != null) { root = root.InnerException; }

					Debug.LogWarning($"[All] {job.Label}: populate threw ({root.GetType().Name}: {root.Message}) " +
						$"at {root.StackTrace?.Split('\n')[0]}; falling back to chrome.");
					job.Populate = null;
				}
			}

			if (job.Populate == null)
			{
				// Same treatment the stock preview tool gives: several panels start hidden.
				VisualElement root = document.rootVisualElement;
				if (root != null)
				{
					foreach (VisualElement child in root.Children())
					{
						if (child.resolvedStyle.display == DisplayStyle.None)
						{
							child.style.display = DisplayStyle.Flex;
						}
					}
				}
			}

			document.rootVisualElement?.MarkDirtyRepaint();
		}

		private static void Capture(Job job)
		{
			string suffix = job.Populate != null ? "-live" : "-chrome";
			(job.Populate != null ? live : chrome).Add(job.Label);

			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				Texture2D image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
				image.Apply();
				File.WriteAllBytes(Path.Combine(OUTPUT_DIR, job.Label + suffix + ".png"), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		/// <summary>Destroys the capture's host. The shared target survives the whole run.</summary>
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

		private static void Report()
		{
			Debug.Log($"[All] RESULT live={live.Count} chrome={chrome.Count} failed={failed.Count}");
			Debug.Log("[All] LIVE: " + string.Join(", ", live));
			Debug.Log("[All] CHROME: " + string.Join(", ", chrome));
			if (failed.Count > 0)
			{
				Debug.Log("[All] FAILED: " + string.Join(", ", failed));
			}
		}
	}
}
