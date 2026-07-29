using System.Collections.Immutable;
using System.Text;
using RailSim.Core.World;

namespace RailSim.Core.Persistence;

public sealed class FileWorldChunkStore : IWorldChunkStore
{
    private const int Magic = 0x31534352;
    private const int MaximumHashBytes = 16 * 1024;

    private readonly string _rootDirectory;

    public FileWorldChunkStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public async ValueTask<WorldChunkSnapshot?> ReadAsync(
        WorldChunkId id,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new BinaryReader(
                stream,
                Encoding.UTF8,
                leaveOpen: true);
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.ReadInt32() != Magic)
            {
                throw new InvalidDataException(
                    "World chunk header is invalid.");
            }

            var schemaVersion = reader.ReadInt32();
            var storedId = new WorldChunkId(
                reader.ReadInt64(),
                reader.ReadInt64());
            if (storedId != id)
            {
                throw new InvalidDataException(
                    "World chunk ID does not match its storage key.");
            }

            var hashLength = reader.ReadInt32();
            if (hashLength <= 0 || hashLength > MaximumHashBytes)
            {
                throw new InvalidDataException(
                    "World chunk hash length is invalid.");
            }

            var hashBytes = reader.ReadBytes(hashLength);
            if (hashBytes.Length != hashLength)
            {
                throw new InvalidDataException(
                    "World chunk hash is truncated.");
            }

            var payloadLength = reader.ReadInt32();
            if (payloadLength < 0 ||
                payloadLength > stream.Length - stream.Position)
            {
                throw new InvalidDataException(
                    "World chunk payload length is invalid.");
            }

            var payload = new byte[payloadLength];
            await stream.ReadExactlyAsync(payload, cancellationToken);
            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException(
                    "World chunk contains trailing data.");
            }

            return new WorldChunkSnapshot(
                storedId,
                schemaVersion,
                Encoding.UTF8.GetString(hashBytes),
                payload.ToImmutableArray());
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException(
                "World chunk is truncated.",
                exception);
        }
    }

    public async ValueTask WriteAsync(
        WorldChunkSnapshot chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(chunk.Id);
        var temporaryPath =
            path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var hashBytes = Encoding.UTF8.GetBytes(chunk.ContentHash);
            if (hashBytes.Length > MaximumHashBytes)
            {
                throw new ArgumentException(
                    "World chunk content hash is too long.",
                    nameof(chunk));
            }

            await using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using (var writer = new BinaryWriter(
                           stream,
                           Encoding.UTF8,
                           leaveOpen: true))
                {
                    writer.Write(Magic);
                    writer.Write(chunk.SchemaVersion);
                    writer.Write(chunk.Id.X);
                    writer.Write(chunk.Id.Y);
                    writer.Write(hashBytes.Length);
                    writer.Write(hashBytes);
                    writer.Write(chunk.Payload.Length);
                    writer.Flush();
                }

                await stream.WriteAsync(
                    chunk.Payload.AsMemory(),
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetPath(WorldChunkId id)
    {
        return Path.Combine(
            _rootDirectory,
            $"{id.X}_{id.Y}.crsim-chunk");
    }
}
