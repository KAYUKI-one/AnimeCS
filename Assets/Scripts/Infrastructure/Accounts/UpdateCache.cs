using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AnimeCS.Networking;
using UnityEngine;

namespace AnimeCS.Accounts
{
    internal static class UpdateCache
    {
        [Serializable] private sealed class Manifest {public int schema;public string version;public Entry[] files;}
        [Serializable] private sealed class Entry {public string path,sha256;public long size;}
        private const int ManifestLimit = 4 * 1024 * 1024;

        internal static string SourceDirectory(string root,string endpoint)
        {
            using var sha = SHA256.Create();
            return Path.Combine(root,"verified",Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(endpoint))));
        }

        internal static string Active(string root, string pin = null)
        {
            string pointer=Path.Combine(root,"active");
            if(!File.Exists(pointer)) return null;
            string key=File.ReadAllText(pointer);
            return IsHash(key) && (pin == null || key == pin) && Directory.Exists(Path.Combine(root,key)) ? Path.Combine(root,key) : null;
        }

        private static bool IsHash(string hash) => hash!=null && hash.Length==64 && hash.All(c=>
            c>='0' && c<='9' || c>='a' && c<='f');
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        internal static string NormalizePin(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string digest = value.Trim().ToLowerInvariant();
            if (!IsHash(digest)) throw new InvalidDataException("本地配置的更新清单 SHA-256 无效");
            return digest;
        }

        internal static string LocalDigest(string path)
        {
            try
            {
                using var manifest = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (manifest.Length > ManifestLimit) throw new InvalidDataException("本机发布清单过大");
                using var sha = SHA256.Create();
                return Hex(sha.ComputeHash(manifest));
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }
        private static bool Valid(Entry item)
        {
            if(item==null || !IsHash(item.sha256) || item.size<0 || string.IsNullOrEmpty(item.path) ||
                item.path.Length>240 || item.path.IndexOfAny(new[]{'\\',':','<','>','"','|','?','*'})>=0) return false;
            foreach(string part in item.path.Split('/'))
            {
                string name=part.Split('.')[0].ToUpperInvariant();
                if(part.Length==0 || part=="." || part==".." || part.EndsWith(".") || part.EndsWith(" ") ||
                    part.Any(char.IsControl) || name=="AUX" || name=="PRN" || name=="NUL" || name=="CON" ||
                    (name.Length==4 && (name.StartsWith("COM") || name.StartsWith("LPT")) && char.IsDigit(name[3]))) return false;
            }
            return true;
        }

        internal static async Task<string> Check(HttpClient http,string url,string root,CancellationToken cancel,
            string publisherDigest = null,bool allowPrivateHttp = false)
        {
            url = ServiceEndpoint.Resolve(url,27842,allowPrivateHttp);
            publisherDigest = NormalizePin(publisherDigest);
            using var response=await http.GetAsync(url+"/v1/manifest",HttpCompletionOption.ResponseHeadersRead,cancel);
            if(response.StatusCode==HttpStatusCode.NoContent)
                return Active(root,publisherDigest) == null ? "已就绪，使用内置资源" : "继续使用已验证的缓存资源";
            response.EnsureSuccessStatusCode();
            if (publisherDigest == null && new Uri(url).Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("HTTP 更新需要发布者另行提供的清单 SHA-256");
            byte[] manifest=await ReadManifest(response.Content,cancel);
            using var sha=SHA256.Create();
            string digest=Hex(sha.ComputeHash(manifest));
            if (publisherDigest != null && digest != publisherDigest)
                throw new InvalidDataException("更新清单与受信发布指纹不匹配");
            var data=JsonUtility.FromJson<Manifest>(Encoding.UTF8.GetString(manifest));
            if(data?.schema!=1 || data.files==null || data.files.Length>10000 || string.IsNullOrEmpty(data.version) ||
                data.files.Any(x=>!Valid(x)) || data.files.Select(x=>x.path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=data.files.Length)
                throw new InvalidDataException("更新清单无效");
            string versionRoot=Path.Combine(root,digest);
            Directory.CreateDirectory(versionRoot);
            foreach(var item in data.files)
            {
                string target=Path.Combine(versionRoot,item.path);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                await EnsureBlob(http,url,item,target,cancel);
            }
            string pointer=Path.Combine(root,"active");
            string temporary=pointer+"."+Guid.NewGuid().ToString("N");
            try
            {
                if(!File.Exists(pointer) || File.ReadAllText(pointer)!=digest)
                {
                    File.WriteAllText(temporary,digest);
                    Install(temporary,pointer);
                }
            }
            finally {if(File.Exists(temporary)) File.Delete(temporary);}
            return "资源版本 "+data.version+" 已就绪";
        }

        private static async Task<byte[]> ReadManifest(HttpContent content,CancellationToken cancel)
        {
            if(content.Headers.ContentLength>ManifestLimit) throw new InvalidDataException("更新清单过大");
            using var stream=await content.ReadAsStreamAsync();
            using var bytes=new MemoryStream();
            byte[] buffer=new byte[16384];
            int count;
            while((count=await stream.ReadAsync(buffer,0,buffer.Length,cancel).ConfigureAwait(false))>0)
            {
                if(bytes.Length+count>ManifestLimit) throw new InvalidDataException("更新清单过大");
                bytes.Write(buffer,0,count);
            }
            return bytes.ToArray();
        }

        private static void Install(string source,string destination)
        {
            if(!File.Exists(destination))
            {
                try {File.Move(source,destination);return;}
                catch(IOException) when(File.Exists(destination)) {}
            }
            File.Replace(source,destination,null);
        }

        private static async Task EnsureBlob(HttpClient http,string url,Entry item,string target,CancellationToken cancel)
        {
            using var sha=SHA256.Create();
            if(File.Exists(target) && new FileInfo(target).Length==item.size)
            {
                bool valid=await Task.Run(()=>{using var f=File.OpenRead(target);return Hex(sha.ComputeHash(f))==item.sha256;},cancel);
                if(valid) return;
            }
            using var response=await http.GetAsync(url+"/v1/blobs/"+item.sha256,HttpCompletionOption.ResponseHeadersRead,cancel);
            response.EnsureSuccessStatusCode();
            using var stream=await response.Content.ReadAsStreamAsync();
            string temporary=target+"."+Guid.NewGuid().ToString("N")+".part";
            try
            {
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                {
                    byte[] buffer=new byte[65536];long total=0;
                    int count;
                    while((count=await stream.ReadAsync(buffer,0,buffer.Length,cancel).ConfigureAwait(false))>0)
                    {
                        total+=count;
                        if(total>item.size) throw new InvalidDataException("下载长度校验失败");
                        sha.TransformBlock(buffer,0,count,null,0);
                        await output.WriteAsync(buffer,0,count,cancel).ConfigureAwait(false);
                    }
                    sha.TransformFinalBlock(Array.Empty<byte>(),0,0);
                    if(total!=item.size || Hex(sha.Hash)!=item.sha256) throw new InvalidDataException("下载内容校验失败");
                }
                Install(temporary,target);
            }
            finally {if(File.Exists(temporary)) File.Delete(temporary);}
        }
    }
}
