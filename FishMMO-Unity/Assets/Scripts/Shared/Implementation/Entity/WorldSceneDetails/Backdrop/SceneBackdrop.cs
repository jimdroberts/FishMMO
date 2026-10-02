using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The terrain around a generated scene, out to the horizon: drawn, never walked on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The scene generator builds it from the same planet the scene was cut from, at the same
	/// radius and vertical scale, so the ground carries on past the scene's edge exactly as the
	/// globe says it does. It has no colliders and no gameplay; the scene's boundary still decides
	/// where anybody can go. Client-only (see <see cref="ClientOnlyObject"/>).
	/// </para>
	/// <para>
	/// <b>It also owns how far the camera sees.</b> The client's camera draws to a fixed distance
	/// (1000 m in ClientPreboot), which hides a backdrop reaching ten kilometres, and hides most of
	/// a six-kilometre scene too. While a backdrop is loaded the main camera's far plane is raised
	/// to reach its far edge from anywhere in the scene, and put back when the last one unloads.
	/// </para>
	/// </remarks>
	public class SceneBackdrop : ClientOnlyObject
	{
		[Tooltip("How far the camera must see to reach this backdrop's far edge from anywhere in the scene, in metres.")]
		public float FarPlaneMetres = 20000f;

		[Tooltip("How far the backdrop reaches past the scene's edge, in metres.")]
		public float ReachMetres;

		[Tooltip("The scene's own size in metres (X, Z), which the backdrop surrounds.")]
		public Vector2 SceneSizeMetres;

#if !UNITY_SERVER
		private static readonly List<SceneBackdrop> active = new List<SceneBackdrop>();
		private static Camera raisedCamera;
		private static float originalFarPlane;

		private void OnEnable()
		{
			if (!active.Contains(this))
			{
				active.Add(this);
			}
		}

		private void OnDisable()
		{
			active.Remove(this);
			if (active.Count == 0)
			{
				Restore();
			}
		}

		/// <summary>
		/// Checked every frame rather than once, because the camera a scene loads under is not the
		/// camera it is played under: the client's camera outlives scenes, and something else may
		/// set its far plane after this loads.
		/// </summary>
		private void LateUpdate()
		{
			if (active.Count == 0 || active[0] != this)
			{
				return;
			}
			Camera camera = Camera.main;
			if (camera == null)
			{
				return;
			}
			if (camera != raisedCamera)
			{
				Restore();
				raisedCamera = camera;
				originalFarPlane = camera.farClipPlane;
			}

			float wanted = originalFarPlane;
			foreach (SceneBackdrop backdrop in active)
			{
				wanted = Mathf.Max(wanted, backdrop.FarPlaneMetres);
			}
			if (!Mathf.Approximately(camera.farClipPlane, wanted))
			{
				camera.farClipPlane = wanted;
			}
		}

		private static void Restore()
		{
			if (raisedCamera != null)
			{
				raisedCamera.farClipPlane = originalFarPlane;
			}
			raisedCamera = null;
		}
#endif
	}
}
