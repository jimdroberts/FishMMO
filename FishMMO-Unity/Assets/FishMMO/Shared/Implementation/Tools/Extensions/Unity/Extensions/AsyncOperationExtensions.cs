using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods that make Unity AsyncOperation awaitable so it can be
	/// used directly with C# async/await (e.g. <c>await request.SendWebRequest()</c>).
	/// </summary>
	public static class AsyncOperationExtensions
	{
		/// <summary>
		/// Returns a task awaiter for an <see cref="AsyncOperation"/>,
		/// completing when the operation finishes.
		/// </summary>
		public static TaskAwaiter<AsyncOperation> GetAwaiter(this AsyncOperation asyncOp)
		{
			var tcs = new TaskCompletionSource<AsyncOperation>();
			asyncOp.completed += _ => tcs.TrySetResult(asyncOp);
			return tcs.Task.GetAwaiter();
		}
	}
}
