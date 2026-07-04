using System.Globalization;

namespace RavenAI.Services.Chat.Tools;

/// <summary>
/// A tiny, dependency-free arithmetic evaluator (recursive descent). Supports decimal numbers, the
/// binary operators + - * / % ^ (^ is right-associative), unary +/-, and parentheses. Throws
/// <see cref="FormatException"/> on malformed input. It only does arithmetic — it never executes code.
///
/// Grammar:
///   expr   := term (('+' | '-') term)*
///   term   := factor (('*' | '/' | '%') factor)*
///   factor := unary ('^' factor)?
///   unary  := ('+' | '-') unary | primary
///   primary:= number | '(' expr ')'
/// </summary>
internal static class ArithmeticEvaluator
{
    public static double Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new FormatException("empty expression");

        var parser = new Parser(expression);
        double result = parser.ParseExpression();
        parser.ExpectEnd();
        return result;
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _pos;

        public Parser(string s) => _s = s;

        public double ParseExpression()
        {
            double value = ParseTerm();
            while (true)
            {
                char op = Peek();
                if (op is '+' or '-')
                {
                    _pos++;
                    double rhs = ParseTerm();
                    value = op == '+' ? value + rhs : value - rhs;
                }
                else break;
            }
            return value;
        }

        private double ParseTerm()
        {
            double value = ParseFactor();
            while (true)
            {
                char op = Peek();
                if (op is '*' or '/' or '%')
                {
                    _pos++;
                    double rhs = ParseFactor();
                    value = op switch
                    {
                        '*' => value * rhs,
                        '/' => rhs == 0 ? throw new FormatException("division by zero") : value / rhs,
                        _   => rhs == 0 ? throw new FormatException("modulo by zero") : value % rhs,
                    };
                }
                else break;
            }
            return value;
        }

        private double ParseFactor()
        {
            double baseValue = ParseUnary();
            if (Peek() == '^')
            {
                _pos++;
                double exponent = ParseFactor(); // right-associative
                return Math.Pow(baseValue, exponent);
            }
            return baseValue;
        }

        private double ParseUnary()
        {
            char c = Peek();
            if (c == '-') { _pos++; return -ParseUnary(); }
            if (c == '+') { _pos++; return ParseUnary(); }
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            if (Peek() == '(')
            {
                _pos++;
                double v = ParseExpression();
                if (Peek() != ')')
                    throw new FormatException("missing closing parenthesis");
                _pos++;
                return v;
            }
            return ParseNumber();
        }

        private double ParseNumber()
        {
            SkipWhitespace();
            int start = _pos;
            while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos] == '.'))
                _pos++;

            if (_pos == start)
            {
                char bad = _pos < _s.Length ? _s[_pos] : '\0';
                throw new FormatException(bad == '\0'
                    ? "unexpected end of expression"
                    : $"unexpected character '{bad}' at position {_pos}");
            }

            string num = _s[start.._pos];
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException($"invalid number '{num}'");
            return value;
        }

        private char Peek()
        {
            SkipWhitespace();
            return _pos < _s.Length ? _s[_pos] : '\0';
        }

        private void SkipWhitespace()
        {
            while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos]))
                _pos++;
        }

        public void ExpectEnd()
        {
            if (Peek() != '\0')
                throw new FormatException($"unexpected trailing characters: '{_s[_pos..]}'");
        }
    }
}
