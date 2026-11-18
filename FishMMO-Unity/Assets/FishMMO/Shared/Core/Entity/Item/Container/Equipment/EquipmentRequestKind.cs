namespace FishMMO.Shared.Core
{
	/// <summary>
	/// What an owner's equipment request asks for.
	/// </summary>
	/// <remarks>
	/// Two bits on the wire — see <c>EquipmentReplicateInput</c> — so there is room for exactly one
	/// more kind. <see cref="None"/> is the value every tick carries that has no request in it, and
	/// must stay zero: a default-initialised replicate has to mean "nothing asked".
	/// </remarks>
	public enum EquipmentRequestKind : byte
	{
		/// <summary>No request on this tick.</summary>
		None = 0,

		/// <summary>Move an item from a container index into an equipment socket.</summary>
		Equip = 1,

		/// <summary>Move the item in an equipment socket into a container.</summary>
		Unequip = 2,
	}
}
