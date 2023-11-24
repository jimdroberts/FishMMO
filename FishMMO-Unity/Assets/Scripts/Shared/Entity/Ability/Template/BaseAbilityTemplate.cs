using Cysharp.Text;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseAbilityTemplate : CachedScriptableObject<BaseAbilityTemplate>, ITooltip, ICachedObject
	{
		public Sprite icon;
		public string Description;
		public float ActivationTime;
		public float ActiveTime;
		public float Cooldown;
		public float Range;
		public float Speed;
		public AbilityResourceDictionary Resources = new AbilityResourceDictionary();
		public AbilityResourceDictionary Requirements = new AbilityResourceDictionary();

		public string Name { get { return this.name; } }

		public Sprite Icon { get { return this.icon; } }

		public string Field(string valueName, float value, bool appendLine = false, string hexColor = null, string size = null)
		{
			if (value == 0.0f)
			{
				return "";
			}

			using (var sb = ZString.CreateStringBuilder())
			{
				if (appendLine)
				{
					sb.AppendLine();
				}
				if (!string.IsNullOrWhiteSpace(size))
				{
					sb.Append("<size=" + size + ">");
				}
				if (!string.IsNullOrWhiteSpace(hexColor))
				{
					sb.Append("<color=#" + hexColor + ">");
				}
				if (!string.IsNullOrWhiteSpace(valueName))
				{
					sb.Append(valueName);
					sb.Append(": ");
				}
				if (value < 0)
				{
					sb.Append("-");
				}
				else if (value > 0)
				{
					sb.Append("+");
				}
				sb.Append(value);
				if (!string.IsNullOrWhiteSpace(hexColor))
				{
					sb.Append("</color>");
				}
				if (!string.IsNullOrWhiteSpace(size))
				{
					sb.Append("</size>");
				}
				return sb.ToString();
			}
		}

		public string Field(string value, bool appendLine = false, string hexColor = null, string size = null)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return "";
			}

			using (var sb = ZString.CreateStringBuilder())
			{
				if (!string.IsNullOrWhiteSpace(value))
				{
					if (appendLine)
					{
						sb.AppendLine();
					}
					if (!string.IsNullOrWhiteSpace(size))
					{
						sb.Append("<size=" + size + ">");
					}
					if (!string.IsNullOrWhiteSpace(hexColor))
					{
						sb.Append("<color=#" + hexColor + ">");
					}
					sb.Append(value);
					if (!string.IsNullOrWhiteSpace(hexColor))
					{
						sb.Append("</color>");
					}
					if (!string.IsNullOrWhiteSpace(size))
					{
						sb.Append("</size>");
					}
				}
				return sb.ToString();
			}
		}

		public virtual string Tooltip()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append(Field(Name, false, "f5ad6e", "120%"));
				sb.Append(Field(Description, true, "a66ef5FF"));
				sb.Append(Field("Activation Time", ActivationTime, true, "a66ef5FF"));
				sb.Append(Field("Active Time", ActiveTime, true, "a66ef5FF"));
				sb.Append(Field("Cooldown", Cooldown, true, "a66ef5FF"));
				sb.Append(Field("Range", Range, true, "a66ef5FF"));
				sb.Append(Field("Speed", Speed, true, "a66ef5FF"));

				if (Resources != null && Resources.Count > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Resources: </color>");

					foreach (CharacterAttributeTemplate attribute in Resources.Keys)
					{
						if (!string.IsNullOrWhiteSpace(attribute.Name))
						{
							sb.Append(Field(attribute.Name, true, "f5ad6eFF", "120%"));
						}
					}
				}
				if (Requirements != null && Requirements.Count > 0)
				{
					sb.AppendLine();
					sb.Append("<color=#a66ef5>Requirements: </color>");

					foreach (CharacterAttributeTemplate attribute in Requirements.Keys)
					{
						if (!string.IsNullOrWhiteSpace(attribute.Name))
						{
							sb.Append(Field(attribute.Name, true, "f5ad6eFF", "120%"));
						}
					}
				}
				return sb.ToString();
			}
		}
	}
}