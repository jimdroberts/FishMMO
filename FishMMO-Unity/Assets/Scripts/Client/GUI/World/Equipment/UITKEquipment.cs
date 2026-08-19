using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the equipment panel.
	/// Binds to <c>UIEquipment.uxml</c> / <c>UIEquipment.uss</c> and replicates all
	/// behaviour of the legacy UGUI <see cref="UIEquipment"/> class without modifying it.
	/// </summary>
	public class UITKEquipment : UITKCharacterControl
	{
		// ── UXML element names ────────────────────────────────────────────────

		/// <summary>Name of the attribute list ScrollView element in the UXML.</summary>
		private const string ATTR_LIST_NAME     = "attribute-list";
		/// <summary>Name of the preview render texture element in the UXML.</summary>
		private const string PREVIEW_RT_NAME    = "preview-rt";
		/// <summary>Name of the close button element in the UXML.</summary>
		private const string CLOSE_BTN_NAME     = "close-button";
		/// <summary>Name of the gear tab button element in the UXML.</summary>
		private const string TAB_GEAR_NAME      = "tab-gear";
		/// <summary>Name of the stats tab button element in the UXML.</summary>
		private const string TAB_STATS_NAME     = "tab-stats";
		/// <summary>Name of the sets tab button element in the UXML.</summary>
		private const string TAB_SETS_NAME      = "tab-sets";
		/// <summary>Name of the panel body element in the UXML.</summary>
		private const string PANEL_BODY_NAME    = "panel-body";
		/// <summary>Name of the panel footer element in the UXML.</summary>
		private const string PANEL_FOOTER_NAME  = "panel-footer";
		/// <summary>Name of the gear score label element in the UXML.</summary>
		private const string GEAR_SCORE_NAME    = "gear-score";
		/// <summary>Name of the HP stat label element in the UXML.</summary>
		private const string STAT_HP_NAME       = "stat-hp";
		/// <summary>Name of the MP stat label element in the UXML.</summary>
		private const string STAT_MP_NAME       = "stat-mp";
		/// <summary>Name of the stamina stat label element in the UXML.</summary>
		private const string STAT_STAM_NAME     = "stat-stam";

		/// <summary>
		/// Maps each <see cref="ItemSlot"/> value to its UXML element name suffix.
		/// Only slots that exist in both the enum and the UXML are listed here.
		/// </summary>
		private static readonly string[] SlotElementNames = new[]
		{
			"slot-head",      // ItemSlot.Head      = 0
			"slot-chest",     // ItemSlot.Chest     = 1
			"slot-shoulders", // ItemSlot.Shoulders = 2
			"slot-hands",     // ItemSlot.Hands     = 3
			"slot-legs",      // ItemSlot.Legs      = 4
			"slot-feet",      // ItemSlot.Feet      = 5
			"slot-back",      // ItemSlot.Back      = 6
			"slot-mainhand",  // ItemSlot.Primary   = 7
			"slot-offhand",   // ItemSlot.Secondary = 8
			"slot-accessory", // ItemSlot.Accessory = 9
		};

		// ── USS class names ───────────────────────────────────────────────────

		/// <summary>USS class for hiding equipment elements.</summary>
		private const string CSS_HIDDEN         = "eq-hidden";
		/// <summary>USS class for an active tab button.</summary>
		private const string CSS_TAB_ACTIVE     = "fish-tab--active";
		/// <summary>USS class for an attribute category header.</summary>
		private const string CSS_ATTR_CATEGORY  = "fish-attr-category";
		/// <summary>USS class for an attribute row.</summary>
		private const string CSS_ATTR_ROW       = "fish-attr-row";
		/// <summary>USS class for an attribute row name label.</summary>
		private const string CSS_ATTR_NAME      = "fish-attr-row__name";
		/// <summary>USS class for an attribute row value label.</summary>
		private const string CSS_ATTR_VALUE     = "fish-attr-row__value";
		/// <summary>USS class for a resource attribute value label.</summary>
		private const string CSS_ATTR_RESOURCE  = "fish-attr-row__value--resource";
		/// <summary>USS class for a percentage attribute value label.</summary>
		private const string CSS_ATTR_PERCENT   = "fish-attr-row__value--percent";

		// ── Per-slot view data ────────────────────────────────────────────────

		/// <summary>Runtime view data for a single equipment slot element.</summary>
		private struct SlotView
		{
			/// <summary>Root VisualElement of the slot (e.g. "slot-head").</summary>
			public VisualElement Root;
			/// <summary>Icon element (fish-slot__icon).</summary>
			public VisualElement Icon;
			/// <summary>Amount label (fish-slot__amount).</summary>
			public Label Amount;
			/// <summary>Lock overlay element (fish-slot__lock).</summary>
			public VisualElement Lock;
		}

		// ── Private state ─────────────────────────────────────────────────────

		/// <summary>Indexed by (int)ItemSlot; null where the UXML slot is absent.</summary>
		private SlotView[] slotViews;

		/// <summary>Live attribute value labels keyed by attribute template ID.</summary>
		private readonly Dictionary<int, Label> attributeValueLabels = new Dictionary<int, Label>();

		/// <summary>Category header elements created at runtime.</summary>
		private readonly List<VisualElement> attributeCategoryElements = new List<VisualElement>();

		/// <summary>Attribute row elements created at runtime.</summary>
		private readonly List<VisualElement> attributeRowElements = new List<VisualElement>();

		/// <summary>The ScrollView that contains the attribute rows.</summary>
		private ScrollView attributeList;
		/// <summary>Root element of the gear tab panel body.</summary>
		private VisualElement panelBody;
		/// <summary>Root element of the stats tab panel footer.</summary>
		private VisualElement panelFooter;
		/// <summary>Character preview render texture element.</summary>
		private VisualElement previewRt;
		/// <summary>Label displaying the gear score.</summary>
		private Label gearScoreLabel;
		/// <summary>Label displaying the HP stat value.</summary>
		private Label statHpLabel;
		/// <summary>Label displaying the MP stat value.</summary>
		private Label statMpLabel;
		/// <summary>Label displaying the stamina stat value.</summary>
		private Label statStamLabel;

		/// <summary>Camera used for the 3D character preview viewport.</summary>
		private Camera equipmentViewCamera;

		/// <summary>Currently selected tab name; defaults to GEAR.</summary>
		private string activeTab = TAB_GEAR_NAME;

		// ── UITKControl lifecycle ─────────────────────────────────────────────

		/// <summary>
		/// Queries all named elements from the visual tree and wires up button callbacks.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			// Body / footer panels used for tab switching
			panelBody   = root.Q(PANEL_BODY_NAME);
			panelFooter = root.Q(PANEL_FOOTER_NAME);

			// Attribute scroll list
			attributeList = root.Q<ScrollView>(ATTR_LIST_NAME);

			// Status-bar labels
			gearScoreLabel = root.Q<Label>(GEAR_SCORE_NAME);
			statHpLabel    = root.Q<Label>(STAT_HP_NAME);
			statMpLabel    = root.Q<Label>(STAT_MP_NAME);
			statStamLabel  = root.Q<Label>(STAT_STAM_NAME);

			// Character-preview render texture element
			previewRt = root.Q(PREVIEW_RT_NAME);

			// Close button
			Button closeBtn = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeBtn != null)
			{
				closeBtn.clicked += Hide;
			}

			// Tab buttons
			WireTab(root.Q<Button>(TAB_GEAR_NAME),  TAB_GEAR_NAME,  root);
			WireTab(root.Q<Button>(TAB_STATS_NAME), TAB_STATS_NAME, root);
			WireTab(root.Q<Button>(TAB_SETS_NAME),  TAB_SETS_NAME,  root);

			// Equipment slots
			int slotCount = SlotElementNames.Length;
			slotViews = new SlotView[slotCount];
			for (int i = 0; i < slotCount; ++i)
			{
				VisualElement slotRoot = root.Q(SlotElementNames[i]);
				if (slotRoot == null)
				{
					continue;
				}

				SlotView view;
				view.Root   = slotRoot;
				view.Icon   = slotRoot.Q(className: "fish-slot__icon");
				view.Amount = slotRoot.Q<Label>(className: "fish-slot__amount");
				view.Lock   = slotRoot.Q(className: "fish-slot__lock");
				slotViews[i] = view;

				int slotIndex = i;
				slotRoot.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(evt, slotIndex));
				slotRoot.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(slotIndex));
				slotRoot.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave());
			}

			// Apply initial tab state
			ApplyTabState(root);
		}

		/// <summary>
		/// Cleans up the camera reference and runtime-created attribute elements.
		/// </summary>
		public override void OnDestroying()
		{
			equipmentViewCamera = null;
			DestroyAttributeElements();
			base.OnDestroying();
		}

		// ── Visibility overrides (camera sync) ───────────────────────────────

		/// <summary>
		/// Shows the equipment panel and enables the equipment-view camera if assigned.
		/// </summary>
		public override void Show()
		{
			base.Show();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(true);
			}
		}

		/// <summary>
		/// Hides the equipment panel and disables the equipment-view camera if assigned.
		/// </summary>
		public override void Hide()
		{
			base.Hide();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(false);
			}
		}

		/// <summary>
		/// Hides the equipment panel (overrideIsAlwaysOpen variant).
		/// </summary>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (!Visible && equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(false);
			}
		}

		/// <summary>
		/// Toggles the panel and syncs the camera accordingly.
		/// </summary>
		public override void ToggleVisibility()
		{
			base.ToggleVisibility();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(Visible);
			}
		}

		// ── Character control ─────────────────────────────────────────────────

		/// <summary>
		/// Unsubscribes from equipment slot events before replacing the character.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated    -= OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged -= OnEquipmentSlotLockChanged;
			}
		}

		/// <summary>
		/// Subscribes to equipment slot events, refreshes all slot visuals, and builds
		/// the attribute row list for the newly set character.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			DestroyAttributeElements();
			ResetStatusBar();

			if (Character == null)
			{
				return;
			}

			// ── Equipment slots ───────────────────────────────────────────────
			if (Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated    -= OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged -= OnEquipmentSlotLockChanged;

				// The array itself is null until OnStarting builds it, and OnStarting does not
				// run until this panel's visual tree exists — which, for a panel that starts
				// hidden, is after world entry has already handed it a character. The existing
				// per-slot check guards the elements, not the array holding them.
				for (int i = 0; slotViews != null && i < slotViews.Length; ++i)
				{
					if (slotViews[i].Root == null)
					{
						continue;
					}
					RefreshSlot(equipmentController, i);
					SetSlotLocked(i, equipmentController.IsSlotLocked(i));
				}

				equipmentController.OnSlotUpdated    += OnEquipmentSlotUpdated;
				equipmentController.OnSlotLockChanged += OnEquipmentSlotLockChanged;
			}

			// ── Character attributes ──────────────────────────────────────────
			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				BuildAttributeRows(attributeController);
				UpdateStatusBar(attributeController);
			}
		}

		// ── Equipment slot callbacks ──────────────────────────────────────────

		/// <summary>
		/// Called when the lock state of an equipment slot changes.
		/// </summary>
		public void OnEquipmentSlotLockChanged(IItemContainer container, int slot, bool isLocked)
		{
			if (slot >= 0 && slot < slotViews.Length && slotViews[slot].Root != null)
			{
				SetSlotLocked(slot, isLocked);
			}
		}

		/// <summary>
		/// Called when an equipment slot's item changes.
		/// </summary>
		public void OnEquipmentSlotUpdated(IItemContainer container, Item item, int equipmentSlot)
		{
			if (container == null || equipmentSlot < 0 || equipmentSlot >= slotViews.Length)
			{
				return;
			}

			if (!container.IsSlotEmpty(equipmentSlot))
			{
				SetSlotItem(equipmentSlot, item);
			}
			else
			{
				ClearSlot(equipmentSlot);
			}
		}

		// ── Attribute update callbacks ────────────────────────────────────────

		/// <summary>
		/// Refreshes the attribute label for an updated attribute.
		/// </summary>
		public void OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (!attributeValueLabels.TryGetValue(attribute.Template.ID, out Label valueLabel))
			{
				return;
			}

			if (attribute.Template.IsResourceAttribute)
			{
				CharacterResourceAttribute resource = attribute as CharacterResourceAttribute;
				if (resource != null)
				{
					valueLabel.text = Mathf.RoundToInt(resource.CurrentValue) + " / " + resource.FinalValue;
					UpdateStatusBarChip(attribute.Template.Name, valueLabel.text);
				}
			}
			else
			{
				valueLabel.text = attribute.Template.IsPercentage
					? attribute.FinalValue + "%"
					: attribute.FinalValue.ToString();
			}
		}

		// ── Camera ────────────────────────────────────────────────────────────

		/// <summary>
		/// Assigns the camera used for the 3D character preview viewport.
		/// Pass null to clear the reference.
		/// </summary>
		/// <param name="camera">Camera to use for preview rendering.</param>
		public void SetEquipmentViewCamera(Camera camera)
		{
			equipmentViewCamera = camera;
		}

		/// <summary>
		/// Assigns a RenderTexture to the preview-rt element so the camera feed is
		/// visible inside the UXML viewport.
		/// </summary>
		/// <param name="rt">The render texture to display.</param>
		public void SetPreviewRenderTexture(RenderTexture rt)
		{
			if (previewRt == null)
			{
				return;
			}

			previewRt.style.backgroundImage = rt != null
				? new StyleBackground(Background.FromRenderTexture(rt))
				: StyleKeyword.None;
		}

		// ── Private helpers ───────────────────────────────────────────────────

		/// <summary>
		/// Wires a tab button's clicked callback and initialises its active CSS class.
		/// </summary>
		private void WireTab(Button button, string tabName, VisualElement root)
		{
			if (button == null)
			{
				return;
			}
			button.clicked += () =>
			{
				activeTab = tabName;
				ApplyTabState(root);
			};
		}

		/// <summary>
		/// Applies the active/inactive CSS class to all tab buttons and shows or hides
		/// the panel body and attribute footer according to the active tab.
		/// </summary>
		private void ApplyTabState(VisualElement root)
		{
			SetTabActive(root.Q<Button>(TAB_GEAR_NAME),  activeTab == TAB_GEAR_NAME);
			SetTabActive(root.Q<Button>(TAB_STATS_NAME), activeTab == TAB_STATS_NAME);
			SetTabActive(root.Q<Button>(TAB_SETS_NAME),  activeTab == TAB_SETS_NAME);

			// GEAR  → show slot grid, hide attribute scroll
			// STATS → hide slot grid, show attribute scroll
			// SETS  → hide both (placeholder)
			bool showBody   = activeTab == TAB_GEAR_NAME;
			bool showFooter = activeTab == TAB_STATS_NAME;

			SetElementVisible(panelBody,   showBody);
			SetElementVisible(panelFooter, showFooter);
		}

		/// <summary>Adds or removes the active styling class on a tab button.</summary>
		/// <param name="button">The tab button to style.</param>
		/// <param name="active">Whether the tab is the active tab.</param>
		private static void SetTabActive(Button button, bool active)
		{
			if (button == null)
			{
				return;
			}
			if (active)
			{
				button.AddToClassList(CSS_TAB_ACTIVE);
			}
			else
			{
				button.RemoveFromClassList(CSS_TAB_ACTIVE);
			}
		}

		/// <summary>
		/// Shows or hides a VisualElement using the display style (no layout space when hidden).
		/// </summary>
		private static void SetElementVisible(VisualElement element, bool visible)
		{
			if (element == null)
			{
				return;
			}
			element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>
		/// Refreshes a slot's icon, amount, and lock display from the equipment container.
		/// </summary>
		private void RefreshSlot(IEquipmentController container, int slotIndex)
		{
			if (container.TryGetItem(slotIndex, out Item item))
			{
				SetSlotItem(slotIndex, item);
			}
			else
			{
				ClearSlot(slotIndex);
			}
		}

		/// <summary>
		/// Populates a slot's icon and stack-count badge.
		/// </summary>
		private void SetSlotItem(int slotIndex, Item item)
		{
			if (item == null || slotIndex < 0 || slotIndex >= slotViews.Length)
			{
				return;
			}

			ref SlotView view = ref slotViews[slotIndex];
			if (view.Root == null)
			{
				return;
			}

			if (view.Icon != null && item.Template != null && item.Template.Icon != null)
			{
				view.Icon.style.backgroundImage = new StyleBackground(item.Template.Icon);
			}

			if (view.Amount != null)
			{
				if (item.IsStackable && item.Stackable != null)
				{
					view.Amount.text = item.Stackable.Amount.ToString();
					view.Amount.RemoveFromClassList(CSS_HIDDEN);
				}
				else
				{
					view.Amount.text = "";
					view.Amount.AddToClassList(CSS_HIDDEN);
				}
			}
		}

		/// <summary>
		/// Clears a slot's icon and hides the stack-count badge.
		/// </summary>
		private void ClearSlot(int slotIndex)
		{
			if (slotIndex < 0 || slotIndex >= slotViews.Length)
			{
				return;
			}

			ref SlotView view = ref slotViews[slotIndex];
			if (view.Root == null)
			{
				return;
			}

			if (view.Icon != null)
			{
				view.Icon.style.backgroundImage = StyleKeyword.None;
			}
			if (view.Amount != null)
			{
				view.Amount.text = "";
				view.Amount.AddToClassList(CSS_HIDDEN);
			}
		}

		/// <summary>
		/// Shows or hides the lock overlay on a slot.
		/// </summary>
		private void SetSlotLocked(int slotIndex, bool isLocked)
		{
			if (slotIndex < 0 || slotIndex >= slotViews.Length)
			{
				return;
			}

			VisualElement lockEl = slotViews[slotIndex].Lock;
			if (lockEl == null)
			{
				return;
			}

			if (isLocked)
			{
				lockEl.RemoveFromClassList(CSS_HIDDEN);
			}
			else
			{
				lockEl.AddToClassList(CSS_HIDDEN);
			}
		}

		/// <summary>
		/// Handles pointer-down events on an equipment slot element.
		/// Left button: drag-and-drop equip or start drag.
		/// Right button: unequip.
		/// </summary>
		private void OnSlotPointerDown(PointerDownEvent evt, int slotIndex)
		{
			if (Character == null || Client == null)
			{
				return;
			}

			if (evt.button == 0) // left
			{
				HandleSlotLeftClick(slotIndex);
			}
			else if (evt.button == 1) // right
			{
				HandleSlotRightClick(slotIndex);
			}
		}

		/// <summary>
		/// Shows the item tooltip when the pointer enters an equipment slot that contains an item.
		/// </summary>
		private void OnSlotPointerEnter(int slotIndex)
		{
			if (Character == null ||
				!Character.TryGet(out IEquipmentController equipmentController) ||
				!equipmentController.TryGetItem(slotIndex, out Item item))
			{
				return;
			}

			if (UIManager.TryGetTK("UITooltip", out UITKTooltip tooltip))
			{
				tooltip.Open(item.Tooltip());
			}
		}

		/// <summary>
		/// Hides the item tooltip when the pointer leaves an equipment slot.
		/// </summary>
		private void OnSlotPointerLeave()
		{
			if (UIManager.TryGetTK("UITooltip", out UITKTooltip tooltip))
			{
				tooltip.Hide();
			}
		}

		/// <summary>
		/// Left-click: if a drag object is active, equip the dragged item into this slot;
		/// otherwise begin dragging the item currently in this slot.
		/// </summary>
		private void HandleSlotLeftClick(int slotIndex)
		{
			if (!UIManager.TryGetTK("UIDragObject", out UITKDragObject dragObject))
			{
				return;
			}

			if (!Character.TryGet(out IEquipmentController equipmentController))
			{
				return;
			}

			if (dragObject.Visible)
			{
				int referenceID = (int)dragObject.ReferenceID;

				if (dragObject.Type == ReferenceButtonType.Inventory &&
					Character.TryGet(out IInventoryController inventoryController))
				{
					Item item = inventoryController.Items[referenceID];
					if (item != null)
					{
						Client.Broadcast(new EquipmentEquipItemBroadcast()
						{
							InventoryIndex = referenceID,
							Slot           = (byte)slotIndex,
							FromInventory  = InventoryType.Inventory,
						}, Channel.Reliable);
					}
				}
				else if (dragObject.Type == ReferenceButtonType.Bank &&
						 Character.TryGet(out IBankController bankController))
				{
					Item item = bankController.Items[referenceID];
					if (item != null)
					{
						Client.Broadcast(new EquipmentEquipItemBroadcast()
						{
							InventoryIndex = referenceID,
							Slot           = (byte)slotIndex,
							FromInventory  = InventoryType.Bank,
						}, Channel.Reliable);
					}
				}

				dragObject.Clear();
			}
			else if (!equipmentController.IsSlotEmpty(slotIndex) &&
					 slotViews[slotIndex].Icon != null)
			{
				// Begin drag: copy the slot icon sprite into the drag object
				Sprite icon = GetSlotSprite(slotIndex);
				if (icon != null)
				{
					dragObject.SetReference(icon, slotIndex, ReferenceButtonType.Equipment);
				}
			}
		}

		/// <summary>
		/// Right-click: unequip the item to the inventory.
		/// </summary>
		private void HandleSlotRightClick(int slotIndex)
		{
			if (UIManager.TryGetTK("UIDragObject", out UITKDragObject dragObject) && dragObject.Visible)
			{
				dragObject.Clear();
			}

			if (!Character.TryGet(out IEquipmentController equipmentController))
			{
				return;
			}

			if (!equipmentController.IsSlotEmpty(slotIndex))
			{
				ClearSlot(slotIndex);

				Client.Broadcast(new EquipmentUnequipItemBroadcast()
				{
					Slot        = (byte)slotIndex,
					ToInventory = InventoryType.Inventory,
				}, Channel.Reliable);
			}
		}

		/// <summary>
		/// Reads the current background-image sprite from a slot icon element.
		/// Returns null if the slot has no icon assigned.
		/// </summary>
		private Sprite GetSlotSprite(int slotIndex)
		{
			if (slotIndex < 0 || slotIndex >= slotViews.Length || slotViews[slotIndex].Icon == null)
			{
				return null;
			}

			StyleBackground bg = slotViews[slotIndex].Icon.style.backgroundImage;
			return bg.value.sprite;
		}

		// ── Attribute row building ────────────────────────────────────────────

		/// <summary>
		/// Categorises all character attributes and creates the scrollable attribute rows
		/// inside the <c>attribute-list</c> ScrollView.
		/// </summary>
		private void BuildAttributeRows(ICharacterAttributeController attributeController)
		{
			if (attributeList == null)
			{
				return;
			}

			var resourceAttributes    = new List<CharacterAttribute>();
			var damageAttributes      = new List<CharacterAttribute>();
			var resistanceAttributes  = new List<CharacterAttribute>();
			var coreAttributes        = new List<CharacterAttribute>();

			foreach (CharacterResourceAttribute ra in attributeController.ResourceAttributes.Values)
			{
				resourceAttributes.Add(ra);
			}

			foreach (CharacterAttribute attr in attributeController.Attributes.Values)
			{
				if (attr.Template.Name.Contains("Regeneration"))
				{
					resourceAttributes.Add(attr);
				}
				else if (attr.Template is DamageAttributeTemplate)
				{
					damageAttributes.Add(attr);
				}
				else if (attr.Template is ResistanceAttributeTemplate)
				{
					resistanceAttributes.Add(attr);
				}
				else
				{
					coreAttributes.Add(attr);
				}
			}

			AddAttributeCategory("Resource",    resourceAttributes);
			AddAttributeCategory("Damage",      damageAttributes);
			AddAttributeCategory("Resistance",  resistanceAttributes);
			AddAttributeCategory("Core",        coreAttributes);

			resourceAttributes.Clear();
			damageAttributes.Clear();
			resistanceAttributes.Clear();
			coreAttributes.Clear();
		}

		/// <summary>
		/// Creates a category header label and one row per attribute, appending all to the
		/// attribute ScrollView and subscribing to <see cref="CharacterAttribute.OnAttributeUpdated"/>.
		/// </summary>
		private void AddAttributeCategory(string categoryName, List<CharacterAttribute> attributes)
		{
			if (attributes == null || attributes.Count == 0 || attributeList == null)
			{
				return;
			}

			// Category header
			Label header = new Label(categoryName);
			header.AddToClassList(CSS_ATTR_CATEGORY);
			attributeList.Add(header);
			attributeCategoryElements.Add(header);

			for (int i = 0; i < attributes.Count; ++i)
			{
				CharacterAttribute attribute = attributes[i];

				attribute.OnAttributeUpdated -= OnAttributeUpdated; // defensive dedup

				VisualElement row = new VisualElement();
				row.AddToClassList(CSS_ATTR_ROW);

				Label nameLabel = new Label(attribute.Template.Name);
				nameLabel.AddToClassList(CSS_ATTR_NAME);
				row.Add(nameLabel);

				Label valueLabel = new Label();
				valueLabel.AddToClassList(CSS_ATTR_VALUE);

				if (attribute.Template.IsResourceAttribute)
				{
					CharacterResourceAttribute resource = attribute as CharacterResourceAttribute;
					if (resource != null)
					{
						valueLabel.text = Mathf.RoundToInt(resource.CurrentValue) + " / " + resource.FinalValue;
					}
					valueLabel.AddToClassList(CSS_ATTR_RESOURCE);
				}
				else
				{
					valueLabel.text = attribute.Template.IsPercentage
						? attribute.FinalValue + "%"
						: attribute.FinalValue.ToString();

					if (attribute.Template.IsPercentage)
					{
						valueLabel.AddToClassList(CSS_ATTR_PERCENT);
					}
				}

				row.Add(valueLabel);
				attributeList.Add(row);
				attributeRowElements.Add(row);

				attributeValueLabels[attribute.Template.ID] = valueLabel;
				attribute.OnAttributeUpdated += OnAttributeUpdated;
			}
		}

		/// <summary>
		/// Removes all runtime-created attribute elements and unsubscribes from all attribute events.
		/// </summary>
		private void DestroyAttributeElements()
		{
			// Unsubscribe from all attribute events before clearing
			if (Character != null && Character.TryGet(out ICharacterAttributeController ac))
			{
				foreach (CharacterAttribute attr in ac.Attributes.Values)
				{
					attr.OnAttributeUpdated -= OnAttributeUpdated;
				}
				foreach (CharacterResourceAttribute ra in ac.ResourceAttributes.Values)
				{
					ra.OnAttributeUpdated -= OnAttributeUpdated;
				}
			}

			if (attributeList != null)
			{
				for (int i = 0; i < attributeCategoryElements.Count; ++i)
				{
					attributeList.Remove(attributeCategoryElements[i]);
				}
				for (int i = 0; i < attributeRowElements.Count; ++i)
				{
					attributeList.Remove(attributeRowElements[i]);
				}
			}

			attributeCategoryElements.Clear();
			attributeRowElements.Clear();
			attributeValueLabels.Clear();
		}

		// ── Status bar ────────────────────────────────────────────────────────

		/// <summary>
		/// Populates the HP, MP, and Stamina status-bar chips from the attribute controller.
		/// </summary>
		private void UpdateStatusBar(ICharacterAttributeController ac)
		{
			if (ac.TryGetHealthAttribute(out CharacterResourceAttribute hp) && statHpLabel != null)
			{
				statHpLabel.text = Mathf.RoundToInt(hp.CurrentValue) + " / " + hp.FinalValue;
			}
			if (ac.TryGetManaAttribute(out CharacterResourceAttribute mp) && statMpLabel != null)
			{
				statMpLabel.text = Mathf.RoundToInt(mp.CurrentValue) + " / " + mp.FinalValue;
			}
			if (ac.TryGetStaminaAttribute(out CharacterResourceAttribute stam) && statStamLabel != null)
			{
				statStamLabel.text = Mathf.RoundToInt(stam.CurrentValue) + " / " + stam.FinalValue;
			}
		}

		/// <summary>
		/// Resets all status-bar chip labels to the default placeholder.
		/// </summary>
		private void ResetStatusBar()
		{
			if (statHpLabel   != null) statHpLabel.text   = "—";
			if (statMpLabel   != null) statMpLabel.text   = "—";
			if (statStamLabel != null) statStamLabel.text = "—";
			if (gearScoreLabel != null) gearScoreLabel.text = "GS: —";
		}

		/// <summary>
		/// Updates a status-bar chip whose attribute name matches one of the known resource labels.
		/// Called from <see cref="OnAttributeUpdated"/> to keep the chips in sync.
		/// </summary>
		private void UpdateStatusBarChip(string templateName, string formattedValue)
		{
			if (templateName.Contains("Health") && statHpLabel != null)
			{
				statHpLabel.text = formattedValue;
			}
			else if (templateName.Contains("Mana") && statMpLabel != null)
			{
				statMpLabel.text = formattedValue;
			}
			else if (templateName.Contains("Stamina") && statStamLabel != null)
			{
				statStamLabel.text = formattedValue;
			}
		}
	}
}
