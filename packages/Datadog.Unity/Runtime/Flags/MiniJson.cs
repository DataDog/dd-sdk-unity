// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

// MiniJSON - Minimal JSON parser for Unity (no external dependencies).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Datadog.Unity.Flags
{
    internal static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            return new Parser(json).ParseValue();
        }

        private class Parser
        {
            private readonly string _json;
            private int _pos;

            public Parser(string json)
            {
                _json = json;
                _pos = 0;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (_pos >= _json.Length)
                {
                    return null;
                }

                var c = _json[_pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't':
                    case 'f': return ParseBool();
                    case 'n': return ParseNull();
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ParseNumber();
                        }
                        return null;
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>();
                _pos++; // skip '{'
                SkipWhitespace();

                while (_pos < _json.Length && _json[_pos] != '}')
                {
                    SkipWhitespace();
                    var key = ParseString();
                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ':')
                    {
                        _pos++;
                    }
                    var value = ParseValue();
                    dict[key] = value;
                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ',')
                    {
                        _pos++;
                    }
                }

                if (_pos < _json.Length)
                {
                    _pos++; // skip '}'
                }

                return dict;
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _pos++; // skip '['
                SkipWhitespace();

                while (_pos < _json.Length && _json[_pos] != ']')
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ',')
                    {
                        _pos++;
                    }
                    SkipWhitespace();
                }

                if (_pos < _json.Length)
                {
                    _pos++; // skip ']'
                }

                return list;
            }

            private string ParseString()
            {
                if (_pos >= _json.Length || _json[_pos] != '"')
                {
                    return string.Empty;
                }

                _pos++; // skip '"'
                var sb = new StringBuilder();

                while (_pos < _json.Length)
                {
                    var c = _json[_pos];
                    if (c == '"')
                    {
                        _pos++;
                        return sb.ToString();
                    }

                    if (c == '\\')
                    {
                        _pos++;
                        if (_pos >= _json.Length)
                        {
                            break;
                        }

                        c = _json[_pos];
                        switch (c)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u':
                                if (_pos + 4 < _json.Length)
                                {
                                    var hex = _json.Substring(_pos + 1, 4);
                                    if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                    {
                                        sb.Append((char)code);
                                    }
                                    _pos += 4;
                                }
                                break;
                            default: sb.Append(c); break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    _pos++;
                }

                return sb.ToString();
            }

            private object ParseNumber()
            {
                var start = _pos;
                var isFloat = false;

                if (_pos < _json.Length && _json[_pos] == '-')
                {
                    _pos++;
                }

                while (_pos < _json.Length && _json[_pos] >= '0' && _json[_pos] <= '9')
                {
                    _pos++;
                }

                if (_pos < _json.Length && _json[_pos] == '.')
                {
                    isFloat = true;
                    _pos++;
                    while (_pos < _json.Length && _json[_pos] >= '0' && _json[_pos] <= '9')
                    {
                        _pos++;
                    }
                }

                if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
                {
                    isFloat = true;
                    _pos++;
                    if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-'))
                    {
                        _pos++;
                    }
                    while (_pos < _json.Length && _json[_pos] >= '0' && _json[_pos] <= '9')
                    {
                        _pos++;
                    }
                }

                var numStr = _json.Substring(start, _pos - start);

                if (isFloat)
                {
                    if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    {
                        return d;
                    }
                }
                else
                {
                    if (long.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
                    {
                        // Return int if it fits
                        if (l >= int.MinValue && l <= int.MaxValue)
                        {
                            return (long)l;
                        }
                        return l;
                    }
                }

                return 0;
            }

            private bool ParseBool()
            {
                if (_pos + 4 <= _json.Length && _json.Substring(_pos, 4) == "true")
                {
                    _pos += 4;
                    return true;
                }

                if (_pos + 5 <= _json.Length && _json.Substring(_pos, 5) == "false")
                {
                    _pos += 5;
                    return false;
                }

                return false;
            }

            private object ParseNull()
            {
                if (_pos + 4 <= _json.Length && _json.Substring(_pos, 4) == "null")
                {
                    _pos += 4;
                }

                return null;
            }

            private void SkipWhitespace()
            {
                while (_pos < _json.Length)
                {
                    var c = _json[_pos];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    {
                        break;
                    }
                    _pos++;
                }
            }
        }
    }
}
