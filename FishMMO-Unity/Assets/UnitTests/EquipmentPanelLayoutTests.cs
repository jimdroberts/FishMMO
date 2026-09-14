using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEditor;
using NUnit.Framework;
using FishMMO.Client;
using FishMMO.Shared;
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
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/CharacterSheet/UICharacterSheet.uxml";

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

		/// <summary>
		/// Every socket is named in full, unclipped, below its icon and centred (issue #257).
		/// </summary>
		/// <remarks>
		/// The labels were "Shldr", "Acc", "Main" and "Off". Checking only the text would let a
		/// longer name come back clipped, so each name's natural width is measured against the
		/// width its label resolved to. The placement checks pin the defect the full names made
		/// visible: Unity's default Label box lifted the text across the icon's bottom edge and
		/// its uneven margins pulled it off centre — a control run with that box restored fails
		/// both, while every name still fits.
		/// </remarks>
		[UnityTest]
		public IEnumerator EverySocketLabelIsTheFullSlotNameAndFits()
		{
			// Yoga has not placed a freshly mounted tree; every width reads NaN until it has.
			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			string[] names = CharacterSheetView.SlotElementNames;
			LogAssert.AreEqual(System.Enum.GetValues(typeof(ItemSlot)).Length, names.Length, "one socket per slot");

			for (int i = 0; i < names.Length; ++i)
			{
				ItemSlot slot = (ItemSlot)i;
				VisualElement socket = Live.Q(names[i]);
				LogAssert.IsNotNull(socket, $"{names[i]} is in the tree");

				Label label = socket.Q<Label>(className: "eq-slot__label");
				LogAssert.IsNotNull(label, $"{names[i]} carries a name label");
				LogAssert.AreEqual(ItemSlotNames.DisplayName(slot), label.text, $"{names[i]} names {slot} in full");

				float available = label.contentRect.width;
				Vector2 natural = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
				LogAssert.IsTrue(available > 0, $"{names[i]}'s label resolved a width (got {available})");
				LogAssert.IsTrue(natural.x <= available,
					$"\"{label.text}\" needs {natural.x}px but {names[i]}'s label has {available}px");

				// Label and icon share the socket as parent, so their layouts are comparable.
				VisualElement icon = socket.Q(className: "eq-slot__icon");
				LogAssert.IsNotNull(icon, $"{names[i]} carries an icon");
				float textTop = label.layout.y + label.contentRect.y;
				LogAssert.IsTrue(textTop >= icon.layout.yMax,
					$"\"{label.text}\" starts at y {textTop}, inside the icon that ends at y {icon.layout.yMax}");

				float textCentre = label.layout.x + label.contentRect.x + label.contentRect.width * 0.5f;
				float socketCentre = socket.layout.width * 0.5f;
				LogAssert.IsTrue(Mathf.Abs(textCentre - socketCentre) <= 0.5f,
					$"\"{label.text}\" is centred at x {textCentre}, the socket at x {socketCentre}");
			}
		}

		[Test]
		public void SlotDisplayNamesAreUnabbreviatedAndDistinct()
		{
			System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>();
			foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
			{
				string name = ItemSlotNames.DisplayName(slot);
				LogAssert.IsTrue(seen.Add(name), $"{slot}'s name \"{name}\" is not shared with another slot");
			}

			LogAssert.AreEqual("Main Hand", ItemSlotNames.DisplayName(ItemSlot.Primary), "the primary slot is named for the hand");
			LogAssert.AreEqual("Off Hand", ItemSlotNames.DisplayName(ItemSlot.Secondary), "the secondary slot is named for the hand");
		}
	}
}
