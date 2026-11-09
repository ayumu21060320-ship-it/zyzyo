using System;
using System.Text;
using UnityEngine;

namespace StandArena
{
    public enum MoveKind { Attack, Charge, Guard, Special }

    [Serializable]
    public class StandStats
    {
        public int power = 3;
        public int speed = 3;
        public int range = 3;
        public int durability = 3;
        public int precision = 3;
        public int potential = 3;

        public static readonly string[] Names = { "破壊力", "スピード", "射程距離", "持続力", "精密動作性", "成長性" };

        public int Get(int i)
        {
            switch (i)
            {
                case 0: return power;
                case 1: return speed;
                case 2: return range;
                case 3: return durability;
                case 4: return precision;
                default: return potential;
            }
        }

        public void ClampAll()
        {
            power = Mathf.Clamp(power, 1, 5);
            speed = Mathf.Clamp(speed, 1, 5);
            range = Mathf.Clamp(range, 1, 5);
            durability = Mathf.Clamp(durability, 1, 5);
            precision = Mathf.Clamp(precision, 1, 5);
            potential = Mathf.Clamp(potential, 1, 5);
        }
    }

    [Serializable]
    public class MoveText
    {
        public string name = "";
        public string desc = "";
    }

    [Serializable]
    public class StandMoves
    {
        public MoveText attack = new MoveText();
        public MoveText charge = new MoveText();
        public MoveText guard = new MoveText();
        public MoveText special = new MoveText();

        public MoveText Get(MoveKind k)
        {
            switch (k)
            {
                case MoveKind.Attack: return attack;
                case MoveKind.Charge: return charge;
                case MoveKind.Guard: return guard;
                default: return special;
            }
        }

        public static string Label(MoveKind k)
        {
            switch (k)
            {
                case MoveKind.Attack: return "ATTACK 通常攻撃";
                case MoveKind.Charge: return "CHARGE 溜め";
                case MoveKind.Guard: return "GUARD 防御";
                default: return "SPECIAL 必殺";
            }
        }
    }

    /// <summary>A generated Stand. Serialized with JsonUtility (save data and share codes).</summary>
    [Serializable]
    public class Stand
    {
        public int v = 1;
        public string owner = "";
        public string type = "";      // e.g. 攻遠計単
        public string by = "local";   // "ai" or "local"
        public string name = "";
        public string reading = "";
        public string origin = "";    // 名前の由来
        public string appearance = "";
        public string abilityName = "";
        public string ability = "";
        public string weakness = "";
        public string quote = "";
        public StandStats stats = new StandStats();
        public StandMoves moves = new StandMoves();

        public static string Grade(int g) => "EDCBA"[Mathf.Clamp(g, 1, 5) - 1].ToString();

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(name) || moves == null) return false;
            foreach (MoveKind k in Enum.GetValues(typeof(MoveKind)))
            {
                var m = moves.Get(k);
                if (m == null || string.IsNullOrEmpty(m.name)) return false;
            }
            return true;
        }

        public void Sanitize()
        {
            owner = Trim(owner, 24);
            name = Trim(name, 40);
            reading = Trim(reading, 40);
            origin = Trim(origin, 80);
            appearance = Trim(appearance, 120);
            abilityName = Trim(abilityName, 24);
            ability = Trim(ability, 240);
            weakness = Trim(weakness, 120);
            quote = Trim(quote, 60);
            if (stats == null) stats = new StandStats();
            stats.ClampAll();
            foreach (MoveKind k in Enum.GetValues(typeof(MoveKind)))
            {
                var m = moves.Get(k);
                m.name = Trim(m.name, 24);
                m.desc = Trim(m.desc, 80);
            }
        }

        static string Trim(string s, int n)
        {
            s = s ?? "";
            return s.Length > n ? s.Substring(0, n) : s;
        }

        // ---- Share code ----
        const string Prefix = "STAND1.";

        public string ToCode() => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(this)));

        public static Stand FromCode(string code)
        {
            try
            {
                var raw = (code ?? "").Trim();
                if (raw.StartsWith(Prefix)) raw = raw.Substring(Prefix.Length);
                raw = raw.Replace("\n", "").Replace("\r", "").Replace(" ", "");
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
                var s = JsonUtility.FromJson<Stand>(json);
                if (s == null || !s.IsValid()) return null;
                s.Sanitize();
                return s;
            }
            catch
            {
                return null;
            }
        }

        // ---- Local save ----
        const string SaveKey = "stand_arena_me";

        public static Stand LoadSaved()
        {
            var json = PlayerPrefs.GetString(SaveKey, "");
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var s = JsonUtility.FromJson<Stand>(json);
                return s != null && s.IsValid() ? s : null;
            }
            catch { return null; }
        }

        public void Save()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
