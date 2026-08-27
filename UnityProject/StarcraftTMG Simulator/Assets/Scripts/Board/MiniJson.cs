using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TmgBoard
{
    /// <summary>
    /// 표준 JSON(RFC 8259) 최소 파서 — 외부 패키지(Newtonsoft 등) 의존 없이
    /// 로스터 JSON을 읽기 위해 직접 구현했다(이 머신에서 패키지 매니저 해석을
    /// 검증할 방법이 없어서, 자기완결적인 방식을 택함). Godot의
    /// JSON.parse_string()과 같은 모양의 결과 트리(Dictionary&lt;string,object&gt;
    /// / List&lt;object&gt; / string / double / bool / null)를 돌려주므로, Godot판
    /// 로스터 파싱 로직(Dictionary 순회)을 거의 그대로 옮길 수 있다. 주석/
    /// 트레일링 콤마 등 비표준 확장은 지원하지 않는다.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            var result = ParseValue(json, ref i);
            SkipWhitespace(json, ref i);
            if (i != json.Length)
            {
                throw new FormatException($"JSON 끝에 남는 내용이 있습니다 (위치 {i})");
            }
            return result;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
            {
                throw new FormatException("예상치 못하게 JSON이 끝났습니다");
            }
            switch (s[i])
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
            {
                throw new FormatException($"JSON 리터럴이 올바르지 않습니다 (위치 {i})");
            }
            i += literal.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return dict;
            }
            while (true)
            {
                SkipWhitespace(s, ref i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    throw new FormatException($"':'가 필요합니다 (위치 {i})");
                }
                i++;
                dict[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                {
                    throw new FormatException("객체 안에서 JSON이 예상치 못하게 끝났습니다");
                }
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (s[i] == '}')
                {
                    i++;
                    break;
                }
                throw new FormatException($"','나 '}}'가 필요합니다 (위치 {i})");
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return list;
            }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                {
                    throw new FormatException("배열 안에서 JSON이 예상치 못하게 끝났습니다");
                }
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (s[i] == ']')
                {
                    i++;
                    break;
                }
                throw new FormatException($"','나 ']'가 필요합니다 (위치 {i})");
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"')
            {
                throw new FormatException($"문자열이 필요합니다 (위치 {i})");
            }
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length)
                {
                    throw new FormatException("문자열 안에서 JSON이 예상치 못하게 끝났습니다");
                }
                char c = s[i++];
                if (c == '"')
                {
                    break;
                }
                if (c == '\\')
                {
                    if (i >= s.Length)
                    {
                        throw new FormatException("이스케이프 시퀀스가 불완전합니다");
                    }
                    char esc = s[i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length)
                            {
                                throw new FormatException("유니코드 이스케이프가 불완전합니다");
                            }
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default:
                            throw new FormatException($"알 수 없는 이스케이프 '\\{esc}'");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            {
                i++;
            }
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
            {
                i++;
            }
            if (i == start)
            {
                throw new FormatException($"숫자가 필요합니다 (위치 {i})");
            }
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }
    }
}
