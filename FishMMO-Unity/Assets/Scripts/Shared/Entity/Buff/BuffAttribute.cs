using Cysharp.Text;

namespace FishMMO.Shared
{
	public class BuffAttribute
	{
		public int Value;
		public CharacterAttributeTemplate Template;

		public BuffAttribute(int value, CharacterAttributeTemplate template)
		{
			this.Value = value;
			this.Template = template;
		}

		public string Tooltip()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append("<color=#a66ef5>");
				sb.Append(Template.Name);
				if (Template != null)
				{
					sb.Append(Template.name);
					sb.Append(": ");
					sb.Append(Value);
				}
				sb.Append("</color>");
				return sb.ToString();
			}
		}
	}
}