using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIBuff : UICharacterControl
	{
		public RectTransform BuffParent;
		public UIBuffGroup BuffButtonPrefab;

		public Dictionary<int, UIBuffGroup> Buffs = new Dictionary<int, UIBuffGroup>();

		public override void OnStarting()
		{
			IBuffController.OnAddBuff += BuffController_OnAddBuff;
			IBuffController.OnRemoveBuff += BuffController_OnRemoveBuff;

			IPlayerCharacter.OnStopLocalClient += (c) => ClearAllBuffs();
		}

		public override void OnDestroying()
		{
			IBuffController.OnAddBuff -= BuffController_OnAddBuff;
			IBuffController.OnRemoveBuff -= BuffController_OnRemoveBuff;

			IPlayerCharacter.OnStopLocalClient -= (c) => ClearAllBuffs();

			ClearAllBuffs();
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
		}

		public override void OnPreUnsetCharacter()
		{
		}

		public override void OnQuitToLogin()
		{
			ClearAllBuffs();
		}

		private void BuffController_OnAddBuff(Buff buff)
		{
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (buff.Template.IsDebuff)
			{
				return;
			}
			if (Buffs.ContainsKey(buff.Template.ID))
			{
				return;
			}
			UIBuffGroup buffGroup = Instantiate(BuffButtonPrefab, BuffParent);
			if (buffGroup.ButtonText != null)
			{
				buffGroup.ButtonText.text = buff.Template.Name;
			}
			if (buffGroup.TooltipButton != null)
			{
				buffGroup.TooltipButton.Initialize(buff.Template.ID, Buff_OnLeftClick, null, buff.Template, "\r\n\r\nLeft Mouse Button to remove.");
			}
			if (buffGroup.Icon != null)
			{
				buffGroup.Icon.sprite = buff.Template.Icon;
			}
			if (Buffs == null)
			{
				Buffs = new Dictionary<int, UIBuffGroup>();
			}
			Buffs.Add(buff.Template.ID, buffGroup);
			buffGroup.gameObject.SetActive(true);
		}

		private void BuffController_OnRemoveBuff(Buff buff)
		{
			if (buff == null)
			{
				return;
			}
			if (buff.Template == null)
			{
				return;
			}
			if (buff.Template.IsDebuff)
			{
				return;
			}
			if (Buffs.TryGetValue(buff.Template.ID, out UIBuffGroup group))
			{
				Destroy(group.gameObject);
				Buffs.Remove(buff.Template.ID);
			}
		}

		public void ClearAllBuffs()
		{
			if (Buffs == null || Buffs.Count == 0)
			{
				return;
			}
			foreach (UIBuffGroup group in Buffs.Values)
			{
				Destroy(group.gameObject);
			}
			Buffs.Clear();
		}

		public void Buff_OnLeftClick(int index, object[] optionalParams)
		{
			Debug.Log($"Clicked buff {index} {optionalParams}");
		}
	}
}