using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace IslandAirport
{
    public sealed partial class AirportGame
    {
        const int ExportWidth = 960, ExportHeight = 540;
        SilentVideoEncoder videoEncoder;
        ReplayExportTimeline exportTimeline;
        ReplayFrame[] exportFrames;
        RenderTexture exportTarget;
        Texture2D exportPixels;
        byte[] pendingExportPixels;
        int exportFrameNumber;
        float exportStartedAt;
        string exportPath = string.Empty, exportedMatch = string.Empty;
        bool shareAfterExport;
        Task<ShareActionResult> shareTask;
        string shareMatchId;
        public bool ShareBusy { get { return shareTask != null; } }
        public bool ExportBusy { get { return videoEncoder != null; } }
        public bool ExportRendering { get; private set; }
        public float ExportFraction { get; private set; }
        public string ExportCaption { get; private set; } = string.Empty;
        public string ExportStatus { get; private set; } = "分享会导出本局无声回放视频";
        public string ExportedVideoPath { get; private set; } = string.Empty;
        public string SettlementMatchId { get { return NetworkShift ? AppState.Ensure().Room.MatchId : localMatchId; } }

        SettlementEligibility settlementEligibility;
        SettlementIdentity settlementIdentity;
        bool settlementLatched, settlementQueued;
        int settlementAuthRevision;
        string settlementUid = string.Empty, settlementMessage = string.Empty;
        public string SettlementStatus
        {
            get
            {
                var state = AppState.Ensure();
                if (settlementQueued && state.Auth != null && state.Auth.User != null && state.Auth.User.Uid == settlementUid && state.AccountProgress != null)
                    return state.AccountProgressNotice;
                return settlementMessage;
            }
        }
        void ResetSettlement()
        {
            CancelReplayExport();
            shareTask = null; shareMatchId = string.Empty;
            ExportedVideoPath = exportedMatch = string.Empty;
            ExportStatus = "分享会导出本局无声回放视频";
            settlementLatched = settlementQueued = false;
            settlementMessage = settlementUid = string.Empty;
            settlementEligibility = null; settlementIdentity = null;
        }
        void CaptureSettlementIdentity()
        {
            var state = AppState.Ensure();
            var user = state.Auth == null ? null : state.Auth.User;
            settlementIdentity = SettlementIdentity.CaptureIdentity(user, state.IsFakeAuth);
            settlementUid = user == null ? string.Empty : user.Uid;
            settlementAuthRevision = state.AuthSessionRevision;
            settlementEligibility = new SettlementEligibility(settlementUid, !state.IsFakeAuth && user != null,
                SettlementMatchId, LocalSeat, NetworkShift);
        }
        void ObserveSettlementIdentity()
        {
            if (settlementLatched || settlementEligibility == null) return;
            var state = AppState.Ensure();
            var user = state.Auth == null ? null : state.Auth.User;
            RoomManager room = state.Room;
            int seat = NetworkShift && room != null ? room.LocalSeat : 0;
            SeatInfo info = room != null && seat >= 0 && seat < room.Seats.Length ? room.Seats[seat] : null;
            settlementEligibility.Observe(user == null ? string.Empty : user.Uid, !state.IsFakeAuth && user != null && settlementAuthRevision == state.AuthSessionRevision,
                SettlementMatchId, seat, Started, Sandbox, room == null ? RoomPhase.Idle : room.Phase,
                info != null && info.Occupied, info == null || info.Bot, info != null && info.CreditEligible);
        }
        void CompleteSettlement()
        {
            if (settlementLatched) return;
            settlementLatched = true;
            var state = AppState.Ensure();
            var user = state.Auth == null ? null : state.Auth.User;
            RoomManager room = state.Room;
            int seat = NetworkShift && room != null ? room.LocalSeat : 0;
            SeatInfo info = room != null && seat >= 0 && seat < room.Seats.Length ? room.Seats[seat] : null;
            bool eligible = settlementEligibility != null && settlementEligibility.CanSettle(
                user == null ? string.Empty : user.Uid, !state.IsFakeAuth && user != null && settlementAuthRevision == state.AuthSessionRevision,
                SettlementMatchId, seat, Started, MatchFinished, Sandbox, room == null ? RoomPhase.Idle : room.Phase,
                info != null && info.Occupied, info == null || info.Bot, info != null && info.CreditEligible);
            settlementMessage = state.IsFakeAuth ? "测试模式 · 本局不会上传账号进度" :
                user == null ? "游客游玩 · 本局仅保留本机合作记忆" : "本局账号未满足结算条件，不同步云端进度";
            if (eligible && state.AccountProgress != null)
            {
                settlementQueued = state.AccountProgress.RecordSettlement(settlementIdentity, SettlementMatchId, RoundId,
                    "coral-bay-1", DisplayStars, DisplayScore, DisplayDeparted, Sim.CompletedTaskCount,
                    Mathf.RoundToInt(DisplayElapsed), true);
                settlementMessage = state.AccountProgressNotice;
            }
        }

        public void ShareReplay()
        {
            if (ExportBusy || ShareBusy) return;
            if (!string.IsNullOrEmpty(ExportedVideoPath) && exportedMatch == SettlementMatchId && File.Exists(ExportedVideoPath))
            { ShareExportedVideo(); return; }
            BeginReplayExport(true);
        }

        /// <summary>Internal export workflow, also exercised without opening external UI by editor acceptance.</summary>
        public void BeginReplayExport(bool openShareWhenReady)
        {
            if (ExportBusy) return;
            if (!ReplayAvailable || replay.Count < 2)
            { ExportStatus = "回放还未准备好，请稍后再试。"; return; }
            try
            {
                exportFrames = replay.ToArray();
                exportTimeline = new ReplayExportTimeline(exportFrames[exportFrames.Length - 1].Time);
                string directory = Path.Combine(Application.temporaryCachePath, "palmbay-replays");
                Directory.CreateDirectory(directory);
                exportPath = Path.Combine(directory, "palmbay-" + Guid.NewGuid().ToString("N") + ".mp4");
                string error;
                videoEncoder = SilentVideoEncoder.Create(exportPath, ExportWidth, ExportHeight, ReplayExportTimeline.FramesPerSecond, out error);
                if (videoEncoder == null) { ExportStatus = "视频导出失败：" + error; ReleaseExportResources(); return; }
                exportFrameNumber = 0; pendingExportPixels = null;
                shareAfterExport = openShareWhenReady;
                exportStartedAt = Time.realtimeSinceStartup;
                exportTarget = new RenderTexture(ExportWidth, ExportHeight, 24, RenderTextureFormat.ARGB32);
                exportPixels = new Texture2D(ExportWidth, ExportHeight, TextureFormat.RGB24, false);
                exportTarget.Create();
                ExportStatus = "正在准备无声视频…";
                RecordResultEvent("replay_export_started", null);
            }
            catch (Exception e) { FailReplayExport(e.Message); }
        }

        public void PumpReplayExport()
        {
            PumpVideoShare();
            if (videoEncoder == null) return;
            try
            {
                VideoEncodingState state = videoEncoder.State;
                if (state == VideoEncodingState.Failed || state == VideoEncodingState.Cancelled)
                { FailReplayExport(videoEncoder.Error); return; }
                if (state == VideoEncodingState.Completed)
                {
                    ExportedVideoPath = exportPath;
                    exportedMatch = SettlementMatchId;
                    TrimReplayCache();
                    ExportStatus = "无声视频已导出，可再次点击分享。";
                    RecordResultEvent("replay_export_completed", null);
                    bool openShare = shareAfterExport;
                    ReleaseExportResources();
                    if (openShare) ShareExportedVideo();
                    return;
                }
                if (Time.realtimeSinceStartup - exportStartedAt > 180f)
                { FailReplayExport("导出超时，请重试。"); return; }
                if (state != VideoEncodingState.Recording) return;
                if (exportFrameNumber >= exportTimeline.FrameCount)
                { videoEncoder.Finish(); ExportStatus = "正在封装视频…"; return; }
                if (pendingExportPixels == null) pendingExportPixels = RenderExportFrame(exportTimeline.TimeAt(exportFrameNumber));
                if (!videoEncoder.TryAddFrame(pendingExportPixels, true)) return;
                pendingExportPixels = null;
                exportFrameNumber++;
                ExportStatus = "正在导出无声视频 · " + (exportFrameNumber * 100 / exportTimeline.FrameCount) + "%";
            }
            catch (Exception e) { FailReplayExport(e.Message); }
        }

        byte[] RenderExportFrame(float time)
        {
            ReplayFrame frame = exportFrames[0];
            foreach (var candidate in exportFrames) { if (candidate.Time > time) break; frame = candidate; }
            ExportCaption = "本局真实回放 · 12 倍速 · 无声视频";
            if (NetworkShift && ReplayStatus != "共同回放已同步") ExportCaption = ReplayStatus;
            else if (frame.Events != null)
                foreach (var e in frame.Events)
                    if (e != null && e.Type == ShiftEventTypes.TaskFailed) ExportCaption = e.Text;
            if (frame.Time > time) ExportCaption = "开场记录缺失 · 画面为首个可用采样";
            else if (time - frame.Time > .75f && time < exportTimeline.Duration) ExportCaption = "此段记录缺失 · 画面停留在上一帧";
            ExportFraction = Mathf.Clamp01(time / exportTimeline.Duration);
            var hud = GetComponent<AirportHudCanvas>();
            RenderTexture previousTarget = View.targetTexture, previousActive = RenderTexture.active;
            float previousAspect = View.aspect;
            try
            {
                ApplyReplayFrame(frame);
                ExportRendering = true;
                View.targetTexture = exportTarget; View.aspect = (float)ExportWidth / ExportHeight;
                if (hud != null) hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
                View.Render();
                RenderTexture.active = exportTarget;
                exportPixels.ReadPixels(new Rect(0, 0, ExportWidth, ExportHeight), 0, 0, false);
                exportPixels.Apply(false, false);
                return exportPixels.GetRawTextureData();
            }
            finally
            {
                RenderTexture.active = previousActive; View.targetTexture = previousTarget; View.aspect = previousAspect;
                ExportRendering = false;
                if (replay.Count > 0) ApplyReplayFrame(replay[Mathf.Clamp(replayIndex, 0, replay.Count - 1)]);
                if (hud != null) hud.RefreshNow();
                Canvas.ForceUpdateCanvases();
            }
        }

        void TrimReplayCache()
        {
            // Only our disposable exports are rotated; user-saved/share-target copies are untouched.
            try
            {
                var files = new DirectoryInfo(Path.GetDirectoryName(exportPath)).GetFiles("palmbay-*.mp4");
                Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = 3; i < files.Length; i++)
                    if (files[i].FullName != exportPath && !files[i].Name.Contains(".encoding.")) files[i].Delete();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        void ShareExportedVideo()
        {
            if (ShareBusy) return;
            shareMatchId = SettlementMatchId;
            ExportStatus = "正在打开系统分享…";
            shareTask = ShareService.ShareVideoAsync(ExportedVideoPath);
        }
        void PumpVideoShare()
        {
            if (shareTask == null || !shareTask.IsCompleted) return;
            ShareActionResult result = shareTask.IsFaulted || shareTask.IsCanceled ? ShareActionResult.Failed : shareTask.Result;
            shareTask = null;
            if (shareMatchId != SettlementMatchId) return;
            ExportStatus = ShareService.GetResultMessage(result);
            RecordResultEvent("replay_share_result", new Dictionary<string, object> { { "result", result.ToString() } });
        }
        void RecordResultEvent(string name, IDictionary<string, object> parameters)
        {
            var events = AppState.Ensure().Events;
            if (events == null) return;
            string previous = events.MatchId;
            try { events.MatchId = SettlementMatchId; events.Enqueue(name, null, parameters); }
            finally { events.MatchId = previous; }
        }
        void FailReplayExport(string reason)
        {
            ExportStatus = "视频导出失败，请重试。" + (string.IsNullOrEmpty(reason) ? string.Empty : " " + reason);
            RecordResultEvent("replay_export_failed", null);
            CancelReplayExport(false);
        }
        public void CancelReplayExport() { CancelReplayExport(true); }
        void CancelReplayExport(bool showStatus)
        {
            if (videoEncoder == null) return;
            videoEncoder.Cancel();
            if (showStatus) ExportStatus = "视频导出已取消，可重新分享。";
            ReleaseExportResources();
        }
        void ReleaseExportResources()
        {
            if (videoEncoder != null) { videoEncoder.Dispose(); videoEncoder = null; }
            if (exportTarget != null) { exportTarget.Release(); Destroy(exportTarget); exportTarget = null; }
            if (exportPixels != null) { Destroy(exportPixels); exportPixels = null; }
            exportFrames = null; pendingExportPixels = null; exportTimeline = null;
            ExportRendering = false;
        }
        void OnDestroy() { CancelReplayExport(); }
    }
}
