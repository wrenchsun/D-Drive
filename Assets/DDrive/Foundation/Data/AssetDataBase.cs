using DDrive.Foundation.Event;
using DDrive.Foundation.Manager;
using UnityEngine;

namespace DDrive.Foundation.Data
{
    // 全種別 Data の共通基底。ロード後は読み取り専用として全 Instance から共有される。
    public abstract class AssetDataBase : ScriptableObject
    {
        [Header("Identity")]
        public ulong Id;
        public string DisplayName;
        [TextArea] public string Description;
        public string Category;
        public string[] Tags;
        public Texture2D Icon;

        [Header("Meta")]
        public int Version;
        public string Author;
        public string UpdatedAt;
        [TextArea] public string ChangeNote;

        [Header("Common")]
        public AssetFlags Flags;
        public AssetEvent[] Events;

        public virtual IAssetBehaviour CreateBehaviour() => null;
    }
}
