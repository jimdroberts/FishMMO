using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The trade window (issue #144) mounted on a real UI Toolkit panel and driven by the
	/// same state messages the server sends.
	/// </summary>
	/// <remarks>
	/// What is pinned: both tables paint from a <see cref="TradeStateBroadcast"/> with the
	/// local player on the left and the partner on the right, the acceptance badges track
	/// the flags, the Accept control is disabled once both have accepted, a server close
	/// takes the window down, and the grid grows to whatever the server sends. Nothing here
	/// touches the network: every entry point used is the public one the broadcast handlers
	/// forward to.
	/// </remarks>
	[TestFixture]
	public class TradePanelTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Trade/UITrade.uxml";

		private GameObject host;
		private UITKTrade trade;
		private UIDocument document;
		private PanelSettings sharedSettings;
		private readonly List<Object> assets = new List<Object>();

		private StackableTestTemplate arrows;
		private SingleTestTemplate sword;

		private class StackableTestTemplate : BaseItemTemplate { }
		private class SingleTestTemplate : BaseItemTemplate { }

		[SetUp]
		public void SetUp()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the trade UXML must exist at {UxmlPath}");

			sharedSettings = Object.Instantiate(settings);

			host = new GameObject("UITrade");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = sharedSettings;
			document.visualTreeAsset = uxml;
			document.enabled = false;

			trade = host.AddComponent<UITKTrade>();
			trade.Document = document;
			trade.StartOpen = false;
			trade.IsAlwaysOpen = false;
			trade.CloseOnQuitToMenu = true;
			trade.ReleasesCursor = false;
			trade.CloseOnEscape = true;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(trade, null);

			arrows = ScriptableObject.CreateInstance<StackableTestTemplate>();
			arrows.MaxStackSize = 20;
			arrows.Generate = false;
			arrows.name = "TradePanel_Arrows";
			arrows.AddToCache(arrows.name);
			assets.Add(arrows);

			sword = ScriptableObject.CreateInstance<SingleTestTemplate>();
			sword.MaxStackSize = 1;
			sword.Generate = false;
			sword.name = "TradePanel_Sword";
			sword.AddToCache(sword.name);
			assets.Add(sword);
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			foreach (Object asset in assets)
			{
				Object.DestroyImmediate(asset);
			}
			assets.Clear();
			if (sharedSettings != null)
			{
				Object.DestroyImmediate(sharedSettings);
			}
		}

		private VisualElement Live => document.rootVisualElement;

		private static TradeOfferEntry Entry(int slot, BaseItemTemplate template, uint amount, long itemID = 500)
		{
			return new TradeOfferEntry { Slot = slot, ItemID = itemID, TemplateID = template.ID, Seed = 0, Amount = amount };
		}

		private static TradeStateBroadcast State(TradeOfferEntry[] own, long ownCurrency, bool ownAccepted,
			TradeOfferEntry[] partner, long partnerCurrency, bool partnerAccepted, uint version = 3)
		{
			return new TradeStateBroadcast
			{
				Version = version,
				OwnOffer = own ?? Array.Empty<TradeOfferEntry>(),
				OwnCurrency = ownCurrency,
				OwnAccepted = ownAccepted,
				PartnerOffer = partner ?? Array.Empty<TradeOfferEntry>(),
				PartnerCurrency = partnerCurrency,
				PartnerAccepted = partnerAccepted,
			};
		}

		private List<VisualElement> Slots(string gridName)
		{
			VisualElement grid = Live.Q<VisualElement>(gridName);
			LogAssert.IsNotNull(grid, $"{gridName} must exist");
			var slots = new List<VisualElement>();
			foreach (VisualElement child in grid.Children())
			{
				slots.Add(child);
			}
			return slots;
		}

		private static bool IsEmpty(VisualElement slot) => slot.ClassListContains("fish-slot--empty");

		private static Label AmountOf(VisualElement slot) => slot.Q<Label>(className: "fish-slot__amount");

		// ── Opening ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void Opening_ShowsTheWindow_NamesThePartner_AndStatesTheRange()
		{
			trade.OpenWith(22, "Bob", 15.0f);

			LogAssert.IsTrue(trade.Visible, "the window is up");
			LogAssert.IsTrue(trade.SessionOpen, "a session is open");
			LogAssert.AreEqual(22L, trade.PartnerCharacterID, "the partner id is recorded");
			LogAssert.AreEqual("with Bob", Live.Q<Label>("trade-subtitle").text, "the subtitle names the partner");
			LogAssert.AreEqual("Bob", Live.Q<Label>("trade-partner-name").text, "the right column is the partner's");
			LogAssert.AreEqual("You", Live.Q<Label>("trade-own-name").text, "the left column is the player's");
			LogAssert.AreEqual("15m", Live.Q<Label>("trade-range").text, "the range badge states the server's range");
		}

		[Test]
		public void TheDefaultGrid_HasEightSlotsPerSide_LeftIsOwnRightIsPartner()
		{
			trade.OpenWith(22, "Bob", 15.0f);

			VisualElement columns = Live.Q<VisualElement>("trade-columns");
			LogAssert.IsNotNull(columns, "the two-column row exists");
			LogAssert.AreEqual("trade-own", columns[0].name, "the first (left) column is the player's");
			LogAssert.AreEqual("trade-partner", columns[columns.childCount - 1].name, "the last (right) column is the partner's");

			LogAssert.AreEqual(TradeRules.DefaultMaxOfferSlots, Slots("trade-own-grid").Count, "own grid slots");
			LogAssert.AreEqual(TradeRules.DefaultMaxOfferSlots, Slots("trade-partner-grid").Count, "partner grid slots");

			foreach (VisualElement slot in Slots("trade-partner-grid"))
			{
				LogAssert.IsTrue(slot.ClassListContains("trade-slot--readonly"), "partner slots are read-only");
			}
			foreach (VisualElement slot in Slots("trade-own-grid"))
			{
				LogAssert.IsFalse(slot.ClassListContains("trade-slot--readonly"), "own slots are interactive");
			}
		}

		// ── Painting from state ─────────────────────────────────────────────────────────────

		[Test]
		public void ApplyState_PaintsBothTables_FromTheReceiversPointOfView()
		{
			trade.OpenWith(22, "Bob", 15.0f);
			trade.ApplyState(State(
				own: new[] { Entry(3, sword, 1, 501), Entry(7, arrows, 12, 502) }, ownCurrency: 40, ownAccepted: false,
				partner: new[] { Entry(0, arrows, 5, 601) }, partnerCurrency: 250, partnerAccepted: true));

			List<VisualElement> own = Slots("trade-own-grid");
			List<VisualElement> partner = Slots("trade-partner-grid");

			LogAssert.IsFalse(IsEmpty(own[0]), "own slot 0 holds the sword");
			LogAssert.IsFalse(IsEmpty(own[1]), "own slot 1 holds the arrows");
			LogAssert.IsTrue(IsEmpty(own[2]), "own slot 2 is empty");
			LogAssert.AreEqual(DisplayStyle.None, AmountOf(own[0]).style.display.value, "a single sword shows no count");
			LogAssert.AreEqual("12", AmountOf(own[1]).text, "the arrow stack shows its count");
			LogAssert.AreEqual(DisplayStyle.Flex, AmountOf(own[1]).style.display.value, "and the count is visible");

			LogAssert.IsFalse(IsEmpty(partner[0]), "partner slot 0 holds their arrows");
			LogAssert.AreEqual("5", AmountOf(partner[0]).text, "with their count");
			LogAssert.IsTrue(IsEmpty(partner[1]), "partner slot 1 is empty");

			LogAssert.AreEqual(40, Live.Q<IntegerField>("trade-own-currency").value, "own currency field mirrors the table");
			LogAssert.AreEqual("250", Live.Q<Label>("trade-partner-currency").text, "partner currency is a number");

			LogAssert.AreEqual("Not accepted", Live.Q<Label>("trade-own-status").text, "own status");
			LogAssert.AreEqual("Accepted", Live.Q<Label>("trade-partner-status").text, "partner status");
			LogAssert.IsTrue(Live.Q<Label>("trade-partner-status").ClassListContains("trade-column__status--accepted"), "the partner badge carries the accepted class");
			LogAssert.IsFalse(Live.Q<Label>("trade-own-status").ClassListContains("trade-column__status--accepted"), "the own badge does not");

			Button accept = Live.Q<Button>("trade-accept-btn");
			LogAssert.AreEqual("Accept", accept.text, "the player has not accepted, so the button offers to");
			LogAssert.IsTrue(accept.enabledSelf, "and is enabled");
			LogAssert.IsTrue(Live.Q<Label>("trade-status").text.Contains("Bob has accepted"), "the status line says the partner accepted");
		}

		[Test]
		public void AChange_ThatClearsAcceptances_RepaintsBothBadgesNeutral()
		{
			trade.OpenWith(22, "Bob", 15.0f);
			trade.ApplyState(State(null, 0, true, null, 0, true, version: 4));
			LogAssert.AreEqual("Accepted", Live.Q<Label>("trade-own-status").text, "precondition: own accepted");

			trade.ApplyState(State(new[] { Entry(0, sword, 1) }, 0, false, null, 0, false, version: 5));

			LogAssert.AreEqual("Not accepted", Live.Q<Label>("trade-own-status").text, "own badge cleared");
			LogAssert.AreEqual("Not accepted", Live.Q<Label>("trade-partner-status").text, "partner badge cleared");
			LogAssert.IsFalse(Live.Q<Label>("trade-own-status").ClassListContains("trade-column__status--accepted"), "own class removed");
			LogAssert.AreEqual(5u, trade.State.Version, "the panel tracks the latest version to quote back");
		}

		[Test]
		public void BothAccepted_DisablesAcceptAndCancel_AndSaysCompleting()
		{
			trade.OpenWith(22, "Bob", 15.0f);
			trade.ApplyState(State(null, 10, true, null, 0, true));

			LogAssert.IsFalse(Live.Q<Button>("trade-accept-btn").enabledSelf, "nothing left to accept");
			LogAssert.IsFalse(Live.Q<Button>("trade-cancel-btn").enabledSelf, "the exchange is decided; it cannot be cancelled");
			LogAssert.IsFalse(Live.Q<IntegerField>("trade-own-currency").enabledSelf, "the currency field is frozen");
			LogAssert.IsTrue(Live.Q<Label>("trade-status").text.Contains("completing"), "the status says so");
		}

		[Test]
		public void OwnAccepted_OffersToUnaccept()
		{
			trade.OpenWith(22, "Bob", 15.0f);
			trade.ApplyState(State(null, 0, true, null, 0, false));

			LogAssert.AreEqual("Unaccept", Live.Q<Button>("trade-accept-btn").text, "the button withdraws");
			LogAssert.IsTrue(Live.Q<Label>("trade-status").text.Contains("Waiting for Bob"), "waiting on the partner");
		}

		[Test]
		public void TheGrid_GrowsToWhatTheServerSends()
		{
			trade.OpenWith(22, "Bob", 15.0f);

			var many = new TradeOfferEntry[12];
			for (int i = 0; i < many.Length; ++i)
			{
				many[i] = Entry(i, arrows, (uint)(i + 1), 700 + i);
			}
			trade.ApplyState(State(null, 0, false, many, 0, false));

			LogAssert.AreEqual(12, Slots("trade-partner-grid").Count, "the partner grid grew");
			LogAssert.AreEqual(12, Slots("trade-own-grid").Count, "and so did the own grid, so the two sides stay symmetric");
			LogAssert.IsFalse(IsEmpty(Slots("trade-partner-grid")[11]), "the twelfth entry is drawn");
		}

		// ── Closing ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void AServerClose_TakesTheWindowDown_WithoutASession()
		{
			trade.OpenWith(22, "Bob", 15.0f);
			trade.ApplyState(State(new[] { Entry(0, sword, 1) }, 0, false, null, 0, false));

			trade.ApplyClosed(TradeCloseReason.Completed);

			LogAssert.IsFalse(trade.Visible, "the window is down");
			LogAssert.IsFalse(trade.SessionOpen, "no session");
			LogAssert.AreEqual(0L, trade.PartnerCharacterID, "the partner is forgotten");
		}

		[Test]
		public void StateArrivingWithNoSession_IsIgnored()
		{
			trade.ApplyState(State(new[] { Entry(0, sword, 1) }, 0, false, null, 0, false));

			LogAssert.IsFalse(trade.SessionOpen, "no session was opened by a stray state");
			LogAssert.IsFalse(trade.Visible, "and no window");
		}

		// ── Wording ─────────────────────────────────────────────────────────────────────────

		[Test]
		public void EveryCloseReason_AndEveryRequestFailure_HasPlayerWording()
		{
			foreach (TradeCloseReason reason in Enum.GetValues(typeof(TradeCloseReason)))
			{
				string text = UITKTrade.DescribeClose(reason);
				LogAssert.IsFalse(string.IsNullOrWhiteSpace(text), $"{reason} has wording");
			}
			foreach (TradeRequestFailure failure in Enum.GetValues(typeof(TradeRequestFailure)))
			{
				string text = UITKTrade.DescribeRequestFailure(failure);
				LogAssert.IsFalse(string.IsNullOrWhiteSpace(text), $"{failure} has wording");
			}
			foreach (TradeRefusalReason reason in Enum.GetValues(typeof(TradeRefusalReason)))
			{
				string text = UITKTrade.DescribeRefusal(reason);
				LogAssert.IsFalse(string.IsNullOrWhiteSpace(text), $"{reason} has wording");
			}
		}
	}
}
