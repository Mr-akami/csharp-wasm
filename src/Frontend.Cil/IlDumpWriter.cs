using System.Globalization;
using System.Text;

namespace CsWasm.Frontend.Cil;

/// <summary>
/// Renders <see cref="AssemblyModel"/> as the text <c>cswasm dump il</c> prints.
/// </summary>
/// <remarks>
/// The output is a fixed function of the file: line separators are "\n" on every platform,
/// numbers are formatted with the invariant culture, and members appear in metadata table
/// order rather than sorted. Nothing that varies between machines or runs - a path, a
/// timestamp, a version - is printed, so two dumps of the same assembly compare equal.
/// </remarks>
public static class IlDumpWriter
{
    private const char LineFeed = '\n';

    public static string Write(AssemblyModel assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var text = new StringBuilder();
        text.Append("assembly ").Append(assembly.Name).Append(LineFeed);

        foreach (var type in assembly.Types)
        {
            text.Append("type ").Append(type.FullName).Append(LineFeed);

            if (type.BaseTypeName is not null)
            {
                text.Append("  extends ").Append(type.BaseTypeName).Append(LineFeed);
            }

            foreach (var field in type.Fields)
            {
                text.Append("  field ").Append(field.TypeName).Append(' ').Append(field.Name).Append(LineFeed);
            }

            foreach (var method in type.Methods)
            {
                AppendMethod(text, method);
            }
        }

        return text.ToString();
    }

    private static void AppendMethod(StringBuilder text, MethodModel method)
    {
        text.Append("  method ")
            .Append(method.IsStatic ? "static" : "instance")
            .Append(' ')
            .Append(method.ReturnTypeName)
            .Append(' ')
            .Append(method.Name)
            .Append('(')
            .Append(string.Join(", ", method.ParameterTypeNames))
            .Append(')')
            .Append(LineFeed);

        if (method.Body is null)
        {
            text.Append("    no body").Append(LineFeed);
            return;
        }

        text.Append("    maxstack ")
            .Append(method.Body.MaxStack.ToString(CultureInfo.InvariantCulture))
            .Append(LineFeed);

        if (method.Body.Locals.Count > 0)
        {
            text.Append("    locals init (")
                .Append(string.Join(", ", method.Body.Locals.Select(local => local.TypeName)))
                .Append(')')
                .Append(LineFeed);
        }

        foreach (var region in method.Body.ExceptionRegions)
        {
            AppendExceptionRegion(text, region);
        }

        foreach (var instruction in method.Body.Instructions)
        {
            text.Append("    ")
                .Append(MethodBodyDecoder.Label(instruction.Offset))
                .Append(": ")
                .Append(instruction.OpCodeName);

            if (instruction.Operand is not null)
            {
                text.Append(' ').Append(instruction.Operand);
            }

            text.Append(LineFeed);
        }
    }

    private static void AppendExceptionRegion(StringBuilder text, ExceptionRegionModel region)
    {
        var handler = region.Kind switch
        {
            ExceptionRegionKind.Catch => "catch " + region.CatchTypeName,
            ExceptionRegionKind.Filter => "filter",
            ExceptionRegionKind.Finally => "finally",
            _ => "fault",
        };

        text.Append("    .try ")
            .Append(MethodBodyDecoder.Label(region.TryOffset))
            .Append("..")
            .Append(MethodBodyDecoder.Label(region.TryOffset + region.TryLength))
            .Append(' ')
            .Append(handler)
            .Append(" handler ")
            .Append(MethodBodyDecoder.Label(region.HandlerOffset))
            .Append("..")
            .Append(MethodBodyDecoder.Label(region.HandlerOffset + region.HandlerLength))
            .Append(LineFeed);
    }
}
