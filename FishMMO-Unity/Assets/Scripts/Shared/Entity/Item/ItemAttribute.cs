namespace FishMMO.Shared
{
	public class ItemAttribute
	{
		public ItemAttributeTemplate Template { get; private set; }

		public int value;

		public ItemAttribute(int templateID, int value)
		{
			Template = ItemAttributeTemplate.Get<ItemAttributeTemplate>(templateID);
			this.value = value;
		}
	}
}