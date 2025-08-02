// FishNetworking MovingPlatform being converted to FishMMO KCCPlatform

using System.Collections.Generic;
using FishNet.Object.Prediction;
using FishNet.Component.Prediction;
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

namespace FishMMO.Shared
{
	public class KCCPlatform : TickNetworkBehaviour, ISceneObject
	{
		#region Types.
		public struct ReplicateData : IReplicateData
		{
			public ReplicateData(uint unused = 0)
			{
				_tick = 0;
			}

			/// <summary>
			/// Tick is set at runtime. There is no need to manually assign this value.
			/// </summary>
			private uint _tick;
			public void Dispose() { }
			public uint GetTick() => _tick;
			public void SetTick(uint value) => _tick = value;
		}

		public struct ReconcileData : IReconcileData
		{
			public ReconcileData(Vector3 position, byte goalIndex)
			{
				Position = position;
				GoalIndex = goalIndex;
				_tick = 0;
			}

			/// <summary>
			/// Position of the character.
			/// </summary>
			public Vector3 Position;
			/// <summary>
			/// Current vertical velocity.
			/// </summary>
			/// <remarks>Used to simulate jumps and falls.</remarks>
			public byte GoalIndex;
			/// <summary>
			/// Tick is set at runtime. There is no need to manually assign this value.
			/// </summary>
			private uint _tick;
			public void Dispose() { }
			public uint GetTick() => _tick;
			public void SetTick(uint value) => _tick = value;
		}
		#endregion

		[SerializeField]
		private float _moveRate = 4f;
		/// <summary>
		/// Goal to move towards.
		/// </summary>
		private byte _goalIndex;
		/// <summary>
		/// Goals to move towards.
		/// </summary>
		private List<Vector3> _goals = new();

		public NetworkCollision _platformCollider;

		public long ID { get; set; }
		public GameObject GameObject { get; private set; }

		private void Awake()
		{
			const float offset = 5f;
			Vector3 position = transform.position;
			_goals.Add(position + new Vector3(0f, 0f, offset));
			_goals.Add(position + new Vector3(0f, 0f, -offset));

			_platformCollider = GetComponent<NetworkCollision>();
			if (_platformCollider != null)
			{
				_platformCollider.OnEnter += OnTriggerEnter;
				_platformCollider.OnExit += OnTriggerExit;
			}

#if UNITY_SERVER
			SceneObject.Register(this);
#endif
		}

		void OnDestroy()
		{
			SceneObject.Unregister(this);
		}

		private void OnTriggerEnter(Collider other)
		{
			if (other.TryGetComponent(out KCCPlayer player))
			{
				player.SetPlatform(this);
			}
		}

		private void OnTriggerExit(Collider other)
		{
			if (other.TryGetComponent(out KCCPlayer player))
			{
				player.SetPlatform(null);
			}
		}

		public override void OnStartNetwork()
		{
			SetTickCallbacks(TickCallback.Tick);
		}

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			SceneObject.Register(this, true);
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
		}

		protected override void TimeManager_OnTick()
		{
			PerformReplicate(default);
			CreateReconcile();
		}

		public override void CreateReconcile()
		{
			ReconcileData rd = new(transform.position, _goalIndex);
			PerformReconcile(rd);
		}

		[Replicate]
		private void PerformReplicate(ReplicateData rd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			float delta = (float)TimeManager.TickDelta;

			Vector3 goal = _goals[_goalIndex];
			Vector3 next = Vector3.MoveTowards(transform.position, goal, delta * _moveRate);

			transform.position = next;

			if (next == goal)
			{
				_goalIndex++;
				if (_goalIndex >= _goals.Count)
					_goalIndex = 0;
			}
		}

		[Reconcile]
		private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
		{
			transform.position = rd.Position;
			_goalIndex = rd.GoalIndex;
		}
	}
}