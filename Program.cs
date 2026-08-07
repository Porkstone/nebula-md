namespace NebulaMd;

static class Program
{
    private const string RegisterFileAssociationsOption = "--register-file-associations";
    private const string UnregisterFileAssociationsOption = "--unregister-file-associations";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && IsFileAssociationCommand(args[0]))
        {
            return RunFileAssociationCommand(args);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.FirstOrDefault()));
        return 0;
    }

    private static bool IsFileAssociationCommand(string argument)
    {
        return argument.Equals(RegisterFileAssociationsOption, StringComparison.OrdinalIgnoreCase)
            || argument.Equals(UnregisterFileAssociationsOption, StringComparison.OrdinalIgnoreCase);
    }

    private static int RunFileAssociationCommand(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine($"{args[0]} does not accept additional arguments.");
            return 2;
        }

        try
        {
            if (args[0].Equals(RegisterFileAssociationsOption, StringComparison.OrdinalIgnoreCase))
            {
                FileAssociationManager.Register(Environment.ProcessPath ?? Application.ExecutablePath);
                Console.WriteLine("nebula-md is registered for Markdown files.");
            }
            else
            {
                FileAssociationManager.Unregister();
                Console.WriteLine("nebula-md file integration has been removed.");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unable to update nebula-md file associations: {exception.Message}");
            return 1;
        }
    }
}
