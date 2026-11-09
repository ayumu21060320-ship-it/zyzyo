namespace StandArena
{
    public enum KeyId
    {
        A, D, W, S, J, K, L, I,
        Left, Right, Up, Down, Space, Escape, Enter,
        Num1, Num2, Num3, Num4, Comma, Period, Slash, Semicolon
    }

    /// <summary>Keyboard reading that works with either the old Input Manager or the new Input System.</summary>
    public static class Keys
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        static UnityEngine.KeyCode Code(KeyId k)
        {
            switch (k)
            {
                case KeyId.A: return UnityEngine.KeyCode.A;
                case KeyId.D: return UnityEngine.KeyCode.D;
                case KeyId.W: return UnityEngine.KeyCode.W;
                case KeyId.S: return UnityEngine.KeyCode.S;
                case KeyId.J: return UnityEngine.KeyCode.J;
                case KeyId.K: return UnityEngine.KeyCode.K;
                case KeyId.L: return UnityEngine.KeyCode.L;
                case KeyId.I: return UnityEngine.KeyCode.I;
                case KeyId.Left: return UnityEngine.KeyCode.LeftArrow;
                case KeyId.Right: return UnityEngine.KeyCode.RightArrow;
                case KeyId.Up: return UnityEngine.KeyCode.UpArrow;
                case KeyId.Down: return UnityEngine.KeyCode.DownArrow;
                case KeyId.Space: return UnityEngine.KeyCode.Space;
                case KeyId.Escape: return UnityEngine.KeyCode.Escape;
                case KeyId.Enter: return UnityEngine.KeyCode.Return;
                case KeyId.Num1: return UnityEngine.KeyCode.Keypad1;
                case KeyId.Num2: return UnityEngine.KeyCode.Keypad2;
                case KeyId.Num3: return UnityEngine.KeyCode.Keypad3;
                case KeyId.Num4: return UnityEngine.KeyCode.Keypad4;
                case KeyId.Comma: return UnityEngine.KeyCode.Comma;
                case KeyId.Period: return UnityEngine.KeyCode.Period;
                case KeyId.Slash: return UnityEngine.KeyCode.Slash;
                default: return UnityEngine.KeyCode.Semicolon;
            }
        }

        public static bool Held(KeyId k) => UnityEngine.Input.GetKey(Code(k));
        public static bool Down(KeyId k) => UnityEngine.Input.GetKeyDown(Code(k));
#elif ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Key Code(KeyId k)
        {
            switch (k)
            {
                case KeyId.A: return UnityEngine.InputSystem.Key.A;
                case KeyId.D: return UnityEngine.InputSystem.Key.D;
                case KeyId.W: return UnityEngine.InputSystem.Key.W;
                case KeyId.S: return UnityEngine.InputSystem.Key.S;
                case KeyId.J: return UnityEngine.InputSystem.Key.J;
                case KeyId.K: return UnityEngine.InputSystem.Key.K;
                case KeyId.L: return UnityEngine.InputSystem.Key.L;
                case KeyId.I: return UnityEngine.InputSystem.Key.I;
                case KeyId.Left: return UnityEngine.InputSystem.Key.LeftArrow;
                case KeyId.Right: return UnityEngine.InputSystem.Key.RightArrow;
                case KeyId.Up: return UnityEngine.InputSystem.Key.UpArrow;
                case KeyId.Down: return UnityEngine.InputSystem.Key.DownArrow;
                case KeyId.Space: return UnityEngine.InputSystem.Key.Space;
                case KeyId.Escape: return UnityEngine.InputSystem.Key.Escape;
                case KeyId.Enter: return UnityEngine.InputSystem.Key.Enter;
                case KeyId.Num1: return UnityEngine.InputSystem.Key.Numpad1;
                case KeyId.Num2: return UnityEngine.InputSystem.Key.Numpad2;
                case KeyId.Num3: return UnityEngine.InputSystem.Key.Numpad3;
                case KeyId.Num4: return UnityEngine.InputSystem.Key.Numpad4;
                case KeyId.Comma: return UnityEngine.InputSystem.Key.Comma;
                case KeyId.Period: return UnityEngine.InputSystem.Key.Period;
                case KeyId.Slash: return UnityEngine.InputSystem.Key.Slash;
                default: return UnityEngine.InputSystem.Key.Semicolon;
            }
        }

        public static bool Held(KeyId k)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb[Code(k)].isPressed;
        }

        public static bool Down(KeyId k)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb[Code(k)].wasPressedThisFrame;
        }
#else
        public static bool Held(KeyId k) => false;
        public static bool Down(KeyId k) => false;
#endif

        public static bool AnyHeld(KeyId[] ks) { foreach (var k in ks) if (Held(k)) return true; return false; }
        public static bool AnyDown(KeyId[] ks) { foreach (var k in ks) if (Down(k)) return true; return false; }
    }

    /// <summary>Turns keys into FightInput with a short buffer for attack presses.</summary>
    public class KeyReader
    {
        public KeyId[] left, right, up, down, light, heavy, special, charge;
        int lb, hb, sb;
        const int Buffer = 5;

        public static KeyReader Player1(bool includeArrows) => new KeyReader
        {
            left = includeArrows ? new[] { KeyId.A, KeyId.Left } : new[] { KeyId.A },
            right = includeArrows ? new[] { KeyId.D, KeyId.Right } : new[] { KeyId.D },
            up = includeArrows ? new[] { KeyId.W, KeyId.Up, KeyId.Space } : new[] { KeyId.W, KeyId.Space },
            down = includeArrows ? new[] { KeyId.S, KeyId.Down } : new[] { KeyId.S },
            light = new[] { KeyId.J },
            heavy = new[] { KeyId.K },
            special = new[] { KeyId.L },
            charge = new[] { KeyId.I }
        };

        public static KeyReader Player2() => new KeyReader
        {
            left = new[] { KeyId.Left },
            right = new[] { KeyId.Right },
            up = new[] { KeyId.Up },
            down = new[] { KeyId.Down },
            light = new[] { KeyId.Num1, KeyId.Comma },
            heavy = new[] { KeyId.Num2, KeyId.Period },
            special = new[] { KeyId.Num3, KeyId.Slash },
            charge = new[] { KeyId.Num4, KeyId.Semicolon }
        };

        /// <summary>Call every rendered frame to catch presses.</summary>
        public void Poll()
        {
            if (Keys.AnyDown(light)) lb = Buffer;
            if (Keys.AnyDown(heavy)) hb = Buffer;
            if (Keys.AnyDown(special)) sb = Buffer;
        }

        public FightInput Frame() => new FightInput
        {
            left = Keys.AnyHeld(left),
            right = Keys.AnyHeld(right),
            up = Keys.AnyHeld(up),
            down = Keys.AnyHeld(down),
            charge = Keys.AnyHeld(charge),
            light = lb > 0,
            heavy = hb > 0,
            special = sb > 0
        };

        /// <summary>After a sim step: presses the sim used are cleared, the rest age out.</summary>
        public void AfterStep(FightInput used)
        {
            lb = used.light ? lb - 1 : 0;
            hb = used.heavy ? hb - 1 : 0;
            sb = used.special ? sb - 1 : 0;
            if (lb < 0) lb = 0;
            if (hb < 0) hb = 0;
            if (sb < 0) sb = 0;
        }
    }
}
