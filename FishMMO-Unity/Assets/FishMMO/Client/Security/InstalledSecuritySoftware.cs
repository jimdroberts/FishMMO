using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// The antivirus products Windows Security Center reports as installed and switched on.
	/// </summary>
	/// <remarks>
	/// Used only to point a player whose connection is being blocked at the right help section.
	/// Nothing is sent anywhere.
	/// <para>
	/// Windows desktop only: browsers have no way to list installed software, Linux has no
	/// equivalent registry, and Windows Server has no Security Center. Everywhere else the list
	/// stays empty and the help page asks the player to pick their product.
	/// </para>
	/// <para>
	/// Read through the documented product-list API (wscapi, Windows 8 and later) with direct
	/// vtable calls rather than WMI or COM interop, so it needs neither System.Management nor
	/// runtime-callable-wrapper support. Both interfaces derive from IDispatch, so their own
	/// methods start at slot 7. The online reference pages say IUnknown, but the SDK header,
	/// the MinGW header and the Windows metadata agree on IDispatch.
	/// </para>
	/// <para>
	/// The query runs once, on its own thread with its own COM apartment, and is started early
	/// so the answer is ready before any connection fails. A query that has not finished yet
	/// simply reads as empty.
	/// </para>
	/// </remarks>
	public static class InstalledSecuritySoftware
	{
		/// <summary>Names reported as installed and on. Empty until the query finishes, and on any failure.</summary>
		private static volatile string[] names = Array.Empty<string>();

		/// <summary>1 once the query has been started.</summary>
		private static int started;

		/// <summary>The antivirus products Security Center reports as on. Empty when unknown.</summary>
		public static IReadOnlyList<string> AntivirusNames => names;

		/// <summary>
		/// Starts the one-time query in the background. Safe to call more than once and on any platform.
		/// </summary>
		public static void BeginDetect()
		{
			if (Interlocked.Exchange(ref started, 1) != 0)
			{
				return;
			}
#if (UNITY_STANDALONE_WIN && !UNITY_EDITOR) || UNITY_EDITOR_WIN
			try
			{
				var thread = new Thread(Query) { IsBackground = true, Name = "SecurityCenterQuery" };
				thread.Start();
			}
			catch (Exception ex)
			{
				Log.Warning("InstalledSecuritySoftware", $"Could not start the Security Center query: {ex.Message}");
			}
#endif
		}

#if (UNITY_STANDALONE_WIN && !UNITY_EDITOR) || UNITY_EDITOR_WIN
		private static readonly Guid ClsidWscProductList = new Guid("17072F7B-9ABE-4A74-A261-1EB76B55107A");
		private static readonly Guid IidWscProductList = new Guid("722A338C-6E8E-4E72-AC27-1417FB0C81C2");

		private const uint ClsctxInprocServer = 0x1;
		private const uint CoinitApartmentThreaded = 0x2;
		private const uint WscSecurityProviderAntivirus = 0x4;
		private const int WscSecurityProductStateOn = 0;

		// Vtable slots: IUnknown 0-2, IDispatch 3-6, then each interface's own methods.
		private const int SlotRelease = 2;
		private const int SlotListInitialize = 7;
		private const int SlotListGetCount = 8;
		private const int SlotListGetItem = 9;
		private const int SlotProductGetName = 7;
		private const int SlotProductGetState = 8;

		[DllImport("ole32.dll")]
		private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

		[DllImport("ole32.dll")]
		private static extern void CoUninitialize();

		[DllImport("ole32.dll")]
		private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate uint ReleaseFn(IntPtr self);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int InitializeFn(IntPtr self, uint provider);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int GetCountFn(IntPtr self, out int count);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int GetItemFn(IntPtr self, uint index, out IntPtr product);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int GetBstrFn(IntPtr self, out IntPtr bstr);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int GetIntFn(IntPtr self, out int value);

		/// <summary>The function at <paramref name="slot"/> of a COM object's vtable, as a delegate.</summary>
		private static T Method<T>(IntPtr instance, int slot) where T : Delegate
		{
			IntPtr vtable = Marshal.ReadIntPtr(instance);
			IntPtr function = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
			return Marshal.GetDelegateForFunctionPointer<T>(function);
		}

		private static void Release(IntPtr instance)
		{
			if (instance != IntPtr.Zero)
			{
				Method<ReleaseFn>(instance, SlotRelease)(instance);
			}
		}

		private static void Query()
		{
			// S_OK or S_FALSE: this thread now has an apartment it must leave. Anything else: it has none.
			int init = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);
			bool uninitialize = init >= 0;
			IntPtr list = IntPtr.Zero;
			try
			{
				Guid clsid = ClsidWscProductList;
				Guid iid = IidWscProductList;
				int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iid, out list);
				if (hr < 0 || list == IntPtr.Zero)
				{
					// Windows Server, or Security Center missing: nothing to report.
					Log.Debug("InstalledSecuritySoftware", $"Security Center unavailable (0x{hr:X8}).");
					return;
				}

				hr = Method<InitializeFn>(list, SlotListInitialize)(list, WscSecurityProviderAntivirus);
				if (hr < 0)
				{
					Log.Debug("InstalledSecuritySoftware", $"Security Center product list refused to initialize (0x{hr:X8}).");
					return;
				}

				hr = Method<GetCountFn>(list, SlotListGetCount)(list, out int count);
				if (hr < 0 || count <= 0)
				{
					return;
				}

				var found = new List<string>(count);
				for (uint i = 0; i < (uint)count && i < 16; i++)
				{
					if (Method<GetItemFn>(list, SlotListGetItem)(list, i, out IntPtr product) < 0 || product == IntPtr.Zero)
					{
						continue;
					}
					try
					{
						if (Method<GetIntFn>(product, SlotProductGetState)(product, out int state) < 0 ||
							state != WscSecurityProductStateOn)
						{
							continue;
						}
						if (Method<GetBstrFn>(product, SlotProductGetName)(product, out IntPtr bstr) < 0 || bstr == IntPtr.Zero)
						{
							continue;
						}
						try
						{
							string name = Marshal.PtrToStringBSTR(bstr);
							if (!string.IsNullOrWhiteSpace(name))
							{
								found.Add(name.Trim());
							}
						}
						finally
						{
							Marshal.FreeBSTR(bstr);
						}
					}
					finally
					{
						Release(product);
					}
				}

				names = found.ToArray();
				if (found.Count > 0)
				{
					Log.Info("InstalledSecuritySoftware", "Antivirus reported by Windows: " + string.Join(", ", found));
				}
			}
			catch (Exception ex)
			{
				// Diagnosis is a nicety; it must never take the client down.
				Log.Warning("InstalledSecuritySoftware", $"Security Center query failed: {ex.Message}");
			}
			finally
			{
				try { Release(list); } catch { /* best effort */ }
				if (uninitialize)
				{
					CoUninitialize();
				}
			}
		}
#endif
	}
}
