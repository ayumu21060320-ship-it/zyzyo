using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace StandArena
{
    /// <summary>
    /// Minimal Claude Messages API client over UnityWebRequest.
    /// (The official C# SDK targets modern .NET and is not a drop-in for Unity, so this talks HTTP directly.)
    ///
    /// API key lookup order: ANTHROPIC_API_KEY environment variable, then the key saved from the in-game settings screen.
    /// The key is only meant for running the game on your own PC. Do NOT ship a build with a key inside it;
    /// for a public release, put a small server in between that holds the key.
    /// </summary>
    public static class ClaudeClient
    {
        public const string Model = "claude-opus-5-5";
        const string Url = "https://api.anthropic.com/v1/messages";
        const string ApiVersion = "2026-10-21";
        const string PrefsKey = "anthropic_api_key";

        public static string ApiKey
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
                if (!string.IsNullOrEmpty(env)) return env;
                return PlayerPrefs.GetString(PrefsKey, "");
            }
        }

        public static bool HasKey => !string.IsNullOrEmpty(ApiKey);
        public static bool KeyFromEnv => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

        public static void SaveKey(string key)
        {
            if (string.IsNullOrEmpty(key)) PlayerPrefs.DeleteKey(PrefsKey);
            else PlayerPrefs.SetString(PrefsKey, key.Trim());
            PlayerPrefs.Save();
        }

        [Serializable] class Msg { public string role; public string content; }
        [Serializable] class OutputConfig { public string effort; }
        [Serializable]
        class Request
        {
            public string model;
            public int max_tokens;
            public Msg[] messages;
            public OutputConfig output_config;
            public string fallbacks;
        }
        [Serializable] class Block { public string type; public string text; }
        [Serializable] class Response { public Block[] content; public string stop_reason; }
        [Serializable] class ErrorBody { public ErrorInner error; }
        [Serializable] class ErrorInner { public string type; public string message; }

        /// <summary>Send one user prompt; calls onOk with the concatenated text blocks, or onError with a readable message.</summary>
        public static IEnumerator Ask(string prompt, string effort, int maxTokens, Action<string> onOk, Action<string> onError)
        {
            if (!HasKey) { onError("APIキーが設定されていません"); yield break; }

            var body = new Request
            {
                model = Model,
                max_tokens = maxTokens,
                messages = new[] { new Msg { role = "user", content = prompt } },
                output_config = new OutputConfig { effort = effort },
                // If the model declines, the server retries on a suitable fallback model.
                fallbacks = "default"
            };

            using (var www = new UnityWebRequest(Url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("content-type", "application/json");
                www.SetRequestHeader("x-api-key", ApiKey);
                www.SetRequestHeader("anthropic-version", ApiVersion);
                www.SetRequestHeader("anthropic-beta", "server-side-fallback-2026-07-01");
                www.timeout = 180;

                yield return www.SendWebRequest();

                var raw = www.downloadHandler != null ? www.downloadHandler.text : "";
                if (www.result != UnityWebRequest.Result.Success)
                {
                    onError(DescribeError(www.responseCode, www.error, raw));
                    yield break;
                }

                Response res = null;
                try { res = JsonUtility.FromJson<Response>(raw); } catch { }
                if (res == null || res.content == null) { onError("AIの応答を読み取れませんでした"); yield break; }
                if (res.stop_reason == "refusal") { onError("AIがこの内容の生成を断りました"); yield break; }

                var sb = new StringBuilder();
                foreach (var b in res.content)
                    if (b != null && b.type == "text") sb.Append(b.text);
                if (sb.Length == 0) { onError("AIの応答が空でした"); yield break; }
                onOk(sb.ToString());
            }
        }

        static string DescribeError(long code, string netError, string raw)
        {
            string detail = netError;
            try
            {
                var e = JsonUtility.FromJson<ErrorBody>(raw);
                if (e != null && e.error != null && !string.IsNullOrEmpty(e.error.message)) detail = e.error.message;
            }
            catch { }

            switch (code)
            {
                case 401: return "APIキーが正しくありません（401）";
                case 403: return "このAPIキーでは利用できません（403）: " + detail;
                case 429: return "リクエストが多すぎます。少し待ってからもう一度（429）";
                case 529: return "AIが混み合っています。少し待ってからもう一度（529）";
                case 0: return "通信できませんでした: " + detail;
                default: return $"AIの呼び出しに失敗しました（{code}）: {detail}";
            }
        }

        /// <summary>Pull the first {...} object out of a reply that may have text or a code fence around it.</summary>
        public static string ExtractJsonObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int a = text.IndexOf('{');
            int b = text.LastIndexOf('}');
            return a >= 0 && b > a ? text.Substring(a, b - a + 1) : null;
        }
    }
}
