public class ItemAttribute
{
	public int templateID;
	public ItemAttributeTemplate Template { get { return ItemAttributeTemplate.Cache[templateID]; } }

	public int value;

	public ItemAttribute(int templateID, int value)
	{
		this.templateID = templateID;
		this.value = value;
	}
}