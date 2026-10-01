using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using newsanguo.Scripts.AutoAnthony;
using newsanguo.Scripts.Characters;

namespace newsanguo.Scripts.Patches;

// AA 整池接管激活时，图鉴网格不再收集被隐藏的手工非基础卡。
//
// 为什么需要补丁：NCardLibraryGrid._Ready 直接遍历 ModelDb.AllCards 且只按
// CardModel.ShouldShowInCardLibrary（构造后不可变）过滤，无法靠改属性隐藏；
// 而“新三国”页签等过滤又是基于 pool.AllCardIds.Contains 的成员过滤（隐藏卡仍是池成员）。
// 因此在 _Ready 收集完成后，把 _allCards 里被接管隐藏的类型就地修剪掉，
// 之后任何页签/筛选（含“全部”）都看不到它们。保留的 2 张先古 Ancient
// （the_truest_mask/divine_insight，不在隐藏集合内）不受影响。
[HarmonyPatch(typeof(NCardLibraryGrid), nameof(NCardLibraryGrid._Ready))]
public static class CardLibraryTakeoverPatch
{
    public static void Postfix(NCardLibraryGrid __instance)
    {
        if (!NewsanguoAutoAnthonyAdapter.IsTakeoverActive)
            return;

        var hidden = NewsanguoCardPool.HiddenHandcraftedTypes;
        if (hidden.Count == 0)
            return;

        var allCards = Traverse.Create(__instance).Field("_allCards").GetValue<List<CardModel>>();
        if (allCards is null)
            return;

        allCards.RemoveAll(card => hidden.Contains(card.GetType()));
    }
}
