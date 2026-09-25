using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet;
using FishNet.Transporting;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit hotkey bar. Renders the character's hotkey slots as dynamically generated
	/// VisualElements and owns their network behaviour:
	/// <see cref="HotkeySetBroadcast"/> / <see cref="HotkeySetMultipleBroadcast"/> assignment,
	/// cooldown sweeps via <see cref="ICooldownController"/>, drag-assign / drag-clear via the
	/// shared <see cref="UITKDragObject"/> overlay, and Input System activation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Model / view split.</b> <see cref="bindings"/> is plain data describing what is bound to
	/// each slot and belongs to the character; <see cref="slots"/> holds the elements currently
	/// rendering it and belongs to ONE visual tree. A <c>UIDocument</c> clones the UXML afresh when
	/// re-enabled (which hiding used to cause on every hide/show), so any element cached across that
	/// is a pointer into a discarded tree — the
	/// old code kept its slot state exclusively in those elements and rebuilt them by APPENDING to
	/// a list it never cleared, so a rebuilt tree produced twelve more orphaned slots and a bar
	/// whose bindings had quietly detached from what the player could see.
	/// </para>
	/// <para>
	/// <b>Change-driven, not per-frame.</b> This panel used to rewrite all twelve slots' icons and
	/// display styles on every single frame from <c>Update</c> — twelve <c>TryGet</c> calls,
	/// twelve dictionary probes and up to twenty-four inline style writes at frame rate for a bar
	/// that changes a handful of times per session. Refreshes are now driven by the events that
	/// can actually invalidate a binding (container slot updates, equip/unequip, ability learned,
	/// hotkey broadcast) and coalesced to at most one sweep per frame.
	/// </para>
	/// <para>
	/// <b>Several bars, one model.</b> A game may give every character more than one bar
	/// (<see cref="Constants.Configuration.HotkeyBarCount"/>). They are drawn as rows of one
	/// strip, the first at the bottom, and all of them index the same flat slot list the server
	/// knows — <see cref="HotkeyBarLayout"/> is the only place that thinks in bars. The player
	/// chooses in Options whether every row is on screen or one at a time
	/// (<see cref="ClientHotbarSettings"/>); either way the same keys reach the same slots, a
	/// position key plus a bar's modifier (see <see cref="HotkeyKeyMap"/>).
	/// </para>
	/// </remarks>
	public class UITKHotkeyBar : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>Name of the container element that holds the generated hotkey slots.</summary>
		private const string LIST_NAME = "hotkey-list";

		/// <summary>USS class applied to each generated bar: one row of slots.</summary>
		private const string ROW_CLASS = "hotkey-row";

		/// <summary>USS class of the page controls beside a paged bar.</summary>
		private const string PAGER_CLASS = "hotkey-pager";

		/// <summary>USS class of the page up and page down buttons.</summary>
		private const string PAGER_BUTTON_CLASS = "hotkey-pager__button";

		/// <summary>USS class of the page number between the buttons.</summary>
		private const string PAGER_LABEL_CLASS = "hotkey-pager__label";

		/// <summary>USS class applied to each generated hotkey slot root.</summary>
		private const string SLOT_CLASS = "hotkey-slot";

		/// <summary>USS class applied to each slot's icon element.</summary>
		private const string ICON_CLASS = "hotkey-slot__icon";

		/// <summary>USS class applied to each slot's cooldown sweep overlay.</summary>
		private const string COOLDOWN_CLASS = "hotkey-slot__cooldown";

		/// <summary>USS class applied to each slot's key-map label.</summary>
		private const string LABEL_CLASS = "hotkey-slot__label";

		/// <summary>Name of the shared drag overlay registered with the UIManager.</summary>
		private const string DRAG_OBJECT_NAME = UITKDragObject.CONTROL_NAME;

		/// <summary>Name of the shared tooltip overlay registered with the UIManager.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		/// <summary>
		/// What a single hotkey slot is bound to. Plain data — no <see cref="VisualElement"/> —
		/// so it survives every rebuild of the visual tree.
		/// </summary>
		private struct HotkeyBinding
		{
			/// <summary>The reference type currently assigned to the slot.</summary>
			public ReferenceButtonType Type;
			/// <summary>The reference ID currently assigned to the slot.</summary>
			public long ReferenceID;
		}

		/// <summary>
		/// Visual elements rendering a single hotkey slot, plus the state needed to avoid
		/// rewriting them when nothing changed.
		/// </summary>
		private sealed class HotkeySlot
		{
			/// <summary>Root container for the slot.</summary>
			public VisualElement Root;
			/// <summary>Icon element showing the assigned item/ability sprite.</summary>
			public VisualElement Icon;
			/// <summary>Cooldown sweep overlay (height driven from C#).</summary>
			public VisualElement Cooldown;
			/// <summary>Key-map label (e.g. "1", "LMB").</summary>
			public Label Label;
			/// <summary>The fixed hotkey slot index.</summary>
			public int Index;

			/// <summary>
			/// The sprite currently written into <see cref="Icon"/>.
			/// </summary>
			/// <remarks>
			/// Kept so the refresh sweep can skip the inline style write when the icon has not
			/// actually changed. Writing an identical <c>StyleBackground</c> still dirties the
			/// element and costs a repaint, which is most of what made the old per-frame refresh
			/// expensive.
			/// </remarks>
			public Sprite AppliedSprite;

			/// <summary>
			/// Whether <see cref="Icon"/> was last written for a slot holding something.
			/// </summary>
			/// <remarks>
			/// Part of the change detection, because the sprite alone cannot carry it. A slot
			/// going from empty to bound-but-iconless has a null sprite on both sides, so a
			/// comparison of sprites alone reports "unchanged" and the placeholder is never
			/// drawn — the exact case this field exists to catch.
			/// </remarks>
			public bool AppliedOccupied;

			/// <summary>The cooldown sweep fraction currently written into <see cref="Cooldown"/>.</summary>
			public float AppliedCooldownFraction = -1.0f;

			/// <summary>True when the held activation still owes the controller a Release().</summary>
			public bool AwaitingRelease;
		}

		/// <summary>What each slot is bound to. Index-aligned with <see cref="slots"/>.</summary>
		private HotkeyBinding[] bindings;

		/// <summary>
		/// The binding a right-click lifted off the bar, and the slot it came from (-1 when none).
		/// </summary>
		/// <remarks>
		/// A lifted shortcut used to be seeded onto the cursor as the item drag it pointed at — a bag
		/// slot index typed Inventory, or a socket typed Equipment — so pressing a bag slot next moved
		/// the real item, and pressing a socket unequipped it. The cursor now carries
		/// <see cref="ReferenceButtonType.Hotkey"/> with the source slot, which only this bar accepts,
		/// and what it lifted is remembered here so a drop can bind it again. The slot itself is
		/// cleared at lift time, as it always was: a right-click IS the gesture that removes a
		/// shortcut, and putting the drag down anywhere else simply leaves it removed.
		/// </remarks>
		private HotkeyBinding liftedBinding;
		private int liftedSlotIndex = -1;

		/// <summary>
		/// All created hotkey slots in flat slot order, every bar's in turn. Belongs to the current
		/// visual tree.
		/// </summary>
		private readonly List<HotkeySlot> slots = new List<HotkeySlot>();

		/// <summary>One row element per bar, in bar order. Belongs to the current visual tree.</summary>
		private readonly List<VisualElement> rows = new List<VisualElement>();

		/// <summary>The container element that holds the generated bar rows.</summary>
		private VisualElement list;

		/// <summary>The page controls, present only when there is more than one bar.</summary>
		private VisualElement pager;

		/// <summary>The page number shown between the page buttons.</summary>
		private Label pageLabel;

		/// <summary>Pairs each position key's press with its release on the bar the press landed on.</summary>
		private readonly HotkeyPressRouter pressRouter = new HotkeyPressRouter(HotkeyBarLayout.SlotsPerBar);

		/// <summary>The bar a paged hotbar shows while no modifier is held.</summary>
		private int restingPage;

		/// <summary>The bar a paged hotbar is showing right now: a held modifier's, else the resting page.</summary>
		private int shownPage;

		/// <summary>Whether the page keys were down last frame, for their press edges.</summary>
		private bool pageUpHeld;
		private bool pageDownHeld;

		/// <summary>Set when the key bindings changed, so the key hints are redrawn on the next tick.</summary>
		private bool labelsDirty;

		/// <summary>
		/// Set when something happened that could have invalidated a binding or an icon.
		/// Consumed by the next <see cref="OnTick"/>.
		/// </summary>
		/// <remarks>
		/// Coalescing matters more than it looks: the server delivers a full bar as one
		/// <see cref="HotkeySetMultipleBroadcast"/> which is unpacked into one set per slot, and
		/// loading a character's inventory raises one slot-updated event per occupied slot. Doing
		/// the sweep inline would run it dozens of times for a single logical change.
		/// </remarks>
		private bool bindingsDirty = true;

		/// <summary>True while at least one slot is showing a cooldown sweep.</summary>
		private bool anyCooldownActive;

		/// <summary>
		/// Queries the list container, builds the hotkey slots and subscribes to cooldown events.
		/// </summary>
		/// <remarks>
		/// Runs again if the tree is ever replaced, so everything here is written to be idempotent: the
		/// slot list is cleared before it is rebuilt, and the static cooldown subscriptions are
		/// removed before they are added. A bare <c>+=</c> on a static event from a hook that can
		/// re-run is an unbounded handler leak.
		/// </remarks>
		public override void OnStarting()
		{
			EnsureBindings();

			/* On a re-run the elements in `slots` belong to the tree that was just replaced.
			 * Dropping them first is what stops BuildBars appending a second set. */
			slots.Clear();
			rows.Clear();
			list = null;
			pager = null;
			pageLabel = null;

			VisualElement root = Root;
			if (root != null)
			{
				list = root.Q(LIST_NAME);
			}

			BuildBars(HotkeyBarLayout.BarCount);

			ICooldownController.OnAddCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnAddCooldown += CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnUpdateCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnUpdateCooldown += CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnRemoveCooldown -= CooldownController_OnRemoveCooldown;
			ICooldownController.OnRemoveCooldown += CooldownController_OnRemoveCooldown;

			/* Static events, so removed before they are added for the reason the cooldown ones are:
			 * this hook re-runs whenever the tree is replaced. */
			ClientHotbarSettings.OnChanged -= ClientHotbarSettings_OnChanged;
			ClientHotbarSettings.OnChanged += ClientHotbarSettings_OnChanged;
			PlayerInputController.OnBindingsChanged -= PlayerInputController_OnBindingsChanged;
			PlayerInputController.OnBindingsChanged += PlayerInputController_OnBindingsChanged;

			restingPage = ClientHotbarSettings.Page;
			shownPage = restingPage;
			ApplyLayout();

			/* The hints are written as the slots are built, but the key bindings they read may not
			 * exist yet at that moment, and the notice that they were loaded may have gone out before
			 * this subscribed. One refresh on the first tick costs nothing and cannot be missed. */
			labelsDirty = true;
			bindingsDirty = true;
		}

		/// <summary>
		/// Re-renders the bar from the binding model after the visual tree was rebuilt.
		/// </summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			bindingsDirty = true;
			RefreshAllSlots();
		}

		/// <summary>
		/// Unsubscribes from cooldown events when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ICooldownController.OnAddCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnUpdateCooldown -= CooldownController_OnAddOrUpdateCooldown;
			ICooldownController.OnRemoveCooldown -= CooldownController_OnRemoveCooldown;
			ClientHotbarSettings.OnChanged -= ClientHotbarSettings_OnChanged;
			PlayerInputController.OnBindingsChanged -= PlayerInputController_OnBindingsChanged;

			UnsubscribeCharacterEvents();

			base.OnDestroying();
		}

		/// <summary>
		/// Registers the hotkey assignment broadcasts when the client connection is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<HotkeySetBroadcast>(OnClientHotkeySetBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<HotkeySetMultipleBroadcast>(OnClientHotkeySetMultipleBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the hotkey assignment broadcasts when the client connection is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HotkeySetBroadcast>(OnClientHotkeySetBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<HotkeySetMultipleBroadcast>(OnClientHotkeySetMultipleBroadcastReceived);
		}

		/// <summary>
		/// Drops the previous character's subscriptions before a new character is applied.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			UnsubscribeCharacterEvents();
		}

		/// <summary>
		/// Subscribes to the events that can invalidate a hotkey binding.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			SubscribeCharacterEvents();

			bindingsDirty = true;
			RefreshAllSlots();
		}

		/// <summary>
		/// Drops this character's subscriptions before it is cleared.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			UnsubscribeCharacterEvents();
		}

		/// <summary>
		/// Clears the binding model so one character's bar cannot appear on the next one's.
		/// </summary>
		/// <remarks>
		/// The model deliberately outlives the visual tree, which means it also outlives the
		/// character unless it is cleared here. A newly selected character with an empty bar
		/// generates no hotkey traffic at all, so nothing would ever overwrite the previous
		/// character's bindings.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			ClearAllBindings();
		}

		/// <inheritdoc />
		public override void OnQuitToLogin()
		{
			ClearAllBindings();

			base.OnQuitToLogin();
		}

		/// <summary>
		/// Allocates the binding model if it does not exist yet.
		/// </summary>
		private void EnsureBindings()
		{
			int count = Constants.Configuration.MaximumPlayerHotkeys;
			if (bindings != null && bindings.Length == count)
			{
				return;
			}

			bindings = new HotkeyBinding[count];
			for (int i = 0; i < count; ++i)
			{
				bindings[i].Type = ReferenceButtonType.None;
				bindings[i].ReferenceID = ReferenceButton.NULL_REFERENCE_ID;
			}
		}

		/// <summary>
		/// Empties every binding and repaints the bar.
		/// </summary>
		private void ClearAllBindings()
		{
			EnsureBindings();

			for (int i = 0; i < bindings.Length; ++i)
			{
				bindings[i].Type = ReferenceButtonType.None;
				bindings[i].ReferenceID = ReferenceButton.NULL_REFERENCE_ID;
			}

			for (int i = 0; i < slots.Count; ++i)
			{
				ApplySlotSprite(slots[i], null, occupied: false);
				ApplyCooldownFraction(slots[i], 0.0f);
				slots[i].AwaitingRelease = false;
			}
			pressRouter.Reset();

			anyCooldownActive = false;
			bindingsDirty = false;
		}

		/// <summary>
		/// Subscribes to the container and ability events that can invalidate a binding.
		/// </summary>
		private void SubscribeCharacterEvents()
		{
			if (Character == null)
			{
				return;
			}

			if (Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated += Container_OnSlotUpdated;
			}
			if (Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated += Container_OnSlotUpdated;
				equipmentController.OnItemEquipped += Equipment_OnItemChanged;
				equipmentController.OnItemUnequipped += Equipment_OnItemChanged;
			}
			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnAddAbility += Ability_OnAddAbility;
				abilityController.OnRemoveAbility += Ability_OnRemoveAbility;
				abilityController.OnReset += Ability_OnReset;
			}
		}

		/// <summary>
		/// Drops every character-scoped subscription this panel holds.
		/// </summary>
		private void UnsubscribeCharacterEvents()
		{
			if (Character == null)
			{
				return;
			}

			if (Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.OnSlotUpdated -= Container_OnSlotUpdated;
			}
			if (Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated -= Container_OnSlotUpdated;
				equipmentController.OnItemEquipped -= Equipment_OnItemChanged;
				equipmentController.OnItemUnequipped -= Equipment_OnItemChanged;
			}
			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnAddAbility -= Ability_OnAddAbility;
				abilityController.OnRemoveAbility -= Ability_OnRemoveAbility;
				abilityController.OnReset -= Ability_OnReset;
			}
		}

		/// <summary>Marks the bar for a refresh when a container slot changed.</summary>
		private void Container_OnSlotUpdated(IItemContainer container, Item item, int slotIndex) => bindingsDirty = true;

		/// <summary>Marks the bar for a refresh when equipment changed.</summary>
		private void Equipment_OnItemChanged(Item item, ItemSlot slot) => bindingsDirty = true;

		/// <summary>Marks the bar for a refresh when an ability was learned.</summary>
		private void Ability_OnAddAbility(Ability ability) => bindingsDirty = true;

		/// <summary>Marks the bar for a refresh when an ability was forgotten.</summary>
		private void Ability_OnRemoveAbility(long referenceID) => bindingsDirty = true;

		/// <summary>Marks the bar for a refresh when the ability set was replaced wholesale.</summary>
		private void Ability_OnReset() => bindingsDirty = true;

		/// <summary>
		/// Builds one row of slots per bar, and the page controls when there is more than one bar.
		/// </summary>
		/// <remarks>
		/// Rows are added in bar order to a <c>column-reverse</c> list, so the first bar is the
		/// bottom row and stays exactly where the single strip always was; the rows above it are the
		/// ones the panels over the bar make room for (<see cref="UITKHudLayout"/>). Every slot goes
		/// into <see cref="slots"/> at its flat index whatever row it is drawn in.
		/// </remarks>
		/// <param name="bars">
		/// How many bars to build: the game's <see cref="HotkeyBarLayout.BarCount"/>, passed in so
		/// the stacked geometry can be measured for several bars in a game that ships one.
		/// </param>
		private void BuildBars(int bars)
		{
			if (list == null)
			{
				return;
			}

			// The list is the UXML's own element, so on a re-run over the same tree it still holds
			// the previous rows.
			list.Clear();

			int perBar = HotkeyBarLayout.SlotsPerBar;
			for (int bar = 0; bar < bars; ++bar)
			{
				VisualElement row = new VisualElement();
				row.AddToClassList(ROW_CLASS);
				row.pickingMode = PickingMode.Ignore;

				for (int position = 0; position < perBar; ++position)
				{
					HotkeySlot slot = CreateSlot(HotkeyBarLayout.SlotIndex(bar, position));
					row.Add(slot.Root);
					slots.Add(slot);
				}

				list.Add(row);
				rows.Add(row);
			}

			if (bars > 1)
			{
				BuildPager();
			}
		}

		/// <summary>
		/// Builds the page up / page number / page down column that sits beside a paged bar.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Absolutely positioned against the list, so it takes no part in the strip's width and the
		/// bar stays centred on the screen whether the pager is showing or not — the resource bars
		/// under the HUD's centre line are placed against the same centre.
		/// </para>
		/// <para>
		/// Paging is a PRESS, like binding, and the buttons wear the slot marker for the same reason
		/// the slots do: a player carrying an ability to the second bar pages to it and puts it down,
		/// so the press that turns the page must not be the press that cancels the carry
		/// (<see cref="UITKControl.SLOT_MARKER_CLASS"/>).
		/// </para>
		/// </remarks>
		private void BuildPager()
		{
			pager = new VisualElement();
			pager.AddToClassList(PAGER_CLASS);
			pager.pickingMode = PickingMode.Ignore;

			VisualElement up = CreatePagerButton("▲", +1);
			pageLabel = new Label();
			pageLabel.AddToClassList(PAGER_LABEL_CLASS);
			pageLabel.pickingMode = PickingMode.Ignore;
			VisualElement down = CreatePagerButton("▼", -1);

			pager.Add(up);
			pager.Add(pageLabel);
			pager.Add(down);
			list.Add(pager);
		}

		/// <summary>Creates one page button that turns the page by <paramref name="delta"/> when pressed.</summary>
		private VisualElement CreatePagerButton(string glyph, int delta)
		{
			Label button = new Label(glyph);
			button.AddToClassList(PAGER_BUTTON_CLASS);
			button.AddToClassList(SLOT_MARKER_CLASS);
			button.RegisterCallback<PointerDownEvent>(evt =>
			{
				if (evt.button == 0)
				{
					TurnPage(delta);
				}
			});
			return button;
		}

		/// <summary>
		/// Shows the rows the layout calls for and the page controls a paged bar needs.
		/// </summary>
		/// <remarks>
		/// Display, not visibility, for the rows a paged bar is not showing: a hidden row would still
		/// take its height, and the point of paging is that the strip is one row tall. Nothing in a
		/// hidden row is lost — the slots, their icons and their cooldowns are all still kept up to
		/// date, so a page that comes back is already current.
		/// </remarks>
		private void ApplyLayout()
		{
			bool paged = IsPaged;

			for (int bar = 0; bar < rows.Count; ++bar)
			{
				rows[bar].style.display = !paged || bar == shownPage ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (pager != null)
			{
				pager.style.display = paged ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (pageLabel != null)
			{
				pageLabel.text = (shownPage + 1).ToString();
			}
		}

		/// <summary>True when the bars are shown one at a time.</summary>
		private static bool IsPaged =>
			HotkeyBarLayout.BarCount > 1 &&
			ClientHotbarSettings.Layout == ClientHotbarSettings.HotbarLayout.Paged;

		/// <summary>
		/// Moves a paged bar's resting page, and shows it unless a held modifier is showing another.
		/// </summary>
		/// <param name="delta">+1 for the next bar up, -1 for the one below; wraps at either end.</param>
		private void TurnPage(int delta)
		{
			if (!IsPaged)
			{
				return;
			}

			restingPage = HotkeyBarLayout.StepPage(restingPage, delta);
			ClientHotbarSettings.SetPage(restingPage);
			ShowPage(HotkeyBarLayout.ResolveShownPage(HotkeyKeyMap.FirstHeldModifierBar(), restingPage));
		}

		/// <summary>Shows one bar of a paged hotbar, if it is not the one already showing.</summary>
		private void ShowPage(int page)
		{
			if (page == shownPage)
			{
				return;
			}

			shownPage = page;
			ApplyLayout();
		}

		/// <summary>Re-lays the bar out when the player switches between stacked and paged.</summary>
		private void ClientHotbarSettings_OnChanged()
		{
			shownPage = restingPage;
			ApplyLayout();
		}

		/// <summary>Redraws the key hints on the next tick after a rebind.</summary>
		private void PlayerInputController_OnBindingsChanged() => labelsDirty = true;

		/// <summary>Rewrites every slot's key hint from the live bindings.</summary>
		private void RefreshLabels()
		{
			labelsDirty = false;

			for (int i = 0; i < slots.Count; ++i)
			{
				HotkeySlot slot = slots[i];
				if (slot.Label != null)
				{
					slot.Label.text = HotkeyKeyMap.SlotLabel(HotkeyBarLayout.BarOf(slot.Index), HotkeyBarLayout.PositionOf(slot.Index));
				}
			}
		}

		/// <summary>
		/// Creates the visual elements and registers interaction callbacks for a single slot.
		/// </summary>
		/// <param name="index">The hotkey slot index.</param>
		/// <returns>The populated <see cref="HotkeySlot"/>.</returns>
		private HotkeySlot CreateSlot(int index)
		{
			VisualElement slotRoot = new VisualElement();
			slotRoot.AddToClassList(SLOT_CLASS);

			/* Marked as a slot — and marked here precisely because this bar does NOT carry
			 * `fish-slot`. A carried ability is bound by pressing a hotkey slot, so a press here must
			 * not cancel the drag; see UITKControl.OnRootPointerDownCancelDrag and
			 * UITKControl.SLOT_MARKER_CLASS for why the marker is its own class. */
			slotRoot.AddToClassList(SLOT_MARKER_CLASS);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(ICON_CLASS);
			slotRoot.Add(icon);

			VisualElement cooldown = new VisualElement();
			cooldown.AddToClassList(COOLDOWN_CLASS);
			cooldown.style.height = Length.Percent(0.0f);
			slotRoot.Add(cooldown);

			Label label = new Label(HotkeyKeyMap.SlotLabel(HotkeyBarLayout.BarOf(index), HotkeyBarLayout.PositionOf(index)));
			label.AddToClassList(LABEL_CLASS);
			slotRoot.Add(label);

			HotkeySlot slot = new HotkeySlot
			{
				Root = slotRoot,
				Icon = icon,
				Cooldown = cooldown,
				Label = label,
				Index = index,
			};

			/* A press, and only a press. There is no PointerUpEvent on a hotkey slot, because a
			 * release moves nothing anywhere in this game — see the note above Notify in
			 * UITKSlotPanelBase. Binding an ability or item to the bar is the PRESS's job, the same
			 * press that puts an item down in a slot panel, so one gesture binds and one gesture
			 * does not: carrying something over the bar and letting go leaves it on the cursor. */
			slotRoot.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(evt, slot));
			slotRoot.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(slot));
			slotRoot.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave());

			return slot;
		}

		/// <summary>
		/// Handles a broadcast that assigns or clears a single hotkey slot.
		/// </summary>
		/// <param name="msg">The broadcast message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientHotkeySetBroadcastReceived(HotkeySetBroadcast msg, Channel channel)
		{
			EnsureBindings();

			int index = msg.HotkeyData.Slot;
			if (index < 0 || index >= bindings.Length)
			{
				return;
			}

			if (msg.HotkeyData.Type == 0)
			{
				bindings[index].Type = ReferenceButtonType.None;
				bindings[index].ReferenceID = ReferenceButton.NULL_REFERENCE_ID;
			}
			else
			{
				bindings[index].Type = (ReferenceButtonType)msg.HotkeyData.Type;
				bindings[index].ReferenceID = msg.HotkeyData.ReferenceID;
			}

			bindingsDirty = true;
		}

		/// <summary>
		/// Handles a broadcast that assigns or clears multiple hotkey slots.
		/// </summary>
		/// <param name="msg">The broadcast message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientHotkeySetMultipleBroadcastReceived(HotkeySetMultipleBroadcast msg, Channel channel)
		{
			if (msg.Hotkeys == null)
			{
				return;
			}

			foreach (HotkeySetBroadcast subMsg in msg.Hotkeys)
			{
				OnClientHotkeySetBroadcastReceived(subMsg, channel);
			}
		}

		/// <summary>
		/// Per-frame hook. Consumes a pending refresh, animates cooldown sweeps and polls input.
		/// </summary>
		/// <remarks>
		/// This used to be <c>private void Update()</c>. <see cref="UITKControl"/> declares its own
		/// <c>Update</c> and Unity binds only the MOST DERIVED one, so declaring a second in a
		/// subclass silently killed <c>PollLoseFocus</c> and <c>OnTick</c> for this panel — the
		/// exact failure the base class's own comment warns about.
		/// </remarks>
		protected override void OnTick()
		{
			if (bindingsDirty)
			{
				RefreshAllSlots();
			}

			if (labelsDirty)
			{
				RefreshLabels();
			}

			/* A lift whose drag was put down anywhere but on this bar — the root's cancel press,
			 * Escape, a panel closing — is forgotten here, so it cannot be resolved by a later drag. */
			if (liftedSlotIndex >= 0 &&
				(!UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) ||
				 !dragObject.Visible ||
				 dragObject.Type != ReferenceButtonType.Hotkey))
			{
				liftedSlotIndex = -1;
			}

			UpdateCooldownSweeps();
			UpdateInput();
		}

		/// <summary>
		/// Validates every binding and repaints any slot whose icon actually changed.
		/// </summary>
		/// <remarks>
		/// A binding whose referenced item or ability no longer exists is dropped here rather than
		/// left pointing at nothing — including on the login path, where the server replays the
		/// stored bar before the client necessarily has the referenced item.
		/// </remarks>
		private void RefreshAllSlots()
		{
			bindingsDirty = false;

			EnsureBindings();

			if (Character == null)
			{
				for (int i = 0; i < slots.Count; ++i)
				{
					ApplySlotSprite(slots[i], null, occupied: false);
				}
				return;
			}

			for (int i = 0; i < slots.Count && i < bindings.Length; ++i)
			{
				HotkeySlot slot = slots[i];

				if (!TryResolveSlotSprite(i, out Sprite sprite))
				{
					// The referenced item or ability is gone; drop the binding rather than
					// leaving a slot that would activate nothing.
					ClearBinding(i, broadcast: false);
					ApplySlotSprite(slot, null, occupied: false);
					ApplyCooldownFraction(slot, 0.0f);
					continue;
				}

				ApplySlotSprite(slot, sprite, bindings[i].Type != ReferenceButtonType.None);
			}
		}

		/// <summary>
		/// Resolves the icon for a binding, reporting whether the binding is still valid.
		/// </summary>
		/// <param name="index">The hotkey slot index.</param>
		/// <param name="sprite">The resolved icon, or null when the slot is empty.</param>
		/// <returns>False when the binding references something that no longer exists.</returns>
		private bool TryResolveSlotSprite(int index, out Sprite sprite)
		{
			sprite = null;

			ref HotkeyBinding binding = ref bindings[index];
			switch (binding.Type)
			{
				case ReferenceButtonType.None:
					return true;
				case ReferenceButtonType.Inventory:
					if (!Character.TryGet(out IInventoryController inventoryController) ||
						!inventoryController.TryGetItem((int)binding.ReferenceID, out Item inventoryItem))
					{
						return false;
					}
					sprite = inventoryItem.Template != null ? inventoryItem.Template.Icon : null;
					return true;
				case ReferenceButtonType.Equipment:
					if (!Character.TryGet(out IEquipmentController equipmentController) ||
						!equipmentController.TryGetItem((int)binding.ReferenceID, out Item equippedItem))
					{
						return false;
					}
					sprite = equippedItem.Template != null ? equippedItem.Template.Icon : null;
					return true;
				case ReferenceButtonType.Ability:
					if (!Character.TryGet(out IAbilityController abilityController) ||
						!abilityController.KnownAbilities.TryGetValue(binding.ReferenceID, out Ability ability))
					{
						return false;
					}
					sprite = ability.Template != null ? ability.Template.Icon : null;
					return true;
				default:
					// Bank and anything else has no meaning on the bar.
					return false;
			}
		}

		/// <summary>
		/// Polls the Input System and drives press / release for every hotkey slot.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two bugs lived here. First, the poll tested <c>isPressed</c> — a LEVEL, not an edge —
		/// and activated on every frame the key was down, so holding a hotkey re-queued the
		/// ability at frame rate. Second, nothing on the client ever called
		/// <see cref="IAbilityController.Release"/> and the bar hard-coded <c>isHeld: true</c>, so
		/// a charged ability could never fire (the controller waits for the held flag to clear,
		/// then cancels it at the hold cap) and a channel could never be stopped early.
		/// </para>
		/// <para>
		/// <c>isHeld</c> now comes from <see cref="IAbilityController.RequiresHeld"/> — the method
		/// the controller provides for exactly this and which the AI path already used — and the
		/// release edge calls <c>Release()</c>.
		/// </para>
		/// <para>
		/// Keys are sampled per POSITION, not per slot: one key drives the same position on every
		/// bar, and the bar modifiers held at the press decide which bar it lands on
		/// (<see cref="HotkeyBarLayout.ResolvePressBar"/>). <see cref="pressRouter"/> remembers that
		/// bar until the key comes up, so the release always reaches the slot the press started.
		/// </para>
		/// </remarks>
		private void UpdateInput()
		{
			if (slots.Count < 1)
			{
				return;
			}

			bool typing = UIManager.InputControlHasFocus();
			bool inputBlocked = Character == null ||
				PlayerInputController.MouseMode ||
				typing;

			/* The modifiers and the page keys are read in mouse mode as well. A paged bar follows a
			 * held modifier so the player can see the bar they are about to use — and that includes
			 * the player who has an ability on the cursor and wants to put it on another bar. Only
			 * typing silences them, for the reason it silences every other key: Ctrl held to copy
			 * text in chat is not a request to look at the second bar. */
			int heldModifierBar = typing ? -1 : HotkeyKeyMap.FirstHeldModifierBar();
			bool paged = IsPaged;

			if (paged)
			{
				bool up = !typing && HotkeyKeyMap.IsPageUpPressed();
				bool down = !typing && HotkeyKeyMap.IsPageDownPressed();
				if (up && !pageUpHeld)
				{
					TurnPage(+1);
				}
				if (down && !pageDownHeld)
				{
					TurnPage(-1);
				}
				pageUpHeld = up;
				pageDownHeld = down;

				ShowPage(HotkeyBarLayout.ResolveShownPage(heldModifierBar, restingPage));
			}

			int targetBar = HotkeyBarLayout.ResolvePressBar(heldModifierBar, paged, restingPage);
			int positions = HotkeyBarLayout.SlotsPerBar;

			for (int position = 0; position < positions; ++position)
			{
				bool pressed = !inputBlocked && HotkeyKeyMap.IsPositionPressed(position);

				pressRouter.Step(position, pressed, targetBar, out int pressedBar, out int releasedBar);

				if (releasedBar >= 0 && TryGetSlot(releasedBar, position, out HotkeySlot released))
				{
					ReleaseSlot(released);
				}
				if (pressedBar >= 0 && TryGetSlot(pressedBar, position, out HotkeySlot slot))
				{
					ActivateSlot(slot);
				}
			}
		}

		/// <summary>The slot drawn at a position on a bar, if the bar has been built.</summary>
		private bool TryGetSlot(int bar, int position, out HotkeySlot slot)
		{
			int index = HotkeyBarLayout.SlotIndex(bar, position);
			slot = index >= 0 && index < slots.Count ? slots[index] : null;
			return slot != null;
		}

		/// <summary>
		/// Handles pointer-down on a slot: assigns a dragged reference (left), activates (left, no drag),
		/// or removes the assignment (right).
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="slot">The slot that was pressed.</param>
		private void OnSlotPointerDown(PointerDownEvent evt, HotkeySlot slot)
		{
			if (evt.button == 0)
			{
				HandleSlotLeftClick(slot);
			}
			else if (evt.button == 1)
			{
				HandleSlotRightClick(slot);
			}
		}

		/// <summary>
		/// Assigns a dragged reference to the slot (broadcasting the change) or activates it.
		/// </summary>
		/// <param name="slot">The slot that was left-clicked.</param>
		private void HandleSlotLeftClick(HotkeySlot slot)
		{
			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject) && dragObject.Visible)
			{
				if (TryResolveDroppedBinding(dragObject, out ReferenceButtonType type, out long referenceID))
				{
					EnsureBindings();
					bindings[slot.Index].Type = type;
					bindings[slot.Index].ReferenceID = referenceID;

					/* Applied whether or not the drag carried art. This used to skip the write
					 * on a null sprite and defer to the refresh sweep, which resolved the same
					 * null and hid the icon — so binding an item with no icon left a slot that
					 * looked empty while being bound, and clicking it activated something the
					 * player could not see. */
					ApplySlotSprite(slot, dragObject.IconSprite, occupied: true);

					Client.Broadcast(new HotkeySetBroadcast()
					{
						HotkeyData = new HotkeyData()
						{
							Type = (byte)type,
							Slot = slot.Index,
							ReferenceID = referenceID,
						}
					}, Channel.Reliable);
				}

				liftedSlotIndex = -1;
				dragObject.Clear();
			}
			else
			{
				/* A click activates and immediately releases: there is no pointer-up path that
				 * could deliver the release, and leaving a charged ability holding forever is
				 * what the hold cap exists to clean up — badly. */
				ActivateSlot(slot);
				ReleaseSlot(slot);
			}
		}

		/// <summary>
		/// Works out what a drag dropped on a slot should bind it to, or refuses it.
		/// </summary>
		/// <param name="dragObject">The drag on the cursor.</param>
		/// <param name="type">The binding's type.</param>
		/// <param name="referenceID">The binding's reference.</param>
		/// <returns>True when the drag may be bound.</returns>
		/// <remarks>
		/// <list type="bullet">
		/// <item>Bank is refused: the server never accepts a bank binding, and a shortcut to a slot
		/// the player is not carrying is not a shortcut to anything.</item>
		/// <item>A split drag is refused, per the drag object's own contract: a hotkey binds a slot,
		/// not a quantity, and binding the whole stack under a badge that promised part of it
		/// silently discarded the number the player typed.</item>
		/// <item>A shortcut lifted off this bar resolves back to what was lifted — and only while the
		/// drag is the one that lifted it, so a stale memory of an earlier lift cannot be bound by
		/// a later, unrelated drag.</item>
		/// </list>
		/// </remarks>
		private bool TryResolveDroppedBinding(UITKDragObject dragObject, out ReferenceButtonType type, out long referenceID)
		{
			type = ReferenceButtonType.None;
			referenceID = ReferenceButton.NULL_REFERENCE_ID;

			if (dragObject.SplitAmount != 0)
			{
				return false;
			}

			switch (dragObject.Type)
			{
				case ReferenceButtonType.Inventory:
				case ReferenceButtonType.Equipment:
				case ReferenceButtonType.Ability:
					type = dragObject.Type;
					referenceID = dragObject.ReferenceID;
					return true;

				case ReferenceButtonType.Hotkey:
					if (liftedSlotIndex < 0 ||
						dragObject.ReferenceID != liftedSlotIndex ||
						liftedBinding.Type == ReferenceButtonType.None ||
						liftedBinding.ReferenceID == ReferenceButton.NULL_REFERENCE_ID)
					{
						return false;
					}
					type = liftedBinding.Type;
					referenceID = liftedBinding.ReferenceID;
					return true;

				default:
					return false;
			}
		}

		/// <summary>
		/// Removes the slot's assignment, seeds the drag overlay, and broadcasts the change.
		/// </summary>
		/// <param name="slot">The slot that was right-clicked.</param>
		/// <remarks>
		/// The cursor carries a <see cref="ReferenceButtonType.Hotkey"/> reference to THIS slot, not
		/// the item or ability the slot pointed at: only another hotkey slot may take it, and it
		/// resolves through <see cref="liftedBinding"/>. See the field for why.
		/// </remarks>
		private void HandleSlotRightClick(HotkeySlot slot)
		{
			EnsureBindings();

			if (bindings[slot.Index].ReferenceID == ReferenceButton.NULL_REFERENCE_ID)
			{
				return;
			}

			if (UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				Sprite sprite = slot.AppliedSprite;
				liftedBinding = bindings[slot.Index];
				liftedSlotIndex = slot.Index;
				dragObject.SetReference(sprite, slot.Index, ReferenceButtonType.Hotkey);

				ClearBinding(slot.Index, broadcast: true);
				ApplySlotSprite(slot, null, occupied: false);
				ApplyCooldownFraction(slot, 0.0f);
			}
		}

		/// <summary>
		/// Empties a binding, optionally telling the server about it.
		/// </summary>
		/// <param name="index">The hotkey slot index.</param>
		/// <param name="broadcast">True to send the clear to the server.</param>
		private void ClearBinding(int index, bool broadcast)
		{
			EnsureBindings();

			bindings[index].Type = ReferenceButtonType.None;
			bindings[index].ReferenceID = ReferenceButton.NULL_REFERENCE_ID;

			if (broadcast && Client != null)
			{
				Client.Broadcast(new HotkeySetBroadcast()
				{
					HotkeyData = new HotkeyData()
					{
						Type = 0,
						Slot = index,
						ReferenceID = HotkeyData.UnsetReferenceID,
					}
				}, Channel.Reliable);
			}
		}

		/// <summary>
		/// Shows the tooltip for the slot's assigned reference when hovered.
		/// </summary>
		/// <param name="slot">The hovered slot.</param>
		private void OnSlotPointerEnter(HotkeySlot slot)
		{
			EnsureBindings();

			if (Character == null || bindings[slot.Index].ReferenceID < 0)
			{
				return;
			}

			if (!UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				return;
			}

			long referenceID = bindings[slot.Index].ReferenceID;
			switch (bindings[slot.Index].Type)
			{
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem((int)referenceID, out Item inventoryItem))
					{
						tooltip.Open(inventoryItem, slot.Root);
					}
					break;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController) &&
						equipmentController.TryGetItem((int)referenceID, out Item equippedItem))
					{
						tooltip.Open(equippedItem, slot.Root);
					}
					break;
				case ReferenceButtonType.Ability:
					if (Character.TryGet(out IAbilityController abilityController) &&
						abilityController.KnownAbilities.TryGetValue(referenceID, out Ability ability))
					{
						tooltip.Open(ability, slot.Root);
					}
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Hides the tooltip when the pointer leaves a slot.
		/// </summary>
		private void OnSlotPointerLeave()
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Hide();
			}
		}

		/// <summary>
		/// Activates the slot's assigned action based on its reference type.
		/// </summary>
		/// <param name="slot">The slot to activate.</param>
		private void ActivateSlot(HotkeySlot slot)
		{
			EnsureBindings();

			if (Character == null)
			{
				return;
			}

			long referenceID = bindings[slot.Index].ReferenceID;
			switch (bindings[slot.Index].Type)
			{
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController))
					{
						inventoryController.Activate((int)referenceID);
					}
					break;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController))
					{
						equipmentController.Activate((int)referenceID);
					}
					break;
				case ReferenceButtonType.Ability:
					if (!UIManager.ControlHasFocus() &&
						Character.TryGet(out IAbilityController abilityController))
					{
						/* The controller knows whether this ability is charged or channeled;
						 * the bar does not, and hard-coding true meant a charged ability sat at
						 * full charge until the hold cap cancelled it. */
						bool isHeld = abilityController.RequiresHeld(referenceID);
						abilityController.Activate(referenceID, isHeld);
						slot.AwaitingRelease = isHeld;
					}
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Releases a held activation started from this slot.
		/// </summary>
		/// <param name="slot">The slot whose input was released.</param>
		/// <remarks>
		/// For a charged ability this is what fires it; for a channel it is what stops it early.
		/// Guarded by <see cref="HotkeySlot.AwaitingRelease"/> so releasing a slot that started a
		/// non-held ability cannot clear the held flag of a DIFFERENT ability that has since
		/// started casting.
		/// </remarks>
		private void ReleaseSlot(HotkeySlot slot)
		{
			if (!slot.AwaitingRelease)
			{
				return;
			}

			slot.AwaitingRelease = false;

			if (Character != null &&
				Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.Release();
			}
		}

		/// <summary>
		/// Writes a slot's icon sprite, skipping the write when nothing has changed.
		/// </summary>
		/// <param name="slot">The slot to update.</param>
		/// <param name="sprite">The sprite to display, or null if there is none to show.</param>
		/// <param name="occupied">
		/// True when the slot holds a binding. A null <paramref name="sprite"/> means two
		/// different things — an empty slot, and a slot bound to something whose template has no
		/// icon — and only the first should draw nothing. Without this the bar was the one item
		/// surface where binding an art-less item produced a slot that looked unbound, so the
		/// player would bind it again and again.
		/// </param>
		private void ApplySlotSprite(HotkeySlot slot, Sprite sprite, bool occupied)
		{
			if (ReferenceEquals(slot.AppliedSprite, sprite) &&
				slot.AppliedOccupied == occupied)
			{
				return;
			}

			slot.AppliedSprite = sprite;
			slot.AppliedOccupied = occupied;

			if (slot.Icon == null)
			{
				return;
			}

			if (!occupied)
			{
				UITKItemIcon.Clear(slot.Icon);
				slot.Icon.style.display = DisplayStyle.None;
				return;
			}

			// Occupied: the icon if there is one, the placeholder if there is not.
			UITKItemIcon.Apply(slot.Icon, sprite);
			slot.Icon.style.display = DisplayStyle.Flex;
		}

		/// <summary>
		/// Writes a slot's cooldown sweep height, skipping the write when it has not changed.
		/// </summary>
		/// <param name="slot">The slot to update.</param>
		/// <param name="fraction">The remaining cooldown fraction (0-1).</param>
		private void ApplyCooldownFraction(HotkeySlot slot, float fraction)
		{
			fraction = Mathf.Clamp01(fraction);

			// Quantised to whole percent: the sweep is 48px tall, so anything finer is not a
			// visible change and would repaint the element every frame for nothing.
			if (Mathf.Abs(slot.AppliedCooldownFraction - fraction) < 0.005f)
			{
				return;
			}

			slot.AppliedCooldownFraction = fraction;

			if (slot.Cooldown != null)
			{
				slot.Cooldown.style.height = Length.Percent(fraction * 100.0f);
			}
		}

		/// <summary>
		/// Resolves the cooldown key a binding is tracked under, if it has one.
		/// </summary>
		/// <param name="index">The hotkey slot index.</param>
		/// <param name="key">The cooldown key.</param>
		/// <returns>True when this binding can be on cooldown.</returns>
		/// <remarks>
		/// Slot type matters and the old code ignored it: an ability's cooldown is keyed by the
		/// ability's INSTANCE ID while an inventory binding's reference is a SLOT INDEX, so
		/// matching a raw ID against every slot let a cooldown on ability 3 paint the sweep over
		/// whatever was sitting in inventory slot 3. Consumable cooldowns are keyed by the
		/// consumable's TEMPLATE ID, which is what an item binding has to resolve to.
		/// </remarks>
		private bool TryGetSlotCooldownKey(int index, out long key)
		{
			key = 0;

			if (Character == null)
			{
				return false;
			}

			switch (bindings[index].Type)
			{
				case ReferenceButtonType.Ability:
					key = bindings[index].ReferenceID;
					return key != ReferenceButton.NULL_REFERENCE_ID;
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem((int)bindings[index].ReferenceID, out Item inventoryItem) &&
						inventoryItem.Template != null)
					{
						key = inventoryItem.Template.ID;
						return true;
					}
					return false;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController) &&
						equipmentController.TryGetItem((int)bindings[index].ReferenceID, out Item equippedItem) &&
						equippedItem.Template != null)
					{
						key = equippedItem.Template.ID;
						return true;
					}
					return false;
				default:
					return false;
			}
		}

		/// <summary>
		/// Flags a cooldown sweep refresh for whichever slot references the cooled-down entry.
		/// </summary>
		/// <param name="referenceID">The reference ID on cooldown.</param>
		/// <param name="cooldown">The cooldown instance.</param>
		private void CooldownController_OnAddOrUpdateCooldown(long referenceID, CooldownInstance cooldown)
		{
			anyCooldownActive = true;
			UpdateCooldownSweeps();
		}

		/// <summary>
		/// Clears the cooldown sweep for whichever slot references the finished cooldown.
		/// </summary>
		/// <param name="referenceID">The reference ID whose cooldown ended.</param>
		private void CooldownController_OnRemoveCooldown(long referenceID)
		{
			UpdateCooldownSweeps();
		}

		/// <summary>
		/// Recomputes every slot's cooldown sweep from the live cooldown controller.
		/// </summary>
		/// <remarks>
		/// Driven per frame rather than from the cooldown events alone. <c>OnUpdateCooldown</c>
		/// only fires when a cooldown is RE-ADDED, so the previous event-only sweep was binary —
		/// it snapped to the starting fraction and stayed there until removal, never animating
		/// down. The loop is skipped entirely once nothing is on cooldown.
		/// </remarks>
		private void UpdateCooldownSweeps()
		{
			if (!anyCooldownActive || Character == null || slots.Count < 1)
			{
				return;
			}

			if (!Character.TryGet(out ICooldownController cooldownController))
			{
				anyCooldownActive = false;
				return;
			}

			uint currentTick = GetCurrentCooldownTick();
			bool stillActive = false;

			for (int i = 0; i < slots.Count && i < bindings.Length; ++i)
			{
				float fraction = 0.0f;

				if (TryGetSlotCooldownKey(i, out long key) &&
					cooldownController.TryGetCooldown(key, currentTick, out float remaining) &&
					remaining > 0.0f)
				{
					float total = ResolveCooldownTotal(cooldownController, key, remaining);
					fraction = total > 0.0f ? remaining / total : 0.0f;
					stillActive = true;
				}

				ApplyCooldownFraction(slots[i], fraction);
			}

			anyCooldownActive = stillActive;
		}

		/// <summary>
		/// Resolves the total duration a cooldown started from, falling back to the remaining
		/// time when the controller cannot report it.
		/// </summary>
		/// <param name="cooldownController">The character's cooldown controller.</param>
		/// <param name="key">The cooldown key.</param>
		/// <param name="remaining">The remaining seconds.</param>
		/// <returns>The total cooldown duration in seconds.</returns>
		private static float ResolveCooldownTotal(ICooldownController cooldownController, long key, float remaining)
		{
			if (cooldownController.TryGetCooldownInstance(key, out CooldownInstance instance) &&
				instance.TotalTime > 0.0f)
			{
				return instance.TotalTime;
			}
			return remaining;
		}

		/// <summary>
		/// Resolves the authoritative cooldown tick for remaining-time calculations.
		/// </summary>
		/// <returns>The authoritative tick.</returns>
		private uint GetCurrentCooldownTick()
		{
			uint localTick = InstanceFinder.TimeManager != null ? InstanceFinder.TimeManager.LocalTick : 0u;
			if (Character != null && Character.TryGet(out ICooldownController cooldownController))
			{
				return cooldownController.ResolveAuthoritativeTick(localTick);
			}
			return localTick;
		}
	}
}
