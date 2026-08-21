namespace FishMMO.Client
{
	/// <summary>
	/// Types of reference buttons used in UI, such as inventory, equipment, bank, and ability buttons.
	/// </summary>
	public enum ReferenceButtonType : byte
	{
		/// <summary>
		/// No reference type assigned.
		/// </summary>
		None = 0,
		/// <summary>
		/// Reference to an inventory slot.
		/// </summary>
		Inventory,
		/// <summary>
		/// Reference to an equipment slot.
		/// </summary>
		Equipment,
		/// <summary>
		/// Reference to a bank slot.
		/// </summary>
		Bank,
		/// <summary>
		/// Reference to an ability.
		/// </summary>
		Ability,
	}

	/// <summary>
	/// Constants shared by every UI element that carries a reference to an inventory item,
	/// equipped item, bank item or ability.
	/// </summary>
	/// <remarks>
	/// <see cref="NULL_REFERENCE_ID"/> used to be a const on the uGUI <c>UIReferenceButton</c>
	/// component. The UI Toolkit slots are plain <c>VisualElement</c>s with no such base class to
	/// inherit it from, and six panels compare against it, so it lives here — beside the enum it
	/// is always used with, and outside the uGUI tree.
	/// </remarks>
	public static class ReferenceButton
	{
		/// <summary>
		/// Reference ID meaning "this slot is empty".
		/// </summary>
		/// <remarks>
		/// Zero is a legitimate database ID, so the empty sentinel has to be negative.
		/// </remarks>
		public const long NULL_REFERENCE_ID = -1;
	}
}
