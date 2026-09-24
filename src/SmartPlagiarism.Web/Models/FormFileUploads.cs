using SmartPlagiarism.Core.Files;

namespace SmartPlagiarism.Web.Models;

/// <summary>
/// Maps uploaded form files onto Core's transport type, keeping their streams
/// open only as long as this object lives. Shared by the web form and the REST
/// API, so both hand the submission service exactly the same thing.
/// </summary>
public sealed class FormFileUploads : IAsyncDisposable
{
    private readonly List<Stream> _streams;

    private FormFileUploads(List<Stream> streams, IReadOnlyList<FileUpload> uploads)
    {
        _streams = streams;
        Uploads = uploads;
    }

    public IReadOnlyList<FileUpload> Uploads { get; }

    public static FormFileUploads Open(IEnumerable<IFormFile> files)
    {
        var streams = new List<Stream>();
        var uploads = new List<FileUpload>();

        foreach (var file in files)
        {
            var stream = file.OpenReadStream();
            streams.Add(stream);
            uploads.Add(new FileUpload(file.FileName, file.Length, stream));
        }

        return new FormFileUploads(streams, uploads);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var stream in _streams)
        {
            await stream.DisposeAsync();
        }
    }
}
