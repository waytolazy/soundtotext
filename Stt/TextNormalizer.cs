using System;

namespace SoundToText.Stt;

public sealed class TextNormalizer
{
    private bool _atSentenceStart = true;

    public void Reset() => _atSentenceStart = true;

    public string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var s = raw.Trim();

        if (_atSentenceStart && char.IsLower(s[0]))
            s = char.ToUpperInvariant(s[0]) + s[1..];

        char last = s[^1];
        bool hasTerminal = last is '.' or '!' or '?' or ',' or ';' or ':';
        if (!hasTerminal) { s += '.'; last = '.'; }

        _atSentenceStart = last is '.' or '!' or '?';
        return s;
    }
}
