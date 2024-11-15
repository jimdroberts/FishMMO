using System.Collections.Generic;
using UnityEngine;
using Cysharp.Text;

namespace FishMMO.Shared
{
	public abstract class BaseBuffTemplate : CachedScriptableObject<BaseBuffTemplate>, ITooltip
	{
		public GameObject FXPrefab;
		public string Description;
		[SerializeField]
		private Sprite icon;
		public float Duration;
		public float TickRate;
		public uint UseCount;
		public uint MaxStacks;
		public bool IsPermanent;
		public bool IsDebuff;
		//public AudioClip OnApplySounds;
		//public AudioClip OnTickSounds;
		//public AudioClip OnRemoveSounds;

		public string Name { get { return this.name; } }
		public Sprite Icon { get { return this.icon; } }

		public virtual string Tooltip()
		{
			return PrimaryTooltip(null);
		}

		public virtual string Tooltip(List<ITooltip> combineList)
		{
			return PrimaryTooltip(combineList);
		}

		public virtual string GetFormattedDescription()
		{
			return Description;
		}

		private string PrimaryTooltip(List<ITooltip> combineList)
		{
 			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append(RichText.Format(Name, true, "f5ad6e", "140%"));

				if (!string.IsNullOrWhiteSpace(Description))
				{
					sb.AppendLine();
					sb.Append(RichText.Format(GetFormattedDescription(), true, "a66ef5FF"));
				}
				SecondaryTooltip(sb);
				return sb.ToString();
			}
		}

		public virtual void SecondaryTooltip(Utf16ValueStringBuilder stringBuilder) { }
		public virtual void OnApplyFX(Buff buff, ICharacter target)
		{
			if (buff == null)
			{
				return;
			}
			if (target == null)
			{
				return;
			}
#if !UNITY_SERVER
			if (FXPrefab != null)
			{
				GameObject fxPrefab = Instantiate(FXPrefab, target.Transform);
			}
#endif
		}
		public abstract void OnApply(Buff buff, ICharacter target);
		public abstract void OnRemove(Buff buff, ICharacter target);
		public abstract void OnApplyStack(Buff buff, ICharacter target);
		public abstract void OnRemoveStack(Buff buff, ICharacter target);
		public abstract void OnTick(Buff buff, ICharacter target);
	}
}