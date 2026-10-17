using Luft.Utility;

namespace Luft.Builder;

// The checker keeps generic arguments as names, so anything that is not a plain name is stored as its printed form; this reads it back
public static class TypeText
{
    public static AeroType? Parse(GenericParameterType argument)
    {
        var parsed = Parse(argument.Name);
        return parsed is null ? null : TypeSubstitution.WithFlags(parsed, argument.IsRef, argument.IsNullable);
    }

    public static AeroType? Parse(string text)
    {
        var at = 0;
        var type = ParseType(text, ref at);
        return type is not null && at == text.Length ? type : null;
    }

    public static GenericParameterType ToArgument(AeroType type)
        => type is ScalarType s ? new GenericParameterType(s.Name, null, s.IsRef, s.IsNullable) : new GenericParameterType(type.ToString()!);

    private static AeroType? ParseType(string text, ref int at)
    {
        var isRef = false;
        if (string.CompareOrdinal(text, at, "ref ", 0, 4) == 0)
        {
            isRef = true;
            at += 4;
        }

        var start = at;
        while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] is '_' or '.')) at++;
        if (at == start) return null;

        AeroType type = new ScalarType(text[start..at]);

        if (at < text.Length && text[at] == '<')
        {
            at++;
            var arguments = new List<GenericParameterType>();
            while (true)
            {
                var argument = ParseType(text, ref at);
                if (argument is null) return null;
                arguments.Add(ToArgument(argument));

                if (at < text.Length && text[at] == ',')
                {
                    at++;
                    while (at < text.Length && text[at] == ' ') at++;
                    continue;
                }
                break;
            }

            if (at >= text.Length || text[at] != '>') return null;
            at++;
            type = new GenericType(type, arguments.ToValueList());
        }

        // Nullable and array suffixes in any order, as ToString prints them
        while (at < text.Length)
        {
            if (text[at] == '?')
            {
                type = TypeSubstitution.WithFlags(type, false, true);
                at++;
            }
            else if (at + 1 < text.Length && text[at] == '[' && text[at + 1] == ']')
            {
                type = new ArrayType(type);
                at += 2;
            }
            else break;
        }

        return isRef ? TypeSubstitution.WithFlags(type, true, false) : type;
    }
}
