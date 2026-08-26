using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Probe: can the real PlayerCharacter component be created in edit mode and its behaviour
	/// registry seeded, so panels resolve controllers through the normal TryGet path?
	/// </summary>
	/// <remarks>
	/// If this works the rig needs ~15 small controller fakes. If it does not, the rig has to
	/// implement IPlayerCharacter (47 members) plus ICharacter (29) from scratch as well.
	/// </remarks>
	public static class RigProbe
	{
		private static int failures;

		private static void Check(string what, bool ok, string got = "")
		{
			Debug.Log((ok ? "PASS  " : "FAIL  ") + what + (string.IsNullOrEmpty(got) ? "" : "   " + got));
			if (!ok) ++failures;
		}

		[MenuItem("FishMMO/UI Toolkit/Probe Character Rig")]
		public static void Run()
		{
			GameObject go = null;
			try
			{
				go = new GameObject("RigProbe") { hideFlags = HideFlags.HideAndDontSave };

				PlayerCharacter character = null;
				try
				{
					character = go.AddComponent<PlayerCharacter>();
				}
				catch (Exception ex)
				{
					Check("AddComponent<PlayerCharacter> in edit mode", false, ex.GetType().Name + ": " + ex.Message);
				}
				Check("AddComponent<PlayerCharacter> in edit mode", character != null);

				if (character != null)
				{
					// Now the real test: build the rig and resolve a controller through TryGet.
					UnityEngine.Object.DestroyImmediate(character);
					PlayerCharacter rigged = Rig.Build(go);
					Check("rig builds", rigged != null);

					bool gotInv = rigged.TryGet(out IInventoryController inv);
					Check("TryGet<IInventoryController> resolves the fake", gotInv && inv != null,
						gotInv ? $"slots={inv.Items.Count}" : "");

					bool gotEquip = rigged.TryGet(out IEquipmentController eq);
					Check("TryGet<IEquipmentController> resolves", gotEquip && eq != null,
						gotEquip ? $"slots={eq.Items.Count}" : "");

					bool gotBank = rigged.TryGet(out IBankController bank);
					Check("TryGet<IBankController> resolves", gotBank && bank != null,
						gotBank ? $"slots={bank.Items.Count}" : "");

					character = rigged;
				}

				if (false)
				{
					// Can identity be set? Panels read CharacterName / ID constantly.
					bool nameOk = TrySet(character, "CharacterName", "Thalorin");
					Check("CharacterName settable", nameOk, ReadString(character, "CharacterName"));

					bool idOk = TrySet(character, "ID", 1001L);
					Check("ID settable", idOk, ReadString(character, "ID"));

					// Can the protected behaviour registry be reached and seeded?
					FieldInfo behaviours = typeof(BaseCharacter).GetField("Behaviours",
						BindingFlags.NonPublic | BindingFlags.Instance);
					Check("Behaviours field reachable", behaviours != null);

					if (behaviours != null)
					{
						var map = behaviours.GetValue(character) as Dictionary<Type, ICharacterBehaviour>;
						Check("Behaviours is a live dictionary", map != null,
							map == null ? "" : $"count={map.Count}");
					}

					// Does TryGet work at all without networking?
					bool threw = false;
					try
					{
						character.TryGet(out IPartyController _);
					}
					catch (Exception ex)
					{
						threw = true;
						Check("TryGet does not throw", false, ex.GetType().Name + ": " + ex.Message);
					}
					if (!threw) Check("TryGet does not throw", true);
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Probe] threw: {ex}");
				++failures;
			}
			finally
			{
				if (go != null) UnityEngine.Object.DestroyImmediate(go);
			}

			Debug.Log(failures == 0 ? "[Probe] RIG FEASIBLE" : $"[Probe] {failures} BLOCKER(S)");
			EditorApplication.Exit(0);
		}

		private static bool TrySet(object target, string member, object value)
		{
			Type t = target.GetType();
			PropertyInfo prop = t.GetProperty(member,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (prop != null && prop.CanWrite)
			{
				try { prop.SetValue(target, value); return true; } catch { }
			}

			// Auto-property backing field, or a plain field.
			for (Type c = t; c != null; c = c.BaseType)
			{
				FieldInfo f = c.GetField(member,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					?? c.GetField($"<{member}>k__BackingField",
						BindingFlags.NonPublic | BindingFlags.Instance);
				if (f != null)
				{
					try { f.SetValue(target, value); return true; } catch { }
				}
			}
			return false;
		}

		private static string ReadString(object target, string member)
		{
			PropertyInfo p = target.GetType().GetProperty(member,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			try { return p?.GetValue(target)?.ToString() ?? "(null)"; } catch { return "(threw)"; }
		}
	}
}
