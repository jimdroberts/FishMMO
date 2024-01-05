using Cysharp.Text;
using UnityEngine;
using System.Collections.Generic;

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
		public long Price;
		public AbilityResourceDictionary Resources = new AbilityResourceDictionary();
		public AbilityResourceDictionary Requirements = new AbilityResourceDictionary();

		public string Name { get { return this.name; } }

		public Sprite Icon { get { return this.icon; } }

		public virtual string Tooltip()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append(RichText.Format(Name, false, "f5ad6e", "135%"));
				sb.Append("\r\n______________________________\r\n");
				sb.Append(RichText.Format(Description, true, "a66ef5FF"));
				sb.Append("\r\n______________________________\r\n");
				sb.Append(RichText.Format("Activation Time", ActivationTime, true, "a66ef5FF"));
				sb.Append(RichText.Format("Active Time", ActiveTime, true, "a66ef5FF"));
				sb.Append(RichText.Format("Cooldown", Cooldown, true, "a66ef5FF"));
				sb.Append(RichText.Format("Range", Range, true, "a66ef5FF"));
				sb.Append(RichText.Format("Speed", Speed, true, "a66ef5FF"));
				if (Resources != null && Resources.Count > 0)
				{
					sb.Append("\r\n______________________________\r\n");
					sb.Append("<color=#a66ef5>Resources: </color>");

					foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in Resources)
					{
						if (!string.IsNullOrWhiteSpace(pair.Key.Name))
						{
							sb.Append(RichText.Format(pair.Key.Name, pair.Value, true, "f5ad6eFF", "120%"));
						}
					}
				}
				if (Requirements != null && Requirements.Count > 0)
				{
					sb.Append("\r\n______________________________\r\n");
					sb.Append("<color=#a66ef5>Requirements: </color>");

					foreach (KeyValuePair<CharacterAttributeTemplate, int> pair in Requirements)
					{
						if (!string.IsNullOrWhiteSpace(pair.Key.Name))
						{
							sb.Append(RichText.Format(pair.Key.Name, pair.Value, true, "f5ad6eFF", "120%"));
						}
					}
				}
				if (Price > 0)
				{
					sb.Append("\r\n______________________________\r\n");
					sb.Append(RichText.Format("Price", Price, true, "a66ef5FF"));
				}
				
				return sb.ToString();
			}
		}
	}
}