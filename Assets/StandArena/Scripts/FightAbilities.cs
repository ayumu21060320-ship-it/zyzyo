using UnityEngine;

namespace StandArena
{
    /// <summary>Stand abilities in the fight: triggers, costs and effects.</summary>
    public partial class FightSim
    {
        static void AddGauge(Fighter2D f, float v)
        {
            if (f.sealT > 0 && v > 0) return;
            f.gauge = Mathf.Clamp(f.gauge + v, 0f, 100f);
        }

        void TickTimers(Fighter2D f)
        {
            if (f.weakenT > 0 && --f.weakenT == 0) f.weakenMul = 1f;
            if (f.sealT > 0) f.sealT--;
            if (f.hasteT > 0) f.hasteT--;
            if (f.abilityCd > 0) f.abilityCd--;
            if (f.barrierT > 0) f.barrierT--;
        }

        /// <summary>False while frozen, and every other frame while slowed.</summary>
        bool Active(Fighter2D f)
        {
            if (f.freezeT > 0) { f.freezeT--; return false; }
            if (f.slowT > 0) { f.slowT--; if ((frame & 1) == 1) return false; }
            return true;
        }

        static void Record(Fighter2D f)
        {
            f.histI = (f.histI + 1) % f.hist.Length;
            f.hist[f.histI] = new Vector2(f.x, f.y);
        }

        /// <summary>The opponent of a barrier's owner can't cross it while it stands.</summary>
        static void KeepOutOfBarrier(Fighter2D owner, Fighter2D o)
        {
            if (owner.barrierT <= 0) return;
            const float gap = 40f;
            if (owner.barrierSide > 0 && o.x < owner.barrierX + gap) { o.x = owner.barrierX + gap; if (o.vx < 0) o.vx = 0; }
            if (owner.barrierSide < 0 && o.x > owner.barrierX - gap) { o.x = owner.barrierX - gap; if (o.vx > 0) o.vx = 0; }
        }

        static bool Armored(Fighter2D o) =>
            o.ab.type == "armor" &&
            ((o.st == FState.Attack && o.move != null && o.move.stand) || o.st == FState.Charge);

        static bool TriggerMatches(AbilitySpec a, MoveDef m)
        {
            switch (a.trigger)
            {
                case "stand": return m.stand;
                case "special": return m.special;
                default: return true;
            }
        }

        /// <summary>Check limits and pay the cost (the stand's weakness). Returns false if the ability can't fire now.</summary>
        static bool TryUse(Fighter2D f)
        {
            var a = f.ab;
            if (f.abilityCd > 0) return false;
            if (a.limit > 0 && a.type != "mark" && f.abilityUses >= a.limit) return false;
            switch (a.cost)
            {
                case "gauge":
                    if (f.gauge < a.costValue) return false;
                    f.gauge -= a.costValue;
                    break;
                case "selfDamage":
                    f.hp = Mathf.Max(1f, f.hp - f.max * a.costValue / 100f);
                    break;
                case "cooldown":
                    f.abilityCd = Mathf.RoundToInt(a.costValue * 60);
                    break;
            }
            f.abilityUses++;
            f.abilityCount++;
            return true;
        }

        static void Announce(Fighter2D f, string label) => f.Callout($"『{f.s.abilityName}』{label}");

        /// <summary>Abilities that fire when the special move starts.</summary>
        void OnSpecialStart(Fighter2D f, Fighter2D o)
        {
            var a = f.ab;
            switch (a.type)
            {
                case "mark":
                    if (f.marks > 0 && o.st != FState.Ko)
                    {
                        float dmg = f.marks * 30f * a.power;
                        o.hp = Mathf.Max(0, o.hp - dmg);
                        f.dealt += dmg;
                        o.flash = 6;
                        sparks.Add(new Spark { x = o.x, y = o.y + 110, big = true });
                        hitstop = 10;
                        Announce(f, $"起爆×{f.marks}！");
                        f.marks = 0;
                        if (o.hp <= 0) Ko(o, f.face);
                        else CheckAwake(o);
                    }
                    break;

                case "barrier":
                    if (TryUse(f))
                    {
                        f.barrierX = Mathf.Clamp(f.x + f.face * 130, 60, StageW - 60);
                        f.barrierSide = o.x >= f.barrierX ? 1 : -1;
                        f.barrierT = Mathf.RoundToInt(a.duration * 60);
                        Announce(f, StandAbility.Label(a.type));
                    }
                    break;

                case "teleport":
                    if (TryUse(f))
                    {
                        ghosts.Add(new Ghost { x = f.x, y = f.y, p1 = f.p1 });
                        float side = Mathf.Sign(o.x - f.x);
                        if (side == 0) side = f.face;
                        f.x = Mathf.Clamp(o.x + side * 90, 50, StageW - 50);
                        f.face = o.x >= f.x ? 1 : -1;
                        Announce(f, StandAbility.Label(a.type));
                    }
                    break;

                case "haste":
                    if (TryUse(f))
                    {
                        f.hasteT = Mathf.RoundToInt(a.duration * 60);
                        Announce(f, StandAbility.Label(a.type));
                    }
                    break;
            }
        }

        /// <summary>Abilities that fire when an attack lands (not blocked).</summary>
        void OnHitEffect(Fighter2D f, Fighter2D o, MoveDef m, float dmg, int dir)
        {
            var a = f.ab;
            switch (a.type)
            {
                case "rewind": case "freeze": case "slow": case "drain": case "mark":
                case "weaken": case "seal": case "pull":
                    break;
                default:
                    return;
            }
            if (!TriggerMatches(a, m)) return;
            if (a.type == "mark" && f.marks >= a.limit) return;
            if (!TryUse(f)) return;

            switch (a.type)
            {
                case "rewind":
                {
                    // Put the opponent back where they were a moment ago and cancel what they were doing.
                    int back = StandAbility.RewindFrames(a);
                    var old = o.hist[(o.histI - back + o.hist.Length * 2) % o.hist.Length];
                    ghosts.Add(new Ghost { x = o.x, y = o.y, p1 = o.p1 });
                    o.x = Mathf.Clamp(old.x, 50, StageW - 50);
                    o.y = Mathf.Max(0, old.y);
                    o.vx = 0;
                    o.move = null;
                    if (o.st == FState.Hit) o.stun += 6;
                    for (int i = 0; i < o.hist.Length; i++) o.hist[i] = new Vector2(o.x, o.y);
                    Announce(f, StandAbility.Label(a.type));
                    break;
                }
                case "freeze":
                    o.freezeT = Mathf.RoundToInt(a.duration * 60);
                    Announce(f, StandAbility.Label(a.type));
                    break;
                case "slow":
                    o.slowT = Mathf.RoundToInt(a.duration * 60);
                    Announce(f, StandAbility.Label(a.type));
                    break;
                case "drain":
                {
                    float heal = dmg * 0.15f * a.power;
                    f.hp = Mathf.Min(f.max, f.hp + heal);
                    Announce(f, $"{StandAbility.Label(a.type)} +{Mathf.RoundToInt(heal)}");
                    break;
                }
                case "mark":
                    f.marks++;
                    Announce(f, $"刻印 {f.marks}/{a.limit}");
                    break;
                case "weaken":
                    o.weakenT = Mathf.RoundToInt(a.duration * 60);
                    o.weakenMul = 1f - Mathf.Clamp(0.3f * a.power, 0.1f, 0.5f);
                    Announce(f, StandAbility.Label(a.type));
                    break;
                case "seal":
                    o.gauge = Mathf.Max(0, o.gauge - 25f * a.power);
                    o.sealT = Mathf.RoundToInt(a.duration * 60);
                    Announce(f, StandAbility.Label(a.type));
                    break;
                case "pull":
                    o.vx = -dir * 6f * a.power;
                    if (o.st == FState.Hit) o.stun += 4;
                    Announce(f, StandAbility.Label(a.type));
                    break;
            }
        }

        /// <summary>Reflect: blocking sends part of the damage back.</summary>
        void OnBlocked(Fighter2D o, Fighter2D attacker, float dmg, int dir)
        {
            if (o.ab.type != "reflect" || attacker.st == FState.Ko) return;
            if (!TryUse(o)) return;
            float back = dmg * 0.4f * o.ab.power;
            attacker.hp = Mathf.Max(0, attacker.hp - back);
            o.dealt += back;
            attacker.flash = 5;
            sparks.Add(new Spark { x = attacker.x, y = attacker.y + 110, big = true });
            Announce(o, $"{StandAbility.Label("reflect")} {Mathf.RoundToInt(back)}");
            if (attacker.hp <= 0) Ko(attacker, -dir);
            else CheckAwake(attacker);
        }
    }
}
