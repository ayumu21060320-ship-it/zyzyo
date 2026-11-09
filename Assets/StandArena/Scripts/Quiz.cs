using System.Collections.Generic;
using UnityEngine;

namespace StandArena
{
    /// <summary>
    /// Four personality axes.
    /// o: 攻(+) / 守(-)   f: 遠(+) / 近(-)   l: 計算(+) / 直感(-)   s: 単独(+) / 共闘(-)
    /// </summary>
    public struct Axes
    {
        public int o, f, l, s;

        public void Add(Option op) { o += op.o; f += op.f; l += op.l; s += op.s; }

        public string TypeCode =>
            (o >= 0 ? "攻" : "守") + (f >= 0 ? "遠" : "近") + (l >= 0 ? "計" : "直") + (s >= 0 ? "単" : "絆");

        public static Axes RandomAxes()
        {
            return new Axes
            {
                o = Random.Range(-6, 7),
                f = Random.Range(-6, 7),
                l = Random.Range(-6, 7),
                s = Random.Range(-6, 7)
            };
        }
    }

    public class Option
    {
        public string text;
        public int o, f, l, s;
        public Option(string text, int o = 0, int f = 0, int l = 0, int s = 0)
        {
            this.text = text; this.o = o; this.f = f; this.l = l; this.s = s;
        }
    }

    public class Question
    {
        public string text;
        public Option[] options;
        public Question(string text, params Option[] options) { this.text = text; this.options = options; }
    }

    public static class Quiz
    {
        public static readonly Question[] Questions =
        {
            new Question("夜道で、背後から自分と同じ速さの足音がついてくる。",
                new Option("立ち止まって振り返る", o: 2, f: -1),
                new Option("店のガラスに映る姿で正体を確かめる", f: 2, l: 1),
                new Option("友人に電話をかけ、話しながら歩く", s: -2, o: -1)),
            new Question("テストで、どうしても答えがわからない問題がある。",
                new Option("直感でいちばんそれっぽいのを書く", l: -2),
                new Option("消去法で選択肢を絞り込む", l: 2),
                new Option("捨てて、確実な問題の見直しに時間を使う", o: -2, l: 1)),
            new Question("クラスの出し物を決める話し合い。",
                new Option("自分の案を強く推す", o: 2, s: 1),
                new Option("みんなの意見をまとめる役に回る", s: -2, o: -1),
                new Option("黙って当日の裏方を一人で引き受ける", s: 2, o: -1)),
            new Question("目の前でケンカが始まった。",
                new Option("割って入って止める", o: 1, f: -2),
                new Option("少し離れて状況を見極める", f: 2, l: 1),
                new Option("すぐに誰かを呼びに走る", s: -2)),
            new Question("初めての街で道に迷った。",
                new Option("勘を信じて歩き続ける", l: -2, s: 1),
                new Option("地図で最短ルートを調べ直す", l: 2),
                new Option("地元の人に聞く", s: -2)),
            new Question("チーム対戦ゲームで、好きな役割は？",
                new Option("前線で暴れるアタッカー", o: 2, f: -1),
                new Option("後方から狙い撃つスナイパー", f: 2, o: 1),
                new Option("味方を守るタンクか回復役", o: -2, s: -1)),
            new Question("大事な約束の日、外は大雨。",
                new Option("予定どおり突っ込む", o: 1, l: -1),
                new Option("前日から別のプランを用意してある", l: 2, o: -1),
                new Option("相手に連絡して一緒に決める", s: -2)),
            new Question("理不尽なことを言われた。",
                new Option("その場で言い返す", o: 2, f: -1),
                new Option("あとで証拠をそろえて反論する", l: 2, f: 1),
                new Option("受け流して、二度と関わらない", o: -2, s: 1)),
            new Question("秘密をひとつだけ持てるなら？",
                new Option("誰にも言えない特技", s: 2),
                new Option("ほんの少し先の未来が見える目", f: 1, l: 1),
                new Option("誰とでも仲良くなれる声", s: -2)),
            new Question("部屋の片づけはどうする？",
                new Option("気分が乗ったとき一気にやる", l: -2),
                new Option("毎日少しずつ、決めた手順で", l: 2, o: -1),
                new Option("人が来る前だけ全力でやる", s: -1, o: 1)),
            new Question("勝負の最終局面。あと一手で決まる。",
                new Option("一か八かの大技に賭ける", o: 2, l: -1),
                new Option("相手のミスを待つ", o: -2, l: 1),
                new Option("仲間に託す", s: -2)),
            new Question("予定のない休日。",
                new Option("一人でふらっと遠くへ出かける", s: 2, f: 1),
                new Option("家にこもって何かを作り込む", f: -1, l: 1, s: 1),
                new Option("けっきょく誰かを誘う", s: -2)),
        };

        public const string FreeQuestion = "あなたが絶対にゆずれないものは何？\n（ひと言でOK。スタンドの能力に反映されます）";

        public static readonly Dictionary<char, string> TypeWords = new Dictionary<char, string>
        {
            { '攻', "攻撃型" }, { '守', "防御型" }, { '遠', "遠距離" }, { '近', "近距離" },
            { '計', "計算派" }, { '直', "直感派" }, { '単', "一匹狼" }, { '絆', "共闘型" }
        };

        public static string Describe(string typeCode)
        {
            var parts = new List<string>();
            foreach (var c in typeCode ?? "")
                if (TypeWords.TryGetValue(c, out var w)) parts.Add(w);
            return string.Join("・", parts);
        }

        /// <summary>Axis scores → six stats graded 1 (E) to 5 (A), with a little randomness.</summary>
        public static StandStats ToStats(Axes ax)
        {
            float N(int v) => Mathf.Clamp(v / 6f, -1f, 1f);
            float o = N(ax.o), f = N(ax.f), l = N(ax.l), s = N(ax.s);
            int G(float v) => Mathf.Clamp(Mathf.RoundToInt(v + Random.Range(-0.6f, 0.6f)), 1, 5);
            return new StandStats
            {
                power = G(3 + o * 1.8f),
                durability = G(3 - o * 1.6f - s * 0.4f),
                speed = G(3 - f * 1.7f),
                range = G(3 + f * 1.8f),
                precision = G(3 + l * 1.8f),
                potential = G(3 - l * 1.3f - s * 0.7f)
            };
        }
    }
}
