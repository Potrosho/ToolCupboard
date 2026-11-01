using System;
using System.IO;
using System.Threading;

namespace ToolCupboard.Functions
{
    /// <summary>
    /// Replaces a folder with another one, and puts the original back if that fails.
    /// </summary>
    public static class FolderSwap
    {
        public static void Replace(string target, string replacement, int attempts = 10, int retryDelayMs = 300)
        {
            string old = target + ".old";
            if (Directory.Exists(old)) { Directory.Delete(old, true); }

            bool hadTarget = Directory.Exists(target);
            if (hadTarget) { Move(target, old, attempts, retryDelayMs); }

            try
            {
                Move(replacement, target, attempts, retryDelayMs);
            }
            catch
            {
                // The original comes first, cleanup must never stand in its way
                if (hadTarget)
                {
                    if (Directory.Exists(target)) { Directory.Delete(target, true); }
                    Move(old, target, attempts, retryDelayMs);
                }

                throw;
            }

            DeleteIfPossible(old);
        }

        public static void DeleteIfPossible(string path)
        {
            try
            {
                if (Directory.Exists(path)) { Directory.Delete(path, true); }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"Fail to delete {path}: {e.Message}");
            }
        }

        // Antivirus often holds new files for a moment after they are written
        private static void Move(string from, string to, int attempts, int retryDelayMs)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Move(from, to);
                    return;
                }
                catch (Exception e) when (attempt < attempts && (e is IOException || e is UnauthorizedAccessException))
                {
                    Thread.Sleep(retryDelayMs);
                }
            }
        }
    }
}
