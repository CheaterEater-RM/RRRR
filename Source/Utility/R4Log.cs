using Verse;

namespace RRRR
{
    /// <summary>
    /// Centralised logging helper. Debug messages are gated behind the
    /// debugLogging setting so the log stays clean for normal players.
    /// Warnings and errors are always emitted.
    /// </summary>
    public static class R4Log
    {
        public static void Debug(string msg)
        {
            if (RRRR_Mod.Settings?.debugLogging == true)
                Log.Message($"[R4] {msg}");
        }

        public static void Warn(string msg)  => Log.Warning($"[R4] {msg}");
        public static void Error(string msg) => Log.Error($"[R4] {msg}");
    }
}
