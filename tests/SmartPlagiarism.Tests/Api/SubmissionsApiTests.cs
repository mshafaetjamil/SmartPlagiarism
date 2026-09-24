using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SmartPlagiarism.Tests.Extraction;

namespace SmartPlagiarism.Tests.Api;

/// <summary>
/// The REST API end to end over HTTP, against the real app: authentication by
/// API key, the upload-to-report happy path through the background worker, a
/// rejected invalid file, and the rules that keep one integration out of
/// another's submissions.
/// </summary>
public sealed class SubmissionsApiTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    private static readonly TimeSpan s_analysisTimeout = TimeSpan.FromSeconds(30);

    private const string Paragraph =
        "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
        + "which guarantees that any sufficiently long shared passage between two documents will be found.";

    [Fact]
    public async Task Health_IsPublic()
    {
        var response = await factory.ClientWithKey(null).GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("spk_notreal_0000000000000000000000000000000000000000000")]
    [InlineData("not-even-the-right-shape")]
    public async Task WithoutAValidKey_EveryEndpointIs401(string? key)
    {
        await factory.SeedAsync();
        var client = factory.ClientWithKey(key);

        (await client.GetAsync("/api/v1/submissions/1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/submissions/1/report")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var upload = await client.PostAsync("/api/v1/submissions", await FormAsync(DocxFixture.Create([Paragraph]), "report.docx"));
        upload.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a missing key must never become a login-page redirect");
        upload.Headers.WwwAuthenticate.Should().ContainSingle().Which.Scheme.Should().Be("ApiKey");
    }

    [Fact]
    public async Task ARevokedKey_IsRefused()
    {
        var data = await factory.SeedAsync();

        var response = await factory.ClientWithKey(data.RevokedKey).GetAsync("/api/v1/submissions/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HappyPath_UploadIsAccepted_AnalysedInTheBackground_AndItsReportDownloads()
    {
        var data = await factory.SeedAsync();
        var client = factory.ClientWithKey(data.PortalKey);

        var upload = await client.PostAsync("/api/v1/submissions", await FormAsync(DocxFixture.Create([Paragraph]), "report.docx"));

        upload.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await Json(upload);
        var id = created.GetProperty("id").GetInt32();
        upload.Headers.Location!.ToString().Should().EndWith($"/api/v1/submissions/{id}");
        created.GetProperty("analysisStatus").GetString().Should().Be("Queued", "enums travel as names");
        created.GetProperty("links").GetProperty("report").GetString().Should().Be($"/api/v1/submissions/{id}/report");

        var finished = await WaitForAnalysisAsync(client, id);

        finished.GetProperty("analysisStatus").GetString().Should().Be(
            "Completed",
            "the analysis should succeed, but reported: {0}\nApp log:\n{1}",
            finished.GetProperty("analysisError").GetString(),
            factory.Log);
        var result = finished.GetProperty("result");
        result.GetProperty("overallSimilarityPercent").GetDecimal().Should().BeInRange(0m, 100m);
        result.GetProperty("similarityBand").GetString().Should().BeOneOf("Low", "Moderate", "High");

        var report = await client.GetAsync($"/api/v1/submissions/{id}/report");

        report.StatusCode.Should().Be(HttpStatusCode.OK);
        report.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        report.Content.Headers.ContentDisposition!.FileName.Should().Contain($"originality-report-{id}.pdf");
        Encoding.ASCII.GetString((await report.Content.ReadAsByteArrayAsync())[..5]).Should().Be("%PDF-");
    }

    [Fact]
    public async Task AnInvalidFile_IsRejectedWith400_AndNothingIsCreated()
    {
        var data = await factory.SeedAsync();
        var client = factory.ClientWithKey(data.PortalKey);
        var before = await factory.CountSubmissionsAsync();

        // Named .pdf, but not a PDF: the magic-byte check must catch it.
        var response = await client.PostAsync(
            "/api/v1/submissions",
            await FormAsync(Encoding.UTF8.GetBytes("this is not a pdf at all"), "essay.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await Json(response);
        problem.GetProperty("errors").GetProperty("submission")[0].GetString().Should().Contain("essay.pdf");
        (await factory.CountSubmissionsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task AnUnknownStudentNumber_IsA400()
    {
        var data = await factory.SeedAsync();

        var response = await factory.ClientWithKey(data.PortalKey).PostAsync(
            "/api/v1/submissions",
            await FormAsync(DocxFixture.Create([Paragraph]), "report.docx", studentNumber: "NO-SUCH-STUDENT"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").GetProperty("StudentNumber")[0].GetString()
            .Should().Contain("No student has that student number");
    }

    [Fact]
    public async Task AKey_CannotReadAnotherKeysSubmission()
    {
        var data = await factory.SeedAsync();

        var upload = await factory.ClientWithKey(data.PortalKey)
            .PostAsync("/api/v1/submissions", await FormAsync(DocxFixture.Create([Paragraph]), "report.docx"));
        var id = (await Json(upload)).GetProperty("id").GetInt32();

        var otherClient = factory.ClientWithKey(data.OtherKey);

        (await otherClient.GetAsync($"/api/v1/submissions/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherClient.GetAsync($"/api/v1/submissions/{id}/report")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Swagger_IsNotExposedOutsideDevelopment()
    {
        (await factory.ClientWithKey(null).GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.ClientWithKey(null).GetAsync("/swagger/index.html")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The multipart form a portal would post, for this factory's seeded student and course.</summary>
    internal static async Task<MultipartFormDataContent> FormAsync(
        ApiTestFactory factory,
        byte[] file,
        string fileName,
        string? studentNumber = null)
    {
        var data = await factory.SeedAsync();
        var form = new MultipartFormDataContent
        {
            { new StringContent(studentNumber ?? data.StudentNumber), "StudentNumber" },
            { new StringContent("Portal submission"), "Title" },
            { new StringContent("Report"), "Type" },
            { new StringContent(data.CourseId.ToString(CultureInfo.InvariantCulture)), "CourseId" },
            { new StringContent(data.SemesterId.ToString(CultureInfo.InvariantCulture)), "SemesterId" },
        };

        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "Files", fileName);

        return form;
    }

    private Task<MultipartFormDataContent> FormAsync(byte[] file, string fileName, string? studentNumber = null) =>
        FormAsync(factory, file, fileName, studentNumber);

    private static async Task<JsonElement> WaitForAnalysisAsync(HttpClient client, int id)
    {
        var deadline = DateTime.UtcNow + s_analysisTimeout;

        while (true)
        {
            var status = await Json(await client.GetAsync($"/api/v1/submissions/{id}"));
            var analysis = status.GetProperty("analysisStatus").GetString();

            if (analysis is "Completed" or "Failed" || DateTime.UtcNow > deadline)
            {
                return status;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    internal static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}

/// <summary>The upload limit, per key, on its own app instance so no other test uses up the allowance.</summary>
public sealed class ApiRateLimitTests(RateLimitedApiTestFactory factory) : IClassFixture<RateLimitedApiTestFactory>
{
    [Fact]
    public async Task UploadsBeyondTheLimit_Get429_WithRetryAfter()
    {
        var data = await factory.SeedAsync();
        var client = factory.ClientWithKey(data.PortalKey);
        var invalid = Encoding.UTF8.GetBytes("not a pdf");

        for (var attempt = 0; attempt < RateLimitedApiTestFactory.Limit; attempt++)
        {
            var allowed = await client.PostAsync("/api/v1/submissions", await SubmissionsApiTests.FormAsync(factory, invalid, "x.pdf"));
            allowed.StatusCode.Should().Be(HttpStatusCode.BadRequest, "within the limit the request reaches validation");
        }

        var limited = await client.PostAsync("/api/v1/submissions", await SubmissionsApiTests.FormAsync(factory, invalid, "x.pdf"));

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();

        // The limit is per key: another integration is unaffected.
        var other = await factory.ClientWithKey(data.OtherKey)
            .PostAsync("/api/v1/submissions", await SubmissionsApiTests.FormAsync(factory, invalid, "x.pdf"));
        other.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
