using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace POSTechSupport.AI
{
    /// <summary>
    /// Step 3 — the MOUTH (GDD nguyên tắc bất biến #7). It is handed a line the policy already decided
    /// and may only reword it. It is never asked what to say, never sees the fault, and its output still
    /// has to clear GroundingGuard before anyone reads it.
    /// </summary>
    public interface ILlmClient
    {
        bool Enabled { get; }

        /// <summary>
        /// Reword <paramref name="line"/> in character. Coroutine rather than a return value because the
        /// template line is already on screen — this only replaces it if a better one arrives in time.
        /// Calls <paramref name="onDone"/> with null on any failure; the caller keeps the template.
        /// </summary>
        IEnumerator Rephrase(string systemPrompt, string line, Action<string> onDone);
    }

    /// <summary>
    /// The default, and the reason the game never depends on a model being installed: GDD §13 Phương án A
    /// says generate from templates and keep the LLM optional. DialoguePolicy's phrasing is already
    /// in-character, so "rephrasing" is a no-op.
    /// </summary>
    public class TemplateLlmClient : ILlmClient
    {
        public bool Enabled => false;
        public IEnumerator Rephrase(string systemPrompt, string line, Action<string> onDone)
        {
            onDone?.Invoke(null);
            yield break;
        }
    }

    /// <summary>
    /// GDD §13 Phương án B — a locally self-hosted model over HTTP (Ollama's /api/generate), so there is
    /// no cloud dependency and no API cost. Off unless GameConfigSO.useLlm is ticked; if the server is
    /// missing, slow, or returns junk, this reports failure and the template line simply stays.
    /// </summary>
    public class OllamaLlmClient : ILlmClient
    {
        private readonly string endpoint;
        private readonly string model;
        private readonly float timeoutSec;

        public bool Enabled => true;

        public OllamaLlmClient(string endpoint, string model, float timeoutSec)
        {
            this.endpoint = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434/api/generate" : endpoint;
            this.model = string.IsNullOrWhiteSpace(model) ? "llama3.2:3b" : model;
            this.timeoutSec = timeoutSec <= 0 ? 4f : timeoutSec;
        }

        [Serializable] private class Req { public string model; public string prompt; public bool stream; }
        [Serializable] private class Res { public string response; }

        public IEnumerator Rephrase(string systemPrompt, string line, Action<string> onDone)
        {
            var body = new Req
            {
                model = model,
                stream = false,
                prompt = $"{systemPrompt}\n\nReword this line in character. Keep the SAME meaning, same facts, " +
                         $"one or two short sentences, plain everyday English. Reply with the line only.\n\n\"{line}\"",
            };

            using var req = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body))),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.CeilToInt(timeoutSec),
            };
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[OllamaLlmClient] {req.error} — keeping the template line.");
                onDone?.Invoke(null);
                yield break;
            }

            string text = null;
            try { text = JsonUtility.FromJson<Res>(req.downloadHandler.text)?.response?.Trim().Trim('"'); }
            catch (Exception e) { Debug.LogWarning($"[OllamaLlmClient] bad response: {e.Message}"); }

            // A model that rambles has misunderstood the job; a two-line reply is all this ever needs.
            if (!string.IsNullOrWhiteSpace(text) && text.Length <= 240) onDone?.Invoke(text);
            else onDone?.Invoke(null);
        }
    }
}
