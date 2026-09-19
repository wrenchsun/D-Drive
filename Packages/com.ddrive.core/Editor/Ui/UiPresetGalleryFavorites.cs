using System;
using System.Collections.Generic;
using UnityEditor;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] B-3.5(4-12) — お気に入りの永続化。EditorPrefs は静的 API で直接テストしづらいため
    // IGalleryPrefsStore で抽象化し、EditMode テストでは fake store に差し替える。
    public interface IGalleryPrefsStore
    {
        string GetString(string key, string defaultValue);
        void SetString(string key, string value);
    }

    public sealed class EditorPrefsGalleryStore : IGalleryPrefsStore
    {
        public string GetString(string key, string defaultValue) => EditorPrefs.GetString(key, defaultValue);
        public void SetString(string key, string value) => EditorPrefs.SetString(key, value);
    }

    public static class UiPresetGalleryFavorites
    {
        private const string PrefsKey = "DDrive.UiPresetGallery.Favorites";

        public static HashSet<string> Load(IGalleryPrefsStore store)
            => Parse(store?.GetString(PrefsKey, string.Empty));

        public static void Save(IGalleryPrefsStore store, IEnumerable<string> favourites)
            => store?.SetString(PrefsKey, Serialize(favourites));

        public static HashSet<string> Parse(string commaList)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(commaList))
            {
                return result;
            }

            foreach (var token in commaList.Split(','))
            {
                var trimmed = token.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        public static string Serialize(IEnumerable<string> favourites)
            => favourites == null ? string.Empty : string.Join(",", favourites);
    }
}
