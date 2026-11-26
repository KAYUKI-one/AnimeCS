using System;
using System.IO;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Profile
{
    public sealed class LocalProfileStore
    {
        private readonly string _path;
        public LocalProfileStore(string path) => _path = Path.GetFullPath(path);

        public WardrobeProfile Load(ContentCatalog catalog)
        {
            foreach (var path in new[] { _path, _path + ".bak" })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var result = JsonUtility.FromJson<WardrobeProfile>(File.ReadAllText(path));
                    if (result == null) continue;
                    if (result.version > 2) throw new NotSupportedException("衣柜档案版本高于当前游戏版本。");
                    result.version = 2;
                    result.Repair(catalog);
                    return result;
                }
                catch (ArgumentException) { /* Try the previous valid write. */ }
            }
            return WardrobeProfile.Create(catalog);
        }

        public void Save(WardrobeProfile profile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonUtility.ToJson(profile, true));
                if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
                else File.Move(temp, _path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
