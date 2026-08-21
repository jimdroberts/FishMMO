using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract base class for UI Toolkit-backed controls. Drives a <see cref="UIDocument"/> and
	/// owns the show/hide, focus, drag and Escape-close behaviour every panel shares.
	/// Automatically registers and unregisters with <see cref="UIManager"/> on Awake/OnDestroy.
	/// </summary>
	public abstract class UITKControl : MonoBehaviour
	{
		/// <summary>
		/// The UIDocument component that owns the visual tree for this control.
		/// Assign in the Inspector.
		/// </summary>
		[Tooltip("UIDocument component backing this control.")]
		public UIDocument Document;

		/// <summary>
		/// If true, the panel is visible when the scene starts.
		/// </summary>
		public bool StartOpen = true;

		/// <summary>
		/// If true, Hide() calls are ignored — the panel cannot be closed.
		/// </summary>
		public bool IsAlwaysOpen = false;

		/// <summary>
		/// If true, closes this panel when quitting to the login menu.
		/// </summary>
		public bool CloseOnQuitToMenu = true;

		/// <summary>
		/// If true, showing this panel releases the mouse cursor so it can be clicked.
		/// </summary>
		/// <remarks>
		/// A window the player interacts with needs the cursor, and a HUD element that is
		/// permanently on screen must not take it away. Defaulting this to false keeps bars and
		/// hotkey strips from stealing the cursor the moment they appear.
		/// <para>
		/// Only ever set true here, never false — releasing the cursor is a panel's business but
		/// recapturing it is not, since another panel may still be open.
		/// <see cref="PlayerInputController"/> and <see cref="Client"/> own the reset.
		/// </para>
		/// </remarks>
		[Tooltip("Show() releases the mouse cursor. Enable for windows and dialogs, not for HUD elements.")]
		public bool ReleasesCursor = false;

		/// <summary>
		/// Whether Escape closes this panel.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="ReleasesCursor"/>. The two were briefly
		/// merged because <c>PlayerInputController</c> used "is anything Escape-closable"
		/// as its test for whether to keep the mouse cursor free — so a panel that released the
		/// cursor without registering for Escape had it taken straight back.
		///
		/// That proxy was the thing at fault, not the separation: the question the input
		/// controller actually wants is "is any panel that needs the cursor on screen", which
		/// <see cref="UIManager.AnyCursorReleasingVisible"/> now answers directly. Keeping the
		/// flags apart matters because they genuinely differ — a confirm dialog needs the cursor
		/// but must not be dismissable with Escape, since the point of it is that the player
		/// chooses.
		/// </remarks>
		public bool CloseOnEscape = false;

		/// <summary>
		/// If true, the player can drag this panel around the screen.
		/// </summary>
		/// <remarks>
		/// Defaults to false so HUD elements stay where their stylesheet puts them; windows are
		/// the panels that opt in.
		/// </remarks>
		[Header("Drag")]
		[Tooltip("Allow the player to drag this panel. Enable for windows, not for HUD elements.")]
		public bool CanDrag = false;

		/// <summary>
		/// If true, a dragged panel is kept fully inside the screen.
		/// </summary>
		/// <remarks>
		/// Without this a window can be dragged far enough off-screen that the header used to
		/// drag it back is no longer reachable,
		/// which for a panel with no other way to move it is unrecoverable short of resetting
		/// the layout.
		/// </remarks>
		[Tooltip("Keep a dragged panel fully on screen.")]
		public bool ClampToScreen = true;

		/// <summary>
		/// GameObject name; used as the key in <see cref="UIManager"/>.
		/// </summary>
		public string Name => gameObject.name;

		/// <summary>
		/// Injected <see cref="Client"/> instance for network/UI interaction.
		/// </summary>
		public Client Client { get; private set; }

		/// <summary>
		/// True when the UIDocument is enabled (panel is rendered and interactive).
		/// </summary>
		public bool Visible { get; private set; }

		/// <summary>
		/// Shortcut to the UIDocument's root VisualElement.
		/// Returns null if <see cref="Document"/> is not assigned.
		/// </summary>
		protected VisualElement Root => Document != null ? Document.rootVisualElement : null;

		/// <summary>
		/// True when the pointer is over this panel or one of its elements has keyboard focus.
		/// </summary>
		/// <remarks>
		/// UI Toolkit already tracks both facts, so this reads them rather than shadowing them in
		/// a field that can fall out of step. A cached flag maintained by pointer enter/leave
		/// handlers is the trap here: a panel destroyed while the pointer is inside never gets
		/// its exit event, and the flag stays true forever.
		/// </remarks>
		public bool HasFocus
		{
			get
			{
				VisualElement root = Root;
				if (!Visible || root == null || root.panel == null)
				{
					return false;
				}

				if (IsInputFieldFocused)
				{
					return true;
				}

				/* Picked rather than bounds-tested. Every panel's root fills the screen — each
				 * one is its own UIDocument — so a rectangle test against the root is true
				 * everywhere and would report every panel as hovered at once. Pick returns the
				 * topmost element that actually accepts the pointer, which is the real question:
				 * is the cursor over this panel's content, or over the world behind it. */
				VisualElement picked = root.panel.Pick(PointerPanelPosition(root.panel));
				while (picked != null)
				{
					if (ReferenceEquals(picked, root))
					{
						// The root itself is the full-screen container, not content.
						return false;
					}
					if (picked.parent != null && ReferenceEquals(picked.parent, root))
					{
						return true;
					}
					picked = picked.parent;
				}
				return false;
			}
		}

		/// <summary>
		/// True when a text field inside this panel currently has keyboard focus.
		/// </summary>
		/// <remarks>
		/// Gates player movement: <c>PlayerInputController</c> asks
		/// <see cref="UIManager.InputControlHasFocus(UITKControl)"/> before reading movement input,
		/// so without this a player typing "west" into chat walks off in three directions.
		/// </remarks>
		public bool IsInputFieldFocused
		{
			get
			{
				VisualElement root = Root;
				if (!Visible || root == null || root.panel == null)
				{
					return false;
				}

				Focusable focused = root.panel.focusController?.focusedElement;
				if (focused == null)
				{
					return false;
				}

				/* A TextField delegates focus to its inner #unity-text-input, so the focused
				 * element is usually that child rather than the field itself. Walking up to the
				 * root is what catches both, and confirms the field belongs to this panel and
				 * not to some other document that happens to be focused. */
				VisualElement element = focused as VisualElement;
				bool sawTextInput = false;
				while (element != null)
				{
					if (element is TextField || element is TextElement)
					{
						sawTextInput = true;
					}
					if (ReferenceEquals(element, root))
					{
						return sawTextInput;
					}
					element = element.parent;
				}
				return false;
			}
		}

		/// <summary>
		/// Reads the pointer position in panel coordinates.
		/// </summary>
		/// <param name="panel">The panel to convert into.</param>
		/// <returns>The pointer position in panel points.</returns>
		private static Vector2 PointerPanelPosition(IPanel panel)
		{
			Vector2 screenPosition = UnityEngine.InputSystem.Mouse.current != null
				? UnityEngine.InputSystem.Mouse.current.position.ReadValue()
				: Vector2.zero;
			// Input System reports Y from the bottom; UI Toolkit lays out from the top.
			return RuntimePanelUtils.ScreenToPanel(
				panel,
				new Vector2(screenPosition.x, Screen.height - screenPosition.y));
		}

		/// <summary>
		/// Draw and input order for this panel. Override to place a panel outside
		/// <see cref="UITKPanelLayer.Window"/>.
		/// </summary>
		/// <remarks>
		/// See <see cref="UITKPanelLayer"/> for why this is declared in code rather than set on
		/// each UIDocument in the scene.
		/// </remarks>
		protected virtual UITKPanelLayer Layer => UITKPanelLayer.Window;

		/// <summary>
		/// Raised when the pointer leaves this panel, having previously been over it.
		/// </summary>
		/// <remarks>
		/// Two panels use this to dismiss themselves: the dropdown and the chat channel picker
		/// both do <c>OnLoseFocus += Hide</c>, so moving the mouse off them closes them without a
		/// click. UI Toolkit has no equivalent event on a panel root — every root fills the
		/// screen, so the pointer never leaves one — which is why this is derived from
		/// <see cref="HasFocus"/> rather than from a PointerLeaveEvent.
		/// <para>
		/// Polling is gated on there being a subscriber, so a panel nobody listens to costs
		/// nothing: <see cref="HasFocus"/> runs a pick against the panel, and doing that every
		/// frame for all forty-odd panels to serve two of them would be wasteful.
		/// </para>
		/// </remarks>
		public Action OnLoseFocus;

		/// <summary>
		/// Whether the pointer was over this panel on the previous frame.
		/// </summary>
		private bool hadPointerFocus;

		/// <summary>
		/// Fires <see cref="OnLoseFocus"/> on the frame the pointer leaves the panel.
		/// </summary>
		private void PollLoseFocus()
		{
			if (OnLoseFocus == null)
			{
				return;
			}

			if (!Visible)
			{
				// A hidden panel cannot be exited; reset so re-showing does not fire immediately.
				hadPointerFocus = false;
				return;
			}

			bool has = HasFocus;
			if (hadPointerFocus && !has)
			{
				OnLoseFocus.Invoke();
			}
			hadPointerFocus = has;
		}

		/// <summary>
		/// Whether clicking this panel raises it above its peers and makes it the next panel
		/// Escape closes.
		/// </summary>
		/// <remarks>
		/// This used to be a per-scene flag, set on every window and on chat and left off for pure
		/// HUD readouts — a health bar has nothing to raise above and nothing to focus. The same
		/// split falls out of the layer, so it no longer has to be authored: windows and
		/// the menu are things the player opens, arranges and closes, and everything at
		/// <see cref="UITKPanelLayer.Hud"/> is a readout. Panels that disagree override this.
		/// </remarks>
		protected virtual bool FocusOnSelect =>
			Layer == UITKPanelLayer.Window || Layer == UITKPanelLayer.Menu;

		/// <summary>
		/// Focus order within each layer, most recently focused last.
		/// </summary>
		/// <remarks>
		/// Panels are raised within their own tier rather than globally. A single flat ordering
		/// lets focusing a panel put it above anything at all — which is exactly how Options once
		/// ended up unreachable behind Login. Keeping the raise inside a tier restores
		/// click-to-front between peers while a modal stays above a window no matter what the
		/// player clicked last.
		/// </remarks>
		private static readonly Dictionary<UITKPanelLayer, List<UITKControl>> focusOrder =
			new Dictionary<UITKPanelLayer, List<UITKControl>>();

		/// <summary>
		/// Highest offset a panel may take inside its layer band.
		/// </summary>
		/// <remarks>
		/// Layers are spaced 100 apart, so an offset is capped short of that to guarantee a
		/// focused panel can never climb into the tier above it. No tier holds anything close to
		/// this many panels; the clamp exists so that a leak in the focus list degrades into
		/// panels sharing a sorting order rather than into a modal being outranked by a window.
		/// </remarks>
		private const int MaxFocusOffset = 89;

		/// <summary>
		/// Raises this panel above the others in its layer.
		/// </summary>
		/// <remarks>
		/// A Canvas would re-parent the transform to make the panel the last sibling. Documents
		/// have no sibling order to exploit, so the equivalent is to renumber the tier: this goes
		/// to the end of its layer's list and every panel in that layer is re-stamped from the
		/// layer's base value.
		/// </remarks>
		public void BringToFront()
		{
			if (Document == null)
			{
				return;
			}

			if (!focusOrder.TryGetValue(Layer, out List<UITKControl> ordered))
			{
				ordered = new List<UITKControl>();
				focusOrder[Layer] = ordered;
			}

			ordered.Remove(this);
			ordered.Add(this);

			// Destroyed panels would otherwise hold slots and push live ones towards the clamp.
			ordered.RemoveAll(c => c == null);

			int baseOrder = (int)Layer;
			for (int i = 0; i < ordered.Count; ++i)
			{
				UITKControl control = ordered[i];
				if (control != null && control.Document != null)
				{
					control.Document.sortingOrder = baseOrder + Mathf.Min(i, MaxFocusOffset);
				}
			}
		}

		/// <summary>
		/// Raises the panel and moves it to the front of the Escape-close order.
		/// </summary>
		/// <remarks>
		/// Re-registering for Escape is not incidental:
		/// with three windows open, Escape should close the one the player last touched, not the
		/// one that happened to open first.
		/// </remarks>
		private void Focus()
		{
			if (!FocusOnSelect)
			{
				return;
			}

			BringToFront();

			if (CloseOnEscape)
			{
				UIManager.UnregisterCloseOnEscapeTK(this);
				UIManager.RegisterCloseOnEscapeTK(this);
			}
		}

		/// <summary>
		/// Raises the panel when it is clicked anywhere.
		/// </summary>
		/// <param name="evt">The pointer press.</param>
		private void OnRootPointerDown(PointerDownEvent evt)
		{
			Focus();
		}

		/// <summary>
		/// Subscribes the focus handler to the panel root.
		/// </summary>
		/// <remarks>
		/// Registered in the trickle-down phase so a press that lands on a button, a slot or a
		/// text field still raises the panel. In the bubble phase a child that stops propagation —
		/// which several controls do — would swallow the press and the window would stay behind.
		/// </remarks>
		private void AttachFocusHandler()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}
			root.UnregisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
			root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
		}

		/// <summary>
		/// Writes <see cref="Layer"/> onto the document's sorting order.
		/// </summary>
		/// <remarks>
		/// Applied on Awake and again on every Show. Once is not enough: hiding a panel disables
		/// its UIDocument, and UI Toolkit drops and re-registers the panel when it is re-enabled,
		/// so a panel that is shown after another was created can otherwise come back in the
		/// wrong order.
		/// </remarks>
		private void ApplySortingOrder()
		{
			if (Document != null)
			{
				Document.sortingOrder = (float)Layer;
			}
		}

		/// <summary>
		/// True once <see cref="OnStarting"/> has run against a populated visual tree.
		/// </summary>
		private bool hasStarted;

		/// <summary>
		/// The first child of the visual tree <see cref="OnStarting"/> last ran against, used to
		/// notice when the tree has been replaced underneath us.
		/// </summary>
		/// <remarks>
		/// Hiding a panel disables its <see cref="UIDocument"/>, and re-enabling it clones the
		/// UXML afresh. Every element reference an override cached during <c>OnStarting</c> then
		/// points into a tree that is no longer displayed: writes to it are silently lost and the
		/// panel shows whatever the UXML declares. A label authored as a placeholder keeps
		/// reading that placeholder, and a bar fill keeps whatever width the stylesheet gave it.
		/// <para>
		/// Comparing identity of the first child is enough to catch a re-clone, and costs one
		/// reference comparison per Show.
		/// </para>
		/// </remarks>
		private VisualElement startedTreeRoot;

		/// <summary>
		/// Registers this control with the <see cref="UIManager"/>, applies
		/// <see cref="StartOpen"/>, and runs <see cref="OnStarting"/> as soon as the visual
		/// tree exists.
		/// </summary>
		private void Awake()
		{
			ApplySortingOrder();
			UIManager.RegisterTK(this);

			// Applied before OnStarting, and independently of it. A panel's initial visibility
			// must not wait on the visual tree, and a hidden panel disables its UIDocument —
			// which is precisely why OnStarting cannot run here for every control.
			if (!StartOpen)
			{
				Hide();
			}

			if (!TryStart())
			{
				StartCoroutine(WaitForVisualTree());
			}
		}

		/// <summary>
		/// Retries initialisation until the visual tree exists.
		/// </summary>
		/// <remarks>
		/// A coroutine rather than <c>Update</c> deliberately. Unity dispatches magic methods to
		/// the most-derived declaration, and several controls — <c>UITKResourceBar</c> and
		/// <c>UITKHotkeyBar</c> among them — declare their own <c>Update</c>, which would shadow
		/// a base one and silently disable this retry for exactly the panels that need it.
		/// A coroutine cannot be shadowed that way.
		/// <para>
		/// A panel that starts hidden has its UIDocument disabled and therefore no tree, so this
		/// keeps waiting until something shows it. The GameObject itself stays active
		/// throughout — only the document is disabled — so the coroutine survives.
		/// </para>
		/// </remarks>
		private IEnumerator WaitForVisualTree()
		{
			while (!TryStart())
			{
				yield return null;
			}
		}

		/// <summary>
		/// Runs <see cref="OnStarting"/> exactly once, and only when the visual tree is
		/// actually populated.
		/// </summary>
		/// <remarks>
		/// This used to be called directly from <see cref="Awake"/>, which was too early.
		/// <see cref="UIDocument"/> allocates <c>rootVisualElement</c> up front but only clones
		/// the UXML into it during its own <c>OnEnable</c> — after every component's Awake — so
		/// Awake sees a real but empty root. Every <c>Q&lt;&gt;</c> in an override returned
		/// null, was cached as null, and never re-resolved, leaving controls that looked
		/// initialised but were wired to nothing.
		/// </remarks>
		private bool TryStart()
		{
			if (this.hasStarted)
			{
				return true;
			}

			VisualElement root = Root;
			if (root == null || root.childCount == 0)
			{
				// Not an error: the document may be disabled, or its tree not cloned yet.
				return false;
			}

			this.hasStarted = true;
			this.startedTreeRoot = root[0];
			OnStarting();
			OnAfterStarting();
			AttachDragHandlers();

			/* Registered here rather than in Awake because the theme is applied by querying the
			 * visual tree for USS classes, and until the tree is cloned there is nothing to
			 * query. ReinitializeIfTreeReplaced re-registers if the tree is later rebuilt. */
			UITKThemeManager.Register(root);
			return true;
		}

		/// <summary>
		/// Keeps a list panel's header count, status line and empty placeholder in step with
		/// the number of rows in its container.
		/// </summary>
		/// <param name="list">Container whose children are the rows.</param>
		/// <param name="count">Header badge showing the row count. May be null.</param>
		/// <param name="subtitle">Header line describing the list. May be null.</param>
		/// <param name="empty">Placeholder shown when the list has no rows. May be null.</param>
		/// <param name="singular">Noun for exactly one row, e.g. "item".</param>
		/// <param name="plural">Noun for zero or many rows, e.g. "items".</param>
		/// <remarks>
		/// Driven off the container's own geometry rather than from each panel's rebuild path.
		/// The panels that own these lists fill them from half a dozen different broadcast
		/// handlers, and hooking every one of them is how a count ends up disagreeing with the
		/// list beneath it. UI Toolkit has no "children changed" event, but adding or removing a
		/// row relayouts the container, so its geometry is a reliable proxy — and a redundant
		/// recount costs one integer read.
		/// </remarks>
		protected void BindListChrome(VisualElement list, Label count, Label subtitle,
			Label empty, string singular, string plural)
		{
			if (list == null)
			{
				return;
			}

			void Refresh()
			{
				int rows = list.childCount;

				if (count != null)
				{
					count.text = rows.ToString();
					count.EnableInClassList("fish-badge--accent", rows > 0);
				}
				if (subtitle != null)
				{
					subtitle.text = rows == 1 ? $"1 {singular}" : $"{rows} {plural}";
				}
				if (empty != null)
				{
					empty.style.display = rows == 0 ? DisplayStyle.Flex : DisplayStyle.None;
				}
			}

			list.UnregisterCallback<GeometryChangedEvent>(OnListGeometryChanged);
			list.userData = (Action)Refresh;
			list.RegisterCallback<GeometryChangedEvent>(OnListGeometryChanged);

			Refresh();
		}

		/// <summary>
		/// Runs the refresh closure a container was bound with.
		/// </summary>
		/// <param name="evt">The geometry change that triggered it.</param>
		/// <remarks>
		/// A named handler rather than a lambda so the matching Unregister above actually
		/// removes it — unregistering a freshly-allocated closure silently does nothing, and
		/// re-binding after a visual tree rebuild would stack duplicate callbacks forever.
		/// </remarks>
		private static void OnListGeometryChanged(GeometryChangedEvent evt)
		{
			if (evt.target is VisualElement element && element.userData is Action refresh)
			{
				refresh();
			}
		}

		/// <summary>
		/// Called immediately after <see cref="OnStarting"/>. Override to re-apply any state
		/// that arrived before the visual tree existed.
		/// </summary>
		/// <remarks>
		/// Initialisation is no longer guaranteed to happen before the control is given data.
		/// A panel that starts hidden has no visual tree until it is shown, and world entry can
		/// hand it a character in the meantime — so whatever it was told before it had elements
		/// to write into has to be applied again here, or the panel stays blank.
		/// </remarks>
		protected virtual void OnAfterStarting() { }

		/// <summary>
		/// Called once the control's visual tree is available.
		/// Override to perform one-time initialisation against <see cref="Root"/>.
		/// </summary>
		/// <remarks>
		/// Guaranteed to see a populated <see cref="Root"/>. For a control that starts hidden
		/// this runs when it is first shown rather than at Awake, because a hidden panel's
		/// UIDocument is disabled and has no tree to query.
		/// </remarks>
		public virtual void OnStarting() { }

		/// <summary>
		/// Called at the start of the MonoBehaviour OnDestroy function.
		/// Override to clean up event subscriptions and managed resources.
		/// </summary>
		public virtual void OnDestroying() { }

		/// <summary>
		/// Called when a <see cref="Client"/> is injected via <see cref="SetClient"/>.
		/// Override to subscribe to client events.
		/// </summary>
		public virtual void OnClientSet() { }

		/// <summary>
		/// Called when the <see cref="Client"/> is cleared via <see cref="SetClient"/>.
		/// Override to unsubscribe from client events.
		/// </summary>
		public virtual void OnClientUnset() { }

		/// <summary>
		/// Injects (or replaces) the <see cref="Client"/> instance.
		/// Handles cleanup of any previously injected client automatically.
		/// </summary>
		/// <param name="client">The new client instance, or null to clear.</param>
		public void SetClient(Client client)
		{
			if (Client != null)
			{
				OnClientUnset();
				Client.OnQuitToLogin -= Client_OnQuitToLogin;
				Client = null;
			}

			if (client != null)
			{
				Client = client;
				Client.OnQuitToLogin += Client_OnQuitToLogin;
				OnClientSet();
			}
		}

		/// <summary>
		/// Handles the client's quit-to-login event. Hides or shows the panel according to
		/// <see cref="CloseOnQuitToMenu"/>, then calls <see cref="OnQuitToLogin"/>.
		/// </summary>
		private void Client_OnQuitToLogin()
		{
			if (CloseOnQuitToMenu)
			{
				Hide(false);
			}
			else
			{
				Show();
			}
			OnQuitToLogin();

			/* OnQuitToLogin stops all coroutines by default, which would silently take the
			 * initialisation retry with it — leaving a control that had not yet seen its visual
			 * tree permanently uninitialised after a single quit to login. Restart it. */
			if (!this.hasStarted)
			{
				StartCoroutine(WaitForVisualTree());
			}
		}

		/// <summary>
		/// Drives <see cref="OnTick"/> for every control in this hierarchy.
		/// </summary>
		/// <remarks>
		/// Centralised so subclasses override a virtual hook instead of declaring their own
		/// <c>Update</c>. Unity binds the most-derived <c>Update</c> only, so two of them in one
		/// hierarchy means the base never runs — a silent failure that is hard to spot and easy
		/// to reintroduce.
		/// </remarks>
		private void Update()
		{
			PollLoseFocus();
			OnTick();
		}

		/// <summary>
		/// Per-frame hook for controls that need one.
		/// </summary>
		protected virtual void OnTick() { }

		/// <summary>
		/// Called when the client quits to the login screen.
		/// Override to perform cleanup such as stopping coroutines.
		/// </summary>
		public virtual void OnQuitToLogin()
		{
			StopAllCoroutines();
		}

		/// <summary>
		/// Toggles the panel's visibility.
		/// </summary>
		public virtual void ToggleVisibility()
		{
			if (Visible)
			{
				Hide();
			}
			else
			{
				Show();
			}
		}

		/// <summary>
		/// Shows the panel by enabling the <see cref="UIDocument"/>.
		/// </summary>
		public virtual void Show()
		{
			if (Visible || Document == null)
			{
				return;
			}
			Document.enabled = true;
			ApplySortingOrder();
			Visible = true;

			// A panel the player just opened belongs above the ones already on screen.
			if (FocusOnSelect)
			{
				BringToFront();
			}

			if (ReleasesCursor)
			{
				PlayerInputController.MouseMode = true;
			}

			if (CloseOnEscape)
			{
				UIManager.RegisterCloseOnEscapeTK(this);
			}

			ReinitializeIfTreeReplaced();
		}

		/// <summary>
		/// Re-runs initialisation when the visual tree has been rebuilt since it last ran.
		/// </summary>
		/// <remarks>
		/// See <see cref="startedTreeRoot"/> for why the tree can change identity. If it has not
		/// changed this is a single reference comparison and does nothing, so it is safe to call
		/// on every Show regardless of whether a given panel ever gets hidden.
		/// </remarks>
		private void ReinitializeIfTreeReplaced()
		{
			if (!this.hasStarted)
			{
				// Never initialised; the startup retry still owns this.
				return;
			}

			VisualElement root = Root;
			if (root == null || root.childCount == 0)
			{
				return;
			}

			if (ReferenceEquals(root[0], this.startedTreeRoot))
			{
				return;
			}

			Log.Debug("UITKControl", $"[{Name}] Visual tree was replaced; re-resolving elements.");

			this.startedTreeRoot = root[0];

			/* Both halves run. Re-resolving elements alone is not enough: panels that build their
			 * contents from character state do it in OnPostSetCharacter, so a rebuilt tree would
			 * come back correctly wired and completely empty — an inventory with no slots in it.
			 * OnAfterStarting re-applies that state, and pairs Pre with Post so re-running it does
			 * not stack up duplicate event subscriptions. */
			OnStarting();
			OnAfterStarting();

			/* The handle lives in the tree that was just replaced, so the callbacks registered
			 * on the old one went with it. Re-registering is not optional: a panel that has
			 * been hidden and re-shown would otherwise silently stop being draggable. */
			AttachDragHandlers();
			AttachFocusHandler();

			// The rebuilt tree carries none of the inline overrides the old one was painted with.
			UITKThemeManager.Apply(root);
		}

		#region Drag

		/// <summary>Name of the header element panels use as a drag handle, when they have one.</summary>
		private const string DragHandleName = "panel-header";

		/// <summary>USS class marking a header element, used when the name lookup misses.</summary>
		private const string DragHandleClass = "fish-panel__header";

		/// <summary>The element the pointer grabs, or null when this panel is not draggable.</summary>
		private VisualElement dragHandle;

		/// <summary>The element actually moved — the panel content root, not the handle.</summary>
		private VisualElement dragTarget;

		/// <summary>Pointer position when the drag began, in panel coordinates.</summary>
		private Vector2 dragStartPointer;

		/// <summary>Target's left/top when the drag began.</summary>
		private Vector2 dragStartPosition;

		/// <summary>Id of the pointer that owns the in-flight drag, or -1 when idle.</summary>
		private int dragPointerId = -1;

		/// <summary>
		/// Wires pointer handling for dragging, replacing any handlers on a previous tree.
		/// </summary>
		/// <remarks>
		/// Called after every initialisation, including the re-initialisation that follows a
		/// visual tree being rebuilt — the handlers belong to elements, so they are discarded
		/// with the tree that owned them.
		/// </remarks>
		private void AttachDragHandlers()
		{
			DetachDragHandlers();

			if (!CanDrag)
			{
				return;
			}

			VisualElement root = Root;
			if (root == null || root.childCount == 0)
			{
				return;
			}

			// The panel root belongs to the UIDocument and fills the screen; moving it would
			// move nothing visible. What the player drags is the content root the UXML declares.
			this.dragTarget = root[0];

			/* Prefer a header. Dragging from anywhere on the panel means a press that begins on
			 * a button and moves a few pixels drags the window instead of pressing it. UI
			 * Toolkit events bubble to the root, so the panel would receive the press either
			 * way. A panel with no header still drags from anywhere rather than becoming
			 * immovable. */
			this.dragHandle = this.dragTarget.Q(DragHandleName)
				?? this.dragTarget.Q(className: DragHandleClass)
				?? this.dragTarget;

			this.dragHandle.RegisterCallback<PointerDownEvent>(OnDragPointerDown);
			this.dragHandle.RegisterCallback<PointerMoveEvent>(OnDragPointerMove);
			this.dragHandle.RegisterCallback<PointerUpEvent>(OnDragPointerUp);
			this.dragHandle.RegisterCallback<PointerCaptureOutEvent>(OnDragPointerCaptureOut);
		}

		/// <summary>
		/// Removes pointer handling for dragging.
		/// </summary>
		private void DetachDragHandlers()
		{
			if (this.dragHandle != null)
			{
				this.dragHandle.UnregisterCallback<PointerDownEvent>(OnDragPointerDown);
				this.dragHandle.UnregisterCallback<PointerMoveEvent>(OnDragPointerMove);
				this.dragHandle.UnregisterCallback<PointerUpEvent>(OnDragPointerUp);
				this.dragHandle.UnregisterCallback<PointerCaptureOutEvent>(OnDragPointerCaptureOut);
			}

			this.dragHandle = null;
			this.dragTarget = null;
			this.dragPointerId = -1;
		}

		/// <summary>
		/// Begins a drag and captures the pointer.
		/// </summary>
		/// <remarks>
		/// Capturing is what makes the drag survive the pointer leaving the handle — without
		/// it a quick movement outruns the panel, the handle stops receiving moves, and the
		/// window is left stuck mid-drag until the next press.
		/// </remarks>
		private void OnDragPointerDown(PointerDownEvent evt)
		{
			if (!CanDrag || this.dragTarget == null || evt.button != 0)
			{
				return;
			}

			// Resolved once here rather than read per move: style.left is a StyleLength that
			// reads back as the value last written, which is NaN until something writes one.
			this.dragStartPosition = new Vector2(this.dragTarget.layout.x, this.dragTarget.layout.y);
			this.dragStartPointer = (Vector2)evt.position;
			this.dragPointerId = evt.pointerId;

			this.dragHandle.CapturePointer(evt.pointerId);

			/* Stopped so the press does not also reach whatever is under it. Not prevented
			 * from bubbling further up, because the panel above still wants to know it was
			 * touched — focus ordering is decided there, not here. */
			evt.StopPropagation();
		}

		/// <summary>
		/// Moves the panel with the pointer.
		/// </summary>
		private void OnDragPointerMove(PointerMoveEvent evt)
		{
			if (this.dragPointerId != evt.pointerId || this.dragTarget == null)
			{
				return;
			}

			Vector2 delta = (Vector2)evt.position - this.dragStartPointer;
			SetDragPosition(this.dragStartPosition + delta);
			evt.StopPropagation();
		}

		/// <summary>
		/// Ends a drag and releases the pointer.
		/// </summary>
		private void OnDragPointerUp(PointerUpEvent evt)
		{
			if (this.dragPointerId != evt.pointerId)
			{
				return;
			}

			this.dragHandle.ReleasePointer(evt.pointerId);
			this.dragPointerId = -1;
			evt.StopPropagation();
		}

		/// <summary>
		/// Clears drag state when the capture is lost to something other than a pointer-up.
		/// </summary>
		/// <remarks>
		/// A capture can be taken away — the panel being hidden mid-drag is the ordinary case.
		/// Without this the control still believes a drag is in flight, and the next move from
		/// that pointer id teleports the panel by the distance travelled in between.
		/// </remarks>
		private void OnDragPointerCaptureOut(PointerCaptureOutEvent evt)
		{
			this.dragPointerId = -1;
		}

		/// <summary>
		/// Writes an absolute position to the panel, clamped to the screen when asked.
		/// </summary>
		/// <param name="position">Desired top-left of the panel, in panel coordinates.</param>
		private void SetDragPosition(Vector2 position)
		{
			VisualElement root = Root;
			if (this.dragTarget == null || root == null)
			{
				return;
			}

			if (ClampToScreen)
			{
				/* Clamped by the panel's own resolved size, so the whole window stays on
				 * screen rather than only its top-left corner. contentRect is the panel root's
				 * size in UI Toolkit's coordinate space, which is what a panel-space position
				 * is measured against — Screen.width/height would be wrong under any
				 * PanelSettings scale mode that is not one-to-one with the display. */
				float maxX = Mathf.Max(0.0f, root.contentRect.width - this.dragTarget.layout.width);
				float maxY = Mathf.Max(0.0f, root.contentRect.height - this.dragTarget.layout.height);

				position.x = Mathf.Clamp(position.x, 0.0f, maxX);
				position.y = Mathf.Clamp(position.y, 0.0f, maxY);
			}

			/* Absolute, so left/top mean what is written here. A panel laid out by its parent
			 * flexbox would otherwise have these quietly ignored, which reads as a drag that
			 * does nothing at all. */
			this.dragTarget.style.position = Position.Absolute;
			this.dragTarget.style.left = position.x;
			this.dragTarget.style.top = position.y;
		}

		/// <summary>
		/// Returns the panel to the position its stylesheet gives it and ends any drag.
		/// </summary>
		/// <remarks>
		/// Clearing the inline styles rather than writing a remembered position hands the panel
		/// back to the USS that placed it,
		/// so it lands where an untouched install would put it on this screen size.
		/// </remarks>
		public void ResetPosition()
		{
			if (this.dragPointerId != -1 && this.dragHandle != null)
			{
				this.dragHandle.ReleasePointer(this.dragPointerId);
				this.dragPointerId = -1;
			}

			if (this.dragTarget == null)
			{
				return;
			}

			this.dragTarget.style.position = StyleKeyword.Null;
			this.dragTarget.style.left = StyleKeyword.Null;
			this.dragTarget.style.top = StyleKeyword.Null;
		}

		#endregion

		/// <summary>
		/// Hides the panel unless <see cref="IsAlwaysOpen"/> is true.
		/// </summary>
		public virtual void Hide()
		{
			Hide(IsAlwaysOpen);
		}

		/// <summary>
		/// Hides the panel unless <paramref name="overrideIsAlwaysOpen"/> is true.
		/// </summary>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public virtual void Hide(bool overrideIsAlwaysOpen)
		{
			if (overrideIsAlwaysOpen || Document == null)
			{
				return;
			}
			Document.enabled = false;
			Visible = false;

			UIManager.UnregisterCloseOnEscapeTK(this);
		}

		/// <summary>
		/// Calls <see cref="OnDestroying"/>, unsubscribes from the client, and unregisters
		/// from the <see cref="UIManager"/>.
		/// </summary>
		private void OnDestroy()
		{
			OnDestroying();

			if (Client != null)
			{
				OnClientUnset();
				Client.OnQuitToLogin -= Client_OnQuitToLogin;
			}
			Client = null;

			UITKThemeManager.Unregister(Root);
			UIManager.UnregisterTK(this);
		}
	}
}
