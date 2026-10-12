using System.Collections.Generic;
using System.IO;
using System.Text;
using DDrive.Editor.Anim;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DDrive.Editor.Model
{
    // 確認する・情報の欄(docs/1010 UX-9): モデルの情報 / Avatar の情報(読み取り専用)・並べて確認(最大 4 体)・シーンで確認(配置・撤去・ターンテーブル)・
    // 固定バーの ▶(配置して DefaultAnimation を流す)。配置・再生は旧 ModelEditorWindow と同じ(実 ModelsManager / AnimManager。SceneAnimPreviewDriver)。
    internal sealed partial class ModelEditor
    {
        private readonly Button[] _parallelButtons = new Button[MaxParallel];
        private readonly Handle<ModelMarker>[] _parallelHandles = new Handle<ModelMarker>[MaxParallel];
        private Label _infoLabel;
        private Label _avatarLabel;
        private Button _avatarFixButton;
        private Label _sceneStatus;
        private string _note = string.Empty;
        private int _infoSig = int.MinValue;

        // ── モデルの情報・Avatar の情報(読み取り専用) ──

        private UxRow CreateInfoRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _infoLabel = new Label();
            _infoLabel.AddToClassList("ux-dim");
            _infoLabel.AddToClassList("ux-note");
            _infoLabel.AddToClassList("ux-mdl-info");
            box.Add(_infoLabel);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, RefreshInfoLabels);
        }

        private UxRow CreateAvatarInfoRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            _avatarLabel = new Label();
            _avatarLabel.AddToClassList("ux-dim");
            _avatarLabel.AddToClassList("ux-note");
            _avatarLabel.AddToClassList("ux-mdl-info");
            box.Add(_avatarLabel);
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            _avatarFixButton = MakeButton("Animator の Avatar を入れる", null, CopyAnimatorAvatar,
                "Prefab の Animator が持つ Humanoid の Avatar を、この Data の Avatar に入れる(Cutscene の検査はこの Data の Avatar を見ます)");
            line.Add(_avatarFixButton);
            box.Add(line);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, RefreshInfoLabels);
        }

        // Prefab と Avatar が変わったときだけ文を作り直す(署名の比較だけなら毎 150ms でも軽い)。
        private void RefreshInfoLabels()
        {
            var t = Target;
            var sig = 17;
            sig = sig * 31 + (t != null && t.Prefab != null ? t.Prefab.GetInstanceID() : 0);
            sig = sig * 31 + (t != null && t.Avatar != null ? t.Avatar.GetInstanceID() : 0);
            if (sig == _infoSig)
            {
                return;
            }

            _infoSig = sig;
            SetLabel(_infoLabel, InfoText());
            SetLabel(_avatarLabel, AvatarText());
            _avatarFixButton?.Show(CanCopyAnimatorAvatar());
        }

        private string InfoText()
        {
            var t = Target;
            if (t == null || t.Prefab == null)
            {
                return "Prefab が未設定です。";
            }

            var prefab = t.Prefab;
            var skinned = 0;
            var mesh = 0;
            var other = 0;
            var slotTotal = 0;
            var shapes = 0;
            var shapeRenderers = 0;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                slotTotal += r.sharedMaterials.Length;
                if (r is SkinnedMeshRenderer smr)
                {
                    skinned++;
                    if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                    {
                        shapes += smr.sharedMesh.blendShapeCount;
                        shapeRenderers++;
                    }
                }
                else if (r is MeshRenderer)
                {
                    mesh++;
                }
                else
                {
                    other++;
                }
            }

            var sb = new StringBuilder();
            sb.Append("Renderer ").Append(skinned + mesh + other).Append("(Skinned ").Append(skinned).Append(" / Mesh ").Append(mesh);
            if (other > 0)
            {
                sb.Append(" / ほか ").Append(other);
            }

            sb.Append(")・Material スロット ").Append(slotTotal).Append("(この Data の Slots は ").Append(t.Slots != null ? t.Slots.Length : 0).Append(")\n");
            sb.Append(shapes > 0 ? $"BlendShape {shapes} 本({shapeRenderers} Renderer)。返却時に既定の重みへ戻ります" : "BlendShape なし").Append('\n');

            var animator = prefab.GetComponentInChildren<Animator>(true);
            sb.Append(animator == null
                ? "Animator なし(静的モデル)"
                : "Animator あり(Controller: " + (animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "なし") + ")").Append('\n');

            var listeners = prefab.GetComponentsInChildren<IModelInstanceListener>(true);
            if (listeners.Length == 0)
            {
                sb.Append("IModelInstanceListener なし");
            }
            else
            {
                var names = new List<string>();
                foreach (var l in listeners)
                {
                    var n = l.GetType().Name;
                    if (!names.Contains(n))
                    {
                        names.Add(n);
                    }
                }

                sb.Append("IModelInstanceListener ").Append(listeners.Length).Append(" 個(").Append(string.Join(", ", names)).Append("): 出現 / 返却の節目で通知されます");
            }

            return sb.ToString();
        }

        private static string DescribeAvatar(Avatar avatar)
        {
            if (avatar == null)
            {
                return "なし";
            }

            return !avatar.isValid ? avatar.name + "(無効)" : avatar.isHuman ? avatar.name + "(Humanoid)" : avatar.name + "(Generic)";
        }

        private string AvatarText()
        {
            var t = Target;
            if (t == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append("この Data の Avatar: ").Append(DescribeAvatar(t.Avatar)).Append('\n');
            var animator = t.Prefab != null ? t.Prefab.GetComponentInChildren<Animator>(true) : null;
            sb.Append("Prefab の Animator: ").Append(animator == null ? "なし(静的モデル。Avatar は不要)" : "あり / その Avatar: " + DescribeAvatar(animator.avatar)).Append('\n');
            var usable = t.Avatar != null && t.Avatar.isValid && t.Avatar.isHuman;
            sb.Append("Cutscene の Humanoid アニメ: ").Append(usable ? "使えます" : "使えません(Humanoid の Avatar をこの Data に入れてください。Cutscene の検査が警告します)");
            return sb.ToString();
        }

        private bool CanCopyAnimatorAvatar()
        {
            var t = Target;
            if (t == null || t.Prefab == null || (t.Avatar != null && t.Avatar.isValid && t.Avatar.isHuman))
            {
                return false;
            }

            var animator = t.Prefab.GetComponentInChildren<Animator>(true);
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        private void CopyAnimatorAvatar()
        {
            if (!CanCopyAnimatorAvatar())
            {
                return;
            }

            var animator = Target.Prefab.GetComponentInChildren<Animator>(true);
            So.Update();
            So.FindProperty("Avatar").objectReferenceValue = animator.avatar;
            So.ApplyModifiedProperties();
            EditorUtility.SetDirty(Target);
            _infoSig = int.MinValue;
            RowChanged();
        }

        // ── 並べて確認(最大 4 体。対象の隣に 2m 間隔) ──

        private UxRow CreateParallelRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            for (var i = 0; i < MaxParallel; i++)
            {
                var slot = i;
                var line = new VisualElement();
                line.AddToClassList("ux-mdl-pline");
                var field = new ObjectField { objectType = typeof(ModelData), allowSceneObjects = false };
                field.SetValueWithoutNotify(_parallelModels != null && slot < _parallelModels.Length ? _parallelModels[slot] : null);
                field.AddToClassList("ux-mdl-slotfield");
                field.tooltip = $"{slot + 1} 体目(対象から {(slot + 1) * ParallelSpacingMeters:0}m 隣)に並べる ModelData";
                field.RegisterValueChangedCallback(evt =>
                {
                    EnsureParallelArray();
                    _parallelModels[slot] = evt.newValue as ModelData;
                });
                line.Add(field);
                var button = new Button(() => ToggleParallel(slot, field)) { text = "配置" };
                button.AddToClassList("ux-btn");
                button.AddToClassList("ux-mdl-pbtn");
                _parallelButtons[slot] = button;
                line.Add(button);
                box.Add(line);
            }

            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        private void EnsureParallelArray()
        {
            if (_parallelModels == null || _parallelModels.Length != MaxParallel)
            {
                System.Array.Resize(ref _parallelModels, MaxParallel);
            }
        }

        private bool IsParallelPlaced(int slot)
            => _scene != null && _scene.Models != null && _scene.Models.IsValid(_parallelHandles[slot]);

        private void ToggleParallel(int slot, ObjectField field)
        {
            if (_scene == null)
            {
                return;
            }

            if (IsParallelPlaced(slot))
            {
                _scene.DespawnExtraModel(_parallelHandles[slot]);
                _parallelHandles[slot] = Handle<ModelMarker>.Invalid;
                RefreshSceneUi();
                return;
            }

            var data = field.value as ModelData;
            if (data == null || data.Prefab == null)
            {
                SetNote("並べる ModelData(Prefab 付き)を選んでください");
                return;
            }

            _parallelHandles[slot] = _scene.SpawnExtraModel(data, new Vector3((slot + 1) * ParallelSpacingMeters, 0f, 0f), Quaternion.identity);
            RefreshSceneUi();
        }

        // ── シーンで確認 ──

        private UxRow CreateSceneRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var scene = PreviewPlacementButton.Create(
                "確認用シーン",
                "確認用シーンを開き(無ければ生成)、対象を原点に配置する。右クリックで、いま開いているシーンへ配置",
                OpenPreviewScene);
            scene.AddToClassList("ux-btn");
            line.Add(scene);
            line.Add(MakeButton("今のシーンに配置", null, () => PlaceMain(true), "開いているシーン / プレハブモードの原点に対象を配置する(保存されない)"));
            line.Add(MakeButton("撤去", null, RemoveMain, "配置したモデル(並べて確認の分も)をシーンから消す"));
            line.Add(MakeButton("Prefab で開く", null, OpenPrefab, "ModelData.Prefab をプレハブモードで開く(Renderer / Material をその場で編集)"));
            box.Add(line);

            var turn = new Toggle { text = "自動で回す", value = _turntableEnabled, tooltip = "シーンに配置したモデルを Y 軸で回す(プレハブモードの実体は回さない)" };
            turn.AddToClassList("ux-mat-toggle");
            turn.RegisterValueChangedCallback(evt => _turntableEnabled = evt.newValue);
            box.Add(Line("ターンテーブル", turn));
            var speed = new Slider(-180f, 180f) { value = _turntableSpeed, showInputField = true, tooltip = "回る速さ(度 / 秒)" };
            speed.RegisterValueChangedCallback(evt => _turntableSpeed = evt.newValue);
            box.Add(Line("回る速さ(度/秒)", speed));

            _sceneStatus = new Label(StatusText());
            _sceneStatus.AddToClassList("ux-dim");
            _sceneStatus.AddToClassList("ux-note");
            box.Add(_sceneStatus);
            return new AnimAuxRow(e, e.Label, e.Hint, e.Tier, box, null);
        }

        // ラベル + 部品の 1 行。
        private static VisualElement Line(string label, VisualElement control)
        {
            var line = new VisualElement();
            line.AddToClassList("ux-mat-ctrl");
            var l = new Label(label);
            l.AddToClassList("ux-mat-ctrl__label");
            line.Add(l);
            control.AddToClassList("ux-mat-ctrl__input");
            line.Add(control);
            return line;
        }

        private void SetNote(string text)
        {
            _note = text;
            SetLabel(_sceneStatus, StatusText());
        }

        private string StatusText()
        {
            if (!string.IsNullOrEmpty(_note))
            {
                return _note;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var where = stage != null
                ? $"プレハブモード '{Path.GetFileNameWithoutExtension(stage.assetPath)}'"
                : $"シーン '{SceneManager.GetActiveScene().name}'";
            if (_scene != null && _scene.OwnsCurrent && _scene.CurrentRoot != null)
            {
                return $"● {where} に配置中: {_scene.CurrentRoot.name}(保存されません)";
            }

            if (stage != null && Target != null && Target.Prefab != null && stage.assetPath == AssetDatabase.GetAssetPath(Target.Prefab))
            {
                return $"● {where} を編集中(対象の Prefab そのもの)。Ctrl+S で保存";
            }

            return Target == null ? "対象の Data を選んでください" : $"未配置。「確認用シーン」か「今のシーンに配置」で {where} に置きます";
        }

        // 150ms ごと: 状態の文・並べて確認のボタンの文言・「配置中に反映」の可否。
        private void RefreshSceneUi()
        {
            SetLabel(_sceneStatus, StatusText());
            for (var i = 0; i < MaxParallel; i++)
            {
                var b = _parallelButtons[i];
                if (b != null)
                {
                    var text = IsParallelPlaced(i) ? "撤去" : "配置";
                    if (b.text != text)
                    {
                        b.text = text;
                    }
                }
            }

            _slotPlacedButton?.SetEnabled(_scene != null && _scene.OwnsCurrent);
        }

        // 左クリック = 確認用シーンを開いて配置 / 右クリック = このシーンに配置・本配置。
        private void OpenPreviewScene(PreviewPlaceMode mode)
        {
            StopMain();
            RemoveMain();
            if (!PreviewPlacement.PrepareScene(mode, VfxPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            if (PreviewPlacement.IsPersistent(mode))
            {
                if (Target == null || Target.Prefab == null)
                {
                    SetNote("本配置するには Prefab が必要です");
                    return;
                }

                // 本配置は ModelsManager が追跡しない実体(Prefab リンク付き)にする。
                PreviewPlacement.PlacePrefabPersistent(Target.Prefab, Vector3.zero, Quaternion.identity);
                SetNote("このシーンに本配置しました(シーンを保存すると残ります)");
                return;
            }

            PlaceMain(true);
        }

        private void OpenPrefab()
        {
            if (Target != null && Target.Prefab != null)
            {
                StopMain();
                RemoveMain();
                AssetDatabase.OpenAsset(Target.Prefab);
            }
            else
            {
                SetNote("Prefab が未設定です");
            }
        }

        // 対象を原点に配置する(Spawn と同時に DefaultAnimation が自動再生される。ランタイムの Models.Spawn と同じ経路)。
        private void PlaceMain(bool focus)
        {
            if (Target == null || _scene == null)
            {
                return;
            }

            RemoveMain();
            if (Target.Prefab == null)
            {
                SetNote("Prefab が未設定です");
                return;
            }

            _note = string.Empty;
            _scene.SpawnModel(Target, Vector3.zero, Quaternion.identity, requireAnimator: false);
            SetLabel(_sceneStatus, StatusText());
            if (focus && _scene.CurrentRoot != null)
            {
                Selection.activeGameObject = _scene.CurrentRoot;
                PreviewPlacement.Focus(_scene.CurrentRoot); // 配置場所が遠いと見えないので SceneView を寄せる
            }
        }

        // 配置したモデル(対象と並べて確認の分)をすべて撤去する。
        private void RemoveMain()
        {
            if (_scene != null)
            {
                _scene.ReleaseTarget();
            }

            for (var i = 0; i < MaxParallel; i++)
            {
                _parallelHandles[i] = Handle<ModelMarker>.Invalid;
            }

            _note = string.Empty;
            SetLabel(_sceneStatus, StatusText());
        }

        // ▶: 配置して DefaultAnimation を流す。配置済みなら置き直さず最初から流し直す。
        private void PlayDefaultAnimation(bool focus)
        {
            if (Target == null || _scene == null)
            {
                return;
            }

            if (!Target.DefaultAnimation.IsValid)
            {
                SetNote("Default Animation が未設定です。「最初に流すアニメ」を選んでください");
                ResetPlayback(); // 未設定のままリピートで何度も試さない
                return;
            }

            if (!_scene.OwnsCurrent || _scene.Current == null)
            {
                PlaceMain(focus); // 配置と同時に DefaultAnimation が再生される
            }
            else
            {
                _scene.Stop();
            }

            if (_scene.OwnsCurrent && _scene.Current != null && _scene.Manager.ActiveCount == 0)
            {
                _scene.Manager.Play(Target.DefaultAnimation, _scene.Current);
            }
        }

        // ── シーン・プレハブモードの変化 ──

        private void OnActiveSceneChanged(Scene previous, Scene current) => OnStageChanged();

        private void OnPrefabStageChanged(PrefabStage stage) => OnStageChanged();

        // ドライバはシーン切替で配置物を自動撤去済み。UI の状態だけ合わせる。
        private void OnStageChanged()
        {
            for (var i = 0; i < MaxParallel; i++)
            {
                _parallelHandles[i] = Handle<ModelMarker>.Invalid;
            }

            _note = string.Empty;
            ResetPlayback();
            RefreshSceneUi();
        }

        // フレームごと: ターンテーブル(配置したモデルを回す)。
        private void OnEditorUpdate()
        {
            var root = _scene != null && _scene.OwnsCurrent ? _scene.CurrentRoot : null;
            if (!_turntableEnabled || root == null)
            {
                _lastTurntableTime = EditorApplication.timeSinceStartup;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTurntableTime), 0f, 0.25f);
            _lastTurntableTime = now;
            root.transform.Rotate(Vector3.up, _turntableSpeed * dt, Space.World);
            SceneView.RepaintAll();
        }
    }
}
