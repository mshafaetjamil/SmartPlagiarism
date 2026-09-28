namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// The document would cost more to read than <see cref="ExtractionLimits"/>
/// allows. The message is written for the student and the teacher.
/// </summary>
public sealed class DocumentTooLargeException(string message) : Exception(message);
