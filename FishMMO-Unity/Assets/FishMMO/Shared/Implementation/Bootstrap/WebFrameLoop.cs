using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Caps the client's render rate without taking the web player off the browser's animation frames.
	/// </summary>
	/// <remarks>
	/// On the web, any <see cref="Application.targetFrameRate"/> but -1 makes Unity schedule every frame with setTimeout,
	/// out of step with the display; Firefox then presented the canvas out of order and the whole picture snapped back
	/// and forth (FrameLoop.jslib has the measurement). There the loop stays on animation frames
	/// (<c>targetFrameRate</c> -1) and the cap is applied as every Nth display frame. Only in Firefox: every other browser,
	/// and every other platform, uses <see cref="Application.targetFrameRate"/> as before, so it is not held to the display
	/// rate.
	/// </remarks>
	public static class WebFrameLoop
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		[DllImport("__Internal")]
		private static extern void FishMMOSetFrameCap(int framesPerSecond);

		[DllImport("__Internal")]
		private static extern int FishMMOFrameLoopOnAnimationFrames();

		private static int onAnimationFrames = -1;

		/// <summary>Whether this browser runs the loop on animation frames (Firefox only; asked once).</summary>
		private static bool OnAnimationFrames
		{
			get
			{
				if (onAnimationFrames < 0)
				{
					onAnimationFrames = FishMMOFrameLoopOnAnimationFrames();
				}
				return onAnimationFrames == 1;
			}
		}
#endif

		/// <summary>Caps the render rate at <paramref name="framesPerSecond"/> (on the web, at least that, in whole display frames).</summary>
		public static void SetCap(int framesPerSecond)
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			if (!OnAnimationFrames)
			{
				// Not Firefox: Unity's own loop, free of the display rate, capped only by the player's setting.
				Application.targetFrameRate = framesPerSecond;
				return;
			}
			Application.targetFrameRate = -1;
			FishMMOSetFrameCap(framesPerSecond);
#else
			Application.targetFrameRate = framesPerSecond;
#endif
		}
	}
}
