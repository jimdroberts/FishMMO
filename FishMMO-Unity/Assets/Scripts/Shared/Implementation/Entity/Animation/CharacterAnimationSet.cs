using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The clips a character model is animated with: what <see cref="CharacterBody"/> blends for standing, walking,
	/// running, the air, crouching, the water, climbing, sitting, fighting and dying. Humanoid clips, so one set fits any
	/// humanoid model; a model names its set on its <see cref="CharacterModel"/>.
	/// </summary>
	/// <remarks>
	/// Only <see cref="Idle"/> is required. A missing clip is stood in for by the nearest there is (a crouch by standing,
	/// a swim or a climb by walking, the air by standing), and a missing combat clip simply does not play.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Character Animation Set", menuName = "FishMMO/Character/Animation Set", order = 0)]
	public class CharacterAnimationSet : ScriptableObject
	{
		[Header("On the ground")]
		public AnimationClip Idle;
		public AnimationClip Walk;
		public AnimationClip Run;
		public AnimationClip CrouchIdle;
		public AnimationClip CrouchWalk;

		[Header("In the air")]
		[Tooltip("Rising, from a jump.")]
		public AnimationClip JumpUp;
		[Tooltip("At the top of a jump or a fall.")]
		public AnimationClip MidAir;
		[Tooltip("Coming down.")]
		public AnimationClip Fall;

		[Header("In the water")]
		[Tooltip("Treading water, upright, still.")]
		public AnimationClip Tread;
		[Tooltip("Swimming at the surface, head up, keyed for SwimPose.SurfaceLean.")]
		public AnimationClip SwimSurface;
		[Tooltip("Swimming under the surface, flat along the swim.")]
		public AnimationClip SwimUnder;
		[Tooltip("The swim speed both strokes were keyed for, m/s.")]
		public float StrokePace = 2.0f;

		[Header("Climbing")]
		public AnimationClip Climb;
		[Tooltip("Metres climbed per climb cycle.")]
		public float ClimbCycleMetres = 1.2f;

		[Header("Fighting (arms only, over whatever the legs do)")]
		public AnimationClip PunchRight;
		public AnimationClip PunchLeft;
		public AnimationClip Cast;
		[Tooltip("The guard, held while blocking.")]
		public AnimationClip Block;

		[Header("Whole body")]
		public AnimationClip Roll;
		[Tooltip("Played once; its last pose is held while dead.")]
		public AnimationClip Death;

		[Header("Sitting")]
		public AnimationClip SitGround;
		public AnimationClip SitSeat;
		[Tooltip("The seat height SitSeat was keyed for, metres, on a figure KeyedHeight tall.")]
		public float SeatHeight = 0.46f;
		[Tooltip("How tall the figure the clips were keyed on stood, metres (the seat height scales with the model's height).")]
		public float KeyedHeight = 1.8f;
	}
}
