using System.Diagnostics;
using UnityEngine;

namespace KreeglandIndustrialRepurposing
{
    public static class KIR_DebugLogger
    {
        // Simple version (no stack trace)
        [Conditional("DEBUG")]
        public static void Log(string message)
        {
            UnityEngine.Debug.Log($"{KIR_Constants.DEBUG_LOG_PREFIX} {message}");
        }

        // Stack trace version
        [Conditional("DEBUG")]
        public static void Log(string message, bool captureStack)
        {
            if (captureStack)
            {
                var stack = GetCallChain(4);
                UnityEngine.Debug.Log(string.Format("{0} [{1}] {2}", KIR_Constants.DEBUG_LOG_PREFIX, stack, message));
            }
            else
            {
                UnityEngine.Debug.Log($"{KIR_Constants.DEBUG_LOG_PREFIX} {message}");
            }
        }

        // Simple format version
        [Conditional("DEBUG")]
        public static void LogFormat(string format, params object[] args)
        {
            UnityEngine.Debug.LogFormat($"{KIR_Constants.DEBUG_LOG_PREFIX} " + format, args);
        }

        // Stack trace format version
        [Conditional("DEBUG")]
        public static void LogFormat(string format, bool captureStack, params object[] args)
        {
            var message = string.Format(format, args);
            if (captureStack)
            {
                var stack = GetCallChain(4);
                UnityEngine.Debug.Log(string.Format("{0} [{1}] {2}", KIR_Constants.DEBUG_LOG_PREFIX, stack, message));
            }
            else
            {
                UnityEngine.Debug.LogFormat($"{KIR_Constants.DEBUG_LOG_PREFIX} " + format, args);
            }
        }

        private static string GetCallChain(int levels)
        {
            var frames = new StackTrace(true).GetFrames();
            var chain = new System.Collections.Generic.List<string>();

            // Start at 2 to skip Log() and its immediate caller
            for (int i = 2; i < Mathf.Min(frames.Length, levels + 2); i++)
            {
                var method = frames[i].GetMethod();
                if (method.DeclaringType.Namespace != null &&
                    (method.DeclaringType.Namespace.Contains("Kreegland") ||
                     method.DeclaringType.Namespace.Contains("USITools")))
                {
                    chain.Add(string.Format("{0}.{1}", method.DeclaringType.Name, method.Name));
                }
            }

            return string.Join(" → ", chain.ToArray());
        }
    }
}