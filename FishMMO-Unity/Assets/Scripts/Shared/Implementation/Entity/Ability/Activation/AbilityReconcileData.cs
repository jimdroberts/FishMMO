using FishNet.Object.Prediction;
using FishNet.CodeGenerating;

namespace FishMMO.Shared
{
	/// <summary>
	/// A single cooldown entry for reconcile serialization.
	/// </summary>
	public struct CooldownReconcileEntry
	{
		public long AbilityID;
		public uint StartTick;
		public uint DurationTicks;
	}

	/// <summary>
	/// A single buff entry for reconcile serialization.
	/// Uses tick-based timing fields (<see cref="ExpiryTick"/>, <see cref="NextTickTick"/>)
	/// mirroring <see cref="CooldownReconcileEntry"/>'s immutable tick design.
	/// Tick values are absolute network ticks, so they are stable between structural changes
	/// (add/remove/stack), allowing the delta serializer's <c>ReferenceEquals</c> fast-path
	/// to suppress transmission on unchanged ticks with zero network overhead.
	/// </summary>
	public struct BuffReconcileEntry
	{
		public int TemplateID;
		/// <summary>Absolute network tick at which this buff expires.</summary>
		public uint ExpiryTick;
		/// <summary>Absolute network tick at which the next OnTick fires.</summary>
		public uint NextTickTick;
		public int Stacks;
		public int TickCount;
	}

	/// <summary>
	/// Reconcile data for the full character prediction state: ability activation,
	/// resource attributes, cooldowns, and buffs.
	/// Includes the current RNG seed so the client can detect prediction mismatches
	/// and roll back erroneously spawned ability objects.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Array references:</b> <see cref="Cooldowns"/> and <see cref="Buffs"/> may hold
	/// the same array reference across consecutive ticks when
	/// <see cref="CooldownController.CreateReconcileSnapshot"/> /
	/// <see cref="BuffController.CreateReconcileSnapshot"/> return a cached snapshot.
	/// The delta serializer exploits this: <c>ReferenceEquals(prev, next)</c> skips the
	/// per-element comparison entirely, producing zero network bytes for unchanged arrays.
	/// </para>
	/// <para>
	/// Activation phase is implicit and reconstructible from existing fields:
	/// <list type="bullet">
	/// <item><description>AbilityID == NO_ABILITY → idle (no ability active).</description></item>
	/// <item><description>AbilityID != NO_ABILITY, RemainingTicks &gt; 0, IsHeld not set → casting.</description></item>
	/// <item><description>AbilityID != NO_ABILITY, RemainingTicks &gt; 0, IsHeld set → channeling (held cast in progress).</description></item>
	/// <item><description>AbilityID != NO_ABILITY, RemainingTicks == 0, IsHeld set → charged/holding (awaiting release).</description></item>
	/// <item><description>IsConsumable flag set → consumable activation (same RemainingTicks logic, no IsHeld).</description></item>
	/// </list>
	/// No explicit phase enum is needed.
	/// </remarks>
	[UseGlobalCustomSerializer]
	public struct CharacterReconcileData : IReconcileData
	{
		/// <summary>
		/// The ID of the ability instance, or consumable template ID.
		/// Typed as long to match <see cref="Ability.ID"/> (database-generated).
		/// </summary>
		public long AbilityID;

		/// <summary>
		/// The remaining activation time in ticks (deterministic, avoids float drift).
		/// </summary>
		public uint RemainingTicks;

		/// <summary>
		/// The current deterministic RNG seed for ability spawning.
		/// Used by the client to detect prediction mismatches and roll back predicted ability objects.
		/// </summary>
		public int Seed;

		/// <summary>
		/// The resource state associated with the ability.
		/// </summary>
		public CharacterAttributeResourceState ResourceState;

		/// <summary>
		/// Packed field combining activation flags and consumable slot to reduce bandwidth.
		/// <list type="bullet">
		/// <item><description>Bits 0–15: persistent activation flags (IsHeld, IsConsumable, IsMount, etc.).
		/// Maps to <c>AbilityController.replicatedFlags</c>. Local input flags are intentionally
		/// NOT included so that inputs queued between ticks survive reconcile.</description></item>
		/// <item><description>Bits 16–31: consumable inventory slot as a signed short, or -1 if none.</description></item>
		/// </list>
		/// Use <see cref="UnpackFlags"/> and <see cref="UnpackConsumableSlot"/> to read.
		/// </summary>
		public int PackedFlagsAndSlot;

		/// <summary>
		/// Snapshot of all active cooldowns at the reconcile tick.
		/// Null when no cooldowns are active.
		/// </summary>
		public CooldownReconcileEntry[] Cooldowns;

		/// <summary>
		/// Snapshot of all active buffs at the reconcile tick.
		/// Null when no buffs are active.
		/// </summary>
		public BuffReconcileEntry[] Buffs;

		/// <summary>
		/// Full xoshiro128** internal state of the ability seed generator.
		/// Required because the 128-bit RNG state cannot be reconstructed from
		/// the 32-bit <see cref="Seed"/> output alone. Without this, a single
		/// prediction mismatch permanently desynchronizes the generator,
		/// causing a cascade of mismatches on every subsequent ability activation.
		/// </summary>
		public uint RngS0;

		/// <summary>
		/// Second xoshiro128** state word. See <see cref="RngS0"/>.
		/// </summary>
		public uint RngS1;

		/// <summary>
		/// Third xoshiro128** state word. See <see cref="RngS0"/>.
		/// </summary>
		public uint RngS2;

		/// <summary>
		/// Fourth xoshiro128** state word. See <see cref="RngS0"/>.
		/// </summary>
		public uint RngS3;

		/// <summary>
		/// Extracts the activation flags from the lower 16 bits of <see cref="PackedFlagsAndSlot"/>.
		/// </summary>
		public int UnpackFlags => PackedFlagsAndSlot & 0xFFFF;

		/// <summary>
		/// Extracts the consumable slot from the upper 16 bits of <see cref="PackedFlagsAndSlot"/> as a signed short.
		/// Returns -1 when no consumable is active.
		/// </summary>
		public short UnpackConsumableSlot => (short)(PackedFlagsAndSlot >> 16);

		/// <summary>
		/// Packs activation flags (lower 16 bits) and consumable slot (upper 16 bits) into a single int.
		/// </summary>
		/// <param name="flags">Activation flags. Only the lower 16 bits are used.</param>
		/// <param name="consumableSlot">Inventory slot, or -1 if none.</param>
		/// <returns>The packed int.</returns>
		public static int Pack(int flags, int consumableSlot)
		{
			System.Diagnostics.Debug.Assert((flags & ~0xFFFF) == 0,
				$"CharacterReconcileData.Pack flags 0x{flags:X} exceed 16-bit storage.");
			System.Diagnostics.Debug.Assert(consumableSlot >= short.MinValue && consumableSlot <= short.MaxValue,
				$"CharacterReconcileData.Pack consumableSlot {consumableSlot} exceeds signed 16-bit storage.");

			return (flags & 0xFFFF) | (((short)consumableSlot) << 16);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterReconcileData"/> struct.
		/// </summary>
		public CharacterReconcileData(
			long abilityID,
			uint remainingTicks,
			int seed,
			CharacterAttributeResourceState resourceState,
			int inputFlags,
			int consumableSlot,
			CooldownReconcileEntry[] cooldowns,
			BuffReconcileEntry[] buffs,
			uint rngS0,
			uint rngS1,
			uint rngS2,
			uint rngS3)
		{
			AbilityID = abilityID;
			RemainingTicks = remainingTicks;
			Seed = seed;
			ResourceState = resourceState;
			PackedFlagsAndSlot = Pack(inputFlags, consumableSlot);
			Cooldowns = cooldowns;
			Buffs = buffs;
			RngS0 = rngS0;
			RngS1 = rngS1;
			RngS2 = rngS2;
			RngS3 = rngS3;

			tick = 0;
		}

		private uint tick;

		/// <summary>
		/// Disposes the reconcile data (no-op).
		/// </summary>
		public void Dispose() { }

		/// <summary>
		/// Gets the network tick value.
		/// </summary>
		/// <returns>The tick value.</returns>
		public uint GetTick() => tick;

		/// <summary>
		/// Sets the network tick value.
		/// </summary>
		/// <param name="value">Tick value.</param>
		public void SetTick(uint value) => tick = value;
	}
}