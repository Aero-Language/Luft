using Luft;
using Luft.AstBuilder.Ast;

namespace LuftCli;

public static class Program
{
    static void Main()
    {
        string[] args = [ "build", "test.aero" ];
        
        if (args.Length <= 0)
        {
            Console.WriteLine("No options specified, quitting...");
            return;
        }
        
        int cursor = 0;
        FlagType[] flags = args.Select(str => str.GetFlagType()).ToArray();

        try
        {
            while (flags.Length > cursor)
            {
                switch (flags[cursor])
                {
                    case FlagType.Build:
                        cursor++;
                        
                        if (flags.Length <= cursor) throw new InvalidOperationException("No files specified.");
                        if (flags[cursor] == FlagType.Unknown)
                        {
                            List<string> files = [];
                            
                            while (flags[cursor] == FlagType.Unknown)
                            {
                                var filePath = args[cursor];
                                if (!File.Exists(filePath)) throw new FileNotFoundException($"'{filePath}' was either not a file, or the file does not exist.");
                                
                                cursor++;
                                files.Add(filePath);
                                
                                if (flags.Length <= cursor) break;
                            }

                            var builder = new Builder();
                            builder.Build(files.ToArray());
                        }
                        else throw new InvalidOperationException("No files specified.");
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
}