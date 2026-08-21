using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for hotkey ingress guards and pending hotkey persistence.
	/// </summary>
	public interface IHotkeySystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Shared ingress guard for per-connection per-operation debounce and in-flight tracking.
		/// </summary>
		IngressGuard IngressGuard { get; }

		/// <summary>
		/// Records the character's current hotkey bar as the next thing to be written to the
		/// database, replacing any earlier unwritten snapshot for that character.
		/// </summary>
		/// <param name="characterID">The character whose bar changed.</param>
		/// <param name="hotkeys">The character's live hotkey list. Copied, not retained.</param>
		/// <remarks>
		/// Coalescing by character is deliberate. Dragging one ability along a twelve-slot bar
		/// produces a dozen accepted requests in a couple of seconds, and each one would otherwise
		/// be its own round trip to Postgres. Only the newest snapshot can be correct, so keeping
		/// only the newest is both cheaper and more accurate.
		/// </remarks>
		void StageHotkeyWrite(long characterID, IReadOnlyList<HotkeyData> hotkeys);

		/// <summary>
		/// Moves every staged snapshot into <paramref name="destination"/> and empties the stage.
		/// </summary>
		/// <param name="destination">List to receive the staged snapshots.</param>
		/// <returns>True if anything was drained.</returns>
		bool DrainHotkeyWrites(List<KeyValuePair<long, HotkeyData[]>> destination);

		/// <summary>
		/// Removes and returns the staged snapshot for one character, if any.
		/// </summary>
		/// <param name="characterID">The character to drain.</param>
		/// <param name="hotkeys">The staged snapshot.</param>
		/// <returns>True if a snapshot was staged for that character.</returns>
		bool TryDrainHotkeyWrite(long characterID, out HotkeyData[] hotkeys);

		/// <summary>
		/// Produces the next strictly-increasing persistence version for a hotkey row.
		/// </summary>
		/// <returns>A monotonic version value.</returns>
		/// <remarks>
		/// <para>
		/// The hotkey upsert is gated <c>WHERE EXCLUDED.version &gt; character_hotkey.version</c>,
		/// so a write only lands if it carries a strictly larger version than whatever is already
		/// in the row. Nothing in the runtime tracked a hotkey version at all — the load path
		/// reads the column and discards it — so the version has to be derived rather than
		/// remembered.
		/// </para>
		/// <para>
		/// <c>DateTime.UtcNow.Ticks</c> supplies that: it is larger than every version any
		/// previous session wrote, and the running maximum below keeps it strictly increasing
		/// even when two writes land inside one tick of clock resolution. It is deliberately NOT
		/// <c>long.MaxValue</c> — the item layer's per-slot poisoning bug (audit CRIT-2) is
		/// exactly what happens when a "make sure this write wins" sentinel is stamped into a
		/// version column: the row becomes permanently unwritable.
		/// </para>
		/// </remarks>
		long NextHotkeyVersion();
	}
}
