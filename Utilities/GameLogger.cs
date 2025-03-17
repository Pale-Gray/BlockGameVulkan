namespace Game.Core.Utilities;

public enum SeverityType : byte
{

    Info,
    Warning,
    Error

}
public class GameLogger
{

    public static void Log(object? message, SeverityType severity = SeverityType.Info)
    {

        string prefix = "[Info]";
        switch (severity)
        {
            case SeverityType.Info:
                Console.ForegroundColor = ConsoleColor.Green;
                prefix = "[Info]";
                break;
            case SeverityType.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                prefix = "[Warning]";
                break;
            case SeverityType.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                prefix = "[Error]";
                break;
        }

        Console.WriteLine($"{prefix} {message?.ToString()}");
        Console.ResetColor();

    }

    public static void Throw(object? message)
    {

        Console.ForegroundColor = ConsoleColor.Red;
        throw new Exception(message?.ToString());

    }

}