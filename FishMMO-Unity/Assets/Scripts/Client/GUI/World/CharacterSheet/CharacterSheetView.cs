using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Binds <c>UICharacterSheet.uxml</c> to one subject: the ten sockets, the character preview and
	/// the HP / MP / Stamina chips.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the half of the character sheet that is the same whoever is looking at it. The player's
	/// own equipment panel and the inspect window mount the same markup and show the same three
	/// regions; what differs is only what they are allowed to reveal, which is
	/// <see cref="CharacterSheetOptions"/>, and what they do with a click, which is not this class's
	/// business at all.
	/// </para>
	/// <para>
	/// <b>Deliberately not a <see cref="UITKSlotPanelBase"/>.</b> That class is not a set of drawing
	/// helpers, it is an operation tracker: its <c>OwnContainer</c>, <c>IsSlotBlocked</c> and
	/// <c>ApplySlotLockVisual</c> all bottom out in <c>Character</c> — which <see cref="UIManager"/>
	/// fills with the LOCAL player for every <see cref="UITKCharacterControl"/> it enrols, with no way
	/// to opt out. An inspect window holding a subject who is somebody else, deriving from that base,
	/// would silently inherit the local player's containers and in-flight equip state. So the sharing
	/// here is composition, the way <see cref="UITKItemIcon"/> and
	/// <see cref="EquipmentPreviewRenderer"/> are shared, and the slot PAINTING is
	/// <see cref="UITKSlotPainter"/>'s.
	/// </para>
	/// <para>
	/// <b>It resolves the sockets and hands them out.</b> <see cref="SlotElementNames"/> is the
	/// contract with <see cref="ItemSlot"/> — one authored element per enum value, in enum order — and
	/// it lives here, once, because both panels need it. Each panel still does its own marking and
	/// wiring on the elements it takes: the equipment panel registers the interactive handlers, the
	/// inspect window registers none. That is the rule <see cref="UITKSlotPanelBase"/> already states
	/// about slot creation, kept intact.
	/// </para>
	/// </remarks>
	public sealed class CharacterSheetView
	{
		// ── The socket contract ───────────────────────────────────────────────

		/// <summary>
		/// UXML element name for every <see cref="ItemSlot"/>, indexed by its enum value.
		/// </summary>
		/// <remarks>
		/// THIS ARRAY IS A CONTRACT, NOT A CONVENIENCE. <see cref="EquipmentController"/> sizes its
		/// container from <c>Enum.GetNames(typeof(ItemSlot)).Length</c>, so slot <c>i</c> of the
		/// container is <c>(ItemSlot)i</c> and must be drawn by <c>SlotElementNames[i]</c>. Every
		/// enum value needs an entry, in order, and every entry needs an element of that name in
		/// <c>UICharacterSheet.uxml</c>.
		/// <para>
		/// The comment that used to sit on a copy of this array claimed it listed "only slots that
		/// exist in both the enum and the UXML", which was untrue in both directions: it named
		/// <c>slot-accessory</c>, which the UXML did not define, while the UXML defined
		/// <c>slot-neck</c> and <c>slot-ring</c>, which are not <see cref="ItemSlot"/> values and
		/// which this array did not name. The result was an <see cref="ItemSlot.Accessory"/> slot
		/// that could never render — <c>Q</c> returned null and the per-slot guard quietly skipped
		/// it — and two dead elements the player could click to no effect. Both halves are fixed;
		/// the markup now declares exactly these ten names, and there is one array rather than one
		/// per panel.
		/// </para>
		/// </remarks>
		public static readonly string[] SlotElementNames = new[]
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

		// ── UXML element names ────────────────────────────────────────────────

		/// <summary>Name of the attribute list ScrollView element in the UXML.</summary>
		private const string ATTR_LIST_NAME  = "attribute-list";
		/// <summary>Name of the preview render texture element in the UXML.</summary>
		private const string PREVIEW_RT_NAME = "preview-rt";
		/// <summary>Name of the HP stat label element in the UXML.</summary>
		private const string STAT_HP_NAME    = "stat-hp";
		/// <summary>Name of the MP stat label element in the UXML.</summary>
		private const string STAT_MP_NAME    = "stat-mp";
		/// <summary>Name of the stamina stat label element in the UXML.</summary>
		private const string STAT_STAM_NAME  = "stat-stam";
		/// <summary>Name of the footer that holds the attribute list.</summary>
		private const string FOOTER_NAME     = "panel-footer";
		/// <summary>Name of the status bar that holds the resource chips.</summary>
		private const string STATUS_BAR_NAME = "status-bar";
		/// <summary>Name of the viewport that holds the preview.</summary>
		private const string PREVIEW_VIEWPORT_NAME = "preview-viewport";
		/// <summary>USS class naming the window itself, as opposed to the document's container.</summary>
		private const string CSS_PANEL = "fish-panel";

		/// <summary>Name of the label showing the subject's name.</summary>
		public const string TITLE_NAME = "header-title";

		// ── USS class names ───────────────────────────────────────────────────

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
		/// <summary>USS class hiding a socket's stack badge.</summary>
		/// <remarks>
		/// The markup's own class, not a panel's <c>Prefix + "-hidden"</c>: this is the sheet's tree
		/// whoever mounted it, and an inspect window has no container prefix of its own to derive one
		/// from. The <c>eq-</c> prefix predates the sheet being shared — read it as "the sheet", as
		/// the markup's own header comment says.
		/// </remarks>
		private const string CSS_HIDDEN = "eq-hidden";

		// ── State ─────────────────────────────────────────────────────────────

		/// <summary>The visual tree this view was resolved against.</summary>
		private readonly VisualElement root;

		/// <summary>What this viewer is allowed to see.</summary>
		private readonly CharacterSheetOptions options;

		/// <summary>The ten sockets, indexed by <see cref="ItemSlot"/>.</summary>
		/// <remarks>
		/// A socket whose element is missing is still ADDED, as a null. Skipping it would shorten the
		/// list and every socket after it would then draw the wrong item — the index into this list is
		/// the container slot index, and that is the contract <see cref="SlotElementNames"/> exists to
		/// keep. <see cref="UITKSlotPainter"/> already treats a null element as nothing to draw.
		/// </remarks>
		private readonly List<VisualElement> slotRoots = new List<VisualElement>();

		/// <summary>The ScrollView that contains the attribute rows.</summary>
		private ScrollView attributeList;
		/// <summary>Character preview render texture element.</summary>
		private VisualElement previewRt;
		/// <summary>Label displaying the HP stat value.</summary>
		private Label statHpLabel;
		/// <summary>Label displaying the MP stat value.</summary>
		private Label statMpLabel;
		/// <summary>Label displaying the stamina stat value.</summary>
		private Label statStamLabel;

		/// <summary>The character this sheet is currently showing, or null.</summary>
		private IPlayerCharacter subject;

		/// <summary>True once the read-only socket tooltip has been wired.</summary>
		private bool socketHoverWired;

		/// <summary>Live attribute value labels keyed by attribute template ID.</summary>
		private readonly Dictionary<int, Label> attributeValueLabels = new Dictionary<int, Label>();

		/// <summary>Category header elements created at runtime.</summary>
		private readonly List<VisualElement> attributeCategoryElements = new List<VisualElement>();

		/// <summary>Attribute row elements created at runtime.</summary>
		private readonly List<VisualElement> attributeRowElements = new List<VisualElement>();

		/// <summary>
		/// The attributes this sheet currently holds a subscription on.
		/// </summary>
		/// <remarks>
		/// Kept as its own list rather than re-derived from the character at unsubscribe time.
		/// <c>DestroyAttributeElements</c> used to walk <c>Character</c>'s attributes to detach — but
		/// on a character change it runs from <c>OnPostSetCharacter</c>, by which point <c>Character</c>
		/// is already the NEW one, so the outgoing character kept every subscription for the rest of
		/// the session and its updates went on repainting a panel that no longer showed it.
		/// </remarks>
		private readonly List<CharacterAttribute> subscribedAttributes = new List<CharacterAttribute>();

		/// <summary>
		/// The resource attributes the status bar follows when the attribute list is NOT built.
		/// </summary>
		/// <remarks>
		/// The chips used to be kept live only as a side effect of the attribute rows' subscription,
		/// so a sheet that showed the bar without the rows — an inspected character — wrote the vitals
		/// once at bind time and froze them there, while the observed values underneath went on
		/// changing. Tracked separately from <see cref="subscribedAttributes"/> so the two cannot be
		/// confused, and released by the same call.
		/// </remarks>
		private readonly List<CharacterAttribute> subscribedVitals = new List<CharacterAttribute>();

		/// <summary>Owns the preview camera and the render texture the viewport draws.</summary>
		private readonly EquipmentPreviewRenderer previewRenderer = new EquipmentPreviewRenderer();

		/// <summary>
		/// True once the preview camera has been framed against the character actually on screen.
		/// </summary>
		/// <remarks>
		/// Cleared whenever the thing being photographed changes — a different character, or a
		/// different set of equipped meshes — and set again the first time the renderer can see one.
		/// Frame is otherwise a per-open cost, not a per-frame one: re-deriving the camera every frame
		/// would make the picture breathe as the character animates.
		/// </remarks>
		private bool previewFramed;

		/// <summary>The render texture currently on the preview element, or null.</summary>
		/// <remarks>
		/// Held so the style is only rewritten when the texture is actually replaced. The preview is
		/// refreshed every frame, and assigning a fresh <see cref="StyleBackground"/> each time would
		/// rebuild the style object, invalidate the element and allocate — for a value that only
		/// changes when the viewport is resized.
		/// </remarks>
		private RenderTexture previewTexture;

		/// <summary>
		/// Resolves the sheet's elements and hides the regions this viewer may not see.
		/// </summary>
		/// <param name="root">The mounted sheet's root element.</param>
		/// <param name="options">What this viewer is allowed to see.</param>
		public CharacterSheetView(VisualElement root, CharacterSheetOptions options)
		{
			this.root = root;
			this.options = options;

			ResolveSockets();
			ResolveRegions();
		}

		/// <summary>The ten sockets, indexed by <see cref="ItemSlot"/>, null where the markup lacks one.</summary>
		public IReadOnlyList<VisualElement> SlotRoots => slotRoots;

		/// <summary>Whether this viewer may see the attribute list.</summary>
		public bool ShowAttributes => options.ShowAttributes;

		/// <summary>The subject this sheet is showing, or null.</summary>
		public IPlayerCharacter Subject => subject;

		/// <summary>
		/// The label a panel may set to name the subject.
		/// </summary>
		/// <remarks>
		/// Exposed rather than set here, because the two windows disagree about it: the player's own
		/// sheet always says EQUIPMENT, and an inspect window names the character it is showing.
		/// </remarks>
		public Label TitleLabel { get; private set; }

		/// <summary>
		/// The window itself — the authored <c>panel-root</c>, not the document's container.
		/// </summary>
		/// <remarks>
		/// A <c>UIDocument</c> hands out a container element that the UXML tree is cloned INTO, so the
		/// tree's own root is a CHILD of the element a panel is tempted to treat as "the root". A class
		/// added to the container is therefore matched by nothing in the stylesheet, and its position
		/// is not the position of anything on screen.
		/// <para>
		/// Everything that places this window belongs on this element, and so does anything that
		/// considers where the window is: <see cref="UITKControl"/> drags the nearest <c>fish-panel</c>
		/// ancestor of the header, which is exactly this one. A placement override written to the
		/// container would not move the box the player drags, and the first drag would then have to
		/// fight it.
		/// </para>
		/// </remarks>
		public VisualElement PanelRoot { get; private set; }

		// ── Setup ─────────────────────────────────────────────────────────────

		/// <summary>
		/// Resolves the authored sockets by name.
		/// </summary>
		private void ResolveSockets()
		{
			slotRoots.Clear();

			for (int i = 0; i < SlotElementNames.Length; ++i)
			{
				slotRoots.Add(root != null ? root.Q(SlotElementNames[i]) : null);
			}
		}

		/// <summary>
		/// Resolves the sheet's named elements and honours the visibility options.
		/// </summary>
		/// <remarks>
		/// The regions are hidden by <c>display</c> rather than removed, so the tree keeps the shape
		/// the stylesheet lays out and nothing downstream has to cope with a missing element. A
		/// region the viewer may not see is hidden ONCE, here, rather than being repainted with
		/// nothing in it on every refresh.
		/// </remarks>
		private void ResolveRegions()
		{
			if (root == null)
			{
				return;
			}

			/* The window, found by class rather than by name, because the name is a property of this
			 * markup and the class is what the stylesheet and the drag both key off. Falling back to
			 * the root keeps a markup without the class — a test harness mounting a bare tree, say —
			 * from resolving a null window and losing its placement silently. */
			PanelRoot = root.Q(className: CSS_PANEL) ?? root;

			TitleLabel = root.Q<Label>(TITLE_NAME);

			VisualElement footer = root.Q(FOOTER_NAME);
			VisualElement statusBar = root.Q(STATUS_BAR_NAME);
			VisualElement viewport = root.Q(PREVIEW_VIEWPORT_NAME);

			if (!options.ShowAttributes)
			{
				if (footer != null) footer.style.display = DisplayStyle.None;
			}
			else
			{
				attributeList = root.Q<ScrollView>(ATTR_LIST_NAME);
			}

			if (!options.ShowStatusBar)
			{
				if (statusBar != null) statusBar.style.display = DisplayStyle.None;
			}
			else
			{
				statHpLabel   = root.Q<Label>(STAT_HP_NAME);
				statMpLabel   = root.Q<Label>(STAT_MP_NAME);
				statStamLabel = root.Q<Label>(STAT_STAM_NAME);
			}

			if (!options.ShowPreview)
			{
				if (viewport != null) viewport.style.display = DisplayStyle.None;
			}
			else
			{
				previewRt = root.Q(PREVIEW_RT_NAME);
			}
		}

		// ── Subject ───────────────────────────────────────────────────────────

		/// <summary>
		/// Points the sheet at a character, replacing whatever it was showing.
		/// </summary>
		/// <param name="subject">The character to show, or null to show nothing.</param>
		/// <remarks>
		/// The outgoing character's subscriptions and preview camera are released BEFORE the reference
		/// moves. Doing it the other way round is the bug this class inherits from the panel it came
		/// from: detaching afterwards looked up the attributes of the INCOMING character and left the
		/// outgoing one wired to this sheet forever, while its camera stayed enabled and rendered into
		/// a texture nothing was drawing.
		/// </remarks>
		public void SetSubject(IPlayerCharacter subject)
		{
			ReleasePreview();
			UnsubscribeAttributes();
			DestroyAttributeElements();
			ResetStatusBar();

			this.subject = subject;

			if (subject == null)
			{
				ClearSockets();
				return;
			}

			if (subject.TryGet(out ICharacterAttributeController attributeController))
			{
				// Built only when this viewer may see them; the chips are their own region.
				if (options.ShowAttributes)
				{
					BuildAttributeRows(attributeController);
				}

				if (options.ShowStatusBar)
				{
					UpdateStatusBar(attributeController);

					/* When the rows are built they keep the chips live through OnAttributeUpdated. When
					 * they are not, the chips need their own subscription or they freeze at bind time. */
					if (!options.ShowAttributes)
					{
						SubscribeVitals(attributeController);
					}
				}
			}

			/* Painted as part of binding, so a caller cannot bind a subject and forget to show their
			 * gear — the window would come up correctly titled and entirely empty. */
			PaintSockets();
		}

		/// <summary>
		/// Re-reads everything the sheet shows from its subject, without rebuilding the tree.
		/// </summary>
		/// <remarks>
		/// The preview is rebuilt on every open, not just the first: the camera and texture belong to
		/// the character and are handed back when the sheet closes, so the opening after that starts
		/// from nothing.
		/// </remarks>
		public void Refresh()
		{
			if (subject != null &&
				options.ShowStatusBar &&
				subject.TryGet(out ICharacterAttributeController attributeController))
			{
				UpdateStatusBar(attributeController);
			}

			previewFramed = false;
			RefreshPreview();
		}

		/// <summary>
		/// Marks the preview framing as stale.
		/// </summary>
		/// <remarks>
		/// Equipment changes the silhouette: a two-handed weapon reaches further than a dagger, and a
		/// frame sized before it existed crops it. Re-framing on the next frame rather than this one is
		/// deliberate — the mesh is attached by the visual controller in the same call chain, and
		/// measuring before it lands would measure the old silhouette again.
		/// </remarks>
		public void InvalidatePreviewFraming()
		{
			if (options.ShowPreview)
			{
				previewFramed = false;
			}
		}

		// ── Sockets ───────────────────────────────────────────────────────────

		/// <summary>
		/// Paints the ten sockets read-only from this sheet's subject.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Every socket is painted, empty ones included, so a sheet pointed at a second character
		/// cannot leave the first one's gear on screen. A sheet with no subject paints every socket
		/// empty for the same reason.
		/// </para>
		/// <para>
		/// The painting itself is <see cref="UITKSlotPainter"/>'s, and it is the same call the player's
		/// own sheet makes. What this method deliberately never does is register a handler on what it
		/// paints: the equipment panel's sockets get their press, drag and drop behaviour from
		/// <see cref="UITKSlotPanelBase"/>, and a second set registered here would double it.
		/// </para>
		/// </remarks>
		public void PaintSockets()
		{
			for (int i = 0; i < slotRoots.Count; ++i)
			{
				VisualElement slotRoot = slotRoots[i];
				if (slotRoot == null)
				{
					continue;
				}

				VisualElement icon = slotRoot.Q(className: "fish-slot__icon");
				Label amount = slotRoot.Q<Label>(className: "fish-slot__amount");
				Item item = SocketItem(i);

				if (item == null)
				{
					UITKSlotPainter.Clear(icon, amount, CSS_HIDDEN);
				}
				else
				{
					UITKSlotPainter.Paint(icon, amount, item, CSS_HIDDEN);
				}

				// The same rule as the painting: whatever the socket now shows is what it describes.
				UITKSlotPainter.RefreshTooltip(slotRoot, item);
			}
		}

		/// <summary>
		/// Paints every socket empty.
		/// </summary>
		public void ClearSockets()
		{
			for (int i = 0; i < slotRoots.Count; ++i)
			{
				VisualElement slotRoot = slotRoots[i];
				if (slotRoot == null)
				{
					continue;
				}

				VisualElement icon = slotRoot.Q(className: "fish-slot__icon");
				Label amount = slotRoot.Q<Label>(className: "fish-slot__amount");

				UITKSlotPainter.Clear(icon, amount, CSS_HIDDEN);
				UITKSlotPainter.RefreshTooltip(slotRoot, null);
			}
		}

		/// <summary>
		/// Wires the read-only item tooltip onto the sockets.
		/// </summary>
		/// <remarks>
		/// Opt-in, and it has to be. The player's own sheet gets this behaviour from
		/// <see cref="UITKSlotPanelBase.OnSlotPointerEnter"/>, which looks the item up in the
		/// container that panel is bound to; a viewer that may not act on a socket has no such
		/// container and asks the sheet for the behaviour instead. Wiring both would open the same
		/// tooltip twice.
		/// <para>
		/// A tooltip is not an interaction. Inspecting a character is how a player judges their gear,
		/// and an item's name and stats live in its tooltip — reading one changes nothing about the
		/// character being read.
		/// </para>
		/// </remarks>
		public void EnableSocketHover()
		{
			if (socketHoverWired)
			{
				return;
			}
			socketHoverWired = true;

			for (int i = 0; i < slotRoots.Count; ++i)
			{
				VisualElement slotRoot = slotRoots[i];
				if (slotRoot == null)
				{
					continue;
				}

				int slotIndex = i;
				/* Registered on elements belonging to the tree resolved right now, so a rebuilt tree
				 * brings new elements and the old handlers go with the old ones — nothing to
				 * unregister, and no per-rebuild accumulation. */
				slotRoot.RegisterCallback<PointerEnterEvent>(_ => OnSocketPointerEnter(slotIndex, slotRoot));
				slotRoot.RegisterCallback<PointerLeaveEvent>(_ => UITKSlotPainter.HideTooltip(slotRoot));
			}
		}

		/// <summary>
		/// Shows the item tooltip for the socket the pointer entered.
		/// </summary>
		/// <param name="slotIndex">The socket the pointer entered.</param>
		/// <param name="owner">The socket element.</param>
		private void OnSocketPointerEnter(int slotIndex, VisualElement owner)
		{
			Item item = SocketItem(slotIndex);
			if (item == null)
			{
				/* A socket that emptied under the pointer must not go on describing what it held. */
				UITKSlotPainter.HideTooltip(owner);
				return;
			}

			UITKSlotPainter.OpenTooltip(owner, item);
		}

		/// <summary>
		/// What this sheet's subject has in a socket.
		/// </summary>
		/// <param name="slotIndex">The socket index, which is an <see cref="ItemSlot"/> value.</param>
		/// <returns>The item, or null when there is none or there is no subject.</returns>
		/// <remarks>
		/// Read from the subject's own replicated container rather than from a container this sheet
		/// resolved. All character data is already synchronised to observers, so an inspected
		/// character's gear is in memory here and needs no server round trip.
		/// </remarks>
		private Item SocketItem(int slotIndex)
		{
			if (slotIndex < 0 || subject == null ||
				!subject.TryGet(out IEquipmentController equipmentController) ||
				equipmentController.Items == null || slotIndex >= equipmentController.Items.Count)
			{
				return null;
			}

			return equipmentController.Items[slotIndex];
		}

		// ── Character preview ─────────────────────────────────────────────────

		/// <summary>
		/// Adopts the subject's preview camera and renders it into the viewport.
		/// </summary>
		/// <remarks>
		/// <para>Safe to call every frame: it returns immediately until the UI layout engine has
		/// measured the viewport, and once the preview is framed it is one small orthographic render
		/// into a texture already sized for the element.</para>
		/// <para>The camera is taken from the character rather than injected by whoever opened the
		/// sheet. It used to be handed over by the equipment hotkey, which meant the preview depended
		/// on how the panel was opened and not on what it was showing.</para>
		/// <para>
		/// For an inspected character this photographs their race model and nothing else: no equipment
		/// geometry is ever built for a character you do not own. See the panel's remarks.
		/// </para>
		/// </remarks>
		public void RefreshPreview()
		{
			if (!options.ShowPreview || root == null)
			{
				return;
			}

			Camera camera = (previewRt != null && subject != null) ? subject.EquipmentViewCamera : null;
			if (camera == null)
			{
				ApplyPreviewTexture(null);
				previewFramed = false;
				return;
			}

			previewRenderer.Configure(camera);

			if (!TryMeasureViewport(previewRt, out int width, out int height))
			{
				return;
			}

			if (!previewRenderer.Render(width, height))
			{
				return;
			}

			/* Render before Frame, deliberately: Frame needs the texture's aspect to know whether the
			 * subject is limited by its height or its width, and the texture is only created once a
			 * size is known. The first frame of an opening therefore draws with the prefab's authored
			 * framing and the second with the corrected one. */
			if (!previewFramed)
			{
				previewFramed = previewRenderer.Frame(subject.MeshRoot);
			}

			ApplyPreviewTexture(previewRenderer.Texture);
		}

		/// <summary>
		/// Displays <paramref name="rt"/> inside the preview element.
		/// </summary>
		/// <param name="rt">The render texture to show, or null to show nothing.</param>
		/// <remarks>
		/// Toolkit cannot sample a RenderTexture through a texture slot, so it goes on as a background
		/// image. That is also why the viewport element is a plain <see cref="VisualElement"/> and not
		/// an <c>Image</c>: nothing here needs a sprite.
		/// </remarks>
		private void ApplyPreviewTexture(RenderTexture rt)
		{
			/* A raw reference compare, not Unity's overloaded == : this is about the texture having been
			 * replaced, and the renderer nulls its own field on release rather than leaving a destroyed
			 * object behind. */
			if (object.ReferenceEquals(previewTexture, rt))
			{
				return;
			}

			previewTexture = rt;

			if (previewRt == null)
			{
				return;
			}

			previewRt.style.backgroundImage = rt != null
				? new StyleBackground(Background.FromRenderTexture(rt))
				: StyleKeyword.None;
		}

		/// <summary>
		/// Reads the viewport's measured size, in whole pixels.
		/// </summary>
		/// <param name="element">The element to measure.</param>
		/// <param name="width">Receives the width in pixels.</param>
		/// <param name="height">Receives the height in pixels.</param>
		/// <returns>True when the layout engine has produced a usable size.</returns>
		/// <remarks>
		/// <c>resolvedStyle</c> reports NaN for a property that has never been resolved, which is the
		/// state of every element between being added to a panel and its first layout pass — so the
		/// NaN test is the real guard here and the size test only rejects a degenerate box.
		/// </remarks>
		private static bool TryMeasureViewport(VisualElement element, out int width, out int height)
		{
			width = 0;
			height = 0;

			float measuredWidth = element.resolvedStyle.width;
			float measuredHeight = element.resolvedStyle.height;

			if (float.IsNaN(measuredWidth) || float.IsNaN(measuredHeight) ||
				measuredWidth < 2.0f || measuredHeight < 2.0f)
			{
				return false;
			}

			width = Mathf.RoundToInt(measuredWidth);
			height = Mathf.RoundToInt(measuredHeight);
			return true;
		}

		// ── Teardown ──────────────────────────────────────────────────────────

		/// <summary>
		/// Hands the preview camera and texture back.
		/// </summary>
		/// <remarks>
		/// Called when the sheet goes off screen, when its subject changes and when it is destroyed.
		/// The camera is handed back rather than simply left enabled because the preview's cost is paid
		/// every frame whether or not anyone is looking at it.
		/// </remarks>
		public void ReleasePreview()
		{
			/* The element stops drawing the texture before the renderer destroys it, so no frame can
			 * find a destroyed texture still on the element's style. */
			ApplyPreviewTexture(null);
			previewRenderer.Configure(null);
			previewFramed = false;
		}

		/// <summary>
		/// Releases everything this sheet owns for the rest of the session.
		/// </summary>
		/// <remarks>
		/// Disposes rather than dropping the reference: the camera belongs to the character and would
		/// otherwise stay enabled — rendering the character into a texture nobody draws, every frame,
		/// for the rest of the session.
		/// </remarks>
		public void Dispose()
		{
			ApplyPreviewTexture(null);
			previewRenderer.Dispose();
			previewFramed = false;

			DestroyAttributeElements();
			subject = null;
		}

		// ── Attribute update callbacks ────────────────────────────────────────

		/// <summary>
		/// Refreshes the attribute label for an updated attribute.
		/// </summary>
		private void OnAttributeUpdated(CharacterAttribute attribute)
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
				subscribedAttributes.Add(attribute);
			}
		}

		/// <summary>
		/// Detaches this sheet from every attribute it is subscribed to.
		/// </summary>
		private void UnsubscribeAttributes()
		{
			for (int i = 0; i < subscribedAttributes.Count; ++i)
			{
				if (subscribedAttributes[i] != null)
				{
					subscribedAttributes[i].OnAttributeUpdated -= OnAttributeUpdated;
				}
			}
			subscribedAttributes.Clear();

			for (int i = 0; i < subscribedVitals.Count; ++i)
			{
				if (subscribedVitals[i] != null)
				{
					subscribedVitals[i].OnAttributeUpdated -= OnVitalUpdated;
				}
			}
			subscribedVitals.Clear();
		}

		/// <summary>
		/// Follows the subject's health, mana and stamina so the status bar stays live without the
		/// attribute rows.
		/// </summary>
		/// <param name="ac">The subject's attribute controller.</param>
		private void SubscribeVitals(ICharacterAttributeController ac)
		{
			if (ac.TryGetHealthAttribute(out CharacterResourceAttribute hp)) SubscribeVital(hp);
			if (ac.TryGetManaAttribute(out CharacterResourceAttribute mp)) SubscribeVital(mp);
			if (ac.TryGetStaminaAttribute(out CharacterResourceAttribute stam)) SubscribeVital(stam);
		}

		private void SubscribeVital(CharacterAttribute attribute)
		{
			if (attribute == null)
			{
				return;
			}
			attribute.OnAttributeUpdated -= OnVitalUpdated; // defensive dedup
			attribute.OnAttributeUpdated += OnVitalUpdated;
			subscribedVitals.Add(attribute);
		}

		/// <summary>
		/// Rewrites one status-bar chip from a resource attribute that changed.
		/// </summary>
		private void OnVitalUpdated(CharacterAttribute attribute)
		{
			if (attribute?.Template == null || !(attribute is CharacterResourceAttribute resource))
			{
				return;
			}
			UpdateStatusBarChip(attribute.Template.Name, Mathf.RoundToInt(resource.CurrentValue) + " / " + resource.FinalValue);
		}

		/// <summary>
		/// Removes all runtime-created attribute elements and unsubscribes from all attribute events.
		/// </summary>
		private void DestroyAttributeElements()
		{
			UnsubscribeAttributes();

			/* RemoveFromHierarchy, not attributeList.Remove. VisualElement.Remove THROWS when the
			 * element is not its child, and after the document re-clones the UXML these rows belong to
			 * the previous tree while attributeList is the new one — so the old code threw part-way
			 * through, aborting the rebuild and leaving the panel permanently empty. RemoveFromHierarchy
			 * asks the element about its own parent and is a no-op when it has none. */
			for (int i = 0; i < attributeCategoryElements.Count; ++i)
			{
				attributeCategoryElements[i]?.RemoveFromHierarchy();
			}
			for (int i = 0; i < attributeRowElements.Count; ++i)
			{
				attributeRowElements[i]?.RemoveFromHierarchy();
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
		/// <remarks>
		/// "—", which is what the markup authors. A sheet that has just been pointed at nobody must not
		/// keep the previous character's vitals on screen, and for an inspect window that is the
		/// ordinary case: the window outlives the character it was showing.
		/// </remarks>
		private void ResetStatusBar()
		{
			if (statHpLabel   != null) statHpLabel.text   = "—";
			if (statMpLabel   != null) statMpLabel.text   = "—";
			if (statStamLabel != null) statStamLabel.text = "—";
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
