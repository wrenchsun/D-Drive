using System;
using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Audio;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Audio
{
    // SE / BGM の新エディターが共有する入力部品(docs/1010 UX-3)。いずれも Data へは Undo.RecordObject + SetDirty で直接書き、
    // 書いたあとに changed(final) を呼ぶ(final = false はドラッグ中)。外部変更・Undo は Sync() で取り込む。

    // SE の「元の音とトリム範囲」: 編集する Source の選択 + 波形(端をドラッグ)+ 開始 / 終了の数値 + 無音の自動検出 + トリミングの適用。
    internal sealed class SeTrimInput : VisualElement, IUxSyncable
    {
        private const float SilenceThreshold = 0.01f;

        private readonly SeData _data;
        private readonly Action<bool> _changed;
        private readonly Func<float> _playhead;
        private readonly DropdownField _source;
        private readonly AudioWaveformView _wave;
        private readonly FloatField _start;
        private readonly FloatField _end;
        private readonly Button _detect;
        private readonly Button _apply;
        private readonly Label _note;
        private int _index;
        private string _choicesKey = string.Empty;

        public SeTrimInput(SeData data, Func<float> playhead, Action<bool> changed)
        {
            _data = data;
            _changed = changed;
            _playhead = playhead;
            AddToClassList("ux-trim");

            _source = new DropdownField("編集する元の音") { tooltip = "Sources のうち、波形に出して範囲を決める 1 つ" };
            _source.AddToClassList("ux-trim__source");
            _source.RegisterValueChangedCallback(e =>
            {
                _index = Mathf.Max(0, _source.choices.IndexOf(e.newValue));
                Sync();
            });
            Add(_source);

            _wave = new AudioWaveformView(Spec, SetRange, final => _changed?.Invoke(final), PlayheadOnSource);
            Add(_wave);

            var fields = new VisualElement();
            fields.AddToClassList("ux-trim__fields");
            _start = new FloatField("開始(秒)") { tooltip = "トリム開始位置。0 ならクリップの先頭から" };
            _start.AddToClassList("ux-trim__field");
            _start.RegisterValueChangedCallback(e =>
            {
                if (!Mathf.Approximately(e.newValue, e.previousValue))
                {
                    SetRange(e.newValue, CurrentEnd());
                    Finish();
                }
            });
            fields.Add(_start);
            _end = new FloatField("終了(秒)") { tooltip = "トリム終了位置。開始以下の値(0 など)は「クリップの最後まで」" };
            _end.AddToClassList("ux-trim__field");
            _end.RegisterValueChangedCallback(e =>
            {
                if (!Mathf.Approximately(e.newValue, e.previousValue))
                {
                    SetRange(CurrentStart(), e.newValue);
                    Finish();
                }
            });
            fields.Add(_end);
            Add(fields);

            var buttons = new VisualElement();
            buttons.AddToClassList("ux-toolsline");
            _detect = new Button(DetectSilence) { text = "無音を自動検出", tooltip = "前後の無音を見つけて、開始 / 終了に入れる" };
            _detect.AddToClassList("ux-btn");
            buttons.Add(_detect);
            _apply = new Button(ApplyTrim) { text = "トリミングを適用(Clips を作り直す)", tooltip = "範囲どおりに切り出した音を Clips に作り直して保存する(元の音は変わりません)" };
            _apply.AddToClassList("ux-btn");
            _apply.AddToClassList("ux-btn--primary");
            buttons.Add(_apply);
            Add(buttons);

            _note = new Label();
            _note.AddToClassList("ux-dim");
            _note.AddToClassList("ux-wrap");
            Add(_note);

            Sync();
        }

        private SeClipSource? CurrentSource()
        {
            var s = _data.Sources;
            if (s != null && _index >= 0 && _index < s.Length)
            {
                return s[_index];
            }

            return null;
        }

        private float CurrentStart() => CurrentSource()?.TrimStartSec ?? 0f;

        private float CurrentEnd()
        {
            var cur = CurrentSource();
            if (cur == null)
            {
                return 0f;
            }

            var e = cur.Value;
            return e.TrimEndSec > e.TrimStartSec ? e.TrimEndSec : (e.Source != null ? e.Source.length : 0f);
        }

        private AudioWaveSpec Spec()
        {
            var spec = new AudioWaveSpec { Marker = -1f, RangeLabel = "トリム範囲" };
            var cur = CurrentSource();
            if (cur != null && cur.Value.Source != null)
            {
                var e = cur.Value;
                spec.Clip = e.Source;
                spec.Length = e.Source.length;
                spec.Start = e.TrimStartSec;
                spec.End = e.TrimEndSec > e.TrimStartSec ? e.TrimEndSec : spec.Length;
                spec.Editable = true;
                spec.Marker = _data.StartOffsetSec > 0f ? spec.Start + _data.StartOffsetSec : -1f;
            }
            else if (_data.Clips != null && _data.Clips.Length > 0 && _data.Clips[0] != null)
            {
                spec.Clip = _data.Clips[0];
                spec.Length = spec.Clip.length;
                spec.End = spec.Length;
                spec.Editable = false;
            }

            return spec;
        }

        // 再生位置は「いま鳴っている Clip の経過」。元の音の波形に重ねるには、トリム済みなら開始位置を足す(Clips が 1 つのときだけ)。
        private float PlayheadOnSource()
        {
            if (_playhead == null || _data.Clips == null || _data.Clips.Length > 1)
            {
                return -1f;
            }

            var t = _playhead();
            if (t < 0f)
            {
                return -1f;
            }

            var spec = Spec();
            var baked = spec.Editable && _data.Clips.Length == 1 && _data.Clips[0] != null && _data.Clips[0] != spec.Clip;
            return t + (baked ? spec.Start : 0f);
        }

        private void SetRange(float start, float end)
        {
            var s = _data.Sources;
            if (s == null || _index < 0 || _index >= s.Length)
            {
                return;
            }

            Undo.RecordObject(_data, "トリム範囲を変更");
            var entry = s[_index];
            entry.TrimStartSec = Mathf.Max(0f, start);
            entry.TrimEndSec = end;
            s[_index] = entry;
            EditorUtility.SetDirty(_data);
        }

        private void Finish()
        {
            Sync();
            _changed?.Invoke(true);
        }

        private void DetectSilence()
        {
            var cur = CurrentSource();
            if (cur == null || cur.Value.Source == null)
            {
                return;
            }

            if (AudioClipTrimUtility.TryDetectSilenceTrim(cur.Value.Source, SilenceThreshold, out var start, out var end))
            {
                SetRange(start, end);
                Finish();
            }
            else
            {
                Debug.LogWarning($"[D-Drive] '{cur.Value.Source.name}' の波形を読めないため、無音を検出できませんでした。");
            }
        }

        private void ApplyTrim()
        {
            if (SeTrimApplier.Apply(_data))
            {
                Finish();
            }
        }

        public void Sync()
        {
            var sources = _data.Sources;
            var count = sources?.Length ?? 0;
            _index = count == 0 ? 0 : Mathf.Clamp(_index, 0, count - 1);

            var choices = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var src = sources[i].Source;
                choices.Add($"[{i}] {(src != null ? src.name : "(未設定)")}");
            }

            var hasSources = count > 0;
            if (!hasSources)
            {
                choices.Add("(Sources が空です)");
            }

            var key = string.Join("|", choices);
            if (key != _choicesKey)
            {
                _choicesKey = key;
                _source.choices = choices;
            }

            _source.SetValueWithoutNotify(choices[Mathf.Clamp(_index, 0, choices.Count - 1)]);
            _source.SetEnabled(hasSources);

            var cur = CurrentSource();
            var canEdit = cur != null && cur.Value.Source != null;
            _start.SetEnabled(canEdit);
            _end.SetEnabled(canEdit);
            _detect.SetEnabled(canEdit);
            _apply.SetEnabled(hasSources);
            if (canEdit)
            {
                _start.SetValueWithoutNotify(CurrentStart());
                _end.SetValueWithoutNotify(CurrentEnd());
            }

            string note = null;
            if (!hasSources)
            {
                note = "元の音(Sources)が空です。下の「元の音とトリム範囲(一覧)」に元のクリップを入れると、ここでトリムできます。";
            }
            else if (!canEdit)
            {
                note = "選んだ Sources に AudioClip が入っていません。";
            }
            else if (_data.Clips == null || _data.Clips.Length != count)
            {
                note = "Clips の数が Sources と合っていません。「トリミングを適用」で作り直してください。";
            }

            _note.text = note ?? string.Empty;
            _note.Show(note != null);
            _wave.Sync();
        }
    }

    // BGM の「ループの範囲」: LoopBody の波形 + ループの開始 / 終了(端をドラッグ)+ 数値 + 全体に戻す。
    internal sealed class BgmLoopInput : VisualElement, IUxSyncable
    {
        private readonly BgmData _data;
        private readonly Action<bool> _changed;
        private readonly AudioWaveformView _wave;
        private readonly DoubleField _start;
        private readonly DoubleField _end;
        private readonly Label _note;

        public BgmLoopInput(BgmData data, Action<bool> changed)
        {
            _data = data;
            _changed = changed;
            AddToClassList("ux-trim");

            _wave = new AudioWaveformView(Spec, SetRange, final => _changed?.Invoke(final), null);
            Add(_wave);

            var fields = new VisualElement();
            fields.AddToClassList("ux-trim__fields");
            _start = new DoubleField("ループ開始(秒)") { tooltip = "ループの開始位置。本体のここから繰り返す" };
            _start.AddToClassList("ux-trim__field");
            _start.RegisterValueChangedCallback(e =>
            {
                if (e.newValue != e.previousValue)
                {
                    SetRange((float)e.newValue, (float)_data.LoopEndSec, true);
                    Finish();
                }
            });
            fields.Add(_start);
            _end = new DoubleField("ループ終了(秒)") { tooltip = "ループの終了位置。0 または本体の長さ以上なら「最後まで」" };
            _end.AddToClassList("ux-trim__field");
            _end.RegisterValueChangedCallback(e =>
            {
                if (e.newValue != e.previousValue)
                {
                    SetRange((float)_data.LoopStartSec, (float)e.newValue, true);
                    Finish();
                }
            });
            fields.Add(_end);
            Add(fields);

            var buttons = new VisualElement();
            buttons.AddToClassList("ux-toolsline");
            var whole = new Button(() =>
            {
                Undo.RecordObject(_data, "ループ範囲を全体に戻す");
                _data.LoopStartSec = 0.0;
                _data.LoopEndSec = 0.0;
                EditorUtility.SetDirty(_data);
                Finish();
            }) { text = "全体をループ", tooltip = "開始 0・終了 0(= 最後まで)に戻す" };
            whole.AddToClassList("ux-btn");
            buttons.Add(whole);
            Add(buttons);

            _note = new Label();
            _note.AddToClassList("ux-dim");
            _note.AddToClassList("ux-wrap");
            Add(_note);

            Sync();
        }

        private AudioWaveSpec Spec()
        {
            var spec = new AudioWaveSpec { Marker = -1f, RangeLabel = "ループ範囲" };
            var clip = _data.LoopBody;
            if (clip != null)
            {
                spec.Clip = clip;
                spec.Length = clip.length;
                spec.Start = (float)_data.LoopStartSec;
                spec.End = _data.LoopEndSec > _data.LoopStartSec ? (float)_data.LoopEndSec : spec.Length;
                spec.Editable = true;
            }

            return spec;
        }

        private void SetRange(float start, float end) => SetRange(start, end, false);

        private void SetRange(float start, float end, bool keepZeroEnd)
        {
            Undo.RecordObject(_data, "ループ範囲を変更");
            _data.LoopStartSec = Mathf.Max(0f, start);
            _data.LoopEndSec = keepZeroEnd && end <= 0f ? 0.0 : end;
            EditorUtility.SetDirty(_data);
        }

        private void Finish()
        {
            Sync();
            _changed?.Invoke(true);
        }

        public void Sync()
        {
            _start.SetValueWithoutNotify(_data.LoopStartSec);
            _end.SetValueWithoutNotify(_data.LoopEndSec);
            _start.SetEnabled(true);
            _end.SetEnabled(true);
            var note = _data.LoopBody == null ? "ループする本体(LoopBody)が空です。「鳴らす」カードで AudioClip を入れると波形が出ます。" : null;
            _note.text = note ?? string.Empty;
            _note.Show(note != null);
            _wave.Sync();
        }
    }

    // 再生を始める位置(SeData.StartOffsetSec): スライダー(0 〜 先頭クリップの長さ)+ 数値。次の再生から反映される。
    internal sealed class SeStartOffsetInput : VisualElement, IUxSyncable
    {
        private readonly SeData _data;
        private readonly Action<bool> _changed;
        private readonly Slider _slider;

        public SeStartOffsetInput(SeData data, Action<bool> changed)
        {
            _data = data;
            _changed = changed;
            AddToClassList("ux-offset");
            _slider = new Slider(0f, 1f) { showInputField = true, tooltip = "再生開始位置(秒)。次の再生から反映" };
            _slider.AddToClassList("ux-offset__slider");
            _slider.RegisterValueChangedCallback(e =>
            {
                if (Mathf.Approximately(e.newValue, _data.StartOffsetSec))
                {
                    return;
                }

                Undo.RecordObject(_data, "再生開始位置を変更");
                _data.StartOffsetSec = e.newValue;
                EditorUtility.SetDirty(_data);
                _changed?.Invoke(true);
            });
            Add(_slider);
            Sync();
        }

        public void Sync()
        {
            var length = _data.Clips != null && _data.Clips.Length > 0 && _data.Clips[0] != null ? _data.Clips[0].length : 1f;
            _slider.highValue = Mathf.Max(0.01f, length);
            _slider.SetValueWithoutNotify(_data.StartOffsetSec);
        }
    }

    // Loop の欄 + 「試聴だけループ」(アセットは変えない)。
    internal sealed class SeLoopRow : UxRow
    {
        public SeLoopRow(SerializedObject so, SerializedObject defaultSo, FieldGuideEntry entry, Func<bool> getPreview, Action<bool> setPreview, Action changed)
            : base(so, defaultSo, new[] { entry }, entry.Label, entry.Hint, entry.Tier, true, Build(so, getPreview, setPreview), changed)
        {
        }

        private static VisualElement Build(SerializedObject so, Func<bool> getPreview, Action<bool> setPreview)
        {
            var box = new VisualElement();
            box.Add(UxRow.MakeInput(so, "Loop"));
            var t = new Toggle("試聴だけループさせる") { value = getPreview(), tooltip = "▶ のときだけループで鳴らします。この Data の Loop は変わりません" };
            t.AddToClassList("ux-trim__toggle");
            t.RegisterValueChangedCallback(e => setPreview(e.newValue));
            var wrap = new VisualElement(); // 行の入力の直下に置くとラベルが隠れるため、1 段包む
            wrap.Add(t);
            box.Add(wrap);
            return box;
        }
    }

    // Anchor アセット(AnchorId)の欄: 参照欄 + 「Anchor Editor で開く」+「埋め込みをアセット化」(旧 SeDataEditor の導線)。
    internal sealed class SeAnchorIdRow : UxRow
    {
        private readonly Button _open;
        private readonly Button _toAsset;
        private readonly Label _label;
        private readonly Func<bool> _usesAsset;
        private readonly Func<string> _assetText;

        public SeAnchorIdRow(
            SerializedObject so,
            SerializedObject defaultSo,
            FieldGuideEntry entry,
            Func<bool> usesAsset,
            Func<string> assetText,
            Action openEditor,
            Action toAsset,
            Action changed)
            : base(so, defaultSo, new[] { entry }, entry.Label, entry.Hint, entry.Tier, true, Build(so, openEditor, toAsset, out var open, out var asset, out var label), changed)
        {
            _open = open;
            _toAsset = asset;
            _label = label;
            _usesAsset = usesAsset;
            _assetText = assetText;
            Sync();
        }

        private static VisualElement Build(SerializedObject so, Action openEditor, Action toAsset, out Button open, out Button asset, out Label label)
        {
            var box = new VisualElement();
            box.Add(UxRow.MakeInput(so, "AnchorId"));
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            open = new Button(openEditor) { text = "Anchor Editor で開く", tooltip = "この Anchor アセットを Anchor Editor で開く(位置・ランダム・ディレイの調整)" };
            open.AddToClassList("ux-btn");
            line.Add(open);
            asset = new Button(toAsset) { text = "埋め込みをアセット化", tooltip = "現在の埋め込み Anchor から Anchor アセットを作り、この SE の AnchorId に設定する(埋め込み値は残る)" };
            asset.AddToClassList("ux-btn");
            line.Add(asset);
            box.Add(line);
            label = new Label();
            label.AddToClassList("ux-dim");
            label.AddToClassList("ux-wrap");
            box.Add(label);
            return box;
        }

        public override void Sync()
        {
            var uses = _usesAsset();
            _open.SetEnabled(uses);
            _toAsset.SetEnabled(!uses);
            var text = _assetText();
            if (_label.text != text)
            {
                _label.text = text;
                _label.Show(!string.IsNullOrEmpty(text));
            }
        }
    }
}
