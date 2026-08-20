using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract base class for UI Toolkit-backed controls. Mirrors the lifecycle of
	/// <see cref="UIControl"/> but drives a <see cref="UIDocument"/> instead of a UGUI Canvas.
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
		/// Mirrors what <see cref="UIControl"/> does for its own panels, where the same behaviour
		/// is gated on <c>CloseOnEscape</c>: a window the player interacts with needs the cursor,
		/// and a HUD element that is permanently on screen must not take it away. Defaulting this
		/// to false keeps bars and hotkey strips from stealing the cursor the moment they appear.
		/// <para>
		/// Only ever set true here, never false, which matches the legacy panels — releasing the
		/// cursor is a panel's business but recapturing it is not, since another panel may still
		/// be open. <see cref="PlayerInputController"/> and <see cref="Client"/> own the reset.
		/// </para>
		/// </remarks>
		[Tooltip("Show() releases the mouse cursor. Enable for windows and dialogs, not for HUD elements.")]
		public bool ReleasesCursor = false;

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
			return true;
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
			Visible = true;

			if (ReleasesCursor)
			{
				PlayerInputController.MouseMode = true;

				/* Registered together with the cursor release, and not optional. The input
				 * controller recaptures the cursor on every frame where nothing is registered as
				 * closeable, so a panel that releases it without registering gets it taken back
				 * before the player can click anything. Escape closing the panel falls out of the
				 * same registration. */
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
		}

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

			UIManager.UnregisterTK(this);
		}
	}
}
