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
    /// <exception cref="ArgumentNullException">Thrown when the key is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the key is empty or whitespace.</exception>
    public static void ValidateKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("State store key must not be empty or whitespace.", nameof(key));
        }
    }

    /// <summary>
    /// Derives a deterministic key from a type for use with <see cref="Abstractions.ITypedStateStore{TState}"/>.
    /// </summary>
    /// <typeparam name="T">The type to derive the key from.</typeparam>
    /// <returns>A stable key string derived from the type.</returns>
    public static string DeriveKey<T>() => typeof(T).FullName ?? typeof(T).Name;
}
