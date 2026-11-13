using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.ChatModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// A player's chat state, on the player's Core entity: the repeat filter, the rate gate and any mute. Each
	/// spawn gets a fresh one from the <see cref="ChatModule"/>, so nothing carries over a pooled character.
	/// </summary>
	/// <remarks>
	/// The tick fields start at zero, meaning "never": no gap pending, no refill yet. Each side keeps them on
	/// its own clock (the server on its monotonic clock, the client on the wall clock), so a starting value
	/// taken from either clock would be wrong for the other: a wall-clock stamp read by the server's monotonic
	/// gate refused every line.
	/// </remarks>
	public struct ChatState
	{
		/// <summary>The last line sent, for refusing an exact repeat.</summary>
		public string LastMessage;

		/// <summary>
		/// Ticks before which the next line is refused by the minimum gap, on the clock of the side that keeps
		/// it: the server's monotonic clock, the client's wall clock. Zero is "no gap pending".
		/// </summary>
		public long NextMessageTicks;

		/// <summary>
		/// Server: tokens in the anti-spam bucket (a double, for precision on long-uptime servers); one is spent
		/// per line, and below one the line is dropped.
		/// </summary>
		public double Tokens;

		/// <summary>
		/// Server: true while the bucket is in its starting full state (no refill applied yet), so the first
		/// refill fills to capacity. Replaced a <c>double.MaxValue</c> sentinel that overflowed to Infinity on the
		/// first add.
		/// </summary>
		public bool IsFull;

		/// <summary>Server: monotonic ticks of the last bucket refill.</summary>
		public long LastRefillTicks;

		/// <summary>
		/// Server: UTC ticks until which the player's chat is muted: 0 when not muted, <c>long.MaxValue</c> for a
		/// mute with no end. Never written to the spawn payload. Resolved from the character's own mute and its
		/// account's when the character loads, and refreshed by the operator command that changes either; held as
		/// ticks so the chat path's test is one comparison against the stamp the message already carries.
		/// </summary>
		public long MutedUntilTicks;

		/// <summary>Server: why the player is muted, for telling them; null when not muted.</summary>
		public string MuteReason;
	}

	/// <summary>Chat as a module: gives every player its <see cref="ChatState"/>.</summary>
	public sealed class ChatModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "chat";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
			builder.Archetype(Archetypes.Player).Add(new ChatState { IsFull = true, LastMessage = string.Empty });
		}
	}
}
