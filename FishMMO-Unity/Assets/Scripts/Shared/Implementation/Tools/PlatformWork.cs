using System;
using System.Threading.Tasks;

namespace FishMMO.Shared
{
    /// <summary>CPU field preparation: managed workers on native players, serial execution on single-threaded Web.</summary>
    public static class PlatformWork
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public const bool Inline = true;
#else
        public const bool Inline = false;
#endif
        public static Task<T> Run<T>(Func<T> work, bool forceInline = false)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (!Inline && !forceInline) return Task.Run(work);
            try { return Task.FromResult(work()); }
            catch (Exception error) { return Task.FromException<T>(error); }
        }
        public static Task Run(Action work, bool forceInline = false)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (!Inline && !forceInline) return Task.Run(work);
            try { work(); return Task.CompletedTask; }
            catch (Exception error) { return Task.FromException(error); }
        }
        public static void For(int fromInclusive, int toExclusive, Action<int> body, bool forceInline = false)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (!Inline && !forceInline) { Parallel.For(fromInclusive,toExclusive,body); return; }
            for (int i=fromInclusive;i<toExclusive;i++) body(i);
        }
    }
}
