using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using SimpleJSON;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // LLM BRIDGE
    // Single entry point for all AI requests.
    // All methods are coroutines — never block the main thread.
    //
    // Usage:
    //   Find.Root.StartCoroutine(DNP_LLMBridge.Send(system, user, reply => { ... }));
    //   or: DNP_Mod.KickCoroutine(DNP_LLMBridge.Send(...));
    // ─────────────────────────────────────────────────────────────
    public static class DNP_LLMBridge
    {
        private static DNP_LLMSettings Cfg => DNP_Mod.Settings;

        // ── Main dispatch ─────────────────────────────────────────

        /// <summary>
        /// Send a prompt to the configured LLM provider.
        /// onResponse is called with the reply text (or an error string starting with "⚠").
        /// </summary>
        public static IEnumerator Send(string systemPrompt, string userPrompt,
                                        Action<string> onResponse)
        {
            if (Cfg == null)
            {
                onResponse?.Invoke("⚠ Settings not loaded.");
                yield break;
            }

            switch (Cfg.provider)
            {
                case DNP_LLMProvider.Player2:
                    yield return SendPlayer2(systemPrompt, userPrompt, onResponse);
                    break;
                case DNP_LLMProvider.OpenAI:
                    yield return SendOpenAI(systemPrompt, userPrompt, onResponse);
                    break;
                case DNP_LLMProvider.OpenRouter:
                    yield return SendOpenRouter(systemPrompt, userPrompt, onResponse);
                    break;
                case DNP_LLMProvider.Gemini:
                    yield return SendGemini(systemPrompt, userPrompt, onResponse);
                    break;
                case DNP_LLMProvider.Custom:
                    yield return SendCustom(systemPrompt, userPrompt, onResponse);
                    break;
                default:
                    onResponse?.Invoke("⚠ Unknown provider.");
                    break;
            }
        }

        // ── Connection test ───────────────────────────────────────

        public static IEnumerator TestConnection(Action<string> onResult)
        {
            const string testMsg = "Reply with exactly: 'D&P connection OK'";
            string result = null;

            yield return Send("You are a connection test assistant.", testMsg, r => result = r);

            // Wait up to 30s
            float waited = 0f;
            while (result == null && waited < 30f) { yield return null; waited += Time.deltaTime; }

            if (result == null)
                onResult?.Invoke("✗ Timeout — no response.");
            else if (result.StartsWith("⚠"))
                onResult?.Invoke("✗ " + result);
            else
                onResult?.Invoke("✓ Connected! Response: "
                    + result.Substring(0, Math.Min(result.Length, 60)));
        }

        // ── Player2 ───────────────────────────────────────────────

        private static IEnumerator SendPlayer2(string system, string user, Action<string> onResponse)
        {
            if (!DNP_Player2Auth.IsAuthenticated)
            {
                onResponse?.Invoke("⚠ Not connected to Player2. Go to Mod Settings and connect your account.");
                yield break;
            }

            // Heartbeat (tracks usage, required by Player2)
            yield return DNP_Player2Auth.SendHeartbeatIfNeeded();

            string endpoint = DNP_Player2Auth.WebApiBase + "/chat/completions";
            string body     = BuildOpenAIBody(system, user, null);

            yield return PostWithRetry(endpoint, body, onResponse, auth: DNP_Player2Auth.GetAuthHeader(),
                extraHeader: ("player2-game-key", "Rimworld-DungeonsAndPawns"));
        }

        // ── OpenAI ────────────────────────────────────────────────

        private static IEnumerator SendOpenAI(string system, string user, Action<string> onResponse)
        {
            if (string.IsNullOrEmpty(Cfg.openAiApiKey))
            {
                onResponse?.Invoke("⚠ Missing OpenAI API key. Add it in Mod Settings.");
                yield break;
            }

            string endpoint = "https://api.openai.com/v1/chat/completions";
            string body     = BuildOpenAIBody(system, user, Cfg.openAiModel);

            yield return PostWithRetry(endpoint, body, onResponse,
                auth: "Bearer " + Cfg.openAiApiKey);
        }

        // ── OpenRouter ────────────────────────────────────────────

        private static IEnumerator SendOpenRouter(string system, string user, Action<string> onResponse)
        {
            if (string.IsNullOrEmpty(Cfg.openRouterApiKey))
            {
                onResponse?.Invoke("⚠ Missing OpenRouter API key. Add it in Mod Settings.");
                yield break;
            }

            string endpoint = "https://openrouter.ai/api/v1/chat/completions";
            string body     = BuildOpenAIBody(system, user, Cfg.openRouterModel);

            yield return PostWithRetry(endpoint, body, onResponse,
                auth: "Bearer " + Cfg.openRouterApiKey,
                extraHeader: ("HTTP-Referer", "https://github.com/CarlosNahuelcoy/DungeonsAndPawns"));
        }

        // ── Gemini ────────────────────────────────────────────────

        private static IEnumerator SendGemini(string system, string user, Action<string> onResponse)
        {
            if (string.IsNullOrEmpty(Cfg.geminiApiKey))
            {
                onResponse?.Invoke("⚠ Missing Gemini API key. Add it in Mod Settings.");
                yield break;
            }

            string model    = string.IsNullOrEmpty(Cfg.geminiModel) ? "gemini-2.0-flash-001" : Cfg.geminiModel;
            string endpoint = "https://generativelanguage.googleapis.com/v1beta/models/"
                            + model + ":generateContent?key=" + Cfg.geminiApiKey;

            // Gemini uses a different body format
            string combined = string.IsNullOrEmpty(system)
                ? user
                : system + "\n\n" + user;

            string body = BuildGeminiBody(combined);

            int   retries   = 3;
            float retryWait = 1f;

            for (int attempt = 0; attempt < retries; attempt++)
            {
                var req = new UnityWebRequest(endpoint, "POST")
                {
                    uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                req.SetRequestHeader("Content-Type", "application/json");

                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool err = req.result != UnityWebRequest.Result.Success;
#else
                bool err = req.result != UnityWebRequest.Result.Success;
#endif

                if (!err)
                {
                    string reply = ParseGeminiResponse(req.downloadHandler.text);
                    if (Cfg.debugMode) Log.Message("[D&P Gemini] " + reply);
                    onResponse?.Invoke(reply);
                    yield break;
                }

                if ((req.responseCode == 429 || req.responseCode >= 500) && attempt < retries - 1)
                {
                    yield return new WaitForSeconds(retryWait);
                    retryWait *= 2f;
                    continue;
                }

                onResponse?.Invoke("⚠ Gemini error " + req.responseCode + ": " + req.error);
                yield break;
            }
        }

        // ── Custom / Local ────────────────────────────────────────

        private static IEnumerator SendCustom(string system, string user, Action<string> onResponse)
        {
            if (string.IsNullOrEmpty(Cfg.customEndpoint))
            {
                onResponse?.Invoke("⚠ Custom endpoint not configured. Add it in Mod Settings.");
                yield break;
            }

            string body = BuildOpenAIBody(system, user, Cfg.customModelName);

            yield return PostWithRetry(
                Cfg.customEndpoint, body, onResponse,
                auth: string.IsNullOrEmpty(Cfg.customApiKey) ? null : "Bearer " + Cfg.customApiKey);
        }

        // ── Shared HTTP helper ────────────────────────────────────

        /// <summary>
        /// POST with 3 retries on 429/5xx. Parses OpenAI-compatible JSON response.
        /// </summary>
        private static IEnumerator PostWithRetry(
            string endpoint, string body, Action<string> onResponse,
            string auth = null,
            (string key, string val)? extraHeader = null,
            int retries = 3)
        {
            float retryWait = 1f;

            for (int attempt = 0; attempt < retries; attempt++)
            {
                var req = new UnityWebRequest(endpoint, "POST")
                {
                    uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                req.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrEmpty(auth))
                    req.SetRequestHeader("Authorization", auth);

                if (extraHeader.HasValue)
                    req.SetRequestHeader(extraHeader.Value.key, extraHeader.Value.val);

                yield return req.SendWebRequest();

                string raw = req.downloadHandler.text;

#if UNITY_2020_2_OR_NEWER
                bool err = req.result != UnityWebRequest.Result.Success;
#else
                bool err = req.result != UnityWebRequest.Result.Success;
#endif

                if (!err)
                {
                    string reply = ParseOpenAIResponse(raw);
                    if (Cfg?.debugMode == true)
                        Log.Message("[D&P LLM] " + Cfg.provider + " response: " + reply);
                    onResponse?.Invoke(reply);
                    yield break;
                }

                // Retry on rate-limit or server error
                if ((req.responseCode == 429 || req.responseCode >= 500) && attempt < retries - 1)
                {
                    if (Cfg?.debugMode == true)
                        Log.Message("[D&P LLM] Retry " + (attempt+1) + "/" + retries
                            + " after " + retryWait + "s (HTTP " + req.responseCode + ")");
                    yield return new WaitForSeconds(retryWait);
                    retryWait *= 2f;
                    continue;
                }

                onResponse?.Invoke("⚠ Connection failed (HTTP " + req.responseCode + "): " + req.error);
                yield break;
            }
        }

        // ── Body builders ─────────────────────────────────────────

        private static string BuildOpenAIBody(string system, string user, string model)
        {
            var payload  = new JSONObject();
            if (!string.IsNullOrEmpty(model))
                payload["model"] = model;
            payload["stream"]     = false;
            payload["max_tokens"] = Cfg?.maxTokens ?? 800;

            var messages = new JSONArray();

            if (!string.IsNullOrEmpty(system))
            {
                var sys     = new JSONObject();
                sys["role"]    = "system";
                sys["content"] = system;
                messages.Add(sys);
            }

            var usr     = new JSONObject();
            usr["role"]    = "user";
            usr["content"] = user;
            messages.Add(usr);

            payload["messages"] = messages;
            return payload.ToString();
        }

        private static string BuildGeminiBody(string prompt)
        {
            var body     = new JSONObject();
            var contents = new JSONArray();
            var turn     = new JSONObject();
            var parts    = new JSONArray();
            var part     = new JSONObject();
            part["text"] = prompt;
            parts.Add(part);
            turn["parts"]    = parts;
            contents.Add(turn);
            body["contents"] = contents;

            // Generation config
            var genConfig            = new JSONObject();
            genConfig["maxOutputTokens"] = Cfg?.maxTokens ?? 400;
            body["generationConfig"] = genConfig;

            return body.ToString();
        }

        // ── Response parsers ──────────────────────────────────────

        /// <summary>
        /// Parses OpenAI-compatible /v1/chat/completions response.
        /// Works for Player2, OpenAI, OpenRouter, and Custom endpoints.
        /// </summary>
        private static string ParseOpenAIResponse(string json)
        {
            try
            {
                var root = JSON.Parse(json);

                // Standard: choices[0].message.content
                if (root["choices"] != null && root["choices"].AsArray.Count > 0)
                {
                    var choice = root["choices"][0];
                    string msg = choice["message"]?["content"]?.Value;
                    if (!string.IsNullOrEmpty(msg)) return CleanText(msg);
                    // Fallback: choices[0].text (some providers)
                    string text = choice["text"]?.Value;
                    if (!string.IsNullOrEmpty(text)) return CleanText(text);
                }

                // Some local providers: root.response
                if (root["response"] != null) return CleanText(root["response"]);

                return "⚠ Unrecognized response format.";
            }
            catch
            {
                return "⚠ Failed to parse response.";
            }
        }

        private static string ParseGeminiResponse(string json)
        {
            try
            {
                var root = JSON.Parse(json);
                var cands = root["candidates"]?.AsArray;
                if (cands != null && cands.Count > 0)
                {
                    var parts = cands[0]["content"]?["parts"]?.AsArray;
                    if (parts != null && parts.Count > 0)
                    {
                        string text = parts[0]["text"]?.Value;
                        if (!string.IsNullOrEmpty(text)) return CleanText(text);
                    }
                }
                return "⚠ Unrecognized Gemini response.";
            }
            catch
            {
                return "⚠ Failed to parse Gemini response.";
            }
        }

        private static string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            text = text.Trim();

            // Remove leading "Name: " patterns
            int colon = text.IndexOf(':');
            if (colon > 0 && colon < 30 && !text.Substring(0, colon).Contains(' '))
                text = text.Substring(colon + 1).Trim();

            // Trim everything after hashtags (social media artifacts)
            int hash = text.IndexOf(" #");
            if (hash > 20) text = text.Substring(0, hash).Trim();

            // Detect mid-sentence truncation and trim to last complete sentence.
            // A complete sentence ends with . ! ? or " or ) or similar.
            text = TrimToLastCompleteSentence(text);

            return text;
        }

        /// <summary>
        /// If the text ends mid-sentence (no terminal punctuation),
        /// trims back to the last complete sentence.
        /// This prevents showing "The air in the Whispering Woods is thick with the scent of wet pine and"
        /// </summary>
        private static string TrimToLastCompleteSentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Never trim if there's a tag present — the tag parser needs the full text
            if (Regex.IsMatch(text, @"\[[A-Z_]+[:\]]"))
                return text;

            char last = text[text.Length - 1];
            if (last == '.' || last == '!' || last == '?' ||
                last == '"' || last == '\'' || last == ')' || last == '…')
                return text;

            int lastEnd = -1;
            for (int i = text.Length - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '.' || c == '!' || c == '?')
                { lastEnd = i; break; }
            }

            if (lastEnd > text.Length / 3)
                return text.Substring(0, lastEnd + 1).Trim();

            return text;
        }
    }
}