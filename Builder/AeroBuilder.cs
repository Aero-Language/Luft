using static Luft.Parser.Lexer.Lexer;
using static Luft.Parser.AstBuilder.AstBuilder;

namespace Luft.Builder;

public class AeroBuilder
{
    /// <summary>
    /// Builds the <paramref name="files"/> as a module
    /// </summary>
    /// <param name="files">An <typeparamref name="Array"/> of strings that contain all the file paths of a project</param>
    /// <returns>The absolute path of the final executable</returns>
    public string Build(string[] files)
    {
        foreach (string file in files)
        {
            var tokens = Tokenize(file); // Turns the string of code into tokens
            var ast = BuildAst(tokens); // Turns the tokens into a tree of nodes
        }
        
        return string.Empty;
    }
}