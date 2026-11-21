using System.Collections.Generic;
using FishMMO.Database.Data;
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

		/// <summary>
		/// Turns a bar into the rows that persist it, one per slot, each stamped with a fresh
		/// <see cref="NextHotkeyVersion"/>.
		/// </summary>
		/// <remarks>
		/// EVERY slot, the empty ones included. The rows are keyed <c>(character_id, slot)</c> and
		/// the upsert has no delete path, so writing only the occupied slots would leave a cleared
		/// slot showing its previous binding forever.
		/// </remarks>
		/// <param name="characterID">The owning character.</param>
		/// <param name="hotkeys">The bar.</param>
		/// <returns>One row per slot.</returns>
		List<CharacterHotkeyData> BuildRows(long characterID, IReadOnlyList<HotkeyData> hotkeys);

		/// <summary>
		/// The departing character's whole live bar as rows, for the save that runs before its
		/// session is released; anything staged for it is dropped, since this supersedes it. Main
		/// thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The bar is written by the character's own departure — <c>CharacterSystem</c>'s
		/// save-and-release, the reattach, the shutdown flush — inside the work that ends in the
		/// release, under the claim it quotes. It used to be flushed from <c>OnDisconnect</c> as a
		/// separate write, and from this system's own teardown, which runs before the character
		/// system's shutdown flush releases every claim: once every hotkey write is
		/// ownership-gated, a write that lands after its release is refused and lost, not merely
		/// late.
		/// </para>
		/// <para>
		/// The LIVE bar, not the stage: a write that failed earlier is re-staged only on the next
		/// pump, which a departing character does not get, so the stage alone could miss it.
		/// </para>
		/// </remarks>
		/// <param name="characterID">The departing character.</param>
		/// <param name="liveBar">Its bar as it stands.</param>
		/// <returns>One row per slot, or null when it has no bar.</returns>
		List<CharacterHotkeyData> TakeDepartingBar(long characterID, IReadOnlyList<HotkeyData> liveBar);
	}
}
