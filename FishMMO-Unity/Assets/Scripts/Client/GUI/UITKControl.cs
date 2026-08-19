using UnityEngine;
using UnityEngine.UIElements;
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
		/// Registers this control with the <see cref="UIManager"/>, calls <see cref="OnStarting"/>,
		/// and hides the panel if <see cref="StartOpen"/> is false.
		/// </summary>
		private void Awake()
		{
			UIManager.RegisterTK(this);

			OnStarting();

			if (!StartOpen)
			{
				Hide();
			}
		}

		/// <summary>
		/// Called at the end of the MonoBehaviour Awake function.
		/// Override to perform one-time initialisation against <see cref="Root"/>.
		/// </summary>
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
			Visible = true;
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
