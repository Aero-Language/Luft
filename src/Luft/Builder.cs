using Luft.Lexer;

namespace Luft;

public class Builder
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
            var lexer = new Lexer.Lexer();
            
            var tokens = lexer.Tokenize(file);
        }
        
        
        return string.Empty;
    }
    
    public async Task<string> BuildAsync(string[] files)
    {
        foreach (string file in files)
        {
            var lexer = new Lexer.Lexer();
            
            var tokens = lexer.Tokenize(file);
        }
        
        return string.Empty;
    }
}