using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace StandArena
{
    /// <summary>
    /// The whole game: screens, flow and battle UI.
    /// Created automatically on play (see Bootstrap), so it works from an empty scene.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        public static GameUI Instance { get; private set; }

        RectTransform canvasRt;
        RectTransform screenRoot;
        RectTransform col;           // current screen's content column
        Action current;              // builder for the current screen (used to rebuild)
        Vector2Int lastScreenSize;
        RectTransform spinner;       // rotating sigil on the generating screen

        // Game state
        string owner = "";
        readonly List<Option> answers = new List<Option>();
        string freeText = "";
        Stand me, enemy;
        Battle battle;
        bool busy;
        bool narrate = true;
        string note;                 // one-off message shown on the next screen
        Text genStatus;

        readonly Dictionary<Stand, Texture2D> sigils = new Dictionary<Stand, Texture2D>();

        // ------------------------------------------------------------ setup

        void Awake()
        {
            Instance = this;
            me = Stand.LoadSaved();
        }

        IEnumerator Start()
        {
            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Pal.Ink;
            }

            if (FindEventSystem() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }

            var cgo = new GameObject("StandArenaCanvas", typeof(RectTransform));
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
            scaler.matchWidthOrHeight = 0.5f;
            cgo.AddComponent<GraphicRaycaster>();
            canvasRt = (RectTransform)cgo.transform;

            yield return null; // let the canvas scaler settle before measuring
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            Show(TitleScreen);
        }

        static EventSystem FindEventSystem() => EventSystem.current;

        void Update()
        {
            if (spinner != null) spinner.Rotate(0, 0, -30f * Time.deltaTime);

            var size = new Vector2Int(Screen.width, Screen.height);
            if (current != null && size != lastScreenSize)
            {
                lastScreenSize = size;
                Show(current);
            }
        }

        int SidePad()
        {
            float w = canvasRt.rect.width;
            return Mathf.RoundToInt(Mathf.Max(28f, (w - 960f) / 2f));
        }

        void Show(Action build)
        {
            current = build;
            spinner = null;
            genStatus = null;
            if (screenRoot != null) Destroy(screenRoot.gameObject);
            var (root, content) = UIKit.ScrollScreen(canvasRt, SidePad());
            screenRoot = root;
            col = content;
            build();
        }

        Texture2D SigilOf(Stand s)
        {
            if (!sigils.TryGetValue(s, out var t) || t == null)
            {
                t = Sigil.Make(s);
                sigils[s] = t;
            }
            return t;
        }

        // ------------------------------------------------------------ screens

        void TitleScreen()
        {
            UIKit.Eyebrow(col, "STAND ARENA · 精神力診断");
            UIKit.Label(col, "スタンド・<color=#ffc93c>アリーナ</color>", 76, Pal.Text, FontStyle.Bold);
            UIKit.Label(col, "12の質問であなたの精神のかたちを測り、AIがあなただけのスタンドを生み出します。生まれたスタンドで、刺客や友達のスタンドと戦いましょう。", 26, Pal.Muted);

            var how = UIKit.HStack(col, 12, true, TextAnchor.UpperLeft);
            foreach (var (h, d) in new[] {
                ("診断", "場面ごとの選択と、ひとつの自由回答に答える"),
                ("覚醒", "4つの性格軸と能力値から、AIがスタンドを創作"),
                ("対戦", "AIの刺客、または友達のスタンドコードとバトル") })
            {
                var p = UIKit.Flex(UIKit.VStack(how, 6, 18, Pal.Panel));
                UIKit.Label(p, h, 24, Pal.Gold, FontStyle.Bold);
                UIKit.Label(p, d, 20, Pal.Muted);
            }

            var panel = UIKit.Panel(col);
            UIKit.Eyebrow(panel, "本体（あなた）の名前");
            var name = UIKit.Input(panel, "名前を入力", owner, charLimit: 24);
            var row = UIKit.HStack(panel, 12);
            UIKit.Btn(row, "診断をはじめる", () =>
            {
                owner = string.IsNullOrWhiteSpace(name.text) ? "名無しの本体" : name.text.Trim();
                answers.Clear();
                freeText = "";
                Show(QuizScreen);
            });
            if (me != null)
                UIKit.Btn(row, $"前回の「{me.name}」で遊ぶ", () => Show(ResultScreen), primary: false);

            var ai = UIKit.Panel(col, 10);
            if (ClaudeClient.HasKey)
            {
                UIKit.Label(ai, $"AI: 使用可能（{ClaudeClient.Model}）", 22, Pal.Cyan, FontStyle.Bold);
                UIKit.Label(ai, ClaudeClient.KeyFromEnv ? "環境変数 ANTHROPIC_API_KEY のキーを使っています。" : "設定画面で保存したキーを使っています。", 20, Pal.Muted);
            }
            else
            {
                UIKit.Label(ai, "AI: 未設定", 22, Pal.Rose, FontStyle.Bold);
                UIKit.Label(ai, "APIキーを設定すると、AIがスタンドを創作し、バトルを実況します。未設定でも簡易生成で遊べます。", 20, Pal.Muted);
            }
            UIKit.Btn(ai, "APIキーの設定", () => Show(SettingsScreen), primary: false, size: 22);
        }

        void SettingsScreen()
        {
            UIKit.Eyebrow(col, "SETTINGS");
            UIKit.Label(col, "Claude APIキー", 44, Pal.Text, FontStyle.Bold);
            var p = UIKit.Panel(col);
            UIKit.Label(p, "Claude Console（platform.claude.com）で発行したAPIキーを入力してください。キーはこのPCの中（PlayerPrefs）にだけ保存されます。", 22, Pal.Muted);
            UIKit.Label(p, "注意：このキーを入れたままビルドを配布しないでください。他の人にキーを使われてしまいます。", 22, Pal.Rose);
            if (ClaudeClient.KeyFromEnv)
                UIKit.Label(p, "環境変数 ANTHROPIC_API_KEY が設定されているので、そちらが優先されます。", 22, Pal.Cyan);
            var input = UIKit.Input(p, "sk-ant-...", "", password: true);
            var status = UIKit.Label(p, "", 20, Pal.Muted);
            var row = UIKit.HStack(p, 12);
            UIKit.Btn(row, "保存", () =>
            {
                if (string.IsNullOrWhiteSpace(input.text)) { status.text = "キーを入力してください"; return; }
                ClaudeClient.SaveKey(input.text);
                Show(TitleScreen);
            });
            UIKit.Btn(row, "保存したキーを削除", () => { ClaudeClient.SaveKey(""); status.text = "削除しました"; }, primary: false);
            UIKit.Btn(row, "戻る", () => Show(TitleScreen), primary: false);
        }

        void QuizScreen()
        {
            int i = answers.Count;
            int total = Quiz.Questions.Length + 1;

            var head = UIKit.HStack(col, 12);
            var ey = UIKit.Eyebrow(head, $"QUESTION {i + 1} / {total}");
            ey.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            UIKit.Btn(head, "戻る", () =>
            {
                if (answers.Count > 0) { answers.RemoveAt(answers.Count - 1); Show(QuizScreen); }
                else Show(TitleScreen);
            }, primary: false, size: 20);
            UIKit.Bar(col, (float)i / total, Pal.Gold, 6);

            if (i < Quiz.Questions.Length)
            {
                var q = Quiz.Questions[i];
                UIKit.Label(col, q.text, 40, Pal.Text, FontStyle.Bold);
                var opts = UIKit.VStack(col, 12);
                for (int k = 0; k < q.options.Length; k++)
                {
                    var op = q.options[k];
                    UIKit.Btn(opts, $"<color=#ffc93c>{"ABC"[k]}</color>   {op.text}", () =>
                    {
                        answers.Add(op);
                        Show(QuizScreen);
                    }, primary: false, align: TextAnchor.MiddleLeft);
                }
            }
            else
            {
                UIKit.Label(col, Quiz.FreeQuestion, 38, Pal.Text, FontStyle.Bold);
                var input = UIKit.Input(col, "例：妹の笑顔／自分で決めたルール／深夜のラーメン", freeText, multiline: true, charLimit: 60);
                UIKit.Btn(col, "スタンドを覚醒させる", () =>
                {
                    freeText = input.text.Trim();
                    Awaken();
                });
            }
        }

        void GeneratingScreen(string title, string typeCode)
        {
            var placeholder = new Stand { name = owner + Time.time, owner = "", type = typeCode };
            var img = UIKit.SigilImage(col, Sigil.Make(placeholder), 300, typeCode);
            spinner = (RectTransform)img.transform;
            UIKit.Label(col, title, 44, Pal.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
            genStatus = UIKit.Label(col,
                ClaudeClient.HasKey ? "AIがあなたの精神を読み解いています（20秒〜1分ほどかかります）" : "スタンドを形にしています",
                22, Pal.Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        void Awaken()
        {
            var ax = new Axes();
            foreach (var a in answers) ax.Add(a);
            Show(() => GeneratingScreen("スタンド覚醒中…", ax.TypeCode));
            StartCoroutine(StandGenerator.Generate(owner, ax, freeText, false, (s, n) =>
            {
                me = s;
                me.Save();
                note = n;
                Show(ResultScreen);
            }));
        }

        void SummonRival()
        {
            var ax = Axes.RandomAxes();
            var rivalName = StandGenerator.RivalNames[Random.Range(0, StandGenerator.RivalNames.Length)];
            var want = StandGenerator.RivalWants[Random.Range(0, StandGenerator.RivalWants.Length)];
            Show(() => GeneratingScreen("刺客が近づいてくる…", ax.TypeCode));
            StartCoroutine(StandGenerator.Generate(rivalName, ax, want, true, (s, n) =>
            {
                enemy = s;
                note = n;
                Show(PreviewScreen);
            }));
        }

        void ShowNote()
        {
            if (string.IsNullOrEmpty(note)) return;
            var p = UIKit.VStack(col, 8, 20, Pal.Deep);
            UIKit.Label(p, note, 20, Pal.Rose);
            note = null;
        }

        void StandCard(Stand s, bool isEnemy)
        {
            var card = UIKit.Panel(col, 18);
            var top = UIKit.HStack(card, 24, false, TextAnchor.MiddleLeft);
            UIKit.SigilImage(top, SigilOf(s), 220, string.IsNullOrEmpty(s.type) ? "" : s.type.Substring(0, 1));
            var names = UIKit.VStack(top, 6);
            UIKit.Flex(names);
            UIKit.Label(names, $"本体：{s.owner}", 20, Pal.Muted);
            UIKit.Label(names, s.name, 44, isEnemy ? Pal.Rose : Pal.Text, FontStyle.Bold);
            UIKit.Label(names, s.reading, 20, Pal.Muted);
            if (!string.IsNullOrEmpty(s.origin))
                UIKit.Label(names, $"名前の由来：{s.origin}", 18, Pal.Muted);
            UIKit.Label(names, $"TYPE {s.type}  ·  {Quiz.Describe(s.type)}", 18, Pal.Gold, FontStyle.Bold);

            void Section(string head, string body)
            {
                var v = UIKit.VStack(card, 4);
                UIKit.Label(v, head, 18, Pal.Gold, FontStyle.Bold);
                UIKit.Label(v, body, 24);
            }
            Section("外見", s.appearance);
            Section($"能力『{s.abilityName}』", s.ability);
            Section("弱点", s.weakness);
            UIKit.Label(card, $"「{s.quote}」", 28, Pal.Text, FontStyle.Bold);
        }

        void StatsBlock(Stand s)
        {
            var p = UIKit.Panel(col, 12);
            UIKit.Eyebrow(p, "STAND PARAMETERS");
            for (int i = 0; i < 6; i++)
            {
                int g = s.stats.Get(i);
                var row = UIKit.HStack(p, 16);
                var n = UIKit.Label(row, StandStats.Names[i], 22);
                n.gameObject.AddComponent<LayoutElement>().preferredWidth = 170;
                UIKit.Bar(row, g / 5f, Pal.Gold);
                var gl = UIKit.Label(row, Stand.Grade(g), 28, Pal.Gold, FontStyle.Bold, TextAnchor.MiddleRight);
                gl.gameObject.AddComponent<LayoutElement>().preferredWidth = 40;
            }

            UIKit.Eyebrow(p, "MOVES");
            var kinds = (MoveKind[])Enum.GetValues(typeof(MoveKind));
            for (int r = 0; r < 2; r++)
            {
                var row = UIKit.HStack(p, 12, true, TextAnchor.UpperLeft);
                for (int c = 0; c < 2; c++)
                {
                    var k = kinds[r * 2 + c];
                    var m = s.moves.Get(k);
                    var box = UIKit.Flex(UIKit.VStack(row, 4, 16, Pal.Deep));
                    UIKit.Label(box, StandMoves.Label(k), 16, k == MoveKind.Special ? Pal.Gold : Pal.Muted, FontStyle.Bold);
                    UIKit.Label(box, m.name, 24, Pal.Text, FontStyle.Bold);
                    UIKit.Label(box, m.desc, 18, Pal.Muted);
                }
            }
        }

        void ResultScreen()
        {
            if (me == null) { Show(TitleScreen); return; }
            ShowNote();
            UIKit.Eyebrow(col, me.by == "ai" ? "YOUR STAND · AI GENERATED" : "YOUR STAND");
            StandCard(me, false);
            StatsBlock(me);

            var fight = UIKit.Panel(col);
            UIKit.Label(fight, "対戦する", 32, Pal.Text, FontStyle.Bold);
            var row = UIKit.HStack(fight, 12);
            UIKit.Btn(row, "刺客と戦う", SummonRival);
            UIKit.Btn(row, "友達のスタンドと戦う", () => Show(FriendScreen), primary: false);

            var share = UIKit.Panel(col, 12);
            UIKit.Label(share, "あなたのスタンドコード", 32, Pal.Text, FontStyle.Bold);
            UIKit.Label(share, "このコードを友達に送ると、友達はあなたのスタンドと対戦できます。", 22, Pal.Muted);
            var code = me.ToCode();
            var field = UIKit.Input(share, "", code, multiline: true);
            field.readOnly = true;
            var msg = UIKit.Label(share, "", 20, Pal.Cyan);
            UIKit.Btn(share, "コードをコピー", () =>
            {
                GUIUtility.systemCopyBuffer = code;
                msg.text = "クリップボードにコピーしました";
            }, primary: false);

            UIKit.Btn(col, "もう一度診断する", () =>
            {
                answers.Clear();
                freeText = "";
                Show(TitleScreen);
            }, primary: false);
        }

        void FriendScreen()
        {
            UIKit.Eyebrow(col, "FRIEND BATTLE");
            UIKit.Label(col, "友達のスタンドコードを貼り付け", 40, Pal.Text, FontStyle.Bold);
            var input = UIKit.Input(col, "STAND1. から始まるコード（Ctrl+V で貼り付け）", "", multiline: true);
            var err = UIKit.Label(col, "", 22, Pal.Rose);
            var row = UIKit.HStack(col, 12);
            UIKit.Btn(row, "読み込む", () =>
            {
                var s = Stand.FromCode(input.text);
                if (s == null)
                {
                    err.text = "コードを読み取れませんでした。STAND1. から最後まで、すべて貼り付けてください。";
                    return;
                }
                enemy = s;
                Show(PreviewScreen);
            });
            UIKit.Btn(row, "クリップボードから貼り付け", () => input.text = GUIUtility.systemCopyBuffer, primary: false);
            UIKit.Btn(row, "戻る", () => Show(ResultScreen), primary: false);
        }

        void PreviewScreen()
        {
            ShowNote();
            UIKit.Eyebrow(col, "ENEMY STAND", Pal.Rose);
            StandCard(enemy, true);
            StatsBlock(enemy);
            var row = UIKit.HStack(col, 12);
            UIKit.Btn(row, "バトル開始", StartBattle);
            UIKit.Btn(row, "やめる", () => Show(ResultScreen), primary: false);
        }

        // ------------------------------------------------------------ battle

        void StartBattle()
        {
            battle = new Battle(me, enemy);
            busy = false;
            Show(BattleScreen);
        }

        void FighterPanel(Transform parent, Fighter f, bool player)
        {
            var accent = player ? Pal.Cyan : Pal.Rose;
            var p = UIKit.Flex(UIKit.VStack(parent, 8, 18, Pal.Panel));
            var strip = UIKit.New("Strip", p);
            UIKit.Bg(strip, accent);
            strip.gameObject.AddComponent<LayoutElement>().preferredHeight = 6;

            UIKit.SigilImage(p, SigilOf(f.s), 150);
            UIKit.Label(p, f.s.owner, 18, Pal.Muted);
            UIKit.Label(p, f.s.name, 28, Pal.Text, FontStyle.Bold);
            UIKit.Bar(p, f.Ratio, accent, 16);
            var hpRow = UIKit.HStack(p, 8);
            var hpText = UIKit.Label(hpRow, $"HP {f.hp} / {f.max}", 20, Pal.Muted);
            hpText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            if (f.lastDelta != 0)
                UIKit.Label(hpRow, f.lastDelta > 0 ? $"-{f.lastDelta}" : $"+{-f.lastDelta}", 28,
                    f.lastDelta > 0 ? Pal.Rose : Pal.Cyan, FontStyle.Bold, TextAnchor.MiddleRight);

            var chips = new List<string>();
            if (f.charge) chips.Add("<color=#ffc93c>溜め</color>");
            if (f.awake) chips.Add("<color=#ffc93c>覚醒</color>");
            chips.Add(f.cd == 0 ? "<color=#ffc93c>必殺OK</color>" : $"必殺あと{f.cd}");
            UIKit.Label(p, string.Join("  ", chips), 18, Pal.Muted, FontStyle.Bold);
        }

        void BattleScreen()
        {
            var b = battle;
            var head = UIKit.HStack(col, 12);
            var ey = UIKit.Eyebrow(head, b.over != Outcome.None ? "BATTLE OVER" : $"TURN {b.turn}");
            ey.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            if (ClaudeClient.HasKey)
                UIKit.Btn(head, narrate ? "AI実況：ON" : "AI実況：OFF", () => { narrate = !narrate; Show(BattleScreen); },
                    primary: false, size: 20);

            var arena = UIKit.HStack(col, 16, true, TextAnchor.UpperLeft);
            FighterPanel(arena, b.p, true);
            FighterPanel(arena, b.e, false);

            ShowNote();

            if (b.over != Outcome.None && !busy)
            {
                var p = UIKit.Panel(col);
                var (text, c) = b.over == Outcome.Win ? ("勝利", Pal.Gold) : b.over == Outcome.Lose ? ("敗北", Pal.Rose) : ("引き分け", Pal.Gold);
                UIKit.Label(p, text, 80, c, FontStyle.Bold, TextAnchor.MiddleCenter);
                var row = UIKit.HStack(p, 12, false, TextAnchor.MiddleCenter);
                UIKit.Btn(row, "再戦", StartBattle);
                UIKit.Btn(row, "次の刺客", SummonRival, primary: false);
                UIKit.Btn(row, "自分のスタンドへ", () => Show(ResultScreen), primary: false);
            }
            else
            {
                UIKit.Label(col, busy ? "実況中…" : "技を選んでください", 22, Pal.Muted);
                var kinds = (MoveKind[])Enum.GetValues(typeof(MoveKind));
                for (int r = 0; r < 2; r++)
                {
                    var row = UIKit.HStack(col, 12, true, TextAnchor.UpperLeft);
                    for (int c = 0; c < 2; c++)
                    {
                        var k = kinds[r * 2 + c];
                        var m = b.p.s.moves.Get(k);
                        bool ok = !busy && b.CanUse(b.p, k);
                        string label = StandMoves.Label(k) + (k == MoveKind.Special && b.p.cd > 0 ? $" · あと{b.p.cd}" : "");
                        UIKit.Flex(UIKit.Btn(row,
                            $"<size=16><color=#{(k == MoveKind.Special ? "ffc93c" : "a597b3")}>{label}</color></size>\n{m.name}\n<size=18><color=#a597b3>{m.desc}</color></size>",
                            () => DoMove(k), primary: false, size: 26, align: TextAnchor.MiddleLeft, interactable: ok));
                    }
                }
            }

            // Log, newest first
            var log = UIKit.VStack(col, 18, 24, Pal.Deep);
            for (int i = b.log.Count - 1; i >= 0; i--)
            {
                var e = b.log[i];
                var v = UIKit.VStack(log, 4);
                UIKit.Label(v, e.title, 16, Pal.Gold, FontStyle.Bold);
                UIKit.Label(v, e.sys, 20, Pal.Muted);
                e.narText = UIKit.Label(v, e.nar ?? "", 26, Pal.Text, FontStyle.Bold);
                if (string.IsNullOrEmpty(e.nar)) e.narText.gameObject.SetActive(false);
            }
        }

        void DoMove(MoveKind k)
        {
            var b = battle;
            if (busy || b.over != Outcome.None || !b.CanUse(b.p, k)) return;
            var ek = b.EnemyChoice();
            var title = $"TURN {b.turn}";
            var events = b.Resolve(k, ek);
            var entry = new LogEntry { title = title, sys = string.Join(" ／ ", events) };
            b.log.Add(entry);

            if (narrate && ClaudeClient.HasKey)
            {
                busy = true;
                entry.nar = "…";
                Show(BattleScreen);
                StartCoroutine(ClaudeClient.Ask(b.NarrationPrompt(k, ek, entry.sys), "low", 2000,
                    text => { entry.nar = text.Trim(); AfterTurn(); },
                    err => { entry.nar = null; note = "実況を取得できませんでした：" + err; AfterTurn(); }));
            }
            else
            {
                AfterTurn();
            }
        }

        void AfterTurn()
        {
            busy = false;
            if (battle.over != Outcome.None) battle.log.Add(battle.FinishEntry());
            if (current != null && current.Method.Name == nameof(BattleScreen)) Show(BattleScreen);
        }
    }

    /// <summary>Starts the game automatically when you press Play, even in an empty scene.</summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (GameUI.Instance != null) return;
            var go = new GameObject("StandArena");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GameUI>();
        }
    }
}
