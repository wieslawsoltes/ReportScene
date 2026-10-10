using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ReportScene.Model;

namespace ReportScene.Expressions;

/// <summary>Compiled immutable ASTs are shared; evaluation has no reflection, eval, I/O or ambient state.</summary>
public sealed class ExpressionEngine
{
    private readonly ConcurrentDictionary<string, Node> cache = new(StringComparer.Ordinal);
    public Value Evaluate(string source, EvaluationContext context)
    {
        if (source.Length > 8192) throw new ExpressionException("Expression exceeds 8192 characters.");
        if (cache.Count >= 512) cache.Clear();
        try { return cache.GetOrAdd(source, s => new Parser(s).Parse()).Eval(context); }
        catch (ExpressionException) { throw; }
        catch (Exception e) when (e is FormatException or OverflowException or DivideByZeroException or ArgumentException)
        { throw new ExpressionException(e.Message); }
    }
    public string Interpolate(string text, EvaluationContext context, string? format = null, CultureInfo? culture = null)
    {
        if (text.Length > 1_000_000) throw new ExpressionException("Text exceeds the one-million-character limit.");
        var result = new StringBuilder();
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '[' or ']')
            { result.Append(text[i + 1]); i += 2; continue; }
            if (text[i] != '[') { result.Append(text[i++]); continue; }
            int start = ++i, depth = 1; char quote = '\0';
            for (; i < text.Length && depth > 0; i++)
            {
                char ch = text[i];
                if (quote != '\0') { if (ch == '\\') i++; else if (ch == quote) quote = '\0'; }
                else if (ch is '\'' or '"') quote = ch;
                else if (ch == '[') depth++;
                else if (ch == ']') depth--;
            }
            if (depth != 0) throw new ExpressionException("Unclosed text expression.");
            result.Append(Evaluate(text[start..(i - 1)], context).AsString(format, culture));
            if (result.Length > 1_000_000) throw new ExpressionException("Expanded text exceeds the output limit.");
        }
        return result.ToString();
    }
    private static bool IsAggregate(string name) => name.ToLowerInvariant() is "sum" or "avg" or "count" or "min" or "max";
    private static IEnumerable<string> Fields(Node node)
    {
        switch (node)
        {
            case Field field: yield return field.Name; break;
            case Unary unary: foreach (var name in Fields(unary.Right)) yield return name; break;
            case Binary binary:
                foreach (var name in Fields(binary.Left)) yield return name;
                foreach (var name in Fields(binary.Right)) yield return name;
                break;
            case Conditional conditional:
                foreach (var name in Fields(conditional.Condition)) yield return name;
                foreach (var name in Fields(conditional.True)) yield return name;
                foreach (var name in Fields(conditional.False)) yield return name;
                break;
            case Call call:
                if (IsAggregate(call.Name)) throw new ExpressionException("Nested aggregates require a separately materialized data column.");
                foreach (var argument in call.Args) foreach (var name in Fields(argument)) yield return name;
                break;
        }
    }
    private abstract record Node { public abstract Value Eval(EvaluationContext c); }
    private sealed record Literal(Value Value) : Node { public override Value Eval(EvaluationContext c) => Value; }
    private sealed record Field(string Name) : Node { public override Value Eval(EvaluationContext c) => c.Resolve(Name); }
    private sealed record Unary(string Op, Node Right) : Node
    {
        public override Value Eval(EvaluationContext c) => Op switch
        { "!" => !Right.Eval(c).AsBoolean(), "-" => -Right.Eval(c).AsNumber(), "+" => Right.Eval(c).AsNumber(), _ => throw new ExpressionException("Unknown unary operator.") };
    }
    private sealed record Binary(string Op, Node Left, Node Right) : Node
    {
        public override Value Eval(EvaluationContext c)
        {
            var a = Left.Eval(c);
            if (Op == "&&") return a.AsBoolean() && Right.Eval(c).AsBoolean();
            if (Op == "||") return a.AsBoolean() || Right.Eval(c).AsBoolean();
            if (Op == "??") return a.IsNull ? Right.Eval(c) : a;
            var b = Right.Eval(c);
            return Op switch
            {
                "+" when a.Kind == ValueKind.String || b.Kind == ValueKind.String => Value.From(a.AsString() + b.AsString()),
                "+" => a.AsNumber() + b.AsNumber(), "-" => a.AsNumber() - b.AsNumber(),
                "*" => a.AsNumber() * b.AsNumber(), "/" => a.AsNumber() / b.AsNumber(), "%" => a.AsNumber() % b.AsNumber(),
                "==" => Value.Compare(a, b) == 0, "!=" => Value.Compare(a, b) != 0,
                "<" => Value.Compare(a, b) < 0, "<=" => Value.Compare(a, b) <= 0,
                ">" => Value.Compare(a, b) > 0, ">=" => Value.Compare(a, b) >= 0,
                _ => throw new ExpressionException($"Unknown operator '{Op}'.")
            };
        }
    }
    private sealed record Conditional(Node Condition, Node True, Node False) : Node
    { public override Value Eval(EvaluationContext c) => Condition.Eval(c).AsBoolean() ? True.Eval(c) : False.Eval(c); }
    private sealed record Call(string Name, Node[] Args) : Node
    {
        public override Value Eval(EvaluationContext c)
        {
            var name = Name.ToLowerInvariant();
            void Arity(int min, int max) { if (Args.Length < min || Args.Length > max) throw new ExpressionException($"{Name} expects {min}..{max} arguments."); }
            if (name is "iif" or "if") { Arity(3, 3); return Args[0].Eval(c).AsBoolean() ? Args[1].Eval(c) : Args[2].Eval(c); }
            if (name == "coalesce") { Arity(1, 32); foreach (var a in Args) { var v = a.Eval(c); if (!v.IsNull) return v; } return Value.Null; }
            if (IsAggregate(name))
            {
                Arity(name == "count" ? 0 : 1, 1);
                var rows = c.Rows; string table = c.TableName;
                if (Args.Length > 0)
                {
                    // Inspect the complete argument AST, not just a direct Field node:
                    // report-level Sum(Items.Quantity * Items.Price) has a Binary root.
                    var sources = Fields(Args[0]).Where(f => f.Contains('.')).Select(f => f[..f.IndexOf('.')])
                        .Where(s => !s.Equals("Parameters", StringComparison.OrdinalIgnoreCase) && !s.Equals("Parent", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    foreach (var source in sources)
                        if (!c.Tables.ContainsKey(source) && !source.Equals(table, StringComparison.OrdinalIgnoreCase)) throw new ExpressionException($"Unknown table '{source}'.");
                    if (sources.Length > 0 && !sources.Contains(table, StringComparer.OrdinalIgnoreCase))
                    {
                        if (sources.Length != 1) throw new ExpressionException("An aggregate outside an active data band must reference one data source; join or materialize cross-table expressions first.");
                        if (!c.Tables.TryGetValue(sources[0], out var data)) throw new ExpressionException($"Unknown table '{sources[0]}'.");
                        rows = data.Rows; table = data.Name;
                    }
                }
                if (name == "count" && Args.Length == 0) return rows.Count;
                Value current = Value.Null; decimal sum = 0; int count = 0;
                foreach (var row in rows)
                {
                    var value = Args[0].Eval(c with { Row = row, TableName = table, Rows = rows });
                    if (value.IsNull) continue;
                    if (name is "sum" or "avg") sum += value.AsNumber();
                    if (current.IsNull || (name == "min" && Value.Compare(value, current) < 0) || (name == "max" && Value.Compare(value, current) > 0)) current = value;
                    count++;
                }
                return name switch { "count" => count, "sum" => sum, "avg" => count == 0 ? Value.Null : Value.From(sum / count), _ => current };
            }
            var values = Args.Select(a => a.Eval(c)).ToArray();
            Value V(int i) => i < values.Length ? values[i] : throw new ExpressionException($"Missing argument for {Name}.");
            string S(int i) => V(i).AsString();
            decimal N(int i) => V(i).AsNumber();
            switch (name)
            {
                case "abs": Arity(1, 1); return Math.Abs(N(0));
                case "round": Arity(1, 2); return Math.Round(N(0), values.Length == 2 ? checked((int)N(1)) : 0, MidpointRounding.ToEven);
                case "floor": Arity(1, 1); return Math.Floor(N(0));
                case "ceiling": Arity(1, 1); return Math.Ceiling(N(0));
                case "len": case "length": Arity(1, 1); return S(0).Length;
                case "upper": case "toupper": Arity(1, 1); return Value.From(S(0).ToUpperInvariant());
                case "lower": case "tolower": Arity(1, 1); return Value.From(S(0).ToLowerInvariant());
                case "trim": Arity(1, 1); return Value.From(S(0).Trim());
                case "left": Arity(2, 2); return Value.From(S(0)[..Math.Clamp(checked((int)N(1)), 0, S(0).Length)]);
                case "right": Arity(2, 2); return Value.From(S(0)[(S(0).Length - Math.Clamp(checked((int)N(1)), 0, S(0).Length))..]);
                case "substring": Arity(2, 3); return Value.From(values.Length == 2 ? S(0)[checked((int)N(1))..] : S(0).Substring(checked((int)N(1)), checked((int)N(2))));
                case "replace": Arity(3, 3); return Value.From(S(0).Replace(S(1), S(2), StringComparison.Ordinal));
                case "contains": Arity(2, 2); return S(0).Contains(S(1), StringComparison.OrdinalIgnoreCase);
                case "startswith": Arity(2, 2); return S(0).StartsWith(S(1), StringComparison.OrdinalIgnoreCase);
                case "endswith": Arity(2, 2); return S(0).EndsWith(S(1), StringComparison.OrdinalIgnoreCase);
                case "isnull": Arity(1, 1); return V(0).IsNull;
                case "tostring": Arity(1, 2); return Value.From(V(0).AsString(values.Length == 2 ? S(1) : null));
                case "tonumber": case "todecimal": Arity(1, 1); return N(0);
                case "formatnumber": Arity(2, 3); return Value.From(N(0).ToString(S(1), values.Length == 3 ? CultureInfo.GetCultureInfo(S(2)) : CultureInfo.InvariantCulture));
                case "formatdate": Arity(2, 2); return Value.From(V(0).AsDate().ToString(S(1), CultureInfo.InvariantCulture));
                case "year": Arity(1, 1); return V(0).AsDate().Year;
                case "month": Arity(1, 1); return V(0).AsDate().Month;
                case "day": Arity(1, 1); return V(0).AsDate().Day;
                case "adddays": Arity(2, 2); return Value.From(V(0).AsDate().AddDays((double)N(1)));
                case "date": Arity(3, 3); return Value.From(new DateTime(checked((int)N(0)), checked((int)N(1)), checked((int)N(2)), 0, 0, 0, DateTimeKind.Utc));
                default: throw new ExpressionException($"Unknown function '{Name}'. Arbitrary C# and VB.NET execution is not enabled.");
            }
        }
    }
    private enum TokenKind { End, Number, String, Name, Operator, Left, Right, Comma, Question, Colon }
    private readonly record struct Token(TokenKind Kind, string Text, int Position);
    private sealed class Parser
    {
        private readonly string source;
        private int position, tokenCount, depth;
        private Token token;
        public Parser(string source) { this.source = source; token = Next(); }
        public Node Parse()
        {
            var node = Expression(0);
            if (token.Kind != TokenKind.End) throw Error("Unexpected trailing token");
            return node;
        }
        private ExpressionException Error(string message) => new($"{message} at character {token.Position + 1}.");
        private void Advance() => token = Next();
        private void Expect(TokenKind kind) { if (token.Kind != kind) throw Error($"Expected {kind}"); Advance(); }
        private Node Expression(int min)
        {
            if (++depth > 96) throw Error("Expression nesting exceeds 96 levels");
            Node left;
            if (token.Kind == TokenKind.Operator && token.Text is "!" or "-" or "+")
            { string op = token.Text; Advance(); left = new Unary(op, Expression(8)); }
            else if (token.Kind == TokenKind.Left) { Advance(); left = Expression(0); Expect(TokenKind.Right); }
            else if (token.Kind == TokenKind.Number)
            { left = new Literal(Value.From(decimal.Parse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture))); Advance(); }
            else if (token.Kind == TokenKind.String) { left = new Literal(Value.From(token.Text)); Advance(); }
            else if (token.Kind == TokenKind.Name)
            {
                string name = token.Text; Advance();
                if (token.Kind == TokenKind.Left)
                {
                    Advance(); var args = new List<Node>();
                    if (token.Kind != TokenKind.Right)
                        while (true) { args.Add(Expression(0)); if (args.Count > 32) throw Error("Too many arguments"); if (token.Kind != TokenKind.Comma) break; Advance(); }
                    Expect(TokenKind.Right); left = new Call(name, args.ToArray());
                }
                else left = name.ToLowerInvariant() switch { "true" => new Literal(true), "false" => new Literal(false), "null" or "nothing" => new Literal(Value.Null), _ => new Field(name) };
            }
            else throw Error("Expected expression");
            while (token.Kind == TokenKind.Operator && Precedence(token.Text) >= min)
            {
                string op = token.Text; int p = Precedence(op); Advance();
                left = new Binary(op, left, Expression(p + 1));
            }
            if (min == 0 && token.Kind == TokenKind.Question)
            { Advance(); var yes = Expression(0); Expect(TokenKind.Colon); left = new Conditional(left, yes, Expression(0)); }
            depth--; return left;
        }
        private static int Precedence(string op) => op switch
        { "??" => 1, "||" => 2, "&&" => 3, "==" or "!=" => 4, "<" or "<=" or ">" or ">=" => 5, "+" or "-" => 6, "*" or "/" or "%" => 7, _ => -1 };
        private Token Next()
        {
            if (++tokenCount > 2048) throw new ExpressionException("Expression exceeds 2048 tokens.");
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
            int start = position;
            if (position == source.Length) return new(TokenKind.End, "", position);
            char ch = source[position++];
            if (ch is '\'' or '"')
            {
                var text = new StringBuilder(); bool closed = false;
                while (position < source.Length)
                {
                    char v = source[position++];
                    if (v == ch)
                    {
                        if (position < source.Length && source[position] == ch) { text.Append(ch); position++; continue; }
                        closed = true; break;
                    }
                    if (v == '\\' && position < source.Length)
                    { char e = source[position++]; text.Append(e switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => e }); }
                    else text.Append(v);
                }
                if (!closed) throw new ExpressionException($"Unclosed string at character {start + 1}.");
                return new(TokenKind.String, text.ToString(), start);
            }
            if (ch == '[')
            {
                int end = source.IndexOf(']', position);
                if (end < 0) throw new ExpressionException("Unclosed field reference.");
                string name = source[position..end].Trim(); position = end + 1;
                if (name.Length == 0 || name.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '.' or '#' or ' '))) throw new ExpressionException("Invalid field reference.");
                return new(TokenKind.Name, name, start);
            }
            if (char.IsDigit(ch) || ch == '.' && position < source.Length && char.IsDigit(source[position]))
            {
                while (position < source.Length && (char.IsDigit(source[position]) || source[position] == '.')) position++;
                if (position < source.Length && source[position] is 'e' or 'E')
                { position++; if (position < source.Length && source[position] is '+' or '-') position++; while (position < source.Length && char.IsDigit(source[position])) position++; }
                return new(TokenKind.Number, source[start..position], start);
            }
            if (char.IsLetter(ch) || ch == '_')
            {
                while (position < source.Length && (char.IsLetterOrDigit(source[position]) || source[position] is '_' or '.' or '#')) position++;
                string word = source[start..position];
                string? op = word.ToLowerInvariant() switch { "and" or "andalso" => "&&", "or" or "orelse" => "||", "not" => "!", "mod" => "%", _ => null };
                return new(op is null ? TokenKind.Name : TokenKind.Operator, op ?? word, start);
            }
            if (position < source.Length)
            {
                string pair = source[start..(position + 1)];
                if (pair is "==" or "!=" or "<=" or ">=" or "<>" or "&&" or "||" or "??")
                { position++; return new(TokenKind.Operator, pair == "<>" ? "!=" : pair, start); }
            }
            return ch switch
            {
                '(' => new(TokenKind.Left, "(", start), ')' => new(TokenKind.Right, ")", start), ',' => new(TokenKind.Comma, ",", start),
                '?' => new(TokenKind.Question, "?", start), ':' => new(TokenKind.Colon, ":", start),
                '+' or '-' or '*' or '/' or '%' or '<' or '>' or '!' => new(TokenKind.Operator, ch.ToString(), start),
                '=' => new(TokenKind.Operator, "==", start),
                _ => throw new ExpressionException($"Invalid character '{ch}' at {start + 1}.")
            };
        }
    }
}
