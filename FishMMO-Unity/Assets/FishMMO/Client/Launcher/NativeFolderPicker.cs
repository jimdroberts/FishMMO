using System;
using System.Runtime.InteropServices;
using System.Text;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Opens the operating system's "choose a folder" dialog.
	/// </summary>
	/// <remarks>
	/// Windows only. Unity exposes no runtime folder picker — <c>EditorUtility.OpenFolderPanel</c>
	/// exists solely in the Editor — so this is a direct shell call, and the other platforms the
	/// launcher supports would each need their own backend (GTK/portal on Linux, Cocoa on macOS).
	/// <para>
	/// <see cref="IsSupported"/> is false everywhere else and callers are expected to hide the
	/// browse affordance rather than offer a button that does nothing. The path text field
	/// remains the way to set a folder on every platform, so nothing is unreachable without
	/// this — it is a convenience over that field, not the only route to it.
	/// </para>
	/// </remarks>
	public static class NativeFolderPicker
	{
		/// <summary>
		/// True when a native folder dialog can be shown on this platform.
		/// </summary>
		public static bool IsSupported =>
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
			true;
#else
			false;
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
		/// <summary>Only return file system directories.</summary>
		private const uint BIF_RETURNONLYFSDIRS = 0x00000001;
		/// <summary>Use the newer dialog with an edit box and resize grip.</summary>
		private const uint BIF_NEWDIALOGSTYLE = 0x00000040;
		/// <summary>Maximum path length the shell will write back.</summary>
		private const int MAX_PATH = 260;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		private struct BROWSEINFO
		{
			public IntPtr hwndOwner;
			public IntPtr pidlRoot;
			public string pszDisplayName;
			public string lpszTitle;
			public uint ulFlags;
			public IntPtr lpfn;
			public IntPtr lParam;
			public int iImage;
		}

		[DllImport("shell32.dll", CharSet = CharSet.Auto)]
		private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

		[DllImport("shell32.dll", CharSet = CharSet.Auto)]
		private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

		/// <summary>
		/// Frees the PIDL the shell allocated for the selection.
		/// </summary>
		/// <remarks>
		/// SHBrowseForFolder returns shell-allocated memory that the caller owns. Letting it go
		/// leaks a little on every use of the dialog.
		/// </remarks>
		[DllImport("ole32.dll")]
		private static extern void CoTaskMemFree(IntPtr pv);

		[DllImport("user32.dll")]
		private static extern IntPtr GetActiveWindow();
#endif

		/// <summary>
		/// Shows the folder dialog and returns the chosen path, or null when the player
		/// cancelled or no dialog is available.
		/// </summary>
		/// <param name="title">Prompt shown above the folder tree.</param>
		/// <remarks>
		/// Every failure returns null rather than throwing. This is reached from a UI callback
		/// on the launcher — the screen that, if it breaks, leaves no way into the game — and a
		/// shell call that misbehaves must cost the convenience, not the launcher.
		/// </remarks>
		public static string PickFolder(string title)
		{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
			IntPtr pidl = IntPtr.Zero;
			try
			{
				BROWSEINFO info = new BROWSEINFO
				{
					// Parenting to the launcher window keeps the dialog modal to it, rather
					// than letting it open behind and look like a freeze.
					hwndOwner = GetActiveWindow(),
					pidlRoot = IntPtr.Zero,
					pszDisplayName = new string('\0', MAX_PATH),
					lpszTitle = title,
					ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
					lpfn = IntPtr.Zero,
					lParam = IntPtr.Zero,
					iImage = 0,
				};

				pidl = SHBrowseForFolder(ref info);
				if (pidl == IntPtr.Zero)
				{
					// Cancelled.
					return null;
				}

				StringBuilder path = new StringBuilder(MAX_PATH);
				if (!SHGetPathFromIDList(pidl, path))
				{
					// A virtual folder with no filesystem path (This PC, a library root).
					Log.Warning("NativeFolderPicker", "The selected item is not a filesystem folder.");
					return null;
				}

				string result = path.ToString();
				return string.IsNullOrWhiteSpace(result) ? null : result;
			}
			catch (Exception ex)
			{
				Log.Warning("NativeFolderPicker", $"Could not open the folder dialog: {ex.Message}. Type the path instead.");
				return null;
			}
			finally
			{
				if (pidl != IntPtr.Zero)
				{
					try { CoTaskMemFree(pidl); } catch { }
				}
			}
#else
			return null;
#endif
		}
	}
}
