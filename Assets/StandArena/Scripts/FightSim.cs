using System.Collections.Generic;
using UnityEngine;

namespace StandArena
{
    /// <summary>One frame of controller input. Directions are held; attack buttons are presses (buffered by the reader).</summary>
    public struct FightInput
    {
        public bool left, right, up, down, charge;
        public bool light, heavy, special;
    }

    public class MoveDef
    {
        public string name;
        public int startup, active, recovery;
        public float damage;
        public float reachX, reachY, w, h;   // hitbox center offset (forward, up from feet) and size
        public int hitstun, blockstun;
        public float push;
        public bool knockdown, stand, special, air;
        public bool projectile;
        public float projSpeed;
        public bool rush;
        public float rushSpeed;
        public int hitEvery;                 // >0: can hit repeatedly every N frames (rush)
        public float cost;
        public int Total => startup + active + recovery;
    }

    public enum FState { Idle, Walk, Crouch, Jump, Attack, Charge, Block, Hit, Knockdown, Ko, Win }

    public class Fighter2D
    {
        public readonly Stand s;
        public readonly bool p1;
        public float x, y, vx, vy;
        public int face = 1;
        public FState st = FState.Idle;
        public int t, downT;
        public MoveDef move;
        public int lastHitT = -999;
        public bool moveHit, airUsed, holdBack;
        public float hp, max, gauge;
        public bool awake;
        public int stun, invuln, flash, comboTaken;
        public float walk, jumpV;
        public readonly MoveDef light, heavy, special, airLight, airHeavy;

        // Stand ability state
        public readonly AbilitySpec ab;
        public int freezeT, slowT, weakenT, sealT, hasteT, abilityCd, abilityUses, marks, barrierT;
        public float weakenMul = 1f, barrierX;
        public int barrierSide;
        public readonly Vector2[] hist = new Vector2[64];
        public int histI;
        public bool projSpawned;

        // Per-match stats
        public int roundsWon, maxCombo, specials, abilityCount;
        public float dealt;

        // Events for the view (consumed each frame)
        public readonly List<string> callouts = new List<string>();
        public int guardCalloutCooldown;

        public bool Grounded => y <= 0.01f && vy <= 0;

        public Fighter2D(Stand s, bool p1)
        {
            this.s = s;
            this.p1 = p1;
            ab = StandAbility.Resolve(s);
            var st = s.stats;
            max = 800 + st.durability * 80;
            walk = 3.2f + st.speed * 0.55f;
            jumpV = 19f + st.speed * 0.4f;

            light = new MoveDef
            {
                name = "弱攻撃", startup = 4, active = 3, recovery = 9, damage = 20 + st.power * 4,
                reachX = 62, reachY = 122, w = 74, h = 40, hitstun = 14, blockstun = 10, push = 3
            };
            heavy = new MoveDef
            {
                name = s.moves.attack.name, startup = 13 - st.speed, active = 5, recovery = 20, damage = 55 + st.power * 14,
                reachX = 95 + st.range * 12, reachY = 115, w = 120, h = 80, hitstun = 22, blockstun = 16, push = 8, stand = true
            };
            airLight = new MoveDef
            {
                name = "空中攻撃", startup = 4, active = 6, recovery = 6, damage = 24 + st.power * 4,
                reachX = 55, reachY = 70, w = 84, h = 64, hitstun = 14, blockstun = 10, push = 3, air = true
            };
            airHeavy = new MoveDef
            {
                name = s.moves.attack.name, startup = 8, active = 6, recovery = 10, damage = 45 + st.power * 10,
                reachX = 80, reachY = 55, w = 116, h = 80, hitstun = 20, blockstun = 14, push = 6, knockdown = true, stand = true, air = true
            };
            if (st.range >= 3)
            {
                special = new MoveDef
                {
                    name = s.moves.special.name, startup = 14, active = 1, recovery = 24, damage = 70 + st.power * 12 + st.range * 8,
                    reachX = 70, reachY = 125, w = 60, h = 60, hitstun = 26, blockstun = 18, push = 7, knockdown = true,
                    stand = true, special = true, projectile = true, projSpeed = 9f + st.speed * 1.2f, cost = 50
                };
            }
            else
            {
                special = new MoveDef
                {
                    name = s.moves.special.name, startup = 10, active = 24, recovery = 20, damage = 25 + st.power * 6,
                    reachX = 70, reachY = 100, w = 110, h = 130, hitstun = 18, blockstun = 12, push = 2,
                    stand = true, special = true, rush = true, rushSpeed = 9f + st.speed, hitEvery = 6, cost = 50
                };
            }
        }

        public void ResetRound(float startX, int startFace)
        {
            x = startX; y = 0; vx = vy = 0;
            face = startFace;
            st = FState.Idle; t = 0; downT = 0;
            move = null; moveHit = false; airUsed = false;
            hp = max;
            awake = false;
            stun = invuln = flash = comboTaken = 0;
            freezeT = slowT = weakenT = sealT = hasteT = abilityCd = abilityUses = marks = barrierT = 0;
            weakenMul = 1f;
            for (int i = 0; i < hist.Length; i++) hist[i] = new Vector2(x, y);
            histI = 0;
            callouts.Clear();
        }

        public void Callout(string text) { if (!string.IsNullOrEmpty(text)) callouts.Add(text); }
    }

    public class Projectile
    {
        public Fighter2D owner;
        public MoveDef m;
        public float x, y, vx;
        public bool alive = true;
        public int age;
    }

    public struct Ghost
    {
        public float x, y;
        public bool p1;
    }

    public struct Spark
    {
        public float x, y;
        public bool blocked, big;
    }

    /// <summary>The fighting game rules, stepped at a fixed 60 frames per second. No UI.</summary>
    public partial class FightSim
    {
        public const float StageW = 1600f;
        public const float Gravity = 1.1f;

        public readonly Fighter2D a, b;
        public readonly List<Projectile> projs = new List<Projectile>();
        public readonly List<Spark> sparks = new List<Spark>();   // consumed by the view
        public readonly List<Ghost> ghosts = new List<Ghost>();   // afterimages for rewind / teleport, consumed by the view
        public int hitstop;
        int frame;

        public FightSim(Stand p1, Stand p2)
        {
            a = new Fighter2D(p1, true);
            b = new Fighter2D(p2, false);
        }

        public void ResetRound()
        {
            a.ResetRound(500, 1);
            b.ResetRound(1100, -1);
            projs.Clear();
            sparks.Clear();
            hitstop = 0;
        }

        public void Step(ref FightInput ia, ref FightInput ib, bool control)
        {
            frame++;
            TickTimers(a);
            TickTimers(b);
            bool actA = Active(a), actB = Active(b);
            if (actA) Think(a, ref ia, b, control);
            if (actB) Think(b, ref ib, a, control);
            if (actA) Physics(a);
            if (actB) Physics(b);
            Separate();
            KeepOutOfBarrier(a, b);
            KeepOutOfBarrier(b, a);
            UpdateProjectiles();
            if (actA) CheckHit(a, b);
            if (actB) CheckHit(b, a);
            Record(a);
            Record(b);
        }

        // ------------------------------------------------------------ per-fighter logic

        static bool IdleLike(FState s) => s == FState.Idle || s == FState.Walk || s == FState.Crouch;

        void Think(Fighter2D f, ref FightInput inp, Fighter2D o, bool control)
        {
            f.t++;
            if (f.invuln > 0) f.invuln--;
            if (f.flash > 0) f.flash--;
            if (f.guardCalloutCooldown > 0) f.guardCalloutCooldown--;

            int dir = (inp.right ? 1 : 0) - (inp.left ? 1 : 0);
            if (IdleLike(f.st) && f.Grounded) f.face = o.x >= f.x ? 1 : -1;
            f.holdBack = dir != 0 && dir == -f.face;

            switch (f.st)
            {
                case FState.Ko:
                case FState.Win:
                    if (f.Grounded) f.vx *= 0.8f;
                    return;
                case FState.Hit:
                    f.vx *= 0.88f;
                    if (f.t >= f.stun && f.Grounded) { f.st = FState.Idle; f.t = 0; f.comboTaken = 0; }
                    return;
                case FState.Block:
                    f.vx *= 0.85f;
                    if (f.t >= f.stun) { f.st = FState.Idle; f.t = 0; }
                    return;
                case FState.Knockdown:
                    if (f.Grounded)
                    {
                        f.vx *= 0.8f;
                        f.downT++;
                        if (f.downT >= 45) { f.st = FState.Idle; f.t = 0; f.invuln = 15; f.comboTaken = 0; }
                    }
                    return;
                case FState.Attack:
                    AttackStep(f);
                    return;
                case FState.Charge:
                    if (!control || !inp.charge) { f.st = FState.Idle; f.t = 0; }
                    else AddGauge(f, 0.45f + f.s.stats.potential * 0.07f);
                    return;
            }

            if (!control)
            {
                if (f.Grounded) { f.vx = 0; if (f.st != FState.Jump) f.st = FState.Idle; }
                return;
            }

            if (f.Grounded)
            {
                if (inp.special && f.gauge >= f.special.cost)
                {
                    inp.special = false;
                    f.gauge -= f.special.cost;
                    f.specials++;
                    Start(f, f.special);
                    OnSpecialStart(f, o);
                    return;
                }
                if (inp.heavy) { inp.heavy = false; Start(f, f.heavy); return; }
                if (inp.light) { inp.light = false; Start(f, f.light); return; }
                if (inp.charge)
                {
                    if (f.st != FState.Charge) f.Callout(f.s.moves.charge.name);
                    f.st = FState.Charge; f.t = 0; f.vx = 0;
                    return;
                }
                if (inp.up)
                {
                    f.vy = f.jumpV;
                    f.vx = dir * f.walk * 1.15f * (f.hasteT > 0 ? 1.5f : 1f);
                    f.st = FState.Jump; f.t = 0; f.airUsed = false;
                    return;
                }
                if (inp.down)
                {
                    if (f.st != FState.Crouch) f.t = 0;
                    f.st = FState.Crouch; f.vx = 0;
                    return;
                }
                f.vx = dir * f.walk * (dir == -f.face ? 0.8f : 1f) * (f.hasteT > 0 ? 1.5f : 1f);
                var next = dir != 0 ? FState.Walk : FState.Idle;
                if (next != f.st) f.t = 0;
                f.st = next;
            }
            else if (!f.airUsed && f.st == FState.Jump)
            {
                if (inp.heavy) { inp.heavy = false; f.airUsed = true; Start(f, f.airHeavy); }
                else if (inp.light) { inp.light = false; f.airUsed = true; Start(f, f.airLight); }
            }
        }

        void Start(Fighter2D f, MoveDef m)
        {
            f.st = FState.Attack;
            f.t = 0;
            f.move = m;
            f.moveHit = false;
            f.projSpawned = false;
            f.lastHitT = -999;
            if (f.Grounded) f.vx = 0;
            if (m.stand) f.Callout(m.special ? $"『{m.name}』！" : m.name);
        }

        void AttackStep(Fighter2D f)
        {
            var m = f.move;
            if (m == null) { f.st = FState.Idle; f.t = 0; return; }
            if (f.hasteT > 0 && (frame & 1) == 0) f.t++;   // hasted: attacks play 1.5x faster
            bool activeNow = f.t >= m.startup && f.t < m.startup + m.active;
            if (m.rush && activeNow) f.vx = f.face * m.rushSpeed;
            else if (f.Grounded) f.vx *= 0.8f;

            if (m.projectile && !f.projSpawned && f.t >= m.startup)
            {
                f.projSpawned = true;
                projs.Add(new Projectile { owner = f, m = m, x = f.x + f.face * m.reachX, y = f.y + m.reachY, vx = f.face * m.projSpeed });
            }

            if (f.t >= m.Total)
            {
                f.st = f.Grounded ? FState.Idle : FState.Jump;
                f.t = 0;
                f.move = null;
            }
        }

        void Physics(Fighter2D f)
        {
            if (!f.Grounded) f.vy -= Gravity;
            f.x += f.vx;
            f.y += f.vy;
            if (f.y <= 0)
            {
                f.y = 0;
                if (f.vy < 0)
                {
                    f.vy = 0;
                    if (f.st == FState.Jump) { f.st = FState.Idle; f.t = 0; }
                    else if (f.st == FState.Attack && f.move != null && f.move.air) { f.st = FState.Idle; f.t = 0; f.move = null; }
                }
            }
            f.x = Mathf.Clamp(f.x, 50, StageW - 50);
        }

        void Separate()
        {
            const float minDist = 64f;
            if (Mathf.Abs(a.y - b.y) > 120) return;
            float dx = b.x - a.x;
            if (Mathf.Abs(dx) >= minDist) return;
            float sign = dx != 0 ? Mathf.Sign(dx) : a.face;
            float overlap = minDist - Mathf.Abs(dx);
            a.x -= sign * overlap / 2;
            b.x += sign * overlap / 2;
            // If one is pinned against a wall, push the other one instead.
            a.x = Mathf.Clamp(a.x, 50, StageW - 50);
            b.x = Mathf.Clamp(b.x, 50, StageW - 50);
            if (Mathf.Abs(b.x - a.x) < minDist)
            {
                if (a.x <= 50 || a.x >= StageW - 50) b.x = a.x + sign * minDist;
                else a.x = b.x - sign * minDist;
            }
        }

        // ------------------------------------------------------------ hits

        static Rect HitBox(Fighter2D f, MoveDef m) =>
            new Rect(f.x + f.face * m.reachX - m.w / 2, f.y + m.reachY - m.h / 2, m.w, m.h);

        static Rect HurtBox(Fighter2D o)
        {
            float h = o.st == FState.Crouch ? 105 : 170;
            return new Rect(o.x - 35, o.y, 70, h);
        }

        static bool CanBeHit(Fighter2D o) =>
            o.invuln == 0 && o.st != FState.Ko && o.st != FState.Knockdown && o.st != FState.Win;

        void CheckHit(Fighter2D f, Fighter2D o)
        {
            if (f.st != FState.Attack || f.move == null || f.move.projectile) return;
            var m = f.move;
            if (f.t < m.startup || f.t >= m.startup + m.active) return;
            if (m.hitEvery > 0) { if (f.t - f.lastHitT < m.hitEvery) return; }
            else if (f.moveHit) return;
            if (!CanBeHit(o)) return;
            var hb = HitBox(f, m);
            if (!hb.Overlaps(HurtBox(o))) return;
            f.moveHit = true;
            f.lastHitT = f.t;
            ApplyHit(f, o, m, f.face, hb.center.x, hb.center.y);
        }

        void UpdateProjectiles()
        {
            foreach (var p in projs)
            {
                if (!p.alive) continue;
                p.x += p.vx;
                p.age++;
                if (p.x < -100 || p.x > StageW + 100) { p.alive = false; continue; }
                var o = p.owner == a ? b : a;
                if (o.barrierT > 0 && Mathf.Abs(p.x - o.barrierX) < 30)
                {
                    p.alive = false;
                    sparks.Add(new Spark { x = p.x, y = p.y, blocked = true, big = true });
                    continue;
                }
                if (CanBeHit(o) && new Rect(p.x - 30, p.y - 30, 60, 60).Overlaps(HurtBox(o)))
                {
                    p.alive = false;
                    ApplyHit(p.owner, o, p.m, p.vx > 0 ? 1 : -1, p.x, p.y);
                }
            }
            // Opposing projectiles cancel each other out.
            for (int i = 0; i < projs.Count; i++)
                for (int j = i + 1; j < projs.Count; j++)
                {
                    var p = projs[i]; var q = projs[j];
                    if (!p.alive || !q.alive || p.owner == q.owner) continue;
                    if (Mathf.Abs(p.x - q.x) < 50 && Mathf.Abs(p.y - q.y) < 60)
                    {
                        p.alive = q.alive = false;
                        sparks.Add(new Spark { x = (p.x + q.x) / 2, y = (p.y + q.y) / 2, big = true });
                    }
                }
            projs.RemoveAll(p => !p.alive);
        }

        void ApplyHit(Fighter2D f, Fighter2D o, MoveDef m, int dir, float px, float py)
        {
            bool blocking = o.Grounded && o.holdBack && (IdleLike(o.st) || o.st == FState.Block);
            float dmg = m.damage * (f.awake ? 1f + f.s.stats.potential * 0.06f : 1f) * f.weakenMul;

            if (blocking)
            {
                o.st = FState.Block; o.t = 0; o.stun = m.blockstun;
                o.vx = dir * m.push * 0.7f;
                if (m.special) o.hp = Mathf.Max(0, o.hp - dmg * 0.15f);
                AddGauge(f, 3);
                AddGauge(o, 4);
                sparks.Add(new Spark { x = px, y = py, blocked = true });
                hitstop = 4;
                if (o.guardCalloutCooldown == 0) { o.Callout(o.s.moves.guard.name); o.guardCalloutCooldown = 90; }
                OnBlocked(o, f, dmg, dir);
                if (o.hp <= 0) Ko(o, dir);
                return;
            }

            if (Armored(o))
            {
                float taken = dmg * (1f - 0.15f * o.ab.power);
                o.hp -= taken;
                f.dealt += taken;
                o.flash = 3;
                sparks.Add(new Spark { x = px, y = py });
                hitstop = 5;
                if (o.guardCalloutCooldown == 0) { o.Callout($"『{o.s.abilityName}』{StandAbility.Label("armor")}"); o.guardCalloutCooldown = 90; }
                if (o.hp <= 0) Ko(o, dir);
                return;
            }

            o.comboTaken++;
            bool crit = Random.value < f.s.stats.precision * 0.04f;
            dmg *= Mathf.Pow(0.88f, o.comboTaken - 1) * (crit ? 1.3f : 1f);
            o.hp -= dmg;
            f.dealt += dmg;
            f.maxCombo = Mathf.Max(f.maxCombo, o.comboTaken);

            if (m.knockdown || !o.Grounded)
            {
                o.st = FState.Knockdown; o.t = 0; o.downT = 0;
                o.vy = 9; o.vx = dir * 5;
            }
            else
            {
                o.st = FState.Hit; o.t = 0; o.stun = m.hitstun;
                o.vx = dir * m.push;
            }
            o.move = null;
            o.flash = 5;
            AddGauge(f, dmg * 0.12f + 2);
            AddGauge(o, dmg * 0.08f);
            hitstop = (m.special || m.stand) ? 9 : 5;
            sparks.Add(new Spark { x = px, y = py, big = m.special || m.stand });
            if (crit) f.Callout("クリティカル！");

            if (o.hp <= 0) { Ko(o, dir); return; }
            OnHitEffect(f, o, m, dmg, dir);
            if (o.hp <= 0) { Ko(o, dir); return; }
            CheckAwake(o);
        }

        static void CheckAwake(Fighter2D o)
        {
            if (!o.awake && o.hp > 0 && o.hp <= o.max * 0.3f)
            {
                o.awake = true;
                o.Callout("覚醒！");
            }
        }

        static void Ko(Fighter2D o, int dir)
        {
            o.hp = 0;
            o.st = FState.Ko; o.t = 0;
            o.vy = 12; o.vx = dir * 6;
            o.move = null;
        }
    }

    /// <summary>Simple computer opponent.</summary>
    public class CpuBrain
    {
        int wait, holdT, blockT;
        FightInput hold;

        public FightInput Think(Fighter2D me, Fighter2D o, FightSim sim)
        {
            var inp = new FightInput();
            float dist = Mathf.Abs(o.x - me.x);
            bool towardRight = o.x > me.x;

            // Keep blocking for a while once decided.
            if (blockT > 0)
            {
                blockT--;
                inp.left = towardRight; inp.right = !towardRight;
                return inp;
            }

            // React to an incoming attack (decided once per attack).
            if (me.Grounded && o.st == FState.Attack && o.move != null && o.t == 1)
            {
                float reach = o.move.reachX + o.move.w / 2 + 60;
                if ((dist < reach || o.move.rush) && Random.value < 0.5f)
                {
                    blockT = o.move.Total;
                    inp.left = towardRight; inp.right = !towardRight;
                    return inp;
                }
            }

            // Incoming projectile: jump over it or block.
            foreach (var p in sim.projs)
            {
                if (p.owner == me) continue;
                bool approaching = (p.vx > 0 && p.x < me.x) || (p.vx < 0 && p.x > me.x);
                if (approaching && Mathf.Abs(p.x - me.x) < 260 && me.Grounded && me.st != FState.Attack)
                {
                    if (Random.value < 0.4f) { inp.up = true; inp.right = towardRight; inp.left = !towardRight; }
                    else { blockT = 20; inp.left = towardRight; inp.right = !towardRight; }
                    return inp;
                }
            }

            if (holdT > 0)
            {
                holdT--;
                inp.left = hold.left; inp.right = hold.right; inp.down = hold.down; inp.charge = hold.charge;
            }

            if (--wait > 0) return inp;
            wait = Random.Range(6, 16);

            hold = new FightInput();
            holdT = 0;
            float heavyReach = me.heavy.reachX + me.heavy.w / 2;
            float r = Random.value;

            if (me.gauge >= me.special.cost &&
                ((me.special.projectile && dist > 280) || (me.special.rush && dist < 300)) && r < 0.5f)
            {
                inp.special = true;
            }
            else if (dist < heavyReach + 20)
            {
                if (r < 0.42f) inp.light = true;
                else if (r < 0.72f) inp.heavy = true;
                else if (r < 0.82f) { inp.up = true; inp.right = towardRight; inp.left = !towardRight; }
                else { hold.left = towardRight; hold.right = !towardRight; holdT = wait; }
            }
            else if (dist > 520 && me.gauge < me.special.cost && r < 0.3f)
            {
                hold.charge = true; holdT = 40; wait = 40;
            }
            else
            {
                hold.right = towardRight; hold.left = !towardRight; holdT = wait;
                if (r < 0.08f) { inp.up = true; inp.right = towardRight; inp.left = !towardRight; }
            }

            if (holdT > 0) { inp.left |= hold.left; inp.right |= hold.right; inp.charge |= hold.charge; }
            return inp;
        }
    }
}
