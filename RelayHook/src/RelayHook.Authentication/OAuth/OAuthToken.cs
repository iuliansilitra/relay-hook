namespace RelayHook.Authentication.OAuth;

internal sealed record OAuthToken(string AccessToken, DateTimeOffset ExpiresAt);
