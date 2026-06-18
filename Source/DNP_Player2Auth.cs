using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using SimpleJSON;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // PLAYER2 AUTH MANAGER
    // Mirrors EchoColony's Player2AuthManager pattern.
    //
    // Two auth flows:
    //   App:     POST localhost:4315/v1/login/web/{clientId} → p2Key
    //   Browser: Device Code OAuth flow → poll until approved
    //
    // After auth: every request sends GET /health (heartbeat)
    // ─────────────────────────────────────────────────────────────
    public static class DNP_Player2Auth
    {
        // ── Constants ─────────────────────────────────────────────
        public  const string GameClientId  = "019ea473-e5a3-7c73-83d5-572106f70840";
        public  const string WebApiBase    = "https://api.player2.game/v1";
        private const string AppLoginUrl   = "http://localhost:4315/v1/login/web/" + GameClientId;
        private const string HealthUrl     = WebApiBase + "/health";
        private const string GameKey       = "Rimworld-DungeonsAndPawns";

        // ── State ─────────────────────────────────────────────────
        private static string _p2Key              = "";
        private static bool   _isAuthenticating   = false;
        private static string _pendingUserCode     = "";
        private static string _connectionMethod    = "";
        private static float  _lastHeartbeatTime   = 0f;
        private const  float  HeartbeatInterval    = 60f; // seconds

        public static bool   IsAuthenticated  => !string.IsNullOrEmpty(_p2Key);
        public static bool   IsAuthenticating => _isAuthenticating;
        public static string PendingUserCode  => _pendingUserCode;
        public static string ConnectionMethod => _connectionMethod;

        // ── Auth header ───────────────────────────────────────────
        public static string GetAuthHeader() =>
            string.IsNullOrEmpty(_p2Key) ? "" : "Bearer " + _p2Key;

        // ── Disconnect ────────────────────────────────────────────
        public static void Disconnect()
        {
            _p2Key           = "";
            _connectionMethod = "";
            _isAuthenticating = false;
            _pendingUserCode  = "";

            // Clear from settings too
            if (DNP_Mod.Settings != null)
            {
                DNP_Mod.Settings.player2ApiKey = "";
                DNP_Mod.Settings.Write();
            }

            Log.Message("[DungeonsAndPawns] Disconnected from Player2.");
        }

        // ── Restore saved key on startup ──────────────────────────
        public static void TryRestoreFromSettings()
        {
            if (DNP_Mod.Settings == null) return;
            string saved = DNP_Mod.Settings.player2ApiKey;
            if (!string.IsNullOrEmpty(saved))
            {
                _p2Key            = saved;
                _connectionMethod = "Saved API key";
                Log.Message("[DungeonsAndPawns] Player2 key restored from settings.");
            }
        }

        // ── Set key from manual input ─────────────────────────────
        public static void SetKeyFromSettings(string key)
        {
            if (string.IsNullOrEmpty(key) || key == _p2Key) return;
            _p2Key            = key;
            _connectionMethod = "Manual API key";
            _lastHeartbeatTime = 0f;
            Log.Message("[DungeonsAndPawns] Player2 key set manually.");
        }

        // ── Auth via local Player2 App ────────────────────────────
        public static IEnumerator AuthenticateViaApp(Action<bool> onComplete)
        {
            if (_isAuthenticating) yield break;
            _isAuthenticating = true;
            _pendingUserCode  = "";

            Log.Message("[DungeonsAndPawns] Player2: Connecting via local App...");

            var request = new UnityWebRequest(AppLoginUrl, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}")),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 5;

            yield return request.SendWebRequest();

            _isAuthenticating = false;

#if UNITY_2020_2_OR_NEWER
            bool hasError = request.result != UnityWebRequest.Result.Success;
#else
            bool hasError = request.isNetworkError || request.isHttpError;
#endif

            if (hasError)
            {
                Log.Warning("[DungeonsAndPawns] Player2 App not reachable: " + request.error);
                onComplete?.Invoke(false);
                yield break;
            }

            try
            {
                var parsed = JSON.Parse(request.downloadHandler.text);
                string key = parsed["p2Key"]?.Value ?? "";
                if (string.IsNullOrEmpty(key))
                {
                    Log.Warning("[DungeonsAndPawns] Player2 App responded but returned no key. Are you logged in?");
                    onComplete?.Invoke(false);
                    yield break;
                }

                SetKey(key, "Player2 App (local)");
                onComplete?.Invoke(true);
            }
            catch (Exception ex)
            {
                Log.Error("[DungeonsAndPawns] Player2 App auth parse error: " + ex.Message);
                onComplete?.Invoke(false);
            }
        }

        // ── Auth via Browser (Player2 Device Code Flow) ───────────
        // Docs: POST /v1/login/device/new → deviceCode + verificationUriComplete
        //       POST /v1/login/device/token (poll) → p2Key
        public static IEnumerator AuthenticateViaBrowser()
        {
            if (_isAuthenticating) yield break;
            _isAuthenticating = true;
            _pendingUserCode  = "";

            Log.Message("[DungeonsAndPawns] Player2: Starting browser auth (device code flow)...");

            // Step 1: Start device flow — correct Player2 endpoint
            string deviceEndpoint = WebApiBase + "/login/device/new";
            string deviceBody     = "{\"client_id\":\"" + GameClientId + "\"}";

            var deviceReq = new UnityWebRequest(deviceEndpoint, "POST")
            {
                uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(deviceBody)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            deviceReq.SetRequestHeader("Content-Type", "application/json");
            deviceReq.timeout = 10;

            yield return deviceReq.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool deviceError = deviceReq.result != UnityWebRequest.Result.Success;
#else
            bool deviceError = deviceReq.isNetworkError || deviceReq.isHttpError;
#endif

            if (deviceError)
            {
                Log.Warning("[DungeonsAndPawns] Player2 device code request failed: " + deviceReq.error);
                _isAuthenticating = false;
                yield break;
            }

            string deviceCode    = "";
            string userCode      = "";
            string verificationUrl = "";
            int    expiresIn     = 300;
            int    pollInterval  = 5;

            try
            {
                var parsed      = JSON.Parse(deviceReq.downloadHandler.text);
                // Player2 uses camelCase: deviceCode, userCode, verificationUriComplete
                deviceCode      = parsed["deviceCode"]?.Value              ?? "";
                userCode        = parsed["userCode"]?.Value                ?? "";
                verificationUrl = parsed["verificationUriComplete"]?.Value
                               ?? parsed["verificationUri"]?.Value
                               ?? "https://player2.game/activate";
                expiresIn       = parsed["expiresIn"].AsInt  != 0 ? parsed["expiresIn"].AsInt  : 300;
                pollInterval    = parsed["interval"].AsInt   != 0 ? parsed["interval"].AsInt   : 5;

                if (string.IsNullOrEmpty(deviceCode))
                {
                    Log.Warning("[DungeonsAndPawns] Player2: missing deviceCode in response: "
                        + deviceReq.downloadHandler.text);
                    _isAuthenticating = false;
                    yield break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DungeonsAndPawns] Device code parse error: " + ex.Message);
                _isAuthenticating = false;
                yield break;
            }

            _pendingUserCode = userCode;
            Log.Message("[DungeonsAndPawns] Player2: Opening → " + verificationUrl + "  Code: " + userCode);

            Application.OpenURL(verificationUrl);

            // Step 2: Poll for p2Key — correct Player2 endpoint
            string tokenEndpoint = WebApiBase + "/login/device/token";
            float  elapsed       = 0f;

            while (elapsed < expiresIn)
            {
                yield return new WaitForSeconds(pollInterval);
                elapsed += pollInterval;

                // Player2 docs: client_id, device_code, grant_type
                string pollBody = "{\"client_id\":\"" + GameClientId + "\","
                    + "\"device_code\":\"" + deviceCode + "\","
                    + "\"grant_type\":\"urn:ietf:params:oauth:grant-type:device_code\"}";

                var pollReq = new UnityWebRequest(tokenEndpoint, "POST")
                {
                    uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(pollBody)),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                pollReq.SetRequestHeader("Content-Type", "application/json");
                pollReq.timeout = 8;

                yield return pollReq.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool pollError = pollReq.result != UnityWebRequest.Result.Success;
#else
                bool pollError = pollReq.isNetworkError || pollReq.isHttpError;
#endif

                if (!pollError)
                {
                    try
                    {
                        var    parsed = JSON.Parse(pollReq.downloadHandler.text);
                        // Player2 returns p2Key (not access_token)
                        string p2Key  = parsed["p2Key"]?.Value ?? "";
                        if (!string.IsNullOrEmpty(p2Key))
                        {
                            SetKey(p2Key, "Player2 Browser (Device Code)");
                            _isAuthenticating = false;
                            _pendingUserCode  = "";
                            yield break;
                        }
                    }
                    catch { /* keep polling */ }
                }
                // Non-200 during poll = still pending — keep going
                if (DNP_Mod.Settings?.debugMode == true)
                    Log.Message("[DungeonsAndPawns] Player2 poll: HTTP "
                        + pollReq.responseCode + " elapsed=" + elapsed + "s");
            }

            Log.Warning("[DungeonsAndPawns] Player2 browser auth timed out after " + expiresIn + "s.");
            _isAuthenticating = false;
            _pendingUserCode  = "";
        }

        // ── Heartbeat ─────────────────────────────────────────────
        // Call this before every chat request.
        // Sends GET /health with Bearer token every 60 seconds.
        public static IEnumerator SendHeartbeatIfNeeded()
        {
            if (!IsAuthenticated) yield break;

            float now = Time.realtimeSinceStartup;
            if (now - _lastHeartbeatTime < HeartbeatInterval) yield break;

            var req = UnityWebRequest.Get(HealthUrl);
            req.timeout = 4;
            req.SetRequestHeader("Authorization", GetAuthHeader());
            req.SetRequestHeader("player2-game-key", GameKey);

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif

            if (ok)
            {
                _lastHeartbeatTime = now;
                if (DNP_Mod.Settings?.debugMode == true)
                    Log.Message("[DungeonsAndPawns] Player2 heartbeat OK.");
            }
            else if (req.responseCode == 401)
            {
                // Key expired — clear it
                Log.Warning("[DungeonsAndPawns] Player2 heartbeat 401 — key expired. Please reconnect.");
                _p2Key = "";
                if (DNP_Mod.Settings != null) { DNP_Mod.Settings.player2ApiKey = ""; DNP_Mod.Settings.Write(); }
            }
            else
            {
                Log.Warning("[DungeonsAndPawns] Player2 heartbeat failed: " + req.error);
            }
        }

        // ── Internal ──────────────────────────────────────────────
        private static void SetKey(string key, string method)
        {
            _p2Key            = key;
            _connectionMethod = method;
            _lastHeartbeatTime = 0f; // force heartbeat on next request

            // Persist key
            if (DNP_Mod.Settings != null)
            {
                DNP_Mod.Settings.player2ApiKey = key;
                DNP_Mod.Settings.Write();
            }

            Log.Message("[DungeonsAndPawns] Player2 authenticated via " + method + ".");
            Messages.Message("Dungeons & Pawns: " + "DNP.Settings.P2ConnectedMsg".Translate(),
                MessageTypeDefOf.PositiveEvent, false);
        }
    }
}