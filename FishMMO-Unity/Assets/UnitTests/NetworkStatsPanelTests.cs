using System.IO;
using System.Reflection;
using FishMMO.Client;
using FishMMO.Shared;
using FishNet.Transporting.WebTransport;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The network statistics overlay mounted on a real UI Toolkit panel from its shipped markup,
	/// fed fabricated snapshots: what each label and badge reads, that the player's setting owns
	/// its visibility, and that a hidden overlay samples nothing.
	/// </summary>
	/// <remarks>
	/// The formatting and smoothing rules themselves are pinned by
	/// <see cref="NetworkStatsPresentationTests"/>; this fixture proves the panel writes them into
	/// the elements the markup declares. A scratch configuration is swapped in, so nothing reads or
	/// writes the developer's own Configuration.cfg.
	/// </remarks>
	[TestFixture]
	public class NetworkStatsPanelTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/Shared/NetworkStats/UINetworkStats.uxml";
		private const string UssPath = "Assets/Scripts/Client/GUI/Shared/NetworkStats/UINetworkStats.uss";
		private const string Dash = NetworkStatsPresentation.Unknown;

		private Configuration previousConfiguration;
		private GameObject host;
		private UIDocument document;
		private UITKNetworkStats panel;
		private PanelSettings settings;

		[SetUp]
		public void SetUp()
		{
			previousConfiguration = Configuration.GlobalSettings;
			Configuration.SetGlobalSettings(new Configuration(
				Path.Combine(Path.GetTempPath(), "FishMMO-NetworkStatsPanelTests")));
			ClientSettings.Set(ClientSettings.NetworkStatsEnabledKey, true);
			ClientSettings.Set(ClientSettings.NetworkStatsGraphKey, true);
		}

		[TearDown]
		public void TearDown()
		{
			if (panel != null)
			{
				/* The panel subscribes to a static settings event and registers with UIManager by
				 * name; a destroyed one left behind would answer the next fixture's events. The
				 * real OnDestroy undoes both. */
				Invoke(panel, "OnDestroy");
				panel = null;
			}
			if (host != null)
			{
				Object.DestroyImmediate(host);
				host = null;
			}
			if (settings != null)
			{
				Object.DestroyImmediate(settings);
				settings = null;
			}

			FieldInfo field = typeof(Configuration).GetField("globalSettings", BindingFlags.NonPublic | BindingFlags.Static);
			if (field != null)
			{
				field.SetValue(null, previousConfiguration);
			}
			else if (previousConfiguration != null)
			{
				Configuration.SetGlobalSettings(previousConfiguration);
			}
		}

		private static void Invoke(UITKControl control, string method)
		{
			MethodInfo info = typeof(UITKControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKControl must still declare {method}");
			info.Invoke(control, null);
		}

		/// <summary>
		/// Mounts the overlay the way ClientPreboot does: its document enabled, Awake registering
		/// and hiding it, and its own start-up deciding visibility from the setting.
		/// </summary>
		private void Mount()
		{
			PanelSettings shared = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(shared, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the overlay UXML must exist at {UxmlPath}");

			settings = Object.Instantiate(shared);

			host = new GameObject(UITKNetworkStats.PanelName);
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;
			document.enabled = false;

			panel = host.AddComponent<UITKNetworkStats>();
			panel.Document = document;
			panel.StartOpen = false;
			panel.IsAlwaysOpen = false;
			panel.CloseOnQuitToMenu = false;
			panel.ReleasesCursor = false;
			panel.CloseOnEscape = false;

			Invoke(panel, "Awake");

			/* Enables the document, which clones the tree and runs OnStarting against it; the
			 * setting then decides whether the overlay stays up. */
			panel.Show();
		}

		private T Q<T>(string name) where T : VisualElement
		{
			T element = document.rootVisualElement.Q<T>(name);
			LogAssert.IsNotNull(element, $"{name} must exist in the cloned tree");
			return element;
		}

		private string Text(string name) => Q<Label>(name).text;

		private void AssertBadge(string stem, string word, string cssClass)
		{
			Label badge = Q<Label>(stem + "-badge");
			LogAssert.AreEqual(word, badge.text, $"{stem} badge word");
			LogAssert.IsTrue(badge.ClassListContains(cssClass), $"{stem} badge must carry {cssClass}");
			foreach (string other in NetworkStatsPresentation.BadgeClasses)
			{
				if (other != cssClass)
				{
					LogAssert.IsFalse(badge.ClassListContains(other), $"{stem} badge must not also carry {other}");
				}
			}
		}

		/// <summary>Seven seconds of a steady desktop client: 40 kB/s down, 8 kB/s up.</summary>
		private void FeedSteadyDesktop()
		{
			for (int t = 0; t < 7; ++t)
			{
				TransportTrafficSnapshot s = NetworkStatsPresentationTests.Native(200.0 + t,
					1_000_000 + 40_000L * t, 200_000 + 8_000L * t,
					1_100_000 + 44_000L * t, 250_000 + 10_000L * t,
					50_000 + 60L * t, 30_000 + 40L * t);
				panel.Ingest(in s);
			}
			panel.Refresh();
		}

		// ── The markup ───────────────────────────────────────────────────────────────────────

		[Test]
		public void TheMarkup_DeclaresEveryElementThePanelWrites()
		{
			Mount();
			string[] suffixes = { "-row", "-down", "-up", "-badge" };
			foreach (string stem in UITKNetworkStats.RowStems)
			{
				foreach (string suffix in suffixes)
				{
					Q<VisualElement>(stem + suffix);
				}
			}
			foreach (string stem in new[] { "netstats-app", "netstats-quic", "netstats-wire", "netstats-link" })
			{
				Q<Label>(stem + "-down-total");
				Q<Label>(stem + "-up-total");
			}
			Q<VisualElement>("panel-header");
			Q<Label>("netstats-headline-down");
			Q<Label>("netstats-headline-up");
			Q<Label>("netstats-headline-badge");
			Q<VisualElement>("netstats-graph");
		}

		[Test]
		public void EveryBadgeClass_IsDeclaredInTheStylesheet()
		{
			/* A class the panel adds but the stylesheet never declares draws the neutral badge:
			 * an estimate that looks exactly like a measurement. */
			string uss = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), UssPath));
			foreach (string badgeClass in NetworkStatsPresentation.BadgeClasses)
			{
				LogAssert.IsTrue(uss.Contains("." + badgeClass), $"UINetworkStats.uss must declare .{badgeClass}");
			}
		}

		// ── Labels and badges ────────────────────────────────────────────────────────────────

		[Test]
		public void BeforeTheFirstSample_EveryBadgeIsPending_AndEveryValueIsADash()
		{
			Mount();
			LogAssert.IsTrue(panel.Visible, "the setting is on, so the overlay is up");
			foreach (string stem in UITKNetworkStats.RowStems)
			{
				AssertBadge(stem, "…", "netstats-badge--pending");
				LogAssert.AreEqual(Dash, Text(stem + "-down"), $"{stem} down");
				LogAssert.AreEqual(Dash, Text(stem + "-up"), $"{stem} up");
			}
			LogAssert.AreEqual(Dash, Text("netstats-headline-down"), "headline");
		}

		[Test]
		public void ASteadyDesktopClient_ShowsEveryLayer_WithItsBadge()
		{
			Mount();
			FeedSteadyDesktop();

			LogAssert.AreEqual("NATIVE QUIC", Text("netstats-backend"), "backend");
			LogAssert.AreEqual("365 kbps", Text("netstats-headline-down"), "headline: wire down in bits");
			LogAssert.AreEqual("89.0 kbps", Text("netstats-headline-up"), "headline: wire up in bits");
			AssertBadge("netstats-headline", "ESTIMATE", "netstats-badge--estimated");

			LogAssert.AreEqual("40.0 kB/s", Text("netstats-app-down"), "app down");
			LogAssert.AreEqual("8.00 kB/s", Text("netstats-app-up"), "app up");
			LogAssert.AreEqual("1.24 MB", Text("netstats-app-down-total"), "app total down: 1,000,000 + 6 × 40,000");
			AssertBadge("netstats-app", "MEASURED", "netstats-badge--measured");

			LogAssert.AreEqual("44.0 kB/s", Text("netstats-quic-down"), "QUIC down");
			LogAssert.AreEqual("10.0 kB/s", Text("netstats-quic-up"), "QUIC up");
			AssertBadge("netstats-quic", "MEASURED", "netstats-badge--measured");

			LogAssert.AreEqual("45.7 kB/s", Text("netstats-wire-down"), "wire down: 44,000 + 60 × 28");
			LogAssert.AreEqual("11.1 kB/s", Text("netstats-wire-up"), "wire up: 10,000 + 40 × 28");
			AssertBadge("netstats-wire", "ESTIMATE", "netstats-badge--estimated");

			LogAssert.AreEqual("60/s", Text("netstats-dgram-down"), "datagrams down");
			LogAssert.AreEqual("40/s", Text("netstats-dgram-up"), "datagrams up");
			AssertBadge("netstats-dgram", "MEASURED", "netstats-badge--measured");

			LogAssert.AreEqual("×1.14", Text("netstats-overhead-down"), "overhead down");
			LogAssert.AreEqual("×1.39", Text("netstats-overhead-up"), "overhead up");
			AssertBadge("netstats-overhead", "ESTIMATE", "netstats-badge--estimated");

			LogAssert.AreEqual("38 ms", Text("netstats-link-down"), "round trip");
			LogAssert.AreEqual("0.20%", Text("netstats-link-up"), "loss");
			LogAssert.AreEqual("1472 B", Text("netstats-link-down-total"), "path MTU");
			LogAssert.AreEqual("61.4 kB", Text("netstats-link-up-total"), "congestion window");
			AssertBadge("netstats-link", "MEASURED", "netstats-badge--measured");
		}

		[Test]
		public void AnUnconnectedDesktopClient_ShowsDashes_NeverZero()
		{
			Mount();
			for (int t = 0; t < 3; ++t)
			{
				TransportTrafficSnapshot s = NetworkStatsPresentationTests.NativeNotStarted(10.0 + t);
				panel.Ingest(in s);
			}
			panel.Refresh();

			LogAssert.AreEqual("0 B/s", Text("netstats-app-down"), "nothing sent yet, and the app layer can say so");
			AssertBadge("netstats-app", "MEASURED", "netstats-badge--measured");
			foreach (string stem in new[] { "netstats-quic", "netstats-wire", "netstats-dgram", "netstats-overhead", "netstats-link" })
			{
				LogAssert.AreEqual(Dash, Text(stem + "-down"), $"{stem} down is unknown, not zero");
				LogAssert.AreEqual(Dash, Text(stem + "-up"), $"{stem} up is unknown, not zero");
				AssertBadge(stem, "N/A", "netstats-badge--unavailable");
			}
			LogAssert.AreEqual(Dash, Text("netstats-quic-down-total"), "no QUIC total either");
			LogAssert.AreEqual(Dash, Text("netstats-headline-down"), "and no headline");
		}

		[Test]
		public void AChromeClient_WearsEstimateBadges_WhereTheBrowserIsSilent()
		{
			Mount();
			for (int t = 0; t < 3; ++t)
			{
				TransportTrafficSnapshot s = NetworkStatsPresentationTests.Browser(50.0 + t, 100_000 + 20_000L * t, 20_000 + 4_000L * t, firefox: false);
				panel.Ingest(in s);
			}
			panel.Refresh();

			LogAssert.AreEqual("BROWSER", Text("netstats-backend"), "backend");
			AssertBadge("netstats-app", "MEASURED", "netstats-badge--measured");
			AssertBadge("netstats-quic", "ESTIMATE", "netstats-badge--estimated");
			AssertBadge("netstats-dgram", "ESTIMATE", "netstats-badge--estimated");
			AssertBadge("netstats-wire", "ESTIMATE", "netstats-badge--estimated");
			AssertBadge("netstats-link", "N/A", "netstats-badge--unavailable");
			LogAssert.AreNotEqual(Dash, Text("netstats-quic-down"), "an estimate is still shown");
			LogAssert.AreEqual(Dash, Text("netstats-link-down"), "Chrome gives no round trip");
		}

		// ── Visibility, sampling and the graph ───────────────────────────────────────────────

		[Test]
		public void ThePlayersSetting_OwnsVisibility()
		{
			ClientSettings.Set(ClientSettings.NetworkStatsEnabledKey, false);
			Mount();
			LogAssert.IsFalse(panel.Visible, "off by the setting, the overlay stays hidden at start-up");

			ClientNetworkStatsSettings.SetEnabled(true);
			LogAssert.IsTrue(panel.Visible, "turning it on in the options shows it at once");

			ClientNetworkStatsSettings.SetEnabled(false);
			LogAssert.IsFalse(panel.Visible, "and turning it off hides it");
		}

		[Test]
		public void AHiddenOverlay_SamplesNothing()
		{
			Mount();
			ClientNetworkStatsSettings.SetEnabled(false);
			Invoke(panel, "Update");
			LogAssert.AreEqual(0, panel.Sampler.Count, "OnTick runs for hidden panels and must return before capturing");

			ClientNetworkStatsSettings.SetEnabled(true);
			Invoke(panel, "Update");
			LogAssert.AreEqual(1, panel.Sampler.Count, "shown, the first tick captures at once");
			Invoke(panel, "Update");
			LogAssert.AreEqual(1, panel.Sampler.Count, "and not again until a second has passed");
		}

		[Test]
		public void ReopeningTheOverlay_StartsAFreshWindowAndGraph()
		{
			Mount();
			FeedSteadyDesktop();
			LogAssert.IsTrue(panel.Sampler.HistoryCount > 0, "a graph was drawn");

			ClientNetworkStatsSettings.SetEnabled(false);
			ClientNetworkStatsSettings.SetEnabled(true);
			LogAssert.AreEqual(0, panel.Sampler.Count, "nothing was sampled while hidden, so nothing is averaged across it");
			LogAssert.AreEqual(0, panel.Sampler.HistoryCount, "and the graph does not join the two ends");
			LogAssert.AreEqual(Dash, Text("netstats-app-down"), "the labels say so too");
		}

		[Test]
		public void TheGraph_FollowsItsSetting_AndDrawsTwoLinesOnOneScale()
		{
			Mount();
			VisualElement graph = Q<VisualElement>("netstats-graph");
			LogAssert.AreEqual(2, graph.Query<NetworkStatsGraphSeries>().ToList().Count, "one line each way");

			FeedSteadyDesktop();
			double scale = NetworkStatsPresentation.GraphScale(panel.Sampler.HistoryPeak());
			LogAssert.AreEqual(NetworkStatsPresentation.ByteRate(scale), Text("netstats-graph-scale"), "the scale is labelled");
			LogAssert.AreEqual("50.0 kB/s", Text("netstats-graph-scale"), "a 45.7 kB/s peak rounds up to 50 kB/s");

			ClientNetworkStatsSettings.SetShowGraph(false);
			LogAssert.AreEqual(DisplayStyle.None, graph.style.display.value, "turned off, the graph leaves the layout");
			LogAssert.IsTrue(panel.Visible, "and the overlay stays up");

			ClientNetworkStatsSettings.SetShowGraph(true);
			LogAssert.AreEqual(DisplayStyle.Flex, graph.style.display.value, "turned on, it comes back");
		}

		[Test]
		public void TheOverlay_IsAHudPanel_UnderEveryWindow()
		{
			Mount();
			PropertyInfo layer = typeof(UITKNetworkStats).GetProperty("Layer", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(layer, "UITKControl.Layer must still exist");
			LogAssert.AreEqual(UITKPanelLayer.Hud, (UITKPanelLayer)layer.GetValue(panel), "a readout never covers a window the player opened");
			LogAssert.AreEqual(PickingMode.Ignore, Q<VisualElement>("netstats-root").pickingMode,
				"the full-screen root must let clicks through to the world");
		}
	}
}
