using Cysharp.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a single attribute modification from a buff, including value and target attribute template.
	/// </summary>
	public class BuffAttribute
	{
		/// <summary>
		/// The value to add (or subtract) from the target attribute.
		/// </summary>
		public int Value;

		/// <summary>
		/// The character attribute template that this buff modifies.
		/// </summary>
		public CharacterAttributeTemplate Template;

		/// <summary>
		/// Constructs a new BuffAttribute with the specified value and template.
		/// </summary>
		/// <param name="value">The value to apply to the attribute.</param>
		/// <param name="template">The attribute template to modify.</param>
		public BuffAttribute(int value, CharacterAttributeTemplate template)
		{
			this.Value = value;
			this.Template = template;
		}

		/// <summary>
		/// Returns a formatted tooltip string describing this buff attribute.
		/// </summary>
		/// <returns>A colorized string with the attribute name and value.</returns>
		public string Tooltip()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append("<color=#a66ef5>");
				// Show the attribute's display name
				sb.Append(Template.Name);
				if (Template != null)
				{
					// Show the internal name and value
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