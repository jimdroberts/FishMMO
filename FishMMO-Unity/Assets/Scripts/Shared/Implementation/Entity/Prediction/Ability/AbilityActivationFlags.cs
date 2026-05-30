namespace FishMMO.Shared
{
	/// <summary>
	/// Flags representing the activation state of an ability.
	/// Stored as bit positions in an int and manipulated via IntBitExtensions.
	/// <para>
	/// Bit position 0 (IsActualData) is used as a sentinel to distinguish real input from
	/// FishNet's default-filled replicate data. When the Replicate method receives data
	/// without this bit set, it returns immediately. This allows bit 0 to double as a
	/// "data present" marker without consuming an extra field in the replicate struct.
	/// </para>
	/// <para>
	/// <b>Constraint:</b> All enum values are bit positions and must remain in the
	/// range 0–15 (bits 0–15 after shifting).
	/// <see cref="CharacterReconcileData.Pack"/> stores flags in the lower 16 bits.
	/// Values at or above 16 will be silently truncated during reconcile.
	/// </para>
	/// </summary>
	public enum AbilityActivationFlags : int
	{
		/// <summary>
		/// Bit 0: Indicates the data is actual activation data (not a default-filled gap).
		/// Set by HandleCharacterInput before packaging; checked at the top of Replicate.
		/// Using position 0 means a default int (0) will never have this flag set.
		/// </summary>
		IsActualData = 0,

		/// <summary>
		/// Indicates the ability was interrupted.
		/// </summary>
		Interrupt,

		/// <summary>
		/// Indicates the activation key is held (for charged/channeled abilities).
		/// </summary>
		IsHeld,

		/// <summary>
		/// Indicates the activation is for a consumable item.
		/// </summary>
		IsConsumable,

		/// <summary>
		/// Indicates the activation is for a mount.
		/// </summary>
		IsMount,

		/// <summary>
		/// Server-authoritative denial flag. Set in <see cref="AbilityController.OnCreateReconcile"/>
		/// when the server rejected the last queued activation (TryStartAbility/TryStartConsumable
		/// failed but input had a queued ability). Clients check this in OnReconcile to fire
		/// <see cref="AbilityController.OnAbilityDenied"/> accurately instead of relying on the
		/// heuristic (currentAbilityID != NO_ABILITY &amp;&amp; rd.AbilityID == NO_ABILITY) which
		/// false-fires on zero-duration abilities that complete before the reconcile tick.
		/// </summary>
		Denied,
	}
}