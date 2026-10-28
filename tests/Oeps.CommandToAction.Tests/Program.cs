using System.Reflection;

namespace Oeps.CommandToAction.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

public static class Assert
{
    public static void True(bool actual, string? message = null)
    {
        if (!actual) throw new Exception(message ?? "Expected true.");
    }
    public static void False(bool actual, string? message = null) => True(!actual, message ?? "Expected false.");
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected <{expected}>, got <{actual}>.");
    }
    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T exception) { return exception; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T exception) { return exception; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}

internal static class Program
{
    public static async Task<int> Main()
    {
        int passed = 0, failed = 0;
        foreach (var method in Assembly.GetExecutingAssembly().GetTypes()
                     .SelectMany(type => type.GetMethods())
                     .Where(method => method.GetCustomAttribute<TestAttribute>() is not null)
                     .OrderBy(method => method.DeclaringType!.Name).ThenBy(method => method.Name))
        {
            try
            {
                if (method.Invoke(null, null) is Task task) await task.WaitAsync(TimeSpan.FromSeconds(15));
                Console.WriteLine($"PASS {method.DeclaringType!.Name}.{method.Name}");
                passed++;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL {method.DeclaringType!.Name}.{method.Name}: {(exception is TargetInvocationException ? exception.InnerException : exception)}");
                failed++;
            }
        }
        Console.WriteLine($"{passed} passed; {failed} failed.");
        return failed == 0 && passed > 0 ? 0 : 1;
    }
}
