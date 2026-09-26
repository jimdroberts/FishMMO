using System.IO;
using System.Reflection;
using FishMMO.Client;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The options rows that turn the network statistics overlay and its graph on and off: the
	/// settings they write, the defaults a fresh install gets, and the round trip through the real
	/// options panel — a click writes the configuration and notifies the overlay, and a panel
	/// opened later shows what was stored without writing it back.
	/// </summary>
	/// <remarks>
	/// Every test runs against a scratch <see cref="Configuration"/> swapped in as the global
	/// store, so nothing here reads or writes the developer's Configuration.cfg (and
	/// <see cref="ClientSettings.Flush"/> is editor-guarded besides).
	/// </remarks>
	[TestFixture]
	public class NetworkStatsOptionsTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string OptionsUxml = "Assets/Scripts/Client/GUI/World/Options/UIOptions.uxml";
		private const string EnabledToggle = "netstats-enabled-toggle";
		private const string GraphToggle = "netstats-graph-toggle";

		private Configuration previous;
		private int raised;
		private readonly System.Collections.Generic.List<GameObject> hosts = new System.Collections.Generic.List<GameObject>();
		private readonly System.Collections.Generic.List<Object> assets = new System.Collections.Generic.List<Object>();

		[SetUp]
		public void SetUp()
		{
			previous = Configuration.GlobalSettings;
			Configuration.SetGlobalSettings(new Configuration(
				Path.Combine(Path.GetTempPath(), "FishMMO-NetworkStatsOptionsTests")));
			raised = 0;
			ClientNetworkStatsSettings.OnChanged += CountRaise;
		}

		[TearDown]
		public void TearDown()
		{
			ClientNetworkStatsSettings.OnChanged -= CountRaise;
			foreach (GameObject host in hosts)
			{
				if (host != null)
				{
					Object.DestroyImmediate(host);
				}
			}
			hosts.Clear();
			foreach (Object asset in assets)
			{
				if (asset != null)
				{
					Object.DestroyImmediate(asset);
				}
			}
			assets.Clear();

			FieldInfo field = typeof(Configuration).GetField("globalSettings", BindingFlags.NonPublic | BindingFlags.Static);
			if (field != null)
			{
				field.SetValue(null, previous);
			}
			else if (previous != null)
			{
				Configuration.SetGlobalSettings(previous);
			}
		}

		private void CountRaise() => ++raised;

		// ── The settings ─────────────────────────────────────────────────────────────────────

		[Test]
		public void AFreshInstall_HasTheOverlayOff_AndTheGraphReady()
		{
			/* A diagnostic nobody asked for must not appear in the corner of a new player's
			 * screen; the graph is on so that turning the overlay on shows the whole of it. */
			LogAssert.IsFalse(ClientNetworkStatsSettings.Enabled, "the overlay ships off");
			LogAssert.IsTrue(ClientNetworkStatsSettings.ShowGraph, "the graph ships on");
		}

		[Test]
		public void BothSettings_RoundTrip_UnderTheirOwnKeys_AndNotifyOnce()
		{
			ClientNetworkStatsSettings.SetEnabled(true);
			LogAssert.AreEqual(1, raised, "SetEnabled notifies the overlay");
			LogAssert.IsTrue(ClientNetworkStatsSettings.Enabled, "and reads back");
			Configuration.GlobalSettings.TryGetBool("NetworkStats.Enabled", out bool stored, false);
			LogAssert.IsTrue(stored, "stored under NetworkStats.Enabled in Configuration.cfg");

			ClientNetworkStatsSettings.SetShowGraph(false);
			LogAssert.AreEqual(2, raised, "SetShowGraph notifies the overlay");
			LogAssert.IsFalse(ClientNetworkStatsSettings.ShowGraph, "and reads back");
			Configuration.GlobalSettings.TryGetBool("NetworkStats.ShowGraph", out bool graph, true);
			LogAssert.IsFalse(graph, "stored under NetworkStats.ShowGraph");
		}

		[Test]
		public void TheOptionsMarkup_DeclaresBothRows()
		{
			/* The panel binds by element name and tolerates a missing one silently, so a renamed
			 * row would leave the overlay with no switch and no error. */
			string uxml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), OptionsUxml));
			LogAssert.IsTrue(uxml.Contains($"name=\"{EnabledToggle}\""), $"UIOptions.uxml must declare {EnabledToggle}");
			LogAssert.IsTrue(uxml.Contains($"name=\"{GraphToggle}\""), $"UIOptions.uxml must declare {GraphToggle}");
		}

		// ── The options panel, mounted ───────────────────────────────────────────────────────

		/// <summary>
		/// Mounts the real options panel on its shipped markup and runs its start-up, as the
		/// ClientPreboot scene does. Awake is not run: the panel is not registered or shown, which
		/// the binding under test does not need.
		/// </summary>
		private VisualElement MountOptions()
		{
			PanelSettings shared = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(OptionsUxml);
			LogAssert.IsNotNull(shared, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the options UXML must exist at {OptionsUxml}");

			PanelSettings clone = Object.Instantiate(shared);
			assets.Add(clone);

			GameObject host = new GameObject("UIOptions");
			hosts.Add(host);
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = clone;
			document.visualTreeAsset = uxml;

			UITKOptions options = host.AddComponent<UITKOptions>();
			options.Document = document;
			options.OnStarting();
			return document.rootVisualElement;
		}

		private static Toggle Find(VisualElement root, string name)
		{
			Toggle toggle = root.Q<Toggle>(name);
			LogAssert.IsNotNull(toggle, $"{name} must exist in the options tree");
			return toggle;
		}

		[Test]
		public void TheOptionsRows_StartFromTheStore_WithoutWritingIt()
		{
			ClientSettings.Set(ClientSettings.NetworkStatsEnabledKey, true);
			ClientSettings.Set(ClientSettings.NetworkStatsGraphKey, false);

			VisualElement root = MountOptions();
			LogAssert.IsTrue(Find(root, EnabledToggle).value, "the overlay row shows the stored on");
			LogAssert.IsFalse(Find(root, GraphToggle).value, "the graph row shows the stored off");
			LogAssert.IsTrue(Find(root, GraphToggle).enabledSelf, "the graph row is live while the overlay is on");
			LogAssert.AreEqual(0, raised, "opening the options must not write back what it just read");
		}

		[Test]
		public void ClickingTheRows_WritesTheStore_AndNotifiesTheOverlay()
		{
			VisualElement root = MountOptions();
			Toggle enabled = Find(root, EnabledToggle);
			Toggle graph = Find(root, GraphToggle);
			LogAssert.IsFalse(enabled.value, "a fresh install's row is off");
			LogAssert.IsFalse(graph.enabledSelf, "and the graph row waits for the overlay");

			enabled.value = true;
			LogAssert.IsTrue(ClientNetworkStatsSettings.Enabled, "ticking the row turns the overlay on");
			LogAssert.AreEqual(1, raised, "and tells it");
			LogAssert.IsTrue(graph.enabledSelf, "the graph row becomes live");

			graph.value = false;
			LogAssert.IsFalse(ClientNetworkStatsSettings.ShowGraph, "unticking the graph row turns the graph off");
			LogAssert.AreEqual(2, raised, "and tells the overlay");

			/* A panel opened afterwards reads the same store: the round trip. */
			VisualElement again = MountOptions();
			LogAssert.IsTrue(Find(again, EnabledToggle).value, "a later options panel shows the overlay on");
			LogAssert.IsFalse(Find(again, GraphToggle).value, "and the graph off");
			LogAssert.AreEqual(2, raised, "without notifying anything again");
		}
	}
}
