using System.Text;

public class BuffAttribute
{
	public int value;
	public CharacterAttributeTemplate template;

	public BuffAttribute(int value, CharacterAttributeTemplate template)
	{
		this.value = value;
		this.template = template;
	}

	public string Tooltip()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("<color=#a66ef5>");
		sb.Append(template.Name);
		if (template != null)
		{
			sb.Append(template.name);
			sb.Append(": ");
			sb.Append(value);
		}
		sb.Append("</color>");
		return sb.ToString();
	}
}