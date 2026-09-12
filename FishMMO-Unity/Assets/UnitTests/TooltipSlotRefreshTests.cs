using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A tooltip whose slot changes underneath it describes what is there now, not what the
	/// pointer found on arrival.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Issue #280. A tooltip is opened from whatever the pointer found at the moment it entered the
	/// slot, and nothing re-read that. Swapping two items repaints both slots while the cursor never
	/// moves, so no <c>PointerLeave</c> and no <c>PointerEnter</c> ever fire: the tooltip went on
	/// describing the item that used to be in the slot the player is looking at. The same hole let a
	/// slot change and keep showing a stale stack count.
	/// </para>
	/// <para>
	/// These mount the real tooltip UXML and drive the real control. The owner guard is the part
	/// worth pinning behaviourally rather than by reading the call sites: a container repaints every
	/// slot it has when it resyncs, and only one of them is under the pointer, so the assertion that
	/// matters as much as "it updates" is "and it does not update for the other one".
	/// </para>
	/// </remarks>
	[TestFixture]
	public class TooltipSlotRefreshTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/Shared/Tooltip/UITooltip.uxml";
		private const string ContentName = "tooltip-content";
		private const string TitleClass = "tip-title";

		private GameObject host;
		private UIDocument document;
		private PanelSettings settings;
		private UITKTooltip tooltip;
		private VisualElement slotContainer;

		[SetUp]
		public void SetUp()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the tooltip UXML must exist at {UxmlPath}");

			settings = Object.Instantiate(asset);

			host = new GameObject("UITooltip");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = uxml;

			tooltip = host.AddComponent<UITKTooltip>();
			tooltip.Document = document;
			tooltip.OnStarting();

			/* Stands in for a grid. The tooltip identifies its owner by identity alone, so a bare
			 * element exercises the owner guard exactly as a real slot would — and a slot built for
			 * real would bring an inventory controller, a character and the drag object with it. */
			slotContainer = new VisualElement();
			document.rootVisualElement.Add(slotContainer);
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null) Object.DestroyImmediate(host);
			if (settings != null) Object.DestroyImmediate(settings);
			host = null;
			document = null;
			settings = null;
			tooltip = null;
			slotContainer = null;
		}

		/// <summary>Adds an element standing in for a slot, and returns it as the tooltip's owner.</summary>
		private VisualElement AddSlot(string name)
		{
			VisualElement slot = new VisualElement();
			slot.name = name;
			slotContainer.Add(slot);
			return slot;
		}

		/// <summary>The title of whatever the tooltip is drawing, or null when it is drawing nothing.</summary>
		/// <remarks>
		/// A hidden panel has its UIDocument disabled, which discards the visual tree — so a closed
		/// tooltip has no root to query at all, and that is the expected shape of "nothing drawn".
		/// </remarks>
		private string TitleText()
		{
			VisualElement root = document.rootVisualElement;
			if (root == null)
			{
				return null;
			}

			VisualElement content = root.Q(ContentName);
			if (content == null)
			{
				return null;
			}

			Label title = content.Q<Label>(className: TitleClass);
			return title != null ? title.text : null;
		}

		/// <summary>
		/// A describable with one title row, so the rendered tooltip says which object it is for.
		/// </summary>
		private sealed class NamedTooltip : ITooltip
		{
			public NamedTooltip(string name)
			{
				Name = name;
			}

			public Sprite Icon => null;

			public string Name { get; }

			public void BuildTooltip(TooltipContent content)
			{
				content.AddTitle(Name);
			}
		}

		[Test]
		public void TheTooltipRereadsTheSlotItIsDescribing()
		{
			VisualElement slot = AddSlot("slot-0");

			tooltip.Open(new NamedTooltip("Iron Sword"), slot);
			LogAssert.AreEqual("Iron Sword", TitleText(), "Precondition: the tooltip describes what the pointer found.");

			tooltip.RefreshFor(slot, new NamedTooltip("Steel Dagger"));

			LogAssert.AreEqual("Steel Dagger", TitleText(),
				"the slot changed under a stationary cursor, so the tooltip must describe the new item");
		}

		[Test]
		public void ARefreshAddressedToAnotherSlotLeavesTheOpenTooltipAlone()
		{
			/* The swap case. Both slots repaint; the pointer is over one of them, and the other's
			 * update must not reach the tooltip on screen. */
			VisualElement hovered = AddSlot("slot-0");
			VisualElement other = AddSlot("slot-1");

			tooltip.Open(new NamedTooltip("Iron Sword"), hovered);
			tooltip.RefreshFor(other, new NamedTooltip("Steel Dagger"));

			LogAssert.AreEqual("Iron Sword", TitleText(),
				"a slot the pointer is not over must not rewrite the tooltip that is showing");
		}

		[Test]
		public void ARefreshWithNothingOpenDoesNotOpenATooltip()
		{
			/* A grid repaints its whole contents when the panel opens or a resync lands, on panels
			 * that have never been hovered. Refreshing is not opening. */
			VisualElement slot = AddSlot("slot-0");

			tooltip.RefreshFor(slot, new NamedTooltip("Iron Sword"));

			LogAssert.IsFalse(tooltip.Visible, "a refresh must not be the thing that opens a tooltip");
			LogAssert.IsNull(TitleText(), "and it must not render one either");
		}

		[Test]
		public void TheTooltipClosesWhenItsSlotIsEmptied()
		{
			VisualElement slot = AddSlot("slot-0");

			tooltip.Open(new NamedTooltip("Iron Sword"), slot);
			tooltip.RefreshFor(slot, null);

			LogAssert.IsFalse(tooltip.Visible, "an emptied slot has nothing left for the tooltip to follow");
			LogAssert.IsNull(TitleText(), "and nothing of it is left on screen");
		}

		[Test]
		public void ARefreshAddressedToTheOwnerIsAncestryNotIdentity()
		{
			/* The same rule HideFor already applies: an element that HOLDS the tooltip's owner is
			 * addressing it. A caller repainting a slot does not have to know which element inside
			 * it the tooltip was opened from. */
			VisualElement slot = AddSlot("slot-0");
			VisualElement icon = new VisualElement();
			slot.Add(icon);

			tooltip.Open(new NamedTooltip("Iron Sword"), icon);
			tooltip.RefreshFor(slot, new NamedTooltip("Steel Dagger"));

			LogAssert.AreEqual("Steel Dagger", TitleText(),
				"a refresh addressed to the container of the owner still finds it");
		}
	}
}
