using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace MockDataStudio.Architecture.Tests;

public sealed class ArchitectureTests
{
    private static readonly string[] ProductAssemblyNames =
    [
        "MockDataStudio.Core",
        "MockDataStudio.Providers.Abstractions",
        "MockDataStudio.Providers.Sqlite",
        "MockDataStudio.Providers.MySql",
        "MockDataStudio.Providers.PostgreSql",
        "MockDataStudio.Providers.Firebird",
        "MockDataStudio.Catalog",
        "MockDataStudio.Mapping",
        "MockDataStudio.Generation",
        "MockDataStudio.Output",
        "MockDataStudio.SqlTooling",
        "MockDataStudio.Projects",
        "MockDataStudio.Application",
        "MockDataStudio.Infrastructure",
        "MockDataStudio.App"
    ];

    [Fact]
    [Trait("Category", "Architecture")]
    [Trait("Id", "T-ARC-001")]
    public void T_ARC_001_Core_MustNotReferenceOtherProductAssemblies_OrDatabaseOrUiPackages()
    {
        var coreAssembly = Assembly.Load(new AssemblyName("MockDataStudio.Core"));
        var referenced = coreAssembly.GetReferencedAssemblies();

        var productRefs = referenced
            .Where(r => r.Name != null && r.Name.StartsWith("MockDataStudio.", StringComparison.OrdinalIgnoreCase))
            .ToList();
        productRefs.ShouldBeEmpty("MockDataStudio.Core não pode referenciar nenhum outro assembly de produto.");

        var forbidden = new[]
        {
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
            "ICSharpCode.AvalonEdit",
            "Microsoft.Data.Sqlite",
            "MySqlConnector",
            "Npgsql",
            "FirebirdSql.Data.FirebirdClient"
        };

        var forbiddenRefs = referenced
            .Where(r => r.Name != null && forbidden.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        forbiddenRefs.ShouldBeEmpty("MockDataStudio.Core não pode referenciar bibliotecas de UI ou drivers de banco.");
    }

    [Theory]
    [InlineData("MockDataStudio.Generation")]
    [InlineData("MockDataStudio.Mapping")]
    [InlineData("MockDataStudio.SqlTooling")]
    [InlineData("MockDataStudio.Projects")]
    [Trait("Category", "Architecture")]
    [Trait("Id", "T-ARC-002")]
    public void T_ARC_002_DomainAssemblies_MustOnlyReferenceCore_AmongProductAssemblies(string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));
        var productRefs = assembly.GetReferencedAssemblies()
            .Where(r => r.Name != null && r.Name.StartsWith("MockDataStudio.", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Name!)
            .ToList();

        var invalidRefs = productRefs
            .Where(r => !string.Equals(r, "MockDataStudio.Core", StringComparison.OrdinalIgnoreCase))
            .ToList();

        invalidRefs.ShouldBeEmpty($"{assemblyName} só pode referenciar MockDataStudio.Core entre os assemblies do produto, mas referencia: {string.Join(", ", invalidRefs)}");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    [Trait("Id", "T-ARC-003")]
    public void T_ARC_003_OnlyApp_CanReferenceWpfAndAvalonEdit()
    {
        var wpfAssemblies = new[]
        {
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
            "ICSharpCode.AvalonEdit"
        };

        var invalidAssemblies = new List<string>();

        foreach (var name in ProductAssemblyNames)
        {
            if (string.Equals(name, "MockDataStudio.App", StringComparison.OrdinalIgnoreCase))
                continue;

            var assembly = Assembly.Load(new AssemblyName(name));
            var references = assembly.GetReferencedAssemblies()
                .Where(r => r.Name != null && wpfAssemblies.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
                .Select(r => r.Name!)
                .ToList();

            if (references.Count > 0)
            {
                invalidAssemblies.Add($"{name} referencia: {string.Join(", ", references)}");
            }
        }

        invalidAssemblies.ShouldBeEmpty("Somente MockDataStudio.App pode referenciar WPF e AvalonEdit.");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    [Trait("Id", "T-ARC-004")]
    public void T_ARC_004_DatabaseDrivers_OnlyReferencedByTheirRespectiveProviders()
    {
        var driverMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.Data.Sqlite"] = ["MockDataStudio.Providers.Sqlite", "MockDataStudio.Catalog"],
            ["MySqlConnector"] = ["MockDataStudio.Providers.MySql"],
            ["Npgsql"] = ["MockDataStudio.Providers.PostgreSql"],
            ["FirebirdSql.Data.FirebirdClient"] = ["MockDataStudio.Providers.Firebird"]
        };

        var violations = new List<string>();

        foreach (var (driver, allowedAssemblies) in driverMap)
        {
            foreach (var productName in ProductAssemblyNames)
            {
                if (allowedAssemblies.Contains(productName, StringComparer.OrdinalIgnoreCase))
                    continue;

                var assembly = Assembly.Load(new AssemblyName(productName));
                var references = assembly.GetReferencedAssemblies();
                if (references.Any(r => string.Equals(r.Name, driver, StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add($"{productName} referencia indevidamente o driver {driver}");
                }
            }
        }

        violations.ShouldBeEmpty("Drivers ADO.NET só podem ser referenciados pelos respectivos providers (e Catalog para SQLite).");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    [Trait("Id", "T-ARC-005")]
    public void T_ARC_005_App_OnlyReferencesProvidersInCompositionRoot()
    {
        var appAssembly = Assembly.Load(new AssemblyName("MockDataStudio.App"));
        var providerNamespaces = new[]
        {
            "MockDataStudio.Providers.Sqlite",
            "MockDataStudio.Providers.MySql",
            "MockDataStudio.Providers.PostgreSql",
            "MockDataStudio.Providers.Firebird"
        };

        var violations = new List<string>();

        foreach (var type in appAssembly.GetTypes())
        {
            if (type.Namespace != null && type.Namespace.StartsWith("MockDataStudio.App.Composition", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var usedTypes = new HashSet<Type>();
            if (type.BaseType != null) usedTypes.Add(type.BaseType);
            foreach (var iface in type.GetInterfaces()) usedTypes.Add(iface);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                usedTypes.Add(field.FieldType);
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                usedTypes.Add(prop.PropertyType);
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var p in ctor.GetParameters()) usedTypes.Add(p.ParameterType);
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                usedTypes.Add(method.ReturnType);
                foreach (var p in method.GetParameters()) usedTypes.Add(p.ParameterType);
            }

            foreach (var t in usedTypes)
            {
                if (t.Namespace != null && providerNamespaces.Any(pns => t.Namespace.StartsWith(pns, StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add($"Tipo '{type.FullName}' usa '{t.FullName}' fora do Composition Root.");
                }
            }
        }

        violations.ShouldBeEmpty("MockDataStudio.App só pode referenciar tipos de Providers no namespace MockDataStudio.App.Composition.");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    [Trait("Id", "T-ARC-006")]
    public void T_ARC_006_Core_PublicModelTypes_MustBeImmutable()
    {
        var coreAssembly = Assembly.Load(new AssemblyName("MockDataStudio.Core"));
        var violations = new List<string>();

        foreach (var type in coreAssembly.GetExportedTypes())
        {
            if (type.IsEnum || type.IsInterface)
                continue;

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                var setter = prop.SetMethod;
                if (setter != null && setter.IsPublic)
                {
                    var hasIsExternalInit = setter.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

                    if (!hasIsExternalInit)
                    {
                        violations.Add($"{type.FullName}.{prop.Name} possui setter público mutável (deve ser init-only ou get-only).");
                    }
                }
            }
        }

        violations.ShouldBeEmpty("Todos os tipos públicos em MockDataStudio.Core devem ser imutáveis.");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    [Trait("Id", "T-ARC-007")]
    public void T_ARC_007_NoSwitchOrConditionOnDatabaseEngine_OutsideProvidersAndAppComposition()
    {
        var repoRoot = FindSolutionRoot();
        var srcDir = Path.Combine(repoRoot, "src");

        var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
        var violations = new List<string>();

        var branchingPatterns = new[]
        {
            new Regex(@"switch\s*\([^)]*DatabaseEngine", RegexOptions.Compiled),
            new Regex(@"case\s+DatabaseEngine\.", RegexOptions.Compiled),
            new Regex(@"==\s*DatabaseEngine\.", RegexOptions.Compiled),
            new Regex(@"!=\s*DatabaseEngine\.", RegexOptions.Compiled),
            new Regex(@"DatabaseEngine\.\w+\s*==", RegexOptions.Compiled),
            new Regex(@"DatabaseEngine\.\w+\s*!=", RegexOptions.Compiled),
            new Regex(@"is\s+DatabaseEngine\.", RegexOptions.Compiled)
        };

        foreach (var file in csFiles)
        {
            var normalizedPath = file.Replace('\\', '/');

            // Permitted locations: Providers and App/Composition
            if (normalizedPath.Contains("/MockDataStudio.Providers.", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Contains("/MockDataStudio.App/Composition/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                foreach (var pattern in branchingPatterns)
                {
                    if (pattern.IsMatch(line))
                    {
                        violations.Add($"{Path.GetRelativePath(repoRoot, file)}:{i + 1} - '{line}'");
                        break;
                    }
                }
            }
        }

        violations.ShouldBeEmpty("Nenhum switch ou branch condicional sobre DatabaseEngine deve existir fora de Providers.* e App/Composition.");
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "MockDataStudio.slnx")) ||
                File.Exists(Path.Combine(dir, "MockDataStudio.sln")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        throw new InvalidOperationException("Não foi possível localizar a raiz da solution MockDataStudio.");
    }
}
