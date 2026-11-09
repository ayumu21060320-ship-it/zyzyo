using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;

namespace StandArena
{
    /// <summary>
    /// How a stand's written ability works in a fight. Stored on the Stand (field "battle").
    /// </summary>
    [Serializable]
    public class AbilitySpec
    {
        public string type = "";      // see StandAbility.Types
        public string trigger = "";   // hit / stand / special
        public float power = 1f;      // 0.5 - 2
        public float duration = 2f;   // seconds
        public int limit = 0;         // uses per round (0 = no limit). For "mark": max marks.
        public string cost = "none";  // none / selfDamage / gauge / cooldown
        public float costValue = 0;   // selfDamage: % of max HP, gauge: SP, cooldown: seconds

        public bool IsValid => Array.IndexOf(StandAbility.Types, type) >= 0;
    }

    public static class StandAbility
    {
        public static readonly string[] Types =
            { "rewind", "freeze", "slow", "drain", "mark", "barrier", "reflect", "teleport", "weaken", "seal", "pull", "haste", "armor" };

        /// <summary>Text that teaches the model the effect list. Shared by stand generation and conversion.</summary>
        public const string SchemaHelp =
            "battle は、能力を格闘ゲームの効果に変換したもの。type は次から1つ選ぶ:\n" +
            "rewind=当たった相手の位置と動きを少し前に巻き戻す / freeze=相手を短時間停止させる / slow=相手をスローにする / " +
            "drain=与えたダメージの一部を回復 / mark=攻撃で刻印を付け、必殺技を出すと起爆 / barrier=必殺技を出すと相手が越えられない壁を作る / " +
            "reflect=ガードした時に相手へダメージを返す / teleport=必殺技を出すと相手の背後へ瞬間移動 / weaken=相手の攻撃力を下げる / " +
            "seal=相手のSPゲージを削り、しばらく貯められなくする / pull=相手を引き寄せる / haste=必殺技を出すと一定時間自分が加速 / " +
            "armor=スタンド攻撃と溜めの最中は攻撃を受けても怯まない\n" +
            "trigger は hit（どの攻撃でも当たったら）/ stand（スタンド攻撃が当たったら）/ special（必殺技が当たったら）。" +
            "power は強さ 0.5〜2、duration は効果の秒数 0.3〜5、limit は1ラウンドの使用回数の上限（0は無制限。markでは刻印の最大数）。\n" +
            "弱点は cost に変換する: none / selfDamage（発動ごとに本体もHPを失う。costValue は最大HPの% 1〜8）/ " +
            "gauge（発動ごとにSPを消費。costValue 5〜40）/ cooldown（再使用まで待つ。costValue は秒 1〜15）。\n" +
            "能力の文章にいちばん近い type を選び、弱点の文章にいちばん近い cost と limit を選ぶこと。";

        public const string SchemaShape =
            "\"battle\":{\"type\":\"\",\"trigger\":\"\",\"power\":1,\"duration\":2,\"limit\":0,\"cost\":\"none\",\"costValue\":0}";

        static string DefaultTrigger(string type)
        {
            switch (type)
            {
                case "rewind": case "drain": return "hit";
                case "freeze": return "special";
                default: return "stand";
            }
        }

        /// <summary>Clamp numbers and fix fields that don't fit the chosen type.</summary>
        public static AbilitySpec Clamp(AbilitySpec a)
        {
            if (a == null || !a.IsValid) return a;
            a.power = Mathf.Clamp(a.power <= 0 ? 1 : a.power, 0.5f, 2f);
            a.duration = Mathf.Clamp(a.duration <= 0 ? 2 : a.duration, 0.3f, 5f);
            if (a.type == "freeze") a.duration = Mathf.Min(a.duration, 2f);
            a.limit = Mathf.Clamp(a.limit, 0, 9);
            if (a.type == "mark" && a.limit == 0) a.limit = 3;
            if (a.trigger != "hit" && a.trigger != "stand" && a.trigger != "special") a.trigger = DefaultTrigger(a.type);
            switch (a.cost)
            {
                case "selfDamage": a.costValue = Mathf.Clamp(a.costValue <= 0 ? 2 : a.costValue, 1, 8); break;
                case "gauge": a.costValue = Mathf.Clamp(a.costValue <= 0 ? 10 : a.costValue, 5, 40); break;
                case "cooldown": a.costValue = Mathf.Clamp(a.costValue <= 0 ? 5 : a.costValue, 1, 15); break;
                default: a.cost = "none"; a.costValue = 0; break;
            }
            // Strong control effects always get some brake.
            if ((a.type == "freeze" || a.type == "rewind") && a.cost == "none" && a.limit == 0)
            {
                a.cost = "cooldown";
                a.costValue = a.type == "freeze" ? 8 : 2;
            }
            return a;
        }

        /// <summary>The stand's battle effect: the stored one, else one guessed from the ability text.</summary>
        public static AbilitySpec Resolve(Stand s)
        {
            if (s.battle != null && s.battle.IsValid) return Clamp(s.battle);
            s.battle = Clamp(Guess(s));
            return s.battle;
        }

        public static bool NeedsConversion(Stand s) => s.battle == null || !s.battle.IsValid;

        /// <summary>Keyword-based conversion, used without an API key or when the AI call fails.</summary>
        public static AbilitySpec Guess(Stand s)
        {
            string ab = (s.abilityName ?? "") + " " + (s.ability ?? "");
            string wk = s.weakness ?? "";
            var a = new AbilitySpec();

            bool Has(string text, params string[] words)
            {
                foreach (var w in words) if (text.Contains(w)) return true;
                return false;
            }

            if (Has(ab, "巻き戻", "戻す", "戻る", "逆再生", "過去")) a.type = "rewind";
            else if (Has(ab, "停止", "止め", "止ま", "凍", "固め", "静止")) a.type = "freeze";
            else if (Has(ab, "刻印", "印を", "爆", "起爆", "炸裂", "マーク")) a.type = "mark";
            else if (Has(ab, "境界", "線を", "壁", "結界", "越えられ", "通さ")) a.type = "barrier";
            else if (Has(ab, "返す", "反射", "鏡", "跳ね返")) a.type = "reflect";
            else if (Has(ab, "瞬間移動", "ワープ", "背後", "転移", "入れ替")) a.type = "teleport";
            else if (Has(ab, "遅く", "スロー", "鈍", "重く", "ゆっくり")) a.type = "slow";
            else if (Has(ab, "吸", "奪", "回復", "癒")) a.type = "drain";
            else if (Has(ab, "封", "禁じ", "使えなく", "消す")) a.type = "seal";
            else if (Has(ab, "引き寄", "引っ張", "磁", "吸い寄")) a.type = "pull";
            else if (Has(ab, "加速", "速く", "素早", "スピード")) a.type = "haste";
            else if (Has(ab, "硬", "鎧", "不動", "動じ", "耐え")) a.type = "armor";
            else if (Has(ab, "弱", "衰", "削")) a.type = "weaken";
            else a.type = s.stats.power >= s.stats.durability ? "drain" : "armor";

            a.trigger = Has(ab, "触れ", "殴", "拳", "当た") ? "hit" : DefaultTrigger(a.type);
            a.power = 0.8f + s.stats.potential * 0.15f;
            a.duration = 1f + s.stats.range * 0.4f;

            var m = Regex.Match(wk, "([0-9０-９一二三四五六七八九])\\s*(つ|回|個|度)");
            if (m.Success) a.limit = DigitValue(m.Groups[1].Value);

            if (Has(wk, "疲", "本体も", "傷つ", "痛み", "命", "削れ")) { a.cost = "selfDamage"; a.costValue = 2; }
            else if (Has(wk, "使えなく", "一日", "時間がかか", "連続", "しばらく")) { a.cost = "cooldown"; a.costValue = 8; }
            else if (Has(wk, "集中", "体力", "エネルギー", "消耗")) { a.cost = "gauge"; a.costValue = 15; }
            else { a.cost = "cooldown"; a.costValue = 3; }
            return a;
        }

        static int DigitValue(string c)
        {
            const string zen = "０１２３４５６７８９", kan = "〇一二三四五六七八九";
            int i = zen.IndexOf(c, StringComparison.Ordinal);
            if (i >= 0) return i;
            i = kan.IndexOf(c, StringComparison.Ordinal);
            if (i >= 0) return i;
            return int.TryParse(c, out var v) ? v : 0;
        }

        [Serializable] class Wrapper { public AbilitySpec battle; }

        /// <summary>Ask Claude to convert the written ability into a battle effect. Falls back to Guess.</summary>
        public static IEnumerator Convert(Stand s, Action<string> onError)
        {
            if (!ClaudeClient.HasKey) { Resolve(s); yield break; }
            string prompt =
                "異能バトル格闘ゲームで、次のスタンド能力をゲームの効果に変換してください。\n\n" +
                $"能力名: {s.abilityName}\n能力: {s.ability}\n弱点: {s.weakness}\n" +
                $"能力値: 破壊力{Stand.Grade(s.stats.power)} スピード{Stand.Grade(s.stats.speed)} 射程{Stand.Grade(s.stats.range)} " +
                $"持続力{Stand.Grade(s.stats.durability)} 精密{Stand.Grade(s.stats.precision)} 成長性{Stand.Grade(s.stats.potential)}\n\n" +
                SchemaHelp + "\n\n次の形のJSONだけを返してください:\n{" + SchemaShape + "}";
            string reply = null, err = null;
            yield return ClaudeClient.Ask(prompt, "low", 4000, t => reply = t, e => err = e);
            if (reply != null)
            {
                try
                {
                    var w = JsonUtility.FromJson<Wrapper>(ClaudeClient.ExtractJsonObject(reply) ?? "{}");
                    if (w != null && w.battle != null && w.battle.IsValid) { s.battle = Clamp(w.battle); yield break; }
                    err = "AIの変換結果を読み取れませんでした";
                }
                catch (Exception) { err = "AIの変換結果を読み取れませんでした"; }
            }
            if (err != null) onError?.Invoke(err);
            Resolve(s);
        }

        // ------------------------------------------------------------ text

        static string Trig(AbilitySpec a)
        {
            switch (a.trigger)
            {
                case "stand": return "スタンド攻撃（K）が当たると";
                case "special": return "必殺技（L）が当たると";
                default: return "攻撃が当たるたびに";
            }
        }

        public static int RewindFrames(AbilitySpec a) => Mathf.Clamp(Mathf.RoundToInt(10 + 10 * a.power), 5, 40);
        public static int Pct(float v) => Mathf.RoundToInt(v * 100);

        /// <summary>One-line Japanese description of what happens in a fight.</summary>
        public static string Describe(AbilitySpec a)
        {
            if (a == null || !a.IsValid) return "（対戦の前に決まります）";
            string d = $"{a.duration:0.#}秒";
            string main;
            switch (a.type)
            {
                case "rewind": main = $"{Trig(a)}、相手の位置と動きを{RewindFrames(a) / 60f:0.0#}秒前に巻き戻す"; break;
                case "freeze": main = $"{Trig(a)}、相手を{d}停止させる"; break;
                case "slow": main = $"{Trig(a)}、相手を{d}スローにする"; break;
                case "drain": main = $"{Trig(a)}、与えたダメージの{Pct(0.15f * a.power)}%を回復する"; break;
                case "mark": main = $"{Trig(a)}、相手に刻印を付ける（最大{a.limit}個）。必殺技を出すと刻印がまとめて起爆する"; break;
                case "barrier": main = $"必殺技を出すと、相手が越えられない壁を{d}作る"; break;
                case "reflect": main = $"ガードすると、受けるはずだったダメージの{Pct(0.4f * a.power)}%を相手に返す"; break;
                case "teleport": main = "必殺技を出すと、相手の背後へ瞬間移動してから技を放つ"; break;
                case "weaken": main = $"{Trig(a)}、相手の攻撃力を{d}{Pct(Mathf.Clamp(0.3f * a.power, 0.1f, 0.5f))}%下げる"; break;
                case "seal": main = $"{Trig(a)}、相手のSPを{Mathf.RoundToInt(25 * a.power)}削り、{d}SPを貯められなくする"; break;
                case "pull": main = $"{Trig(a)}、相手を引き寄せて連続攻撃につなげる"; break;
                case "haste": main = $"必殺技を出すと、{d}自分が加速する"; break;
                default: main = $"スタンド攻撃と溜めの最中は、攻撃を受けても怯まない（ダメージ{Pct(0.15f * a.power)}%軽減）"; break;
            }
            string cost = "";
            switch (a.cost)
            {
                case "selfDamage": cost = $"代償：発動のたびに本体もHPを{a.costValue:0.#}%失う"; break;
                case "gauge": cost = $"代償：発動のたびにSPを{a.costValue:0}消費"; break;
                case "cooldown": cost = $"再使用まで{a.costValue:0.#}秒"; break;
            }
            if (a.limit > 0 && a.type != "mark") cost += (cost.Length > 0 ? "、" : "") + $"1ラウンド{a.limit}回まで";
            return cost.Length > 0 ? $"{main}（{cost}）" : main;
        }

        public static string Label(string type)
        {
            switch (type)
            {
                case "rewind": return "巻き戻し！";
                case "freeze": return "停止！";
                case "slow": return "スロー！";
                case "drain": return "吸収！";
                case "mark": return "刻印！";
                case "barrier": return "壁！";
                case "reflect": return "反射！";
                case "teleport": return "瞬間移動！";
                case "weaken": return "弱体化！";
                case "seal": return "封印！";
                case "pull": return "引き寄せ！";
                case "haste": return "加速！";
                default: return "不動！";
            }
        }
    }
}
