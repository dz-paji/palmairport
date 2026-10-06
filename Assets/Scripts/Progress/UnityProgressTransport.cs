using System;
using System.Text;
using UnityEngine.Networking;

namespace IslandAirport
{
    /// <summary>Firestore REST with Firebase ID token; SDK and service-account secrets are unnecessary.</summary>
    public sealed class UnityProgressTransport : IProgressTransport
    {
        UnityWebRequest request;
        ProgressHttpResponse immediate;
        public void Start(ProgressHttpRequest input)
        {
            if (request != null || immediate != null) throw new InvalidOperationException("Progress request already active.");
            try
            {
                request = new UnityWebRequest(input.Url, input.Method);
                request.downloadHandler = new DownloadHandlerBuffer();
                if (!string.IsNullOrEmpty(input.Body))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(input.Body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("Authorization", "Bearer " + input.IdToken);
                request.timeout = 30;
                request.SendWebRequest();
            }
            catch (Exception)
            {
                if (request != null) request.Dispose();
                request = null;
                immediate = new ProgressHttpResponse { StatusCode = 0, Body = "" };
            }
        }
        public bool TryTakeResponse(out ProgressHttpResponse response)
        {
            if (immediate != null) { response = immediate; immediate = null; return true; }
            response = null;
            if (request == null || !request.isDone) return false;
            response = new ProgressHttpResponse { StatusCode = request.responseCode,
                Body = request.downloadHandler == null ? "" : request.downloadHandler.text };
            request.Dispose(); request = null; return true;
        }
        public void Cancel()
        {
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            immediate = null;
        }
        public void Dispose() { Cancel(); }
    }
}
