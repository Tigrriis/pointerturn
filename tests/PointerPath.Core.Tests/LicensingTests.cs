using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PointerPath.Core.Licensing;
using Xunit;

namespace PointerPath.Core.Tests
{
    /// <summary>The licence check: reading a signed licence, and signing in, refreshing and signing out.</summary>
    public class LicensingTests : IDisposable
    {
        private const string Website = "https://pointerpath.test";
        private const string Machine = "machine-1";

        private readonly string _folder = Path.Combine(Path.GetTempPath(), "pointerpath-tests-" + Guid.NewGuid().ToString("N"));
        private readonly TestKey _key = new TestKey();
        private readonly FakeServer _server = new FakeServer();
        private DateTime _now = new DateTime(2026, 11, 14, 4, 0, 0, DateTimeKind.Utc);

        private string StorePath => Path.Combine(_folder, "licence.json");

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        private LicenceManager Manager(string website = Website, LicencePublicKey? key = null, string machine = Machine)
        {
            return new LicenceManager(website, key ?? _key.Public, StorePath, machine, _server, () => _now);
        }

        /// <summary>A licence as the website would issue it now, good for 14 days.</summary>
        private string Licence(string machine = Machine, TestKey? signedBy = null, int days = 14)
        {
            return (signedBy ?? _key).Sign(new Dictionary<string, object>
            {
                ["iss"] = "pointerpath",
                ["sub"] = "7",
                ["email"] = "ada@example.com",
                ["lic"] = "12",
                ["kind"] = "perpetual",
                ["mid"] = machine,
                ["iat"] = Seconds(_now),
                ["exp"] = Seconds(_now.AddDays(days)),
            });
        }

        private static long Seconds(DateTime utc) => (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        private async Task<LicenceManager> SignedIn()
        {
            LicenceManager manager = Manager();
            _server.Activate = () => new LicenceReply { ActivationToken = "act-1", LicenceToken = Licence() };
            Assert.Equal(LicenceState.Licensed, (await manager.SignInAsync("ada@example.com", "pw", "DESK")).State);
            return manager;
        }

        // ------------------------------------------------------------ reading a licence

        [Fact]
        public void ReadsALicenceSignedTheWayTheWebsiteSignsThem()
        {
            // Made with Node's crypto exactly as website/src/licences.js does: ES256, the
            // signature as r then s. Expires in 2100.
            var key = new LicencePublicKey("T7QYpoQSoge-JG8xmiDSg_vVCLWJoO_20V_T39Dxnio", "xt04Y3sBVqGpGy3s1G5TZ83KRf-2MAF7GiCGlPiFads");
            const string token =
                "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6InRlc3QifQ." +
                "eyJpc3MiOiJwb2ludGVycGF0aCIsInN1YiI6IjciLCJlbWFpbCI6ImFkYUBleGFtcGxlLmNvbSIsImxpYyI6IjEyIiwia2luZCI6InBlcnBldHVhbCIsIm1pZCI6Im1hY2hpbmUtMSIsImlhdCI6MTc5NDYyODgwMCwiZXhwIjo0MTAyNDQ0ODAwfQ." +
                "Jpb_522zrmmyrtO4od_PcmDIeb2Ku8qpSEXIQfPUorOfH0TwsVp7D-PlatxwewJk6Un36XJkR8pujKWC-tHsYw";

            SignedLicence? licence = SignedLicence.TryRead(token, key, out string problem);

            Assert.NotNull(licence);
            Assert.Equal("", problem);
            Assert.Equal("ada@example.com", licence!.Email);
            Assert.Equal("12", licence.LicenceId);
            Assert.Equal("perpetual", licence.Kind);
            Assert.Equal("machine-1", licence.MachineId);
            Assert.Equal(new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc), licence.ExpiresUtc);
        }

        [Fact]
        public void RefusesALicenceThatWasAlteredOrSignedBySomeoneElse()
        {
            string good = Licence();
            Assert.NotNull(SignedLicence.TryRead(good, _key.Public, out _));

            // The claims changed (a later expiry), the signature kept.
            string[] parts = good.Split('.');
            string forged = TestKey.Encode(new Dictionary<string, object>
            {
                ["iss"] = "pointerpath", ["mid"] = Machine, ["exp"] = Seconds(_now.AddYears(50)),
            });
            Assert.Null(SignedLicence.TryRead(parts[0] + "." + forged + "." + parts[2], _key.Public, out string problem));
            Assert.Contains("signature", problem);

            // Signed properly, but by a different key.
            Assert.Null(SignedLicence.TryRead(Licence(signedBy: new TestKey()), _key.Public, out _));

            // An unsigned token that says it needs no signature.
            string none = TestKey.Encode(new Dictionary<string, object> { ["alg"] = "none" }) + "." + parts[1] + ".";
            Assert.Null(SignedLicence.TryRead(none, _key.Public, out _));

            // Signed by the right key, but not a PointerPath licence.
            Assert.Null(SignedLicence.TryRead(_key.Sign(new Dictionary<string, object> { ["iss"] = "someone-else", ["mid"] = Machine, ["exp"] = Seconds(_now.AddDays(1)) }), _key.Public, out _));
            Assert.Null(SignedLicence.TryRead(_key.Sign(new Dictionary<string, object> { ["iss"] = "pointerpath", ["exp"] = Seconds(_now.AddDays(1)) }), _key.Public, out _));

            foreach (string? junk in new[] { null, "", "not a licence", "a.b.c", "....", parts[0] + "." + parts[1], good + "x" })
            {
                Assert.Null(SignedLicence.TryRead(junk, _key.Public, out _));
            }

            // No key built in: nothing verifies.
            Assert.Null(SignedLicence.TryRead(good, new LicencePublicKey("", ""), out _));
        }

        // ------------------------------------------------------------ where a computer stands

        [Fact]
        public void ABuildWithNoServerOrKeyDoesNotAskForALicence()
        {
            Assert.Equal(LicenceState.NotConfigured, Manager(website: "").Check().State);
            Assert.Equal(LicenceState.NotConfigured, Manager(key: new LicencePublicKey("", "")).Check().State);
            Assert.True(Manager(website: "").Check().MayRun);
            Assert.False(Manager(website: "").RefreshDue);
        }

        [Fact]
        public void AComputerThatHasNeverSignedInIsSignedOut()
        {
            LicenceStatus status = Manager().Check();
            Assert.Equal(LicenceState.SignedOut, status.State);
            Assert.False(status.MayRun);
            Assert.False(status.CanRefresh);
        }

        [Fact]
        public async Task SigningInSavesTheLicenceAndItWorksWithNoConnection()
        {
            LicenceManager manager = await SignedIn();
            Assert.Equal(("ada@example.com", "pw", Machine, "DESK"), _server.LastActivation);

            // A new session, and the website unreachable: the saved licence is enough.
            _server.Activate = () => throw new InvalidOperationException("should not be called");
            _server.Refresh = () => throw new InvalidOperationException("should not be called");
            LicenceStatus status = Manager().Check();
            Assert.Equal(LicenceState.Licensed, status.State);
            Assert.Equal("ada@example.com", status.Email);
            Assert.Equal(_now.AddDays(14), status.ExpiresUtc);
            Assert.False(manager.RefreshDue);
        }

        [Theory]
        [InlineData("invalid_credentials", "do not match")]
        [InlineData("email_not_verified", "confirmed")]
        [InlineData("no_licence", "https://pointerpath.test")]
        [InlineData("region_not_allowed", "not available in the country")]
        [InlineData("network", "could not reach https://pointerpath.test")]
        [InlineData("too_many_attempts", "Too many attempts")]
        [InlineData("something_new", "something_new")]
        public async Task ARefusedSignInSaysWhy(string error, string expected)
        {
            _server.Activate = () => new LicenceReply { Error = error };
            LicenceStatus status = await Manager().SignInAsync("ada@example.com", "pw", "DESK");
            Assert.Equal(LicenceState.SignedOut, status.State);
            Assert.Contains(expected, status.Message);
            Assert.False(File.Exists(StorePath));
        }

        [Fact]
        public async Task AFullLicenceNamesTheComputersUsingIt()
        {
            _server.Activate = () =>
            {
                var reply = new LicenceReply { Error = "machine_limit" };
                reply.Machines.Add("DESIGN-PC-04");
                reply.Machines.Add("Site laptop");
                return reply;
            };
            LicenceStatus status = await Manager().SignInAsync("ada@example.com", "pw", "DESK");
            Assert.Contains("DESIGN-PC-04, Site laptop", status.Message);
            Assert.Contains("https://pointerpath.test/account", status.Message);
        }

        [Fact]
        public async Task SignInNeedsAnEmailAndPassword()
        {
            _server.Activate = () => throw new InvalidOperationException("should not be called");
            Assert.Equal(LicenceState.SignedOut, (await Manager().SignInAsync(" ", "pw", "DESK")).State);
            Assert.Equal(LicenceState.SignedOut, (await Manager().SignInAsync("ada@example.com", "", "DESK")).State);
        }

        [Fact]
        public async Task ALicenceTheServerSendsIsCheckedBeforeItIsTrusted()
        {
            // For another computer, signed by another key, or missing.
            foreach (string? token in new[] { Licence(machine: "someone-elses-pc"), Licence(signedBy: new TestKey()), null })
            {
                _server.Activate = () => new LicenceReply { ActivationToken = "act-1", LicenceToken = token };
                LicenceStatus status = await Manager().SignInAsync("ada@example.com", "pw", "DESK");
                Assert.Equal(LicenceState.SignedOut, status.State);
                Assert.Contains("could not be checked", status.Message);
                Assert.False(File.Exists(StorePath));
            }
        }

        [Fact]
        public async Task ALicenceCopiedToAnotherComputerDoesNotWorkThere()
        {
            await SignedIn();
            LicenceStatus status = Manager(machine: "another-pc").Check();
            Assert.Equal(LicenceState.SignedOut, status.State);
            Assert.False(status.CanRefresh);
        }

        // ------------------------------------------------------------ expiry and refreshing

        [Fact]
        public async Task ALicenceRunsOutAndARefreshBringsItBack()
        {
            LicenceManager manager = await SignedIn();

            _now = _now.AddDays(13);
            Assert.Equal(LicenceState.Licensed, manager.Check().State);
            Assert.True(manager.RefreshDue);

            _now = _now.AddDays(2);
            LicenceStatus expired = manager.Check();
            Assert.Equal(LicenceState.SignedOut, expired.State);
            Assert.True(expired.CanRefresh);
            Assert.Contains("14 November 2026", expired.Message);

            _server.Refresh = () => new LicenceReply { LicenceToken = Licence() };
            LicenceStatus refreshed = await manager.RefreshAsync();
            Assert.Equal("act-1", _server.LastRefreshToken);
            Assert.Equal(LicenceState.Licensed, refreshed.State);
            Assert.Equal(_now.AddDays(14), refreshed.ExpiresUtc);
            Assert.False(manager.RefreshDue);
        }

        [Fact]
        public async Task SettingTheClockBackDoesNotReviveAnExpiredLicence()
        {
            LicenceManager manager = await SignedIn();
            DateTime signedInAt = _now;

            _now = signedInAt.AddDays(15);
            Assert.Equal(LicenceState.SignedOut, manager.Check().State);

            _now = signedInAt.AddDays(1);
            Assert.Equal(LicenceState.SignedOut, Manager().Check().State);
            // A clock that far behind the last refresh also means a refresh is due.
            Assert.True(manager.RefreshDue);
        }

        [Fact]
        public async Task WithNoConnectionARefreshLeavesTheLicenceAlone()
        {
            LicenceManager manager = await SignedIn();
            _now = _now.AddDays(3);
            // The same when the website won't issue a licence where the computer is (a trip
            // abroad): the one held runs its course.
            foreach (string error in new[] { LicenceReply.Network, LicenceReply.Unexpected, LicenceReply.TooManyAttempts, "region_not_allowed" })
            {
                _server.Refresh = () => new LicenceReply { Error = error };
                Assert.Equal(LicenceState.Licensed, (await manager.RefreshAsync()).State);
            }

            // A "fresh" licence that doesn't verify is ignored too.
            _server.Refresh = () => new LicenceReply { LicenceToken = Licence(signedBy: new TestKey()) };
            LicenceStatus status = await manager.RefreshAsync();
            Assert.Equal(LicenceState.Licensed, status.State);
            Assert.Equal(_now.AddDays(11), status.ExpiresUtc);
            Assert.True(manager.RefreshDue);
        }

        [Fact]
        public async Task BeingSignedOutFromTheAccountPageTakesEffectAtTheNextRefresh()
        {
            LicenceManager manager = await SignedIn();
            _server.Refresh = () => new LicenceReply { Error = "invalid_activation" };

            LicenceStatus status = await manager.RefreshAsync();

            Assert.Equal(LicenceState.SignedOut, status.State);
            Assert.False(status.CanRefresh);
            Assert.Contains("signed out", status.Message);
            Assert.Equal(LicenceState.SignedOut, Manager().Check().State);
        }

        [Fact]
        public async Task AnEndedLicenceStopsAtOnceAndComesBackWhenRenewed()
        {
            LicenceManager manager = await SignedIn();
            _server.Refresh = () => new LicenceReply { Error = "licence_inactive" };

            LicenceStatus ended = await manager.RefreshAsync();
            Assert.Equal(LicenceState.SignedOut, ended.State);
            Assert.True(ended.CanRefresh);
            Assert.Contains("has ended", ended.Message);
            Assert.Contains("has ended", Manager().Check().Message);

            _server.Refresh = () => new LicenceReply { LicenceToken = Licence() };
            LicenceStatus renewed = await manager.RefreshAsync();
            Assert.Equal(LicenceState.Licensed, renewed.State);
            Assert.Equal("", renewed.Message);
        }

        [Fact]
        public async Task SigningOutForgetsTheLicenceAndFreesThePlace()
        {
            LicenceManager manager = await SignedIn();

            await manager.SignOutAsync();

            Assert.Equal("act-1", _server.LastDeactivateToken);
            Assert.False(File.Exists(StorePath));
            Assert.Equal(LicenceState.SignedOut, manager.Check().State);
            Assert.Equal(LicenceState.SignedOut, (await manager.RefreshAsync()).State);

            // With no connection it still signs out here.
            await SignedIn();
            _server.Deactivate = () => new LicenceReply { Error = LicenceReply.Network };
            await manager.SignOutAsync();
            Assert.Equal(LicenceState.SignedOut, manager.Check().State);
        }

        [Fact]
        public async Task ADamagedLicenceFileIsTheSameAsBeingSignedOut()
        {
            await SignedIn();
            File.WriteAllText(StorePath, "{ not json");
            Assert.Equal(LicenceState.SignedOut, Manager().Check().State);
        }

        // ------------------------------------------------------------ talking to the website

        [Fact]
        public async Task TheServerClientSpeaksTheWebsitesApi()
        {
            var handler = new StubHandler();
            var server = new LicenceServer(Website + "/", "PointerPath/test", handler);

            handler.Respond(HttpStatusCode.OK, "{\"activation_token\":\"act-9\",\"licence_token\":\"a.b.c\",\"licence\":{\"id\":\"12\"}}");
            LicenceReply ok = await server.ActivateAsync("ada@example.com", "pw", Machine, "DESK");
            Assert.True(ok.Ok);
            Assert.Equal("act-9", ok.ActivationToken);
            Assert.Equal("a.b.c", ok.LicenceToken);
            Assert.Equal("https://pointerpath.test/api/v1/activate", handler.LastUrl);
            using (JsonDocument sent = JsonDocument.Parse(handler.LastBody!))
            {
                Assert.Equal("ada@example.com", sent.RootElement.GetProperty("email").GetString());
                Assert.Equal("pw", sent.RootElement.GetProperty("password").GetString());
                Assert.Equal(Machine, sent.RootElement.GetProperty("machine_id").GetString());
                Assert.Equal("DESK", sent.RootElement.GetProperty("machine_name").GetString());
            }

            handler.Respond(HttpStatusCode.Conflict, "{\"error\":\"machine_limit\",\"max_machines\":2,\"machines\":[{\"name\":\"DESK-1\"},{\"name\":\"\"}]}");
            LicenceReply full = await server.ActivateAsync("ada@example.com", "pw", Machine, "DESK");
            Assert.Equal("machine_limit", full.Error);
            Assert.Equal(new[] { "DESK-1", "an unnamed computer" }, full.Machines);

            handler.Respond(HttpStatusCode.PaymentRequired, "{\"error\":\"licence_inactive\"}");
            Assert.Equal("licence_inactive", (await server.RefreshAsync("act-9")).Error);
            Assert.Equal("https://pointerpath.test/api/v1/refresh", handler.LastUrl);
            Assert.Contains("act-9", handler.LastBody);

            handler.Respond(HttpStatusCode.OK, "{}");
            Assert.True((await server.DeactivateAsync("act-9")).Ok);
            Assert.Equal("https://pointerpath.test/api/v1/deactivate", handler.LastUrl);

            handler.Respond((HttpStatusCode)429, "Too many requests");
            Assert.Equal(LicenceReply.TooManyAttempts, (await server.RefreshAsync("act-9")).Error);

            // A proxy's or host's own error page, with or without JSON in it.
            handler.Respond(HttpStatusCode.BadGateway, "<html>Bad gateway</html>");
            Assert.Equal(LicenceReply.Unexpected, (await server.RefreshAsync("act-9")).Error);
            handler.Respond(HttpStatusCode.ServiceUnavailable, "{\"message\":\"down\"}");
            Assert.Equal(LicenceReply.Unexpected, (await server.RefreshAsync("act-9")).Error);
            handler.Respond(HttpStatusCode.OK, "[]");
            Assert.Equal(LicenceReply.Unexpected, (await server.RefreshAsync("act-9")).Error);

            handler.Fail(new HttpRequestException("no such host"));
            Assert.Equal(LicenceReply.Network, (await server.RefreshAsync("act-9")).Error);
            handler.Fail(new TaskCanceledException("timed out"));
            Assert.Equal(LicenceReply.Network, (await server.RefreshAsync("act-9")).Error);
        }

        // ------------------------------------------------------------ stand-ins

        /// <summary>A signing key like the website's, for making licences in tests.</summary>
        private sealed class TestKey
        {
            private readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            public TestKey()
            {
                ECParameters p = _ecdsa.ExportParameters(includePrivateParameters: false);
                Public = new LicencePublicKey(Url(p.Q.X!), Url(p.Q.Y!));
            }

            public LicencePublicKey Public { get; }

            public string Sign(Dictionary<string, object> claims)
            {
                string input = Encode(new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "JWT" }) + "." + Encode(claims);
                return input + "." + Url(_ecdsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256));
            }

            public static string Encode(Dictionary<string, object> value) => Url(JsonSerializer.SerializeToUtf8Bytes(value));

            private static string Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private sealed class FakeServer : ILicenceServer
        {
            public Func<LicenceReply> Activate { get; set; } = () => new LicenceReply { Error = "no_licence" };

            public Func<LicenceReply> Refresh { get; set; } = () => new LicenceReply { Error = LicenceReply.Network };

            public Func<LicenceReply> Deactivate { get; set; } = () => new LicenceReply();

            public (string Email, string Password, string MachineId, string MachineName)? LastActivation { get; private set; }

            public string? LastRefreshToken { get; private set; }

            public string? LastDeactivateToken { get; private set; }

            public Task<LicenceReply> ActivateAsync(string email, string password, string machineId, string machineName)
            {
                LastActivation = (email, password, machineId, machineName);
                return Task.FromResult(Activate());
            }

            public Task<LicenceReply> RefreshAsync(string activationToken)
            {
                LastRefreshToken = activationToken;
                return Task.FromResult(Refresh());
            }

            public Task<LicenceReply> DeactivateAsync(string activationToken)
            {
                LastDeactivateToken = activationToken;
                return Task.FromResult(Deactivate());
            }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private Func<HttpResponseMessage> _next = () => new HttpResponseMessage(HttpStatusCode.OK);

            public string? LastUrl { get; private set; }

            public string? LastBody { get; private set; }

            public void Respond(HttpStatusCode status, string body)
            {
                _next = () => new HttpResponseMessage(status) { Content = new StringContent(body) };
            }

            public void Fail(Exception exception)
            {
                _next = () => throw exception;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastUrl = request.RequestUri!.ToString();
                LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return _next();
            }
        }
    }
}
