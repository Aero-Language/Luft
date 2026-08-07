using System.Collections.Generic;
using Luft;

namespace LuftCli;

public static class FlagExt
{
    private static readonly Dictionary<string, FlagType> Flags = new()
    {
        { "build", FlagType.Build },
        { "run", FlagType.Run }
    };
    
    public static FlagType GetFlagType(this string flag)
    {
        return Flags.GetValueOrDefault(flag, FlagType.Unknown);
    }
}