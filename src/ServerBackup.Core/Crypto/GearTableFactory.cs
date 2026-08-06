using System.Buffers.Binary;

namespace ServerBackup.Core.Crypto;

/// <summary>
/// Derives the 256-entry Gear hash table used by the FastCDC chunker from the
/// repository master key. Seeding the table from the key (instead of using a
/// fixed public table) means chunk boundaries cannot be predicted or
/// fingerprinted by anyone without repository access.
/// </summary>
public static class GearTableFactory
{
    public const int EntryCount = 256;

    public static ulong[] Derive(ReadOnlySpan<byte> masterKey)
    {
        var raw = SubKeys.Derive(masterKey, SubKeys.GearSeedInfo, EntryCount * sizeof(ulong));
        var table = new ulong[EntryCount];
        for (var i = 0; i < EntryCount; i++)
        {
            table[i] = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(i * sizeof(ulong)));
        }

        return table;
    }
}
