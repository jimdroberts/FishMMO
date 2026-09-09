using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit ability crafting panel: a base ability, the effects configured onto it, and a
	/// live preview of what the two would produce.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Crafting is the game's ability designer. A base ability is the core — a fireball that hangs
	/// in the air — and effects configure it: one to move it forward, one to make it bigger. Each
	/// effect pays for itself in cooldown, cast time and resource cost as well as in currency, and
	/// the point of this panel is that the player can see that trade before they commit.
	/// </para>
	/// <para>
	/// The preview is composed by <see cref="AbilitySummary"/> — the same composition the server
	/// performs when it builds the crafted ability, and the same one the finished ability's tooltip
	/// reports afterwards. It used to be a third, separate calculation that ignored the effects a
	/// template already ships with, so the preview and the ability the player received disagreed.
	/// Every stat now also carries what the chosen effects changed it by.
	/// </para>
	/// </remarks>
	public class UITKAbilityCraft : UITKCharacterControl
	{
		/// <summary>The maximum number of event slots the panel will draw for one ability.</summary>
		private const int MAX_CRAFT_EVENT_SLOTS = 10;

		/// <summary>Element names inside the UXML.</summary>
		private const string MAIN_ENTRY_NAME = "craft-main-entry";
		private const string DESCRIPTION_NAME = "craft-description";
		private const string PREVIEW_EMPTY_NAME = "craft-preview-empty";
		private const string COST_NAME = "craft-cost";
		private const string BALANCE_NAME = "craft-balance";
		private const string EVENT_LIST_NAME = "craft-event-list";
		private const string SLOTS_HEAD_NAME = "craft-slots-head";
		private const string SLOTS_EMPTY_NAME = "craft-slots-empty";
		private const string CRAFT_BUTTON_NAME = "craft-confirm-btn";
		private const string CLOSE_BUTTON_NAME = "craft-close-btn";
		private const string STATUS_NAME = "craft-status";

		/// <summary>USS class applied to runtime-created effect slot rows.</summary>
		private const string SLOT_CLASS = "craft-slot";

		/// <summary>USS class marking a slot with nothing in it.</summary>
		private const string SLOT_EMPTY_CLASS = "craft-slot--empty";

		/// <summary>
		/// The template ID for the currency used to craft abilities.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int CurrencyTemplateID;

		/// <summary>The last interactable ID used for crafting.</summary>
		private long lastInteractableID = 0;

		/// <summary>The base ability entry button.</summary>
		private Button mainEntryButton;

		/// <summary>The container the composed preview is rendered into.</summary>
		private VisualElement descriptionRoot;

		/// <summary>The preview's placeholder, shown until a base ability is chosen.</summary>
		private Label previewEmpty;

		/// <summary>The cost and balance labels.</summary>
		private Label costLabel;
		private Label balanceLabel;

		/// <summary>The container holding the effect slot rows.</summary>
		private VisualElement eventListContainer;

		/// <summary>The effects header and the note shown when an ability takes no effects.</summary>
		private Label slotsHead;
		private Label slotsEmpty;

		/// <summary>The status line, where refusals and confirmations are written.</summary>
		private Label statusLabel;

		/// <summary>The status text, kept across the tree rebuilds a hide/show causes.</summary>
		private string statusText = string.Empty;

		/// <summary>Cached reference to the craft confirm button, for the submit lock.</summary>
		private Button craftButton;

		/// <summary>The slot rows currently built.</summary>
		private readonly List<VisualElement> slotRows = new List<VisualElement>();

		/// <summary>
		/// The effects chosen for each slot, as plain data.
		/// </summary>
		/// <remarks>
		/// The rows are <see cref="VisualElement"/>s belonging to one visual tree, and
		/// <c>UIDocument</c> re-clones the tree on every enable. Keeping the SELECTION only in
		/// those rows meant a hide/show — or any tree rebuild — silently emptied a half-built craft
		/// while the panel still looked populated to the code that reads it.
		/// </remarks>
		private readonly List<ITooltip> selectedEvents = new List<ITooltip>();

		/// <summary>The base ability selected for the craft.</summary>
		private AbilityTemplate selectedMain;

		/// <summary>How many effect slots the selected ability allows.</summary>
		private int selectedSlotCount;

		/// <summary>Submit lock held while a craft request is awaiting the server's reply.</summary>
		/// <remarks>
		/// The server answers every craft — accepted or refused — with
		/// <see cref="AbilityCraftResultBroadcast"/>, so the guard's timeout is a backstop for a
		/// reply that never arrives rather than the normal way the lock is released.
		/// </remarks>
		private readonly PendingReplyGuard craftGuard = new PendingReplyGuard();

		/// <summary>Seconds to wait for the server's answer to a craft.</summary>
		private const float CraftReplyTimeoutSeconds = 10.0f;

		/// <summary>
		/// Registers the ability crafter broadcast handlers when the client is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<AbilityCrafterBroadcast>(OnClientAbilityCrafterBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<AbilityCraftResultBroadcast>(OnClientAbilityCraftResultReceived);
		}

		/// <summary>
		/// Unregisters the ability crafter broadcast handlers when the client is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<AbilityCrafterBroadcast>(OnClientAbilityCrafterBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<AbilityCraftResultBroadcast>(OnClientAbilityCraftResultReceived);
		}

		/// <summary>
		/// Queries panel elements and wires the base entry, craft, and close buttons.
		/// </summary>
		public override void OnStarting()
		{
			/* Every row in slotRows belongs to the tree that was just discarded. */
			slotRows.Clear();

			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			descriptionRoot = root.Q(DESCRIPTION_NAME);
			previewEmpty = root.Q<Label>(PREVIEW_EMPTY_NAME);
			costLabel = root.Q<Label>(COST_NAME);
			balanceLabel = root.Q<Label>(BALANCE_NAME);
			eventListContainer = root.Q(EVENT_LIST_NAME);
			slotsHead = root.Q<Label>(SLOTS_HEAD_NAME);
			slotsEmpty = root.Q<Label>(SLOTS_EMPTY_NAME);
			statusLabel = root.Q<Label>(STATUS_NAME);

			mainEntryButton = root.Q<Button>(MAIN_ENTRY_NAME);
			if (mainEntryButton != null)
			{
				mainEntryButton.clicked += MainEntry_OnLeftClick;
				mainEntryButton.RegisterCallback<PointerDownEvent>(OnMainEntryPointerDown);
			}

			craftButton = root.Q<Button>(CRAFT_BUTTON_NAME);
			if (craftButton != null)
			{
				craftButton.clicked += OnCraft;
			}

			Button closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}
		}

		/// <summary>Clears all slots when the UI is being destroyed.</summary>
		public override void OnDestroying()
		{
			ClearSlots();
			base.OnDestroying();
		}

		/// <summary>
		/// Opens the panel for a crafter the player interacted with.
		/// </summary>
		/// <param name="msg">The broadcast message carrying the crafter's ID.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientAbilityCrafterBroadcastReceived(AbilityCrafterBroadcast msg, Channel channel)
		{
			lastInteractableID = msg.InteractableID;
			Show();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			ApplySelection();
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplySelection();
		}

		/// <summary>
		/// Re-applies the in-progress craft to the current visual tree.
		/// </summary>
		/// <remarks>
		/// Runs from both hooks: on the very first open <c>hasStarted</c> is still false so
		/// <c>ReinitializeIfTreeReplaced</c> bails and only <c>OnAfterShow</c> fires, while on later
		/// shows the tree may genuinely have been replaced.
		/// </remarks>
		private void ApplySelection()
		{
			ApplyMainEntry();
			BuildEventSlots();
			RefreshPreview();

			if (statusLabel != null)
			{
				statusLabel.text = statusText;
			}
			if (craftButton != null)
			{
				craftButton.SetEnabled(!craftGuard.IsPending);
			}
		}

		/// <summary>Draws the base ability row from the current selection.</summary>
		private void ApplyMainEntry()
		{
			if (mainEntryButton == null)
			{
				return;
			}

			mainEntryButton.Clear();
			mainEntryButton.text = string.Empty;

			VisualElement icon = new VisualElement();
			icon.AddToClassList("craft-main__icon");
			icon.pickingMode = PickingMode.Ignore;
			if (selectedMain?.Icon != null)
			{
				icon.style.backgroundImage = new StyleBackground(selectedMain.Icon);
			}
			mainEntryButton.Add(icon);

			VisualElement text = new VisualElement();
			text.AddToClassList("craft-main__text");
			text.pickingMode = PickingMode.Ignore;

			Label name = new Label(selectedMain != null ? selectedMain.Name : "Choose a base ability");
			name.AddToClassList("craft-main__name");
			name.pickingMode = PickingMode.Ignore;
			text.Add(name);

			Label meta = new Label(selectedMain != null
				? DescribeSlots(selectedMain.AdditionalEventSlots)
				: "Click to pick from the base abilities you know");
			meta.AddToClassList("craft-main__meta");
			meta.pickingMode = PickingMode.Ignore;
			text.Add(meta);

			mainEntryButton.Add(text);
		}

		/// <summary>How many effects an ability accepts, in words.</summary>
		private static string DescribeSlots(int slots)
		{
			if (slots <= 0)
			{
				return "Takes no additional effects";
			}
			return slots == 1 ? "1 effect slot" : $"{slots} effect slots";
		}

		/// <summary>
		/// Opens the selector for base abilities.
		/// </summary>
		private void MainEntry_OnLeftClick()
		{
			if (Character == null ||
				!Character.TryGet(out IAbilityController abilityController) ||
				!UIManager.TryGetTK("UISelector", out UITKSelector uiSelector))
			{
				return;
			}

			List<ICachedObject> templates = AbilityTemplate.Get<AbilityTemplate>(abilityController.KnownBaseAbilities);

			// An ability already crafted from a template must be forgotten before it can be crafted again.
			templates.RemoveAll(t => abilityController.KnowsLearnedAbility(t.ID));

			if (templates.Count == 0)
			{
				SetStatus("You know no base abilities you have not already crafted.");
				return;
			}

			uiSelector.Open(templates, (i) =>
			{
				AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(i);
				if (template == null)
				{
					return;
				}

				SetMainEntry(template);
				SetEventSlots(template.AdditionalEventSlots);
				RefreshPreview();
			});
		}

		/// <summary>Clears the base ability and every slot on right-click.</summary>
		private void MainEntry_OnRightClick()
		{
			SetMainEntry(null);
			ClearSlots();
			BuildEventSlots();
			RefreshPreview();
		}

		/// <summary>
		/// Opens the selector for effects to put in one slot.
		/// </summary>
		/// <param name="index">The index of the slot.</param>
		private void EventEntry_OnLeftClick(int index)
		{
			if (index < 0 || index >= selectedSlotCount)
			{
				return;
			}

			if (Character == null ||
				!Character.TryGet(out IAbilityController abilityController) ||
				!UIManager.TryGetTK("UISelector", out UITKSelector uiSelector))
			{
				return;
			}

			List<ICachedObject> templates = AbilityEvent.Get<AbilityEvent>(abilityController.KnownAbilityEvents);

			/* Anything already in another slot is not offered again. An ability holds one entry per
			 * event, and the server refuses a duplicate outright, so offering it would only produce
			 * a craft that cannot succeed. */
			for (int i = 0; i < selectedEvents.Count; ++i)
			{
				if (i == index)
				{
					continue;
				}
				if (selectedEvents[i] is AbilityTypeOverrideEventType)
				{
					templates.RemoveAll(t => t is AbilityTypeOverrideEventType);
				}
				if (selectedEvents[i] is ICachedObject cached)
				{
					templates.Remove(cached);
				}
			}

			if (templates.Count == 0)
			{
				SetStatus("You know no other effects to add.");
				return;
			}

			uiSelector.Open(templates, (i) =>
			{
				AbilityEvent template = AbilityEvent.Get<AbilityEvent>(i);
				if (template != null)
				{
					SetEventSlot(index, template);
					RefreshPreview();
				}
			});
		}

		/// <summary>Empties a slot on right-click.</summary>
		/// <param name="index">The index of the slot.</param>
		private void EventEntry_OnRightClick(int index)
		{
			if (index > -1 && index < selectedSlotCount)
			{
				SetEventSlot(index, null);
				RefreshPreview();
			}
		}

		/// <summary>Assigns the base ability.</summary>
		private void SetMainEntry(AbilityTemplate template)
		{
			selectedMain = template;
			ApplyMainEntry();
		}

		/// <summary>Assigns a slot's effect and redraws that row.</summary>
		private void SetEventSlot(int index, ITooltip tooltip)
		{
			if (index < 0 || index >= selectedSlotCount)
			{
				return;
			}

			while (selectedEvents.Count <= index)
			{
				selectedEvents.Add(null);
			}
			selectedEvents[index] = tooltip;

			BuildEventSlots();
		}

		/// <summary>
		/// Sets how many effect slots the panel offers.
		/// </summary>
		/// <param name="count">The template's allowance.</param>
		private void SetEventSlots(int count)
		{
			/* The per-ability limit. The server enforces this too — it used to accept up to a
			 * global 32 events regardless of what the template allowed, so this method was the ONLY
			 * thing standing between a crafted packet and a 32-effect ability on a 0-slot
			 * template. */
			selectedSlotCount = Mathf.Clamp(count, 0, MAX_CRAFT_EVENT_SLOTS);

			selectedEvents.Clear();
			for (int i = 0; i < selectedSlotCount; ++i)
			{
				selectedEvents.Add(null);
			}

			BuildEventSlots();
		}

		/// <summary>Removes every slot row and forgets what was in them.</summary>
		private void ClearSlots()
		{
			ClearSlotViews();
			selectedEvents.Clear();
			selectedSlotCount = 0;
		}

		/// <summary>Detaches the slot rows without forgetting which effects were chosen.</summary>
		private void ClearSlotViews()
		{
			foreach (VisualElement row in slotRows)
			{
				row?.RemoveFromHierarchy();
			}
			slotRows.Clear();
		}

		/// <summary>
		/// Rebuilds the slot rows for the current selection.
		/// </summary>
		private void BuildEventSlots()
		{
			ClearSlotViews();

			if (eventListContainer == null)
			{
				return;
			}

			while (selectedEvents.Count < selectedSlotCount)
			{
				selectedEvents.Add(null);
			}
			if (selectedEvents.Count > selectedSlotCount)
			{
				selectedEvents.RemoveRange(selectedSlotCount, selectedEvents.Count - selectedSlotCount);
			}

			for (int i = 0; i < selectedSlotCount && i < MAX_CRAFT_EVENT_SLOTS; ++i)
			{
				slotRows.Add(BuildSlotRow(i, selectedEvents[i]));
			}

			/* Both notes describe a real state and neither is an error: an ability with no slots is
			 * cast exactly as its template ships, and no base ability chosen yet is where every
			 * craft starts. */
			if (slotsHead != null)
			{
				slotsHead.style.display = selectedSlotCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (slotsEmpty != null)
			{
				bool show = selectedSlotCount == 0;
				slotsEmpty.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
				slotsEmpty.text = selectedMain == null
					? string.Empty
					: "This ability takes no additional effects. It is cast exactly as it comes.";
			}
		}

		/// <summary>Builds one slot row.</summary>
		/// <param name="index">The slot index.</param>
		/// <param name="chosen">What is in it, or null.</param>
		private VisualElement BuildSlotRow(int index, ITooltip chosen)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList("fish-row");
			row.AddToClassList(SLOT_CLASS);
			if (chosen == null)
			{
				row.AddToClassList(SLOT_EMPTY_CLASS);
			}

			Label ordinal = new Label((index + 1).ToString());
			ordinal.AddToClassList("craft-slot__index");
			ordinal.pickingMode = PickingMode.Ignore;
			row.Add(ordinal);

			VisualElement icon = new VisualElement();
			icon.AddToClassList("craft-slot__icon");
			icon.pickingMode = PickingMode.Ignore;
			if (chosen?.Icon != null)
			{
				icon.style.backgroundImage = new StyleBackground(chosen.Icon);
			}
			row.Add(icon);

			VisualElement text = new VisualElement();
			text.AddToClassList("craft-slot__text");
			text.pickingMode = PickingMode.Ignore;

			Label name = new Label(chosen != null ? chosen.Name : "Empty effect slot");
			name.AddToClassList("craft-slot__name");
			name.pickingMode = PickingMode.Ignore;
			text.Add(name);

			Label meta = new Label(DescribeSlotContents(chosen));
			meta.AddToClassList("craft-slot__meta");
			meta.pickingMode = PickingMode.Ignore;
			text.Add(meta);
			row.Add(text);

			int price = chosen is AbilityEvent abilityEvent ? abilityEvent.Price
				: chosen is BaseAbilityTemplate baseTemplate ? baseTemplate.Price
				: 0;
			if (price > 0)
			{
				Label priceLabel = new Label(price.ToString());
				priceLabel.AddToClassList("craft-slot__price");
				priceLabel.pickingMode = PickingMode.Ignore;
				row.Add(priceLabel);
			}

			int captured = index;
			row.RegisterCallback<PointerDownEvent>(evt =>
			{
				if (evt.button == 1)
				{
					EventEntry_OnRightClick(captured);
				}
				else if (evt.button == 0)
				{
					EventEntry_OnLeftClick(captured);
				}
			});

			eventListContainer.Add(row);
			return row;
		}

		/// <summary>What a slot's contents do, in one line.</summary>
		private static string DescribeSlotContents(ITooltip chosen)
		{
			switch (chosen)
			{
				case null:
					return "Click to choose an effect  ·  right-click to clear";
				case AbilityEvent abilityEvent:
					{
						List<string> parts = new List<string> { AbilitySummary.EventCategory(abilityEvent) };
						AppendModifier(parts, abilityEvent.Cooldown, "s cd");
						AppendModifier(parts, abilityEvent.ActivationTime, "s cast");
						AppendModifier(parts, abilityEvent.Speed, "m/s");
						AppendModifier(parts, abilityEvent.LifeTime, "s life");
						return string.Join("  ·  ", parts);
					}
				case AbilityTypeOverrideEventType typeOverride:
					return $"changes the ability to {typeOverride.OverrideAbilityType}";
				default:
					return string.Empty;
			}
		}

		/// <summary>Appends a signed modifier, or nothing when the effect does not change that stat.</summary>
		private static void AppendModifier(List<string> parts, float value, string suffix)
		{
			if (Mathf.Abs(value) < 0.0005f)
			{
				return;
			}
			parts.Add(value > 0.0f ? $"+{value:0.##}{suffix}" : $"{value:0.##}{suffix}");
		}

		/// <summary>
		/// Recomposes the preview from the current recipe and redraws the cost.
		/// </summary>
		/// <remarks>
		/// The whole point of the panel. <see cref="AbilitySummary"/> composes the base ability
		/// with the chosen effects using the same rules the server will, and the rendered content
		/// carries a delta on every stat an effect moved — so "this costs 4 more mana and 1.2s more
		/// cooldown" is visible before the craft, not discovered after it.
		/// </remarks>
		private void RefreshPreview()
		{
			if (selectedMain == null)
			{
				UITKTooltipView.Render(descriptionRoot, null);
				SetDisplayed(previewEmpty, true);
				SetCost(0);
				return;
			}

			SetDisplayed(previewEmpty, false);

			AbilitySummary summary = AbilitySummary.Compose(selectedMain, selectedEvents);
			TooltipContent content = new TooltipContent();
			summary.BuildTooltip(content, showDeltas: summary.HasCraftedEvents, includePrice: false);
			UITKTooltipView.Render(descriptionRoot, content);

			SetCost(summary.CraftPrice);
		}

		/// <summary>Writes the cost line and says whether the character can pay it.</summary>
		private void SetCost(int price)
		{
			if (costLabel != null)
			{
				/* "Free" rather than "Cost: 0". Every ability and effect in the project currently
				 * ships at a price of zero, so this is the line a player actually meets, and a
				 * zero reads as a value that failed to load rather than as a price. */
				costLabel.text = selectedMain == null ? "Cost: —"
					: price > 0 ? $"Cost: {price}"
					: "Cost: Free";
			}

			if (balanceLabel == null)
			{
				return;
			}

			if (!TryGetCurrencyBalance(out long balance))
			{
				balanceLabel.text = string.Empty;
				balanceLabel.RemoveFromClassList("craft-balance--short");
				return;
			}

			balanceLabel.text = $"You have {balance}";
			balanceLabel.EnableInClassList("craft-balance--short", balance < price);
		}

		/// <summary>
		/// Sends the craft request.
		/// </summary>
		public void OnCraft()
		{
			/* Double-submit guard. The craft is a purchase: two clicks a frame apart used to send
			 * two AbilityCraftBroadcasts, and only the server's 100ms ingress debounce stood
			 * between the player and paying twice. Released by the server's answer, which arrives
			 * for a refusal as well as a success. */
			if (craftGuard.IsPending)
			{
				return;
			}

			if (selectedMain == null)
			{
				SetStatus("Choose a base ability to craft.");
				return;
			}

			AbilitySummary summary = AbilitySummary.Compose(selectedMain, selectedEvents);

			List<int> eventIds = new List<int>();
			foreach (ITooltip chosen in selectedEvents)
			{
				switch (chosen)
				{
					case AbilityEvent abilityEvent:
						eventIds.Add(abilityEvent.ID);
						break;
					case AbilityTypeOverrideEventType typeOverride:
						eventIds.Add(typeOverride.ID);
						break;
				}
			}

			/* The affordability check is a COURTESY, not a gate.
			 *
			 * It used to be a gate, and a hard one: an unset CurrencyTemplateID — which is exactly
			 * what this panel shipped with — refused every craft here, before anything was sent, in
			 * silence. The server holds the authoritative balance, charges against it, and answers
			 * a craft it cannot afford with a reason, so a client that cannot resolve the currency
			 * template sends the request and lets the server answer rather than refusing on the
			 * player's behalf. */
			if (Character != null &&
				TryGetCurrencyBalance(out long balance) &&
				balance < summary.CraftPrice)
			{
				SetStatus($"You cannot afford that. It costs {summary.CraftPrice} and you have {balance}.");
				return;
			}

			Client.Broadcast(new AbilityCraftBroadcast()
			{
				InteractableID = lastInteractableID,
				TemplateID = selectedMain.ID,
				Events = eventIds.ToArray(),
			}, Channel.Reliable);

			SetCraftPending(true);
			SetStatus("Crafting...");

			/* The recipe is NOT cleared here. It used to be, on send, so a refused craft threw away
			 * the ability and every effect the player had just chosen and left them to rebuild it
			 * with no idea what went wrong. It is cleared when the server confirms the craft. */
		}

		/// <summary>
		/// Reads the character's currency balance, when this panel is configured to know about it.
		/// </summary>
		/// <remarks>
		/// Reads the BASE value, which is what the server charges against. Testing FinalValue — the
		/// base plus every modifier in force — offered a character with a currency-boosting buff a
		/// craft the server would then refuse.
		/// </remarks>
		/// <param name="balance">The balance, when it could be read.</param>
		/// <returns>True when the balance is known; false when it is not, which is not a refusal.</returns>
		private bool TryGetCurrencyBalance(out long balance)
		{
			balance = 0;

			if (CurrencyTemplateID == 0 || Character == null)
			{
				return false;
			}

			/* Resolved to a template here rather than passing the raw ID. CharacterCurrency speaks
			 * in templates only, and this panel is configured with an ID, so the conversion has to
			 * happen somewhere — doing it at the one call site that needs it is cheaper than a
			 * parallel ID-shaped API that exists for a single caller. */
			CharacterAttributeTemplate currencyTemplate = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(CurrencyTemplateID);
			if (currencyTemplate == null)
			{
				Log.Debug("UITKAbilityCraft", $"CurrencyTemplateID {CurrencyTemplateID} did not resolve to a template.");
				return false;
			}

			return CharacterCurrency.TryGetBalance(Character, currencyTemplate, out balance);
		}

		/// <summary>Arms or releases the submit lock and reflects it on the confirm button.</summary>
		/// <param name="pending">True while a request is in flight.</param>
		private void SetCraftPending(bool pending)
		{
			if (pending)
			{
				craftGuard.Begin(CraftReplyTimeoutSeconds);
			}
			else
			{
				craftGuard.Clear();
			}

			if (craftButton != null)
			{
				craftButton.SetEnabled(!pending);
			}
		}

		/// <summary>Writes the status line, remembering it across tree rebuilds.</summary>
		/// <param name="text">The text to display.</param>
		private void SetStatus(string text)
		{
			statusText = text ?? string.Empty;

			if (statusLabel != null)
			{
				statusLabel.text = statusText;
			}
		}

		/// <summary>Releases a submit lock whose reply never arrived.</summary>
		protected override void OnTick()
		{
			if (craftGuard.HasExpired())
			{
				SetCraftPending(false);
				SetStatus("No reply from the server; try again.");
			}
		}

		/// <summary>
		/// Applies the server's answer to a craft request.
		/// </summary>
		/// <param name="msg">The broadcast message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientAbilityCraftResultReceived(AbilityCraftResultBroadcast msg, Channel channel)
		{
			SetCraftPending(false);

			if (msg.Success)
			{
				SetStatus(msg.Charged > 0
					? $"Crafted for {msg.Charged}. It is on your Abilities tab, ready to hotkey."
					: "Crafted. It is on your Abilities tab, ready to hotkey.");

				/* Cleared on success only. The crafted ability can no longer be selected — the
				 * base-ability selector filters out anything already learned — so leaving it in the
				 * recipe would show a craft that cannot be repeated. */
				SetMainEntry(null);
				ClearSlots();
				BuildEventSlots();
				RefreshPreview();
				return;
			}

			SetStatus(DescribeCraftFailure(msg.Failure));
		}

		/// <summary>Player-facing wording for a refusal.</summary>
		private static string DescribeCraftFailure(AbilityCraftFailure failure)
		{
			switch (failure)
			{
				case AbilityCraftFailure.Unavailable:
					return "That crafter is no longer available.";
				case AbilityCraftFailure.CannotAct:
					return "You cannot craft right now.";
				case AbilityCraftFailure.InvalidEntry:
					return "That ability is no longer available.";
				case AbilityCraftFailure.NotKnown:
					return "You have not learned that base ability yet.";
				case AbilityCraftFailure.AlreadyCrafted:
					return "You already have that ability; forget it before crafting it again.";
				case AbilityCraftFailure.AbilityLimit:
					return "You cannot hold any more abilities.";
				case AbilityCraftFailure.InvalidEvents:
					return "Those effects cannot be combined on that ability.";
				case AbilityCraftFailure.InsufficientFunds:
					return "You cannot afford that.";
				case AbilityCraftFailure.Busy:
					return "Still handling your last request; try again.";
				case AbilityCraftFailure.PersistFailed:
					return "The craft could not be saved; nothing was charged.";
				default:
					return "The crafter refused that craft.";
			}
		}

		/// <summary>Handles right-click on the base ability row.</summary>
		/// <param name="evt">The pointer down event.</param>
		private void OnMainEntryPointerDown(PointerDownEvent evt)
		{
			if (evt.button == 1)
			{
				MainEntry_OnRightClick();
			}
		}

		/// <summary>Shows or hides an element without disturbing its layout rules.</summary>
		private static void SetDisplayed(VisualElement element, bool displayed)
		{
			if (element != null)
			{
				element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}
	}
}
