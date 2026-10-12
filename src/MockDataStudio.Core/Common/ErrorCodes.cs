namespace MockDataStudio.Core.Common;

/// <summary>
/// Catálogo estável de códigos de erro e diagnósticos do Mock Data Studio.
/// Sincronizado com docs/25 §3.
/// </summary>
public static class ErrorCodes
{
    // Providers (PRV-)
    public const string PrvConnFailed = "PRV-CONN-FAILED";
    public const string PrvConnLost = "PRV-CONN-LOST";
    public const string PrvTimeout = "PRV-TIMEOUT";
    public const string PrvIntrospectPartial = "PRV-INTROSPECT-PARTIAL";
    public const string PrvSqliteNoFile = "PRV-SQLITE-NOFILE";
    public const string PrvUnsupportedVersion = "PRV-UNSUPPORTED-VERSION";
    public const string PrvCancelled = "PRV-CANCELLED";
    public const string PrvSqlSyntax = "PRV-SQL-SYNTAX";
    public const string PrvSqlError = "PRV-SQL-ERROR";

    // Schema Digest (DIG-)
    public const string DigJson = "DIG-JSON";
    public const string DigFormat = "DIG-FORMAT";
    public const string DigVersion = "DIG-VERSION";
    public const string DigMajorUnsupported = "DIG-MAJOR-UNSUPPORTED";
    public const string DigMinorNewer = "DIG-MINOR-NEWER";
    public const string DigSchema = "DIG-SCHEMA";
    public const string DigDup = "DIG-DUP";
    public const string DigPkCol = "DIG-PK-COL";
    public const string DigFkTarget = "DIG-FK-TARGET";
    public const string DigFkExternal = "DIG-FK-EXTERNAL";
    public const string DigFkArity = "DIG-FK-ARITY";
    public const string DigFlags = "DIG-FLAGS";
    public const string DigHashMismatch = "DIG-HASH-MISMATCH";

    // Catálogo interno (CAT-)
    public const string CatBuiltinReadOnly = "CAT-BUILTIN-READONLY";
    public const string CatMigChecksum = "CAT-MIG-CHECKSUM";
    public const string CatNewerSchema = "CAT-NEWER-SCHEMA";
    public const string CatStgForbidden = "CAT-STG-FORBIDDEN";
    public const string CatStgSqlError = "CAT-STG-SQLERROR";
    public const string CatImpCode = "CAT-IMP-CODE";
    public const string CatImpKind = "CAT-IMP-KIND";
    public const string CatImpUnknownTemplate = "CAT-IMP-UNKNOWN-TEMPLATE";
    public const string CatImpField = "CAT-IMP-FIELD";
    public const string CatImpRecordJson = "CAT-IMP-RECORD-JSON";
    public const string CatImpValueType = "CAT-IMP-VALUE-TYPE";
    public const string CatImpLength = "CAT-IMP-LENGTH";
    public const string CatImpDupValue = "CAT-IMP-DUPVALUE";
    public const string CatImpBuiltin = "CAT-IMP-BUILTIN";
    public const string CatImpWeight = "CAT-IMP-WEIGHT";
    public const string CatImpRule = "CAT-IMP-RULE";
    public const string CatImpSize = "CAT-IMP-SIZE";
    public const string CatMigFailed = "CAT-MIG-FAILED";
    public const string CatStgSession = "CAT-STG-SESSION";
    public const string CatProfileSecret = "CAT-PROFILE-SECRET";
    public const string CatBackupFailed = "CAT-BACKUP-FAILED";

    // Mapeamento semântico (MAP-)
    public const string MapUnmapped = "MAP-UNMAPPED";
    public const string MapAmbiguous = "MAP-AMBIGUOUS";
    public const string MapLowConf = "MAP-LOWCONF";
    public const string MapType = "MAP-TYPE";
    public const string MapOrphan = "MAP-ORPHAN";
    public const string MapReview = "MAP-REVIEW";

    // Geração (GEN-)
    public const string GenParamRequired = "GEN-PARAM-REQUIRED";
    public const string GenParamRange = "GEN-PARAM-RANGE";
    public const string GenParamType = "GEN-PARAM-TYPE";
    public const string GenCoerce = "GEN-COERCE";
    public const string GenUniqueExhausted = "GEN-UNIQUE-EXHAUSTED";
    public const string GenExprSyntax = "GEN-EXPR-SYNTAX";
    public const string GenExprType = "GEN-EXPR-TYPE";
    public const string GenExprUnknownFn = "GEN-EXPR-UNKNOWN-FN";
    public const string GenUqUniform = "GEN-UQ-UNIFORM";
    public const string GenParamValue = "GEN-PARAM-VALUE";
    public const string GenUnknownGenerator = "GEN-UNKNOWN-GENERATOR";
    public const string GenUnsupported = "GEN-UNSUPPORTED";
    public const string GenExprLimit = "GEN-EXPR-LIMIT";
    public const string GenExprColumn = "GEN-EXPR-COLUMN";
    public const string GenExprCycle = "GEN-EXPR-CYCLE";

    // Planejamento relacional (PLN-)
    public const string PlnFkParentExcluded = "PLN-FK-PARENT-EXCLUDED";
    public const string PlnCycleUnbreakable = "PLN-CYCLE-UNBREAKABLE";
    public const string PlnCycleNullUpdate = "PLN-CYCLE-NULLUPDATE";
    public const string PlnFkChecksOff = "PLN-FKCHECKS-OFF";
    public const string PlnSelfNotNull = "PLN-SELF-NOTNULL";
    public const string PlnSelfCaptured = "PLN-SELF-CAPTURED";
    public const string PlnKeyAlwaysScript = "PLN-KEY-ALWAYS-SCRIPT";
    public const string PlnFbTriggerKey = "PLN-FB-TRIGGER-KEY";
    public const string PlnKeyOverflow = "PLN-KEY-OVERFLOW";
    public const string PlnKeyOffline = "PLN-KEY-OFFLINE";
    public const string PlnUniqueCapacity = "PLN-UNIQUE-CAPACITY";
    public const string PlnUniqueExisting = "PLN-UNIQUE-EXISTING";
    public const string PlnOneToOneCapacity = "PLN-ONE-TO-ONE-CAPACITY";
    public const string PlnParentDrivenMulti = "PLN-PARENTDRIVEN-MULTI";
    public const string PlnFkNonPureParent = "PLN-FK-NONPURE-PARENT";
    public const string PlnFkOverlap = "PLN-FK-OVERLAP";
    public const string PlnRefEmpty = "PLN-REF-EMPTY";
    public const string PlnExprCycle = "PLN-EXPR-CYCLE";
    public const string PlnExprParent = "PLN-EXPR-PARENT";
    public const string PlnBindingReview = "PLN-BINDING-REVIEW";
    public const string PlnNoRows = "PLN-NO-ROWS";

    // Validação estática / pré-voo (VAL-)
    public const string ValNullNotNull = "VAL-NULL-NOTNULL";
    public const string ValUnbound = "VAL-UNBOUND";
    public const string ValType = "VAL-TYPE";
    public const string ValLen = "VAL-LEN";
    public const string ValLenBytes = "VAL-LEN-BYTES";
    public const string ValPrecision = "VAL-PRECISION";
    public const string ValIntRange = "VAL-INT-RANGE";
    public const string ValDateRange = "VAL-DATE-RANGE";
    public const string ValEnum = "VAL-ENUM";
    public const string ValCheck = "VAL-CHECK";
    public const string ValCheckUnverified = "VAL-CHECK-UNVERIFIED";
    public const string ValUqPartial = "VAL-UQ-PARTIAL";
    public const string ValTriggerInsert = "VAL-TRIGGER-INSERT";
    public const string ValRefMissingInTarget = "VAL-REF-MISSING-IN-TARGET";
    public const string ValGeneratedBound = "VAL-GENERATED-BOUND";
    public const string ValDefaultNullRate = "VAL-DEFAULT-NULLRATE";
    public const string ValSqliteDateStorage = "VAL-SQLITE-DATESTORAGE";
    public const string ValMySqlNonStrict = "VAL-MYSQL-NONSTRICT";
    public const string ValRepro = "VAL-REPRO";
    public const string ValProdTarget = "VAL-PROD-TARGET";

    // Saída (OUT-)
    public const string OutIo = "OUT-IO";
    public const string OutSegmentOversize = "OUT-SEGMENT-OVERSIZE";
    public const string OutDiskFull = "OUT-DISK-FULL";

    // Execução (RUN-)
    public const string RunKeyRangeTaken = "RUN-KEY-RANGE-TAKEN";
    public const string RunBatchFailed = "RUN-BATCH-FAILED";
    public const string RunInterrupted = "RUN-INTERRUPTED";

    // Editor SQL (SQL-)
    public const string SqlSplit = "SQL-SPLIT";
    public const string SqlBlockedReadOnly = "SQL-BLOCKED-READONLY";
    public const string SqlCancelled = "SQL-CANCELLED";
    public const string SqlDisconnected = "SQL-DISCONNECTED";

    // Projetos (PRJ-)
    public const string PrjJson = "PRJ-JSON";
    public const string PrjSchema = "PRJ-SCHEMA";
    public const string PrjDigest = "PRJ-DIGEST";
    public const string PrjBindingColumn = "PRJ-BINDING-COLUMN";
    public const string PrjGenerator = "PRJ-GENERATOR";
    public const string PrjParams = "PRJ-PARAMS";
    public const string PrjQuery = "PRJ-QUERY";
    public const string PrjCatalog = "PRJ-CATALOG";
    public const string PrjMajorUnsupported = "PRJ-MAJOR-UNSUPPORTED";

    // Segurança (SEC-)
    public const string SecNoAuthorization = "SEC-NO-AUTHORIZATION";
    public const string SecAuthExpired = "SEC-AUTH-EXPIRED";
    public const string SecFingerprintMismatch = "SEC-FINGERPRINT-MISMATCH";
    public const string SecTrialProd = "SEC-TRIAL-PROD";
    public const string SecUntrustedProject = "SEC-UNTRUSTED-PROJECT";
}
