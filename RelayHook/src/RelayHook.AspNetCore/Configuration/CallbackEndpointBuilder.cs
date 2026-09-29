using System.Text.Json;
using RelayHook.Authentication;
using RelayHook.Authentication.ApiKey;
using RelayHook.Authentication.Basic;
using RelayHook.Authentication.Bearer;
using RelayHook.Authentication.Jwt;
using RelayHook.Authentication.OAuth;
using RelayHook.AspNetCore.Delivery;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Configuration;

/// <summary>Builds one named callback endpoint and its non-secret authentication configuration.</summary>
public sealed class CallbackEndpointBuilder
{
    private readonly string _name;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private string _authenticationType = AuthenticationTypeNames.None;
    private JsonElement _authenticationConfiguration = JsonSerializer.SerializeToElement(new { });

    internal CallbackEndpointBuilder(string name) => _name = name;

    /// <summary>Absolute callback destination.</summary>
    public Uri? Url { get; set; }

    /// <summary>HTTP method. POST, PUT, and PATCH are supported.</summary>
    public HttpMethod Method { get; set; } = HttpMethod.Post;

    /// <summary>Payload content type.</summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>Per-request timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Adds a non-sensitive request header.</summary>
    public CallbackEndpointBuilder AddHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        if (!_headers.TryAdd(name, value))
        {
            throw new CallbackConfigurationException($"Header '{name}' is already configured.");
        }

        return this;
    }

    /// <summary>Uses no authentication.</summary>
    public CallbackEndpointBuilder UseNoAuthentication() =>
        UseAuthentication(AuthenticationTypeNames.None, new { });

    /// <summary>Uses a secret-resolved API-key header.</summary>
    public CallbackEndpointBuilder UseApiKey(Action<ApiKeyAuthenticationOptions> configure) =>
        ConfigureAuthentication(AuthenticationTypeNames.ApiKey, configure);

    /// <summary>Uses HTTP Basic authentication.</summary>
    public CallbackEndpointBuilder UseBasicAuthentication(Action<BasicAuthenticationOptions> configure) =>
        ConfigureAuthentication(AuthenticationTypeNames.Basic, configure);

    /// <summary>Uses a secret-resolved static bearer token.</summary>
    public CallbackEndpointBuilder UseBearerToken(Action<BearerAuthenticationOptions> configure) =>
        ConfigureAuthentication(AuthenticationTypeNames.Bearer, configure);

    /// <summary>Generates one short-lived HS256 JWT per delivery.</summary>
    public CallbackEndpointBuilder UseGeneratedJwt(Action<JwtAuthenticationOptions> configure) =>
        ConfigureAuthentication(AuthenticationTypeNames.Jwt, configure);

    /// <summary>Uses cached OAuth2 client-credentials tokens.</summary>
    public CallbackEndpointBuilder UseOAuth2ClientCredentials(
        Action<OAuth2ClientCredentialsOptions> configure) =>
        ConfigureAuthentication(AuthenticationTypeNames.OAuth2ClientCredentials, configure);

    /// <summary>Selects a custom registered authenticator and stores only its non-secret configuration.</summary>
    public CallbackEndpointBuilder UseAuthentication<TOptions>(string type, TOptions options)
        where TOptions : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(options);
        _authenticationType = type;
        _authenticationConfiguration = JsonSerializer.SerializeToElement(options, AuthenticationJson.Options);
        return this;
    }

    internal CallbackEndpointDefinition Build()
    {
        if (Url is null || !Url.IsAbsoluteUri)
        {
            throw new CallbackConfigurationException($"Endpoint '{_name}' requires an absolute URL.");
        }

        if (Url.AbsoluteUri.Length > 2048)
        {
            throw new CallbackConfigurationException($"Endpoint '{_name}' URL cannot exceed 2048 characters.");
        }

        if (Method != HttpMethod.Post && Method != HttpMethod.Put && Method != HttpMethod.Patch)
        {
            throw new CallbackConfigurationException(
                $"Endpoint '{_name}' method must be POST, PUT, or PATCH.");
        }

        if (string.IsNullOrWhiteSpace(ContentType) || ContentType.Length > 100 || Timeout <= TimeSpan.Zero)
        {
            throw new CallbackConfigurationException(
                $"Endpoint '{_name}' requires a content type and positive timeout.");
        }

        return new CallbackEndpointDefinition(
            _name,
            Url,
            Method,
            ContentType,
            Timeout,
            _authenticationType,
            _authenticationConfiguration.Clone(),
            new Dictionary<string, string>(_headers, StringComparer.OrdinalIgnoreCase));
    }

    private CallbackEndpointBuilder ConfigureAuthentication<TOptions>(
        string type,
        Action<TOptions> configure)
        where TOptions : class, new()
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TOptions();
        configure(options);
        ValidateBuiltInAuthentication(options);
        return UseAuthentication(type, options);
    }

    private static void ValidateBuiltInAuthentication<TOptions>(TOptions value)
    {
        var valid = value switch
        {
            ApiKeyAuthenticationOptions options =>
                !string.IsNullOrWhiteSpace(options.HeaderName) &&
                !string.IsNullOrWhiteSpace(options.SecretReference) &&
                IsPermittedAuthenticationHeader(options.HeaderName),
            BasicAuthenticationOptions options =>
                !string.IsNullOrWhiteSpace(options.Username) &&
                !options.Username.Contains(':') &&
                !string.IsNullOrWhiteSpace(options.PasswordSecretReference),
            BearerAuthenticationOptions options =>
                !string.IsNullOrWhiteSpace(options.TokenSecretReference),
            JwtAuthenticationOptions options =>
                !string.IsNullOrWhiteSpace(options.Issuer) &&
                !string.IsNullOrWhiteSpace(options.Audience) &&
                !string.IsNullOrWhiteSpace(options.SigningKeySecretReference) &&
                options.Lifetime > TimeSpan.Zero &&
                options.Lifetime <= TimeSpan.FromHours(1) &&
                options.Claims is not null,
            OAuth2ClientCredentialsOptions options =>
                Uri.TryCreate(options.TokenEndpoint, UriKind.Absolute, out var tokenEndpoint) &&
                tokenEndpoint.Scheme == Uri.UriSchemeHttps &&
                !string.IsNullOrWhiteSpace(options.ClientId) &&
                !string.IsNullOrWhiteSpace(options.ClientSecretReference) &&
                options.RefreshBeforeExpiry >= TimeSpan.Zero &&
                options.RequestTimeout > TimeSpan.Zero &&
                options.Scopes is not null &&
                options.Scopes.All(scope =>
                    !string.IsNullOrWhiteSpace(scope) && !scope.Any(char.IsWhiteSpace)),
            _ => true
        };

        if (!valid)
        {
            throw new CallbackConfigurationException(
                $"Authentication configuration for {typeof(TOptions).Name} is invalid.");
        }
    }

    private static bool IsPermittedAuthenticationHeader(string headerName)
    {
        using var request = new HttpRequestMessage();
        try
        {
            CallbackHeaderPolicy.Apply(
                request,
                new Dictionary<string, string> { [headerName] = "value" });
            return true;
        }
        catch (CallbackConfigurationException)
        {
            return false;
        }
    }
}
