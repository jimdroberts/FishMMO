using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// The Mercenary window (issue #135): the player's hired mercenary, its gear, its attributes, its stance, and sending
	/// it to rest (to free its party seat), bringing it back, or releasing it. It also asks "Would you like to hire this
	/// mercenary?" when a Mercenary is spoken to, and says what the server answered.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Laid out as the player's own character sheet (<c>UICharacterSheet.uxml</c>, whose stylesheet it loads): the same
	/// sockets in the same places around a preview of the mercenary, painted with the same painter
	/// (<see cref="UITKSlotPainter"/>). There is no saddle socket: a mercenary does not ride
	/// (<see cref="MercenaryRules.Wears"/>).
	/// </para>
	/// <para>
	/// The state shown is the server's (<see cref="IMercenaryController"/>, <see cref="IMercenaryGear"/>, both filled from
	/// broadcasts); the attributes and the preview are read off the mercenary itself while it is out and seen here. Gear is
	/// given by dragging it from the bags onto its socket, or by right-clicking it in the bags while this window is open
	/// (<see cref="MercenaryClient"/>); clicking a socket takes it back. Nothing is written here first: a socket changes
	/// when the server's gear broadcast says so.
	/// </para>
	/// </remarks>
	public class UITKMercenary : UITKCharacterControl
	{
		/// <summary>The control's name (the prefab's).</summary>
		public const string ControlName = "UIMercenary";

		private const string DIALOG_NAME = "UIDialogBox";
		private const string TOAST_NAME = "UIToast";
		private const string CSS_HIDDEN = "merc-hidden";
		private const string CSS_TAB_ACTIVE = "fish-tab--active";

		/// <summary>The sheet's class that hides a socket's stack badge (UICharacterSheet.uss).</summary>
		private const string CSS_SOCKET_HIDDEN = "eq-hidden";

		/// <summary>Seconds a Release press stays armed for the confirming second press.</summary>
		private const float ReleaseArmSeconds = 3f;

		/// <summary>Seconds between attribute refreshes while the window is open.</summary>
		private const float AttributeRefreshSeconds = 0.5f;

		private Label nameLabel;
		private Label kitLabel;
		private Label statusLabel;
		private Label emptyLabel;
		private Label attributeHint;
		private Label previewLabel;
		private VisualElement summary;
		private VisualElement attributes;
		private VisualElement footer;
		private VisualElement previewRt;
		private ScrollView attributeList;
		private Button restButton;
		private Button releaseButton;
		private Button passiveButton;
		private Button defensiveButton;
		private Button aggressiveButton;

		private IMercenaryController controller;
		private IMercenaryGear gear;
		private float releaseArmedUntil;
		private float nextAttributeRefresh;
		private bool hadMercenary;

		[Tooltip("Open the bags with this window, so gear can be dragged (or right-clicked) onto the mercenary.")]
		[SerializeField] private bool openInventoryOnShow = true;

		/// <inheritdoc />
		protected override bool OpensInventoryOnShow => openInventoryOnShow;

		/// <summary>The sockets, by gear slot (an <see cref="ItemSlot"/> value); null for one the window has not got (the saddle).</summary>
		private readonly List<VisualElement> sockets = new List<VisualElement>();

		/// <summary>Photographs the mercenary into the preview, as the sheet does the player.</summary>
		private readonly EquipmentPreviewRenderer previewRenderer = new EquipmentPreviewRenderer();

		/// <summary>The camera made for the preview, parented to <see cref="previewSubject"/>; null when none is.</summary>
		private Camera previewCamera;

		/// <summary>The mercenary the preview camera is on.</summary>
		private Mercenary previewSubject;

		/// <summary>Whether the preview has been framed to its subject since the camera was made.</summary>
		private bool previewFramed;

		/// <inheritdoc />
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}
			Button close = root.Q<Button>("close-button");
			if (close != null)
			{
				close.clicked += Hide;
			}
			nameLabel = root.Q<Label>("merc-name");
			kitLabel = root.Q<Label>("merc-kit");
			statusLabel = root.Q<Label>("merc-status");
			emptyLabel = root.Q<Label>("merc-empty");
			attributeHint = root.Q<Label>("merc-attribute-hint");
			previewLabel = root.Q<Label>("preview-label");
			previewRt = root.Q("preview-rt");
			summary = root.Q("merc-summary");
			attributes = root.Q("merc-attributes");
			footer = root.Q("panel-footer");
			attributeList = root.Q<ScrollView>("merc-attribute-list");
			restButton = root.Q<Button>("merc-rest");
			releaseButton = root.Q<Button>("merc-release");
			passiveButton = root.Q<Button>("merc-stance-passive");
			defensiveButton = root.Q<Button>("merc-stance-defensive");
			aggressiveButton = root.Q<Button>("merc-stance-aggressive");

			if (restButton != null)
			{
				restButton.clicked += OnClick_RestOrReturn;
			}
			if (releaseButton != null)
			{
				releaseButton.clicked += OnClick_Release;
			}
			if (passiveButton != null)
			{
				passiveButton.clicked += () => RequestStance(PetStance.Passive);
			}
			if (defensiveButton != null)
			{
				defensiveButton.clicked += () => RequestStance(PetStance.Defensive);
			}
			if (aggressiveButton != null)
			{
				aggressiveButton.clicked += () => RequestStance(PetStance.Aggressive);
			}
			BindSockets(root);
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			Render();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			base.OnAfterShow();
			Render();
		}

		/// <inheritdoc />
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<MercenaryOfferBroadcast>(OnOffer);
			Client.NetworkManager.ClientManager.RegisterBroadcast<MercenaryResultBroadcast>(OnResult);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MercenaryOfferBroadcast>(OnOffer);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MercenaryResultBroadcast>(OnResult);
		}

		/// <inheritdoc />
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
			Unbind();
			if (Character != null)
			{
				if (Character.TryGet(out IMercenaryController found))
				{
					controller = found;
					controller.OnChanged += OnControllerChanged;
				}
				if (Character.TryGet(out IMercenaryGear foundGear))
				{
					gear = foundGear;
					gear.OnSlotUpdated += OnGearSlotUpdated;
				}
			}
			hadMercenary = controller != null && controller.HasMercenary;
			Render();
		}

		/// <inheritdoc />
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();
			Unbind();
		}

		/// <inheritdoc />
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			ReleasePreview();
			hadMercenary = false;
			Render();
			Hide();
		}

		private void Unbind()
		{
			if (controller != null)
			{
				controller.OnChanged -= OnControllerChanged;
				controller = null;
			}
			if (gear != null)
			{
				gear.OnSlotUpdated -= OnGearSlotUpdated;
				gear = null;
			}
		}

		/// <inheritdoc />
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);
			if (!Visible)
			{
				// The preview costs a render every frame it is kept: it goes as soon as nobody can see it.
				ReleasePreview();
			}
		}

		/// <inheritdoc />
		public override void OnDestroying()
		{
			ReleasePreview();
			previewRenderer.Dispose();
			base.OnDestroying();
		}

		/// <inheritdoc />
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}
			RefreshPreview();
			if (Time.unscaledTime >= nextAttributeRefresh)
			{
				nextAttributeRefresh = Time.unscaledTime + AttributeRefreshSeconds;
				RenderAttributes();
			}
			if (releaseArmedUntil > 0f && Time.unscaledTime > releaseArmedUntil)
			{
				releaseArmedUntil = 0f;
				RenderButtons();
			}
		}

		// ── Hiring ────────────────────────────────────────────────────────────

		/// <summary>A Mercenary was spoken to: ask whether to hire them.</summary>
		private void OnOffer(MercenaryOfferBroadcast msg, Channel channel)
		{
			MercenaryKitTemplate kit = MercenaryKitTemplate.Get<MercenaryKitTemplate>(msg.KitID);
			string kitName = kit != null ? kit.Label : "Mercenary";
			string name = string.IsNullOrEmpty(msg.Name) ? "This mercenary" : msg.Name;
			string description = kit != null && !string.IsNullOrWhiteSpace(kit.Description) ? "\n" + kit.Description : string.Empty;
			string text = $"Would you like to hire this mercenary?\n\n{name}, {kitName}.{description}\n\nCost: {msg.Cost:N0}";

			if (controller != null && controller.HasMercenary)
			{
				Toast(MercenaryRules.Describe(MercenaryResult.AlreadyHaveOne, controller.MercenaryName), ToastSeverity.Warning);
				return;
			}
			long interactableID = msg.InteractableID;
			if (UIManager.TryGetTK(DIALOG_NAME, out UITKDialogBox dialog))
			{
				dialog.Open(text, () => Client.Broadcast(new MercenaryHireBroadcast() { InteractableID = interactableID }, Channel.Reliable),
					() => { }, "Hire", "Not now");
			}
		}

		/// <summary>The server's answer to a request, or news.</summary>
		private void OnResult(MercenaryResultBroadcast msg, Channel channel)
		{
			string text = MercenaryRules.Describe(msg.Result, msg.Name);
			if (!string.IsNullOrEmpty(text))
			{
				Toast(text, msg.Result == MercenaryResult.Died ? ToastSeverity.Warning : ToastSeverity.Info);
			}
		}

		// ── Requests ──────────────────────────────────────────────────────────

		private void OnClick_RestOrReturn()
		{
			if (controller == null || !controller.HasMercenary)
			{
				return;
			}
			if (controller.Resting)
			{
				Client.Broadcast(new MercenaryReturnBroadcast(), Channel.Reliable);
			}
			else
			{
				Client.Broadcast(new MercenaryDismissBroadcast(), Channel.Reliable);
			}
		}

		/// <summary>Two presses: the first arms it for a few seconds, the second lets the mercenary go.</summary>
		private void OnClick_Release()
		{
			if (controller == null || !controller.HasMercenary)
			{
				return;
			}
			if (releaseArmedUntil > 0f && Time.unscaledTime <= releaseArmedUntil)
			{
				releaseArmedUntil = 0f;
				Client.Broadcast(new MercenaryReleaseBroadcast(), Channel.Reliable);
			}
			else
			{
				releaseArmedUntil = Time.unscaledTime + ReleaseArmSeconds;
				Toast($"Press Release again to let {controller.MercenaryName} go for good. Their gear stays with you.", ToastSeverity.Warning);
			}
			RenderButtons();
		}

		private void RequestStance(PetStance stance)
		{
			if (controller == null || !controller.HasMercenary || controller.Stance == stance)
			{
				return;
			}
			Client.Broadcast(new MercenaryStanceBroadcast() { Stance = stance }, Channel.Reliable);
		}

		// ── Sockets ───────────────────────────────────────────────────────────

		/// <summary>
		/// Finds the sockets by the sheet's names (<see cref="CharacterSheetView.SlotElementNames"/>, indexed by
		/// <see cref="ItemSlot"/>, which is also the gear container's index) and wires them. A socket a mercenary does not
		/// wear is left out even if the markup had one.
		/// </summary>
		private void BindSockets(VisualElement root)
		{
			sockets.Clear();
			for (int slot = 0; slot < MercenaryRules.GearSlotCount; ++slot)
			{
				VisualElement socket = slot < CharacterSheetView.SlotElementNames.Length && MercenaryRules.Wears((ItemSlot)slot)
					? root.Q(CharacterSheetView.SlotElementNames[slot])
					: null;
				sockets.Add(socket);
				if (socket == null)
				{
					continue;
				}
				int captured = slot;
				// A slot, so a press on it while carrying an item is the drop and not a cancel (UITKControl).
				socket.AddToClassList(SLOT_MARKER_CLASS);
				socket.RegisterCallback<PointerDownEvent>(evt => OnSocketPointerDown(evt, captured));
				socket.RegisterCallback<PointerEnterEvent>(_ => OnSocketPointerEnter(captured, socket));
				socket.RegisterCallback<PointerLeaveEvent>(_ => UITKSlotPainter.HideTooltip(socket));
			}
		}

		/// <summary>
		/// Left: drops what the player is carrying onto the socket, or takes back what is in it. Right (or shift): takes it
		/// back, and puts down anything carried.
		/// </summary>
		private void OnSocketPointerDown(PointerDownEvent evt, int slot)
		{
			if (Character == null || Client == null)
			{
				return;
			}
			UITKDragObject dragObject = UIManager.TryGetTK(UITKDragObject.CONTROL_NAME, out UITKDragObject found) ? found : null;
			bool carrying = dragObject != null && dragObject.IsDragging;
			if (evt.button == 0)
			{
				if (carrying)
				{
					DropOntoSocket(dragObject, slot);
				}
				else
				{
					TakeBack(slot);
				}
			}
			else if (evt.button == 1)
			{
				if (carrying)
				{
					dragObject.Clear();
				}
				TakeBack(slot);
			}
		}

		/// <summary>
		/// Gives the item carried from the bags to the mercenary. Only the bags give: the server moves bag items, and the
		/// item goes in the socket its template names, so a drop on another socket says which one instead of sending.
		/// </summary>
		private void DropOntoSocket(UITKDragObject dragObject, int slot)
		{
			if (dragObject.Type != ReferenceButtonType.Inventory)
			{
				Toast(dragObject.Type == ReferenceButtonType.Equipment
					? "Take it off first, then give it from your bags."
					: dragObject.Type == ReferenceButtonType.Bank
						? "Take it out of the bank first, then give it from your bags."
						: string.Empty, ToastSeverity.Warning);
				dragObject.Clear();
				return;
			}
			// A split half is a quantity the server has not made yet, not an item to give.
			if (dragObject.SplitAmount > 0)
			{
				dragObject.Clear();
				return;
			}

			int bagSlot = (int)dragObject.ReferenceID;
			if (!Character.TryGet(out IInventoryController inventory) || !inventory.IsValidSlot(bagSlot) ||
				!inventory.TryGetItem(bagSlot, out Item item) || !dragObject.MatchesSource(item))
			{
				// The bag slot is not what it was when the drag began.
				dragObject.Clear();
				return;
			}
			dragObject.Clear();

			int fits = MercenaryRules.GearSlotFor(item.Template);
			if (fits < 0)
			{
				Toast(MercenaryRules.Describe(MercenaryResult.DoesNotFit, null), ToastSeverity.Warning);
				return;
			}
			if (fits != slot)
			{
				Toast($"{item.Name} goes in the {ItemSlotNames.DisplayName((ItemSlot)fits)} slot.", ToastSeverity.Warning);
				return;
			}
			if (inventory.IsSlotLocked(bagSlot) || item.ID <= 0)
			{
				Toast(MercenaryRules.Describe(MercenaryResult.Busy, null), ToastSeverity.Warning);
				return;
			}
			if (!CharacterStateValidation.CanAct(Character))
			{
				Toast(MercenaryRules.Describe(MercenaryResult.CannotAct, null), ToastSeverity.Warning);
				return;
			}
			Client.Broadcast(new MercenaryEquipBroadcast() { InventorySlot = bagSlot }, Channel.Reliable);
		}

		/// <summary>Takes the item in a socket back into the bags (the server finds the room).</summary>
		private void TakeBack(int slot)
		{
			if (gear == null || !gear.TryGetItem(slot, out _))
			{
				return;
			}
			Client.Broadcast(new MercenaryUnequipBroadcast() { GearSlot = (byte)slot }, Channel.Reliable);
		}

		private void OnSocketPointerEnter(int slot, VisualElement socket)
		{
			if (gear != null && gear.TryGetItem(slot, out Item item) && item != null)
			{
				UITKSlotPainter.OpenTooltip(socket, item);
			}
			else
			{
				UITKSlotPainter.HideTooltip(socket);
			}
		}

		// ── Rendering ─────────────────────────────────────────────────────────

		private void OnControllerChanged(IMercenaryController changed)
		{
			bool has = changed != null && changed.HasMercenary;
			if (has && !hadMercenary)
			{
				// Just hired: say so and show them.
				Toast($"{changed.MercenaryName} has joined you.", ToastSeverity.Info);
				Show();
			}
			hadMercenary = has;
			Render();
		}

		private void OnGearSlotUpdated(IItemContainer container, Item item, int slot)
		{
			RenderGear();
		}

		private void Render()
		{
			if (Root == null)
			{
				return;
			}
			bool has = controller != null && controller.HasMercenary;
			SetShown(emptyLabel, !has);
			SetShown(summary, has);
			SetShown(attributes, has);
			SetShown(footer, has);

			if (has)
			{
				MercenaryKitTemplate kit = controller.Kit;
				if (nameLabel != null)
				{
					nameLabel.text = controller.MercenaryName;
				}
				if (kitLabel != null)
				{
					kitLabel.text = kit != null
						? (string.IsNullOrWhiteSpace(kit.Description) ? kit.Label : $"{kit.Label}: {kit.Description}")
						: string.Empty;
				}
				if (statusLabel != null)
				{
					statusLabel.text = controller.Resting ? "RESTING" : "WITH YOU";
					statusLabel.EnableInClassList("fish-badge--good", !controller.Resting);
				}
				SetActive(passiveButton, controller.Stance == PetStance.Passive);
				SetActive(defensiveButton, controller.Stance == PetStance.Defensive);
				SetActive(aggressiveButton, controller.Stance == PetStance.Aggressive);
			}
			RenderButtons();
			RenderGear();
			RenderAttributes();
		}

		private void RenderButtons()
		{
			if (controller == null || !controller.HasMercenary)
			{
				return;
			}
			if (restButton != null)
			{
				restButton.text = controller.Resting ? "Return" : "Send to Rest";
				restButton.tooltip = controller.Resting
					? "Bring them back into your party (you must lead it, and it needs a free seat)."
					: "Dismiss them for now. They leave your party, freeing the seat for a player, and come back for free.";
			}
			if (releaseButton != null)
			{
				releaseButton.text = releaseArmedUntil > 0f ? "Confirm Release" : "Release";
			}
		}

		/// <summary>Paints every socket from the gear container, empty ones included, with the sheet's painter.</summary>
		private void RenderGear()
		{
			for (int slot = 0; slot < sockets.Count; ++slot)
			{
				VisualElement socket = sockets[slot];
				if (socket == null)
				{
					continue;
				}
				VisualElement icon = socket.Q(className: "fish-slot__icon");
				Label amount = socket.Q<Label>(className: "fish-slot__amount");
				Item item = gear != null && gear.TryGetItem(slot, out Item held) ? held : null;
				if (item == null)
				{
					UITKSlotPainter.Clear(icon, amount, CSS_SOCKET_HIDDEN);
				}
				else
				{
					UITKSlotPainter.Paint(icon, amount, item, CSS_SOCKET_HIDDEN);
				}
				// Whatever the socket now shows is what its tooltip describes.
				UITKSlotPainter.RefreshTooltip(socket, item);
			}
		}

		// ── Preview ───────────────────────────────────────────────────────────

		/// <summary>
		/// Photographs the mercenary into the preview while it is out and seen here, as the sheet photographs the player.
		/// </summary>
		/// <remarks>
		/// An NPC carries no preview camera (a player's is authored on the character prefab), so one is made here, parented
		/// to the mercenary and turned to face it: <see cref="EquipmentPreviewRenderer.Frame"/> places it in front of the
		/// subject in its parent's space. It is destroyed when the window hides or the mercenary changes, so a pooled
		/// mercenary never carries it into another life.
		/// </remarks>
		private void RefreshPreview()
		{
			Mercenary mercenary = controller != null && controller.HasMercenary && !controller.Resting ? controller.Live : null;
			Transform subject = mercenary != null ? mercenary.MeshRoot : null;
			if (subject == null || previewRt == null)
			{
				ReleasePreview();
				SetPreviewLabel(controller == null || !controller.HasMercenary ? string.Empty
					: controller.Resting ? "RESTING" : "OUT OF SIGHT");
				return;
			}

			if (!ReferenceEquals(previewSubject, mercenary) || previewCamera == null)
			{
				ReleasePreview();
				previewSubject = mercenary;
				GameObject cameraObject = new GameObject("MercenaryPreviewCamera");
				cameraObject.transform.SetParent(mercenary.Transform, false);
				cameraObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
				previewCamera = cameraObject.AddComponent<Camera>();
				previewRenderer.Configure(previewCamera);
			}
			SetPreviewLabel(string.Empty);

			if (!TryMeasure(previewRt, out int width, out int height) || !previewRenderer.Render(width, height, subject))
			{
				return;
			}
			// Rendered before framed: the frame needs the texture's aspect, and the first render makes the texture.
			if (!previewFramed)
			{
				previewFramed = previewRenderer.Frame(subject);
			}
			previewRt.style.backgroundImage = previewRenderer.Texture != null
				? new StyleBackground(Background.FromRenderTexture(previewRenderer.Texture))
				: StyleKeyword.None;
		}

		/// <summary>Stops drawing the preview and destroys its camera.</summary>
		private void ReleasePreview()
		{
			if (previewRt != null)
			{
				previewRt.style.backgroundImage = StyleKeyword.None;
			}
			previewRenderer.Configure(null);
			if (previewCamera != null)
			{
				Destroy(previewCamera.gameObject);
			}
			previewCamera = null;
			previewSubject = null;
			previewFramed = false;
		}

		private void SetPreviewLabel(string text)
		{
			if (previewLabel != null && previewLabel.text != text)
			{
				previewLabel.text = text;
			}
		}

		/// <summary>The element's laid-out size in device pixels; false before its first layout.</summary>
		private static bool TryMeasure(VisualElement element, out int width, out int height)
		{
			width = 0;
			height = 0;
			float w = element.resolvedStyle.width;
			float h = element.resolvedStyle.height;
			if (float.IsNaN(w) || float.IsNaN(h) || w < 2f || h < 2f)
			{
				return false;
			}
			float pixelsPerPoint = element.panel != null ? element.scaledPixelsPerPoint : 1f;
			if (float.IsNaN(pixelsPerPoint) || pixelsPerPoint <= 0f)
			{
				pixelsPerPoint = 1f;
			}
			width = Mathf.RoundToInt(w * pixelsPerPoint);
			height = Mathf.RoundToInt(h * pixelsPerPoint);
			return true;
		}

		/// <summary>The mercenary's resources and every attribute with a value, read off it while it is out and in view.</summary>
		private void RenderAttributes()
		{
			if (attributeList == null)
			{
				return;
			}
			attributeList.Clear();
			Mercenary mercenary = controller != null ? controller.Live : null;
			string hint = string.Empty;
			if (controller == null || !controller.HasMercenary)
			{
				hint = string.Empty;
			}
			else if (controller.Resting)
			{
				hint = "Resting. Return them to see their attributes.";
			}
			else if (mercenary == null || !mercenary.TryGet(out ICharacterAttributeController attributes))
			{
				hint = "Out of sight.";
			}
			else
			{
				foreach (KeyValuePair<int, CharacterResourceAttribute> pair in attributes.ResourceAttributes)
				{
					CharacterResourceAttribute resource = pair.Value;
					if (resource?.Template != null)
					{
						AddAttributeRow(resource.Template.Name, $"{(int)resource.CurrentValue:N0} / {resource.FinalValue:N0}");
					}
				}
				var rows = new List<CharacterAttribute>();
				foreach (KeyValuePair<int, CharacterAttribute> pair in attributes.Attributes)
				{
					if (pair.Value?.Template != null && pair.Value.FinalValue != 0)
					{
						rows.Add(pair.Value);
					}
				}
				rows.Sort((a, b) => string.CompareOrdinal(a.Template.Name, b.Template.Name));
				for (int i = 0; i < rows.Count; ++i)
				{
					AddAttributeRow(rows[i].Template.Name, rows[i].FinalValue.ToString("N0"));
				}
			}
			if (attributeHint != null)
			{
				attributeHint.text = hint;
			}
		}

		private void AddAttributeRow(string name, string value)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList("fish-attr-row");
			row.AddToClassList("merc-attr-row");
			Label nameLabel = new Label(name);
			nameLabel.AddToClassList("fish-attr-row__name");
			Label valueLabel = new Label(value);
			valueLabel.AddToClassList("fish-attr-row__value");
			row.Add(nameLabel);
			row.Add(valueLabel);
			attributeList.Add(row);
		}

		private static void SetShown(VisualElement element, bool shown)
		{
			element?.EnableInClassList(CSS_HIDDEN, !shown);
		}

		private static void SetActive(Button button, bool active)
		{
			button?.EnableInClassList(CSS_TAB_ACTIVE, active);
		}

		private static void Toast(string text, ToastSeverity severity)
		{
			if (!string.IsNullOrEmpty(text) && UIManager.TryGetTK(TOAST_NAME, out UITKToast toast))
			{
				toast.Show(text, severity);
			}
		}
	}
}
