using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PointerPath.Core.Licensing
{
    /// <summary>What the website said to a licence request.</summary>
    public sealed class LicenceReply
    {
        /// <summary>The website was not reached at all (no connection, timeout).</summary>
        public const string Network = "network";

        /// <summary>The website answered, but not with anything this version understands.</summary>
        public const string Unexpected = "unexpected";

        public const string TooManyAttempts = "too_many_attempts";

        public bool Ok => Error == null;

        /// <summary>Null on success; else the website's error code, or one of the constants above.</summary>
        public string? Error { get; set; }

        public string? ActivationToken { get; set; }

        public string? LicenceToken { get; set; }

        /// <summary>With "machine_limit": the names of the computers the licence is in use on.</summary>
        public List<string> Machines { get; } = new List<string>();
    }

    /// <summary>The website's licence API (website/README.md, "Licence API").</summary>
    public interface ILicenceServer
    {
        Task<LicenceReply> ActivateAsync(string email, string password, string machineId, string machineName);

        Task<LicenceReply> RefreshAsync(string activationToken);

        Task<LicenceReply> DeactivateAsync(string activationToken);
    }

    public sealed class LicenceServer : ILicenceServer
    {
        private readonly HttpClient _http;
        private readonly string _api;

        /// <param name="website">The website's address, e.g. https://www.example.com.</param>
        /// <param name="userAgent">How this add-in names itself to the website, e.g. "PointerPath/1.2.0".</param>
        public LicenceServer(string website, string userAgent)
            : this(website, userAgent, new HttpClientHandler())
        {
        }

        internal LicenceServer(string website, string userAgent, HttpMessageHandler handler)
        {
            _api = website.TrimEnd('/') + "/api/v1/";
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        }

        public Task<LicenceReply> ActivateAsync(string email, string password, string machineId, string machineName)
        {
            return PostAsync("activate", new Dictionary<string, string>
            {
                ["email"] = email,
                ["password"] = password,
                ["machine_id"] = machineId,
                ["machine_name"] = machineName,
            });
        }

        public Task<LicenceReply> RefreshAsync(string activationToken)
        {
            return PostAsync("refresh", new Dictionary<string, string> { ["activation_token"] = activationToken });
        }

        public Task<LicenceReply> DeactivateAsync(string activationToken)
        {
            return PostAsync("deactivate", new Dictionary<string, string> { ["activation_token"] = activationToken });
        }

        private async Task<LicenceReply> PostAsync(string path, Dictionary<string, string> body)
        {
            string text;
            int status;
            try
            {
                using (var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await _http.PostAsync(_api + path, content).ConfigureAwait(false))
                {
                    status = (int)response.StatusCode;
                    text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException)
            {
                return new LicenceReply { Error = LicenceReply.Network };
            }

            if (status == 429)
            {
                return new LicenceReply { Error = LicenceReply.TooManyAttempts };
            }

            var reply = new LicenceReply();
            try
            {
                using (JsonDocument document = JsonDocument.Parse(text))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        reply.Error = LicenceReply.Unexpected;
                        return reply;
                    }

                    reply.Error = Text(root, "error");
                    reply.ActivationToken = Text(root, "activation_token");
                    reply.LicenceToken = Text(root, "licence_token");
                    if (root.TryGetProperty("machines", out JsonElement machines) && machines.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement machine in machines.EnumerateArray())
                        {
                            string? name = Text(machine, "name");
                            reply.Machines.Add(string.IsNullOrWhiteSpace(name) ? "an unnamed computer" : name!);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                reply.Error = LicenceReply.Unexpected;
                return reply;
            }

            // A refusal always names its reason; a reply that is neither a success nor a named
            // refusal (a proxy's error page that happened to be JSON, say) is not trusted.
            if (reply.Error == null && (status < 200 || status > 299))
            {
                reply.Error = LicenceReply.Unexpected;
            }

            return reply;
        }

        private static string? Text(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }
}
