using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Managing.Timing;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// Capture support: what the collider overlay draws for this sim beyond the live colliders.
	/// </summary>
	/// <remarks>
	/// <list type="bullet">
	/// <item>Yellow: each fighter's REAL collider shape stood at the pose lag compensation rewinds it to
	/// under the current claim (read from the same resolver + history ring the sweep uses), with a line
	/// from the live body to it.</item>
	/// <item>Magenta: the server's recorded sweep segments of every ability object (what a hit claim is
	/// checked against); dim magenta for an ended object still held for late claims.</item>
	/// <item>Orange: each ability object's sweep shape at its live pose.</item>
	/// <item>Red: a damage event, attacker to defender, with a ring on the defender (0.6 s).</item>
	/// </list>
	/// </remarks>
	public sealed partial class CombatSimBootstrap
	{
		private struct CaptureHit
		{
			public Vector3 From;
			public Vector3 To;
			public float At;
		}

		private const float CaptureHitSeconds = 0.6f;
		private readonly List<CaptureHit> captureHits = new List<CaptureHit>();
		private static FieldInfo recordedSweepsField;
		private static FieldInfo sweepShapeField;
		private static FieldInfo sweepFrom, sweepTo, sweepValid;

		/// <summary>Sweep segments drawn on the most recent frame (report statistic).</summary>
		public int CaptureSweepsDrawn { get; private set; }
		public int CaptureAbilityObjectsSeen { get; private set; }
		public int CaptureRewoundPosesDrawn { get; private set; }
		public long CaptureMaxAbilityObjects { get; private set; }
		public long TotalDamage => totalDamage;
		public long BuffRemoves => buffRemoves;

		private void NoteCaptureHit(ICharacter attacker, ICharacter defender)
		{
			if (defender?.Transform == null)
			{
				return;
			}
			Vector3 to = defender.Transform.position + Vector3.up * 1f;
			Vector3 from = attacker?.Transform != null ? attacker.Transform.position + Vector3.up * 1.2f : to;
			captureHits.Add(new CaptureHit { From = from, To = to, At = Time.time });
		}

		/// <summary>Hooks this sim's extra shapes into <paramref name="overlay"/>.</summary>
		public void AttachCaptureOverlay(ColliderOverlay overlay)
		{
			overlay.Classify = collider =>
				collider.GetComponentInParent<AbilityObject>() != null ? (Color?)null : ColliderOverlay.DefaultClassify(collider);
			overlay.ExtraShapes += DrawCaptureShapes;
		}

		private void DrawCaptureShapes(ColliderOverlay overlay)
		{
			// Rewound poses.
			int rewound = 0;
			foreach (Fighter fighter in fighters)
			{
				if (!fighter.Alive || fighter.RewindGhost == null || !fighter.RewindGhost.activeSelf
					|| fighter.Character?.Collider == null || fighter.Character.Transform == null)
				{
					continue;
				}
				Transform root = fighter.Character.Transform;
				Vector3 rootPosition = fighter.RewindGhost.transform.position - Vector3.up * 1f;
				Quaternion rootRotation = fighter.RewindGhost.transform.rotation;
				overlay.AddColliderAtRootPose(fighter.Character.Collider, root, rootPosition, rootRotation, ColliderOverlay.RewindColor);
				overlay.AddLine(root.position + Vector3.up * 0.05f, rootPosition + Vector3.up * 0.05f, ColliderOverlay.RewindColor);
				rewound++;
			}
			CaptureRewoundPosesDrawn = rewound;

			// Ability objects: sweep shape (live) and the server's recorded sweeps.
			recordedSweepsField ??= typeof(AbilityObject).GetField("recordedSweeps", BindingFlags.Instance | BindingFlags.NonPublic);
			sweepShapeField ??= typeof(AbilityObject).GetField("sweepShape", BindingFlags.Instance | BindingFlags.NonPublic);
			int sweeps = 0;
			AbilityObject[] objects = FindObjectsByType<AbilityObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			int live = 0;
			foreach (AbilityObject abilityObject in objects)
			{
				if (abilityObject == null)
				{
					continue;
				}
				bool active = abilityObject.isActiveAndEnabled;
				if (active)
				{
					live++;
					Collider shape = sweepShapeField?.GetValue(abilityObject) as Collider;
					if (shape == null)
					{
						shape = abilityObject.GetComponentInChildren<Collider>(true);
					}
					if (shape != null)
					{
						overlay.AddCollider(shape, ColliderOverlay.AttackColor);
					}
				}
				if (recordedSweepsField?.GetValue(abilityObject) is Array ring)
				{
					Color color = active ? ColliderOverlay.SweepColor : new Color(1f, 0.25f, 0.9f, 0.35f);
					foreach (object boxed in ring)
					{
						if (boxed == null)
						{
							continue;
						}
						Type t = boxed.GetType();
						sweepFrom ??= t.GetField("From");
						sweepTo ??= t.GetField("To");
						sweepValid ??= t.GetField("Valid");
						if (!(bool)sweepValid.GetValue(boxed))
						{
							continue;
						}
						Vector3 from = (Vector3)sweepFrom.GetValue(boxed);
						Vector3 to = (Vector3)sweepTo.GetValue(boxed);
						overlay.AddLine(from, to, color);
						overlay.AddCircle(to, Vector3.up, 0.08f, color);
						sweeps++;
					}
				}
			}
			CaptureSweepsDrawn = sweeps;
			CaptureAbilityObjectsSeen = live;
			if (live > CaptureMaxAbilityObjects)
			{
				CaptureMaxAbilityObjects = live;
			}

			// Damage events.
			for (int i = captureHits.Count - 1; i >= 0; --i)
			{
				CaptureHit hit = captureHits[i];
				float age = Time.time - hit.At;
				if (age > CaptureHitSeconds)
				{
					captureHits.RemoveAt(i);
					continue;
				}
				Color color = ColliderOverlay.HitColor;
				color.a = 1f - age / CaptureHitSeconds;
				overlay.AddLine(hit.From, hit.To, color);
				overlay.AddCircle(hit.To, Vector3.up, 0.6f + age, color);
				overlay.AddCircle(hit.To, Vector3.right, 0.6f + age, color);
			}
		}

		/// <summary>Centroid of the living fighters (the probe moves them), or null before any exist.</summary>
		public Vector3? CaptureFocus()
		{
			Vector3 sum = Vector3.zero;
			int n = 0;
			foreach (Fighter fighter in fighters)
			{
				if (fighter.Alive && fighter.Character?.Transform != null)
				{
					sum += fighter.Character.Transform.position;
					n++;
				}
			}
			return n > 0 ? sum / n + Vector3.up : (Vector3?)null;
		}

		/// <summary>The caption burned into each captured frame.</summary>
		public string CaptureCaption(string title)
		{
			StringBuilder sb = new StringBuilder(512);
			sb.Append("<b>").Append(title).Append("</b>\n");
			TimeManager tm = NetTime;
			string claim = "";
			if (tm != null)
			{
				LagCompensationTick.ResolveViewOffset(ClaimMs, tm.TickDelta, out byte ticks, out byte fraction);
				claim = $" = {ticks} ticks + {fraction}/256";
			}
			sb.Append($"latency claim <b>{ClaimMs} ms</b>{claim}   t={Time.time - startClock:F1}s\n");
			sb.Append($"casts {castsStarted}  damage {damageEvents} ({totalDamage})  heals {healEvents}  buffs +{buffAdds}/-{buffRemoves}  kills {kills}\n");
			sb.Append($"claims consulted {claimsConsulted}  rewinds resolved {rewindsResolved}  ability objects {CaptureAbilityObjectsSeen}  sweeps drawn {CaptureSweepsDrawn}\n");
			foreach (Fighter fighter in fighters)
			{
				if (!fighter.Alive)
				{
					continue;
				}
				CharacterResourceAttribute health = fighter.Damage?.ResourceInstance;
				string hp = health != null ? $"{Mathf.CeilToInt(health.CurrentValue)}/{health.FinalValue}" : "-";
				sb.Append($"  {fighter.Npc.name}: {hp} HP {(string.IsNullOrEmpty(fighter.LastCast) ? "" : "· " + fighter.LastCast)}\n");
			}
			sb.Append("<color=#33ff4d>green</color> character colliders  <color=#ffee26>yellow</color> rewound pose  " +
				"<color=#ff8c1a>orange</color> attack shape  <color=#ff40e6>magenta</color> server sweeps  <color=#ff2626>red</color> hit");
			return sb.ToString();
		}
	}
}
