using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Core.Hashing;

public interface IStructureHasher
{
    // SHA-256 (hex minúsculo, prefixo "sha256:") da forma canônica (12 §6).
    string Compute(SchemaModel model);

    string ComputeTable(ObjectName name, TableModel table);

    // 16 caracteres hexadecimais (primeiros 8 bytes do SHA-256 da impressão digital).
    string ComputeColumnFingerprint(TableModel table, ColumnModel column);
}
