// ============================================================================================
// SlotCards —— 固定卡槽模型（96 个）
//
// 东尼算法要求外部角色**预先声明**足够数量的具体卡牌类（ModelDb 需要发现具体类型，
// 不能在启动后动态生成模型类型）。旧实现用 Reflection.Emit + ModContentRegistry 注入，
// 本独立 mod 改为静态声明，符合 AA 官方 examples/WatcherComponentAdapter 的推荐形态。
//
// 槽位数与 newsanguo 0.2.38 的 NewsanguoCardPool 实际成员对齐：
//   手工可获取卡 = Basic 4 + Common 20 + Uncommon 40 + Rare 26 = 90（另 2 张 Ancient 手工保留）
//   本 mod 槽位   = Basic 10 + Common 20 + Uncommon 40 + Rare 26 = 96
// Basic 取 10 是为承担“初始牌组替换”：每槽 1 张 → 开局 10 张独一无二的生成基础卡。
//
// 本文件由 tools/gen_slotcards.py 生成，请勿手改。
// ============================================================================================
using System;
using AutoAnthony;
using MegaCrit.Sts2.Core.Models;
using newsanguo.Scripts.Characters;

namespace Newsanguo.AutoAnthony;

/// <summary>
/// 三国随机卡槽基类：只提供 ProfileId 与所属卡池，其余（描述/升级/触发器/执行）全部继承自东尼算法。
/// </summary>
public abstract class NewsanguoChaosCard : ExternalChaosCardModel
{
    protected sealed override string ComponentProfileId => Adapter.ProfileId;

    public sealed override CardPoolModel Pool => ModelDb.CardPool<NewsanguoCardPool>();
}

// ---- Basic：槽 0..9（10 个）----
public sealed class NewsanguoChaosCard000 : NewsanguoChaosCard { protected override int Slot => 0; }
public sealed class NewsanguoChaosCard001 : NewsanguoChaosCard { protected override int Slot => 1; }
public sealed class NewsanguoChaosCard002 : NewsanguoChaosCard { protected override int Slot => 2; }
public sealed class NewsanguoChaosCard003 : NewsanguoChaosCard { protected override int Slot => 3; }
public sealed class NewsanguoChaosCard004 : NewsanguoChaosCard { protected override int Slot => 4; }
public sealed class NewsanguoChaosCard005 : NewsanguoChaosCard { protected override int Slot => 5; }
public sealed class NewsanguoChaosCard006 : NewsanguoChaosCard { protected override int Slot => 6; }
public sealed class NewsanguoChaosCard007 : NewsanguoChaosCard { protected override int Slot => 7; }
public sealed class NewsanguoChaosCard008 : NewsanguoChaosCard { protected override int Slot => 8; }
public sealed class NewsanguoChaosCard009 : NewsanguoChaosCard { protected override int Slot => 9; }

// ---- Common：槽 10..29（20 个）----
public sealed class NewsanguoChaosCard010 : NewsanguoChaosCard { protected override int Slot => 10; }
public sealed class NewsanguoChaosCard011 : NewsanguoChaosCard { protected override int Slot => 11; }
public sealed class NewsanguoChaosCard012 : NewsanguoChaosCard { protected override int Slot => 12; }
public sealed class NewsanguoChaosCard013 : NewsanguoChaosCard { protected override int Slot => 13; }
public sealed class NewsanguoChaosCard014 : NewsanguoChaosCard { protected override int Slot => 14; }
public sealed class NewsanguoChaosCard015 : NewsanguoChaosCard { protected override int Slot => 15; }
public sealed class NewsanguoChaosCard016 : NewsanguoChaosCard { protected override int Slot => 16; }
public sealed class NewsanguoChaosCard017 : NewsanguoChaosCard { protected override int Slot => 17; }
public sealed class NewsanguoChaosCard018 : NewsanguoChaosCard { protected override int Slot => 18; }
public sealed class NewsanguoChaosCard019 : NewsanguoChaosCard { protected override int Slot => 19; }
public sealed class NewsanguoChaosCard020 : NewsanguoChaosCard { protected override int Slot => 20; }
public sealed class NewsanguoChaosCard021 : NewsanguoChaosCard { protected override int Slot => 21; }
public sealed class NewsanguoChaosCard022 : NewsanguoChaosCard { protected override int Slot => 22; }
public sealed class NewsanguoChaosCard023 : NewsanguoChaosCard { protected override int Slot => 23; }
public sealed class NewsanguoChaosCard024 : NewsanguoChaosCard { protected override int Slot => 24; }
public sealed class NewsanguoChaosCard025 : NewsanguoChaosCard { protected override int Slot => 25; }
public sealed class NewsanguoChaosCard026 : NewsanguoChaosCard { protected override int Slot => 26; }
public sealed class NewsanguoChaosCard027 : NewsanguoChaosCard { protected override int Slot => 27; }
public sealed class NewsanguoChaosCard028 : NewsanguoChaosCard { protected override int Slot => 28; }
public sealed class NewsanguoChaosCard029 : NewsanguoChaosCard { protected override int Slot => 29; }

// ---- Uncommon：槽 30..69（40 个）----
public sealed class NewsanguoChaosCard030 : NewsanguoChaosCard { protected override int Slot => 30; }
public sealed class NewsanguoChaosCard031 : NewsanguoChaosCard { protected override int Slot => 31; }
public sealed class NewsanguoChaosCard032 : NewsanguoChaosCard { protected override int Slot => 32; }
public sealed class NewsanguoChaosCard033 : NewsanguoChaosCard { protected override int Slot => 33; }
public sealed class NewsanguoChaosCard034 : NewsanguoChaosCard { protected override int Slot => 34; }
public sealed class NewsanguoChaosCard035 : NewsanguoChaosCard { protected override int Slot => 35; }
public sealed class NewsanguoChaosCard036 : NewsanguoChaosCard { protected override int Slot => 36; }
public sealed class NewsanguoChaosCard037 : NewsanguoChaosCard { protected override int Slot => 37; }
public sealed class NewsanguoChaosCard038 : NewsanguoChaosCard { protected override int Slot => 38; }
public sealed class NewsanguoChaosCard039 : NewsanguoChaosCard { protected override int Slot => 39; }
public sealed class NewsanguoChaosCard040 : NewsanguoChaosCard { protected override int Slot => 40; }
public sealed class NewsanguoChaosCard041 : NewsanguoChaosCard { protected override int Slot => 41; }
public sealed class NewsanguoChaosCard042 : NewsanguoChaosCard { protected override int Slot => 42; }
public sealed class NewsanguoChaosCard043 : NewsanguoChaosCard { protected override int Slot => 43; }
public sealed class NewsanguoChaosCard044 : NewsanguoChaosCard { protected override int Slot => 44; }
public sealed class NewsanguoChaosCard045 : NewsanguoChaosCard { protected override int Slot => 45; }
public sealed class NewsanguoChaosCard046 : NewsanguoChaosCard { protected override int Slot => 46; }
public sealed class NewsanguoChaosCard047 : NewsanguoChaosCard { protected override int Slot => 47; }
public sealed class NewsanguoChaosCard048 : NewsanguoChaosCard { protected override int Slot => 48; }
public sealed class NewsanguoChaosCard049 : NewsanguoChaosCard { protected override int Slot => 49; }
public sealed class NewsanguoChaosCard050 : NewsanguoChaosCard { protected override int Slot => 50; }
public sealed class NewsanguoChaosCard051 : NewsanguoChaosCard { protected override int Slot => 51; }
public sealed class NewsanguoChaosCard052 : NewsanguoChaosCard { protected override int Slot => 52; }
public sealed class NewsanguoChaosCard053 : NewsanguoChaosCard { protected override int Slot => 53; }
public sealed class NewsanguoChaosCard054 : NewsanguoChaosCard { protected override int Slot => 54; }
public sealed class NewsanguoChaosCard055 : NewsanguoChaosCard { protected override int Slot => 55; }
public sealed class NewsanguoChaosCard056 : NewsanguoChaosCard { protected override int Slot => 56; }
public sealed class NewsanguoChaosCard057 : NewsanguoChaosCard { protected override int Slot => 57; }
public sealed class NewsanguoChaosCard058 : NewsanguoChaosCard { protected override int Slot => 58; }
public sealed class NewsanguoChaosCard059 : NewsanguoChaosCard { protected override int Slot => 59; }
public sealed class NewsanguoChaosCard060 : NewsanguoChaosCard { protected override int Slot => 60; }
public sealed class NewsanguoChaosCard061 : NewsanguoChaosCard { protected override int Slot => 61; }
public sealed class NewsanguoChaosCard062 : NewsanguoChaosCard { protected override int Slot => 62; }
public sealed class NewsanguoChaosCard063 : NewsanguoChaosCard { protected override int Slot => 63; }
public sealed class NewsanguoChaosCard064 : NewsanguoChaosCard { protected override int Slot => 64; }
public sealed class NewsanguoChaosCard065 : NewsanguoChaosCard { protected override int Slot => 65; }
public sealed class NewsanguoChaosCard066 : NewsanguoChaosCard { protected override int Slot => 66; }
public sealed class NewsanguoChaosCard067 : NewsanguoChaosCard { protected override int Slot => 67; }
public sealed class NewsanguoChaosCard068 : NewsanguoChaosCard { protected override int Slot => 68; }
public sealed class NewsanguoChaosCard069 : NewsanguoChaosCard { protected override int Slot => 69; }

// ---- Rare：槽 70..95（26 个）----
public sealed class NewsanguoChaosCard070 : NewsanguoChaosCard { protected override int Slot => 70; }
public sealed class NewsanguoChaosCard071 : NewsanguoChaosCard { protected override int Slot => 71; }
public sealed class NewsanguoChaosCard072 : NewsanguoChaosCard { protected override int Slot => 72; }
public sealed class NewsanguoChaosCard073 : NewsanguoChaosCard { protected override int Slot => 73; }
public sealed class NewsanguoChaosCard074 : NewsanguoChaosCard { protected override int Slot => 74; }
public sealed class NewsanguoChaosCard075 : NewsanguoChaosCard { protected override int Slot => 75; }
public sealed class NewsanguoChaosCard076 : NewsanguoChaosCard { protected override int Slot => 76; }
public sealed class NewsanguoChaosCard077 : NewsanguoChaosCard { protected override int Slot => 77; }
public sealed class NewsanguoChaosCard078 : NewsanguoChaosCard { protected override int Slot => 78; }
public sealed class NewsanguoChaosCard079 : NewsanguoChaosCard { protected override int Slot => 79; }
public sealed class NewsanguoChaosCard080 : NewsanguoChaosCard { protected override int Slot => 80; }
public sealed class NewsanguoChaosCard081 : NewsanguoChaosCard { protected override int Slot => 81; }
public sealed class NewsanguoChaosCard082 : NewsanguoChaosCard { protected override int Slot => 82; }
public sealed class NewsanguoChaosCard083 : NewsanguoChaosCard { protected override int Slot => 83; }
public sealed class NewsanguoChaosCard084 : NewsanguoChaosCard { protected override int Slot => 84; }
public sealed class NewsanguoChaosCard085 : NewsanguoChaosCard { protected override int Slot => 85; }
public sealed class NewsanguoChaosCard086 : NewsanguoChaosCard { protected override int Slot => 86; }
public sealed class NewsanguoChaosCard087 : NewsanguoChaosCard { protected override int Slot => 87; }
public sealed class NewsanguoChaosCard088 : NewsanguoChaosCard { protected override int Slot => 88; }
public sealed class NewsanguoChaosCard089 : NewsanguoChaosCard { protected override int Slot => 89; }
public sealed class NewsanguoChaosCard090 : NewsanguoChaosCard { protected override int Slot => 90; }
public sealed class NewsanguoChaosCard091 : NewsanguoChaosCard { protected override int Slot => 91; }
public sealed class NewsanguoChaosCard092 : NewsanguoChaosCard { protected override int Slot => 92; }
public sealed class NewsanguoChaosCard093 : NewsanguoChaosCard { protected override int Slot => 93; }
public sealed class NewsanguoChaosCard094 : NewsanguoChaosCard { protected override int Slot => 94; }
public sealed class NewsanguoChaosCard095 : NewsanguoChaosCard { protected override int Slot => 95; }

/// <summary>槽位表：供 ExternalComponentCharacterApi.RegisterRuntime 按槽取类型。</summary>
internal static class SlotTable
{
    internal const int Count = 96;

    internal static readonly Type[] Types =
    [
        typeof(NewsanguoChaosCard000),
        typeof(NewsanguoChaosCard001),
        typeof(NewsanguoChaosCard002),
        typeof(NewsanguoChaosCard003),
        typeof(NewsanguoChaosCard004),
        typeof(NewsanguoChaosCard005),
        typeof(NewsanguoChaosCard006),
        typeof(NewsanguoChaosCard007),
        typeof(NewsanguoChaosCard008),
        typeof(NewsanguoChaosCard009),
        typeof(NewsanguoChaosCard010),
        typeof(NewsanguoChaosCard011),
        typeof(NewsanguoChaosCard012),
        typeof(NewsanguoChaosCard013),
        typeof(NewsanguoChaosCard014),
        typeof(NewsanguoChaosCard015),
        typeof(NewsanguoChaosCard016),
        typeof(NewsanguoChaosCard017),
        typeof(NewsanguoChaosCard018),
        typeof(NewsanguoChaosCard019),
        typeof(NewsanguoChaosCard020),
        typeof(NewsanguoChaosCard021),
        typeof(NewsanguoChaosCard022),
        typeof(NewsanguoChaosCard023),
        typeof(NewsanguoChaosCard024),
        typeof(NewsanguoChaosCard025),
        typeof(NewsanguoChaosCard026),
        typeof(NewsanguoChaosCard027),
        typeof(NewsanguoChaosCard028),
        typeof(NewsanguoChaosCard029),
        typeof(NewsanguoChaosCard030),
        typeof(NewsanguoChaosCard031),
        typeof(NewsanguoChaosCard032),
        typeof(NewsanguoChaosCard033),
        typeof(NewsanguoChaosCard034),
        typeof(NewsanguoChaosCard035),
        typeof(NewsanguoChaosCard036),
        typeof(NewsanguoChaosCard037),
        typeof(NewsanguoChaosCard038),
        typeof(NewsanguoChaosCard039),
        typeof(NewsanguoChaosCard040),
        typeof(NewsanguoChaosCard041),
        typeof(NewsanguoChaosCard042),
        typeof(NewsanguoChaosCard043),
        typeof(NewsanguoChaosCard044),
        typeof(NewsanguoChaosCard045),
        typeof(NewsanguoChaosCard046),
        typeof(NewsanguoChaosCard047),
        typeof(NewsanguoChaosCard048),
        typeof(NewsanguoChaosCard049),
        typeof(NewsanguoChaosCard050),
        typeof(NewsanguoChaosCard051),
        typeof(NewsanguoChaosCard052),
        typeof(NewsanguoChaosCard053),
        typeof(NewsanguoChaosCard054),
        typeof(NewsanguoChaosCard055),
        typeof(NewsanguoChaosCard056),
        typeof(NewsanguoChaosCard057),
        typeof(NewsanguoChaosCard058),
        typeof(NewsanguoChaosCard059),
        typeof(NewsanguoChaosCard060),
        typeof(NewsanguoChaosCard061),
        typeof(NewsanguoChaosCard062),
        typeof(NewsanguoChaosCard063),
        typeof(NewsanguoChaosCard064),
        typeof(NewsanguoChaosCard065),
        typeof(NewsanguoChaosCard066),
        typeof(NewsanguoChaosCard067),
        typeof(NewsanguoChaosCard068),
        typeof(NewsanguoChaosCard069),
        typeof(NewsanguoChaosCard070),
        typeof(NewsanguoChaosCard071),
        typeof(NewsanguoChaosCard072),
        typeof(NewsanguoChaosCard073),
        typeof(NewsanguoChaosCard074),
        typeof(NewsanguoChaosCard075),
        typeof(NewsanguoChaosCard076),
        typeof(NewsanguoChaosCard077),
        typeof(NewsanguoChaosCard078),
        typeof(NewsanguoChaosCard079),
        typeof(NewsanguoChaosCard080),
        typeof(NewsanguoChaosCard081),
        typeof(NewsanguoChaosCard082),
        typeof(NewsanguoChaosCard083),
        typeof(NewsanguoChaosCard084),
        typeof(NewsanguoChaosCard085),
        typeof(NewsanguoChaosCard086),
        typeof(NewsanguoChaosCard087),
        typeof(NewsanguoChaosCard088),
        typeof(NewsanguoChaosCard089),
        typeof(NewsanguoChaosCard090),
        typeof(NewsanguoChaosCard091),
        typeof(NewsanguoChaosCard092),
        typeof(NewsanguoChaosCard093),
        typeof(NewsanguoChaosCard094),
        typeof(NewsanguoChaosCard095),
    ];

    internal static Type TypeForSlot(int slot) => Types[slot];
}
