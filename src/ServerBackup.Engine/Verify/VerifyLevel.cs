namespace ServerBackup.Engine.Verify;

public enum VerifyLevel
{
    /// <summary>Catalog self-consistency: packs referenced by rows actually exist, no dangling references.</summary>
    Index,

    /// <summary>Index checks plus: every pack file's SHA-256 matches what the catalog recorded.</summary>
    Packs,

    /// <summary>Packs checks plus: every blob decrypts, decompresses, and its content-addressed id matches what's stored.</summary>
    Full,
}
