using System.Text;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class AbilityEvent : CachedScriptableObject<AbilityEvent>
	{
		public Sprite Icon;
		public string Description;
		public float ActivationTime;
		public float ActiveTime;
		public float Cooldown;
		public float Range;
		public float Speed;
		public AbilityResourceDictionary Resources = new AbilityResourceDictionary();
		public AbilityResourceDictionary Requirements = new AbilityResourceDictionary();

		public string Name { get { return this.name; } }

		public string Tooltip()
		{
			StringBuilder sb = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(Name))
			{
				sb.Append("<size=120%><color=#f5ad6e>");
				sb.Append(name);
				sb.Append("</color></size>");
			}
			if (!string.IsNullOrWhiteSpace(Description))
			{
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Description: ");
				sb.Append(Description);
				sb.Append("</color>");
			}
			if (ActivationTime > 0)
			{
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Activation Time: ");
				sb.Append(ActivationTime);
				sb.Append("</color>");
			}
			if (ActiveTime > 0)
			{
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Active Time: ");
				sb.Append(ActiveTime);
				sb.Append("</color>");
			}
			if (Cooldown > 0)
			{
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Cooldown: ");
				sb.Append(Cooldown);
				sb.Append("</color>");
			}
			if (Range > 0)
			{
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Range: ");
				sb.Append(Range);
				sb.Append("</color>");
			}
			if (Speed > 0)
			{
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Speed: ");
				sb.Append(Speed);
				sb.Append("</color>");
			}
			if (Resources != null && Resources.Count > 0)
			{
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Resources: </color>");

				foreach (CharacterAttributeTemplate attribute in Resources.Keys)
				{
					sb.Append(attribute.Tooltip());
				}
			}
			if (Requirements != null && Requirements.Count > 0)
			{
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Requirements: </color>");

				foreach (CharacterAttributeTemplate attribute in Requirements.Keys)
				{
					sb.Append(attribute.Tooltip());
				}
			}
			return sb.ToString();
		}
	}
}