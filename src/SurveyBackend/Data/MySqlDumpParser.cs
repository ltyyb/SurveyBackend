using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SurveyBackend.Data;

internal static class MySqlDumpParser
{
    internal sealed record Table(string Name, string[] Columns, long? NextId);
    internal sealed record Row(string Table, string[] Columns, string?[] Values);
    internal sealed record Dump(List<Table> Tables, List<Row> Rows);

    public static Dump Parse(string sql)
    {
        var tables = new List<Table>();
        var rows = new List<Row>();
        foreach (var statement in SplitStatements(sql))
        {
            if (Regex.IsMatch(statement, @"\ASET FOREIGN_KEY_CHECKS\s*=\s*[01]\z", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var create = Regex.Match(statement, @"\ACREATE TABLE `(?<table>\w+)`\s*\((?<definition>.*)\)\s*ENGINE=InnoDB(?<options>.*)\z", RegexOptions.Singleline);
            if (create.Success)
            {
                var columns = Regex.Matches(create.Groups["definition"].Value, @"^\s*`(?<column>\w+)`\s+", RegexOptions.Multiline)
                    .Select(match => match.Groups["column"].Value).ToArray();
                var nextId = Regex.Match(create.Groups["options"].Value, @"\bAUTO_INCREMENT=(\d+)\b");
                tables.Add(new Table(create.Groups["table"].Value, columns,
                    nextId.Success ? long.Parse(nextId.Groups[1].Value, CultureInfo.InvariantCulture) : null));
                continue;
            }

            var insert = Regex.Match(statement, @"\AINSERT INTO `(?<table>\w+)`\s*\((?<columns>`\w+`(?:\s*,\s*`\w+`)*)\)\s*VALUES\s*(?<values>.*)\z", RegexOptions.Singleline);
            if (!insert.Success)
            {
                throw new InvalidDataException("Unsupported dump statement. Expected explicit CREATE TABLE / INSERT INTO statements.");
            }

            var table = insert.Groups["table"].Value;
            var names = Regex.Matches(insert.Groups["columns"].Value, @"`(\w+)`")
                .Select(match => match.Groups[1].Value).ToArray();
            var valuesText = insert.Groups["values"].Value;
            var offset = 0;
            do
            {
                Expect(valuesText, ref offset, '(');
                var values = new List<string?>();
                do
                {
                    values.Add(ReadValue(valuesText, ref offset));
                    SkipSpace(valuesText, ref offset);
                    if (offset < valuesText.Length && valuesText[offset] == ',')
                    {
                        offset++;
                    }
                    else
                    {
                        break;
                    }
                } while (true);
                Expect(valuesText, ref offset, ')');
                if (values.Count != names.Length)
                {
                    throw new InvalidDataException("INSERT column/value count differs.");
                }
                rows.Add(new Row(table, names, values.ToArray()));
                SkipSpace(valuesText, ref offset);
                if (offset == valuesText.Length)
                {
                    break;
                }
                Expect(valuesText, ref offset, ',');
            } while (true);
        }

        return new Dump(tables, rows);
    }

    private static IEnumerable<string> SplitStatements(string sql)
    {
        var start = 0;
        var quoted = false;
        for (var index = 0; index < sql.Length; index++)
        {
            var character = sql[index];
            if (quoted && character == '\\')
            {
                index++;
            }
            else if (character == '\'')
            {
                if (quoted && index + 1 < sql.Length && sql[index + 1] == '\'')
                {
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (!quoted && character == ';')
            {
                var statement = sql[start..index].Trim();
                if (statement.Length > 0)
                {
                    yield return statement;
                }
                start = index + 1;
            }
        }

        if (quoted || !string.IsNullOrWhiteSpace(sql[start..]))
        {
            throw new InvalidDataException("Truncated or unterminated dump.");
        }
    }

    private static string? ReadValue(string text, ref int offset)
    {
        SkipSpace(text, ref offset);
        if (offset >= text.Length)
        {
            throw new InvalidDataException("Missing SQL literal.");
        }
        if (text[offset] != '\'')
        {
            var start = offset;
            while (offset < text.Length && text[offset] is not (',' or ')'))
            {
                offset++;
            }
            var literal = text[start..offset].Trim();
            if (literal.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (!Regex.IsMatch(literal, @"\A-?\d+\z"))
            {
                throw new InvalidDataException("Unsupported SQL literal.");
            }
            return literal;
        }

        offset++;
        var value = new StringBuilder();
        while (offset < text.Length)
        {
            var character = text[offset++];
            if (character == '\'')
            {
                if (offset < text.Length && text[offset] == '\'')
                {
                    value.Append('\'');
                    offset++;
                    continue;
                }
                return value.ToString();
            }
            if (character == '\\')
            {
                if (offset >= text.Length)
                {
                    throw new InvalidDataException("Truncated escape.");
                }
                var escaped = text[offset++];
                value.Append(escaped switch
                {
                    '0' => "\0", 'b' => "\b", 'n' => "\n", 'r' => "\r", 't' => "\t", 'Z' => "\u001a",
                    '%' => "\\%", '_' => "\\_", _ => escaped.ToString()
                });
            }
            else
            {
                value.Append(character);
            }
        }
        throw new InvalidDataException("Unterminated SQL string.");
    }

    private static void SkipSpace(string text, ref int offset)
    {
        while (offset < text.Length && char.IsWhiteSpace(text[offset]))
        {
            offset++;
        }
    }

    private static void Expect(string text, ref int offset, char expected)
    {
        SkipSpace(text, ref offset);
        if (offset >= text.Length || text[offset++] != expected)
        {
            throw new InvalidDataException("Unexpected SQL delimiter.");
        }
    }
}
