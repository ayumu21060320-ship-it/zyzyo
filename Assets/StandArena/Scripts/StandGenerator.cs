using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace StandArena
{
    public static class StandGenerator
    {
        public static readonly string[] RivalNames = { "黒崎 玲", "真柴 カイ", "灰原 ミオ", "鳴海 ジン", "氷室 アゲハ", "八代 ロク", "霧島 セナ", "九条 トウマ" };
        public static readonly string[] RivalWants = { "自分のルール", "静寂", "勝利の記録", "過去の約束", "誰にも縛られない自由", "家族の名誉", "完璧な美しさ", "一番であること" };

        const string JsonShape =
            "{\"name\":\"スタンド名\",\"reading\":\"ひらがなの読み\",\"origin\":\"名前の由来（どんなアーティスト／コンビ名の雰囲気を、どうもじったか。40字以内）\"," +
            "\"appearance\":\"見た目（60字以内）\"," +
            "\"abilityName\":\"能力名（10字以内）\",\"ability\":\"能力の説明（120字以内）\",\"weakness\":\"弱点（60字以内）\"," +
            "\"quote\":\"本体が言いそうな決め台詞（30字以内）\",\"moves\":{\"attack\":{\"name\":\"\",\"desc\":\"\"}," +
            "\"charge\":{\"name\":\"\",\"desc\":\"\"},\"guard\":{\"name\":\"\",\"desc\":\"\"},\"special\":{\"name\":\"\",\"desc\":\"\"}}," + StandAbility.SchemaShape + "}";

        /// <summary>共闘型（絆）はお笑いコンビ風、一匹狼（単）は音楽アーティスト風の名前にする。</summary>
        public static bool ComedyStyle(Axes ax) => ax.s < 0;

        static string NameRule(bool comedy) => comedy
            ? "スタンド名は「日本のお笑いコンビ名」の雰囲気で付けること。2つの言葉の意外な組み合わせ、語感の良さ、ちょっとしたズレがある名前にする（例の方向性：食べ物＋施設、身近なモノ＋大げさな言葉）。\n"
            : "スタンド名は「日本の音楽アーティスト名（バンド名・ユニット名・ソロの名義）」の雰囲気で付けること。漢字・カタカナ・英語の混ぜ方や、略して呼びたくなる語感まで、それらしくする。\n";

        static string BuildPrompt(string owner, string typeCode, StandStats st, string free, bool rival, bool comedy)
        {
            var statTxt = string.Join(" / ", Enumerable.Range(0, 6).Select(i => $"{StandStats.Names[i]}:{Stand.Grade(st.Get(i))}"));
            return
                "あなたは異能バトル漫画の原作者です。性格診断の結果から、その人の精神が形になった守護霊のような能力体「スタンド」をひとつ、完全オリジナルで創作してください。\n" +
                "既存の漫画・アニメ・ゲームに登場する名前やキャラクター、決め台詞は使わないこと。\n" +
                NameRule(comedy) +
                "実在するアーティスト名・グループ名・コンビ名をそのまま使うのは禁止。元ネタが連想できる程度の「もじり」や、完全な創作にすること。読みはひらがなで付けること。\n\n" +
                (rival ? "これは主人公の前に立ちはだかる敵スタンド使いです。少し不穏で印象的にしてください。\n" : "") +
                $"本体の名前: {owner}\n" +
                $"タイプ: {typeCode}（{Quiz.Describe(typeCode)}）\n" +
                $"能力値（A〜E）: {statTxt}\n" +
                $"本人が絶対にゆずれないもの: {(string.IsNullOrEmpty(free) ? "（未回答）" : free)}\n\n" +
                "能力は「条件付き」で具体的にし、能力値と矛盾しないこと（射程Eなら手の届く範囲だけ、など）。ゆずれないものを能力のモチーフに必ず織り込むこと。弱点も必ず付けること。\n" +
                "技は次の4種類で、それぞれ能力に沿った名前と短い説明（40字以内）を付ける:\n" +
                "- attack: 通常攻撃\n- charge: 次の攻撃を強化するための溜め\n- guard: 防御と回復\n- special: 能力の真骨頂。防御を貫く必殺技\n\n" +
                StandAbility.SchemaHelp + "\n\n" +
                "次の形のJSONだけを返してください:\n" + JsonShape;
        }

        /// <summary>
        /// Generate a Stand. Uses Claude when an API key is set, otherwise (or on failure) the offline generator.
        /// done(stand, note): note is a message to show when AI generation fell back, else null.
        /// </summary>
        public static IEnumerator Generate(string owner, Axes ax, string free, bool rival, Action<Stand, string> done)
        {
            var stats = Quiz.ToStats(ax);
            var typeCode = ax.TypeCode;
            Stand result = null;
            string note = null;

            if (ClaudeClient.HasKey)
            {
                string reply = null, error = null;
                yield return ClaudeClient.Ask(BuildPrompt(owner, typeCode, stats, free, rival, ComedyStyle(ax)), "medium", 8000,
                    t => reply = t, e => error = e);

                if (reply != null)
                {
                    try
                    {
                        var json = ClaudeClient.ExtractJsonObject(reply);
                        var s = json != null ? JsonUtility.FromJson<Stand>(json) : null;
                        if (s != null && s.IsValid()) { result = s; result.by = "ai"; }
                        else error = "AIの応答がスタンドの形式になっていませんでした";
                    }
                    catch (Exception)
                    {
                        error = "AIの応答を読み取れませんでした";
                    }
                }
                if (result == null) note = $"AI生成に失敗したため、簡易生成に切り替えました（{error}）";
            }

            if (result == null) result = Offline(free, ax.o >= 0, ComedyStyle(ax));

            result.owner = owner;
            result.type = typeCode;
            result.stats = stats;
            StandAbility.Resolve(result);
            result.Sanitize();
            done(result, note);
        }

        // ---------- Offline generator ----------
        // 音楽アーティスト風：前半 + 後半（[表記, 読み]）
        static readonly string[][] MusicA = { new[] { "群青", "ぐんじょう" }, new[] { "午前二時", "ごぜんにじ" }, new[] { "硝子", "がらす" }, new[] { "放課後", "ほうかご" }, new[] { "電光", "でんこう" }, new[] { "銀河鉄", "ぎんがてつ" }, new[] { "夜更かし", "よふかし" }, new[] { "灼熱", "しゃくねつ" } };
        static readonly string[][] MusicB = { new[] { "サーカス", "さーかす" }, new[] { "シンドローム", "しんどろーむ" }, new[] { "ファンファーレ", "ふぁんふぁーれ" }, new[] { "ノイズ", "のいず" }, new[] { "オーケストラ", "おーけすとら" }, new[] { "ロマンス", "ろまんす" }, new[] { "少年団", "しょうねんだん" }, new[] { "パレヱド", "ぱれーど" } };
        // お笑いコンビ風
        static readonly string[][] ComedyA = { new[] { "おにぎり", "おにぎり" }, new[] { "わんぱく", "わんぱく" }, new[] { "真顔", "まがお" }, new[] { "やかん", "やかん" }, new[] { "満月", "まんげつ" }, new[] { "鉄板", "てっぱん" }, new[] { "ハイパー", "はいぱー" }, new[] { "二度寝", "にどね" } };
        static readonly string[][] ComedyB = { new[] { "ジャンクション", "じゃんくしょん" }, new[] { "町内会", "ちょうないかい" }, new[] { "銀行", "ぎんこう" }, new[] { "探偵団", "たんていだん" }, new[] { "ブラザーズ", "ぶらざーず" }, new[] { "ストレート", "すとれーと" }, new[] { "百貨店", "ひゃっかてん" }, new[] { "スタジアム", "すたじあむ" } };

        static readonly string[][] AttackAbilities =
        {
            new[] { "衝撃刻印", "殴った物体に『衝撃』を刻み、本体が指を鳴らした瞬間にまとめて炸裂させる。", "刻印は3つまで。指が鳴らせない状況では起爆できない。" },
            new[] { "加速打撃", "拳が触れるたび、相手の動きを一瞬だけ『巻き戻す』。連打するほど相手は前に進めない。", "本体も同じだけ疲労する。長期戦に弱い。" },
        };
        static readonly string[][] GuardAbilities =
        {
            new[] { "境界線", "本体が引いた線を、敵意を持つものは越えられない。", "線は一本だけ。本体が線をまたぐと消える。" },
            new[] { "鏡像返し", "受けた攻撃の『痛み』だけを鏡に映し、相手に返す。", "鏡が割れると能力が一日使えなくなる。" },
        };

        static AbilitySpec OfflineSpec(string abilityName)
        {
            switch (abilityName)
            {
                case "衝撃刻印": return new AbilitySpec { type = "mark", trigger = "stand", power = 1.2f, limit = 3 };
                case "加速打撃": return new AbilitySpec { type = "rewind", trigger = "hit", power = 1f, cost = "selfDamage", costValue = 2 };
                case "境界線": return new AbilitySpec { type = "barrier", trigger = "stand", duration = 3f, cost = "cooldown", costValue = 8 };
                default: return new AbilitySpec { type = "reflect", trigger = "stand", power = 1f, limit = 3 };
            }
        }
        static readonly string[] Looks =
        {
            "顔のない騎士のような人型。胸に古い時計がはめこまれている。",
            "細い針金で組まれた鳥。羽ばたくたびにノイズが鳴る。",
            "半透明のマネキン。全身に楽譜の模様が浮かんでいる。",
            "巨大な片腕だけの存在。指輪が五つ光っている。"
        };
        static readonly string[] Quotes = { "ここから先は、オレの領域だ。", "選ぶのは、いつだって自分だ。", "まだ終わってない。始まってもいない。", "その一歩、高くつくぞ。" };

        static T Pick<T>(T[] a) => a[Random.Range(0, a.Length)];

        static Stand Offline(string free, bool offensive, bool comedy)
        {
            var x = Pick(comedy ? ComedyA : MusicA);
            var y = Pick(comedy ? ComedyB : MusicB);
            var ab = Pick(offensive ? AttackAbilities : GuardAbilities);
            var motif = string.IsNullOrEmpty(free) ? "" : $"「{free}」への想いが強いほど、能力の精度が上がる。";
            return new Stand
            {
                by = "local",
                name = x[0] + y[0],
                reading = x[1] + y[1],
                origin = comedy ? "お笑いコンビ風。身近な言葉と大げさな言葉の組み合わせ" : "音楽アーティスト風。情景の言葉と音楽用語の組み合わせ",
                appearance = Pick(Looks),
                abilityName = ab[0],
                battle = StandAbility.Clamp(OfflineSpec(ab[0])),
                ability = ab[1] + motif,
                weakness = ab[2],
                quote = Pick(Quotes),
                moves = new StandMoves
                {
                    attack = new MoveText { name = Pick(new[] { "ラッシュ", "スマッシュ", "ストライク" }), desc = "正面から叩き込む連打。" },
                    charge = new MoveText { name = Pick(new[] { "チャージ・ノート", "深呼吸", "照準固定" }), desc = "力を溜め、次の一撃を強化する。" },
                    guard = new MoveText { name = Pick(new[] { "ガード・ライン", "シェル", "受け流し" }), desc = "攻撃を受け止め、少し回復する。" },
                    special = new MoveText { name = ab[0] + "・解放", desc = "能力の真骨頂。防御を貫く。" }
                }
            };
        }
    }
}
