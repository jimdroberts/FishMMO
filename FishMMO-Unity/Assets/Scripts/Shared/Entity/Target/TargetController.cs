using UnityEngine;
using System;

namespace FishMMO.Shared
{
	public class TargetController : CharacterBehaviour, ITargetController
	{
		public const float MAX_TARGET_DISTANCE = 50.0f;
		public const float TARGET_UPDATE_RATE = 0.05f;

		public LayerMask LayerMask;
		public TargetInfo Last;
		public TargetInfo Current { get; private set; }

		public event Action<GameObject> OnChangeTarget;
		public event Action<GameObject> OnUpdateTarget;

		public event Action<Transform> OnClearTarget;
		public event Action<Transform> OnNewTarget;

#if !UNITY_SERVER
		private float nextTick = 0.0f;

		public override void OnDestroying()
		{
			OnChangeTarget = null;
			OnUpdateTarget = null;
			Last = default;
			Current = default;
		}

		void Update()
		{
			if (Camera.main == null)
			{
				return;
			}

			// update target label for the client
			if (nextTick < 0.0f)
			{
				nextTick = TARGET_UPDATE_RATE;

				Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
				//Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0));

				UpdateTarget(ray.origin, ray.direction, MAX_TARGET_DISTANCE);

				// target has changed
				if (Current.Target != Last.Target)
				{
					// invoke our change target function
					if (Current.Target == null)
					{
						OnChangeTarget?.Invoke(null);
					}
					else
					{
						OnChangeTarget?.Invoke(Current.Target.gameObject);
					}

					// disable the previous outline and target label
					if (Last.Target != null)
					{
						OnClearTarget?.Invoke(Last.Target);
					}

					// construct or enable the labels and outlines
					if (Current.Target != null)
					{
						OnNewTarget?.Invoke(Current.Target);
					}
				}
				else
				{
					// invoke our update function
					if (Current.Target != null)
					{
						OnUpdateTarget?.Invoke(Current.Target.gameObject);
					}
				}
			}
			nextTick -= Time.deltaTime;
		}
#endif
		
		/// <summary>
		/// Updates and returns the TargetInfo for the Current target.
		/// </summary>
		public TargetInfo UpdateTarget(Vector3 origin, Vector3 direction, float maxDistance)
		{
			Last = Current;

			float distance = maxDistance.Clamp(0.0f, MAX_TARGET_DISTANCE);
			RaycastHit hit;
#if !UNITY_SERVER
			Ray ray = new Ray(origin, direction);
			if (Physics.Raycast(ray, out hit, distance, LayerMask))
#else
			if (Character.Motor.PhysicsScene.Raycast(origin, direction, out hit, distance, LayerMask))
#endif
			{
				//Debug.DrawLine(ray.origin, hit.point, Color.red, 1);
				//Debug.Log("hit: " + hit.transform.name + " pos: " + hit.point);
				Current = new TargetInfo(hit.transform, hit.point);
			}
			else
			{
#if UNITY_SERVER
				Ray ray = new Ray(origin, direction);
#endif
				Current = new TargetInfo(null, ray.GetPoint(distance));
			}
			return Current;
		}
	}
}