using System.Text.Json;

namespace RcktMon.Client.Core.Trading;

public enum TradeVenue { Walt, Finam }

/// <summary>Only a local reference; never store broker secrets, OAuth state or an execution token.</summary>
public sealed record PendingSubmission(string Reference, DateTimeOffset CreatedUtc, string? Description = null);

/// <summary>
/// Write ahead of the one provider call, holding an exclusive OS file handle until the caller leaves.
/// A lost ACK, process death or unreadable file must not turn into permission to submit again.
/// </summary>
public sealed class SubmissionJournal(string directory, TradeVenue venue)
{
    private string Marker => Path.Combine(directory, $"{venue}.pending.json");
    public PendingSubmission? Read()
    {
        try
        {
            var record = JsonSerializer.Deserialize<PendingSubmission>(File.ReadAllText(Marker));
            return record is { Reference.Length: > 0 } ? record : throw new InvalidDataException();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public Lease Begin(string reference, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException(null, nameof(reference));
        var handle = Lock();
        try
        {
            // CreateNew is a separate guard even on filesystems without advisory lock support.
            using var stream = new FileStream(Marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, new PendingSubmission(reference, DateTimeOffset.UtcNow, description));
            stream.Flush(flushToDisk: true);
            return new Lease(handle, Marker);
        }
        catch { handle.Dispose(); throw; }
    }

    public bool AcknowledgeCheckedReference(string reference)
    {
        using var handle = Lock();
        if (Read()?.Reference != reference) return false;
        File.Delete(Marker);
        return true;
    }

    private FileStream Lock()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new FileStream(Marker + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public sealed class Lease : IDisposable
    {
        private FileStream? _handle;
        private readonly string _path;
        internal Lease(FileStream handle, string path) { _handle = handle; _path = path; }
        public void MarkDefinitive()
        {
            ObjectDisposedException.ThrowIf(_handle is null, this);
            File.Delete(_path);
        }
        public void Dispose() { _handle?.Dispose(); _handle = null; }
    }
}
