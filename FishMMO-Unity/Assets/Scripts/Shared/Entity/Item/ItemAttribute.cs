namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an attribute instance for an item, such as strength, durability, or custom stat.
	/// Holds a reference to the attribute template and its current value.
	/// </summary>
	public class ItemAttribute
	{
		/// <summary>
		/// The template that defines the type and metadata of this attribute.
		/// </summary>
		public ItemAttributeTemplate Template { get; private set; }

		/// <summary>
		/// The current value of this attribute instance.
		/// </summary>
		public int value;

		/// <summary>
		/// Constructs an ItemAttribute from a template ID and value.
		/// Looks up the template using the provided ID.
		/// </summary>
		/// <param name="templateID">The ID of the attribute template.</param>
		/// <param name="value">The initial value for this attribute.</param>
		public ItemAttribute(int templateID, int value)
		{
			// Lookup the attribute template by ID. This allows dynamic assignment of attribute types.
			Template = ItemAttributeTemplate.Get<ItemAttributeTemplate>(templateID);
			this.value = value;
		}
	}
}