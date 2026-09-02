using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Transient messages stacked at the top of the screen: what just happened, and why it did not.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Built general rather than for any one caller. Several server systems already compute a
	/// precise reason for refusing something and then have nowhere to put it — <c>BindstoneAction</c>
	/// knows whether a bind failed because the player was inside an instance, in the wrong scene, or
	/// standing somewhere with too little clearance, and wrote all three to a server-side debug log.
	/// The player saw an identical nothing in every case, including success.
	/// </para>
	/// <para>
	/// Anything may post one: the server through <see cref="ToastBroadcast"/>, or client code
	/// directly through <see cref="Show(string, ToastSeverity)"/> for things the server never hears
	/// about. There is no queue behind the visible set — a toast that would overflow
	/// <see cref="MaximumVisible"/> evicts the oldest instead of waiting, because a message that
	/// arrives late enough to be shown after the thing it describes has scrolled past is worse than
	/// one that is dropped.
	/// </para>
	/// </remarks>
	public class UITKToast : UITKControl
	{
		/// <summary>Longest text a toast will show. Anything beyond this is truncated with an ellipsis.</summary>
		/// <remarks>
		/// A cap rather than a wrap, so a caller cannot turn this into a scrolling log. The server
		/// sends free text, and this is the only thing bounding it.
		/// </remarks>
		public const int MaximumLength = 160;

		/// <summary>How many toasts may be on screen at once before the oldest is evicted.</summary>
		public const int MaximumVisible = 5;

		/// <summary>Seconds a toast stays fully visible before it begins to fade.</summary>
		[Tooltip("Seconds a toast stays fully visible before it begins to fade.")]
		public float HoldSeconds = 4.0f;

		/// <summary>Seconds the fade-out takes once the hold has elapsed.</summary>
		[Tooltip("Seconds the fade-out takes once the hold expires.")]
		public float FadeSeconds = 0.5f;

		/// <summary>Draw order tier. Toasts sit with the HUD, above the world and below windows.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>The container every toast is added to.</summary>
		private VisualElement stack;

		/// <summary>One live toast and the clock it is being retired on.</summary>
		private sealed class LiveToast
		{
			public VisualElement Element;
			public float RemainingSeconds;
		}

		private readonly List<LiveToast> live = new List<LiveToast>();

		/// <summary>
		/// Resolves the stack container from the visual tree.
		/// </summary>
		/// <remarks>
		/// Re-resolved rather than cached across rebuilds: <c>OnStarting</c> runs again whenever the
		/// visual tree is replaced, and an element reference held from the previous tree points at
		/// something no longer on screen.
		/// </remarks>
		public override void OnStarting()
		{
			stack = Document?.rootVisualElement?.Q<VisualElement>("toast-stack");

			/* The tree was replaced, so whatever was on screen belongs to the old one. Dropping the
			 * tracking list keeps the timers from ticking against orphaned elements. */
			live.Clear();
		}

		/// <summary>Subscribes to server-sent toasts.</summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ToastBroadcast>(OnClientToastBroadcastReceived);
		}

		/// <summary>Unsubscribes from server-sent toasts.</summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ToastBroadcast>(OnClientToastBroadcastReceived);
		}

		/// <summary>Clears any live toasts when the panel goes away.</summary>
		public override void OnDestroying()
		{
			live.Clear();
			stack = null;
		}

		/// <summary>
		/// Shows a toast sent by the server.
		/// </summary>
		private void OnClientToastBroadcastReceived(ToastBroadcast msg, FishNet.Transporting.Channel channel)
		{
			Show(msg.Text, msg.Severity);
		}

		/// <summary>
		/// Shows a toast.
		/// </summary>
		/// <remarks>
		/// Safe to call before the visual tree exists — the toast is dropped rather than queued. A
		/// message posted while the HUD is being rebuilt describes something the player has already
		/// moved past.
		/// </remarks>
		/// <param name="text">What to show. Truncated at <see cref="MaximumLength"/>.</param>
		/// <param name="severity">How prominently it reads.</param>
		public void Show(string text, ToastSeverity severity = ToastSeverity.Info)
		{
			if (stack == null || string.IsNullOrWhiteSpace(text))
			{
				return;
			}

			string trimmed = text.Trim();
			if (trimmed.Length > MaximumLength)
			{
				trimmed = trimmed.Substring(0, MaximumLength - 1) + "…";
			}

			Label label = new Label(trimmed);
			label.AddToClassList("fish-label");
			label.AddToClassList("toast");
			label.AddToClassList(ClassFor(severity));
			label.pickingMode = PickingMode.Ignore;

			stack.Add(label);
			live.Add(new LiveToast
			{
				Element = label,
				RemainingSeconds = Mathf.Max(0.1f, HoldSeconds) + Mathf.Max(0.0f, FadeSeconds),
			});

			// Oldest first, so eviction takes from the front.
			while (live.Count > MaximumVisible)
			{
				Retire(live[0]);
				live.RemoveAt(0);
			}
		}

		/// <summary>
		/// Ages the visible toasts and retires the ones whose time is up.
		/// </summary>
		private void Update()
		{
			if (live.Count < 1)
			{
				return;
			}

			float fade = Mathf.Max(0.0001f, FadeSeconds);

			for (int i = live.Count - 1; i >= 0; --i)
			{
				LiveToast toast = live[i];
				toast.RemainingSeconds -= Time.unscaledDeltaTime;

				if (toast.RemainingSeconds <= 0.0f)
				{
					Retire(toast);
					live.RemoveAt(i);
					continue;
				}

				/* Faded by opacity rather than removed and re-added, so the elements below do not
				 * jump up while the one above is still legible. */
				if (toast.RemainingSeconds < fade && toast.Element != null)
				{
					toast.Element.style.opacity = Mathf.Clamp01(toast.RemainingSeconds / fade);
				}
			}
		}

		/// <summary>Removes a toast's element from the tree.</summary>
		private void Retire(LiveToast toast)
		{
			toast?.Element?.RemoveFromHierarchy();
		}

		/// <summary>Maps a severity onto the USS class that colours it.</summary>
		private static string ClassFor(ToastSeverity severity)
		{
			switch (severity)
			{
				case ToastSeverity.Success: return "toast-success";
				case ToastSeverity.Warning: return "toast-warning";
				case ToastSeverity.Error: return "toast-error";
				default: return "toast-info";
			}
		}
	}
}
