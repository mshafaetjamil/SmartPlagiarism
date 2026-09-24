namespace SmartPlagiarism.Engine.Text;

/// <summary>
/// One word, in its normalised form, together with where it came from in the
/// original text.
///
/// The offsets are what make highlighting possible: a match found by comparing
/// normalised words can be traced back to exact character positions in the text
/// the student actually wrote.
/// </summary>
/// <param name="Value">Normalised word - lowercased, punctuation removed.</param>
/// <param name="Start">Index of the word's first character in the original text.</param>
/// <param name="Length">Length of the word in the original text.</param>
public readonly record struct Token(string Value, int Start, int Length)
{
    /// <summary>Exclusive end offset in the original text.</summary>
    public int End => Start + Length;
}
