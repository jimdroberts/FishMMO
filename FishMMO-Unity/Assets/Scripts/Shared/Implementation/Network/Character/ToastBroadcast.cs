using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// How prominently a toast should read, and what colour it takes.
	/// </summary>
	/// <remarks>
	/// Severity rather than colour, so the theme owns the palette and a server never encodes a
	/// hex value into gameplay code.
	/// </remarks>
	public enum ToastSeverity : byte
	{
		/// <summary>Neutral. "Your bind point has moved."</summary>
		Info = 0,

		/// <summary>Something the player wanted, and got.</summary>
		Success = 1,

		/// <summary>Refused for a reason the player can act on. "Not enough clearance to bind here."</summary>
		Warning = 2,

		/// <summary>Refused for a reason the player cannot act on, or something went wrong.</summary>
		Error = 3,
	}

	/// <summary>
	/// A short, transient message shown to one player.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Deliberately general rather than bindstone-shaped: any server system that refuses an action
	/// for a reason the player could otherwise not see can send one, and several already have that
	/// problem. <c>BindstoneAction</c> computed a precise rejection — wrong scene, inside an
	/// instance, not enough clearance — and wrote every one of them to a server-side debug log, so
	/// binding either worked or did nothing and the player could not tell which.
	/// </para>
	/// <para>
	/// Text is sent rather than a message id. A message id would be the right answer for a
	/// localised client, and this should become one when localisation exists; sending text now
	/// keeps the failure mode "the wrong words appear" rather than "a system cannot report at
	/// all". The client truncates, so a caller cannot use this as an unbounded channel.
	/// </para>
	/// </remarks>
	public struct ToastBroadcast : IBroadcast
	{
		/// <summary>What to show. Truncated by the client at <c>UITKToast.MaximumLength</c>.</summary>
		public string Text;

		/// <summary>How prominently it reads.</summary>
		public ToastSeverity Severity;
	}
}
