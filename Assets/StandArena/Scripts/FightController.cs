using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StandArena
{
    /// <summary>
    /// Runs one match (best of 3 rounds): fixed-step simulation, drawing, HUD and the result panel.
    /// Everything on the stage is laid out in a 1600 x 900 "stage" space that is scaled to fit the screen.
    /// </summary>
    public class FightController : MonoBehaviour
    {
        const float StageH = 900f;
        const float Ground = 150f;
        const int RoundFrames = 60 * 60;
        const float Step = 1f / 60f;

        enum Phase { Intro, Fight, Ko, RoundOver, MatchOver }

        RectTransform canvasRt, root, stage, fxLayer;
        Stand p1s, p2s;
        bool twoPlayer;
        Action<string> onExit;

        FightSim sim;
        CpuBrain cpu;
        KeyReader r1, r2;
        Phase phase;
        int phaseT, round, timer;
        float acc;
        Fighter2D roundWinner;

        // View
        FighterView v1, v2;
        Image hp1, hp2, trail1, trail2, gauge1, gauge2;
        Text timerText, centerText, combo1, combo2, awake1, awake2, gaugeText1, gaugeText2;
        Image[] dots1, dots2;
        float trailR1 = 1, trailR2 = 1;
        int comboShow1, comboShow2;
        RectTransform shakeTarget;
        float shake;
        RawImage bgSigilL, bgSigilR;
        readonly Dictionary<Projectile, RawImage> projViews = new Dictionary<Projectile, RawImage>();
        readonly List<Fx> fx = new List<Fx>();
        Texture2D sig1, sig2;

        static Sprite circle, white;

        static Sprite White
        {
            get
            {
                if (white != null) return white;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = Color.white;
                tex.SetPixels(px);
                tex.Apply();
                white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                return white;
            }
        }

        class Fx
        {
            public RectTransform rt;
            public Graphic g;
            public int age, life;
            public bool spark;
            public float from, to;
        }

        public static FightController Begin(RectTransform canvas, Stand p1, Stand p2, bool twoPlayer, Action<string> onExit)
        {
            var go = new GameObject("Fight");
            var fc = go.AddComponent<FightController>();
            fc.Init(canvas, p1, p2, twoPlayer, onExit);
            return fc;
        }

        void Init(RectTransform canvas, Stand p1, Stand p2, bool two, Action<string> exit)
        {
            canvasRt = canvas; p1s = p1; p2s = p2; twoPlayer = two; onExit = exit;
            sim = new FightSim(p1, p2);
            cpu = two ? null : new CpuBrain();
            r1 = KeyReader.Player1(!two);
            r2 = two ? KeyReader.Player2() : null;
            sig1 = Sigil.Make(p1);
            sig2 = Sigil.Make(p2);
            BuildView();
            round = 1;
            StartRound();
        }

        static Sprite Circle
        {
            get
            {
                if (circle != null) return circle;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                        px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d));
                    }
                tex.SetPixels(px);
                tex.Apply();
                circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
                return circle;
            }
        }

        // ------------------------------------------------------------ flow

        void StartRound()
        {
            sim.ResetRound();
            timer = RoundFrames;
            phase = Phase.Intro;
            phaseT = 0;
            roundWinner = null;
            trailR1 = trailR2 = 1;
            foreach (var kv in projViews) if (kv.Value != null) Destroy(kv.Value.gameObject);
            projViews.Clear();
        }

        int IntroLength => round == 1 ? 210 : 120;

        void Update()
        {
            if (phase != Phase.MatchOver && Keys.Down(KeyId.Escape)) { Exit("back"); return; }

            r1.Poll();
            r2?.Poll();

            acc += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            int steps = 0;
            while (acc >= Step && steps < 4)
            {
                acc -= Step;
                steps++;
                Tick();
            }
            Render();
        }

        void Tick()
        {
            if (phase == Phase.MatchOver) return;
            if (sim.hitstop > 0) { sim.hitstop--; shake = Mathf.Max(shake, 6); return; }

            phaseT++;
            switch (phase)
            {
                case Phase.Intro:
                    StepSim(false);
                    if (phaseT >= IntroLength) { phase = Phase.Fight; phaseT = 0; }
                    break;

                case Phase.Fight:
                    StepSim(true);
                    timer--;
                    if (sim.a.st == FState.Ko || sim.b.st == FState.Ko)
                    {
                        roundWinner = sim.a.st == FState.Ko && sim.b.st == FState.Ko ? null : sim.a.st == FState.Ko ? sim.b : sim.a;
                        phase = Phase.Ko; phaseT = 0;
                    }
                    else if (timer <= 0)
                    {
                        float ra = sim.a.hp / sim.a.max, rb = sim.b.hp / sim.b.max;
                        roundWinner = Mathf.Approximately(ra, rb) ? null : ra > rb ? sim.a : sim.b;
                        phase = Phase.Ko; phaseT = 0;
                    }
                    break;

                case Phase.Ko:
                    // Slow motion for the first second, then let things settle.
                    if (phaseT > 60 || phaseT % 3 == 0) StepSim(false);
                    if (phaseT >= 150)
                    {
                        if (roundWinner != null)
                        {
                            roundWinner.roundsWon++;
                            roundWinner.st = FState.Win; roundWinner.t = 0;
                        }
                        phase = Phase.RoundOver; phaseT = 0;
                    }
                    break;

                case Phase.RoundOver:
                    StepSim(false);
                    if (phaseT >= 100)
                    {
                        bool drawLimit = round >= 5;
                        if (sim.a.roundsWon >= 2 || sim.b.roundsWon >= 2 || drawLimit)
                        {
                            phase = Phase.MatchOver;
                            ShowResult();
                        }
                        else
                        {
                            round++;
                            StartRound();
                        }
                    }
                    break;
            }
        }

        void StepSim(bool control)
        {
            var ia = control ? r1.Frame() : new FightInput();
            FightInput ib;
            if (!control) ib = new FightInput();
            else if (twoPlayer) ib = r2.Frame();
            else ib = cpu.Think(sim.b, sim.a, sim);

            sim.Step(ref ia, ref ib, control);
            // Outside of play, ia/ib are empty, so any presses made during intros are dropped.
            r1.AfterStep(ia);
            if (twoPlayer) r2.AfterStep(control ? ib : new FightInput());
        }

        void Exit(string action)
        {
            if (root != null) Destroy(root.gameObject);
            Destroy(gameObject);
            onExit?.Invoke(action);
        }

        // ------------------------------------------------------------ view building

        RectTransform At(string name, Transform parent, float x, float y, float w, float h, Vector2 pivot)
        {
            var rt = UIKit.New(name, parent);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = pivot;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        Image Box(Transform parent, float x, float y, float w, float h, Color c, Vector2 pivot, bool round = false)
        {
            var rt = At("Box", parent, x, y, w, h, pivot);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            if (round) img.sprite = Circle;
            return img;
        }

        Text Txt(Transform parent, string s, float x, float y, float w, float h, int size, Color c, TextAnchor align, Vector2 pivot, bool outline = true)
        {
            var rt = At("Text", parent, x, y, w, h, pivot);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UIKit.Font;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = s;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0, 0, 0, 0.85f);
                o.effectDistance = new Vector2(3, -3);
            }
            return t;
        }

        void BuildView()
        {
            root = UIKit.New("FightRoot", canvasRt);
            UIKit.Stretch(root);
            UIKit.Bg(root, Color.black);

            stage = UIKit.New("Stage", root);
            stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 0.5f);
            stage.pivot = new Vector2(0.5f, 0.5f);
            stage.sizeDelta = new Vector2(FightSim.StageW, StageH);
            stage.gameObject.AddComponent<RectMask2D>();
            shakeTarget = UIKit.New("World", stage);
            UIKit.Stretch(shakeTarget);

            // Background
            Box(shakeTarget, 0, 0, FightSim.StageW, StageH, Pal.Ink, Vector2.zero);
            bgSigilL = SigilAt(shakeTarget, sig1, 380, 520, 560, 0.10f);
            bgSigilR = SigilAt(shakeTarget, sig2, 1220, 520, 560, 0.10f);
            for (int i = 0; i < 9; i++)
                Box(shakeTarget, i * 200, Ground, 2, StageH - Ground, new Color(1, 1, 1, 0.03f), new Vector2(0.5f, 0));
            Box(shakeTarget, 0, 0, FightSim.StageW, Ground, Pal.Deep, Vector2.zero);
            Box(shakeTarget, 0, Ground, FightSim.StageW, 3, new Color(Pal.Gold.r, Pal.Gold.g, Pal.Gold.b, 0.5f), new Vector2(0, 0.5f));

            v1 = new FighterView(this, shakeTarget, sim.a, Pal.Cyan, sig1);
            v2 = new FighterView(this, shakeTarget, sim.b, Pal.Rose, sig2);
            fxLayer = UIKit.New("Fx", shakeTarget);
            UIKit.Stretch(fxLayer);

            // HUD (not shaken)
            var hud = UIKit.New("Hud", stage);
            UIKit.Stretch(hud);
            BuildBar(hud, true, out hp1, out trail1);
            BuildBar(hud, false, out hp2, out trail2);
            Txt(hud, $"{p1s.owner}　<color=#45d9c8>{p1s.name}</color>", 60, 770, 700, 40, 26, Pal.Text, TextAnchor.LowerLeft, Vector2.zero);
            Txt(hud, $"<color=#ff5d7a>{p2s.name}</color>　{p2s.owner}", 1540, 770, 700, 40, 26, Pal.Text, TextAnchor.LowerRight, new Vector2(1, 0));
            awake1 = Txt(hud, "覚醒", 60, 735, 200, 30, 22, Pal.Gold, TextAnchor.LowerLeft, Vector2.zero);
            awake2 = Txt(hud, "覚醒", 1540, 735, 200, 30, 22, Pal.Gold, TextAnchor.LowerRight, new Vector2(1, 0));

            Box(hud, 800, 806, 120, 80, Pal.Deep, new Vector2(0.5f, 0));
            timerText = Txt(hud, "60", 800, 846, 120, 80, 58, Pal.Gold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            dots1 = new[] { Box(hud, 770, 780, 18, 18, Pal.Line, new Vector2(0.5f, 0.5f), true), Box(hud, 746, 780, 18, 18, Pal.Line, new Vector2(0.5f, 0.5f), true) };
            dots2 = new[] { Box(hud, 830, 780, 18, 18, Pal.Line, new Vector2(0.5f, 0.5f), true), Box(hud, 854, 780, 18, 18, Pal.Line, new Vector2(0.5f, 0.5f), true) };

            BuildGauge(hud, true, out gauge1, out gaugeText1);
            BuildGauge(hud, false, out gauge2, out gaugeText2);

            combo1 = Txt(hud, "", 70, 560, 400, 80, 54, Pal.Gold, TextAnchor.MiddleLeft, new Vector2(0, 0.5f));
            combo2 = Txt(hud, "", 1530, 560, 400, 80, 54, Pal.Gold, TextAnchor.MiddleRight, new Vector2(1, 0.5f));
            centerText = Txt(hud, "", 800, 500, 1400, 200, 110, Pal.Gold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));

            string hint = twoPlayer
                ? "1P: A/D 移動 W ジャンプ S しゃがみ J 弱 K スタンド L 必殺 I 溜め　｜　2P: ←→ 移動 ↑ ジャンプ ↓ しゃがみ テンキー1/2/3/4（, . / ;）　｜　後ろ入力でガード　Esc 中断"
                : "A/D 移動　W ジャンプ　S しゃがみ　後ろ入力でガード　J 弱攻撃　K スタンド攻撃　L 必殺技（SP50）　I 溜め　Esc 中断";
            Txt(hud, hint, 800, 92, 1560, 30, 18, Pal.Muted, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), false);
        }

        RawImage SigilAt(Transform parent, Texture tex, float x, float y, float size, float alpha)
        {
            var rt = At("Sigil", parent, x, y, size, size, new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.color = new Color(1, 1, 1, alpha);
            img.raycastTarget = false;
            return img;
        }

        void BuildBar(Transform hud, bool left, out Image fill, out Image trail)
        {
            float x = left ? 60 : 880;
            var frame = At("HpFrame", hud, x, 812, 660, 34, Vector2.zero);
            UIKit.Bg(frame, Pal.Deep, true);
            trail = Fill(frame, new Color(0.75f, 0.2f, 0.25f), left);
            fill = Fill(frame, Pal.Gold, left);
        }

        Image Fill(RectTransform frame, Color c, bool left)
        {
            var rt = UIKit.New("Fill", frame);
            UIKit.Stretch(rt, 3, 3, 3, 3);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            // The bars drain toward the screen edge; the side next to the timer stays put.
            img.fillOrigin = left ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
            img.sprite = White; // Filled images need a sprite
            img.fillAmount = 1;
            return img;
        }

        void BuildGauge(Transform hud, bool left, out Image fill, out Text label)
        {
            float x = left ? 60 : 1120;
            var frame = At("Gauge", hud, x, 28, 420, 26, Vector2.zero);
            UIKit.Bg(frame, Pal.Deep, true);
            var rt = UIKit.New("Fill", frame);
            UIKit.Stretch(rt, 3, 3, 3, 3);
            fill = rt.gameObject.AddComponent<Image>();
            fill.sprite = White;
            fill.raycastTarget = false;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = left ? (int)Image.OriginHorizontal.Left : (int)Image.OriginHorizontal.Right;
            Box(frame, 210, 13, 3, 26, Pal.Ink, new Vector2(0.5f, 0.5f));
            label = Txt(hud, "SP", left ? x : x + 420, 58, 300, 28, 20, Pal.Muted, left ? TextAnchor.LowerLeft : TextAnchor.LowerRight,
                left ? Vector2.zero : new Vector2(1, 0));
        }

        // ------------------------------------------------------------ rendering

        void Render()
        {
            if (stage == null) return;
            float s = Mathf.Min(canvasRt.rect.width / FightSim.StageW, canvasRt.rect.height / StageH);
            stage.localScale = new Vector3(s, s, 1);

            shake = Mathf.Max(0, shake - 60 * Time.unscaledDeltaTime);
            shakeTarget.anchoredPosition = shake > 0 ? UnityEngine.Random.insideUnitCircle * shake : Vector2.zero;

            bgSigilL.rectTransform.localRotation = Quaternion.Euler(0, 0, Time.time * 4);
            bgSigilR.rectTransform.localRotation = Quaternion.Euler(0, 0, -Time.time * 4);

            v1.Update(sim.a);
            v2.Update(sim.b);
            UpdateHud();
            UpdateProjectiles();
            UpdateFx();
        }

        void UpdateHud()
        {
            float ra = sim.a.hp / sim.a.max, rb = sim.b.hp / sim.b.max;
            hp1.fillAmount = ra; hp2.fillAmount = rb;
            trailR1 = trailR1 > ra ? Mathf.MoveTowards(trailR1, ra, 0.4f * Time.unscaledDeltaTime) : ra;
            trailR2 = trailR2 > rb ? Mathf.MoveTowards(trailR2, rb, 0.4f * Time.unscaledDeltaTime) : rb;
            trail1.fillAmount = trailR1; trail2.fillAmount = trailR2;
            hp1.color = ra < 0.3f ? Pal.Rose : Pal.Gold;
            hp2.color = rb < 0.3f ? Pal.Rose : Pal.Gold;
            awake1.enabled = sim.a.awake;
            awake2.enabled = sim.b.awake;

            timerText.text = Mathf.CeilToInt(Mathf.Max(0, timer) / 60f).ToString();
            for (int i = 0; i < 2; i++)
            {
                dots1[i].color = sim.a.roundsWon > i ? Pal.Gold : Pal.Line;
                dots2[i].color = sim.b.roundsWon > i ? Pal.Gold : Pal.Line;
            }

            SetGauge(gauge1, gaugeText1, sim.a);
            SetGauge(gauge2, gaugeText2, sim.b);

            UpdateCombo(combo1, ref comboShow1, sim.b.comboTaken);
            UpdateCombo(combo2, ref comboShow2, sim.a.comboTaken);

            centerText.text = CenterMessage();
        }

        void SetGauge(Image fill, Text label, Fighter2D f)
        {
            fill.fillAmount = f.gauge / 100f;
            bool ready = f.gauge >= f.special.cost;
            fill.color = ready ? Pal.Gold : Pal.Cyan;
            label.text = ready ? $"SP {Mathf.FloorToInt(f.gauge)}　<color=#ffc93c>{f.special.name} OK</color>" : $"SP {Mathf.FloorToInt(f.gauge)}";
        }

        void UpdateCombo(Text t, ref int show, int combo)
        {
            if (combo >= 2) { t.text = $"{combo} HITS"; show = 60; }
            else if (show > 0) show--;
            else t.text = "";
        }

        string CenterMessage()
        {
            switch (phase)
            {
                case Phase.Intro:
                    if (round == 1 && phaseT < 90)
                        return $"<size=64><color=#45d9c8>{p1s.name}</color>\n<color=#efe8f4>VS</color>\n<color=#ff5d7a>{p2s.name}</color></size>";
                    int offset = round == 1 ? 90 : 0;
                    return phaseT - offset < 70 ? $"ROUND {round}" : "FIGHT!";
                case Phase.Ko:
                    return timer <= 0 && sim.a.st != FState.Ko && sim.b.st != FState.Ko ? "TIME UP" : "K.O.";
                case Phase.RoundOver:
                    if (roundWinner == null) return "<size=80>DRAW</size>";
                    return $"<size=64>{roundWinner.s.name}\nWIN</size>";
                default:
                    return "";
            }
        }

        void UpdateProjectiles()
        {
            foreach (var p in sim.projs)
            {
                if (!projViews.TryGetValue(p, out var img) || img == null)
                {
                    img = SigilAt(fxLayer, p.owner == sim.a ? sig1 : sig2, 0, 0, 90, 0.95f);
                    projViews[p] = img;
                }
                img.rectTransform.anchoredPosition = new Vector2(p.x, Ground + p.y);
                img.rectTransform.localRotation = Quaternion.Euler(0, 0, -p.age * 18f * Mathf.Sign(p.vx));
            }
            var dead = new List<Projectile>();
            foreach (var kv in projViews)
                if (!sim.projs.Contains(kv.Key)) { if (kv.Value != null) Destroy(kv.Value.gameObject); dead.Add(kv.Key); }
            foreach (var d in dead) projViews.Remove(d);
        }

        void UpdateFx()
        {
            foreach (var sp in sim.sparks)
            {
                var img = Box(fxLayer, sp.x, Ground + sp.y, 40, 40, sp.blocked ? Pal.Cyan : (sp.big ? Pal.Gold : Color.white), new Vector2(0.5f, 0.5f), true);
                fx.Add(new Fx { rt = img.rectTransform, g = img, life = 14, spark = true, from = 30, to = sp.big ? 190 : 120 });
                if (sp.big) shake = Mathf.Max(shake, 14);
            }
            sim.sparks.Clear();

            SpawnCallouts(sim.a, v1);
            SpawnCallouts(sim.b, v2);

            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var f = fx[i];
                f.age++;
                float k = (float)f.age / f.life;
                if (f.spark)
                {
                    float size = Mathf.Lerp(f.from, f.to, Mathf.Sqrt(k));
                    f.rt.sizeDelta = new Vector2(size, size);
                }
                else
                {
                    f.rt.anchoredPosition += new Vector2(0, 1.2f);
                }
                var c = f.g.color; c.a = 1 - k * k; f.g.color = c;
                if (f.age >= f.life) { Destroy(f.rt.gameObject); fx.RemoveAt(i); }
            }
        }

        void SpawnCallouts(Fighter2D f, FighterView v)
        {
            foreach (var text in f.callouts)
            {
                var t = Txt(fxLayer, text, f.x, Ground + 270, 700, 50, 34, f.p1 ? Pal.Cyan : Pal.Rose, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
                fx.Add(new Fx { rt = t.rectTransform, g = t, life = 55 });
            }
            f.callouts.Clear();
        }

        // ------------------------------------------------------------ result

        void ShowResult()
        {
            var me = sim.a; var en = sim.b;
            string title = me.roundsWon > en.roundsWon ? (twoPlayer ? "1P WIN" : "勝利") : en.roundsWon > me.roundsWon ? (twoPlayer ? "2P WIN" : "敗北") : "引き分け";
            Color tc = me.roundsWon >= en.roundsWon ? Pal.Gold : Pal.Rose;

            var dim = UIKit.New("Dim", root);
            UIKit.Stretch(dim);
            UIKit.Bg(dim, new Color(0, 0, 0, 0.7f));

            var panel = UIKit.VStack(dim, 16, 36, Pal.Panel, TextAnchor.UpperCenter);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(Mathf.Min(820, canvasRt.rect.width - 32), 0);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.Label(panel, title, 84, tc, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIKit.Label(panel, $"{me.s.name}  {me.roundsWon} - {en.roundsWon}  {en.s.name}", 28, Pal.Text, FontStyle.Bold, TextAnchor.MiddleCenter);
            UIKit.Label(panel,
                $"最大コンボ {me.maxCombo} / {en.maxCombo}　　必殺技 {me.specials} / {en.specials}回　　与ダメージ {Mathf.RoundToInt(me.dealt)} / {Mathf.RoundToInt(en.dealt)}",
                20, Pal.Muted, FontStyle.Normal, TextAnchor.MiddleCenter);

            if (ClaudeClient.HasKey)
            {
                var review = UIKit.Label(panel, "実況席が試合を振り返っています…", 24, Pal.Text, FontStyle.Normal, TextAnchor.UpperLeft);
                StartCoroutine(ClaudeClient.Ask(ReviewPrompt(title), "low", 2000,
                    text => { if (review != null) review.text = text.Trim(); },
                    err => { if (review != null) { review.text = "総評を取得できませんでした：" + err; review.color = Pal.Rose; } }));
            }

            var row = UIKit.HStack(panel, 12, false, TextAnchor.MiddleCenter);
            UIKit.Btn(row, "再戦", () => Exit("rematch"));
            if (!twoPlayer) UIKit.Btn(row, "次の刺客", () => Exit("next"), primary: false);
            UIKit.Btn(row, "スタンド画面へ", () => Exit("back"), primary: false);
        }

        string ReviewPrompt(string result)
        {
            var me = sim.a; var en = sim.b;
            string Who(Fighter2D f) => $"本体{f.s.owner}／スタンド「{f.s.name}」能力『{f.s.abilityName}』{f.s.ability}";
            return
                "あなたは異能バトル格闘ゲームの実況者です。次の試合結果をもとに、熱い実況口調の総評を日本語で3文、150字以内で書いてください。" +
                "スタンドの能力を絡めて、決め手になった場面が目に浮かぶようにしてください。本文だけを返すこと。\n\n" +
                $"1P: {Who(me)}\n2P: {Who(en)}\n" +
                $"結果: {result}（ラウンド {me.roundsWon}-{en.roundsWon}）\n" +
                $"最大コンボ 1P {me.maxCombo} / 2P {en.maxCombo}、必殺技の使用 1P {me.specials}回 / 2P {en.specials}回、" +
                $"与ダメージ 1P {Mathf.RoundToInt(me.dealt)} / 2P {Mathf.RoundToInt(en.dealt)}、" +
                $"1Pの覚醒 {(me.awake ? "あり" : "なし")}、2Pの覚醒 {(en.awake ? "あり" : "なし")}";
        }

        // ------------------------------------------------------------ fighter drawing

        class FighterView
        {
            readonly RectTransform rootRt, body, arm, standRt, shield, shadow;
            readonly Image torso, head, legL, legR, armImg, shieldImg;
            readonly RawImage standImg;
            readonly Color accent, headCol, legCol;

            public FighterView(FightController fc, Transform parent, Fighter2D f, Color accent, Texture sigil)
            {
                this.accent = accent;
                headCol = Color.Lerp(accent, Color.white, 0.55f);
                legCol = Color.Lerp(accent, Color.black, 0.25f);

                shadow = fc.Box(parent, f.x, Ground, 120, 20, new Color(0, 0, 0, 0.5f), new Vector2(0.5f, 0.5f), true).rectTransform;
                rootRt = fc.At("Fighter", parent, f.x, Ground, 0, 0, new Vector2(0.5f, 0));

                standImg = fc.SigilAt(rootRt, sigil, 0, 0, 230, 0.45f);
                standRt = standImg.rectTransform;
                standRt.anchorMin = standRt.anchorMax = new Vector2(0.5f, 0);

                body = fc.At("Body", rootRt, 0, 0, 0, 0, new Vector2(0.5f, 0));
                body.anchorMin = body.anchorMax = new Vector2(0.5f, 0);
                legL = Part(fc, body, -15, 0, 22, 78, legCol);
                legR = Part(fc, body, 15, 0, 22, 78, legCol);
                torso = Part(fc, body, 0, 74, 58, 76, accent);
                head = Part(fc, body, 0, 150, 48, 48, headCol, true);
                armImg = Part(fc, body, 6, 128, 46, 18, headCol);
                arm = armImg.rectTransform;
                arm.pivot = new Vector2(0, 0.5f);
                shieldImg = Part(fc, body, 60, 95, 150, 190, new Color(accent.r, accent.g, accent.b, 0.25f), true);
                shield = shieldImg.rectTransform;
                shield.pivot = new Vector2(0.5f, 0.5f);
            }

            static Image Part(FightController fc, RectTransform parent, float x, float y, float w, float h, Color c, bool round = false)
            {
                var img = fc.Box(parent, x, y, w, h, c, new Vector2(0.5f, 0), round);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
                return img;
            }

            public void Update(Fighter2D f)
            {
                rootRt.anchoredPosition = new Vector2(f.x, Ground + f.y);
                float air = Mathf.Clamp01(f.y / 250f);
                shadow.anchoredPosition = new Vector2(f.x, Ground);
                shadow.localScale = new Vector3(1 - air * 0.5f, 1 - air * 0.5f, 1);

                bool down = f.st == FState.Knockdown || f.st == FState.Ko;
                float yScale = f.st == FState.Crouch ? 0.68f : 1f;
                body.localScale = new Vector3(f.face, yScale, 1);
                float rot = 0;
                if (down) rot = f.Grounded ? 90 * f.face : 35 * f.face;
                else if (f.st == FState.Hit) rot = 14 * f.face;
                else if (f.st == FState.Block) rot = 6 * f.face;
                body.localRotation = Quaternion.Euler(0, 0, rot);

                // Arm: punches reach out on light attacks; raised on victory.
                float armW = 46, armAngle = 0;
                var m = f.move;
                if (f.st == FState.Attack && m != null && !m.stand)
                {
                    bool active = f.t >= m.startup && f.t < m.startup + m.active;
                    armW = active ? m.reachX + m.w / 2 - 8 : 30;
                    if (m.air) armAngle = -25;
                }
                if (f.st == FState.Win) armAngle = 70;
                if (f.st == FState.Charge) armAngle = 40;
                arm.sizeDelta = new Vector2(armW, 18);
                arm.localRotation = Quaternion.Euler(0, 0, armAngle);

                // Flash white when hit.
                var tc = f.flash > 0 ? Color.white : accent;
                torso.color = tc;
                head.color = f.flash > 0 ? Color.white : headCol;
                legL.color = legR.color = f.flash > 0 ? Color.white : legCol;

                shield.gameObject.SetActive(f.st == FState.Block);

                // The stand: floats behind, lunges out on stand attacks, glows while charging.
                Vector2 pos = new Vector2(-f.face * 55, 150);
                float scale = 0.9f, alpha = 0.45f;
                if (f.st == FState.Attack && m != null && m.stand)
                {
                    bool before = f.t < m.startup;
                    bool active = f.t >= m.startup && f.t < m.startup + m.active;
                    float k = before ? (float)f.t / Mathf.Max(1, m.startup) : active ? 1f : 1f - (float)(f.t - m.startup - m.active) / Mathf.Max(1, m.recovery);
                    k = Mathf.Clamp01(k);
                    var target = new Vector2(f.face * (m.reachX * 0.85f), m.reachY + 10);
                    if (m.rush && active) target += UnityEngine.Random.insideUnitCircle * 12;
                    pos = Vector2.Lerp(pos, target, k);
                    scale = Mathf.Lerp(0.9f, m.special ? 1.4f : 1.2f, k);
                    alpha = Mathf.Lerp(0.45f, 0.95f, k);
                }
                else if (f.st == FState.Charge)
                {
                    pos = new Vector2(0, 150);
                    scale = 1.15f + Mathf.Sin(Time.time * 12) * 0.05f;
                    alpha = 0.8f;
                }
                else if (f.awake)
                {
                    alpha = 0.6f + Mathf.Sin(Time.time * 6) * 0.1f;
                }
                if (down) alpha *= 0.4f;
                standRt.anchoredPosition = pos;
                standRt.localScale = new Vector3(scale, scale, 1);
                standRt.localRotation = Quaternion.Euler(0, 0, Time.time * 30 * f.face);
                standImg.color = new Color(1, 1, 1, alpha);
            }
        }
    }
}
