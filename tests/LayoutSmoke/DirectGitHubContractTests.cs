using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using DISM_GUI;

internal static class DirectGitHubContractTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int IssuePostCount;
        public bool LeakedInIssue;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? "";
            var body = request.Content is null ? "" :
                       await request.Content.ReadAsStringAsync(cancellationToken);
            if (!uri.StartsWith("https://github.com/", StringComparison.Ordinal) &&
                !uri.StartsWith("https://api.github.com/", StringComparison.Ordinal))
                throw new SecurityException("Unexpected URL in GitHub contract test.");
            if (uri.EndsWith("/login/device/code"))
            {
                if (body.Contains("client_secret")) throw new Exception("Secret in device request.");
                return Json(HttpStatusCode.OK,
                    "{\"device_code\":\"0123456789012345678901234567890123456789\"," +
                    "\"user_code\":\"ABCD-EFGH\"," +
                    "\"verification_uri\":\"https://github.com/login/device\"," +
                    "\"interval\":5,\"expires_in\":60}");
            }
            if (uri.EndsWith("/login/oauth/access_token"))
                return Json(HttpStatusCode.OK,
                    "{\"access_token\":\"mock-token-for-test-only-12345\"}");
            if (uri.EndsWith("/user"))
            {
                if (request.Headers.Authorization?.Scheme != "Bearer")
                    throw new Exception("Missing user verification bearer header.");
                return Json(HttpStatusCode.OK, "{\"login\":\"test-account\"}");
            }
            if (uri.EndsWith("/repos/sgennadi/DISM-GUI/issues"))
            {
                if (request.Method != HttpMethod.Post ||
                    request.Headers.Authorization?.Scheme != "Bearer")
                    throw new Exception("Issue requires authenticated POST.");
                IssuePostCount++;
                LeakedInIssue = body.Contains("DO_NOT_POST_SECRET", StringComparison.Ordinal);
                return Json(HttpStatusCode.Created,
                    "{\"number\":41," +
                    "\"html_url\":\"https://github.com/sgennadi/DISM-GUI/issues/41\"}");
            }
            throw new Exception("Unexpected GitHub test endpoint.");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string value)
            => new(status) { Content = new StringContent(value) };
    }

    public static void Run()
    {
        if (!DirectGitHubAccount.ValidClientId("Iv1.Test1234") ||
            DirectGitHubAccount.ValidClientId("bad client\nsecret"))
            throw new Exception("Public GitHub client ID validation regression.");

        using var handler = new FakeHandler();
        using var transport = new HttpClient(handler);
        var client = new DirectGitHubClient(transport);
        var code = client.StartDeviceFlowAsync("Iv1.Test1234",
                      CancellationToken.None).GetAwaiter().GetResult();
        if (code.UserCode != "ABCD-EFGH" ||
            code.VerificationUri != "https://github.com/login/device")
            throw new Exception("Device authorization response was not validated.");

        var token = client.FinishDeviceFlowAsync("Iv1.Test1234", code,
                      CancellationToken.None).GetAwaiter().GetResult();
        var account = client.WhoAmIAsync(token,
                      CancellationToken.None).GetAwaiter().GetResult();
        if (account != "test-account")
            throw new Exception("GitHub account identity check failed.");

        var issue = client.CreateIssueAsync(token, "Example failure",
                 "Password: DO_NOT_POST_SECRET\nSafe error text",
                 CancellationToken.None).GetAwaiter().GetResult();
        if (issue.Number != 41 || handler.IssuePostCount != 1 ||
            handler.LeakedInIssue)
            throw new Exception("Direct report endpoint, response or redaction failed.");

        DirectGitHubDedup.Remember("Example failure", "Safe error text", issue.Url);
        if (!DirectGitHubDedup.AlreadySubmitted("Example failure", "Safe error text"))
            throw new Exception("Duplicate issue guard did not remember submission.");
        Console.WriteLine("PASS simulated GitHub Device Flow, account identity, Issue redaction and duplicate guard.");
    }
}
