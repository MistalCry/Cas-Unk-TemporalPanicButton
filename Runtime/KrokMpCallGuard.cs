using System;
using System.Diagnostics;
using System.Reflection;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 检测当前陷阱调用是否来自 KrokMP 的网络回放。
    /// 远端回放应该更新联机世界，但不能被当成本机玩家亲自触发陷阱，否则每台客机都会错误给自己开时停。
    /// </summary>
    internal static class KrokMpCallGuard
    {
        private const string KrokMpNamespacePrefix = "KrokoshaCasualtiesMP.";

        public static bool IsKrokMpCallStackActive()
        {
            if (!KrokMpBridge.IsNetworkRunning)
                return false;

            try
            {
                StackTrace stackTrace = new StackTrace(false);
                for (int i = 0; i < stackTrace.FrameCount; i++)
                {
                    MethodBase method = stackTrace.GetFrame(i)?.GetMethod();
                    Type declaringType = method?.DeclaringType;
                    string fullName = declaringType?.FullName;
                    if (!string.IsNullOrEmpty(fullName) && fullName.StartsWith(KrokMpNamespacePrefix, StringComparison.Ordinal))
                        return true;
                }
            }
            catch
            {
                // 调用栈检查只是保险过滤器；失败时回退到正常归属判断，不让陷阱逻辑整体失效。
            }

            return false;
        }
    }
}
