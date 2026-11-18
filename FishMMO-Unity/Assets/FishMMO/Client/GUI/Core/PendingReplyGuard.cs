using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Watchdog for a UI control that has been disabled while waiting for a server reply.
	/// </summary>
	/// <remarks>
	/// Every login-flow panel works the same way: it disables its action button, sends a
	/// request, and re-enables the button when the reply arrives. That is correct right up
	/// until the reply never arrives, at which point the panel is a dead end — the player is
	/// looking at a screen whose only control does nothing, with no error and no explanation.
	/// <para>
	/// The reply can go missing for reasons the client cannot see and cannot fix: the server's
	/// main-thread queue rejecting the action at capacity, a handler throwing before it sends,
	/// or the server simply never getting to it. Rather than enumerate those, the panel assumes
	/// a reply that has not arrived within <see cref="DefaultTimeoutSeconds"/> is not coming,
	/// hands the control back, and says so.
	/// </para>
	/// <para>
	/// Re-enabling is deliberately all it does. Nothing is torn down and no connection is
	/// dropped, so a late reply is still handled normally when it lands — the panel's own
	/// handler re-enables an already-enabled control, which is a no-op.
	/// </para>
	/// </remarks>
	public sealed class PendingReplyGuard
	{
		/// <summary>
		/// How long to wait for a reply before handing the control back.
		/// </summary>
		/// <remarks>
		/// Comfortably longer than any round trip these panels make, including one that has to
		/// wait on a database write, so a merely slow server is never mistaken for a silent
		/// one. Short enough that a player does not sit in front of a dead button wondering
		/// whether the game has stopped responding.
		/// </remarks>
		public const float DefaultTimeoutSeconds = 30.0f;

		/// <summary>True while a request is outstanding.</summary>
		private bool pending;

		/// <summary>Unscaled time at which the wait is abandoned.</summary>
		private float expiresAtUnscaled;

		/// <summary>True while a reply is still expected.</summary>
		public bool IsPending => pending;

		/// <summary>
		/// Starts the wait. Call when the control is disabled and the request goes out.
		/// </summary>
		/// <param name="timeoutSeconds">Seconds to wait; defaults to <see cref="DefaultTimeoutSeconds"/>.</param>
		public void Begin(float timeoutSeconds = DefaultTimeoutSeconds)
		{
			pending = true;
			expiresAtUnscaled = Time.unscaledTime + Mathf.Max(1.0f, timeoutSeconds);
		}

		/// <summary>
		/// Extends the wait because the server has been heard from.
		/// </summary>
		/// <remarks>
		/// For flows that report progress before they finish — the SRP exchange and the
		/// two-factor prompt both send intermediate results — an intermediate message is proof
		/// the server is still working, so it should buy the same grace again rather than
		/// counting against the original deadline.
		/// </remarks>
		/// <param name="timeoutSeconds">Seconds to wait from now.</param>
		public void Refresh(float timeoutSeconds = DefaultTimeoutSeconds)
		{
			if (pending)
			{
				expiresAtUnscaled = Time.unscaledTime + Mathf.Max(1.0f, timeoutSeconds);
			}
		}

		/// <summary>
		/// Ends the wait. Call when the reply arrives, or when the control is re-enabled for
		/// any other reason.
		/// </summary>
		public void Clear()
		{
			pending = false;
			expiresAtUnscaled = 0.0f;
		}

		/// <summary>
		/// Reports whether the wait has just been abandoned.
		/// </summary>
		/// <remarks>
		/// Self-clearing, so it returns <c>true</c> exactly once per wait and the caller can
		/// drive it straight from a per-frame tick without tracking anything itself.
		/// </remarks>
		/// <returns><c>true</c> on the single frame the timeout elapses.</returns>
		public bool HasExpired()
		{
			if (!pending || Time.unscaledTime < expiresAtUnscaled)
			{
				return false;
			}

			Clear();
			return true;
		}
	}
}
