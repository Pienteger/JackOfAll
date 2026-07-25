namespace JackOfAll.Lib.Tools;

public static class EnvironmentContext
{
    public static AppEnvironment? Current { get; private set; }

    public static event Action? Changed;

    public static void Set(AppEnvironment? environment)
    {
        Current = environment;
        Changed?.Invoke();
    }
}
