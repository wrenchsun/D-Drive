using System;
using System.Collections.Generic;
using System.Globalization;
using DDrive.Editor.Preview;
using DDrive.Editor.Presentation;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.4 MCP-7(2026-10-07) — ddrive_preview(open / play / stop / stop_all / sweep / status)。
    // ツール数の上限(§5.1、20 個)のため、spec の preview_open / preview_play / preview_sweep を 1 ツールの action にまとめた。
    // ツールはアダプタに徹する。確認用シーンは各 PreviewSceneSetup.TryOpenOrCreate、再生は各エディタが使う
    // Editor プレビュー用ドライバ(PreviewService / SceneVfxPreviewDriver / ScenePresentationPreviewDriver。実 Manager、ADR-4)を呼ぶだけ。
    //  - open は保存確認ダイアログを出さない(AI からは答えられない)。未保存シーンがあれば {blocked:"unsaved scene", scenes} で返す
    //  - play が対応する種別は Se / Bgm / Vfx / Presentation(それ以外は invalid_params。Shake / Haptics / Anim / UiTween / Cutscene は
    //    それぞれ専用ドライバがエディタ側にあり、対象オブジェクトの配置が前提のため MCP には出していない)
    //  - Play Mode 中は status 以外を拒否する
    public static class DDrivePreviewTools
    {
        public const string BlockedUnsaved = "unsaved scene";

        public static readonly string[] Actions = { "open", "play", "stop", "stop_all", "sweep", "status" };
        public static readonly string[] SceneKinds = { "common", "canvas", "cutscene", "shake" };
        public static readonly AssetType[] PlayTypes = { AssetType.Se, AssetType.Bgm, AssetType.Vfx, AssetType.Presentation };

        // 再生中の登録簿(handle 文字列 → 停止/生存判定)。ドメインリロードで消える(その時は Manager も消える)。
        private sealed class Playing
        {
            public string Handle;
            public AssetType Type;
            public string Id;
            public Action Stop;
            public Func<bool> IsAlive;
        }

        private static readonly Dictionary<string, Playing> Registry = new Dictionary<string, Playing>();
        private static int _nextHandle = 1;

        private static PreviewService _audio;
        private static SceneVfxPreviewDriver _vfx;
        private static ScenePresentationPreviewDriver _presentation;

        [McpTool(
            "ddrive_preview",
            "確認用シーンを開く・実 Manager で再生/停止・残骸掃除・状態(action で選ぶ)",
            Group = "authoring",
            Examples = new[] { "{\"action\":\"play\",\"type\":\"Se\",\"id\":\"1253036813875978544\"}" })]
        public static JObject Preview(
            [McpArg("action", "open / play / stop / stop_all / sweep / status", Required = true)]
            string action,
            [McpArg("scene", "open の確認用シーン: common(既定)/canvas/cutscene/shake")]
            string scene = null,
            [McpArg("type", "種別名。open は配置、play は Se/Bgm/Vfx/Presentation")]
            string type = null,
            [McpArg("id", "ID(10 進文字列か 0x 16 進)")]
            string id = null,
            [McpArg("handle", "stop 用。play が返した handle")]
            string handle = null)
        {
            return McpGuard.Run(() =>
            {
                var act = ParseAction(action);
                if (act != "status")
                {
                    McpGuard.EnsureCanWrite();
                }

                switch (act)
                {
                    case "open":
                        return Open(scene, type, id);
                    case "play":
                        return Play(type, id);
                    case "stop":
                        return Stop(handle);
                    case "stop_all":
                        return StopAll();
                    case "sweep":
                        return Sweep();
                    default:
                        return Status();
                }
            });
        }

        public static string ParseAction(string action)
        {
            var text = (action ?? string.Empty).Trim().ToLowerInvariant();
            if (Array.IndexOf(Actions, text) < 0)
            {
                throw new McpToolError(
                    McpGuard.CodeInvalidParams,
                    $"action '{action}' が不正です({string.Join(" / ", Actions)})");
            }

            return text;
        }

        public static string ParseScene(string scene)
        {
            var text = string.IsNullOrWhiteSpace(scene) ? "common" : scene.Trim().ToLowerInvariant();
            if (Array.IndexOf(SceneKinds, text) < 0)
            {
                throw new McpToolError(
                    McpGuard.CodeInvalidParams,
                    $"scene '{scene}' が不正です({string.Join(" / ", SceneKinds)})");
            }

            return text;
        }

        // ── open ──

        public static string ScenePathOf(string sceneKind)
        {
            switch (sceneKind)
            {
                case "canvas":
                    return CanvasPreviewSceneSetup.ScenePath;
                case "cutscene":
                    return CutscenePreviewSceneSetup.ScenePath;
                case "shake":
                    return CameraShakePreviewSceneSetup.ScenePath;
                default:
                    return VfxPreviewSceneSetup.ScenePath;
            }
        }

        private static bool TryOpenScene(string sceneKind)
        {
            switch (sceneKind)
            {
                case "canvas":
                    return CanvasPreviewSceneSetup.TryOpenOrCreate();
                case "cutscene":
                    return CutscenePreviewSceneSetup.TryOpenOrCreate();
                case "shake":
                    return CameraShakePreviewSceneSetup.TryOpenOrCreate();
                default:
                    return VfxPreviewSceneSetup.TryOpenOrCreate();
            }
        }

        // テスト専用: 未保存シーンの一覧を差し替える(無題シーンが未保存だと追加ロードのシーンを作れないため)。
        public static Func<List<string>> DirtyScenesOverride;

        // 未保存(dirty)の読み込み済みシーンのパス(無題は "(untitled)")。確認用シーンへ切り替えるとこれが失われる。
        public static List<string> DirtyScenes()
        {
            if (DirtyScenesOverride != null)
            {
                return DirtyScenesOverride();
            }

            var result = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.isLoaded && s.isDirty)
                {
                    result.Add(string.IsNullOrEmpty(s.path) ? "(untitled)" : s.path);
                }
            }

            return result;
        }

        // 保存ダイアログを出さずに開けるか。既に目的のシーンが開いていれば true(TryOpenOrCreate も開き直さない)。
        // 開けないなら dirty なシーンの一覧を返す。
        public static bool CanOpenWithoutDialog(string targetPath, out List<string> dirty)
        {
            dirty = new List<string>();
            if (SceneManager.GetActiveScene().path == targetPath)
            {
                return true;
            }

            dirty = DirtyScenes();
            return dirty.Count == 0;
        }

        private static JObject Open(string scene, string type, string id)
        {
            var kind = ParseScene(scene);
            Located? asset = string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(id) ? (Located?)null : LocateAsset(type, id);

            var path = ScenePathOf(kind);
            if (!CanOpenWithoutDialog(path, out var dirty))
            {
                return McpJson.Obj(("blocked", BlockedUnsaved), ("scenes", new JArray(dirty)));
            }

            if (!TryOpenScene(kind))
            {
                throw new McpToolError(McpGuard.CodeException, "確認用シーンを開けませんでした");
            }

            var result = McpJson.Obj(("scene", SceneManager.GetActiveScene().path));
            if (asset == null)
            {
                return result;
            }

            // 配置は各エディタの「確認用シーンで開く」と同じ経路。VFX は SceneVfxPreviewDriver で出す(= VfxEditor の再生)。
            if (asset.Value.Type == AssetType.Vfx)
            {
                var played = PlayAsset(asset.Value.Type, asset.Value.Data);
                result["placed"] = SceneVfxPreviewDriver.PreviewRootName;
                if (played["handle"] != null)
                {
                    result["handle"] = played["handle"];
                }

                return result;
            }

            result["placed"] = false;
            result["hint"] = HintFor(asset.Value.Type);
            return result;
        }

        public static string HintFor(AssetType type)
        {
            switch (type)
            {
                case AssetType.Se:
                case AssetType.Bgm:
                    return "action=play で再生(配置は不要)";
                case AssetType.Presentation:
                    return "action=play で再生。モデルの配置は Presentation Editor で";
                default:
                    return type + " の配置はそれぞれの専用エディタで(ddrive_editor_open)";
            }
        }

        // ── play / stop ──

        private readonly struct Located
        {
            public readonly AssetType Type;
            public readonly AssetDataBase Data;

            public Located(AssetType type, AssetDataBase data)
            {
                Type = type;
                Data = data;
            }
        }

        private static Located LocateAsset(string type, string id)
        {
            var entry = FieldTables.RequireType(type);
            var data = DDriveAssetTools.Locate(entry, id, null);
            return new Located(entry.Type, data);
        }

        private static JObject Play(string type, string id)
        {
            var entry = FieldTables.RequireType(type);
            if (Array.IndexOf(PlayTypes, entry.Type) < 0)
            {
                throw new McpToolError(
                    McpGuard.CodeInvalidParams,
                    $"play は {string.Join(" / ", PlayTypes)} だけです({entry.Type} は専用エディタで)");
            }

            var data = DDriveAssetTools.Locate(entry, id, null);
            return PlayAsset(entry.Type, data);
        }

        // Data を直接再生する(テストは未登録の一時 Data を渡す)。再生できなければ {ok:false, hint}、成功なら {handle, ok:true}。
        public static JObject PlayAsset(AssetType type, AssetDataBase data)
        {
            var idText = McpJson.FormatId(data.Id);
            Playing entry = null;
            switch (type)
            {
                case AssetType.Se when data is SeData se:
                {
                    var service = EnsureAudio();
                    var h = service.PlaySe(se);
                    if (service.AudioManager.IsPlaying(h))
                    {
                        entry = new Playing
                        {
                            Stop = () => service.AudioManager.Stop(h),
                            IsAlive = () => service.AudioManager.IsPlaying(h),
                        };
                    }

                    break;
                }

                case AssetType.Bgm when data is BgmData bgm:
                {
                    var service = EnsureAudio();
                    service.PlayBgm(bgm);
                    if (service.BgmManager.IsPlaying)
                    {
                        entry = new Playing
                        {
                            Stop = () => service.BgmManager.StopBgm(0f),
                            IsAlive = () => service.BgmManager.IsPlaying,
                        };
                    }

                    break;
                }

                case AssetType.Vfx when data is VfxData vfx:
                {
                    var driver = _vfx ??= new SceneVfxPreviewDriver();
                    var h = driver.Play(vfx);
                    if (driver.IsPlaying(h))
                    {
                        var focus = driver.Manager.GetGameObject(h);
                        if (focus != null)
                        {
                            PreviewPlacement.Focus(focus);
                        }

                        entry = new Playing
                        {
                            Stop = () => driver.Stop(h),
                            IsAlive = () => driver.IsPlaying(h),
                        };
                    }

                    break;
                }

                case AssetType.Presentation when data is DDrive.Runtime.Presentation.PresentationData pres:
                {
                    var driver = _presentation ??= new ScenePresentationPreviewDriver();
                    var h = driver.Play(pres);
                    if (driver.Manager != null && driver.Manager.IsPlaying(h))
                    {
                        entry = new Playing
                        {
                            Stop = () => driver.Manager.Cancel(h),
                            IsAlive = () => driver.Manager.IsPlaying(h),
                        };
                    }

                    break;
                }

                default:
                    throw new McpToolError(McpGuard.CodeInvalidParams, $"{type} は再生できません");
            }

            if (entry == null)
            {
                return McpJson.Obj(("ok", McpJson.Keep(false)), ("hint", "再生されませんでした(Clip / Prefab 未設定など)"));
            }

            entry.Handle = (_nextHandle++).ToString(CultureInfo.InvariantCulture);
            entry.Type = type;
            entry.Id = idText;
            Registry[entry.Handle] = entry;
            return McpJson.Obj(("handle", entry.Handle), ("ok", McpJson.Keep(true)));
        }

        private static PreviewService EnsureAudio()
        {
            if (_audio == null)
            {
                _audio = new PreviewService();
            }

            if (!_audio.IsInitialized)
            {
                _audio.Initialize();
            }

            return _audio;
        }

        private static JObject Stop(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle))
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, "stop には handle が必要です(play の返り値)");
            }

            var key = handle.Trim();
            if (!Registry.TryGetValue(key, out var entry))
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, $"handle '{handle}' は再生中ではありません");
            }

            StopEntry(entry);
            Registry.Remove(key);
            return McpJson.Obj(("stopped", 1));
        }

        private static JObject StopAll()
        {
            var count = Registry.Count;
            foreach (var entry in new List<Playing>(Registry.Values))
            {
                StopEntry(entry);
            }

            Registry.Clear();
            return McpJson.Obj(("stopped", McpJson.Keep(count)));
        }

        private static void StopEntry(Playing entry)
        {
            try
            {
                entry.Stop?.Invoke();
            }
            catch (Exception ex)
            {
                // 止められなくても落とさない(CLAUDE.md §0-4)。
                Debug.LogWarning($"[DDrive] プレビューの停止に失敗しました({entry.Type} {entry.Id}): {ex.Message}");
            }
        }

        // ── sweep / status ──

        private static JObject Sweep()
            => McpJson.Obj(("destroyed", McpJson.Keep(EditorPreviewSweeper.DestroyOrphans())));

        private static JObject Status()
        {
            // 鳴り終わった・消えたものは登録簿から外す。
            foreach (var key in new List<string>(Registry.Keys))
            {
                var alive = false;
                try
                {
                    alive = Registry[key].IsAlive != null && Registry[key].IsAlive();
                }
                catch (Exception)
                {
                    // 参照が消えていれば再生中ではない。
                }

                if (!alive)
                {
                    Registry.Remove(key);
                }
            }

            var playing = new JArray();
            foreach (var entry in Registry.Values)
            {
                playing.Add(McpJson.Obj(("handle", entry.Handle), ("type", entry.Type.ToString()), ("id", entry.Id)));
            }

            var active = SceneManager.GetActiveScene();
            return McpJson.Obj(
                ("scene", active.path),
                ("previewScene", McpJson.Keep(IsPreviewScenePath(active.path))),
                ("playing", McpJson.Keep(playing)),
                ("playMode", McpJson.Keep(EditorApplication.isPlayingOrWillChangePlaymode)));
        }

        public static bool IsPreviewScenePath(string path)
        {
            foreach (var kind in SceneKinds)
            {
                if (ScenePathOf(kind) == path)
                {
                    return true;
                }
            }

            return false;
        }

        // テスト専用: 登録簿とドライバを破棄する(プレビューシーン・update フックを残さない)。
        public static void ResetForTests()
        {
            StopAll();
            _audio?.Dispose();
            _audio = null;
            _vfx?.Dispose();
            _vfx = null;
            _presentation?.Dispose();
            _presentation = null;
        }
    }
}
