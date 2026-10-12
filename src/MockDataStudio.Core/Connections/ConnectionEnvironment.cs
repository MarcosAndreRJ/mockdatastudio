namespace MockDataStudio.Core.Connections;

// Ambiente do perfil de conexão (23 §5.1). "Unknown" é o padrão de perfil novo e é tratado como Production
// nas proteções (DA-006).
public enum ConnectionEnvironment
{
    Development,
    Test,
    Staging,
    Production,
    Unknown,
}
