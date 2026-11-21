using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// Records a sim to a numbered JPEG sequence (for an animated WebP made afterwards by ffmpeg) with a
	/// caption burned in, from its own off-screen camera.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Output goes under the directory named by the <c>FISHMMO_SIM_CAPTURE</c> environment variable; with
	/// it unset <see cref="OutputRoot"/> is null and the capture tests skip. Nothing in the game references
	/// this — it exists so a sim run can be reviewed as video.
	/// </para>
	/// <para>
	/// Frames are taken on wall-clock intervals (the sims are tick-driven and catch up on their own),
	/// read back asynchronously, and encoded on the thread pool, so capture does not stall the frame it
	/// records. The camera renders once per frame like any other; nothing loops <c>Render()</c>.
	/// </para>
	/// </remarks>
	[DefaultExecutionOrder(32000)]
	public sealed class SimCapture : MonoBehaviour
	{
		public const string OutputVariable = "FISHMMO_SIM_CAPTURE";

		/// <summary>Root output directory, or null when capture is not requested.</summary>
		public static string OutputRoot
		{
			get
			{
				string root = Environment.GetEnvironmentVariable(OutputVariable);
				return string.IsNullOrWhiteSpace(root) ? null : root;
			}
		}

		public int Width = 1280;
		public int Height = 720;
		public float FramesPerSecond = 15f;

		/// <summary>Caption text, polled every captured frame.</summary>
		public Func<string> Caption;

		/// <summary>Where the camera looks, and from where (both may be moved at any time).</summary>
		public Vector3 Focus;
		public Vector3 EyeOffset = new Vector3(0f, 14f, -16f);
		public float FieldOfView = 45f;

		/// <summary>When set, the camera follows this point (smoothed) instead of <see cref="Focus"/>.</summary>
		public Func<Vector3?> FocusProvider;

		public Camera Camera { get; private set; }
		public string Directory { get; private set; }
		public int FramesWritten => framesWritten;
		public bool Recording { get; private set; }

		private RenderTexture target;
		private Text captionText;
		private float nextFrameAt;
		private int frameIndex;
		private int framesWritten;
		private int pending;

		/// <summary>Creates a capture rig writing <c>&lt;root&gt;/&lt;clip&gt;/f_00000.jpg</c>.</summary>
		public static SimCapture Create(string clip, Vector3 focus, Vector3 eyeOffset)
		{
			string root = OutputRoot;
			if (root == null)
			{
				return null;
			}
			GameObject go = new GameObject("SimCapture " + clip);
			SimCapture capture = go.AddComponent<SimCapture>();
			capture.Focus = focus;
			capture.EyeOffset = eyeOffset;
			capture.Directory = Path.Combine(root, clip);
			System.IO.Directory.CreateDirectory(capture.Directory);
			foreach (string old in System.IO.Directory.GetFiles(capture.Directory, "f_*.jpg"))
			{
				File.Delete(old);
			}
			capture.BuildRig();
			return capture;
		}

		private void BuildRig()
		{
			target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
			{
				name = "SimCapture",
				antiAliasing = 1,
			};
			target.Create();

			GameObject cameraGo = new GameObject("SimCapture Camera");
			cameraGo.transform.SetParent(transform, false);
			cameraGo.tag = "MainCamera";
			Camera = cameraGo.AddComponent<Camera>();
			Camera.targetTexture = target;
			Camera.clearFlags = CameraClearFlags.SolidColor;
			Camera.backgroundColor = new Color(0.09f, 0.1f, 0.13f);
			Camera.fieldOfView = FieldOfView;
			Camera.nearClipPlane = 0.1f;
			Camera.farClipPlane = 500f;
			// Left enabled so Camera.main finds it (the sims turn their labels toward Camera.main); CaptureFrame
			// still renders by hand, since a batch-mode editor does not reliably render cameras itself.
			Aim();

			// Caption: a screen-space-camera canvas so it is part of the rendered frame.
			GameObject canvasGo = new GameObject("SimCapture Caption");
			canvasGo.transform.SetParent(transform, false);
			Canvas canvas = canvasGo.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceCamera;
			canvas.worldCamera = Camera;
			canvas.planeDistance = 1f;
			CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(Width, Height);

			GameObject panel = new GameObject("Backdrop");
			panel.transform.SetParent(canvasGo.transform, false);
			Image backdrop = panel.AddComponent<Image>();
			backdrop.color = new Color(0f, 0f, 0f, 0.55f);
			RectTransform panelRect = panel.GetComponent<RectTransform>();
			panelRect.anchorMin = new Vector2(0f, 1f);
			panelRect.anchorMax = new Vector2(0f, 1f);
			panelRect.pivot = new Vector2(0f, 1f);
			panelRect.anchoredPosition = new Vector2(8f, -8f);
			panelRect.sizeDelta = new Vector2(Width * 0.62f, 10f);
			ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
			layout.padding = new RectOffset(10, 10, 6, 6);
			layout.childControlHeight = true;
			layout.childControlWidth = true;

			GameObject textGo = new GameObject("Text");
			textGo.transform.SetParent(panel.transform, false);
			captionText = textGo.AddComponent<Text>();
			captionText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			captionText.fontSize = 15;
			captionText.color = Color.white;
			captionText.supportRichText = true;
			captionText.horizontalOverflow = HorizontalWrapMode.Wrap;
			captionText.verticalOverflow = VerticalWrapMode.Overflow;
		}

		private void Aim()
		{
			if (Camera == null)
			{
				return;
			}
			Vector3? follow = FocusProvider?.Invoke();
			if (follow.HasValue)
			{
				Focus = Vector3.Lerp(Focus, follow.Value, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
			}
			Camera.transform.position = Focus + EyeOffset;
			Camera.transform.rotation = Quaternion.LookRotation(Focus - Camera.transform.position);
		}

		public void StartRecording()
		{
			Recording = true;
			nextFrameAt = Time.realtimeSinceStartup;
			// Frames are taken in LateUpdate (CaptureFrame).
		}

		public void StopRecording() => Recording = false;

		/// <summary>Blocks until every queued readback and encode has landed on disk.</summary>
		public void Flush()
		{
			AsyncGPUReadback.WaitAllRequests();
			float deadline = Time.realtimeSinceStartup + 30f;
			while (Volatile.Read(ref pending) > 0 && Time.realtimeSinceStartup < deadline)
			{
				Thread.Sleep(10);
			}
		}

		private void LateUpdate()
		{
			Aim();
			if (captionText != null && Caption != null)
			{
				try
				{
					captionText.text = Caption();
				}
				catch (Exception e)
				{
					captionText.text = "caption failed: " + e.Message;
				}
			}
			CaptureFrame();
		}

		/// <summary>
		/// Renders and reads back one frame when one is due. Driven from LateUpdate because a batch-mode editor never
		/// resumes <c>WaitForEndOfFrame</c>.
		/// </summary>
		private void CaptureFrame()
		{
			{
				if (!Recording || Time.realtimeSinceStartup < nextFrameAt)
				{
					return;
				}
				nextFrameAt += 1f / FramesPerSecond;
				if (nextFrameAt < Time.realtimeSinceStartup)
				{
					// Fell behind (a slow frame): resync rather than burst-capture duplicates.
					nextFrameAt = Time.realtimeSinceStartup + 1f / FramesPerSecond;
				}
				/* Rendered here, by hand, once: a batch-mode editor does not reliably render cameras on its own,
				 * and this runs last in LateUpdate so the overlay mesh and caption are this frame's. */
				Canvas.ForceUpdateCanvases();
				Camera.Render();
				string path = Path.Combine(Directory, $"f_{frameIndex++:D5}.jpg");
				int width = target.width;
				int height = target.height;
				Interlocked.Increment(ref pending);
				if (!SystemInfo.supportsAsyncGPUReadback)
				{
					// Software GL under xvfb, say: a blocking read of this one frame.
					readback ??= new Texture2D(width, height, TextureFormat.RGBA32, false);
					RenderTexture previous = RenderTexture.active;
					RenderTexture.active = target;
					readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
					RenderTexture.active = previous;
					Encode(readback.GetRawTextureData(), path, width, height);
					return;
				}
				AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
				{
					if (request.hasError)
					{
						Interlocked.Decrement(ref pending);
						return;
					}
					Encode(request.GetData<byte>().ToArray(), path, width, height);
				});
			}
		}

		private Texture2D readback;

		/// <summary>Encodes and writes one frame on the thread pool; balances the pending count.</summary>
		private void Encode(byte[] pixels, string path, int width, int height)
		{
					ThreadPool.QueueUserWorkItem(_ =>
					{
						try
						{
							byte[] jpg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB,
								(uint)width, (uint)height, 0, 92);
							File.WriteAllBytes(path, jpg);
							Interlocked.Increment(ref framesWritten);
						}
						catch (Exception e)
						{
							Debug.LogWarning("[SimCapture] frame encode failed: " + e.Message);
						}
						finally
						{
							Interlocked.Decrement(ref pending);
						}
					});
		}

		private void OnDestroy()
		{
			Recording = false;
			if (target != null)
			{
				if (Camera != null) Camera.targetTexture = null;
				target.Release();
				Destroy(target);
			}
			if (readback != null)
			{
				Destroy(readback);
			}
		}
	}
}
