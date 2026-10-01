using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib;

namespace Newsanguo.AutoAnthony;

/// <summary>
/// 独立适配器 mod 入口。与旧内置实现不同，本 mod 的唯一职责就是东尼算法适配，
/// 因此把 AutoAnthony 声明为必需依赖，不再需要"AA 未加载也要能安全启动"的反射/DispatchProxy 规避。
/// </summary>
[ModInitializer(nameof(Init))]
public static class ModEntry
{
    /// <summary>本 mod 的清单 id（newsanguo_autoanthony.json）。同时作为 RitsuLib 内容注册的归属 id。</summary>
    public const string ModId = "newsanguo_autoanthony";

    /// <summary>
    /// 游戏内日志器。注意：创建它需要 Godot 运行时（sts2 的 Logger 静态构造会读 Godot 命令行），
    /// 因此**只在 <see cref="Log"/> 确认已在游戏内之后才被访问** —— 否则离线工具一碰就是原生崩溃。
    /// </summary>
    internal static readonly Logger Logger = RitsuLibFramework.CreateLogger(ModId);

    public static void Init()
    {
        // 先放行日志：只有真正被游戏加载（能安全创建 Godot 日志器）时才会走到这里。
        Log.Enable();
        try
        {
            Adapter.Startup();
        }
        catch (Exception e)
        {
            Log.Error("Init failed: " + e);
        }
    }
}

/// <summary>
/// 统一日志出口。
///
/// 关键约束：sts2 的 <c>Logger</c> 依赖 Godot 运行时，在非游戏进程里构造会**原生崩溃**
/// （0xC0000005，托管 try/catch 拦不住）。因此这里：
///   · 默认关闭 —— 只有 <see cref="Enable"/>（由 <see cref="ModEntry.Init"/> 调用）之后才真正写日志；
///   · 日志器惰性获取，失败即永久降级为静默。
/// 这样离线冒烟检查才能完整跑通注册路径，而不必先把 Godot 拉起来。
/// </summary>
internal static class Log
{
    private static bool _enabled;
    private static bool _loggerUnavailable;
    private static Logger? _logger;

    /// <summary>由 <see cref="ModEntry.Init"/> 调用：确认已在游戏内，允许写日志。</summary>
    internal static void Enable() => _enabled = true;

    internal static void Info(string message) => Write(message, error: false);

    internal static void Error(string message) => Write(message, error: true);

    private static void Write(string message, bool error)
    {
        if (!_enabled) return;
        try
        {
            if (_logger is null && !_loggerUnavailable)
            {
                try { _logger = ModEntry.Logger; }
                catch { _loggerUnavailable = true; }
            }
            if (_logger is null) return;

            var line = "[AAadapter] " + message;
            if (error) _logger.Error(line);
            else _logger.Info(line);
        }
        catch
        {
            // 日志失败绝不打断 mod 本体
        }
    }
}
