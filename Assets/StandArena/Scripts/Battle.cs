using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StandArena
{
    public class Fighter
    {
        public readonly Stand s;
        public int hp, max, cd;
        public bool guard, charge, awake;
        public int lastDelta;   // damage (+) or heal (-) taken in the last turn, for the UI

        public Fighter(Stand s)
        {
            this.s = s;
            max = hp = 70 + s.stats.durability * 14;
        }

        public float Ratio => (float)hp / max;
    }

    public class LogEntry
    {
        public string title, sys, nar;
        public Text narText;   // live UI reference while the battle screen is shown
    }

    public enum Outcome { None, Win, Lose, Draw }

    /// <summary>Turn-based battle rules. Pure logic, no UI.</summary>
    public class Battle
    {
        public readonly Fighter p, e;
        public int turn = 1;
        public Outcome over = Outcome.None;
        public readonly List<LogEntry> log = new List<LogEntry>();

        public Battle(Stand me, Stand enemy)
        {
            p = new Fighter(me);
            e = new Fighter(enemy);
            log.Add(new LogEntry
            {
                title = "BATTLE START",
                sys = $"{me.owner}の「{me.name}」 vs {enemy.owner}の「{enemy.name}」",
                nar = $"「{me.quote}」"
            });
        }

        public bool CanUse(Fighter f, MoveKind k)
        {
            if (k == MoveKind.Special) return f.cd == 0;
            if (k == MoveKind.Charge) return !f.charge;
            return true;
        }

        public MoveKind EnemyChoice()
        {
            if (e.cd == 0 && Random.value < 0.55f) return MoveKind.Special;
            if (p.charge && Random.value < 0.45f) return MoveKind.Guard;
            if (e.Ratio < 0.4f && Random.value < 0.35f) return MoveKind.Guard;
            if (!e.charge && Random.value < 0.22f) return MoveKind.Charge;
            return MoveKind.Attack;
        }

        struct Act { public Fighter me, foe; public MoveKind m; public int pri; public float spd; }

        /// <summary>Resolve one turn. Returns the event lines.</summary>
        public List<string> Resolve(MoveKind pm, MoveKind em)
        {
            var ev = new List<string>();
            p.lastDelta = 0; e.lastDelta = 0;

            int Pri(MoveKind m) => (m == MoveKind.Guard || m == MoveKind.Charge) ? 1 : 0;
            float Spd(Fighter f) => f.s.stats.speed + Random.Range(0f, 1.5f);

            var acts = new List<Act>
            {
                new Act { me = p, foe = e, m = pm, pri = Pri(pm), spd = Spd(p) },
                new Act { me = e, foe = p, m = em, pri = Pri(em), spd = Spd(e) }
            };
            // Guard and charge resolve first, then the faster fighter.
            acts.Sort((a, b) => a.pri != b.pri ? b.pri.CompareTo(a.pri) : b.spd.CompareTo(a.spd));

            var usedSpecial = new HashSet<Fighter>();

            foreach (var a in acts)
            {
                var me = a.me; var foe = a.foe;
                if (me.hp <= 0 || foe.hp <= 0) continue;
                var mv = me.s.moves.Get(a.m);
                var st = me.s.stats; var fs = foe.s.stats;

                if (a.m == MoveKind.Guard)
                {
                    me.guard = true;
                    int heal = Mathf.Min(me.max - me.hp, 4 + st.durability * 2);
                    me.hp += heal; me.lastDelta -= heal;
                    ev.Add($"{me.s.name}は「{mv.name}」で構えた（HP+{heal}）");
                }
                else if (a.m == MoveKind.Charge)
                {
                    me.charge = true;
                    ev.Add($"{me.s.name}は「{mv.name}」で力を溜めた");
                }
                else
                {
                    bool special = a.m == MoveKind.Special;
                    if (!special)
                    {
                        float evade = Mathf.Clamp(0.05f + (fs.speed - st.precision) * 0.05f, 0f, 0.3f);
                        if (Random.value < evade)
                        {
                            ev.Add($"{me.s.name}の「{mv.name}」！ しかし{foe.s.name}はかわした");
                            continue;
                        }
                    }
                    float dmg = special ? (14 + st.power * 3 + st.range * 3) : (8 + st.power * 4);
                    dmg *= Random.Range(0.85f, 1.15f);
                    var notes = new List<string>();
                    if (me.charge) { dmg *= 1.8f; me.charge = false; notes.Add("溜め"); }
                    if (Random.value < st.precision * 0.05f) { dmg *= 1.5f; notes.Add("急所"); }
                    if (me.awake) dmg *= 1 + st.potential * 0.06f;
                    if (foe.guard && !special) { dmg *= 0.35f; notes.Add("ガード"); }
                    int d = Mathf.Max(1, Mathf.RoundToInt(dmg));
                    foe.hp = Mathf.Max(0, foe.hp - d);
                    foe.lastDelta += d;
                    if (special) { me.cd = 3; usedSpecial.Add(me); }
                    ev.Add($"{me.s.name}の「{mv.name}」！ {foe.s.name}に{d}ダメージ" + (notes.Count > 0 ? $"（{string.Join("・", notes)}）" : ""));
                }
            }

            foreach (var f in new[] { p, e })
            {
                f.guard = false;
                if (f.cd > 0 && !usedSpecial.Contains(f)) f.cd--;
                if (!f.awake && f.hp > 0 && f.hp <= f.max / 2)
                {
                    f.awake = true;
                    ev.Add($"{f.s.owner}の精神が昂ぶる！ {f.s.name}が覚醒した（成長性{Stand.Grade(f.s.stats.potential)}）");
                }
            }

            if (p.hp <= 0 || e.hp <= 0)
                over = (e.hp <= 0 && p.hp > 0) ? Outcome.Win : (p.hp <= 0 && e.hp > 0) ? Outcome.Lose : Outcome.Draw;

            turn++;
            return ev;
        }

        public LogEntry FinishEntry()
        {
            return new LogEntry
            {
                title = "FINISH",
                sys = over == Outcome.Win ? $"{e.s.name}は再起不能（リタイア）"
                    : over == Outcome.Lose ? $"{p.s.name}は再起不能（リタイア）" : "相打ち",
                nar = over == Outcome.Lose ? $"「{e.s.quote}」" : $"「{p.s.quote}」"
            };
        }

        public string NarrationPrompt(MoveKind pm, MoveKind em, string events)
        {
            string Side(Fighter f, MoveKind k) =>
                $"本体{f.s.owner}／スタンド「{f.s.name}」能力『{f.s.abilityName}』{f.s.ability} 使った技「{f.s.moves.Get(k).name}」({f.s.moves.Get(k).desc}) 残りHP{Mathf.RoundToInt(f.Ratio * 100)}%";
            return
                "あなたは異能バトル漫画の語り手です。次の1ターンの出来事を、迫力のある日本語で2〜3文、120字以内で描写してください。" +
                "数値は書かず、スタンドの能力と技の説明に沿った絵が浮かぶ描写にしてください。" +
                (over != Outcome.None ? "このターンで決着がつきました。決着の瞬間として描いてください。" : "") +
                "セリフを入れる場合は1つまで。本文だけを返すこと。\n\n" +
                $"味方: {Side(p, pm)}\n敵: {Side(e, em)}\n出来事: {events}";
        }
    }
}
