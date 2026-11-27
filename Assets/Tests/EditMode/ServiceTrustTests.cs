using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AnimeCS.Accounts;
using AnimeCS.Networking;
using NUnit.Framework;

namespace AnimeCS.Tests
{
    public sealed class ServiceTrustTests
    {
        private sealed class ReleaseHandler : HttpMessageHandler
        {
            public byte[] Manifest, Blob;
            public int BlobRequests;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)
            {
                bool listing=request.RequestUri.AbsolutePath.EndsWith("/manifest",StringComparison.Ordinal);
                if (!listing) BlobRequests++;
                byte[] body=listing?Manifest:Blob;
                var response=new HttpResponseMessage(body==null?HttpStatusCode.NoContent:HttpStatusCode.OK);
                if (body!=null) response.Content=new ByteArrayContent(body);
                return Task.FromResult(response);
            }

            public void Release(string version,string text)
            {
                Blob=Encoding.UTF8.GetBytes(text);
                string hash=Fingerprint(Blob);
                Manifest=Encoding.UTF8.GetBytes($"{{\"schema\":1,\"version\":\"{version}\",\"files\":[{{\"path\":\"art/card.txt\",\"sha256\":\"{hash}\",\"size\":{Blob.Length}}}]}}");
            }
        }

        private string root;
        [SetUp] public void SetUp() => root=Path.Combine(Path.GetTempPath(),"AnimeCS-update-test-"+Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown() { if(Directory.Exists(root)) Directory.Delete(root,true); }
        private static string Fingerprint(byte[] bytes)
        {
            using var sha=SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }

        private static async Task MustFail<T>(Func<Task> check) where T:Exception
        {
            try { await check(); } catch (T) { return; }
            Assert.Fail("Expected "+typeof(T).Name);
        }

        [TestCase("https://assets.example.test",false,true)] [TestCase("http://assets.example.test",false,false)]
        [TestCase("http://assets.example.test",true,false)] [TestCase("http://192.0.2.4",true,false)]
        [TestCase("http://localhost",false,true)] [TestCase("http://127.0.0.1",false,true)]
        [TestCase("http://[::1]",false,true)] [TestCase("http://[::ffff:127.0.0.1]",false,true)]
        [TestCase("http://10.12.0.1",false,false)] [TestCase("http://10.12.0.1",true,true)]
        [TestCase("http://172.16.10.2",true,true)] [TestCase("http://172.32.1.1",true,false)]
        [TestCase("http://192.168.1.20",true,true)] [TestCase("http://[fd00::2]",true,true)]
        [TestCase("http://[::ffff:192.0.2.4]",true,false)] [TestCase("https://name@assets.example.test",false,false)]
        public void EndpointsRequireAnAuthenticatedRemoteOrExplicitPrivateNetwork(string uri,bool allowLan,bool accepted)
        {
            if (accepted) Assert.That(ServiceEndpoint.Resolve(uri,27842,allowLan),Is.Not.Empty);
            else Assert.Throws<ArgumentException>(()=>ServiceEndpoint.Resolve(uri,27842,allowLan));
        }

        [Test] public void AccountUpdateAndSettlementRequestsUseDirectOrigins()
        {
            using var handler=ServiceEndpoint.Handler();
            Assert.That(handler.AllowAutoRedirect,Is.False);
            Assert.That(ServiceEndpoint.Resolve(null,27841,false),Is.EqualTo("http://127.0.0.1:27841"));
            Assert.That(UpdateCache.SourceDirectory(root,"https://one.example.test"),
                Is.Not.EqualTo(UpdateCache.SourceDirectory(root,"https://two.example.test")));
        }

        [Test] public async Task PrivateManifestNeedsAnIndependentPinBeforeFetchingBlobs()
        {
            const string url="http://192.168.1.20:27842";
            var release=new ReleaseHandler();
            using var client=new HttpClient(release);
            string empty=await UpdateCache.Check(client,url,root,CancellationToken.None,null,true);
            Assert.That(empty,Does.Contain("内置"));
            release.Release("v1","one");
            await MustFail<InvalidDataException>(()=>UpdateCache.Check(client,url,root,CancellationToken.None,null,true));
            Assert.That(release.BlobRequests,Is.Zero);
            string pin=Fingerprint(release.Manifest);
            await UpdateCache.Check(client,url,root,CancellationToken.None,pin,true);
            string installed=UpdateCache.Active(root,pin);
            Assert.That(File.ReadAllText(Path.Combine(installed,"art","card.txt")),Is.EqualTo("one"));
            release.Release("v2","two");
            await MustFail<InvalidDataException>(()=>UpdateCache.Check(client,url,root,CancellationToken.None,pin,true));
            Assert.That(release.BlobRequests,Is.EqualTo(1));
            Assert.That(UpdateCache.Active(root,pin),Is.EqualTo(installed));
            Assert.That(UpdateCache.Active(root,Fingerprint(release.Manifest)),Is.Null);
        }

        [Test] public async Task HttpsReleaseChecksEachBlobAndPreservesLastVerifiedDirectory()
        {
            const string url="https://assets.example.test";
            var release=new ReleaseHandler();
            using var client=new HttpClient(release);
            release.Release("v1","one");
            await UpdateCache.Check(client,url,root,CancellationToken.None);
            string installed=UpdateCache.Active(root);
            release.Release("v2","two");
            release.Blob=Encoding.UTF8.GetBytes("bad");
            await MustFail<InvalidDataException>(()=>UpdateCache.Check(client,url,root,CancellationToken.None));
            Assert.That(UpdateCache.Active(root),Is.EqualTo(installed));
            Assert.That(Directory.GetFiles(root,"*.part",SearchOption.AllDirectories),Is.Empty);
            release.Manifest=null;
            Assert.That(await UpdateCache.Check(client,url,root,CancellationToken.None),Does.Contain("已验证"));
        }

        [Test] public async Task LocalPublisherFingerprintComesFromTheLocalRelease()
        {
            const string url="http://127.0.0.1:27842";
            var release=new ReleaseHandler();
            using var client=new HttpClient(release);
            release.Release("local","one");
            string path=Path.Combine(root,"publisher","current.json");
            Assert.That(UpdateCache.LocalDigest(path),Is.Null);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path,release.Manifest);
            string pin=UpdateCache.LocalDigest(path);
            Assert.That(pin,Is.EqualTo(Fingerprint(release.Manifest)));
            await UpdateCache.Check(client,url,root,CancellationToken.None,pin);
            Assert.That(UpdateCache.Active(root,pin),Is.Not.Null);
            Assert.Throws<InvalidDataException>(()=>UpdateCache.NormalizePin("invalid"));
        }
    }
}
