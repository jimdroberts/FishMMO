using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Asks the server to take a buff off the sender.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Client → server, a request, and refused at the source.</b> Clicking a buff icon on the HUD
	/// strip has always been advertised — the tooltip says "Left Mouse Button to remove" — and has
	/// never done anything: the strip had no click handler and there was no message for one to send.
	/// This is that message.
	/// </para>
	/// <para>
	/// <b>The server owns the decision.</b> It resolves the sender's own character from its own
	/// connection, looks the buff up in THAT character's container, and removes it only when
	/// <see cref="BaseBuffTemplate.CanBeDismissedByPlayer"/> says it may be. Nothing is taken on the
	/// client's word: an id naming a buff the sender does not have removes nothing, and a debuff is
	/// refused however it was asked for — a debuff is on you until it expires or something dispels
	/// it. Sending this for a debuff is therefore wasted packets rather than a way to clear one.
	/// </para>
	/// <para>
	/// <b>Keyed by template ID, not by position.</b> The character's buff container is keyed by
	/// template ID already (<see cref="IBuffController.Buffs"/>), so the id in this message is the
	/// same value the server looks up with and there is no index to age out between send and
	/// receipt. One click clears the buff and every stack of it, which is what the strip draws as a
	/// single icon and what the hint promises.
	/// </para>
	/// <para>
	/// <b>No optimistic removal on the client.</b> The removal travels back on the reconcile the
	/// server already sends — <c>BuffController.Remove</c> marks the snapshot dirty and the owner's
	/// <c>RestoreFromReconcile</c> fires <c>OnRemoveBuff</c> — so the icon leaves when the server
	/// says it left. Blanking it locally would show a buff as gone while the server still held it,
	/// which for a refused removal never corrects itself.
	/// </para>
	/// </remarks>
	public struct DismissBuffBroadcast : IBroadcast
	{
		/// <summary>Template ID of the buff to remove. See <see cref="IBuffController.Buffs"/>.</summary>
		public int TemplateID;
	}
}
