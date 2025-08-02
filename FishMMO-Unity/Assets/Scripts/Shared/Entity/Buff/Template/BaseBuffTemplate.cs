using System.Collections.Generic;
using UnityEngine;
using Cysharp.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for all buff templates, defining shared properties, tooltip logic, and effect hooks.
	/// </summary>
	public abstract class BaseBuffTemplate : CachedScriptableObject<BaseBuffTemplate>, ITooltip
	{
		/// <summary>
		/// The visual effect prefab to instantiate when the buff is applied.
		/// </summary>
		public GameObject FXPrefab;

		/// <summary>
		/// The description of the buff, shown in tooltips.
		/// </summary>
		public string Description;

		/// <summary>
		/// The icon representing this buff in the UI.
		/// </summary>
		[SerializeField]
		private Sprite icon;

		/// <summary>
		/// The duration of the buff in seconds. If 0, the buff may be permanent or event-driven.
		/// </summary>
		public float Duration;

		/// <summary>
		/// The interval in seconds between OnTick calls while the buff is active.
		/// </summary>
		public float TickRate;

		/// <summary>
		/// The number of times this buff can be used or triggered.
		/// </summary>
		public uint UseCount;

		/// <summary>
		/// The maximum number of stacks this buff can have.
		/// </summary>
		public uint MaxStacks;

		/// <summary>
		/// True if the buff is permanent and does not expire.
		/// </summary>
		public bool IsPermanent;

		/// <summary>
		/// True if this buff is a debuff (negative effect).
		/// </summary>
		public bool IsDebuff;

		//public AudioClip OnApplySounds;
		//public AudioClip OnTickSounds;
		//public AudioClip OnRemoveSounds;

		/// <summary>
		/// The name of this buff template (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

		/// <summary>
		/// The icon for this buff template (from the serialized field).
		/// </summary>
		public Sprite Icon { get { return this.icon; } }

		/// <summary>
		/// Returns the tooltip string for this buff (primary tooltip only).
		/// </summary>
		public virtual string Tooltip()
		{
			return PrimaryTooltip(null);
		}

		/// <summary>
		/// Returns the tooltip string for this buff, optionally combining with other tooltips.
		/// </summary>
		/// <param name="combineList">Optional list of other tooltips to combine with.</param>
		public virtual string Tooltip(List<ITooltip> combineList)
		{
			return PrimaryTooltip(combineList);
		}

		/// <summary>
		/// Returns the formatted description for this buff, used in tooltips.
		/// </summary>
		public virtual string GetFormattedDescription()
		{
			return Description;
		}

		/// <summary>
		/// Builds the primary tooltip string for this buff, including name, description, and secondary tooltip.
		/// </summary>
		/// <param name="combineList">Optional list of other tooltips to combine with.</param>
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

		/// <summary>
		/// Appends additional information to the tooltip (e.g., secondary effects). Override in derived classes.
		/// </summary>
		/// <param name="stringBuilder">The string builder to append to.</param>
		public virtual void SecondaryTooltip(Utf16ValueStringBuilder stringBuilder) { }

		/// <summary>
		/// Instantiates the FXPrefab on the target when the buff is applied (client-side only).
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
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
				// Instantiate the FXPrefab as a child of the character's mesh root or transform
				GameObject fxPrefab = Instantiate(FXPrefab, target.MeshRoot != null ? target.MeshRoot : target.Transform);
			}
#endif
		}

		/// <summary>
		/// Called when the buff is applied to a character. Must be implemented by derived classes.
		/// </summary>
		/// <param name="buff">The buff instance being applied.</param>
		/// <param name="target">The character receiving the buff.</param>
		public abstract void OnApply(Buff buff, ICharacter target);

		/// <summary>
		/// Called when the buff is removed from a character. Must be implemented by derived classes.
		/// </summary>
		/// <param name="buff">The buff instance being removed.</param>
		/// <param name="target">The character losing the buff.</param>
		public abstract void OnRemove(Buff buff, ICharacter target);

		/// <summary>
		/// Called when a stack of the buff is applied. Must be implemented by derived classes.
		/// </summary>
		/// <param name="buff">The buff instance being stacked.</param>
		/// <param name="target">The character receiving the stack.</param>
		public abstract void OnApplyStack(Buff buff, ICharacter target);

		/// <summary>
		/// Called when a stack of the buff is removed. Must be implemented by derived classes.
		/// </summary>
		/// <param name="buff">The buff instance being unstacked.</param>
		/// <param name="target">The character losing the stack.</param>
		public abstract void OnRemoveStack(Buff buff, ICharacter target);

		/// <summary>
		/// Called on each tick while the buff is active. Must be implemented by derived classes.
		/// </summary>
		/// <param name="buff">The buff instance.</param>
		/// <param name="target">The character affected.</param>
		public abstract void OnTick(Buff buff, ICharacter target);
	}
}