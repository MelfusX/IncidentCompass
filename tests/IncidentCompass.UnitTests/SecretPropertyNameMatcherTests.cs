using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Redaction;

namespace IncidentCompass.UnitTests;

/// <summary>
/// The property-name denylist matches normalized segment runs, so real header and payload spellings
/// such as <c>x-api-key</c> or <c>user_password_hash</c> are redacted while near-miss names such as
/// <c>session_id</c> stay readable. Both directions are asserted because over-matching would quietly
/// destroy ordinary incident context.
/// </summary>
public sealed class SecretPropertyNameMatcherTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("secret")]
    [InlineData("token")]
    [InlineData("apikey")]
    [InlineData("api_key")]
    [InlineData("apiKey")]
    [InlineData("accesskey")]
    [InlineData("access_key")]
    [InlineData("clientsecret")]
    [InlineData("client_secret")]
    [InlineData("connectionstring")]
    [InlineData("connection_string")]
    [InlineData("privatekey")]
    [InlineData("private_key")]
    [InlineData("authorization")]
    [InlineData("x-api-key")]
    [InlineData("X-API-Key")]
    [InlineData("XApiKey")]
    [InlineData("APIKey")]
    [InlineData("cookie")]
    [InlineData("Cookie")]
    [InlineData("set-cookie")]
    [InlineData("Set-Cookie")]
    [InlineData("jwt")]
    [InlineData("session")]
    [InlineData("user_password_hash")]
    [InlineData("db_password_2")]
    [InlineData("password2")]
    [InlineData("Authorization-Bearer")]
    [InlineData("access_token")]
    [InlineData("refresh.token")]
    [InlineData("deviceToken")]
    public void IsSensitive_SecretBearingNamesAreMatched(string propertyName) =>
        Assert.True(SecretPropertyNameMatcher.IsSensitive(propertyName), propertyName);

    [Theory]
    [InlineData("session_id")]
    [InlineData("sessionId")]
    [InlineData("session_start_time")]
    [InlineData("sessionCount")]
    [InlineData("keyword")]
    [InlineData("monkey")]
    [InlineData("key_count")]
    [InlineData("key")]
    [InlineData("tokenizer")]
    [InlineData("inputTokens")]
    [InlineData("totalTokens")]
    [InlineData("serviceName")]
    [InlineData("errorMessage")]
    [InlineData("user")]
    [InlineData("id")]
    [InlineData("")]
    public void IsSensitive_NearMissNamesAreNotMatched(string propertyName) =>
        Assert.False(SecretPropertyNameMatcher.IsSensitive(propertyName), propertyName);

    [Fact]
    public void RedactJsonNode_WidenedDenylistRedactsHeadersButKeepsSessionIdentifiers()
    {
        var node = JsonNode.Parse("""
            {
              "http": {
                "request": { "x-api-key": "live-key-value", "Cookie": "sid=abc123" },
                "response": { "Set-Cookie": "sid=def456" }
              },
              "user_password_hash": "argon2id$hash",
              "session": "session-secret",
              "session_id": "s-1042",
              "sessionCount": 3,
              "keyword": "checkout"
            }
            """)!;

        var redacted = SecretRedactor.RedactJsonNode(node);

        var request = redacted["http"]!["request"]!;
        Assert.Equal("[REDACTED]", request["x-api-key"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", request["Cookie"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", redacted["http"]!["response"]!["Set-Cookie"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", redacted["user_password_hash"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", redacted["session"]!.GetValue<string>());
        Assert.Equal("s-1042", redacted["session_id"]!.GetValue<string>());
        Assert.Equal(3, redacted["sessionCount"]!.GetValue<int>());
        Assert.Equal("checkout", redacted["keyword"]!.GetValue<string>());
    }
}
