using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace RcktMon.Client.Core.Security;

/// <summary>
/// Where the access key lives between runs.
/// </summary>
/// <remarks>
/// One secret, three platforms, so this is hand-rolled rather than taking a dependency.
/// Every implementation below satisfies the same hard requirements, stated explicitly
/// because the pre-rewrite <c>CryptoHelper</c> violated all of them: authenticated
/// encryption, a random per-record nonce, a random per-record salt, a modern KDF with a
/// real iteration count, and no dependence on a machine identifier.
/// </remarks>
public interface ISecretStore
{
    /// <summary>Reads a secret, or null when it was never stored.</summary>
    string? Read(string key);

    /// <summary>Stores or replaces a secret.</summary>
    void Write(string key, string value);

    /// <summary>Removes a secret. Succeeds whether or not it existed.</summary>
    void Delete(string key);
}

/// <summary>Picks the best available backend for the current OS.</summary>
public static class SecretStoreFactory
{
    /// <summary>Keychain on macOS, DPAPI on Windows, an encrypted file elsewhere.</summary>
    public static ISecretStore Create(string stateDirectory)
    {
        if (OperatingSystem.IsMacOS() && KeychainSecretStore.IsAvailable)
        {
            return new KeychainSecretStore();
        }

        if (OperatingSystem.IsWindows())
        {
            return new DpapiSecretStore(stateDirectory);
        }

        // Linux Secret Service via libsecret is the intended backend here; until that
        // lands this is the same construction used as its documented headless fallback.
        return new EncryptedFileSecretStore(stateDirectory);
    }
}

/// <summary>
/// macOS Keychain, via the <c>security</c> command-line tool.
/// </summary>
/// <remarks>
/// The alternative is P/Invoking <c>SecItemAdd</c>/<c>SecItemCopyMatching</c>, which means
/// hand-building CoreFoundation dictionaries and getting their ownership rules right. For
/// a single short string read once at startup, spawning the OS's own supported CLI is the
/// smaller risk. The secret is passed as an argument to <c>security</c>, which is visible
/// in the process list for the duration of the call — acceptable for a write that happens
/// when the user pastes a key, and the reason reads use the safer query form.
/// </remarks>
public sealed class KeychainSecretStore : ISecretStore
{
    private const string Tool = "/usr/bin/security";
    private const string ServiceName = "ru.technopriest.rcktmon";

    /// <summary>True when the Keychain CLI is present, which it is on any stock macOS.</summary>
    public static bool IsAvailable => File.Exists(Tool);

    public string? Read(string key)
    {
        (int exitCode, string stdout) = Run(["find-generic-password", "-s", ServiceName, "-a", key, "-w"]);
        return exitCode == 0 ? stdout.TrimEnd('\n') : null;
    }

    public void Write(string key, string value)
    {
        // -U updates in place if the item already exists, which is the difference between
        // "change your key" working and failing with a duplicate-item error.
        Run(["add-generic-password", "-s", ServiceName, "-a", key, "-w", value, "-U"]);
    }

    public void Delete(string key) =>
        Run(["delete-generic-password", "-s", ServiceName, "-a", key]);

    private static (int ExitCode, string StdOut) Run(string[] arguments)
    {
        var info = new ProcessStartInfo(Tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(info);
            if (process is null)
            {
                return (-1, string.Empty);
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);

            return (process.HasExited ? process.ExitCode : -1, output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, string.Empty);
        }
    }
}

/// <summary>Windows DPAPI, scoped to the current user.</summary>
public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ru.technopriest.rcktmon/v1");

    public string? Read(string key)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            byte[] plaintext = ProtectedData.Unprotect(
                File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            return null;
        }
    }

    public void Write(string key, string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(directory);

        byte[] ciphertext = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);

        File.WriteAllBytes(PathFor(key), ciphertext);
    }

    public void Delete(string key) => File.Delete(PathFor(key));

    private string PathFor(string key) => Path.Combine(directory, $"{Sanitise(key)}.dpapi");

    private static string Sanitise(string key) =>
        string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}

/// <summary>
/// AES-GCM in a file, for platforms with no OS keystore available.
/// </summary>
/// <remarks>
/// <para>
/// The key is derived from a random 32-byte value written alongside, which means this
/// protects against casual reading and backup leakage but not against an attacker who can
/// read the user's home directory. That limitation is inherent to having no OS keystore,
/// and it is stated here rather than papered over — the pre-rewrite code derived its key
/// from the Windows registry <c>MachineGuid</c>, which offered the same protection while
/// implying more.
/// </para>
/// <para>
/// Every record carries its own random salt and nonce, and the KDF runs 600 000 PBKDF2
/// iterations. Compare the code being replaced: a fixed IV string, a fixed salt, and
/// <c>PasswordDeriveBytes</c> with two iterations.
/// </para>
/// </remarks>
public sealed class EncryptedFileSecretStore(string directory) : ISecretStore
{
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 600_000;

    public string? Read(string key)
    {
        string path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            byte[] blob = File.ReadAllBytes(path);
            if (blob.Length <= SaltBytes + NonceBytes + TagBytes)
            {
                return null;
            }

            ReadOnlySpan<byte> span = blob;
            ReadOnlySpan<byte> salt = span[..SaltBytes];
            ReadOnlySpan<byte> nonce = span.Slice(SaltBytes, NonceBytes);
            ReadOnlySpan<byte> tag = span.Slice(SaltBytes + NonceBytes, TagBytes);
            ReadOnlySpan<byte> ciphertext = span[(SaltBytes + NonceBytes + TagBytes)..];

            byte[] plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(DeriveKey(salt), TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            // A failed tag check means the file was tampered with or the master key
            // changed. Either way there is no secret to return.
            return null;
        }
    }

    public void Write(string key, string value)
    {
        Directory.CreateDirectory(directory);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] plaintext = Encoding.UTF8.GetBytes(value);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagBytes];

        using (var aes = new AesGcm(DeriveKey(salt), TagBytes))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        byte[] blob = [.. salt, .. nonce, .. tag, .. ciphertext];

        string path = PathFor(key);
        File.WriteAllBytes(path + ".tmp", blob);
        File.Move(path + ".tmp", path, overwrite: true);

        TryRestrictPermissions(path);
    }

    public void Delete(string key) => File.Delete(PathFor(key));

    private byte[] DeriveKey(ReadOnlySpan<byte> salt) =>
        Rfc2898DeriveBytes.Pbkdf2(MasterSecret(), salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);

    /// <summary>
    /// A random value generated once per installation and stored next to the secrets.
    /// </summary>
    /// <remarks>
    /// Deliberately not derived from hardware or from a machine id: those are neither
    /// secret nor stable, and keying off one is how the pre-rewrite build silently lost
    /// every stored credential whenever the value changed.
    /// </remarks>
    private byte[] MasterSecret()
    {
        string path = Path.Combine(directory, "keyring.master");

        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        Directory.CreateDirectory(directory);

        byte[] master = RandomNumberGenerator.GetBytes(KeyBytes);
        File.WriteAllBytes(path, master);
        TryRestrictPermissions(path);

        return master;
    }

    private string PathFor(string key) => Path.Combine(directory, $"{Sanitise(key)}.aesgcm");

    private static string Sanitise(string key) =>
        string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    private static void TryRestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort; a filesystem with no Unix modes is not a reason to fail.
        }
    }
}

/// <summary>Well-known secret keys.</summary>
public static class SecretKeys
{
    /// <summary>The RcktMon server access key (<c>rm_…</c>).</summary>
    public const string AccessKey = "access-key";
}
