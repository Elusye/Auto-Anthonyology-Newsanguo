using System;
using System.Reflection;
using System.Threading.Tasks;
using AutoAnthony;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using newsanguo.Scripts.Powers;

namespace newsanguo.Scripts.AutoAnthony;

/// <summary>
/// 三国"执行词"运行时路由 —— 把 AA 生成卡上的自定义 opcode 接到 mod 现有的实现。
///
/// 背景：自写目录（NewsanguoSelfCatalog）里带 CustomOpcode 的原子（如 ns_gain_wine），
/// 数值/文案/分类沿用官方基线，但执行时内建 executor 不认识该 opcode，会静默放行并落到
/// ComponentRuntimeApi 按 (Opcode, Variant) 分发。这里注册对应 handler，直接复用现成的
/// PowerModel（酒力 = drunken_might），不新增任何机制。
///
/// 约束（AA 为可选依赖，本文件含 AA 类型引用，**必须在无 AA 时也能安全加载**）：
///  - 本文件所有类不得继承/实现任何 AA 类型（程序集类型扫描在 AA.dll 缺失时对这类继承
///    直接抛 TypeLoadException → 不订阅东尼算法就无法启动）；AA 类型只允许出现在
///    EnsureRegistered 方法体内（该方法只在适配器 TryRegister 成功、AA 已确认加载后被调用）。
///  - 接口实例用 System.Reflection.DispatchProxy 在运行时生成，逻辑回调到不依赖 AA 的普通类。
/// </summary>
internal static class NewsanguoRuntimeRoutes
{
    private const string PackageId = "newsanguo:autoanthony:runtime";

    private static bool _registered;

    /// <summary>在 ComponentRuntimeApi 路由表冻结前注册全部三国执行路由（首次生成卡 op 执行时冻结）。幂等。</summary>
    internal static void EnsureRegistered()
    {
        if (_registered) return;
        // 异常仅记录：注册失败只让自定义 op 静默无效果，不影响 AA 其它卡。
        try
        {
            ComponentRuntimeApi.RegisterPackage(PackageId,
            [
                new ComponentRuntimeRoute("ns_gain_wine", "", CreateProxy("ns_gain_wine")),
                new ComponentRuntimeRoute("ns_gain_heaven", "", CreateProxy("ns_gain_heaven"))
            ]);
            _registered = true;
        }
        catch
        {
            _registered = true; // 不重试
        }
    }

    // 生成实现 AA IComponentRuntimeHandler 的透明代理。IComponentRuntimeHandler 等 AA 类型
    // 只出现在本方法体（EnsureRegistered 调用链），不会造成类级依赖。
    private static IComponentRuntimeHandler CreateProxy(string opcode)
    {
        // DispatchProxy.Create 返回接口类型；底层实例实际是 RouteHandlerProxy 子类，
        // 先经 object 解除编译期类型限制后再设 opcode。
        var proxy = (RouteHandlerProxy)(object)DispatchProxy.Create<IComponentRuntimeHandler, RouteHandlerProxy>();
        proxy.Opcode = opcode;
        return (IComponentRuntimeHandler)(object)proxy;
    }
}

/// <summary>
/// 透明代理：运行时替身实现 AA 接口，把 ExecuteAsync 转发给 <see cref="NewsanguoRouteExecutor"/>。
/// 本类自身只依赖 BCL（DispatchProxy），不含任何 AA 类型引用。
/// </summary>
internal sealed class RouteHandlerProxy : DispatchProxy
{
    internal string Opcode { get; set; } = "";

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "ExecuteAsync")
        {
            return NewsanguoRouteExecutor.ExecuteAsync(Opcode, args is { Length: > 0 } ? args[0] : null);
        }

        return null;
    }
}

/// <summary>
/// 路由执行入口：按 opcode 分发到本 mod 现有 PowerModel 的实现（不新增机制）。
/// 以 object 承载 AA 的上下文，字段经反射读取，避免任何类级 AA 类型引用。
/// </summary>
internal static class NewsanguoRouteExecutor
{
    internal static Task<bool> ExecuteAsync(string opcode, object? rawContext)
    {
        if (rawContext is null)
        {
            return Task.FromResult(false);
        }

        RuntimeOpData data = ReadContext(rawContext);
        return opcode switch
        {
            "ns_gain_wine" => GainWineAsync(data),
            "ns_gain_heaven" => GainHeavenAsync(data),
            _ => Task.FromResult(false)
        };
    }

    // 从 AA 的 ComponentRuntimeContext 反射读出执行所需的数值/目标/卡牌。
    private static RuntimeOpData ReadContext(object rawContext)
    {
        Type type = rawContext.GetType();
        return new RuntimeOpData(
            Card: type.GetProperty("Card")?.GetValue(rawContext),
            ChoiceContext: type.GetProperty("ChoiceContext")?.GetValue(rawContext),
            Amount: ConvertAmount(type.GetProperty("Amount")?.GetValue(rawContext)));
    }

    private static decimal ConvertAmount(object? value) => value switch
    {
        null => 0m,
        decimal d => d,
        int i => i,
        long l => l,
        float f => (decimal)f,
        double db => (decimal)db,
        _ => 0m
    };

    // ns_gain_wine → 复用 drunken_might（酒力），与手工「获得酒力」卡同款 PowerCmd.Apply。
    private static async Task<bool> GainWineAsync(RuntimeOpData data)
    {
        if (data.Card is not CardModel card || card.Owner?.Creature is not { } creature ||
            data.ChoiceContext is not PlayerChoiceContext choiceContext)
        {
            return false;
        }

        await PowerCmd.Apply<drunken_might>(
            choiceContext,
            creature,
            data.Amount,
            creature,
            card,
            silent: false);
        return true;
    }

    // ns_gain_heaven → 复用 heavens_force（天意之力），与手工「获得天意之力」卡同款 PowerCmd.Apply。
    private static async Task<bool> GainHeavenAsync(RuntimeOpData data)
    {
        if (data.Card is not CardModel card || card.Owner?.Creature is not { } creature ||
            data.ChoiceContext is not PlayerChoiceContext choiceContext)
        {
            return false;
        }

        await PowerCmd.Apply<heavens_force>(
            choiceContext,
            creature,
            data.Amount,
            creature,
            card,
            silent: false);
        return true;
    }

    private readonly record struct RuntimeOpData(object? Card, object? ChoiceContext, decimal Amount);
}
