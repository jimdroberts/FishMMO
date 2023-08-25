public class ItemAttribute
{
	public int templateID;

	private ItemAttributeTemplate cachedTemplate;
	public ItemAttributeTemplate Template { get { return cachedTemplate; } }

	public int value;

	public ItemAttribute(int templateID, int value)
	{
		this.templateID = templateID;
		this.cachedTemplate = ItemAttributeTemplate.Get<ItemAttributeTemplate>(templateID);
		this.value = value;
	}
}