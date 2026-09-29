namespace RelayHook.Core.Abstractions;

/// <summary>Resolves a secret reference immediately before delivery.</summary>
public interface ICallbackSecretProvider
{
    /// <summary>Gets a secret value or <see langword="null"/> when the reference does not exist.</summary>
    ValueTask<string?> GetSecretAsync(string secretReference, CancellationToken cancellationToken);
}
