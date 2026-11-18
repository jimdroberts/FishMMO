using FishNet.Connection;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Whether a player request passes through the character-state gate before the handler runs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An enum rather than a <c>bool</c>, because the opt-out is the dangerous half and a bare
	/// <c>false</c> at a call site carries no reason with it. <c>SkipCanAct</c> has to be typed out,
	/// which is the point: it is the spelling a reviewer can grep for, and the one that reads wrong
	/// on a handler that mutates game state.
	/// </para>
	/// </remarks>
	public enum PlayerRequestGate
	{
		/// <summary>
		/// Refuse the request unless <see cref="CharacterStateValidation.CanAct"/> passes. The
		/// default, and correct for anything that changes game state.
		/// </summary>
		RequireCanAct = 0,

		/// <summary>
		/// Resolve the character but do not ask whether it may act.
		/// </summary>
		/// <remarks>
		/// Only for requests that are not themselves an action — joining a queue, asking the server
		/// what it already knows — AND where the action the request eventually leads to is gated
		/// separately. A handler that reaches for this needs a comment saying which later gate
		/// covers it; see <c>OnServerGroupFinderQueueBroadcastReceived</c> and
		/// <c>TryResolveArenaBoard</c>, both of which lead to a transfer that
		/// <c>HandleMatchedEntry</c> gates on <see cref="CharacterStateValidation.CanActOrMove"/>.
		/// </remarks>
		SkipCanAct = 1,
	}

	/// <summary>
	/// The acting player behind one client request, resolved once at the top of a broadcast handler.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A struct rather than a bare <c>out IPlayerCharacter</c> so that what the preamble produces can
	/// grow without touching every call site again. The whole reason this type exists is that the
	/// preamble HAS grown, repeatedly, and each growth spurt was pasted: the connection check, then
	/// the first-object check, then <c>CanAct</c>, then a controller lookup. Thirty-odd handlers
	/// carried private copies of the sequence and the copies had already drifted apart in which
	/// checks they ran and in what order.
	/// </para>
	/// <para>
	/// Deliberately NOT carrying the ingress guard key. The guard lives in per-system runtime data
	/// (<c>IGuildSystemRuntimeData</c>, <c>IInteractableSystemRuntimeData</c>, one per system), is
	/// keyed by a per-system operation enum, and its debounce interval is a serialized field on the
	/// system asset — so acquiring it here would mean passing three system-specific things through a
	/// base-class method. More to the point, a REFUSED guard is answered differently by almost every
	/// handler: some return in silence, some send a throttle reason, one re-sends the whole hotkey
	/// bar so the client stops believing its own optimistic edit. Folding that into the shared
	/// preamble is a separate change; this one moves only the part that is genuinely identical.
	/// </para>
	/// </remarks>
	public readonly struct PlayerRequestContext
	{
		/// <summary>The connection the request arrived on. Never null when <see cref="IsValid"/>.</summary>
		public readonly NetworkConnection Connection;

		/// <summary>The acting player character. Never null when <see cref="IsValid"/>.</summary>
		public readonly IPlayerCharacter Character;

		internal PlayerRequestContext(NetworkConnection connection, IPlayerCharacter character)
		{
			Connection = connection;
			Character = character;
		}

		/// <summary>True when a character was resolved. False on a <c>default</c> context.</summary>
		public bool IsValid => Character != null;

		/// <summary>The acting character's database id, or 0 on a <c>default</c> context.</summary>
		public long CharacterID => Character != null ? Character.ID : 0;

		/// <summary>
		/// Resolves one of the acting character's controllers.
		/// </summary>
		/// <remarks>
		/// Null-safe on a <c>default</c> context, so a caller that ignored the return of
		/// <c>TryBeginPlayerRequest</c> gets a refusal rather than a NullReferenceException.
		/// </remarks>
		public bool TryGet<TBehaviour>(out TBehaviour behaviour)
			where TBehaviour : class, ICharacterBehaviour
		{
			if (Character == null)
			{
				behaviour = null;
				return false;
			}
			return Character.TryGet(out behaviour);
		}
	}

	public abstract partial class ServerBehaviour
	{
		/// <summary>
		/// Resolves the acting player behind a client request and applies the character-state gate.
		/// The single entry point every player-initiated broadcast handler should open with.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Usage:
		/// <code>
		/// if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
		/// {
		///     return;
		/// }
		/// </code>
		/// The ingress guard, and whatever this handler answers a refused guard with, stay at the
		/// call site — see <see cref="PlayerRequestContext"/> for why.
		/// </para>
		/// <para>
		/// The checks here are exactly the ones the hand-written preambles ran, in the order they
		/// ran them, and nothing more. In particular a MISSING character refuses silently rather
		/// than through <see cref="CharacterStateValidation.CanAct"/>, which logs its refusals: the
		/// inline copies returned without a word, and this replaces them rather than changing what
		/// they do. That is also why this does not delegate to
		/// <c>CharacterStateValidation.TryGetPlayerAndValidate</c>, the Shared-assembly cousin that
		/// declares itself canonical and has no callers — it folds the null-character case into
		/// <c>CanAct</c>, and it cannot express <see cref="PlayerRequestGate.SkipCanAct"/>.
		/// </para>
		/// <para>
		/// <c>conn.FirstObject</c>, not the character mapping table, because that is what the
		/// handlers being replaced used. <c>CharacterSystem</c>'s own handlers resolve through
		/// <c>ICharacterMappingData.ConnectionCharacters</c> instead and are NOT interchangeable
		/// with this: the two disagree for a connection mid-hand-off, whose network object exists
		/// before the mapping entry does.
		/// </para>
		/// </remarks>
		/// <param name="conn">The connection the broadcast arrived on. May be null.</param>
		/// <param name="context">The acting player, or <c>default</c> when the request is refused.</param>
		/// <param name="gate">
		/// Leave at <see cref="PlayerRequestGate.RequireCanAct"/> unless the request is not an action
		/// and something downstream gates the action it leads to.
		/// </param>
		/// <returns>True when the handler may proceed.</returns>
		protected bool TryBeginPlayerRequest(
			NetworkConnection conn,
			out PlayerRequestContext context,
			PlayerRequestGate gate = PlayerRequestGate.RequireCanAct)
		{
			context = default;

			if (conn == null || conn.FirstObject == null)
			{
				return false;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return false;
			}

			if (gate == PlayerRequestGate.RequireCanAct &&
				!CharacterStateValidation.CanAct(character))
			{
				return false;
			}

			context = new PlayerRequestContext(conn, character);
			return true;
		}
	}
}
