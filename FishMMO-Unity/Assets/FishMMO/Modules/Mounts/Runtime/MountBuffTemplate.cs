using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A mount (issue #109): riding it IS this buff. While it lasts the character moves as <see cref="Motion"/> says and every
	/// client draws <see cref="Body"/> under it with the rider astride; removing it (clicking it off the buff strip, a hit,
	/// casting, deep water, a roof overhead, the saddle coming off) dismounts.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing of its own on the wire.</b> The buff set is already reconciled to the owner and sent to observers, so the
	/// motor (through <see cref="MountController"/>, the core's <see cref="ICharacterMount"/>) and every client's drawing read
	/// it from there.
	/// </para>
	/// <para>
	/// <b>Lasts until removed</b> (<see cref="BaseBuffTemplate.LastsUntilRemoved"/>): saved with the character, so a rider
	/// logs back in mounted, and cleared by a death like any buff. It ends when its carrier acts
	/// (<see cref="BaseBuffTemplate.BreaksOnAction"/>, set by the content builder), which is the "casting dismounts" rule.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Mount", menuName = "FishMMO/Character/Buff/Mount", order = 10)]
	public class MountBuffTemplate : BaseBuffTemplate
	{
		[Header("Motion")]
		[Tooltip("Multiplies the rider's run and sprint speed.")]
		[Min(0.1f)]
		public float GroundSpeedMultiplier = 1.5f;

		[Tooltip("Multiplies how high a jump goes (a frog's leap).")]
		[Min(0f)]
		public float JumpHeightMultiplier = 1f;

		[Tooltip("Metres per second a jump adds along the steering (a frog's long leap); 0 for none.")]
		[Min(0f)]
		public float LeapSpeed;

		[Tooltip("The steepest slope it walks up as ground, degrees; 0 keeps the rider's own (a llama climbs steeper).")]
		[Range(0f, 85f)]
		public float MaxSlopeDegrees;

		[Tooltip("Flies whenever it is off stable ground: Jump takes off, held Jump climbs, Crouch descends.")]
		public bool Flies;

		[Tooltip("Flying speed, metres per second.")]
		[Min(0f)]
		public float FlySpeed = 15f;

		[Tooltip("Sprinting in flight multiplies the speed by this (at twice the stamina).")]
		[Min(1f)]
		public float FlySprintMultiplier = 2f;

		[Tooltip("Stamina a second of flight costs; with none left it glides down.")]
		[Min(0f)]
		public float FlightStaminaPerSecond = 3f;

		[Tooltip("How fast it sinks gliding, metres per second.")]
		[Min(0.5f)]
		public float GlideSinkSpeed = 3f;

		[Tooltip("The share of its flying speed a glide keeps going forward.")]
		[Range(0f, 1f)]
		public float GlideSpeedShare = 0.6f;

		[Header("Body")]
		[Tooltip("The procedural body every client draws under the rider (the wildlife bodies' builder).")]
		public WildlifeBodySpec Body = WildlifeBodySpec.Deer;

		[Tooltip("The wildlife material (FishMMO/Wildlife shader): colours ride the mesh.")]
		public Material Material;

		[Tooltip("How far back of the body's middle the rider sits, metres (forward is negative).")]
		public float SaddleBack = 0.05f;

		[Tooltip("The walk-to-gallop speed the body's legs are keyed against, metres per second.")]
		[Min(0.5f)]
		public float GaitRunSpeed = 9f;

		/// <inheritdoc />
		public override bool LastsUntilRemoved => true;

		/// <summary>How the rider moves on it (read by the motor every tick).</summary>
		public MountMotion Motion => new MountMotion
		{
			GroundSpeedMultiplier = GroundSpeedMultiplier,
			JumpHeightMultiplier = JumpHeightMultiplier,
			LeapSpeed = LeapSpeed,
			MaxSlopeDegrees = MaxSlopeDegrees,
			Flies = Flies,
			FlySpeed = FlySpeed,
			FlySprintMultiplier = FlySprintMultiplier,
			FlightStaminaPerSecond = FlightStaminaPerSecond,
			GlideSinkSpeed = GlideSinkSpeed,
			GlideSpeedShare = GlideSpeedShare,
		};

		/// <summary>
		/// Where the rider's hip joints sit over the ground, metres: on top of the back (legs and the torso's depth) and a
		/// saddle's thickness more.
		/// </summary>
		public float SaddleHeight => MountRules.SaddleHeight(Body);

		/// <inheritdoc />
		public override void SecondaryTooltip(TooltipContent content)
		{
			foreach ((string label, string value) in MountRules.Describe(this))
			{
				content.AddStat(label, value, TooltipPriority.Effects, tone: TooltipTone.Good);
			}
		}

		/// <summary>Nothing to do: everything a mount changes is read from the buff's presence (see remarks).</summary>
		public override void OnApply(Buff buff, ICharacter target)
		{
		}

		/// <summary>Nothing to undo: see <see cref="OnApply"/>.</summary>
		public override void OnRemove(Buff buff, ICharacter target)
		{
		}

		/// <summary>A mount does not stack: riding it again is the same ride.</summary>
		public override void OnApplyStack(Buff buff, ICharacter target)
		{
		}

		/// <inheritdoc cref="OnApplyStack"/>
		public override void OnRemoveStack(Buff buff, ICharacter target)
		{
		}
	}
}
