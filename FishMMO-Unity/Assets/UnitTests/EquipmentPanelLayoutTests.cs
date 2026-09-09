using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using NUnit.Framework;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The equipment panel shows the gear and the stats it adds up to at the same time.
	/// </summary>
	/// <remarks>
	/// They used to be a GEAR / STATS / SETS tab bar. That made the panel answer only one of the
	/// two questions a player opens it to compare, and SETS was a placeholder that showed nothing
	/// at all. These tests mount the real UXML, run the panel's own <c>OnStarting</c>, and check
	/// the tree the player would see: no tab bar, no gear score, and both halves on screen.
	/// </remarks>
	[TestFixture]
	public class EquipmentPanelLayoutTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Equipment/UIEquipment.uxml";

		private GameObject host;
		private UIDocument document;
		private PanelSettings settings;

		[SetUp]
		public void SetUp()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the equipment UXML must exist at {UxmlPath}");

			settings = Object.Instantiate(asset);

			host = new GameObject("EquipmentPanelTest");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;

			UITKEquipment panel = host.AddComponent<UITKEquipment>();
			panel.Document = document;
			panel.OnStarting();
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null) Object.DestroyImmediate(host);
			if (settings != null) Object.DestroyImmediate(settings);
			host = null;
			document = null;
			settings = null;
		}

		private VisualElement Live => document.rootVisualElement;

		[Test]
		public void ThePanelHasNoTabs()
		{
			LogAssert.IsNull(Live.Q("tab-bar"), "the tab bar is gone");
			LogAssert.IsNull(Live.Q<Button>("tab-gear"), "the GEAR tab is gone");
			LogAssert.IsNull(Live.Q<Button>("tab-stats"), "the STATS tab is gone");
			LogAssert.IsNull(Live.Q<Button>("tab-sets"), "the SETS tab is gone");
		}

		[Test]
		public void TheGearGridAndTheAttributeListAreBothOnScreen()
		{
			VisualElement body = Live.Q("panel-body");
			VisualElement footer = Live.Q("panel-footer");

			LogAssert.IsNotNull(body, "the slot grid is in the tree");
			LogAssert.IsNotNull(footer, "the attribute list is in the tree");
			LogAssert.IsTrue(body.resolvedStyle.display != DisplayStyle.None, "the slot grid is displayed");
			LogAssert.IsTrue(footer.resolvedStyle.display != DisplayStyle.None, "the attribute list is displayed");
			LogAssert.IsNotNull(Live.Q<ScrollView>("attribute-list"), "the attribute scroll survives the merge");
		}

		[Test]
		public void TheAttributeListIsStillNamed()
		{
			// The STATS tab used to name it. Nothing else does now, so the heading has to.
			Label heading = Live.Q<Label>("attribute-heading");
			LogAssert.IsNotNull(heading, "the attribute list carries a heading");
			LogAssert.AreEqual("ATTRIBUTES", heading.text, "and it says what the list is");
		}

		[Test]
		public void ThereIsNoGearScore()
		{
			LogAssert.IsNull(Live.Q("gear-score"), "the gear score is gone from the tree");
		}

		[Test]
		public void TheStatusBarStillCarriesTheThreeResources()
		{
			LogAssert.IsNotNull(Live.Q<Label>("stat-hp"), "health survives the merge");
			LogAssert.IsNotNull(Live.Q<Label>("stat-mp"), "mana survives the merge");
			LogAssert.IsNotNull(Live.Q<Label>("stat-stam"), "stamina survives the merge");
		}

		[Test]
		public void EveryEquipmentSlotIsStillDrawn()
		{
			string[] slots =
			{
				"slot-head", "slot-chest", "slot-shoulders", "slot-hands", "slot-legs",
				"slot-feet", "slot-back", "slot-mainhand", "slot-offhand", "slot-accessory",
			};

			foreach (string slot in slots)
			{
				LogAssert.IsNotNull(Live.Q(slot), $"{slot} is in the tree");
			}
		}
	}
}
