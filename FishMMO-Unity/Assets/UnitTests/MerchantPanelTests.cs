using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The merchant panel, mounted on a real <see cref="UIDocument"/> and driven the way a player
	/// drives it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two reports, both about numbers on this panel. "Quantity field not populating" — the
	/// transaction footer's quantity box showed nothing to type into or read. And "quantity of
	/// offered items is a little buggy and will sometimes show previous tabs amount" — the header
	/// count badge reporting the tab the player had just left.
	/// </para>
	/// <para>
	/// Reading the source could not settle either. The first is a question about what the UXML
	/// actually resolves to and how it is themed, and the second is about a callback that survives
	/// the tab switch that was supposed to replace it — neither is visible without a tree. These
	/// mount the real UXML on the real panel settings and assert against the tree the player sees.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MerchantPanelTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Merchant/UIMerchant.uxml";

		private GameObject host;
		private UITKMerchant merchant;
		private UIDocument document;
		private PanelSettings sharedSettings;

		[SetUp]
		public void SetUp()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the merchant UXML must exist at {UxmlPath}");

			sharedSettings = Object.Instantiate(settings);

			host = new GameObject("UIMerchant");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = sharedSettings;
			document.visualTreeAsset = uxml;

			// A start-hidden panel: the document is off until something shows it.
			document.enabled = false;

			merchant = host.AddComponent<UITKMerchant>();
			merchant.Document = document;
			merchant.StartOpen = false;
			merchant.IsAlwaysOpen = false;
			merchant.CloseOnQuitToMenu = true;
			merchant.ReleasesCursor = false;
			merchant.CloseOnEscape = true;

			MethodInfo awake = typeof(UITKControl).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake, "UITKControl must still declare Awake");
			awake.Invoke(merchant, null);

			merchant.Show();
			LogAssert.IsTrue(merchant.Visible, "the panel must be up before anything is asserted about its tree");
		}

		[TearDown]
		public void TearDown()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
		}

		private VisualElement Live => document.rootVisualElement;

		private static void Invoke(object target, string method, params object[] args)
		{
			MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKMerchant must still declare {method}");
			info.Invoke(target, args);
		}

		private T Field<T>(string name) where T : class
		{
			FieldInfo info = typeof(UITKMerchant).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKMerchant must still declare {name}");
			return info.GetValue(merchant) as T;
		}

		/// <summary>Reads an int field off the panel, for the ones <see cref="Field{T}"/> cannot box.</summary>
		private int Number(string name)
		{
			FieldInfo info = typeof(UITKMerchant).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"UITKMerchant must still declare {name}");
			return (int)info.GetValue(merchant);
		}

		/// <summary>Selects an entry through the panel's own selection path.</summary>
		private void Select(VisualElement row, int unitPrice, int maxQuantity, string name, bool isSale, long saleItemID)
		{
			Invoke(merchant, "SelectEntry", row, MerchantTabType.Item, 0, unitPrice, maxQuantity, name, isSale, saleItemID);
		}

		/// <summary>Relayouts a container the way adding or hiding rows does.</summary>
		private static void Relayout(VisualElement element)
		{
			using (GeometryChangedEvent evt = GeometryChangedEvent.GetPooled(new Rect(0, 0, 100, 10), new Rect(0, 0, 100, 40)))
			{
				evt.target = element;
				element.SendEvent(evt);
			}
		}

		private static VisualElement Rows(VisualElement container, int count)
		{
			container.Clear();
			for (int i = 0; i < count; ++i)
			{
				container.Add(new VisualElement());
			}
			return container;
		}

		[Test]
		public void TheQuantityFieldExistsInTheTreeThePlayerSees()
		{
			/* The report was "quantity field not populating". A field the UXML failed to
			 * instantiate resolves to null, every ± and Max press then writes nowhere, and
			 * CurrentQuantity silently answers 1 — which looks exactly like a field that does not
			 * populate. */
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");
			LogAssert.IsNotNull(field, "the footer's quantity field must exist in the cloned tree");
			LogAssert.AreSame(field, Field<IntegerField>("quantityField"),
				"the panel must have cached the live field, not one from a discarded tree");
		}

		[Test]
		public void TheQuantityFieldIsThemedLikeEveryOtherInput()
		{
			/* fish-input is the project's rule for a text-entry control, and it is not decoration:
			 * it colours the inner .unity-text-element — which UITK's default theme paints near
			 * black — and gives it flex-grow. Every other input in the project carries it (the
			 * colour picker's channel fields, chat, login); this one was the exception. */
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");
			LogAssert.IsNotNull(field, "the footer's quantity field must exist");
			LogAssert.IsTrue(field.ClassListContains("fish-input"),
				"the quantity field must carry fish-input or its text is left at UITK's near-black default on a dark panel");
		}

		[Test]
		public void TheQuantityFieldDoesNotClampMidWord()
		{
			/* An IntegerField reports a change per keystroke. Clamping on every one of them makes
			 * the box untypeable: "12" is rewritten to "1" the moment the 1 lands. */
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");
			LogAssert.IsTrue(field.isDelayed,
				"the quantity field must apply its clamp when the player is done, not per character");
		}

		[Test]
		public void ASingleItemEntryOffersNoQuantityAtAll()
		{
			/* MaxStackSize is 1 for anything non-stacking — and 0 is read as 1 on both sides — so
			 * most merchant entries can only be bought one at a time. There is no choice to make,
			 * so the controls are not shown. They used to stay live and clamp every input straight
			 * back to 1, which is a field that looks broken rather than one that looks full. */
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");
			Button less = Live.Q<Button>("merchant-qty-less");
			Button more = Live.Q<Button>("merchant-qty-more");
			Button max = Live.Q<Button>("merchant-qty-max");
			Label total = Live.Q<Label>("merchant-transaction-total");

			Select(new VisualElement(), 25, 1, "Test Sword", false, 0);

			LogAssert.AreEqual(DisplayStyle.None, field.style.display.value, "no quantity box for a single item");
			LogAssert.AreEqual(DisplayStyle.None, less.style.display.value, "nothing to decrement");
			LogAssert.AreEqual(DisplayStyle.None, more.style.display.value, "nothing to increment");
			LogAssert.AreEqual(DisplayStyle.None, max.style.display.value, "and Max has nothing to go to");

			LogAssert.AreEqual("-25", total.text, "the cost still has to be visible");
			LogAssert.AreEqual(1, field.value, "and the request the confirm sends is still one");
		}

		[Test]
		public void AStackableSelectionKeepsItsQuantityControls()
		{
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");
			Button more = Live.Q<Button>("merchant-qty-more");

			Select(new VisualElement(), 25, 20, "Bread", false, 0);

			LogAssert.AreEqual(DisplayStyle.Flex, field.style.display.value, "a stackable selection gets its box");
			LogAssert.AreEqual(DisplayStyle.Flex, more.style.display.value, "and its adjusters");
		}

		[Test]
		public void TheControlsComeBackWhenAStackableEntryIsSelectedAfterASingleOne()
		{
			/* Hiding on selection has to be undone on the next selection, or the first
			 * non-stacking item the player clicks kills the quantity box for the rest of the
			 * session. */
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");

			Select(new VisualElement(), 25, 1, "Test Sword", false, 0);
			LogAssert.AreEqual(DisplayStyle.None, field.style.display.value, "hidden for the sword");

			Select(new VisualElement(), 4, 20, "Bread", false, 0);
			LogAssert.AreEqual(DisplayStyle.Flex, field.style.display.value, "and back for the bread");
		}

		[Test]
		public void SelectingAnEntryPopulatesTheQuantityFieldAndTotal()
		{
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");
			Label total = Live.Q<Label>("merchant-transaction-total");
			VisualElement footer = Live.Q("merchant-transaction");

			Select(new VisualElement(), 25, 10, "Bread", false, 0);

			LogAssert.AreEqual(DisplayStyle.Flex, footer.style.display.value, "selecting opens the footer");
			LogAssert.AreEqual(1, field.value, "a fresh selection starts at one");
			LogAssert.AreEqual("-25", total.text, "the total is the unit price at quantity one");
		}

		[Test]
		public void TheQuantityControlsWriteThroughToTheField()
		{
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");
			Label total = Live.Q<Label>("merchant-transaction-total");

			Select(new VisualElement(), 25, 10, "Bread", false, 0);

			Invoke(merchant, "NudgeQuantity", 1);
			LogAssert.AreEqual(2, field.value, "the + button raises the quantity");
			LogAssert.AreEqual("-50", total.text, "and the total follows it");

			Invoke(merchant, "SetQuantity", 10);
			LogAssert.AreEqual(10, field.value, "Max goes to the selection's ceiling");
			LogAssert.AreEqual("-250", total.text, "and the total follows that too");

			Invoke(merchant, "SetQuantity", 999);
			LogAssert.AreEqual(10, field.value, "an over-large quantity is clamped to the ceiling");

			Invoke(merchant, "SetQuantity", 0);
			LogAssert.AreEqual(1, field.value, "and a zero or negative one is clamped to one");
		}

		[Test]
		public void TypingAQuantityIsClampedInTheFieldItself()
		{
			/* The clamp runs from inside a ChangeEvent dispatch, where assigning .value would be
			 * queued and re-entered. SetValueWithoutNotify is what makes the corrected number
			 * appear in the box rather than the number that was typed. */
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");
			Select(new VisualElement(), 5, 3, "Bread", false, 0);

			field.value = 99;

			LogAssert.AreEqual(3, field.value, "a hand-typed quantity above the ceiling is written back clamped");
		}

		/// <summary>
		/// The reported defect itself: the box has to be tall enough for the element that draws the
		/// number, and that element has to be painted in a colour that shows.
		/// </summary>
		/// <remarks>
		/// Issue #263 was reported as "the quantity is not being rendered in the quantity box — is it
		/// due to the background colour being the same as the value font colour?", and #277 as "doesn't
		/// populate, can't edit it". Colour was never involved: measured, the glyph box resolves to
		/// this theme's own light text colour on the panel's dark ground the whole time, and the
		/// second half of this test keeps it that way.
		/// <para>
		/// The height is the defect. Unity's default theme pads the editable surface inside a text
		/// field by 10px top and bottom, sized for a 36px inspector row. The quantity box is pinned to
		/// 22px, so 22 − 10 − 10 − 1 − 1 leaves the element that stretches to that surface — the one
		/// that draws the characters — nothing at all, and it resolves to zero height. Nothing is
		/// drawn in the box and there is nothing to click into, which is both reports at once. The fix
		/// trims that padding on the field's own <c>fish-input--compact</c> class
		/// (FishMMO-Theme.uss); this asserts it stays trimmed.
		/// </para>
		/// <para>
		/// A coroutine, because layout settles over frames rather than inside the call that changed
		/// the tree — the same reason ColorPickerTests.TheHexFieldHasRoomToShowItsCode is one. The
		/// field's own height is asserted first and on purpose: it is what shows the numbers below are
		/// a laid-out tree rather than a tree that was never measured, where every size is zero and a
		/// "greater than zero" check could not tell a fix from a failure.
		/// </para>
		/// </remarks>
		[UnityTest]
		public IEnumerator TheQuantityBoxHasRoomToDrawItsNumber()
		{
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");

			// A stackable selection: the quantity controls exist only when there is a quantity to choose.
			Select(new VisualElement(), 25, 20, "Bread", false, 0);

			for (int frame = 0; frame < 10; ++frame)
			{
				yield return null;
			}

			VisualElement input = field.Q("unity-text-input");
			VisualElement glyph = input?.Q(className: "unity-text-element");

			LogAssert.IsNotNull(input, "the field's editable surface (#unity-text-input) must exist");
			LogAssert.IsNotNull(glyph, "and the element inside it that draws the characters");

			LogAssert.IsTrue(Mathf.Abs(field.layout.height - 22f) < 0.5f,
				$"the panel's own 22px box must survive the fix; it laid out at {field.layout.height}");
			LogAssert.IsTrue(glyph.layout.height > 0f,
				$"the element that draws the number must have a height to draw in; it resolved to " +
				$"{glyph.layout.width}x{glyph.layout.height}, so the box renders empty and cannot be " +
				"clicked into (issues #263 and #277)");
			LogAssert.IsTrue(glyph.layout.width > 0f,
				$"and a width; it resolved to {glyph.layout.width}x{glyph.layout.height}");

			/* The half of the report that named a cause, so it cannot come back: the number must be
			 * drawn in a colour that stands off the box behind it. */
			Color number = glyph.resolvedStyle.color;
			LogAssert.IsTrue(number.a > 0f, "the number must not be drawn fully transparent");
			LogAssert.IsTrue(number.r + number.g + number.b >= 1.5f,
				$"the number must be drawn in the panel's light text colour, not a dark one that " +
				$"disappears into the box; it resolved to {number}");
			LogAssert.AreNotEqual(field.resolvedStyle.backgroundColor, number,
				"and it must not be the colour it is sitting on");
		}

		/// <summary>
		/// Max on a sale must reach the whole slot, which is what #277 asked for in as many words:
		/// "Max Sell Quantity at a time should be max stack size."
		/// </summary>
		/// <remarks>
		/// The ceiling is the row's, and a sell row's ceiling is the stack in the bag slot — the
		/// server caps a sale at exactly that (<c>InteractableSystem.Merchant</c>: the requested
		/// quantity is clamped to the slot's own amount), so asking for the whole stack is honoured
		/// rather than quietly reduced. Nothing had to change here for that: the box simply had to
		/// render, which is the test above.
		/// </remarks>
		[Test]
		public void MaxOnASaleAsksForTheWholeSlot()
		{
			IntegerField field = Live.Q<IntegerField>("merchant-qty-field");
			Label total = Live.Q<Label>("merchant-transaction-total");
			Button max = Live.Q<Button>("merchant-qty-max");

			// A twenty-strong stack in a bag slot, as BuildSellEntries reports it.
			Select(new VisualElement(), 4, 20, "Bread", true, 123L);

			LogAssert.AreEqual(20, Number("selectedMaxQuantity"),
				"a sell row's ceiling must be the stack in the slot, not one");
			LogAssert.AreEqual(DisplayStyle.Flex, max.style.display.value, "a stack gets its adjusters");

			// Exactly what the Max button does.
			Invoke(merchant, "SetQuantity", Number("selectedMaxQuantity"));

			LogAssert.AreEqual(20, field.value, "Max must fill the box with the stack");
			LogAssert.AreEqual("+80", total.text, "and the payout is the whole stack's");
		}

		[Test]
		public void ASaleSelectionIsDroppedWhenItsRowCannotBeRebuilt()
		{
			/* The sell list is torn down and rebuilt whenever the bags change, and a sale
			 * selection names a SLOT. Left alone across a rebuild, the footer would still say
			 * "Sell 5 x Bread" over a slot that had since been emptied and refilled — and
			 * confirming would sell whatever is in it now. */
			VisualElement footer = Live.Q("merchant-transaction");

			Select(new VisualElement(), 4, 5, "Bread", true, 123L);
			LogAssert.AreEqual(DisplayStyle.Flex, footer.style.display.value, "the sale selection opened the footer");

			// No merchant template is loaded here, so the rebuild produces no rows at all.
			Invoke(merchant, "BuildSellEntries");

			LogAssert.AreEqual(DisplayStyle.None, footer.style.display.value,
				"a sale selection whose row did not come back must be dropped, not left pointing at a slot");
		}

		[Test]
		public void APurchaseSelectionSurvivesASellListRebuild()
		{
			/* The buy side reads a merchant template's own list, which does not move under it. */
			VisualElement footer = Live.Q("merchant-transaction");

			Select(new VisualElement(), 25, 10, "Bread", false, 0);
			Invoke(merchant, "BuildSellEntries");

			LogAssert.AreEqual(DisplayStyle.Flex, footer.style.display.value,
				"a purchase selection has nothing to do with the sell list");
		}

		[Test]
		public void TheHeaderCountDescribesTheVisibleTab()
		{
			Label count = Live.Q<Label>("merchant-count");
			Label subtitle = Live.Q<Label>("merchant-subtitle");

			Rows(Field<VisualElement>("itemsList"), 3);
			Rows(Field<VisualElement>("abilitiesList"), 7);

			Invoke(merchant, "SwitchTab", MerchantTabType.Item);
			LogAssert.AreEqual("3", count.text, "the items tab reports the item rows");
			LogAssert.AreEqual("3 offers", subtitle.text, "and so does the subtitle");

			Invoke(merchant, "SwitchTab", MerchantTabType.Ability);
			LogAssert.AreEqual("7", count.text, "the abilities tab reports the ability rows");
		}

		[Test]
		public void AHiddenTabRelayoutDoesNotOverwriteTheVisibleTabsCount()
		{
			/* The reported defect. Every tab switch pointed the SAME badge at a new container
			 * without taking the previous binding down, so the container the player had just left
			 * kept its geometry callback — and relayouted the moment it was hidden, or the next
			 * time its own contents were rebuilt in the background. Whichever container moved last
			 * won the badge, which is how it came to show the previous tab's number. */
			Label count = Live.Q<Label>("merchant-count");
			Label subtitle = Live.Q<Label>("merchant-subtitle");

			VisualElement items = Rows(Field<VisualElement>("itemsList"), 3);
			Rows(Field<VisualElement>("abilitiesList"), 7);

			Invoke(merchant, "SwitchTab", MerchantTabType.Item);
			Invoke(merchant, "SwitchTab", MerchantTabType.Ability);

			Relayout(items);

			LogAssert.AreEqual("7", count.text, "the hidden items tab must not write its count over the visible one");
			LogAssert.AreEqual("7 offers", subtitle.text, "nor over the subtitle");
		}

		[Test]
		public void AHiddenTabRebuiltInTheBackgroundDoesNotOverwriteTheCount()
		{
			/* The sell list is rebuilt off inventory updates whether or not its tab is showing, so
			 * this is the same defect arriving without any tab switch at all. */
			Label count = Live.Q<Label>("merchant-count");

			VisualElement events = Rows(Field<VisualElement>("eventsList"), 2);
			Rows(Field<VisualElement>("itemsList"), 5);

			Invoke(merchant, "SwitchTab", MerchantTabType.AbilityEvent);
			Invoke(merchant, "SwitchTab", MerchantTabType.Item);

			// Contents change behind the hidden tab, exactly as a background rebuild would.
			Rows(events, 9);
			Relayout(events);

			LogAssert.AreEqual("5", count.text, "a background rebuild of a hidden tab must not touch the badge");
		}

		[Test]
		public void TheVisibleTabStillTracksItsOwnRows()
		{
			/* The unbinding must not go so far as to stop the ACTIVE list driving its own chrome —
			 * that is the whole point of the binding. */
			Label count = Live.Q<Label>("merchant-count");
			Label empty = Live.Q<Label>("merchant-empty");

			VisualElement items = Rows(Field<VisualElement>("itemsList"), 0);
			Invoke(merchant, "SwitchTab", MerchantTabType.Item);

			LogAssert.AreEqual("0", count.text, "an empty tab reports zero");
			LogAssert.AreEqual(DisplayStyle.Flex, empty.style.display.value, "and shows the empty placeholder");

			Rows(items, 4);
			Relayout(items);

			LogAssert.AreEqual("4", count.text, "the visible tab still drives its own count");
			LogAssert.AreEqual(DisplayStyle.None, empty.style.display.value, "and hides the placeholder once it has rows");
		}

		[Test]
		public void ReopeningThePanelDoesNotStrandTheOldTreesChrome()
		{
			/* Hiding a panel disables its UIDocument, which discards the tree; showing it clones a
			 * fresh one. The bindings held across that boundary point at labels nobody can see. */
			Rows(Field<VisualElement>("itemsList"), 3);
			Invoke(merchant, "SwitchTab", MerchantTabType.Item);

			merchant.Hide();
			merchant.Show();

			Label count = Live.Q<Label>("merchant-count");
			Rows(Field<VisualElement>("itemsList"), 6);
			Invoke(merchant, "SwitchTab", MerchantTabType.Item);

			LogAssert.AreEqual("6", count.text, "the rebuilt tree's badge is the one being written");
		}
	}
}
