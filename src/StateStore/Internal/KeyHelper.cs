namespace StateStore.Internal;

/// <summary>
/// Provides helper methods for key validation and derivation.
/// </summary>
internal static class KeyHelper
{
    /// <summary>
    /// Validates that the key is non-null, non-empty, and non-whitespace.
    /// </summary>
    /// <param name="key">The key to validate.</param>
    /// <exception cref="ArgumentException">Thrown when the key is null, empty, or whitespace.</exception>
    public static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            ArgumentNullException.ThrowIfNull(key, "State store key must not be null, empty, or whitespace.");
        }
    }

    /// <summary>
    /// Derives a deterministic key from a type for use with <see cref="Abstractions.ITypedStateStore{TState}"/>.
    /// </summary>
    /// <typeparam name="T">The type to derive the key from.</typeparam>
    /// <returns>A stable key string derived from the type.</returns>
    public static string DeriveKey<T>() => typeof(T).FullName ?? typeof(T).Name;
}
